namespace NiiRMotion.Core;

public sealed record ProfileFallbackRecommendation(MotionProfile Profile, string Reason);

public static class ProfileFallbackAdvisor
{
    private static readonly HashSet<DeviceKind> MotionDevices =
    [
        DeviceKind.JoyConLeft, DeviceKind.JoyConRight, DeviceKind.PsMoveLeft, DeviceKind.PsMoveRight,
        DeviceKind.Phone, DeviceKind.BalanceBoard
    ];

    public static ProfileFallbackRecommendation? Find(
        MotionProfile selected,
        IEnumerable<ProfileRecommendation> candidates,
        IReadOnlyCollection<DeviceStatus> devices)
    {
        if (!selected.LocomotionAllowed) return null;
        var connected = devices.Where(x => x.IsConnected).Select(x => x.Kind).ToHashSet();
        var selectedMotion = selected.Required.Where(MotionDevices.Contains).ToHashSet();
        if (selectedMotion.All(connected.Contains)) return null;

        var fallback = candidates
            .Where(x => x.Profile.LocomotionAllowed && x.Profile.Id != selected.Id)
            .Where(x => x.Profile.Required.Where(MotionDevices.Contains).All(connected.Contains))
            .Where(x => x.Profile.Required.Any(MotionDevices.Contains))
            .Where(x => x.Profile.Required.Where(MotionDevices.Contains).All(selectedMotion.Contains))
            .OrderBy(x => x.Experimental)
            .ThenByDescending(x => x.PerformanceScore)
            .ThenBy(x => x.Profile.Required.Count(MotionDevices.Contains))
            .FirstOrDefault();

        return fallback is null
            ? null
            : new(fallback.Profile, $"{selected.Name} için gereken bir sensör bağlı değil; {fallback.Profile.Name} bağlı cihazlarla kullanılabilir.");
    }
}
