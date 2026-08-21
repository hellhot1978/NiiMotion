using System.Buffers.Binary;

namespace NiiRMotion.Core;

public readonly record struct BalanceBoardSensorCalibration(ushort ZeroKg, ushort SeventeenKg, ushort ThirtyFourKg)
{
    public float ConvertToKg(ushort raw)
    {
        if (raw <= ZeroKg) return 0;
        if (raw < SeventeenKg)
        {
            var span = SeventeenKg - ZeroKg;
            return span == 0 ? 0 : 17f * (raw - ZeroKg) / span;
        }
        var upperSpan = ThirtyFourKg - SeventeenKg;
        return upperSpan == 0 ? 17 : Math.Clamp(17f + 17f * (raw - SeventeenKg) / upperSpan, 0, 68);
    }
}

public sealed record BalanceBoardCalibration(
    BalanceBoardSensorCalibration TopRight,
    BalanceBoardSensorCalibration BottomRight,
    BalanceBoardSensorCalibration TopLeft,
    BalanceBoardSensorCalibration BottomLeft)
{
    public static BalanceBoardCalibration Parse(ReadOnlySpan<byte> memoryBlock)
    {
        if (memoryBlock.Length < 28) throw new ArgumentException("Balance Board calibration block must contain at least 28 bytes.", nameof(memoryBlock));
        static ushort Read(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
        return new(
            new(Read(memoryBlock, 4), Read(memoryBlock, 12), Read(memoryBlock, 20)),
            new(Read(memoryBlock, 6), Read(memoryBlock, 14), Read(memoryBlock, 22)),
            new(Read(memoryBlock, 8), Read(memoryBlock, 16), Read(memoryBlock, 24)),
            new(Read(memoryBlock, 10), Read(memoryBlock, 18), Read(memoryBlock, 26)));
    }
}

public readonly record struct BalanceBoardRawPacket(ushort TopRight, ushort BottomRight, ushort TopLeft, ushort BottomLeft, byte Temperature, byte BatteryLevel)
{
    public BalanceBoardSample ToSample(BalanceBoardCalibration calibration, string sourceId, long sequence, SensorTimestamp timestamp) =>
        new(sourceId, sequence, timestamp,
            calibration.TopLeft.ConvertToKg(TopLeft), calibration.TopRight.ConvertToKg(TopRight),
            calibration.BottomLeft.ConvertToKg(BottomLeft), calibration.BottomRight.ConvertToKg(BottomRight));
}

public static class BalanceBoardPacketParser
{
    public static BalanceBoardRawPacket ParseExtensionPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 11) throw new ArgumentException("Balance Board extension payload must contain 11 bytes.", nameof(payload));
        return new(
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(0, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(2, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(4, 2)),
            BinaryPrimitives.ReadUInt16BigEndian(payload.Slice(6, 2)),
            payload[8], payload[10]);
    }
}
