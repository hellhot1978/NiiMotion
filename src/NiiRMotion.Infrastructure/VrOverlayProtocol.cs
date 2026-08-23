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
