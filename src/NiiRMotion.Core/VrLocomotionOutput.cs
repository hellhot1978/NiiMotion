namespace NiiRMotion.Core;

public readonly record struct LocomotionVector(float X, float Y)
{
    public static LocomotionVector Zero => new(0, 0);
    public LocomotionVector Clamped() => new(Math.Clamp(X, -1, 1), Math.Clamp(Y, -1, 1));
}

public interface IAnalogLocomotionSink : IAsyncDisposable
{
    bool IsAttached { get; }
    ValueTask AttachAsync(CancellationToken cancellationToken = default);
    ValueTask WriteAsync(LocomotionVector value, CancellationToken cancellationToken = default);
    ValueTask DetachAsync(CancellationToken cancellationToken = default);
}

public sealed class VrOutputController : IAsyncDisposable
{
    private readonly IAnalogLocomotionSink _sink;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _enabled;

    public VrOutputController(IAnalogLocomotionSink sink) => _sink = sink;
    public bool IsEnabled => _enabled && _sink.IsAttached;

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_enabled) return;
            await _sink.AttachAsync(cancellationToken);
            await _sink.WriteAsync(LocomotionVector.Zero, cancellationToken);
            _enabled = true;
        }
        catch
        {
            _enabled = false;
            if (_sink.IsAttached) await SafeDetachAsync();
            throw;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask SetAsync(LocomotionVector value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!IsEnabled) throw new InvalidOperationException("VR locomotion output is OFF.");
            await _sink.WriteAsync(value.Clamped(), cancellationToken);
        }
        catch
        {
            _enabled = false;
            if (_sink.IsAttached) await SafeDetachAsync();
            throw;
        }
        finally { _gate.Release(); }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            _enabled = false;
            if (!_sink.IsAttached) return;
            await _sink.WriteAsync(LocomotionVector.Zero, cancellationToken);
            await _sink.DetachAsync(cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async ValueTask SafeDetachAsync()
    {
        try { await _sink.WriteAsync(LocomotionVector.Zero); } catch { }
        try { await _sink.DetachAsync(); } catch { }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        await _sink.DisposeAsync();
        _gate.Dispose();
    }
}
