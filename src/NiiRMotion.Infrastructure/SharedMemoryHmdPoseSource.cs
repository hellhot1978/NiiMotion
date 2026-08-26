using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Numerics;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class SharedMemoryHmdPoseSource : IHmdPoseSource
{
    public const string MappingName = "NiiMotion.HmdPose.v1";
    private const uint Magic = 0x31444D48;
    private readonly BoundedSensorBuffer<HmdPoseSample> _buffer = new(64);
    private readonly string _mappingName;
    private CancellationTokenSource? _lifetime;
    private Task? _worker;
    public string SourceId => "steamvr-hmd";
    public SensorMode Mode => SensorMode.Live;
    public System.Threading.Channels.ChannelReader<HmdPoseSample> Samples => _buffer.Reader;

    public SharedMemoryHmdPoseSource(string? mappingName = null) => _mappingName = mappingName ?? MappingName;

    public static bool TryGetFreshTracking(out bool tracked, string? mappingName = null)
    {
        tracked = false;
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mappingName ?? MappingName, MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 64, MemoryMappedFileAccess.Read);
            if (!TryRead(view, out _, out var qpc, out tracked, out _, out _)) return false;
            var age = Stopwatch.GetTimestamp() - qpc;
            return age >= 0 && age <= Stopwatch.Frequency;
        }
        catch (FileNotFoundException) { return false; }
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_lifetime is not null) return Task.CompletedTask;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _worker = RunAsync(_lifetime.Token); return Task.CompletedTask;
    }

    private async Task RunAsync(CancellationToken token)
    {
        if (!OperatingSystem.IsWindows()) { _buffer.Complete(); return; }
        long previousSequence = -1; float previousYaw = 0; long previousTicks = 0;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(20));
            while (await timer.WaitForNextTickAsync(token))
            {
                try
                {
                    using var map = MemoryMappedFile.OpenExisting(_mappingName, MemoryMappedFileRights.Read);
                    using var view = map.CreateViewAccessor(0, 64, MemoryMappedFileAccess.Read);
                    if (!TryRead(view, out var sequence, out var qpc, out var tracked, out var position, out var orientation) || sequence == previousSequence) continue;
                    var yaw = Yaw(orientation); var dt = previousTicks == 0 ? 0 : (qpc - previousTicks) / (float)Stopwatch.Frequency;
                    var rate = dt <= 0 || dt > .5f ? 0 : AngleDelta(previousYaw, yaw) / dt;
                    previousSequence = sequence; previousTicks = qpc; previousYaw = yaw;
                    _buffer.TryWrite(new(SourceId, sequence, new(qpc, DateTimeOffset.UtcNow), tracked, position, orientation, yaw, rate));
                }
                catch (FileNotFoundException) { }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { _buffer.Complete(); }
    }

    internal static bool TryRead(MemoryMappedViewAccessor view, out long sequence, out long qpc, out bool tracked, out Vector3 position, out Quaternion orientation)
    {
        sequence = qpc = 0; tracked = false; position = default; orientation = Quaternion.Identity;
        if (view.ReadUInt32(0) != Magic || view.ReadUInt32(4) != 1) return false;
        sequence = view.ReadInt64(8); qpc = view.ReadInt64(16); tracked = view.ReadUInt32(24) != 0;
        position = new(view.ReadSingle(28), view.ReadSingle(32), view.ReadSingle(36)); orientation = Quaternion.Normalize(new(view.ReadSingle(40), view.ReadSingle(44), view.ReadSingle(48), view.ReadSingle(52)));
        return sequence > 0 && qpc > 0 && float.IsFinite(position.X) && float.IsFinite(orientation.W);
    }

    private static float Yaw(Quaternion q) => MathF.Atan2(2 * ((q.W * q.Y) + (q.X * q.Z)), 1 - (2 * ((q.Y * q.Y) + (q.Z * q.Z))));
    private static float AngleDelta(float from, float to) { var delta = to - from; while (delta > MathF.PI) delta -= 2 * MathF.PI; while (delta < -MathF.PI) delta += 2 * MathF.PI; return delta; }

    public async ValueTask DisposeAsync() { var lifetime = _lifetime; _lifetime = null; if (lifetime is not null) { lifetime.Cancel(); if (_worker is not null) try { await _worker; } catch { } lifetime.Dispose(); } }
}
