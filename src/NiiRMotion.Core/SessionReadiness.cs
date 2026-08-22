namespace NiiRMotion.Core;

public enum ReadinessState { Ready, Degraded, NotReady }
public sealed record SessionReadiness(ReadinessState State, string Message, IReadOnlyList<DeviceStatus> BlockingDevices);
public static class SessionReadinessEvaluator
{
    public static SessionReadiness Evaluate(MotionProfile profile, IReadOnlyCollection<DeviceStatus> devices)
    {
        var byKind = devices.ToDictionary(x => x.Kind);
        var blocking = profile.Required.Where(k => !byKind.TryGetValue(k, out var s) || !s.IsConnected)
            .Select(k => byKind.TryGetValue(k, out var s) ? s : new DeviceStatus(k, k.ToString(), DeviceState.Missing, "Tarama sonucu yok.", "Cihazı bağlayın ve tekrar tarayın."))
            .ToArray();
        if (blocking.Length > 0) return new(ReadinessState.NotReady, $"{blocking.Length} zorunlu bileşen eksik.", blocking);
        var optionalMissing = profile.Optional.Count(k => !byKind.TryGetValue(k, out var s) || (!s.IsConnected && s.State != DeviceState.Configured));
        return optionalMissing > 0
            ? new(ReadinessState.Degraded, "Oturum desteklenen düşük kapsamlı modda çalışabilir.", Array.Empty<DeviceStatus>())
            : new(ReadinessState.Ready, "Tüm profil bileşenleri hazır.", Array.Empty<DeviceStatus>());
    }
}
