using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Threading.Channels;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class OwoTrackSensorSource : ISensorSource<PhoneImuSample>
{
    private readonly int _port; private readonly BoundedSensorBuffer<PhoneImuSample> _buffer = new(512); private readonly SensorTimingDiagnostics _timing = new(); private readonly SequenceDiagnostics _sequence = new();
    private UdpClient? _client; private CancellationTokenSource? _lifetime; private Task? _loop; private IPEndPoint? _phone; private Vector3 _accel; private Vector3 _gyro;
    public OwoTrackSensorSource(int port = PhoneSensorSource.DefaultPort) => _port = port;
    public string SourceId => "phone:owotrack"; public SensorMode Mode => SensorMode.Live; public ChannelReader<PhoneImuSample> Samples => _buffer.Reader;
    public SensorTimingSnapshot Timing => _timing.Snapshot(Stopwatch.GetTimestamp()); public long MissingPackets => _sequence.Missing; public long OutOfOrderPackets => _sequence.OutOfOrder; public IPEndPoint? PhoneEndpoint => _phone;
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, _port)); _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); _loop = LoopAsync(_lifetime.Token); return Task.CompletedTask;
    }
    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var datagram = await _client!.ReceiveAsync(cancellationToken); if (_phone is not null && !datagram.RemoteEndPoint.Equals(_phone)) continue;
                if (!OwoTrackPacketParser.TryParse(datagram.Buffer, out var packet)) continue;
                if (packet.Type == OwoTrackPacketType.Handshake) { _phone = datagram.RemoteEndPoint; var hello = new byte[13]; hello[0] = 3; Encoding.ASCII.GetBytes("Hey OVR =D 5").CopyTo(hello, 1); await _client.SendAsync(hello, _phone, cancellationToken); continue; }
                if (packet.Type == OwoTrackPacketType.PingPong) { await _client.SendAsync(datagram.Buffer, datagram.RemoteEndPoint, cancellationToken); continue; }
                _sequence.Observe(packet.Sequence);
                if (packet.Type == OwoTrackPacketType.Acceleration) { _accel = packet.Vector * 9.80665f; continue; }
                if (packet.Type == OwoTrackPacketType.Gyroscope) { _gyro = packet.Vector; continue; }
                if (packet.Type != OwoTrackPacketType.Rotation) continue;
                PhonePresence.Mark(datagram.RemoteEndPoint.ToString());
                var now = Stopwatch.GetTimestamp(); _timing.Observe(now); _buffer.TryWrite(new(SourceId, packet.Sequence, new(now, DateTimeOffset.UtcNow), 0, Quaternion.Normalize(packet.Rotation), _accel, _gyro));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _buffer.Complete(ex); return; }
        _buffer.Complete();
    }
    public async ValueTask DisposeAsync() { _lifetime?.Cancel(); _client?.Dispose(); if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { } _lifetime?.Dispose(); }
}
