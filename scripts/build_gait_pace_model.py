"""Build a compact NiiMotion pace prior from the GPL-3.0 DeepGait dataset."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from scipy.io import loadmat

ROOT = Path(__file__).resolve().parents[1]
RAW = ROOT / "data" / "external" / "DeepGait" / "Bipedal-Motion-Dataset" / "Raw"
OUTPUT = ROOT / "models" / "deepgait-pace-v1.json"
RATE_HZ = 400.0
WINDOW = 800
STRIDE = 400


def features(block: np.ndarray) -> tuple[list[float], float] | None:
    # Dataset columns: thigh accel 0:3, thigh gyro 9:12, speed 18, time 19.
    gyro = block[:, 9:12] * 0.061  # raw -> deg/s
    speed = float(np.median(block[:, 18]))
    if not np.isfinite(speed) or speed < 0 or speed > 10:
        return None
    gyro_mag = np.linalg.norm(gyro, axis=1)
    centered = gyro_mag - np.mean(gyro_mag)
    spectrum = np.abs(np.fft.rfft(centered * np.hanning(len(centered))))
    freqs = np.fft.rfftfreq(len(centered), 1 / RATE_HZ)
    band = (freqs >= 0.35) & (freqs <= 2.2)
    stride_hz = float(freqs[band][np.argmax(spectrum[band])]) if np.any(band) else 0.0
    cadence_hz = stride_hz * 2.0
    gyro_p95 = float(np.percentile(gyro_mag, 95))
    return [
        1.0,
        cadence_hz,
        gyro_p95,
        cadence_hz**2,
        gyro_p95**2,
        cadence_hz * gyro_p95,
    ], speed


def collect() -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    rows, targets, subjects = [], [], []
    for subject, path in enumerate(sorted(RAW.glob("data_subj_*.mat")), start=1):
        data = np.asarray(loadmat(path)["data"], dtype=np.float64)
        for start in range(0, len(data) - WINDOW + 1, STRIDE):
            item = features(data[start : start + WINDOW])
            if item is None:
                continue
            x, y = item
            # Exclude stationary windows; gait start/stop remains deterministic in Core.
            if y >= 0.5:
                rows.append(x)
                targets.append(y)
                subjects.append(subject)
    return np.asarray(rows), np.asarray(targets), np.asarray(subjects)


def ridge_fit(x: np.ndarray, y: np.ndarray, alpha: float = 4.0) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    mean = x[:, 1:].mean(axis=0)
    scale = x[:, 1:].std(axis=0)
    scale[scale < 1e-9] = 1
    z = np.column_stack([np.ones(len(x)), (x[:, 1:] - mean) / scale])
    penalty = np.eye(z.shape[1]) * alpha
    penalty[0, 0] = 0
    coef = np.linalg.solve(z.T @ z + penalty, z.T @ y)
    return coef, mean, scale


def predict(x: np.ndarray, coef: np.ndarray, mean: np.ndarray, scale: np.ndarray) -> np.ndarray:
    z = np.column_stack([np.ones(len(x)), (x[:, 1:] - mean) / scale])
    return np.clip(z @ coef, 0, 30)


def main() -> None:
    x, y, subjects = collect()
    folds = []
    for subject in sorted(set(subjects)):
        train, test = subjects != subject, subjects == subject
        coef, mean, scale = ridge_fit(x[train], y[train])
        estimate = predict(x[test], coef, mean, scale)
        folds.append({
            "subject": int(subject),
            "windows": int(test.sum()),
            "maeKmh": float(np.mean(np.abs(estimate - y[test]))),
            "rmseKmh": float(np.sqrt(np.mean((estimate - y[test]) ** 2))),
        })
    coef, mean, scale = ridge_fit(x, y)
    estimate = predict(x, coef, mean, scale)
    payload = {
        "version": 1,
        "source": "https://github.com/Josef4Sci/DeepGait",
        "license": "GPL-3.0",
        "windowSeconds": WINDOW / RATE_HZ,
        "featureNames": ["cadenceHz", "gyroP95Dps", "cadenceSquared", "gyroP95Squared", "cadenceTimesGyroP95"],
        "mean": mean.tolist(),
        "scale": scale.tolist(),
        "coefficients": coef.tolist(),
        "training": {
            "subjects": int(len(set(subjects))),
            "windows": int(len(y)),
            "speedMinKmh": float(y.min()),
            "speedMedianKmh": float(np.median(y)),
            "speedMaxKmh": float(y.max()),
            "fitMaeKmh": float(np.mean(np.abs(estimate - y))),
            "fitRmseKmh": float(np.sqrt(np.mean((estimate - y) ** 2))),
            "leaveOneSubjectOut": folds,
            "crossSubjectMaeKmh": float(np.mean([f["maeKmh"] for f in folds])),
        },
        "niirmotionPersonalization": {
            "leftLiftP95Dps": 157.3,
            "rightLiftP95Dps": 140.9,
            "leftRestP95Dps": 44.9,
            "rightRestP95Dps": 42.9,
            "source": "recordings/leg-balance.jsonl",
        },
    }
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    OUTPUT.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(json.dumps(payload["training"], indent=2))
    print(f"MODEL_OK path={OUTPUT} bytes={OUTPUT.stat().st_size}")


if __name__ == "__main__":
    main()
