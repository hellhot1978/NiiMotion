using System.Numerics;

namespace NiiRMotion.Core;

public enum PsMoveTransport
{
    Unknown,
    Usb,
    Bluetooth
}

public sealed record PsMoveRawSample(Vector3 Acceleration, Vector3 AngularVelocity);

public sealed record PsMoveRawInputReport(
    byte Sequence,
    uint Buttons,
    byte Trigger,
    byte Battery,
    ushort Timestamp,
    PsMoveRawSample OlderSample,
    PsMoveRawSample LatestSample,
    Vector3 Magnetometer);

public static class PsMoveZcm1ReportParser
{
    public const int InputReportBytes = 49;
    public const byte InputReportId = 0x01;

    public static PsMoveRawInputReport Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length != InputReportBytes || report[0] != InputReportId)
            throw new ArgumentException("Expected a 49-byte ZCM1 input report with report ID 0x01.", nameof(report));

        var buttons = (uint)(report[2]
            | report[1] << 8
            | (report[3] & 0x01) << 16
            | (report[4] & 0xF0) << 13);

        return new(
            (byte)(report[4] & 0x0F),
            buttons,
            (byte)((report[5] + report[6]) / 2),
            report[12],
            (ushort)((report[11] << 8) | report[43]),
            new(ReadVector(report, 13), ReadVector(report, 25)),
            new(ReadVector(report, 19), ReadVector(report, 31)),
            new(
                Decode12(((report[38] & 0x0F) << 8) | report[39]),
                Decode12((report[40] << 4) | ((report[41] & 0xF0) >> 4)),
                Decode12(((report[41] & 0x0F) << 8) | report[42])));
    }

    private static Vector3 ReadVector(ReadOnlySpan<byte> report, int offset)
        => new(Decode16(report, offset), Decode16(report, offset + 2), Decode16(report, offset + 4));

    private static int Decode16(ReadOnlySpan<byte> report, int offset)
        => (report[offset] | report[offset + 1] << 8) - 0x8000;

    private static int Decode12(int value)
        => (value & 0x800) != 0 ? -(((~value) & 0xFFF) + 1) : value;
}

public sealed record PsMoveDeviceDescriptor(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    PsMoveTransport Transport,
    string Model)
{
    public const ushort SonyVendorId = 0x054C;
    public const ushort Zcm1ProductId = 0x03D5;

    public static bool TryCreate(
        string devicePath,
        ushort vendorId,
        ushort productId,
        out PsMoveDeviceDescriptor? descriptor)
    {
        descriptor = null;
        if (string.IsNullOrWhiteSpace(devicePath) || vendorId != SonyVendorId || productId != Zcm1ProductId)
            return false;

        descriptor = new(
            devicePath,
            vendorId,
            productId,
            InferTransport(devicePath),
            "PS Move CECH-ZCM1");
        return true;
    }

    private static PsMoveTransport InferTransport(string path)
    {
        if (path.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase)
            || path.Contains("BTHLEDEVICE", StringComparison.OrdinalIgnoreCase)
            || path.Contains("00001124-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase)
            || path.Contains("vid&0002", StringComparison.OrdinalIgnoreCase))
            return PsMoveTransport.Bluetooth;

        // Windows HID paths do not always expose their parent bus. An unknown
        // transport is safer than treating every VID/PID match as sensor-ready.
        return path.Contains("USB", StringComparison.OrdinalIgnoreCase)
            || path.Contains("vid_054c", StringComparison.OrdinalIgnoreCase)
            ? PsMoveTransport.Usb
            : PsMoveTransport.Unknown;
    }
}
