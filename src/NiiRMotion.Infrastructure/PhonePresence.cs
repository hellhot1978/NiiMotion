using System.Diagnostics;

namespace NiiRMotion.Infrastructure;

public static class PhonePresence
{
    private static readonly object Sync = new();
    private static long _lastSampleTicks;
    private static string _endpoint = "";

    public static void Mark(string? endpoint)
    {
        lock (Sync)
        {
            _lastSampleTicks = Stopwatch.GetTimestamp();
            if (!string.IsNullOrWhiteSpace(endpoint)) _endpoint = endpoint;
        }
    }

    public static bool TryGetFresh(out string endpoint, TimeSpan? maximumAge = null)
    {
        lock (Sync)
        {
            endpoint = _endpoint;
            if (_lastSampleTicks == 0) return false;
            var age = Stopwatch.GetElapsedTime(_lastSampleTicks);
            return age <= (maximumAge ?? TimeSpan.FromMilliseconds(850));
        }
    }
}
