using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Win32.SafeHandles;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed class JoyConSensorSource : ISensorSource<JoyConImuSample>
{
    private readonly JoyConDeviceDescriptor _device;
    private readonly BoundedSensorBuffer<JoyConImuSample> _buffer;
    private readonly SensorTimingDiagnostics _timing = new();
    private FileStream? _stream; private Task? _readLoop; private Task? _keepAliveLoop; private CancellationTokenSource? _lifetime; private byte _packetNumber; private long _sequence;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    internal static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(20);
    private HidCapabilities _capabilities;
    public JoyConSensorSource(JoyConDeviceDescriptor device, int bufferCapacity = 512) { _device = device; _buffer = new(bufferCapacity); }
    public string SourceId => _device.Side == JoyConSide.Left ? "joycon-left" : "joycon-right";
    public SensorMode Mode => SensorMode.Live;
    public ChannelReader<JoyConImuSample> Samples => _buffer.Reader;
    public SensorTimingSnapshot Timing => _timing.Snapshot(Stopwatch.GetTimestamp());
    public ushort InputReportLength => _capabilities.InputReportByteLength;
    public ushort OutputReportLength => _capabilities.OutputReportByteLength;
    public JoyConImuCalibration? FactoryCalibration { get; private set; }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_stream is not null) throw new InvalidOperationException("Joy-Con source already started.");
        _stream = new FileStream(_device.DevicePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite, 1, FileOptions.None);
        _capabilities = GetCapabilities(_stream.SafeFileHandle);
        FactoryCalibration = await ReadFactoryCalibrationAsync(cancellationToken);
        await SendSubcommandAsync(0x40, new byte[] { 0x01 }, cancellationToken); // enable IMU
        await Task.Delay(65, cancellationToken);
        await SendSubcommandAsync(0x03, new byte[] { JoyConReportParser.StandardFullReportId }, cancellationToken); // standard full report mode
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _readLoop = Task.Run(() => ReadLoop(_lifetime.Token), CancellationToken.None);
        _keepAliveLoop = Task.Run(() => KeepAliveLoopAsync(_lifetime.Token), CancellationToken.None);
    }

    private async Task SendSubcommandAsync(byte subcommand, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            var report = new byte[Math.Max(49, (int)_capabilities.OutputReportByteLength)]; report[0] = 0x01; report[1] = (byte)(_packetNumber++ & 0x0F);
            ReadOnlySpan<byte> neutralRumble = [0x00, 0x01, 0x40, 0x40, 0x00, 0x01, 0x40, 0x40]; neutralRumble.CopyTo(report.AsSpan(2));
            report[10] = subcommand; payload.Span.CopyTo(report.AsSpan(11));
            await _stream!.WriteAsync(report, cancellationToken); await _stream.FlushAsync(cancellationToken);
        }
        finally { _writeLock.Release(); }
    }

    private async Task KeepAliveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(KeepAliveInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
                await SendSubcommandAsync(0x03, new byte[] { JoyConReportParser.StandardFullReportId }, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (IOException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _buffer.Complete(ex); _lifetime?.Cancel(); }
    }

    private async Task<JoyConImuCalibration> ReadFactoryCalibrationAsync(CancellationToken cancellationToken)
    {
        const uint address = 0x6020; const byte length = 24;
        var request = new byte[5]; BitConverter.TryWriteBytes(request, address); request[4] = length;
        await SendSubcommandAsync(0x10, request, cancellationToken);
        var response = new byte[Math.Max(49, (int)_capabilities.InputReportByteLength)];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReadFile(_stream!.SafeFileHandle, response, (uint)response.Length, out var bytesRead, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
            if (bytesRead < 44 || response[0] != 0x21 || response[14] != 0x10) continue;
            if ((response[13] & 0x80) == 0) throw new InvalidOperationException("Joy-Con rejected SPI calibration read.");
            var returnedAddress = BitConverter.ToUInt32(response, 15); var returnedLength = response[19];
            if (returnedAddress != address || returnedLength < length) throw new InvalidDataException("Unexpected Joy-Con SPI calibration response.");
            return JoyConImuCalibration.ParseFactory(response.AsSpan(20, length));
        }
    }

    private void ReadLoop(CancellationToken cancellationToken)
    {
        var report = new byte[Math.Max(JoyConReportParser.ReportLength, (int)_capabilities.InputReportByteLength)];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!ReadFile(_stream!.SafeFileHandle, report, (uint)report.Length, out var bytesRead, 0)) throw new Win32Exception(Marshal.GetLastWin32Error());
                var read = (int)bytesRead;
                if (read == 0) throw new EndOfStreamException("Joy-Con HID stream closed.");
                if (report[0] != JoyConReportParser.StandardFullReportId || read < JoyConReportParser.ReportLength) continue;
                var receivedTicks = Stopwatch.GetTimestamp();
                foreach (var sample in JoyConReportParser.ParseStandardFullReport(report.AsSpan(0, read), SourceId, Interlocked.Add(ref _sequence, 3) - 3, receivedTicks, DateTimeOffset.UtcNow, FactoryCalibration)) { _timing.Observe(sample.Timestamp.MonotonicTicks); _buffer.TryWrite(sample); }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _buffer.Complete(ex); return; }
        _buffer.Complete();
    }

    public async ValueTask DisposeAsync()
    {
        if (_lifetime is not null) _lifetime.Cancel();
        if (_stream is not null) await _stream.DisposeAsync();
        if (_readLoop is not null) try { await _readLoop; } catch (OperationCanceledException) { }
        if (_keepAliveLoop is not null) try { await _keepAliveLoop; } catch (OperationCanceledException) { }
        _lifetime?.Dispose(); _stream = null;
        _writeLock.Dispose();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadFile(SafeFileHandle handle, byte[] buffer, uint bytesToRead, out uint bytesRead, nint overlapped);

    private static HidCapabilities GetCapabilities(SafeFileHandle handle)
    {
        if (!HidD_GetPreparsedData(handle, out var data)) throw new Win32Exception(Marshal.GetLastWin32Error());
        try { var status = HidP_GetCaps(data, out var caps); if (status < 0) throw new InvalidOperationException($"HidP_GetCaps failed: 0x{status:X8}"); return caps; }
        finally { HidD_FreePreparsedData(data); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct HidCapabilities
    {
        public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
        public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
    }
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out nint preparsedData);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool HidD_FreePreparsedData(nint preparsedData);
    [DllImport("hid.dll")] private static extern int HidP_GetCaps(nint preparsedData, out HidCapabilities capabilities);
}
