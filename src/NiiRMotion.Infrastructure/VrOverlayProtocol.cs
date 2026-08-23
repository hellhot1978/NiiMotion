using System.IO.MemoryMappedFiles;
using System.Text.Json;

namespace NiiRMotion.Infrastructure;

public sealed record VrPanelState(int SchemaVersion, string Profile, string Game, string Locomotion, float Speed, string DeviceSummary, string Message, DateTimeOffset UpdatedAtUtc);

public sealed class VrPanelStatePublisher : IDisposable
{
    public const string MappingName = "NiiMotion.VrPanel.v1";
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedViewAccessor? _view;
    public VrPanelStatePublisher()
    {
        if (!OperatingSystem.IsWindows()) return;
        _mapping = MemoryMappedFile.CreateOrOpen(MappingName, 4096, MemoryMappedFileAccess.ReadWrite); _view = _mapping.CreateViewAccessor();
    }
    public void Publish(VrPanelState state)
    {
        if (_view is null) return; var bytes = JsonSerializer.SerializeToUtf8Bytes(state); if (bytes.Length > 4000) throw new InvalidDataException("VR panel durumu çok büyük.");
        _view.Write(0, 0x3150564Eu); _view.Write(4, bytes.Length); _view.WriteArray(8, bytes, 0, bytes.Length); _view.Flush();
    }
    public void Dispose() { _view?.Dispose(); _mapping?.Dispose(); }
}

public sealed class VrPanelStateReader : IDisposable
{
    private readonly MemoryMappedFile? _mapping;
    private readonly MemoryMappedViewAccessor? _view;
    public VrPanelStateReader()
    {
        if (!OperatingSystem.IsWindows()) return;
        _mapping = MemoryMappedFile.CreateOrOpen(VrPanelStatePublisher.MappingName, 4096, MemoryMappedFileAccess.ReadWrite); _view = _mapping.CreateViewAccessor();
    }
    public VrPanelState? Read()
    {
        if (_view is null || _view.ReadUInt32(0) != 0x3150564E) return null;
        var length = _view.ReadInt32(4); if (length is <= 0 or > 4000) return null;
        var bytes = new byte[length]; _view.ReadArray(8, bytes, 0, length);
        try { return JsonSerializer.Deserialize<VrPanelState>(bytes); } catch { return null; }
    }
    public void Dispose() { _view?.Dispose(); _mapping?.Dispose(); }
}

public enum VrPanelCommand { None, EmergencyStop, Rescan }
public sealed class VrPanelCommandChannel : IDisposable
{
    public const string MappingName = "NiiMotion.VrPanel.Commands.v1";
    private readonly MemoryMappedFile? _mapping; private readonly MemoryMappedViewAccessor? _view; private long _lastSequence;
    public VrPanelCommandChannel() { if (!OperatingSystem.IsWindows()) return; _mapping = MemoryMappedFile.CreateOrOpen(MappingName, 64, MemoryMappedFileAccess.ReadWrite); _view = _mapping.CreateViewAccessor(); }
    public void Send(VrPanelCommand command) { if (_view is null || command == VrPanelCommand.None) return; _view.Write(0, (int)command); _view.Write(8, DateTime.UtcNow.Ticks); _view.Flush(); }
    public VrPanelCommand Receive()
    {
        if (_view is null) return VrPanelCommand.None; var sequence = _view.ReadInt64(8); if (sequence == 0 || sequence == _lastSequence) return VrPanelCommand.None;
        _lastSequence = sequence; var value = _view.ReadInt32(0); return Enum.IsDefined(typeof(VrPanelCommand), value) ? (VrPanelCommand)value : VrPanelCommand.None;
    }
    public void Dispose() { _view?.Dispose(); _mapping?.Dispose(); }
}
