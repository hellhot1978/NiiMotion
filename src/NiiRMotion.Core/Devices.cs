namespace NiiRMotion.Core;

public enum DeviceKind { Quest3, SteamVr, VirtualDesktop, HandTracking, JoyConLeft, JoyConRight, PsMoveLeft, PsMoveRight, Phone, BalanceBoard }
public enum DeviceState { Connected, Missing, Unknown }
public sealed record DeviceStatus(DeviceKind Kind, string Name, DeviceState State, string Detail, string Action)
{
    public bool IsConnected => State == DeviceState.Connected;
    public string Symbol => State switch { DeviceState.Connected => "✓", DeviceState.Missing => "×", _ => "?" };
    public string StateText => State switch { DeviceState.Connected => "Bağlı", DeviceState.Missing => "Eksik", _ => "Belirsiz" };
    public string StateColor => State switch { DeviceState.Connected => "#55E6C1", DeviceState.Missing => "#FF7F9B", _ => "#F5C96A" };
    public string IconGlyph => Kind switch
    {
        DeviceKind.Quest3 => "⌁", DeviceKind.SteamVr => "▶", DeviceKind.VirtualDesktop => "▣", DeviceKind.HandTracking => "✋",
        DeviceKind.JoyConLeft => "◖", DeviceKind.JoyConRight => "◗", DeviceKind.PsMoveLeft => "●", DeviceKind.PsMoveRight => "●", DeviceKind.Phone => "▯", DeviceKind.BalanceBoard => "◇", _ => "•"
    };
    public string IconPath => Kind switch
    {
        DeviceKind.Quest3 => "/NiiRMotion.App;component/Assets/device-v3-quest3.png",
        DeviceKind.SteamVr => "/NiiRMotion.App;component/Assets/device-v3-steamvr.png",
        DeviceKind.VirtualDesktop => "/NiiRMotion.App;component/Assets/device-v3-virtual-desktop.png",
        DeviceKind.HandTracking => "/NiiRMotion.App;component/Assets/device-v3-hands.png",
        DeviceKind.JoyConLeft => "/NiiRMotion.App;component/Assets/device-v3-joycon-left.png",
        DeviceKind.JoyConRight => "/NiiRMotion.App;component/Assets/device-v3-joycon-right.png",
        DeviceKind.PsMoveLeft => "/NiiRMotion.App;component/Assets/niirmotion-icon.png",
        DeviceKind.PsMoveRight => "/NiiRMotion.App;component/Assets/niirmotion-icon.png",
        DeviceKind.Phone => "/NiiRMotion.App;component/Assets/device-v3-phone.png",
        DeviceKind.BalanceBoard => "/NiiRMotion.App;component/Assets/device-v3-board.png",
        _ => ""
    };
}
public interface IHardwareDiscoveryService
{
    bool IsTestMode { get; }
    Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken = default);
}
