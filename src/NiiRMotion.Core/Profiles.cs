namespace NiiRMotion.Core;

public sealed record MotionProfile(string Id, string Name, IReadOnlySet<DeviceKind> Required, IReadOnlySet<DeviceKind> Optional, bool LocomotionAllowed)
{
    public static MotionProfile AlyxFullFusion { get; } = new("alyx-full-fusion", "Half-Life: Alyx / Full Fusion",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.JoyConLeft, DeviceKind.JoyConRight, DeviceKind.Phone, DeviceKind.BalanceBoard },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile ClassicVr { get; } = new("classic-vr", "Classic VR",
        new HashSet<DeviceKind> { DeviceKind.Quest3 },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, false);
    public static MotionProfile JoyConOnly { get; } = new("joycon-only", "Joy-Con Only",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.JoyConLeft, DeviceKind.JoyConRight },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile JoyConPhone { get; } = new("joycon-phone", "Joy-Con + Phone",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.JoyConLeft, DeviceKind.JoyConRight, DeviceKind.Phone },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile FullFusion { get; } = new("full-fusion", "All Devices",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.JoyConLeft, DeviceKind.JoyConRight, DeviceKind.Phone, DeviceKind.BalanceBoard },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile PhoneOnly { get; } = new("phone-only", "Phone Only (Experimental)",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.Phone },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile BoardOnly { get; } = new("board-only", "Balance Board Only (Experimental)",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.BalanceBoard },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile BoardJoyCon { get; } = new("board-joycon", "Balance Board + Joy-Con",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.BalanceBoard, DeviceKind.JoyConLeft, DeviceKind.JoyConRight },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
    public static MotionProfile BoardPhone { get; } = new("board-phone", "Balance Board + Phone (Experimental)",
        new HashSet<DeviceKind> { DeviceKind.Quest3, DeviceKind.BalanceBoard, DeviceKind.Phone },
        new HashSet<DeviceKind> { DeviceKind.VirtualDesktop, DeviceKind.HandTracking }, true);
}
