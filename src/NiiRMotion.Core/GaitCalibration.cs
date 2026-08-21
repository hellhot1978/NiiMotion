namespace NiiRMotion.Core;

public sealed record GaitCalibrationProfile(int Version, DateTimeOffset CreatedAtUtc, double LeftRestMeanDps, double RightRestMeanDps, double LeftRestStdDevDps, double RightRestStdDevDps, double RecommendedLegThresholdDps, double ObservedCadenceMinHz, double ObservedCadenceMaxHz);

public sealed class GaitCalibrationAccumulator
{
    private readonly RunningStats _leftRest = new(), _rightRest = new(); private readonly List<double> _stepTimes = [];
    public void ObserveRest(LegSide side, double angularVelocityMagnitudeDps) => (side == LegSide.Left ? _leftRest : _rightRest).Push(angularVelocityMagnitudeDps);
    public void ObserveStep(double seconds) { if (_stepTimes.Count == 0 || seconds > _stepTimes[^1]) _stepTimes.Add(seconds); }
    public GaitCalibrationProfile Complete()
    {
        if (_leftRest.Count < 100 || _rightRest.Count < 100) throw new InvalidOperationException("At least 100 rest samples per Joy-Con are required.");
        if (_stepTimes.Count < 6) throw new InvalidOperationException("At least 6 alternating gait steps are required.");
        var intervals = _stepTimes.Zip(_stepTimes.Skip(1), (a, b) => b - a).Where(x => x > 0.2 && x < 2).ToArray();
        if (intervals.Length < 5) throw new InvalidOperationException("Gait cadence samples are invalid.");
        var threshold = Math.Max(80, Math.Max(_leftRest.Mean + 6 * _leftRest.StdDev, _rightRest.Mean + 6 * _rightRest.StdDev));
        var cadence = intervals.Select(x => 1 / x).Order().ToArray();
        return new(1, DateTimeOffset.UtcNow, _leftRest.Mean, _rightRest.Mean, _leftRest.StdDev, _rightRest.StdDev, threshold, cadence.First(), cadence.Last());
    }
    private sealed class RunningStats
    {
        public long Count { get; private set; } public double Mean { get; private set; } private double _m2;
        public double StdDev => Count > 1 ? Math.Sqrt(_m2 / (Count - 1)) : 0;
        public void Push(double value) { Count++; var delta = value - Mean; Mean += delta / Count; _m2 += delta * (value - Mean); }
    }
}
