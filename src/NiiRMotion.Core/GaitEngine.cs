using System.Diagnostics;

namespace NiiRMotion.Core;

public enum LegSide { Left, Right }
public enum GaitState { Idle, Starting, Walking, FastWalk, Running, Stopping, TrackingDegraded }
public sealed record GaitSnapshot(GaitState State, double CadenceHz, double Confidence, double TargetSpeed, LegSide? LastStepSide, long StepCount);

public sealed class GaitEngine
{
    private readonly GaitPacePrior? _pacePrior;
    private readonly PersonalGaitPace? _personalPace;
    private readonly double _legStartThresholdDps, _legReleaseThresholdDps; private readonly long _refractoryTicks; private bool _leftAbove, _rightAbove; private long _leftRiseTicks, _rightRiseTicks; private double _leftRiseMagnitude, _rightRiseMagnitude, _leftSwingPeak, _rightSwingPeak; private long _lastStepTicks, _previousStepTicks, _lastLegMotionTicks; private LegSide? _lastSide; private long _steps; private int _alternatingSteps; private double _confidence; private double _phoneAgreement, _smoothedCadence, _smoothedSwingDps;
    public GaitEngine(double legStartThresholdDps = 56, TimeSpan? refractory = null, GaitPacePrior? pacePrior = null, PersonalGaitPace? personalPace = null)
    {
        _pacePrior = pacePrior;
        _personalPace = personalPace;
        _legStartThresholdDps = legStartThresholdDps;
        _legReleaseThresholdDps = legStartThresholdDps * 0.64;
        _refractoryTicks = (long)((refractory ?? TimeSpan.FromMilliseconds(250)).TotalSeconds * Stopwatch.Frequency);
    }
    public bool ObserveLeg(LegSide side, double angularVelocityMagnitudeDps, long timestampTicks)
    {
        // Step edges start locomotion safely, but continuous leg energy keeps it
        // alive between naturally uneven steps. This prevents cadence gaps from
        // becoming stop/start pulses while still allowing a prompt real stop.
        if (angularVelocityMagnitudeDps >= _legReleaseThresholdDps * .72) _lastLegMotionTicks = timestampTicks;
        var wasAbove = side == LegSide.Left ? _leftAbove : _rightAbove;
        // Separate start/release levels prevent noisy samples near the threshold
        // from turning one smooth swing into several short steps.
        var above = angularVelocityMagnitudeDps >= (wasAbove ? _legReleaseThresholdDps : _legStartThresholdDps);
        if (side == LegSide.Left) _leftAbove = above; else _rightAbove = above;
        if (above)
        {
            if (side == LegSide.Left) _leftSwingPeak = Math.Max(_leftSwingPeak, angularVelocityMagnitudeDps);
            else _rightSwingPeak = Math.Max(_rightSwingPeak, angularVelocityMagnitudeDps);
        }
        else if (wasAbove)
        {
            var completedPeak = side == LegSide.Left ? _leftSwingPeak : _rightSwingPeak;
            // The user's accepted fast-walk capture reaches ~386 dps at P95.
            // Keep those intentional swings while rejecting the >500 dps tail
            // that is dominated by strap/controller shocks.
            if (completedPeak is >= 40 and <= 500) _smoothedSwingDps = _smoothedSwingDps == 0 ? completedPeak : _smoothedSwingDps * 0.68 + completedPeak * 0.32;
            if (side == LegSide.Left) _leftSwingPeak = 0; else _rightSwingPeak = 0;
        }
        if (above && !wasAbove)
        {
            if (side == LegSide.Left) { _leftRiseTicks = timestampTicks; _leftRiseMagnitude = angularVelocityMagnitudeDps; }
            else { _rightRiseTicks = timestampTicks; _rightRiseMagnitude = angularVelocityMagnitudeDps; }
            var bilateralWindow = Stopwatch.Frequency * 0.12;
            if (_leftRiseTicks != 0 && _rightRiseTicks != 0 && Math.Abs(_leftRiseTicks - _rightRiseTicks) <= bilateralWindow)
            {
                // A strong simultaneous pair is a crouch/jump. A weaker second
                // edge is normal opposing-thigh motion, so ignore only that edge.
                if (_leftRiseMagnitude >= _legStartThresholdDps * 1.8 && _rightRiseMagnitude >= _legStartThresholdDps * 1.8)
                {
                    _confidence = 0; _alternatingSteps = 0; _lastSide = null; _lastStepTicks = 0; _previousStepTicks = 0;
                }
                return false;
            }
        }
        if (!above || wasAbove || (_lastStepTicks != 0 && timestampTicks - _lastStepTicks < _refractoryTicks)) return false;
        // A thigh-mounted Joy-Con can cross the threshold more than once during
        // one leg swing. Do not let same-leg rebound events break alternation;
        // permit a resync only after walking evidence has genuinely gone stale.
        if (_lastSide == side && _lastStepTicks != 0) return false;
        var alternates = _lastSide.HasValue && _lastSide.Value != side; _previousStepTicks = _lastStepTicks; _lastStepTicks = timestampTicks; _lastSide = side; _steps++;
        if (alternates && _previousStepTicks > 0)
        {
            var instantCadence = Stopwatch.Frequency / (double)(_lastStepTicks - _previousStepTicks);
            if (instantCadence is >= 0.8 and <= 4.0) _smoothedCadence = _smoothedCadence == 0 ? instantCadence : _smoothedCadence * 0.72 + instantCadence * 0.28;
        }
        if (alternates) _alternatingSteps++; else _alternatingSteps = Math.Max(0, _alternatingSteps - 1);
        _confidence = Math.Clamp(_confidence + (alternates ? 0.50 : 0.05) + _phoneAgreement * 0.08, 0, 1); return true;
    }
    public void ObservePhoneRhythm(double agreement) => _phoneAgreement = Math.Clamp(agreement, -1, 1); // context only; never creates a step
    public GaitSnapshot Update(long nowTicks)
    {
        var age = _lastStepTicks == 0 ? double.PositiveInfinity : (nowTicks - _lastStepTicks) / (double)Stopwatch.Frequency;
        var motionAge = _lastLegMotionTicks == 0 ? double.PositiveInfinity : (nowTicks - _lastLegMotionTicks) / (double)Stopwatch.Frequency;
        if (motionAge > 0.26 && double.IsFinite(motionAge)) _confidence = Math.Max(0, _confidence - Math.Min(0.10, motionAge * 0.03));
        var cadence = _smoothedCadence;
        var established = _alternatingSteps >= 1 && _confidence >= 0.35;
        var state = motionAge switch
        {
            > 0.68 => GaitState.Idle,
            > 0.36 when established => GaitState.Stopping,
            > 0.24 => GaitState.Idle,
            _ when _confidence < 0.35 || _alternatingSteps < 1 => GaitState.Idle,
            _ when _confidence < 0.68 => GaitState.Starting,
            _ when cadence >= 2.5 => GaitState.Running,
            _ when cadence >= 1.8 => GaitState.FastWalk,
            _ => GaitState.Walking
        };
        if (state == GaitState.Idle && motionAge > 0.68) { _confidence = 0; _lastSide = null; _alternatingSteps = 0; }
        // Cadence says how often steps occur; swing radius approximates how
        // forcefully the thigh traverses its phase portrait. Combining both
        // distinguishes slow short steps from energetic walking at similar rhythm.
        var cadenceFactor = Math.Clamp((cadence - 1.0) / 1.8, 0, 1);
        var swingFactor = Math.Clamp((_smoothedSwingDps - 50) / 130, 0, 1);
        var heuristicPace = Math.Clamp(0.56 + 0.62 * (cadenceFactor * 0.58 + swingFactor * 0.42), 0.58, 1);
        var learnedPace = _pacePrior?.EstimateAnalogPace(cadence, _smoothedSwingDps) ?? heuristicPace;
        var basePace = Math.Clamp(heuristicPace * 0.30 + learnedPace * 0.70, 0.50, 1);
        var naturalPace = _personalPace is null ? basePace : Math.Clamp(basePace * 0.20 + _personalPace.EstimateAnalog(_smoothedSwingDps) * 0.80, 0.50, 1);
        // Keep the three effort levels perceptually distinct. Cadence alone is
        // often similar when walking and jogging in place, while thigh swing
        // amplitude changes substantially. Preserve the personalized pace, but
        // give swing effort enough authority to avoid compressing every active
        // gait into the top end of the stick range.
        var fastWalkPace = Math.Clamp(naturalPace * 0.30 + (0.60 + 0.38 * swingFactor) * 0.70, 0.66, 0.98);
        var target = state switch
        {
            GaitState.Walking => Math.Clamp(naturalPace, 0.50, 0.72),
            GaitState.FastWalk => fastWalkPace,
            GaitState.Running => 1,
            GaitState.Starting => Math.Clamp(naturalPace * _confidence, 0.38, 0.58),
            _ => 0
        };
        return new(state, cadence, _confidence, target, _lastSide, _steps);
    }
}

public sealed class LocomotionSmoother
{
    public double OutputSpeed { get; private set; }
    public double Update(double targetSpeed, TimeSpan delta, double accelerationPerSecond = 1.9, double decelerationPerSecond = 2.2)
    {
        targetSpeed = Math.Clamp(targetSpeed, 0, 1); var maxChange = (targetSpeed > OutputSpeed ? accelerationPerSecond : decelerationPerSecond) * delta.TotalSeconds;
        OutputSpeed = Math.Abs(targetSpeed - OutputSpeed) <= maxChange ? targetSpeed : OutputSpeed + Math.Sign(targetSpeed - OutputSpeed) * maxChange; return OutputSpeed;
    }
    public void EmergencyZero() => OutputSpeed = 0;
}
