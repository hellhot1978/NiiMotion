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
    private const uint DigcfDeviceInterface = 0x10;
    private static readonly nint InvalidHandleValue = new(-1);

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

    private static bool TryReadVidPid(string path, out ushort vid, out ushort pid)
    {
        vid = pid = 0;
        var match = VidPidRegex().Match(path);
        return match.Success
            && ushort.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vid)
            && ushort.TryParse(match.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
    }

    public static IReadOnlyList<string> FindAllHidPaths()
    {
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevs(ref hidGuid, null, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == InvalidHandleValue) throw new Win32Exception(Marshal.GetLastWin32Error());
        var results = new List<string>();
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
                SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out var required, 0);
                var buffer = Marshal.AllocHGlobal((int)required);
                try
                {
                    Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, 0)) continue;
                    var path = Marshal.PtrToStringUni(buffer + 4) ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(path)) results.Add(path);
                }
                finally { Marshal.FreeHGlobal(buffer); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    [GeneratedRegex("(?:vid_|vid&0002)([0-9a-f]{4}).*(?:pid_|pid&)([0-9a-f]{4})", RegexOptions.IgnoreCase)] private static partial Regex VidPidRegex();
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterfaceData { public int Size; public Guid InterfaceClassGuid; public uint Flags; public nint Reserved; }
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid hidGuid);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint SetupDiGetClassDevs(ref Guid classGuid, string? enumerator, nint hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] private static extern bool SetupDiEnumDeviceInterfaces(nint deviceInfoSet, nint deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref DeviceInterfaceData deviceInterfaceData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool SetupDiGetDeviceInterfaceDetail(nint deviceInfoSet, ref DeviceInterfaceData deviceInterfaceData, nint detailData, uint detailDataSize, out uint requiredSize, nint deviceInfoData);
    [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(nint deviceInfoSet);
}
