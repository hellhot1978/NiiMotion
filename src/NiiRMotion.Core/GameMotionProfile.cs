namespace NiiRMotion.Core;

public sealed record GameMotionProfile(
    int SchemaVersion,
    string GameId,
    string MappingVersion,
    double SpeedMultiplier,
    double MaximumOutput,
    double Deadzone,
    double AccelerationPerSecond,
    double DecelerationPerSecond,
    string DirectionMode)
{
    public GameMotionProfile Safe() => this with
    {
        SpeedMultiplier = Math.Clamp(SpeedMultiplier, .25, 3), MaximumOutput = Math.Clamp(MaximumOutput, .2, 1), Deadzone = Math.Clamp(Deadzone, 0, .2),
        AccelerationPerSecond = Math.Clamp(AccelerationPerSecond, .5, 12), DecelerationPerSecond = Math.Clamp(DecelerationPerSecond, 2, 30)
    };

    public static GameMotionProfile Default(string gameId, string mappingVersion = "generic-steamvr-v1") => new(1, gameId, mappingVersion, 1, 1, 0, 3, 12, "GameNative");
}
