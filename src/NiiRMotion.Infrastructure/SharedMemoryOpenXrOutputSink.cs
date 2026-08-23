using System.IO.MemoryMappedFiles;
using System.Runtime.Versioning;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

[SupportedOSPlatform("windows")]
public sealed class SharedMemoryOpenXrOutputSink : IAnalogLocomotionSink
{
    public const string MappingName = "NiiMotion.OpenXR.v1";
    private MemoryMappedFile? _mapping;
    private MemoryMappedViewAccessor? _view;
    private ulong _sequence;
    public bool IsAttached => _view is not null;

    public ValueTask AttachAsync(CancellationToken cancellationToken = default)
    {
        if (IsAttached) return ValueTask.CompletedTask;
        _mapping = MemoryMappedFile.CreateOrOpen(MappingName, 64, MemoryMappedFileAccess.ReadWrite);
        _view = _mapping.CreateViewAccessor(0, 64, MemoryMappedFileAccess.ReadWrite);
        Write(LocomotionVector.Zero, enabled: true);
        return ValueTask.CompletedTask;
    }

    public ValueTask WriteAsync(LocomotionVector value, CancellationToken cancellationToken = default)
    {
        if (_view is null) throw new InvalidOperationException("OpenXR hareket köprüsü bağlı değil.");
        Write(value.Clamped(), enabled: true); return ValueTask.CompletedTask;
    }

    public ValueTask DetachAsync(CancellationToken cancellationToken = default)
    {
        if (_view is not null) Write(LocomotionVector.Zero, enabled: false);
        _view?.Dispose(); _mapping?.Dispose(); _view = null; _mapping = null; return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => DetachAsync();

    private void Write(LocomotionVector value, bool enabled)
    {
        var view = _view!; var odd = ++_sequence | 1UL; view.Write(8, odd);
        view.Write(0, 0x3158524Eu); view.Write(4, 1u); view.Write(16, value.X); view.Write(20, value.Y); view.Write(24, enabled ? 1u : 0u);
        view.Write(28, Fnv1a("Impact-Win64-Shipping.exe")); view.Write(32, Fnv1a("Impact.exe")); view.Write(36, 0u); view.Write(40, (ulong)Environment.TickCount64);
        view.Write(8, ++_sequence & ~1UL); view.Flush();
    }

    public static uint Fnv1a(string value)
    {
        var hash = 2166136261u;
        foreach (var c in value.ToLowerInvariant()) { hash ^= (byte)c; hash *= 16777619u; }
        return hash;
    }
}

public sealed class MultiplexedVrOutputSink(params IAnalogLocomotionSink[] sinks) : IAnalogLocomotionSink
{
    public bool IsAttached => sinks.All(x => x.IsAttached);
    public async ValueTask AttachAsync(CancellationToken cancellationToken = default)
    {
        try { foreach (var sink in sinks) await sink.AttachAsync(cancellationToken); }
        catch { foreach (var sink in sinks.Reverse()) try { await sink.DetachAsync(cancellationToken); } catch { } throw; }
    }
    public async ValueTask WriteAsync(LocomotionVector value, CancellationToken cancellationToken = default) { foreach (var sink in sinks) await sink.WriteAsync(value, cancellationToken); }
    public async ValueTask DetachAsync(CancellationToken cancellationToken = default) { foreach (var sink in sinks.Reverse()) try { await sink.DetachAsync(cancellationToken); } catch { } }
    public async ValueTask DisposeAsync() { foreach (var sink in sinks.Reverse()) try { await sink.DisposeAsync(); } catch { } }
}

public static class VrOutputSinkFactory
{
    public static IAnalogLocomotionSink CreateActive()
    {
        var game = new GameSelectionStore().Load();
        if (!OperatingSystem.IsWindows()) return new NamedPipeVrOutputSink();
        return game == "metro-awakening"
            ? new MultiplexedVrOutputSink(new NamedPipeVrOutputSink(), new SharedMemoryOpenXrOutputSink())
            : new NamedPipeVrOutputSink();
    }
}
