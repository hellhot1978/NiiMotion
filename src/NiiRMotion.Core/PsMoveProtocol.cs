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

public static class PsMoveZcm1OutputReport
{
    public const byte LedReportId = 0x06;

    public static byte[] CreateLed(byte red, byte green, byte blue, int reportBytes = 49)
    {
        if (reportBytes < 7) throw new ArgumentOutOfRangeException(nameof(reportBytes));
        var report = new byte[reportBytes];
        report[0] = LedReportId;
        report[2] = red;
        report[3] = green;
        report[4] = blue;
        report[6] = 0; // Rumble always remains off during identification.
        return report;
    }
}

public sealed record PsMoveZcm1FactoryCalibration(
    Vector3 AccelerationLow,
    Vector3 AccelerationHigh,
    Vector3 GyroscopeBias,
    Vector3 GyroscopeRadiansPerSecondPerUnit)
{
    public static PsMoveZcm1FactoryCalibration Parse(ReadOnlySpan<byte> blob)
    {
        if (blob.Length != 143) throw new ArgumentException("Expected a 143-byte ZCM1 factory calibration blob.", nameof(blob));
        var low = new Vector3(ReadUnsigned(blob, 10), ReadUnsigned(blob, 36), ReadUnsigned(blob, 20));
        var high = new Vector3(ReadUnsigned(blob, 22), ReadUnsigned(blob, 30), ReadUnsigned(blob, 8));
        var bias = new Vector3(ReadUnsigned(blob, 42), ReadUnsigned(blob, 44), ReadUnsigned(blob, 46));
        var rpm80 = new Vector3(ReadUnsigned(blob, 70) - bias.X, ReadUnsigned(blob, 80) - bias.Y, ReadUnsigned(blob, 90) - bias.Z);
        if (high.X == low.X || high.Y == low.Y || high.Z == low.Z || rpm80.X == 0 || rpm80.Y == 0 || rpm80.Z == 0)
            throw new InvalidDataException("ZCM1 factory calibration contains a zero sensor range.");
        var radiansAt80Rpm = 80f * 2f * MathF.PI / 60f;
        return new(low, high, bias, new(radiansAt80Rpm / rpm80.X, radiansAt80Rpm / rpm80.Y, radiansAt80Rpm / rpm80.Z));
    }

    public Vector3 CalibrateAcceleration(Vector3 raw)
        => new(
            2f * (raw.X - AccelerationLow.X) / (AccelerationHigh.X - AccelerationLow.X) - 1f,
            2f * (raw.Y - AccelerationLow.Y) / (AccelerationHigh.Y - AccelerationLow.Y) - 1f,
            2f * (raw.Z - AccelerationLow.Z) / (AccelerationHigh.Z - AccelerationLow.Z) - 1f);

    public Vector3 CalibrateGyroscope(Vector3 raw)
        => raw * GyroscopeRadiansPerSecondPerUnit;

    private static int ReadUnsigned(ReadOnlySpan<byte> blob, int offset)
        => (blob[offset] | blob[offset + 1] << 8) - 0x8000;
}

public sealed record PsMoveDeviceDescriptor(
    string DevicePath,
    ushort VendorId,
    ushort ProductId,
    PsMoveTransport Transport,
    string Model,
    string? StableId)
{
    public const ushort SonyVendorId = 0x054C;
    public const ushort Zcm1ProductId = 0x03D5;

    public static bool TryCreate(
        string devicePath,
        ushort vendorId,
        ushort productId,
        out PsMoveDeviceDescriptor? descriptor,
        string? stableId = null)
    {
        descriptor = null;
        if (string.IsNullOrWhiteSpace(devicePath) || vendorId != SonyVendorId || productId != Zcm1ProductId)
            return false;

        descriptor = new(
            devicePath,
            vendorId,
            productId,
            InferTransport(devicePath),
            "PS Move CECH-ZCM1",
            stableId);
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
