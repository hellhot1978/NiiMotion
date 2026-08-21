using System.Numerics;
using System.Text.Json.Serialization;

namespace NiiRMotion.Core;

public sealed record PhonePacket(
    [property: JsonPropertyName("protocolVersion")] int ProtocolVersion,
    [property: JsonPropertyName("sessionToken")] string SessionToken,
    [property: JsonPropertyName("deviceId")] string DeviceId,
    [property: JsonPropertyName("sequence")] long Sequence,
    [property: JsonPropertyName("sentAtUnixMicroseconds")] long SentAtUnixMicroseconds,
    [property: JsonPropertyName("orientation")] float[] Orientation,
    [property: JsonPropertyName("accelerationMps2")] float[] AccelerationMps2,
    [property: JsonPropertyName("angularVelocityRadps")] float[] AngularVelocityRadps)
{
    public const int CurrentProtocolVersion = 1;
    public PhoneImuSample ToSample(long receivedTicks, DateTimeOffset receivedUtc)
    {
        if (ProtocolVersion != CurrentProtocolVersion) throw new InvalidDataException($"Unsupported phone protocol {ProtocolVersion}.");
        if (Orientation.Length != 4 || AccelerationMps2.Length != 3 || AngularVelocityRadps.Length != 3) throw new InvalidDataException("Invalid phone vector dimensions.");
        var q = Quaternion.Normalize(new(Orientation[0], Orientation[1], Orientation[2], Orientation[3]));
        return new($"phone:{DeviceId}", Sequence, new(receivedTicks, receivedUtc), SentAtUnixMicroseconds, q, new(AccelerationMps2[0], AccelerationMps2[1], AccelerationMps2[2]), new(AngularVelocityRadps[0], AngularVelocityRadps[1], AngularVelocityRadps[2]));
    }
}

public sealed class SequenceDiagnostics
{
    private long? _last; public long Received { get; private set; } public long Missing { get; private set; } public long OutOfOrder { get; private set; }
    public void Observe(long sequence)
    {
        Received++; if (_last.HasValue) { if (sequence <= _last) OutOfOrder++; else if (sequence > _last + 1) Missing += sequence - _last.Value - 1; }
        if (!_last.HasValue || sequence > _last) _last = sequence;
    }
}
