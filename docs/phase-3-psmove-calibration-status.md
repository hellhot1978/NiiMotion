# Phase 3 — PS Move lower-leg calibration and training

Status: complete for the owner hardware and placement.

## Placement contract

- PS Move: below knee / calf / lower leg, sphere up, buttons outward-forward.
- Joy-Con: hip-to-knee / thigh / upper leg.
- Locomotion output remains disabled throughout all calibration recordings.

## Personal hardware calibration

- Factory-calibrated dual PS Move source used for every sample.
- Neutral mounting/gravity calibration: 6,633 samples.
- Personal placement file: `config/personal-psmove-placement.json` (ignored personal data).

## Labeled owner dataset

- Foundation: 300 seconds, 99,309 samples.
- Discrimination: 300 seconds, 99,241 samples.
- Total: 600.011 seconds, 198,550 samples.
- No per-side gaps above 60 ms were observed in either recording.
- Labels cover stand, slow/natural/fast walk, repeated start-stop, bend, single-leg hold, turn, crouch and reach.

## Learned personal anchors

- Rest release: 0.105 rad/s.
- Gait activation: 0.238 rad/s.
- Slow median: 0.434 rad/s.
- Natural median: 0.691 rad/s.
- Fast median: 1.171 rad/s.
- Natural left/right ratio: 1.089.

The profile is stored in `config/personal-psmove-training.json` (ignored personal data). Phase 4 may now build Move-only locomotion, but must retain bilateral alternation and reject the labeled non-walking motions; angular magnitude alone is not a gait classifier.
