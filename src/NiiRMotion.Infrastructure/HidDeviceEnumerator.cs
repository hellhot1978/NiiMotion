using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public static partial class HidDeviceEnumerator
{
    private const ushort NintendoVendorId = 0x057E;
    private const ushort BalanceBoardProductId = 0x0306;
    private const uint DigcfPresent = 0x2;
    private const uint DigcfAllClasses = 0x4;
    private const uint DigcfDeviceInterface = 0x10;
    private static readonly nint InvalidHandleValue = new(-1);

    private sealed record HidInterface(string Path, string? ParentInstanceId);

    public static IReadOnlyList<JoyConDeviceDescriptor> FindJoyCons()
    {
        var results = new List<JoyConDeviceDescriptor>();
        foreach (var path in FindAllHidPaths())
        {
            var match = VidPidRegex().Match(path);
            if (match.Success && ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var vid) && ushort.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out var pid) && JoyConDeviceDescriptor.TryCreate(path, vid, pid, out var descriptor)) results.Add(descriptor!);
        }
        return results.DistinctBy(x => x.DevicePath).ToArray();
    }

    public static IReadOnlyList<string> FindBalanceBoards() => FindAllHidPaths()
        .Where(path => TryReadVidPid(path, out var vid, out var pid) && vid == NintendoVendorId && pid == BalanceBoardProductId)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    public static IReadOnlyList<PsMoveDeviceDescriptor> FindPsMoves()
    {
        var results = new List<PsMoveDeviceDescriptor>();
        foreach (var hid in FindAllHidInterfaces())
        {
            var stableId = TryReadBluetoothAddress(hid.ParentInstanceId);
            if (TryReadVidPid(hid.Path, out var vid, out var pid)
                && PsMoveDeviceDescriptor.TryCreate(hid.Path, vid, pid, out var descriptor, stableId))
                results.Add(descriptor!);
        }

        return results.DistinctBy(x => x.DevicePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public static IReadOnlySet<string> FindPresentBluetoothAddresses()
    {
        var set = SetupDiGetClassDevsAll(0, "BTHENUM", 0, DigcfPresent | DigcfAllClasses);
        if (set == InvalidHandleValue) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                if (!SetupDiEnumDeviceInfo(set, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    continue;
                }
                var buffer = new System.Text.StringBuilder(512);
                if (CM_Get_Device_ID(data.DevInst, buffer, buffer.Capacity, 0) != 0) continue;
                var match = BluetoothDeviceAddressRegex().Match(buffer.ToString());
                if (match.Success) results.Add(match.Groups[1].Value.ToUpperInvariant());
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return results;
    }

    private static bool TryReadVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        var match = VidPidRegex().Match(path);
        return match.Success
            && ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vid)
            && ushort.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
    }

    public static IReadOnlyList<string> FindAllHidPaths()
        => FindAllHidInterfaces().Select(x => x.Path).ToArray();

    private static IReadOnlyList<HidInterface> FindAllHidInterfaces()
    {
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevs(ref hidGuid, null, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandleValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        var results = new List<HidInterface>();
        try
        {
            for (uint index = 0; ; index++)
            {
                var data = new DeviceInterfaceData { Size = Marshal.SizeOf<DeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, ref hidGuid, index, ref data))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    continue;
                }
                SetupDiGetDeviceInterfaceDetailSize(set, ref data, 0, 0, out var required, 0);
                var buffer = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    var deviceInfo = new DeviceInfoData { Size = Marshal.SizeOf<DeviceInfoData>() };
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, ref deviceInfo)) continue;
                    // The variable-length path begins immediately after cbSize. The
                    // cbSize value is 8 on x64 for API alignment, but the string itself
                    // still starts at byte 4 in the returned buffer.
                    var path = Marshal.PtrToStringUni(buffer + 4) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path)) results.Add(new(path, GetParentInstanceId(deviceInfo.DevInst)));
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return results.DistinctBy(x => x.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string? GetParentInstanceId(uint deviceInstance)
    {
        if (CM_Get_Parent(out var parent, deviceInstance, 0) != 0) return null;
        var buffer = new System.Text.StringBuilder(512);
        return CM_Get_Device_ID(parent, buffer, buffer.Capacity, 0) == 0 ? buffer.ToString() : null;
    }

    private static string? TryReadBluetoothAddress(string? parentInstanceId)
    {
        if (string.IsNullOrWhiteSpace(parentInstanceId)) return null;
        var match = BluetoothAddressRegex().Match(parentInstanceId);
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    [GeneratedRegex("(?:vid_|vid&0002)([0-9a-f]{4}).*(?:pid_|pid&)([0-9a-f]{4})", RegexOptions.IgnoreCase)] private static partial Regex VidPidRegex();
    [GeneratedRegex("&0&([0-9a-f]{12})(?:_C[0-9a-f]+)?$", RegexOptions.IgnoreCase)] private static partial Regex BluetoothAddressRegex();
    [GeneratedRegex(@"^BTHENUM\\DEV_([0-9a-f]{12})\\", RegexOptions.IgnoreCase)] private static partial Regex BluetoothDeviceAddressRegex();
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public uint Flags; public nint Reserved; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInfoData { public int Size; public Guid ClassGuid; public uint DevInst; public nint Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetClassDevsW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevsAll(nint classGuid, string? enumerator, nint hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInfo(nint deviceInfoSet, uint memberIndex, ref DeviceInfoData deviceInfoData);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, nint detailData, uint detailDataSize, out uint requiredSize, ref DeviceInfoData deviceInfoData);
    [DllImport("setupapi.dll", EntryPoint = "SetupDiGetDeviceInterfaceDetailW", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetailSize(nint deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, nint detailData, uint detailDataSize, out uint requiredSize, nint deviceInfoData);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
    [DllImport("cfgmgr32.dll")] private static extern int CM_Get_Parent(out uint parentDeviceInstance, uint deviceInstance, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)] private static extern int CM_Get_Device_ID(uint deviceInstance, System.Text.StringBuilder buffer, int bufferLength, uint flags);
}
