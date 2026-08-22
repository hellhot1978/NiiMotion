namespace NiiRMotion.Core;

public sealed record RegressionCase(string ProfileId, string Scenario, bool Passed, string Detail);
public sealed record NonHmdRegressionReport(DateTimeOffset CompletedAtUtc, IReadOnlyList<RegressionCase> Cases)
{
    public bool Passed => Cases.All(x => x.Passed);
}

public static class NonHmdRegressionMatrix
{
    public static NonHmdRegressionReport Run()
    {
        var cases = new List<RegressionCase>();
        for (var mask = 0; mask < 16; mask++)
        {
            var inventory = new UserHardwareInventory(1, (mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0, (mask & 8) != 0, true, DateTimeOffset.UtcNow);
            foreach (var recommendation in MotionProfileCatalog.For(inventory))
            {
                var profile = recommendation.Profile;
                var ready = profile.Required.Select(Connected).Concat(profile.Optional.Select(OptionalReady)).ToArray();
                var result = SessionReadinessEvaluator.Evaluate(profile, ready);
                cases.Add(new(profile.Id, "all-required-ready", result.State == ReadinessState.Ready, result.Message));
                foreach (var required in profile.Required)
                {
                    var missing = ready.Where(x => x.Kind != required).Append(new DeviceStatus(required, required.ToString(), DeviceState.Missing, "simulated", "")).ToArray();
                    var blocked = SessionReadinessEvaluator.Evaluate(profile, missing);
                    cases.Add(new(profile.Id, $"missing-{required}", blocked.State == ReadinessState.NotReady, blocked.Message));
                }
                cases.Add(new(profile.Id, "locomotion-policy", profile.Id != MotionProfile.ClassicVr.Id || !profile.LocomotionAllowed, profile.LocomotionAllowed ? "Locomotion enabled" : "Locomotion disabled"));
            }
        }
        return new(DateTimeOffset.UtcNow, cases);
    }

    private static DeviceStatus Connected(DeviceKind kind) => new(kind, kind.ToString(), DeviceState.Connected, "simulated", "");
    private static DeviceStatus OptionalReady(DeviceKind kind) => kind == DeviceKind.HandTracking
        ? new(kind, kind.ToString(), DeviceState.Configured, "simulated", "") : Connected(kind);
}
