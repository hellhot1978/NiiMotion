using System.Buffers.Binary;
using System.Numerics;

namespace NiiRMotion.Core;

public enum OwoTrackPacketType { Heartbeat = 0, Rotation = 1, Gyroscope = 2, Handshake = 3, Acceleration = 4, PingPong = 10 }
public readonly record struct OwoTrackPacket(OwoTrackPacketType Type, long Sequence, Quaternion Rotation, Vector3 Vector);

public static class OwoTrackPacketParser
{
    public static bool TryParse(ReadOnlySpan<byte> bytes, out OwoTrackPacket packet)
    {
        packet = default; if (bytes.Length < 12) return false;
        var type = (OwoTrackPacketType)BinaryPrimitives.ReadInt32BigEndian(bytes); var sequence = BinaryPrimitives.ReadInt64BigEndian(bytes[4..]);
        if (type == OwoTrackPacketType.Rotation && bytes.Length >= 28) { packet = new(type, sequence, new(ReadFloat(bytes[12..]), ReadFloat(bytes[16..]), ReadFloat(bytes[20..]), ReadFloat(bytes[24..])), default); return true; }
        if ((type == OwoTrackPacketType.Gyroscope || type == OwoTrackPacketType.Acceleration) && bytes.Length >= 24) { packet = new(type, sequence, default, new(ReadFloat(bytes[12..]), ReadFloat(bytes[16..]), ReadFloat(bytes[20..]))); return true; }
        if (type is OwoTrackPacketType.Handshake or OwoTrackPacketType.Heartbeat or OwoTrackPacketType.PingPong) { packet = new(type, sequence, default, default); return true; }
        return false;
    }
    private static float ReadFloat(ReadOnlySpan<byte> bytes) => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes));
}
