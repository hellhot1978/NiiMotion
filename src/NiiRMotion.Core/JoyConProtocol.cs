using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;

namespace NiiRMotion.Core;

public enum JoyConSide { Left, Right }
public sealed record JoyConImuCalibration(Vector3 AccelOrigin, Vector3 AccelSensitivity, Vector3 GyroOrigin, Vector3 GyroSensitivity)
{
    public static JoyConImuCalibration ParseFactory(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24) throw new ArgumentException("Joy-Con IMU calibration requires 24 bytes.", nameof(bytes));
        return new(ReadVector(bytes), ReadVector(bytes[6..]), ReadVector(bytes[12..]), ReadVector(bytes[18..]));
    }
    private static Vector3 ReadVector(ReadOnlySpan<byte> b) => new(BinaryPrimitives.ReadInt16LittleEndian(b), BinaryPrimitives.ReadInt16LittleEndian(b[2..]), BinaryPrimitives.ReadInt16LittleEndian(b[4..]));
    public Vector3 ConvertAcceleration(Vector3 raw) => Scale(raw, AccelOrigin, AccelSensitivity, 4f);
    public Vector3 ConvertAngularVelocity(Vector3 raw) => Scale(raw, GyroOrigin, GyroSensitivity, 936f);
    private static Vector3 Scale(Vector3 raw, Vector3 origin, Vector3 sensitivity, float range) => new(
        (raw.X - origin.X) * range / (sensitivity.X - origin.X),
        (raw.Y - origin.Y) * range / (sensitivity.Y - origin.Y),
        (raw.Z - origin.Z) * range / (sensitivity.Z - origin.Z));
}
public sealed record JoyConDeviceDescriptor(string DevicePath, ushort VendorId, ushort ProductId, JoyConSide Side)
{
    public const ushort NintendoVendorId = 0x057E;
    public const ushort LeftProductId = 0x2006;
    public const ushort RightProductId = 0x2007;
    public static bool TryCreate(string path, ushort vendorId, ushort productId, out JoyConDeviceDescriptor? descriptor)
    {
        var side = productId switch { LeftProductId => JoyConSide.Left, RightProductId => JoyConSide.Right, _ => (JoyConSide?)null };
        descriptor = vendorId == NintendoVendorId && side.HasValue ? new(path, vendorId, productId, side.Value) : null;
        return descriptor is not null;
    }
}

public static class JoyConReportParser
{
    public const byte StandardFullReportId = 0x30;
    public const int ReportLength = 49;
    private const int ImuOffset = 13;
    private const int ImuStride = 12;
    public static IReadOnlyList<JoyConImuSample> ParseStandardFullReport(ReadOnlySpan<byte> report, string sourceId, long firstSequence, long receivedTicks, DateTimeOffset receivedAtUtc, JoyConImuCalibration? calibration = null)
    {
        if (report.Length < ReportLength) throw new ArgumentException($"Joy-Con 0x30 report must be at least {ReportLength} bytes.", nameof(report));
        if (report[0] != StandardFullReportId) throw new ArgumentException("Unsupported Joy-Con report id.", nameof(report));
        var samples = new JoyConImuSample[3];
        var tickStep = Stopwatch.Frequency / 200; // reports contain three ~5 ms IMU samples
        for (var i = 0; i < samples.Length; i++)
        {
            var offset = ImuOffset + i * ImuStride;
            var rawAccel = ReadVector(report.Slice(offset, 6)); var rawGyro = ReadVector(report.Slice(offset + 6, 6));
            var accel = calibration?.ConvertAcceleration(rawAccel) ?? rawAccel / 4096f;
            var gyro = calibration?.ConvertAngularVelocity(rawGyro) ?? rawGyro * 0.070f;
            samples[i] = new(sourceId, firstSequence + i, new(receivedTicks - (2 - i) * tickStep, receivedAtUtc), accel, gyro, i);
        }
        return samples;
    }
    private static Vector3 ReadVector(ReadOnlySpan<byte> bytes) => new(BinaryPrimitives.ReadInt16LittleEndian(bytes), BinaryPrimitives.ReadInt16LittleEndian(bytes[2..]), BinaryPrimitives.ReadInt16LittleEndian(bytes[4..]));
}
