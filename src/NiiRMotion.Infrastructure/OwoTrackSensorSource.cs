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
    private const int DiscoveryPort = 35903;
    private static readonly byte[] HandshakeReply = [3, .. "Hey OVR =D 5"u8];
    private static readonly TimeSpan PhoneStalenessTimeout = TimeSpan.FromSeconds(4);

    private readonly int _port;
    private readonly BoundedSensorBuffer<PhoneImuSample> _buffer = new(512);
    private readonly SensorTimingDiagnostics _timing = new();
    private readonly SequenceDiagnostics _sequence = new();
    private UdpClient? _client;
    private UdpClient? _discovery;
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private Task? _broadcast;
    private Task? _discoveryLoop;
    private volatile IPEndPoint? _phone;
    private Vector3 _accel;
    private Vector3 _gyro;

    public OwoTrackSensorSource(int port = PhoneSensorSource.DefaultPort) => _port = port;
    public string SourceId => "phone:owotrack";
    public SensorMode Mode => SensorMode.Live;
    public ChannelReader<PhoneImuSample> Samples => _buffer.Reader;
    public SensorTimingSnapshot Timing => _timing.Snapshot(Stopwatch.GetTimestamp());
    public long MissingPackets => _sequence.Missing;
    public long OutOfOrderPackets => _sequence.OutOfOrder;
    public IPEndPoint? PhoneEndpoint => _phone;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _client = new UdpClient(new IPEndPoint(IPAddress.Any, _port));
        _client.EnableBroadcast = true;
        _discovery = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
        _discovery.EnableBroadcast = true;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _lifetime.Token;
        _loop = LoopAsync(token);
        _broadcast = BroadcastAsync(token);
        _discoveryLoop = DiscoveryLoopAsync(token);
        return Task.CompletedTask;
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        var endpoint = new IPEndPoint(IPAddress.Broadcast, _port);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_phone is not null)
                {
                    await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
                    if (PhonePresence.TryGetFresh(out _, PhoneStalenessTimeout)) continue;
                    _phone = null;
                    PhonePresence.Reset();
                }
                try { await _client!.SendAsync(HandshakeReply, endpoint, cancellationToken).ConfigureAwait(false); }
                catch (SocketException) { }
                catch (ObjectDisposedException) { break; }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
                await Task.Delay(2000, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var datagram = await _client!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var sender = datagram.RemoteEndPoint;
                if (_phone is not null && !sender.Equals(_phone)) continue;
                if (!OwoTrackPacketParser.TryParse(datagram.Buffer, out var packet)) continue;
                if (packet.Type == OwoTrackPacketType.Handshake)
                {
                    _phone = sender;
                    try { await _client.SendAsync(HandshakeReply, sender, cancellationToken).ConfigureAwait(false); } catch { }
                    PhonePresence.Mark(sender.ToString());
                    continue;
                }
                if (packet.Type == OwoTrackPacketType.PingPong)
                {
                    PhonePresence.Mark(sender.ToString());
                    try { await _client.SendAsync(datagram.Buffer, sender, cancellationToken).ConfigureAwait(false); } catch { }
                    continue;
                }
                if (packet.Type == OwoTrackPacketType.Heartbeat)
                {
                    PhonePresence.Mark(sender.ToString());
                    continue;
                }
                _sequence.Observe(packet.Sequence);
                if (packet.Type == OwoTrackPacketType.Acceleration) { _accel = packet.Vector * 9.80665f; continue; }
                if (packet.Type == OwoTrackPacketType.Gyroscope) { _gyro = packet.Vector; continue; }
                if (packet.Type != OwoTrackPacketType.Rotation) continue;
                PhonePresence.Mark(sender.ToString());
                var now = Stopwatch.GetTimestamp();
                _timing.Observe(now);
                _buffer.TryWrite(new(SourceId, packet.Sequence, new(now, DateTimeOffset.UtcNow), 0, Quaternion.Normalize(packet.Rotation), _accel, _gyro));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _buffer.Complete(ex); return; }
        _buffer.Complete();
    }

    private async Task DiscoveryLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var datagram = await _discovery!.ReceiveAsync(cancellationToken).ConfigureAwait(false);
                try { await _discovery.SendAsync(HandshakeReply, datagram.RemoteEndPoint, cancellationToken).ConfigureAwait(false); } catch { }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch (Exception) when (!cancellationToken.IsCancellationRequested) { await Task.Delay(1000, cancellationToken).ConfigureAwait(false); }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime?.Cancel();
        _client?.Dispose();
        _discovery?.Dispose();
        if (_loop is not null) try { await _loop; } catch (OperationCanceledException) { }
        if (_broadcast is not null) try { await _broadcast; } catch (OperationCanceledException) { }
        if (_discoveryLoop is not null) try { await _discoveryLoop; } catch (OperationCanceledException) { }
        try { _buffer.Complete(); } catch (InvalidOperationException) { }
        _lifetime?.Dispose();
    }
}
