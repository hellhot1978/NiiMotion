using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record PsMoveHidProbe(
    PsMoveDeviceDescriptor Device,
    bool Opened,
    ushort InputReportBytes,
    ushort OutputReportBytes,
    ushort FeatureReportBytes,
    string Detail)
{
    public bool SensorReportsPossible => Opened && InputReportBytes > 0;
}

public sealed record PsMoveRawCapture(
    PsMoveDeviceDescriptor Device,
    int ReportCount,
    int DistinctReportCount,
    byte ReportId,
    int ReportBytes,
    string FirstReportHex);

public sealed class PsMoveDiagnosticsService
{
    public IReadOnlyList<PsMoveHidProbe> Discover()
        => HidDeviceEnumerator.FindPsMoves().Select(ProbeReadOnly).ToArray();

    public async Task<PsMoveRawCapture?> CaptureInputReportsAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var probe = Discover().FirstOrDefault(x => x.SensorReportsPossible);
        if (probe is null) return null;

        return await CaptureProbeAsync(probe, duration, cancellationToken);
    }

    public async Task<IReadOnlyList<PsMoveRawCapture>> CaptureAllInputReportsAsync(TimeSpan duration, CancellationToken cancellationToken = default)
    {
        var probes = Discover()
            .Where(x => x.SensorReportsPossible)
            .DistinctBy(x => x.Device.StableId ?? x.Device.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return await Task.WhenAll(probes.Select(x => CaptureProbeAsync(x, duration, cancellationToken)));
    }

    public async Task<PsMoveDeviceDescriptor?> WaitForButtonAsync(uint buttonMask, TimeSpan timeoutDuration, CancellationToken cancellationToken = default)
    {
        var probes = Discover()
            .Where(x => x.SensorReportsPossible)
            .DistinctBy(x => x.Device.StableId ?? x.Device.DevicePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (probes.Length == 0) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutDuration);
        var found = new TaskCompletionSource<PsMoveDeviceDescriptor>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readers = probes.Select(x => WatchButtonAsync(x, buttonMask, found, timeout.Token)).ToArray();
        try
        {
            return await found.Task.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            timeout.Cancel();
            try { await Task.WhenAll(readers); } catch (OperationCanceledException) { }
        }
    }

    public async Task ShowAssignmentColorsAsync(PsMoveAssignments assignments, TimeSpan duration, CancellationToken cancellationToken = default)
    {
        if (!assignments.IsComplete) throw new ArgumentException("Complete left/right assignments are required.", nameof(assignments));
        var probes = Discover().Where(x => x.SensorReportsPossible).ToArray();
        var left = probes.SingleOrDefault(x => string.Equals(x.Device.StableId, assignments.LeftStableId, StringComparison.OrdinalIgnoreCase));
        var right = probes.SingleOrDefault(x => string.Equals(x.Device.StableId, assignments.RightStableId, StringComparison.OrdinalIgnoreCase));
        if (left is null || right is null) throw new InvalidOperationException("Both assigned PS Move controllers must be connected.");

        await using var leftStream = OpenOutput(left);
        await using var rightStream = OpenOutput(right);
        var leftRed = PsMoveZcm1OutputReport.CreateLed(255, 0, 0, left.OutputReportBytes);
        var rightBlue = PsMoveZcm1OutputReport.CreateLed(0, 80, 255, right.OutputReportBytes);
        var offLeft = PsMoveZcm1OutputReport.CreateLed(0, 0, 0, left.OutputReportBytes);
        var offRight = PsMoveZcm1OutputReport.CreateLed(0, 0, 0, right.OutputReportBytes);
        var until = DateTimeOffset.UtcNow + duration;
        try
        {
            while (DateTimeOffset.UtcNow < until)
            {
                await leftStream.WriteAsync(leftRed, cancellationToken);
                await rightStream.WriteAsync(rightBlue, cancellationToken);
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        finally
        {
            await leftStream.WriteAsync(offLeft, CancellationToken.None);
            await rightStream.WriteAsync(offRight, CancellationToken.None);
        }
    }

    private static FileStream OpenOutput(PsMoveHidProbe probe)
        => new(probe.Device.DevicePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite, probe.OutputReportBytes, FileOptions.Asynchronous);

    private static async Task WatchButtonAsync(PsMoveHidProbe probe, uint buttonMask, TaskCompletionSource<PsMoveDeviceDescriptor> found, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(probe.Device.DevicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, probe.InputReportBytes, FileOptions.Asynchronous);
        var buffer = new byte[probe.InputReportBytes];
        while (!cancellationToken.IsCancellationRequested)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read != PsMoveZcm1ReportParser.InputReportBytes) continue;
            var report = PsMoveZcm1ReportParser.Parse(buffer);
            if ((report.Buttons & buttonMask) != 0)
            {
                found.TrySetResult(probe.Device);
                return;
            }
        }
    }

    private static async Task<PsMoveRawCapture> CaptureProbeAsync(PsMoveHidProbe probe, TimeSpan duration, CancellationToken cancellationToken)
    {

        await using var stream = new FileStream(
            probe.Device.DevicePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            probe.InputReportBytes,
            FileOptions.Asynchronous);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        var buffer = new byte[probe.InputReportBytes];
        byte[]? first = null;
        var reports = 0;
        var distinct = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read <= 0) continue;
                reports++;
                var report = buffer.AsSpan(0, read).ToArray();
                first ??= report;
                distinct.Add(Convert.ToHexString(report));
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }

        return new(
            probe.Device,
            reports,
            distinct.Count,
            first is { Length: > 0 } ? first[0] : (byte)0,
            first?.Length ?? 0,
            first is null ? string.Empty : Convert.ToHexString(first));
    }

    private static PsMoveHidProbe ProbeReadOnly(PsMoveDeviceDescriptor device)
    {
        using var handle = CreateFile(
            device.DevicePath,
            0,
            FileShare.ReadWrite,
            0,
            FileMode.Open,
            0,
            0);

        if (handle.IsInvalid)
            return new(device, false, 0, 0, 0, new Win32Exception(Marshal.GetLastWin32Error()).Message);

        if (!HidD_GetPreparsedData(handle, out var preparsedData))
            return new(device, false, 0, 0, 0, "HID capability data could not be opened.");

        try
        {
            var status = HidP_GetCaps(preparsedData, out var caps);
            if (status < 0)
                return new(device, false, 0, 0, 0, $"HidP_GetCaps failed: 0x{status:X8}");

            return new(
                device,
                true,
                caps.InputReportByteLength,
                caps.OutputReportByteLength,
                caps.FeatureReportByteLength,
                "Read-only HID metadata available.");
        }
        finally
        {
            HidD_FreePreparsedData(preparsedData);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HidpCaps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        FileShare shareMode,
        nint securityAttributes,
        FileMode creationDisposition,
        uint flagsAndAttributes,
        nint templateFile);

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle hidDeviceObject, out nint preparsedData);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint preparsedData, out HidpCaps capabilities);
}
