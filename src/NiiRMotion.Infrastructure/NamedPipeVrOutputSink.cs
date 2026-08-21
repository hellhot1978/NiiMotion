using System.Buffers.Binary;
using System.IO.Pipes;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class NamedPipeVrOutputSink(string pipeName = "NiiRMotion.VrOutput.v1") : IAnalogLocomotionSink
{
    private NamedPipeClientStream? _pipe;
    public bool IsAttached => _pipe?.IsConnected == true;

    public async ValueTask AttachAsync(CancellationToken cancellationToken = default)
    {
        if (IsAttached) return;
        _pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        try { await Task.Run(() => _pipe.Connect(1500), cancellationToken); }
        catch { await _pipe.DisposeAsync(); _pipe = null; throw; }
    }

    public async ValueTask WriteAsync(LocomotionVector value, CancellationToken cancellationToken = default)
    {
        if (!IsAttached) throw new InvalidOperationException("SteamVR output helper is not attached.");
        var clamped = value.Clamped(); Span<byte> payload = stackalloc byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, 0x31524D4E); // NMR1
        BinaryPrimitives.WriteInt32LittleEndian(payload[4..], BitConverter.SingleToInt32Bits(clamped.X));
        BinaryPrimitives.WriteInt32LittleEndian(payload[8..], BitConverter.SingleToInt32Bits(clamped.Y));
        await _pipe!.WriteAsync(payload.ToArray(), cancellationToken); await _pipe.FlushAsync(cancellationToken);
    }

    public ValueTask DetachAsync(CancellationToken cancellationToken = default)
    {
        _pipe?.Dispose(); _pipe = null; return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() { if (_pipe is not null) await _pipe.DisposeAsync(); _pipe = null; }
}
