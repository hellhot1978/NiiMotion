# Phase 4 — PS Move Only runtime

Status: software complete; owner VR validation pending.

## Implemented runtime

- `PsMoveGaitEngine` consumes the owner's calibrated lower-leg Move streams and preserves bilateral alternation.
- Move-only sessions use the existing fail-closed OpenVR named-pipe output; they do not simulate keyboard input.
- Critical stream loss cancels output and returns locomotion to safe zero.
- Native HMD yaw-rate suppression prevents a head/body turn from becoming forward movement.
- Live diagnostic CSV files are size-bounded by the existing retention policy.

## Replay gate

The current owner dataset produces:

- fast walk: 87.7% active
- natural walk: 67.5% active
- slow walk: 66.3% active
- bend: 2.7% false activity
- crouch/reach: 0.7% false activity
- single-leg hold: 0.0% false activity
- turn: 5.6% false activity (native HMD suppression is the runtime turn guard)

## Automated end-user onboarding

Fresh installations can complete controller assignment, USB factory calibration, Bluetooth verification, mounting calibration, two guided recordings and personal profile generation without an AI assistant. Left is permanently identified in red and right in blue. Clicking either connected Move card performs an individual three-second color check; an incomplete device opens onboarding instead.

## Remaining physical validation

With both assigned Move controllers connected over Bluetooth and mounted below the knees, validate in a real SteamVR game:

1. first-step latency;
2. immediate stop behavior;
3. straight walking without lateral drift;
4. in-place body/head turns without forward motion;
5. slow, natural and fast pace separation;
6. reconnect/fail-safe behavior.

Do not begin the hybrid Move + Joy-Con phase until this gate is accepted.
