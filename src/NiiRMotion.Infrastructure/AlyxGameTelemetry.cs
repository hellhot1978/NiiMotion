using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public enum GameTelemetryMode { Direct, Guided }
public sealed record GameTelemetryCapability(GameTelemetryMode Mode, string Title, string Description);

public interface IGameTelemetrySession : IAsyncDisposable
{
    event EventHandler<string>? StatusChanged;
    void Start();
}

public interface IGameTelemetryProvider
{
    GameTelemetryCapability Capability { get; }
    string LaunchArguments { get; }
    IGameTelemetrySession? CreateSession(LiveLocomotionService locomotion, string motionProfileId);
}

public static class GameTelemetryProviderFactory
{
    public static IGameTelemetryProvider Create(string gameId, string? steamAppId) =>
        steamAppId == "546560" || gameId.Equals("half-life-alyx", StringComparison.OrdinalIgnoreCase)
            ? new AlyxTelemetryProvider()
            : new GuidedTelemetryProvider();

    private sealed class AlyxTelemetryProvider : IGameTelemetryProvider
    {
        public GameTelemetryCapability Capability => new(GameTelemetryMode.Direct, "OTOMATİK OYUN TELEMETRİSİ", "Avatar mesafesi doğrudan okunur; temiz yürüyüş bölümleri kendiliğinden eşlenir.");
        public string LaunchArguments => $" -netconport {AlyxNetConsoleClient.DefaultPort}";
        public IGameTelemetrySession CreateSession(LiveLocomotionService locomotion, string motionProfileId) => new AlyxStrideTelemetryCoordinator(locomotion, motionProfileId);
    }

    private sealed class GuidedTelemetryProvider : IGameTelemetryProvider
    {
        public GameTelemetryCapability Capability => new(GameTelemetryMode.Guided, "EVRENSEL OYUN EŞLEME", "Bu oyun konum verisi sunmuyor. Oyunda kısa yürüyüşten sonra yalnızca yavaş, doğru veya hızlı seçmen yeterli.");
        public string LaunchArguments => "";
        public IGameTelemetrySession? CreateSession(LiveLocomotionService locomotion, string motionProfileId) => null;
    }
}

public sealed record AlyxPlayerPose(double X, double Y, double Z, double Pitch, double Yaw, double Roll, DateTimeOffset CapturedAt)
{
    public double HorizontalDistanceTo(AlyxPlayerPose other)
    {
        const double sourceUnitsPerMeter = 39.37007874;
        var dx = X - other.X; var dy = Y - other.Y;
        return Math.Sqrt((dx * dx) + (dy * dy)) / sourceUnitsPerMeter;
    }
}

public static partial class AlyxPositionParser
{
    [GeneratedRegex(@"setpos_exact\s+([-+]?\d+(?:\.\d+)?)\s+([-+]?\d+(?:\.\d+)?)\s+([-+]?\d+(?:\.\d+)?)\s*;?\s*setang_exact\s+([-+]?\d+(?:\.\d+)?)\s+([-+]?\d+(?:\.\d+)?)\s+([-+]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PositionPattern();

    public static bool TryParse(string text, out AlyxPlayerPose pose)
    {
        var match = PositionPattern().Match(text ?? "");
        if (match.Success && Enumerable.Range(1, 6).Select(i => double.TryParse(match.Groups[i].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)).All(x => x))
        {
            var values = Enumerable.Range(1, 6).Select(i => double.Parse(match.Groups[i].Value, CultureInfo.InvariantCulture)).ToArray();
            pose = new(values[0], values[1], values[2], values[3], values[4], values[5], DateTimeOffset.UtcNow); return true;
        }
        pose = default!; return false;
    }
}

public sealed class AlyxNetConsoleClient : IAsyncDisposable
{
    public const int DefaultPort = 29091;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public async Task<AlyxPlayerPose?> QueryPoseAsync(int port = DefaultPort, CancellationToken cancellationToken = default)
    {
        await EnsureConnectedAsync(port, cancellationToken);
        try
        {
            while (_stream!.DataAvailable) { var discard = new byte[2048]; await _stream.ReadAtLeastAsync(discard, 1, false, cancellationToken); }
            await _stream.WriteAsync(Encoding.UTF8.GetBytes("getpos_exact\n"), cancellationToken);
            await _stream.FlushAsync(cancellationToken);
            var result = new StringBuilder(); var buffer = new byte[2048];
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromMilliseconds(700));
            do
            {
                var read = await _stream.ReadAsync(buffer, timeout.Token); if (read == 0) throw new IOException("Alyx telemetri bağlantısı kapandı.");
                result.Append(Encoding.UTF8.GetString(buffer, 0, read));
                if (AlyxPositionParser.TryParse(result.ToString(), out var pose)) return pose;
            } while (!timeout.IsCancellationRequested && result.Length < 16384);
            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch { await DisposeAsync(); throw; }
    }

    private async Task EnsureConnectedAsync(int port, CancellationToken cancellationToken)
    {
        if (_client?.Connected == true && _stream is not null) return;
        await DisposeAsync(); _client = new TcpClient();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(1));
        await _client.ConnectAsync("127.0.0.1", port, timeout.Token); _stream = _client.GetStream();
    }

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose(); _client?.Dispose(); _stream = null; _client = null; return ValueTask.CompletedTask;
    }
}

public sealed class AlyxStrideTelemetryCoordinator : IGameTelemetrySession
{
    private readonly LiveLocomotionService _locomotion;
    private readonly string _motionProfileId;
    private readonly GameSensorOptimizationStore _store;
    private readonly AlyxNetConsoleClient _client = new();
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    private LocomotionTelemetrySample? _motion;
    public event EventHandler<string>? StatusChanged;

    public AlyxStrideTelemetryCoordinator(LiveLocomotionService locomotion, string motionProfileId, GameSensorOptimizationStore? store = null)
    { _locomotion = locomotion; _motionProfileId = motionProfileId; _store = store ?? new(); }

    public void Start()
    {
        if (_lifetime is not null) return;
        _lifetime = new(); _locomotion.TelemetryUpdated += OnTelemetryUpdated; _worker = RunAsync(_lifetime.Token);
    }

    private void OnTelemetryUpdated(object? sender, LocomotionTelemetrySample sample) => _motion = sample;

    private async Task RunAsync(CancellationToken token)
    {
        AlyxPlayerPose? first = null, previous = null; long firstSteps = 0; var distance = 0d; var began = Stopwatch.GetTimestamp(); var hadTurn = false;
        try
        {
            StatusChanged?.Invoke(this, "Alyx oyun mesafesi bekleniyor…");
            while (!token.IsCancellationRequested)
            {
                AlyxPlayerPose? pose;
                try { pose = await _client.QueryPoseAsync(cancellationToken: token); }
                catch (SocketException) { await Task.Delay(750, token); continue; }
                var motion = _motion;
                if (pose is null || motion is null) { await Task.Delay(250, token); continue; }
                var motionIsFresh = Stopwatch.GetTimestamp() - motion.MonotonicTicks < Stopwatch.Frequency;
                if (!motionIsFresh || !motion.IsMoving || motion.TargetSpeed < .08)
                {
                    if (first is not null && motion.StepCount - firstSteps >= 6) Apply(firstSteps, motion.StepCount, distance, began, hadTurn);
                    first = previous = null; distance = 0; hadTurn = false; await Task.Delay(200, token); continue;
                }
                if (first is null) { first = previous = pose; firstSteps = motion.StepCount; began = Stopwatch.GetTimestamp(); }
                else
                {
                    var segment = pose.HorizontalDistanceTo(previous!);
                    if (segment > 2.5) { first = previous = null; distance = 0; hadTurn = false; StatusChanged?.Invoke(this, "Işınlanma ölçümü dışlandı."); continue; }
                    distance += segment; previous = pose;
                    hadTurn |= Math.Abs(motion.TurnTarget) > .08 || AngleDifference(first.Yaw, pose.Yaw) > 45;
                    if (motion.StepCount - firstSteps >= 12)
                    {
                        Apply(firstSteps, motion.StepCount, distance, began, hadTurn); first = previous = null; distance = 0; hadTurn = false;
                    }
                }
                await Task.Delay(200, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private void Apply(long firstSteps, long finalSteps, double distance, long began, bool hadTurn)
    {
        var steps = checked((int)Math.Clamp(finalSteps - firstSteps, 0, int.MaxValue));
        var seconds = (Stopwatch.GetTimestamp() - began) / (double)Stopwatch.Frequency;
        var teleport = distance > Math.Max(steps * 2.2, 3);
        var result = _store.ApplyTelemetry("half-life-alyx", _motionProfileId, new(steps, distance, .75, seconds, teleport, hadTurn));
        StatusChanged?.Invoke(this, result.Source.StartsWith("Reddedildi:") ? result.Source : $"Adım eşleme güncellendi · {result.DistanceScale:0.00}× · sonraki oturumda uygulanır");
    }

    private static double AngleDifference(double a, double b) { var d = Math.Abs(a - b) % 360; return d > 180 ? 360 - d : d; }

    public async ValueTask DisposeAsync()
    {
        var lifetime = _lifetime; _lifetime = null; if (lifetime is not null) { lifetime.Cancel(); if (_worker is not null) try { await _worker; } catch { } lifetime.Dispose(); }
        _locomotion.TelemetryUpdated -= OnTelemetryUpdated; await _client.DisposeAsync();
    }
}
