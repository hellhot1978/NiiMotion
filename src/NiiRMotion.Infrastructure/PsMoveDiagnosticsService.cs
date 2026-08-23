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

public sealed record PsMoveFactoryCalibrationCapture(PsMoveDeviceDescriptor Device, byte[] Blob);

public sealed record PsMoveCalibratedHealth(
    string StableId,
    int ReportCount,
    int MissingReports,
    double ReportRateHz,
    double JitterMs,
    byte Battery,
    uint ObservedButtons,
    float MinimumAccelerationG,
    float MaximumAccelerationG,
    float MaximumAngularVelocityRadPerSecond);

public sealed record PsMoveBatteryStatus(string StableId, byte RawLevel, int? Percent, bool Charging)
{
    public string Display => Charging ? "Şarj oluyor" : Percent is int percent ? $"%{percent}" : "Bilinmiyor";
}

public sealed class PsMoveDiagnosticsService
{
    public IReadOnlyList<PsMoveHidProbe> Discover()
        => HidDeviceEnumerator.FindPsMoves().Select(ProbeReadOnly).ToArray();

    public async Task<IReadOnlyList<PsMoveBatteryStatus>> ReadBatteryStatusAsync(CancellationToken cancellationToken = default)
    {
        var probes = Discover().Where(x => x.SensorReportsPossible && x.Device.StableId is not null)
            .DistinctBy(x => x.Device.StableId!, StringComparer.OrdinalIgnoreCase).ToArray();
        var results = await Task.WhenAll(probes.Select(ReadBatteryAsync));
        return results.Where(x => x is not null).Select(x => x!).ToArray();

        async Task<PsMoveBatteryStatus?> ReadBatteryAsync(PsMoveHidProbe probe)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(1));
            try
            {
                await using var stream = new FileStream(probe.Device.DevicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, probe.InputReportBytes, FileOptions.Asynchronous);
                var buffer = new byte[probe.InputReportBytes];
                while (!timeout.IsCancellationRequested)
                {
                    if (await stream.ReadAsync(buffer, timeout.Token) != PsMoveZcm1ReportParser.InputReportBytes) continue;
                    var raw = PsMoveZcm1ReportParser.Parse(buffer).Battery;
                    return new(probe.Device.StableId!, raw, raw <= 5 ? raw * 20 : raw == 0xEF ? 100 : null, raw == 0xEE);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            catch { }
            return null;
        }
    }

    public PsMoveFactoryCalibrationCapture? ReadUsbFactoryCalibration()
    {
        var probe = Discover().FirstOrDefault(x => x.Device.Transport == PsMoveTransport.Usb && x.SensorReportsPossible);
        if (probe is null) return null;

        using var handle = CreateFile(probe.Device.DevicePath, 0, FileShare.ReadWrite, 0, FileMode.Open, 0, 0);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error());
        var blocks = new Dictionary<byte, byte[]>();
        for (var attempt = 0; attempt < 6 && blocks.Count < 3; attempt++)
        {
            var report = new byte[49];
            report[0] = 0x10;
            if (!HidD_GetFeature(handle, report, report.Length))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "PS Move factory calibration feature report could not be read.");
            if (report[1] is 0x00 or 0x01 or 0x82) blocks[report[1]] = report;
        }
        if (!blocks.TryGetValue(0x00, out var first) || !blocks.TryGetValue(0x01, out var second) || !blocks.TryGetValue(0x82, out var third))
            throw new InvalidDataException("PS Move returned an incomplete factory calibration sequence.");

        var blob = new byte[143];
        Buffer.BlockCopy(first, 0, blob, 0, 49);
        Buffer.BlockCopy(second, 2, blob, 49, 47);
        Buffer.BlockCopy(third, 2, blob, 96, 47);
        return new(probe.Device, blob);
    }

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

    public async Task ShowAssignedControllerColorAsync(
        PsMoveAssignments assignments,
        LegSide side,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        if (!assignments.IsComplete) throw new ArgumentException("Complete left/right assignments are required.", nameof(assignments));
        var stableId = side == LegSide.Left ? assignments.LeftStableId : assignments.RightStableId;
        var probe = await WaitForAssignedProbeAsync(stableId, TimeSpan.FromSeconds(8), cancellationToken);
        if (probe is null)
            throw new InvalidOperationException(side == LegSide.Left
                ? "Sol PS Move Bluetooth'ta kayıtlı ancak sensör kanalı uyanmadı. Büyük Move düğmesine basıp yeniden deneyin."
                : "Sağ PS Move Bluetooth'ta kayıtlı ancak sensör kanalı uyanmadı. Büyük Move düğmesine basıp yeniden deneyin.");

        await using var stream = OpenOutput(probe);
        var color = side == LegSide.Left
            ? PsMoveZcm1OutputReport.CreateLed(255, 0, 0, probe.OutputReportBytes)
            : PsMoveZcm1OutputReport.CreateLed(0, 80, 255, probe.OutputReportBytes);
        var off = PsMoveZcm1OutputReport.CreateLed(0, 0, 0, probe.OutputReportBytes);
        var until = DateTimeOffset.UtcNow + duration;
        try
        {
            while (DateTimeOffset.UtcNow < until)
            {
                await stream.WriteAsync(color, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
        finally
        {
            await stream.WriteAsync(off, CancellationToken.None);
        }
    }

    private async Task<PsMoveHidProbe?> WaitForAssignedProbeAsync(
        string stableId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow + timeout;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var probe = Discover()
                .Where(x => x.SensorReportsPossible)
                .SingleOrDefault(x => string.Equals(x.Device.StableId, stableId, StringComparison.OrdinalIgnoreCase));
            if (probe is not null) return probe;
            await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < until);
        return null;
    }

    public async Task ShowControllerColorAsync(
        string stableId,
        LegSide side,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var probe = Discover()
            .Where(x => x.SensorReportsPossible)
            .SingleOrDefault(x => string.Equals(x.Device.StableId, stableId, StringComparison.OrdinalIgnoreCase));
        if (probe is null) throw new InvalidOperationException("Tanıtılan PS Move Bluetooth üzerinden bulunamadı.");

        await using var stream = OpenOutput(probe);
        var color = side == LegSide.Left
            ? PsMoveZcm1OutputReport.CreateLed(255, 0, 0, probe.OutputReportBytes)
            : PsMoveZcm1OutputReport.CreateLed(0, 80, 255, probe.OutputReportBytes);
        var off = PsMoveZcm1OutputReport.CreateLed(0, 0, 0, probe.OutputReportBytes);
        var until = DateTimeOffset.UtcNow + duration;
        try
        {
            while (DateTimeOffset.UtcNow < until)
            {
                await stream.WriteAsync(color, cancellationToken);
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }
        finally { await stream.WriteAsync(off, CancellationToken.None); }
    }

    public async Task<IReadOnlyList<PsMoveCalibratedHealth>> CaptureCalibratedHealthAsync(
        IReadOnlyList<StoredPsMoveCalibration> storedCalibrations,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var calibrations = storedCalibrations.ToDictionary(x => x.StableId, x => x.Parse(), StringComparer.OrdinalIgnoreCase);
        var probes = Discover().Where(x => x.SensorReportsPossible && x.Device.StableId is not null && calibrations.ContainsKey(x.Device.StableId)).ToArray();
        return await Task.WhenAll(probes.Select(x => CaptureHealthProbeAsync(x, calibrations[x.Device.StableId!], duration, cancellationToken)));
    }

    private static async Task<PsMoveCalibratedHealth> CaptureHealthProbeAsync(PsMoveHidProbe probe, PsMoveZcm1FactoryCalibration calibration, TimeSpan duration, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(probe.Device.DevicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, probe.InputReportBytes, FileOptions.Asynchronous);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(duration);
        var buffer = new byte[probe.InputReportBytes];
        var intervals = new List<double>();
        long previousTicks = 0;
        int? previousSequence = null;
        var reports = 0;
        var missing = 0;
        byte battery = 0;
        uint buttons = 0;
        var minAccel = float.MaxValue;
        var maxAccel = 0f;
        var maxGyro = 0f;
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            while (!timeout.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, timeout.Token);
                if (read != PsMoveZcm1ReportParser.InputReportBytes) continue;
                var now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (previousTicks > 0) intervals.Add((now - previousTicks) * 1000d / System.Diagnostics.Stopwatch.Frequency);
                previousTicks = now;
                var report = PsMoveZcm1ReportParser.Parse(buffer);
                if (previousSequence.HasValue)
                {
                    var delta = (report.Sequence - previousSequence.Value + 16) % 16;
                    if (delta > 1) missing += delta - 1;
                }
                previousSequence = report.Sequence;
                reports++;
                battery = report.Battery;
                buttons |= report.Buttons;
                foreach (var sample in new[] { report.OlderSample, report.LatestSample })
                {
                    var accel = calibration.CalibrateAcceleration(sample.Acceleration).Length();
                    var gyro = calibration.CalibrateGyroscope(sample.AngularVelocity).Length();
                    minAccel = Math.Min(minAccel, accel);
                    maxAccel = Math.Max(maxAccel, accel);
                    maxGyro = Math.Max(maxGyro, gyro);
                }
            }
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
        var elapsed = Math.Max(.001, (System.Diagnostics.Stopwatch.GetTimestamp() - started) / (double)System.Diagnostics.Stopwatch.Frequency);
        var mean = intervals.Count == 0 ? 0 : intervals.Average();
        var jitter = intervals.Count == 0 ? 0 : Math.Sqrt(intervals.Sum(x => (x - mean) * (x - mean)) / intervals.Count);
        return new(probe.Device.StableId!, reports, missing, reports / elapsed, jitter, battery, buttons, minAccel == float.MaxValue ? 0 : minAccel, maxAccel, maxGyro);
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

    [DllImport("hid.dll", SetLastError = true)]
    private static extern bool HidD_GetFeature(SafeFileHandle hidDeviceObject, [Out] byte[] reportBuffer, int reportBufferLength);

    [DllImport("hid.dll")]
    private static extern bool HidD_FreePreparsedData(nint preparsedData);

    [DllImport("hid.dll")]
    private static extern int HidP_GetCaps(nint preparsedData, out HidpCaps capabilities);
}
