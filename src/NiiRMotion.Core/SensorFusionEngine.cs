using System.Diagnostics;

namespace NiiRMotion.Core;

public sealed record FusionSnapshot(
    GaitSnapshot Gait,
    double GlobalConfidence,
    double TargetSpeed,
    bool PhoneFresh,
    bool BoardFresh,
    bool BoardContact,
    double BoardTransferVelocity,
    double TurnTarget = 0,
    double BoardCopX = 0,
    double BoardTotalKg = 0);

public sealed class SensorFusionEngine
{
    private readonly GaitEngine _gait;
    private readonly long _optionalFreshTicks;
    private long _lastPhoneTicks;
    private long _lastBoardTicks;
    private float _lastBoardCopX;
    private double _boardTransferVelocity;
    private double _boardTotalKg;
    private bool _boardContact;
    private double _phoneAgreement;
    private double _phonePace;
    private double _phoneTurnRate;
    private double _lastActiveTarget;
    private readonly PersonalPhoneMotion? _phoneProfile;
    private readonly PersonalBoardMotion? _boardProfile;
    private readonly bool _allowPhoneOnly;
    private readonly bool _allowBoardOnly;
    private readonly bool _allowBoardTurn;
    private int _boardSide;
    private long _lastBoardStepTicks;
    private long _lastBoardCandidateTicks;
    private long _boardMotionStartedTicks;
    private double _boardCadenceHz;
    private long _boardStepCount;
    private int _boardLeanSide;
    private long _boardLeanStartedTicks;
    private double _boardTurnTarget;
    private long _phoneMotionStartedTicks;
    private long _lastPhoneMotionTicks;

    public SensorFusionEngine(double legStartThresholdDps = 56, TimeSpan? optionalFreshness = null, GaitPacePrior? pacePrior = null, PersonalGaitPace? personalPace = null, PersonalPhoneMotion? phoneProfile = null, PersonalBoardMotion? boardProfile = null, bool allowPhoneOnly = false, bool allowBoardOnly = false, bool allowBoardTurn = false)
    {
        _gait = new GaitEngine(legStartThresholdDps, pacePrior: pacePrior, personalPace: personalPace);
        _phoneProfile = phoneProfile;
        _boardProfile = boardProfile;
        _allowPhoneOnly = allowPhoneOnly;
        _allowBoardOnly = allowBoardOnly;
        _allowBoardTurn = allowBoardTurn;
        _optionalFreshTicks = (long)((optionalFreshness ?? TimeSpan.FromMilliseconds(350)).TotalSeconds * Stopwatch.Frequency);
    }

    public bool ObserveLeg(LegSide side, double angularVelocityMagnitudeDps, long timestampTicks) =>
        _gait.ObserveLeg(side, angularVelocityMagnitudeDps, timestampTicks);

    public void ObservePhoneRhythm(double agreement, long timestampTicks)
    {
        var observed = Math.Clamp(agreement, -1, 1);
        _phoneAgreement = _lastPhoneTicks == 0 ? observed : _phoneAgreement * 0.82 + observed * 0.18;
        _lastPhoneTicks = timestampTicks;
        _gait.ObservePhoneRhythm(_phoneAgreement);
    }

    public void ObservePhoneMotion(double gyroRadps, double accelMps2, long timestampTicks, double verticalTurnRadps = 0)
    {
        var estimate = _phoneProfile?.Estimate(gyroRadps, accelMps2) ?? (Math.Clamp(gyroRadps / 2.5, 0, 1), 0.0);
        _phonePace = estimate.Item2;
        if (estimate.Item1 >= .55)
        {
            if (_phoneMotionStartedTicks == 0) _phoneMotionStartedTicks = timestampTicks;
            _lastPhoneMotionTicks = timestampTicks;
        }
        else if (_lastPhoneMotionTicks > 0 && timestampTicks - _lastPhoneMotionTicks > Stopwatch.Frequency * .38) _phoneMotionStartedTicks = 0;
        _phoneTurnRate = _lastPhoneTicks == 0 ? Math.Abs(verticalTurnRadps) : _phoneTurnRate * .78 + Math.Abs(verticalTurnRadps) * .22;
        ObservePhoneRhythm(estimate.Item1, timestampTicks);
    }

    public void ObserveBoard(BalanceBoardSample sample)
    {
        if (_lastBoardTicks > 0 && sample.Timestamp.MonotonicTicks > _lastBoardTicks)
        {
            var seconds = (sample.Timestamp.MonotonicTicks - _lastBoardTicks) / (double)Stopwatch.Frequency;
            _boardTransferVelocity = (sample.CenterOfPressureX - _lastBoardCopX) / seconds;
        }
        _lastBoardCopX = sample.CenterOfPressureX;
        _boardTotalKg = sample.TotalKg;
        _lastBoardTicks = sample.Timestamp.MonotonicTicks;
        _boardContact = sample.HasStableContact((float)(_boardProfile?.ContactKg ?? 10));
        var leftThreshold = _boardProfile?.LeftStepThreshold ?? -.035;
        var rightThreshold = _boardProfile?.RightStepThreshold ?? .035;
        var inTurnPosture = _boardProfile is not null && sample.CenterOfPressureY > _boardProfile.TurnCopYThreshold;
        if (_boardContact && !inTurnPosture && (sample.CenterOfPressureX <= leftThreshold || sample.CenterOfPressureX >= rightThreshold))
        {
            var side = sample.CenterOfPressureX < 0 ? -1 : 1;
            if (_boardSide != 0 && side != _boardSide)
            {
                var interval = _lastBoardCandidateTicks == 0 ? 0 : (sample.Timestamp.MonotonicTicks - _lastBoardCandidateTicks) / (double)Stopwatch.Frequency;
                _lastBoardCandidateTicks = sample.Timestamp.MonotonicTicks;
                if (interval is >= .24 and <= 1.80)
                {
                    _boardCadenceHz = _boardCadenceHz == 0 ? 1 / interval : _boardCadenceHz * .72 + (1 / interval) * .28;
                    _boardStepCount++;
                    if (_boardMotionStartedTicks == 0) _boardMotionStartedTicks = sample.Timestamp.MonotonicTicks;
                    _lastBoardStepTicks = sample.Timestamp.MonotonicTicks;
                }
            }
            else if (_boardSide == 0) _lastBoardCandidateTicks = sample.Timestamp.MonotonicTicks;
            _boardSide = side;
        }
        if (_lastBoardStepTicks > 0 && sample.Timestamp.MonotonicTicks - _lastBoardStepTicks > Stopwatch.Frequency * 2.0)
        {
            _boardMotionStartedTicks = 0;
            _boardCadenceHz = 0;
        }
        var turnLeft = _boardProfile?.TurnLeftThreshold ?? -.50;
        var turnRight = _boardProfile?.TurnRightThreshold ?? .50;
        var leanSide = sample.CenterOfPressureX <= turnLeft ? -1 : sample.CenterOfPressureX >= turnRight ? 1 : 0;
        if (_allowBoardTurn && _boardContact && _boardMotionStartedTicks == 0 && leanSide != 0)
        {
            if (_boardLeanSide != leanSide)
            {
                _boardLeanSide = leanSide;
                _boardLeanStartedTicks = sample.Timestamp.MonotonicTicks;
            }
            var heldSeconds = (sample.Timestamp.MonotonicTicks - _boardLeanStartedTicks) / (double)Stopwatch.Frequency;
            _boardTurnTarget = heldSeconds >= (_boardProfile?.TurnHoldSeconds ?? .55)
                ? leanSide * (_boardProfile?.TurnSpeed ?? .65)
                : 0;
        }
        else
        {
            _boardLeanSide = 0;
            _boardLeanStartedTicks = 0;
            _boardTurnTarget = 0;
        }
    }

    public FusionSnapshot Update(long nowTicks)
    {
        var gait = _gait.Update(nowTicks);
        var phoneFresh = IsFresh(_lastPhoneTicks, nowTicks);
        var boardFresh = IsFresh(_lastBoardTicks, nowTicks);
        var confidence = gait.Confidence;

        if (phoneFresh) confidence *= 1 + 0.08 * _phoneAgreement;
        if (boardFresh) confidence *= _boardContact
            ? 1 + Math.Min(0.12, Math.Abs(_boardTransferVelocity) * 0.03)
            : 0.65;

        confidence = Math.Clamp(confidence, 0, 1);
        var gaitActive = gait.State is GaitState.Starting or GaitState.Walking or GaitState.FastWalk or GaitState.Running;
        var target = gaitActive && confidence >= 0.35 ? gait.TargetSpeed : 0;
        if (target > 0 && phoneFresh && _phoneProfile is not null) target = Math.Clamp(target * .85 + _phonePace * .15, 0, 1);
        if (_allowPhoneOnly && phoneFresh && _phoneProfile is not null)
        {
            var sustained = _phoneMotionStartedTicks > 0 && nowTicks - _phoneMotionStartedTicks >= Stopwatch.Frequency * .35;
            var recent = _lastPhoneMotionTicks > 0 && nowTicks - _lastPhoneMotionTicks <= Stopwatch.Frequency * .38;
            target = sustained && recent && _phoneTurnRate < 1.25 ? Math.Clamp(_phonePace, .58, 1) : 0;
            var phoneState = target > 0 ? GaitState.Walking : sustained ? GaitState.Stopping : GaitState.Idle;
            gait = new(phoneState, 0, Math.Clamp(_phoneAgreement, 0, 1), target, null, gait.StepCount);
            confidence = gait.Confidence;
        }
        if (_allowBoardOnly)
        {
            var stepHoldSeconds = Math.Clamp(2.40 / Math.Max(.01, _boardCadenceHz), 1.20, 1.80);
            var recent = boardFresh && _boardContact && _lastBoardStepTicks > 0 && nowTicks - _lastBoardStepTicks <= Stopwatch.Frequency * stepHoldSeconds;
            var sustained = _boardMotionStartedTicks > 0 && nowTicks - _boardMotionStartedTicks >= Stopwatch.Frequency * .28;
            target = recent && sustained ? (_boardProfile?.SpeedFor(_boardCadenceHz) ?? Math.Clamp(.40 + _boardCadenceHz * .28, .52, 1)) : 0;
            var boardState = target > 0 ? (_boardCadenceHz > 2.1 ? GaitState.FastWalk : GaitState.Walking) : sustained ? GaitState.Stopping : GaitState.Idle;
            confidence = recent ? Math.Clamp(.45 + Math.Abs(_boardTransferVelocity) * .04, .45, .9) : 0;
            gait = new(boardState, _boardCadenceHz, confidence, target, null, _boardStepCount);
        }
        if (boardFresh && !_boardContact) target = 0;
        if (target > 0) _lastActiveTarget = target;
        // Portrait chest phone: local Y is vertical. A sustained yaw means the
        // user is turning in place, not asking for forward locomotion. Ordinary
        // chest sway while walking reaches roughly 0.4-0.9 rad/s, so only reject
        // an unmistakable turn; the lower threshold caused rhythmic cut-outs.
        if (phoneFresh && _phoneProfile is not null && _phoneTurnRate >= 1.25) target = 0;
        var turnTarget = _allowBoardTurn && boardFresh && _boardContact ? _boardTurnTarget : 0;
        if (turnTarget != 0) target = 0;
        return new(gait, confidence, target, phoneFresh, boardFresh, boardFresh && _boardContact, _boardTransferVelocity, turnTarget, _lastBoardCopX, _boardTotalKg);
    }

    private bool IsFresh(long sampleTicks, long nowTicks) => sampleTicks > 0 && nowTicks >= sampleTicks && nowTicks - sampleTicks <= _optionalFreshTicks;
}

public sealed class VrLocomotionSession : IAsyncDisposable
{
    private readonly VrOutputController _output;
    private readonly LocomotionSmoother _smoother = new();
    private readonly LocomotionSmoother _turnSmoother = new();
    private readonly double _speedMultiplier;
    private bool _started;

    public VrLocomotionSession(IAnalogLocomotionSink sink, double speedMultiplier = 1)
    {
        _output = new VrOutputController(sink);
        _speedMultiplier = Math.Clamp(speedMultiplier, .25, 3);
    }

    public async ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (_started) return;
        await _output.StartAsync(cancellationToken);
        _smoother.EmergencyZero();
        _started = true;
    }

    public async ValueTask UpdateAsync(FusionSnapshot snapshot, TimeSpan delta, CancellationToken cancellationToken = default)
    {
        if (!_started) throw new InvalidOperationException("Locomotion session is OFF.");
        var target = Math.Clamp(snapshot.TargetSpeed * _speedMultiplier, 0, 1);
        var speed = _smoother.Update(target, delta, 3.0, target == 0 ? 12.0 : 2.8);
        var turn = _turnSmoother.Update(snapshot.TurnTarget, delta, 4.5, snapshot.TurnTarget == 0 ? 10.0 : 4.5);
        await _output.SetAsync(new LocomotionVector((float)turn, (float)speed), cancellationToken);
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken = default)
    {
        _smoother.EmergencyZero();
        _turnSmoother.EmergencyZero();
        if (_started) await _output.StopAsync(cancellationToken);
        _started = false;
    }

    public async ValueTask DisposeAsync() { await StopAsync(); await _output.DisposeAsync(); }
}
