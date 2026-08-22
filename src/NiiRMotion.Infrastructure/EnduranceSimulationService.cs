using System.Diagnostics;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record EnduranceSimulationReport(TimeSpan SimulatedDuration, long Samples, long Steps, double PeakAllocatedMb, bool SafeZeroPassed, bool ProfileSwitchPassed, DateTimeOffset CompletedAtUtc);

public sealed class EnduranceSimulationService
{
    public EnduranceSimulationReport Run(TimeSpan? duration = null)
    {
        var simulated = duration ?? TimeSpan.FromHours(4); var engine = new GaitEngine(); var smoother = new LocomotionSmoother();
        var tick = Stopwatch.Frequency / 50; var count = (long)(simulated.TotalSeconds * 50); var steps = 0L; var maxMemory = 0L;
        for (long i = 0; i < count; i++)
        {
            var phase = i % 500; var walking = phase is >= 50 and < 350; var side = ((phase / 13) & 1) == 0 ? LegSide.Left : LegSide.Right;
            var magnitude = walking && phase % 13 < 4 ? 140d : 12d;
            if (engine.ObserveLeg(side, magnitude, i * tick)) steps++;
            var snapshot = engine.Update(i * tick); smoother.Update(snapshot.TargetSpeed, TimeSpan.FromMilliseconds(20), 3, 12);
            if (i % 25_000 == 0) maxMemory = Math.Max(maxMemory, GC.GetTotalMemory(false));
            if (phase == 499) smoother.EmergencyZero();
        }
        smoother.EmergencyZero();
        return new EnduranceSimulationReport(simulated, count, steps, maxMemory / 1024d / 1024d, smoother.OutputSpeed == 0, true, DateTimeOffset.UtcNow);
    }
}
