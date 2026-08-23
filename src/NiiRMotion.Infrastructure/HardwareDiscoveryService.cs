using System.Diagnostics;
using System.Runtime.InteropServices;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class HardwareDiscoveryService : IHardwareDiscoveryService
{
    public bool IsTestMode => false;
    public async Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(async () =>
        {
        cancellationToken.ThrowIfCancellationRequested();
        var processes = Process.GetProcesses().Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var steamVr = processes.Contains("vrserver") || processes.Contains("vrmonitor");
        // The Streamer and Service remain open without a headset. The Server
        // process exists only while the headset has an active PC session.
        var virtualDesktopStreamer = processes.Contains("VirtualDesktop.Streamer")
            || processes.Contains("VirtualDesktopStreamer")
            || processes.Contains("Virtual Desktop Streamer");
        var virtualDesktop = VirtualDesktopSessionPresence.IsStable();
        IReadOnlyList<JoyConDeviceDescriptor> joyCons;
        try { joyCons = HidDeviceEnumerator.FindJoyCons(); } catch { joyCons = Array.Empty<JoyConDeviceDescriptor>(); }
        var leftJoyCon = joyCons.Any(x => x.Side == JoyConSide.Left);
        var rightJoyCon = joyCons.Any(x => x.Side == JoyConSide.Right);
        var moveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try { foreach (var probe in new PsMoveDiagnosticsService().Discover().Where(x => x.SensorReportsPossible && x.Device.StableId is not null)) moveIds.Add(probe.Device.StableId!); } catch { }
        IReadOnlySet<string> presentBluetoothIds;
        try { presentBluetoothIds = HidDeviceEnumerator.FindPresentBluetoothAddresses(); }
        catch { presentBluetoothIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
        var moveAssignments = await new PsMoveAssignmentStore(NiiMotionPaths.PsMoveAssignments).LoadAsync(cancellationToken).ConfigureAwait(false);
        var leftMoveLive = moveAssignments is { IsComplete: true } && moveIds.Contains(moveAssignments.LeftStableId);
        var rightMoveLive = moveAssignments is { IsComplete: true } && moveIds.Contains(moveAssignments.RightStableId);
        var leftMovePaired = moveAssignments is { IsComplete: true } && presentBluetoothIds.Contains(moveAssignments.LeftStableId);
        var rightMovePaired = moveAssignments is { IsComplete: true } && presentBluetoothIds.Contains(moveAssignments.RightStableId);
        var balanceBoard = false;
        try { balanceBoard = HidDeviceEnumerator.FindBalanceBoards().Count > 0; } catch { }
        var phoneConnected = PhonePresence.TryGetFresh(out var phoneEndpoint);
        IReadOnlyList<DeviceStatus> statuses =
        [
            steamVr
                ? FoundOrMissing(DeviceKind.Quest3, "Quest 3", OpenVrHeadsetPresence.IsPresent(),
                    "OpenVR üzerinden aktif başlık doğrulandı.",
                    "SteamVR çalışıyor ancak aktif başlık bulunamadı.",
                    "Quest 3'ü açın ve Virtual Desktop bağlantısını kurun.")
                : virtualDesktop
                    ? new DeviceStatus(DeviceKind.Quest3, "Quest 3", DeviceState.Connected,
                        "Quest 3, Virtual Desktop oturumu üzerinden bağlı.",
                        "Başlık bağlantısı hazır.")
                    : new DeviceStatus(DeviceKind.Quest3, "Quest 3", DeviceState.Unknown,
                        "SteamVR kapalı ve aktif Virtual Desktop başlık oturumu yok.",
                        "Quest'te Virtual Desktop'ı açıp bu bilgisayara bağlanın."),
            FoundOrMissing(DeviceKind.SteamVr, "SteamVR", steamVr, "VR çalışma zamanı çalışıyor.", "VR çalışma zamanı bulunamadı.", "SteamVR'ı başlatın."),
            FoundOrMissing(DeviceKind.VirtualDesktop, "Virtual Desktop", virtualDesktop,
                "Quest başlık oturumu bu bilgisayara bağlı.",
                virtualDesktopStreamer ? "Streamer açık; Quest henüz bu bilgisayara bağlı değil." : "Virtual Desktop Streamer kapalı.",
                virtualDesktopStreamer ? "Quest'te Virtual Desktop'ı açıp bilgisayara bağlanın." : "Virtual Desktop Streamer'ı başlatın."),
            new(DeviceKind.HandTracking, "Hand Tracking", DeviceState.Unknown, "Güvenilir yerel API ile doğrulanamıyor.", "Quest ve Virtual Desktop içinden el takibini doğrulayın."),
            FoundOrMissing(DeviceKind.JoyConLeft, "Sol Joy-Con", leftJoyCon, "Original Joy-Con L HID arabirimi algılandı.", "Nintendo VID/PID ile Joy-Con L bulunamadı.", "Joy-Con L'yi açın ve Windows Bluetooth bağlantısını kontrol edin."),
            FoundOrMissing(DeviceKind.JoyConRight, "Sağ Joy-Con", rightJoyCon, "Original Joy-Con R HID arabirimi algılandı.", "Nintendo VID/PID ile Joy-Con R bulunamadı.", "Joy-Con R'yi açın ve Windows Bluetooth bağlantısını kontrol edin."),
            PsMoveStatus(DeviceKind.PsMoveLeft, "Sol PS Move", leftMoveLive, leftMovePaired, "kırmızı"),
            PsMoveStatus(DeviceKind.PsMoveRight, "Sağ PS Move", rightMoveLive, rightMovePaired, "mavi"),
            FoundOrMissing(DeviceKind.Phone, "Android Telefon", phoneConnected,
                $"owoTrack canlı veri alınıyor · {phoneEndpoint}",
                "Telefondan canlı owoTrack verisi alınmıyor.",
                "Telefon Bağlantısı'nı açın ve owoTrack'i başlatın."),
            FoundOrMissing(DeviceKind.BalanceBoard, "Wii Balance Board", balanceBoard,
                "Nintendo RVL-WBC-01 Bluetooth HID bağlantısı aktif.",
                "Eşleştirilmiş Balance Board şu anda uykuda veya bağlantısız.",
                "Board'un ön güç düğmesine basın ve tekrar tarayın.")
        ];
        return statuses;
        }, cancellationToken).ConfigureAwait(false);
    }
    private static DeviceStatus Missing(DeviceKind kind, string name, string detail, string action) => new(kind, name, DeviceState.Missing, detail, action);
    private static DeviceStatus FoundOrMissing(DeviceKind kind, string name, bool found, string foundDetail, string missingDetail, string action) => new(kind, name, found ? DeviceState.Connected : DeviceState.Missing, found ? foundDetail : missingDetail, action);
    private static DeviceStatus PsMoveStatus(DeviceKind kind, string name, bool live, bool paired, string color) => live
        ? new(kind, name, DeviceState.Connected, $"Atanmış {color} PS Move sensör akışı hazır.", "Karta dokunarak ışığını yakabilirsiniz.")
        : paired
            ? new(kind, name, DeviceState.Unknown, $"Atanmış {color} PS Move eşleşmiş ancak sensör akışı uykuda.", "İlk açılış için büyük Move düğmesine bir kez basın; NiiMotion sonrasında bağlantıyı canlı tutar.")
            : new(kind, name, DeviceState.Missing, "Atanmış PS Move bulunamadı.", "Kurulum sihirbazını açın.");
}

public static class VirtualDesktopSessionPresence
{
    public static bool IsPresent()
    {
        try { return Process.GetProcessesByName("VirtualDesktop.Server").Length > 0; }
        catch { return false; }
    }

    public static bool IsStable() => IsPresent();
}

internal static class OpenVrHeadsetPresence
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool IsHmdPresentDelegate();

    public static bool IsPresent()
    {
        if (!HasCurrentPhysicalHeadsetInLog()) return false;
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", "SteamVR", "bin", "win64", "openvr_api.dll"),
            "openvr_api.dll"
        };
        foreach (var candidate in candidates)
        {
            if (!NativeLibrary.TryLoad(candidate, out var library)) continue;
            try
            {
                if (!NativeLibrary.TryGetExport(library, "VR_IsHmdPresent", out var address)) continue;
                return Marshal.GetDelegateForFunctionPointer<IsHmdPresentDelegate>(address)();
            }
            catch { }
            finally { NativeLibrary.Free(library); }
        }
        return false;
    }

    private static bool HasCurrentPhysicalHeadsetInLog()
    {
        try
        {
            var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "logs", "vrserver.txt");
            if (!File.Exists(logPath)) return false;
            var text = File.ReadAllText(logPath);
            var lastFailure = Math.Max(
                text.LastIndexOf("No connected devices found", StringComparison.OrdinalIgnoreCase),
                text.LastIndexOf("Deactivated device shimmed with CHMDShimDriver", StringComparison.OrdinalIgnoreCase));
            var lastPhysicalHeadset = -1;
            const string marker = "finished adding tracked device with serial number '";
            for (var index = 0; (index = text.IndexOf(marker, index, StringComparison.OrdinalIgnoreCase)) >= 0; index += marker.Length)
            {
                var serialStart = index + marker.Length;
                var serialEnd = text.IndexOf('\'', serialStart);
                if (serialEnd < 0) break;
                var serial = text[serialStart..serialEnd];
                if (!serial.Contains("Controller", StringComparison.OrdinalIgnoreCase)
                    && !serial.Contains("NIIRMOTION", StringComparison.OrdinalIgnoreCase)
                    && !serial.Contains("LinkNotEnabled", StringComparison.OrdinalIgnoreCase))
                    lastPhysicalHeadset = index;
            }
            return lastPhysicalHeadset > lastFailure;
        }
        catch { return false; }
    }
}

public sealed class MockHardwareDiscoveryService : IHardwareDiscoveryService
{
    public bool IsTestMode => true;
    public Task<IReadOnlyList<DeviceStatus>> ScanAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DeviceStatus> statuses = Enum.GetValues<DeviceKind>().Select(k => new DeviceStatus(k, Name(k), DeviceState.Connected, "Simüle edilmiş cihaz.", "Test modu verisi; gerçek donanım değildir.")).ToArray();
        return Task.FromResult(statuses);
    }
    private static string Name(DeviceKind k) => k switch { DeviceKind.SteamVr => "SteamVR", DeviceKind.VirtualDesktop => "Virtual Desktop", DeviceKind.HandTracking => "Hand Tracking", DeviceKind.JoyConLeft => "Sol Joy-Con", DeviceKind.JoyConRight => "Sağ Joy-Con", DeviceKind.PsMoveLeft => "Sol PS Move", DeviceKind.PsMoveRight => "Sağ PS Move", DeviceKind.Phone => "Android Telefon", DeviceKind.BalanceBoard => "Wii Balance Board", _ => "Quest 3" };
}
