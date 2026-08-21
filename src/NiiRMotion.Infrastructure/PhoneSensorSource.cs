using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading.Channels;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class PhoneSensorSource : ISensorSource<PhoneImuSample>
{
    public const int DefaultPort = 6969;
    private readonly int _port; private readonly string _sessionToken; private readonly BoundedSensorBuffer<PhoneImuSample> _buffer = new(512);
    private readonly SensorTimingDiagnostics _timing = new(); private readonly SequenceDiagnostics _sequence = new();
    private UdpClient? _client; private CancellationTokenSource? _lifetime; private Task? _loop;
    public PhoneSensorSource(string sessionToken, int port = DefaultPort) { if (sessionToken.Length < 12) throw new ArgumentException("Session token must contain at least 12 characters."); _sessionToken = sessionToken; _port = port; }
    public string SourceId => "phone"; public SensorMode Mode => SensorMode.Live; public ChannelReader<PhoneImuSample> Samples => _buffer.Reader;
    public SensorTimingSnapshot Timing => _timing.Snapshot(Stopwatch.GetTimestamp()); public long MissingPackets => _sequence.Missing; public long OutOfOrderPackets => _sequence.OutOfOrder;
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_client is not null) throw new InvalidOperationException("Phone source already started.");
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, _port)); _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _loop = ReceiveLoopAsync(_lifetime.Token); return Task.CompletedTask;
    }
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var packet = await _client!.ReceiveAsync(cancellationToken); var receivedTicks = Stopwatch.GetTimestamp();
                PhonePacket? dto;
                try { dto = JsonSerializer.Deserialize<PhonePacket>(packet.Buffer); } catch (JsonException) { continue; }
                if (dto is null || !CryptographicEquals(dto.SessionToken, _sessionToken)) continue;
                try { var sample = dto.ToSample(receivedTicks, DateTimeOffset.UtcNow); _sequence.Observe(sample.Sequence); _timing.Observe(receivedTicks); _buffer.TryWrite(sample); } catch (InvalidDataException) { }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _buffer.Complete(ex); return; }
        _buffer.Complete();
    }
    private static bool CryptographicEquals(string left, string right)
    {
        var a = System.Text.Encoding.UTF8.GetBytes(left); var b = System.Text.Encoding.UTF8.GetBytes(right); return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
    }
    public async ValueTask DisposeAsync() { _lifetime?.Cancel(); _client?.Dispose(); if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { } _lifetime?.Dispose(); }
}
