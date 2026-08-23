using System.Diagnostics;

namespace NiiRMotion.Core;

public sealed class PsMoveGaitEngine(PsMoveTrainingProfile profile)
{
    private readonly long _refractory = (long)(Stopwatch.Frequency * .22);
    private long _lastStep, _previousStep, _lastMotion;
    private LegSide? _lastSide;
    private int _alternations;
    private bool _established;
    private long _steps;
    private double _cadence, _peak, _confidence, _leftSpeed, _rightSpeed;
    private double _leftX, _leftY, _leftZ, _rightX, _rightY, _rightZ;
    private double _leftPlaneConfidence, _rightPlaneConfidence;
    private bool _leftStationary, _rightStationary;
    private long _leftSampleTicks, _rightSampleTicks, _bothStationarySince;

    public bool Observe(PsMoveImuSample sample) => Observe(sample.Side, sample.AngularVelocityRadps, sample.AccelerationG, sample.Timestamp.MonotonicTicks);

    public bool Observe(LegSide side, System.Numerics.Vector3 gyro, System.Numerics.Vector3 accelerationG, long ticks)
    {
        var stationary = gyro.Length() < profile.RestReleaseThresholdRadps
            && Math.Abs(accelerationG.Length() - 1) < .16;
        if (side == LegSide.Left) { _leftStationary = stationary; _leftSampleTicks = ticks; }
        else { _rightStationary = stationary; _rightSampleTicks = ticks; }

        var bothFresh = Math.Abs(_leftSampleTicks - _rightSampleTicks) < Stopwatch.Frequency * .08;
        if (bothFresh && _leftStationary && _rightStationary)
        {
            _bothStationarySince = _bothStationarySince == 0 ? ticks : _bothStationarySince;
            if (_established && ticks - _bothStationarySince >= Stopwatch.Frequency * .14) ResetEvidence();
        }
        else _bothStationarySince = 0;

        return Observe(side, gyro, ticks);
    }

    public bool Observe(LegSide side, System.Numerics.Vector3 gyro, long ticks)
    {
        var x = Math.Abs(gyro.X); var y = Math.Abs(gyro.Y); var z = Math.Abs(gyro.Z);
        ref var sx = ref (side == LegSide.Left ? ref _leftX : ref _rightX);
        ref var sy = ref (side == LegSide.Left ? ref _leftY : ref _rightY);
        ref var sz = ref (side == LegSide.Left ? ref _leftZ : ref _rightZ);
        sx = sx * .90 + x * .10; sy = sy * .90 + y * .10; sz = sz * .90 + z * .10;
        var ratio = sx / Math.Max(.01, sy);
        // Owner recordings show walking in a balanced X/Y calf-rotation plane.
        // Turns are Y-dominant; bilateral bends are X-dominant.
        var rawGaitPlane = ratio is >= .58 and <= 1.35 && sz <= Math.Max(.18, sy * .78);
        ref var planeConfidence = ref (side == LegSide.Left ? ref _leftPlaneConfidence : ref _rightPlaneConfidence);
        planeConfidence = planeConfidence * .94 + (rawGaitPlane ? .06 : 0);
        var gaitPlane = rawGaitPlane && planeConfidence >= .25;
        return Observe(side, gaitPlane ? gyro.Length() : 0, ticks);
    }

    public bool Observe(LegSide side, double angularSpeedRadps, long ticks)
    {
        if (side == LegSide.Left) _leftSpeed = angularSpeedRadps; else _rightSpeed = angularSpeedRadps;
        var strongest = Math.Max(_leftSpeed, _rightSpeed);
        if (strongest >= profile.RestReleaseThresholdRadps) _lastMotion = ticks;
        _peak = Math.Max(_peak, strongest);
        // Foot impact appears in both calf sensors. A real step is represented by
        // which leg dominates, and walking requires that dominance to alternate.
        var difference = _leftSpeed - _rightSpeed;
        var candidate = Math.Abs(difference) >= profile.GaitActivationThresholdRadps * .55 && strongest >= profile.GaitActivationThresholdRadps
            ? difference > 0 ? LegSide.Left : LegSide.Right
            : (LegSide?)null;
        if (candidate is null) return false;
        side = candidate.Value;
        if (_lastStep > 0 && ticks - _lastStep < _refractory) return false;
        if (_lastSide == side) return false;
        var alternates = _lastSide.HasValue;
        _previousStep = _lastStep; _lastStep = ticks; _lastSide = side; _steps++;
        if (alternates && _previousStep > 0)
        {
            var instant = Stopwatch.Frequency / (double)(_lastStep - _previousStep);
            if (instant is >= .75 and <= 4.6)
            {
                _cadence = _cadence == 0 ? instant : _cadence * .7 + instant * .3;
                _alternations++;
                _confidence = Math.Clamp(_confidence + .50, 0, 1);
                if (_alternations >= 1 && _confidence >= .50) _established = true;
            }
            else ResetEvidence();
        }
        return alternates;
    }

    public GaitSnapshot Update(long ticks)
    {
        var motionAge = _lastMotion == 0 ? double.PositiveInfinity : (ticks - _lastMotion) / (double)Stopwatch.Frequency;
        var stepAge = _lastStep == 0 ? double.PositiveInfinity : (ticks - _lastStep) / (double)Stopwatch.Frequency;
        var cadenceGrace = _cadence <= 0 ? .48 : Math.Clamp(1.12 / _cadence, .28, .48);
        // Confidence used to be reduced once per 10 ms output frame. That made it
        // collapse between two perfectly normal 2-3 Hz steps and produced short
        // zero-output gaps. Once bilateral alternation is established, preserve it
        // across one natural step interval and then stop decisively.
        var state = !_established || stepAge > cadenceGrace ? GaitState.Idle : motionAge switch
        {
            _ when _cadence >= 2.6 => GaitState.Running,
            _ when _cadence >= 1.85 => GaitState.FastWalk,
            _ => GaitState.Walking
        };
        if (state == GaitState.Idle && stepAge > cadenceGrace) ResetEvidence();
        var effort = Sigmoid(Normalize(_peak, profile.SlowAnchorRadps, profile.FastAnchorRadps));
        var target = state switch
        {
            GaitState.Walking => .48 + effort * .22,
            GaitState.FastWalk => .68 + effort * .24,
            GaitState.Running => .90 + effort * .10,
            _ => 0
        };
        return new(state, _cadence, _confidence, Math.Clamp(target, 0, 1), _lastSide, _steps);
    }

    private void ResetEvidence() { _alternations = 0; _confidence = 0; _established = false; _lastSide = null; _lastStep = _previousStep = 0; _peak = 0; _bothStationarySince = 0; }
    private static double Normalize(double value, double low, double high) => Math.Clamp((value - low) / Math.Max(.01, high - low), 0, 1);
    private static double Sigmoid(double x) { var y = 1 / (1 + Math.Exp(-7 * (x - .5))); var lo = 1 / (1 + Math.Exp(3.5)); var hi = 1 / (1 + Math.Exp(-3.5)); return (y - lo) / (hi - lo); }
}
