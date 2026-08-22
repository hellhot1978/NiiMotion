using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using NiiRMotion.Core;

namespace NiiRMotion.Infrastructure;

public sealed record OfflineCalibrationResult(IReadOnlyList<string> UpdatedProfiles, IReadOnlyDictionary<string, long> AcceptedSamples, DateTimeOffset CompletedAtUtc);

/// <summary>Turns local calibration captures into runtime profiles without network or AI services.</summary>
public sealed class OfflineCalibrationPipeline
{
    private static readonly JsonSerializerOptions ReadOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true, PropertyNameCaseInsensitive = true };
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public async Task<OfflineCalibrationResult> ApplyAvailableAsync(CancellationToken token = default)
    {
        var updated = new List<string>();
        var counts = new Dictionary<string, long>();
        var captures = FindCompletedCaptures();

        if (TryBuildJoyCon(captures, out var gait, out var joyCount))
        {
            await WriteAtomicAsync(Path.Combine(NiiMotionPaths.Config, "personal-gait-pace.json"), gait, token);
            updated.Add("Joy-Con"); counts["Joy-Con"] = joyCount;
        }
        if (TryBuildPhone(captures, out var phone, out var phoneCount))
        {
            await WriteAtomicAsync(Path.Combine(NiiMotionPaths.Config, "personal-phone-motion.json"), phone, token);
            updated.Add("Telefon"); counts["Telefon"] = phoneCount;
        }
        if (TryBuildBoard(captures, out var board, out var boardCount))
        {
            await WriteAtomicAsync(Path.Combine(NiiMotionPaths.Config, "personal-board-motion.json"), board, token);
            updated.Add("Balance Board"); counts["Balance Board"] = boardCount;
        }
        if (TryBuildPsMove(captures, out var move, out var moveCount))
        {
            await WriteAtomicAsync(NiiMotionPaths.PsMoveTrainingProfile, move, token);
            updated.Add("PS Move"); counts["PS Move"] = moveCount;
        }

        var result = new OfflineCalibrationResult(updated, counts, DateTimeOffset.UtcNow);
        await WriteAtomicAsync(Path.Combine(NiiMotionPaths.Config, "calibration-analysis.json"), result, token);
        return result;
    }

    private static Capture[] FindCompletedCaptures()
    {
        if (!Directory.Exists(NiiMotionPaths.Data)) return [];
        var captures = new List<Capture>();
        foreach (var manifest in Directory.EnumerateFiles(NiiMotionPaths.Data, "manifest.json", SearchOption.AllDirectories))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(manifest));
                var root = document.RootElement;
                if (!TryEnum(root, "sensor", out SensorFamily sensor) || !TryInt(root, "phase", out var phase)) continue;
                var folder = Path.GetDirectoryName(manifest)!;
                if (!ExpectedFiles(sensor).All(name => File.Exists(Path.Combine(folder, name)))) continue;
                captures.Add(new(sensor, phase, folder));
            }
            catch (JsonException) { }
            catch (IOException) { }
        }
        return captures.ToArray();
    }

    private static bool TryBuildJoyCon(IEnumerable<Capture> captures, out PersonalGaitPace profile, out long count)
    {
        var phases = PhaseValues<JoyConImuSample>(captures, SensorFamily.JoyCon, ["left.jsonl", "right.jsonl"], x => x.AngularVelocityDps.Length());
        count = phases.Values.Sum(x => (long)x.Count);
        if (!HasBase(phases, 1000)) { profile = default!; return false; }
        var slow = Blend(P(phases[1], .90), P(phases[0], .65));
        var natural = Math.Max(Blend(P(phases[2], .92), P(phases[0], .88)), slow * 1.12);
        var fast = Math.Max(Blend(P(phases[3], .97), P(phases[0], .98)), natural * 1.12);
        profile = new(Math.Round(slow, 2), Math.Round(natural, 2), Math.Round(fast, 2)); return true;
    }

    private static bool TryBuildPhone(IEnumerable<Capture> captures, out PersonalPhoneMotion profile, out long count)
    {
        var gyro = PhaseValues<PhoneImuSample>(captures, SensorFamily.Phone, ["phone.jsonl"], x => x.AngularVelocityRadps.Length());
        var accel = PhaseValues<PhoneImuSample>(captures, SensorFamily.Phone, ["phone.jsonl"], x => x.AccelerationMps2.Length());
        count = gyro.Values.Sum(x => (long)x.Count);
        if (!HasBase(gyro, 100) || !HasBase(accel, 100)) { profile = default!; return false; }
        var rest = P(gyro[1], .20); var slowG = Math.Max(Blend(P(gyro[1], .90), P(gyro[0], .65)), rest + .03); var naturalG = Math.Max(Blend(P(gyro[2], .92), P(gyro[0], .88)), slowG * 1.08); var fastG = Math.Max(Blend(P(gyro[3], .97), P(gyro[0], .98)), naturalG * 1.08);
        var slowA = Blend(P(accel[1], .90), P(accel[0], .65)); var naturalA = Math.Max(Blend(P(accel[2], .92), P(accel[0], .88)), slowA * 1.03); var fastA = Math.Max(Blend(P(accel[3], .97), P(accel[0], .98)), naturalA * 1.03);
        profile = new(rest, slowG, naturalG, fastG, slowA, naturalA, fastA); return true;
    }

    private static bool TryBuildBoard(IEnumerable<Capture> captures, out PersonalBoardMotion profile, out long count)
    {
        var samples = PhaseSamples<BalanceBoardSample>(captures, SensorFamily.BalanceBoard, "board.jsonl");
        count = samples.Values.Sum(x => (long)x.Count);
        if (!HasBase(samples, 100)) { profile = default!; return false; }
        var contact = samples.Values.SelectMany(x => x).Where(x => x.TotalKg >= 5).ToArray();
        if (contact.Length < 100) { profile = default!; return false; }
        var cop = contact.Select(x => (double)x.CenterOfPressureX).Order().ToArray();
        var left = Math.Min(-.025, P(cop, .22)); var right = Math.Max(.025, P(cop, .78));
        var cadence = new[] { Cadence(samples[1], left, right), Blend(Cadence(samples[2], left, right), Cadence(samples[0], left, right)), Cadence(samples[3], left, right) };
        var slow = cadence[0] > .2 ? cadence[0] : .85; var natural = Math.Max(cadence[1], slow + .12); var fast = Math.Max(cadence[2], natural + .18);
        var turnY = Math.Clamp(P(contact.Select(x => (double)x.CenterOfPressureY).Order().ToArray(), .82), -.15, .45);
        var weight = P(contact.Select(x => (double)x.TotalKg).Order().ToArray(), .10);
        profile = new(left, right, slow, natural, fast, turnY, Math.Clamp(weight * .25, 7, 20), Math.Min(-.35, P(cop, .08)), Math.Max(.35, P(cop, .92)), .45, .65); return true;
    }

    private static bool TryBuildPsMove(IEnumerable<Capture> captures, out PsMoveTrainingProfile profile, out long count)
    {
        var samples = PhaseSamples<PsMoveImuSample>(captures, SensorFamily.PsMove, "moves.jsonl");
        count = samples.Values.Sum(x => (long)x.Count);
        if (!HasBase(samples, 1000)) { profile = default!; return false; }
        var phaseValues = samples.ToDictionary(x => x.Key, x => x.Value.Select(s => (double)s.AngularVelocityRadps.Length()).Order().ToArray());
        if (phaseValues[0].Length > 0) phaseValues[2] = phaseValues[2].Concat(phaseValues[0]).Order().ToArray();
        var standValues = phaseValues[1].Take(Math.Max(1, phaseValues[1].Length / 5)).ToArray();
        var anchors = new Dictionary<string, PsMoveMotionAnchor>
        {
            ["stand"] = Anchor(standValues), ["slow_walk"] = Anchor(phaseValues[1]), ["natural_walk"] = Anchor(phaseValues[2]), ["fast_walk"] = Anchor(phaseValues[3])
        };
        var release = Math.Max(anchors["stand"].MedianRadps * 3, P(standValues, .75));
        var activation = Math.Clamp(Math.Max(release * 1.6, anchors["slow_walk"].MedianRadps * .55), release + .04, anchors["natural_walk"].MedianRadps * .85);
        var left = samples[2].Where(x => x.Side == LegSide.Left).Select(x => x.AngularVelocityRadps.Length()).DefaultIfEmpty(1).Average();
        var right = samples[2].Where(x => x.Side == LegSide.Right).Select(x => x.AngularVelocityRadps.Length()).DefaultIfEmpty(1).Average();
        profile = new(1, DateTimeOffset.UtcNow, SensorPlacement.CalfLowerLeg, (int)count, 900, release, activation, anchors["slow_walk"].MedianRadps, anchors["natural_walk"].MedianRadps, anchors["fast_walk"].MedianRadps, right <= .0001 ? 1 : left / right, anchors); return true;
    }

    private static Dictionary<int, List<double>> PhaseValues<T>(IEnumerable<Capture> captures, SensorFamily sensor, string[] files, Func<T, double> selector) =>
        Enumerable.Range(0, 4).ToDictionary(phase => phase, phase => captures.Where(x => x.Sensor == sensor && x.Phase == phase).SelectMany(x => files.SelectMany(file => Read<T>(Path.Combine(x.Folder, file)))).Select(selector).Where(double.IsFinite).Order().ToList());

    private static Dictionary<int, List<T>> PhaseSamples<T>(IEnumerable<Capture> captures, SensorFamily sensor, string file) =>
        Enumerable.Range(0, 4).ToDictionary(phase => phase, phase => captures.Where(x => x.Sensor == sensor && x.Phase == phase).SelectMany(x => Read<T>(Path.Combine(x.Folder, file))).ToList());

    private static IEnumerable<T> Read<T>(string path)
    {
        if (!File.Exists(path)) yield break;
        foreach (var line in File.ReadLines(path))
        {
            T? value; try { value = JsonSerializer.Deserialize<T>(line, ReadOptions); } catch (JsonException) { continue; }
            if (value is not null) yield return value;
        }
    }

    private static bool HasBase<T>(IReadOnlyDictionary<int, List<T>> phases, int minimum) => Enumerable.Range(1, 3).All(x => phases.TryGetValue(x, out var values) && values.Count >= minimum);
    private static double Cadence(IReadOnlyList<BalanceBoardSample> values, double left, double right)
    {
        var events = new List<long>(); var side = 0;
        foreach (var sample in values.Where(x => x.TotalKg >= 5).OrderBy(x => x.Timestamp.MonotonicTicks))
        {
            var next = sample.CenterOfPressureX <= left ? -1 : sample.CenterOfPressureX >= right ? 1 : 0;
            if (next != 0 && side != 0 && next != side) events.Add(sample.Timestamp.MonotonicTicks);
            if (next != 0) side = next;
        }
        var intervals = events.Zip(events.Skip(1), (a, b) => (b - a) / (double)Stopwatch.Frequency).Where(x => x is >= .24 and <= 1.8).ToArray();
        return intervals.Length == 0 ? 0 : 1 / intervals.Order().ElementAt(intervals.Length / 2);
    }
    private static PsMoveMotionAnchor Anchor(IEnumerable<double> source) { var v = source.Order().ToArray(); return new(v.Average(), P(v, .5), P(v, .95), v.Length); }
    private static double Blend(double baseline, double additional) => additional > 0 ? baseline * .75 + additional * .25 : baseline;
    private static double P(IReadOnlyList<double> ordered, double fraction) => ordered.Count == 0 ? 0 : ordered[(int)Math.Clamp(Math.Round((ordered.Count - 1) * fraction), 0, ordered.Count - 1)];
    private static IEnumerable<string> ExpectedFiles(SensorFamily sensor) => sensor switch { SensorFamily.JoyCon => ["left.jsonl", "right.jsonl"], SensorFamily.PsMove => ["moves.jsonl"], SensorFamily.Phone => ["phone.jsonl"], _ => ["board.jsonl"] };
    private static bool TryInt(JsonElement root, string name, out int value) { value = 0; return root.TryGetProperty(name, out var p) && p.TryGetInt32(out value); }
    private static bool TryEnum<T>(JsonElement root, string name, out T value) where T : struct, Enum
    {
        value = default;
        if (!root.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind != JsonValueKind.Number) return Enum.TryParse(property.GetString(), true, out value);
        if (!property.TryGetInt32(out var number) || !Enum.IsDefined(typeof(T), number)) return false;
        value = (T)Enum.ToObject(typeof(T), number);
        return true;
    }
    private static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var content = JsonSerializer.Serialize(value, WriteOptions);
        if (File.Exists(path))
        {
            var previous = await File.ReadAllTextAsync(path, token);
            if (string.Equals(previous, content, StringComparison.Ordinal)) return;
            var history = Path.Combine(NiiMotionPaths.Config, "model-history", Path.GetFileNameWithoutExtension(path));
            Directory.CreateDirectory(history);
            var backup = Path.Combine(history, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".json");
            await File.WriteAllTextAsync(backup, previous, token);
            foreach (var old in new DirectoryInfo(history).GetFiles("*.json").OrderByDescending(x => x.CreationTimeUtc).Skip(20))
                try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        var temporary = path + ".tmp";
        await File.WriteAllTextAsync(temporary, content, token);
        File.Move(temporary, path, true);
    }
    private sealed record Capture(SensorFamily Sensor, int Phase, string Folder);
}
