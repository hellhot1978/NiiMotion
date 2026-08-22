namespace NiiRMotion.Core;

public sealed record CalibrationStreamPoint(string Stream, long Sequence, double Seconds);
public sealed record CalibrationQualitySegment(double StartSeconds, double EndSeconds, int Samples, double Score, string Issue)
{
    public bool NeedsRedo => Score < .72;
}
public sealed record CalibrationQualityReport(double Score, IReadOnlyList<CalibrationQualitySegment> Segments)
{
    public IReadOnlyList<CalibrationQualitySegment> RedoSegments => Segments.Where(x => x.NeedsRedo).ToArray();
    public bool IsClean => RedoSegments.Count == 0;
}

public static class CalibrationQualityAnalyzer
{
    public static CalibrationQualityReport Analyze(IEnumerable<CalibrationStreamPoint> source, double durationSeconds, double segmentSeconds = 10)
    {
        if (durationSeconds <= 0 || segmentSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        var points = source.Where(x => x.Seconds >= 0 && x.Seconds <= durationSeconds).OrderBy(x => x.Seconds).ToArray();
        var streamRates = points.GroupBy(x => x.Stream).ToDictionary(x => x.Key, x => Math.Max(.1, EstimateRate(x.OrderBy(y => y.Seconds).ToArray())));
        var segments = new List<CalibrationQualitySegment>();
        for (var start = 0d; start < durationSeconds; start += segmentSeconds)
        {
            var end = Math.Min(durationSeconds, start + segmentSeconds); var slice = points.Where(x => x.Seconds >= start && x.Seconds < end).ToArray();
            var expected = streamRates.Sum(x => x.Value * (end - start));
            var coverage = expected <= 0 ? 0 : Math.Clamp(slice.Length / expected, 0, 1);
            var presentStreams = slice.Select(x => x.Stream).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var streamCoverage = streamRates.Count == 0 ? 0 : presentStreams / (double)streamRates.Count;
            var longestGap = slice.GroupBy(x => x.Stream).Select(LongestGap).DefaultIfEmpty(end - start).Max();
            var continuity = Math.Clamp(1 - Math.Max(0, longestGap - .25) / 1.75, 0, 1);
            var score = .45 * coverage + .35 * streamCoverage + .20 * continuity;
            var issue = streamCoverage < 1 ? "Bir sensör akışı eksik" : longestGap > .75 ? "Sensör verisinde kesinti" : coverage < .7 ? "Örnek yoğunluğu düşük" : "Temiz";
            segments.Add(new(start, end, slice.Length, Math.Round(score, 3), issue));
        }
        return new(Math.Round(segments.Count == 0 ? 0 : segments.Average(x => x.Score), 3), segments);
    }

    private static double EstimateRate(CalibrationStreamPoint[] points)
    {
        if (points.Length < 2) return 0;
        var span = points[^1].Seconds - points[0].Seconds;
        return span <= 0 ? 0 : (points.Length - 1) / span;
    }

    private static double LongestGap(IGrouping<string, CalibrationStreamPoint> stream)
    {
        var values = stream.OrderBy(x => x.Seconds).Select(x => x.Seconds).ToArray(); var longest = 0d;
        for (var i = 1; i < values.Length; i++) longest = Math.Max(longest, values[i] - values[i - 1]);
        return longest;
    }
}
