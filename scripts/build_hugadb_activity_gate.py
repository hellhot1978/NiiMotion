"""Distill HuGaDB bilateral thigh motion into a compact gait/activity gate."""
from __future__ import annotations

import io
import json
import re
import zipfile
from pathlib import Path

import numpy as np
from sklearn.ensemble import RandomForestClassifier

ROOT = Path(__file__).resolve().parents[1]
ARCHIVE = ROOT / "data" / "external" / "HuGaDB" / "HumanGaitDataBase.zip"
OUTPUT = ROOT / "models" / "hugadb-activity-gate-v1.json"
WINDOW = 112  # roughly two seconds at HuGaDB's 56.35 Hz
STRIDE = 56
GYRO_SCALE = 2000.0 / 32768.0


def window_features(block: np.ndarray) -> np.ndarray | None:
    # Right thigh gyro: 15:18; left thigh gyro: 33:36; activity: 38.
    raw_r, raw_l = block[:, 15:18], block[:, 33:36]
    if np.mean(np.abs(raw_r) >= 32760) > 0.01 or np.mean(np.abs(raw_l) >= 32760) > 0.01:
        return None
    right = np.linalg.norm(raw_r * GYRO_SCALE, axis=1)
    left = np.linalg.norm(raw_l * GYRO_SCALE, axis=1)
    lr, rr = np.sqrt(np.mean(left**2)), np.sqrt(np.mean(right**2))
    lp, rp = np.percentile(left, 95), np.percentile(right, 95)
    asym = abs(lp - rp) / max(20.0, lp + rp)
    both_active = np.mean((left >= 50) & (right >= 50))
    return np.log1p([lr, rr, lp, rp, asym * 100, both_active * 100])


def collect() -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    xs, ys, subjects = [], [], []
    with zipfile.ZipFile(ARCHIVE) as archive:
        names = [n for n in archive.namelist() if n.lower().endswith(".txt") and "/hugadb_" in n.lower()]
        for name in names:
            match = re.search(r"_(\d{2})_\d{2}\.txt$", name)
            subject = int(match.group(1)) if match else 0
            raw = archive.read(name)
            try:
                data = np.loadtxt(io.BytesIO(raw), delimiter="\t", skiprows=4, dtype=np.float64)
            except ValueError:
                continue
            if data.ndim != 2 or data.shape[1] < 39:
                continue
            for start in range(0, len(data) - WINDOW + 1, STRIDE):
                block = data[start : start + WINDOW]
                labels = block[:, 38].astype(int)
                label = int(np.bincount(labels[labels >= 0]).argmax())
                # Flat walking/running are positive. Stairs, sitting, standing and
                # transitions are negative for NiiMotion's flat locomotion output.
                if label not in range(1, 9):
                    continue
                feat = window_features(block)
                if feat is not None:
                    xs.append(feat); ys.append(1.0 if label in (1, 2) else 0.0); subjects.append(subject)
    return np.asarray(xs), np.asarray(ys), np.asarray(subjects)


def fit(x: np.ndarray, y: np.ndarray, alpha: float = 8.0) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    mean, scale = x.mean(axis=0), x.std(axis=0)
    scale[scale < 1e-8] = 1
    z = np.column_stack([np.ones(len(x)), (x - mean) / scale])
    penalty = np.eye(z.shape[1]) * alpha; penalty[0, 0] = 0
    coef = np.linalg.solve(z.T @ z + penalty, z.T @ y)
    return coef, mean, scale


def predict(x: np.ndarray, coef: np.ndarray, mean: np.ndarray, scale: np.ndarray) -> np.ndarray:
    z = np.column_stack([np.ones(len(x)), (x - mean) / scale])
    return np.clip(z @ coef, 0, 1)


def main() -> None:
    x, y, subjects = collect()
    folds = []
    for subject in sorted(set(subjects)):
        train, test = subjects != subject, subjects == subject
        if test.sum() == 0 or len(set(y[train])) < 2:
            continue
        forest = RandomForestClassifier(n_estimators=60, max_depth=7, min_samples_leaf=25, class_weight="balanced", n_jobs=-1, random_state=100 + subject)
        forest.fit(x[train], y[train]); pred = forest.predict(x[test])
        folds.append({"subject": int(subject), "windows": int(test.sum()), "accuracy": float(np.mean(pred == y[test]))})
    forest = RandomForestClassifier(n_estimators=80, max_depth=7, min_samples_leaf=25, class_weight="balanced", n_jobs=-1, random_state=42)
    forest.fit(x, y); pred = forest.predict(x)
    trees = []
    for estimator in forest.estimators_:
        tree = estimator.tree_
        trees.append({
            "feature": tree.feature.astype(int).tolist(),
            "threshold": tree.threshold.tolist(),
            "left": tree.children_left.astype(int).tolist(),
            "right": tree.children_right.astype(int).tolist(),
            "gaitProbability": (tree.value[:, 0, 1] / np.maximum(1e-9, tree.value[:, 0].sum(axis=1))).tolist(),
        })
    payload = {
        "version": 1,
        "source": "https://github.com/romanchereshnev/HuGaDB",
        "featureNames": ["logLeftRms", "logRightRms", "logLeftP95", "logRightP95", "logAsymmetryPercent", "logBothActivePercent"],
        "forest": trees,
        "training": {
            "subjects": int(len(set(subjects))), "windows": int(len(y)), "gaitWindows": int(y.sum()),
            "fitAccuracy": float(np.mean((pred >= 0.5) == y)),
            "crossSubjectAccuracy": float(np.mean([f["accuracy"] for f in folds])),
            "leaveOneSubjectOut": folds,
        },
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(json.dumps(payload["training"], indent=2))
    print(f"MODEL_OK path={OUTPUT} bytes={OUTPUT.stat().st_size}")


if __name__ == "__main__":
    main()
