import csv
import json
import math
from collections import defaultdict
from pathlib import Path

ROOT = Path(r"C:\NiirMotion")


def magnitude(vector):
    return math.sqrt(sum(float(vector.get(axis, 0)) ** 2 for axis in ("X", "Y", "Z")))


def percentile(values, fraction):
    if not values:
        return 0.0
    ordered = sorted(values)
    return ordered[min(len(ordered) - 1, round((len(ordered) - 1) * fraction))]


def summarize(values):
    return {
        "samples": len(values),
        "p50Dps": round(percentile(values, .50), 2),
        "p90Dps": round(percentile(values, .90), 2),
        "p95Dps": round(percentile(values, .95), 2),
        "p99Dps": round(percentile(values, .99), 2),
    }


completed = set(json.loads((ROOT / "data/user-gait/joycon-learning/progress-v2.json").read_text()))
activities = defaultdict(list)
accepted_files = []
for folder in sorted((ROOT / "data/user-gait/joycon-learning").glob("part-*-*")):
    session_path = folder / "session.json"
    samples_path = folder / "joycons.jsonl"
    if not session_path.exists() or not samples_path.exists():
        continue
    session = json.loads(session_path.read_text())
    if int(session.get("part", -1)) not in completed:
        continue
    accepted_files.append(str(samples_path.relative_to(ROOT)))
    with samples_path.open(encoding="utf-8") as source:
        for line in source:
            row = json.loads(line)
            tag = row.get("activity", "unknown")
            activities[tag].append(magnitude(row["sample"]["AngularVelocityDps"]))

# Short guided captures contribute only to pace anchors. Trim their five-second
# stationary lead-in/out and avoid treating them as negative examples.
short_paces = defaultdict(list)
for folder in sorted((ROOT / "data/user-gait").glob("2026*-*")):
    session_path = folder / "session.json"
    samples_path = folder / "joycons.jsonl"
    if not session_path.exists() or not samples_path.exists():
        continue
    label = json.loads(session_path.read_text()).get("label")
    if label not in {"slow", "natural", "fast"}:
        continue
    rows = []
    with samples_path.open(encoding="utf-8") as source:
        for line in source:
            row = json.loads(line)
            rows.append((int(row.get("elapsedMs", 0)), magnitude(row["sample"]["AngularVelocityDps"])))
    if not rows:
        continue
    start, end = min(t for t, _ in rows) + 5000, max(t for t, _ in rows) - 5000
    short_paces[label].extend(value for timestamp, value in rows if start <= timestamp <= end)

live_sessions = []
for path in sorted((ROOT / "logs/live").glob("20260817-*.csv")):
    with path.open(newline="", encoding="utf-8") as source:
        rows = list(csv.DictReader(source, delimiter=";"))
    if len(rows) < 2:
        continue
    active = [row for row in rows if float(row["target_speed"]) > 0]
    segments = []
    segment_start = None
    previous_tick = None
    for row in rows:
        tick = int(row["elapsed_ticks"])
        is_active = float(row["target_speed"]) > 0
        if is_active and segment_start is None:
            segment_start = tick
        if not is_active and segment_start is not None:
            segments.append((tick - segment_start) / 10_000_000)
            segment_start = None
        previous_tick = tick
    if segment_start is not None and previous_tick is not None:
        segments.append((previous_tick - segment_start) / 10_000_000)
    live_sessions.append({
        "file": path.name,
        "durationSeconds": round((int(rows[-1]["elapsed_ticks"]) - int(rows[0]["elapsed_ticks"])) / 10_000_000, 1),
        "steps": int(rows[-1]["steps"]),
        "activeSegments": len(segments),
        "medianActiveSegmentSeconds": round(percentile(segments, .5), 2),
        "cadenceP50Hz": round(percentile([float(row["cadence_hz"]) for row in active], .5), 2),
        "cadenceP90Hz": round(percentile([float(row["cadence_hz"]) for row in active], .9), 2),
        "speedP50": round(percentile([float(row["target_speed"]) for row in active], .5), 2),
        "speedP90": round(percentile([float(row["target_speed"]) for row in active], .9), 2),
    })

negative_tags = {"stand", "bend_no_walk", "crouch_no_walk", "single_leg_hold", "reach_no_walk", "turn_no_walk", "side_lean_no_walk", "look_reach_no_walk", "combat_stance_no_walk", "pickup_no_walk", "interact_no_walk"}
negative_values = [value for tag, values in activities.items() if tag in negative_tags for value in values]
pace = {
    "slowP95Dps": round(percentile(activities.get("slow_walk", []) + short_paces.get("slow", []), .95), 2),
    "naturalP95Dps": round(percentile(activities.get("natural_walk", []) + short_paces.get("natural", []), .95), 2),
    "fastP95Dps": round(percentile(short_paces.get("fast", []), .95), 2),
}

result = {
    "acceptedLearningFiles": accepted_files,
    "excludedLearningFolder": "data/user-gait/joycon-learning-rejected",
    "activitySummary": {tag: summarize(values) for tag, values in sorted(activities.items())},
    "nonWalkingCombined": summarize(negative_values),
    "shortPaceSummary": {tag: summarize(values) for tag, values in sorted(short_paces.items())},
    "recommendedPersonalPace": pace,
    "alyxLiveSessions": live_sessions,
}

output = ROOT / "analysis/alyx-personal-optimization.json"
output.parent.mkdir(exist_ok=True)
output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(result, ensure_ascii=False, indent=2))
