using System.Numerics;

namespace NiiRMotion.Core;

public readonly record struct HmdPoseSample(
    string SourceId,
    long Sequence,
    SensorTimestamp Timestamp,
    bool IsTracked,
    Vector3 PositionMeters,
    Quaternion Orientation,
    float YawRadians,
    float YawRateRadiansPerSecond) : ISensorSample;

public interface IHmdPoseSource : ISensorSource<HmdPoseSample> { }
