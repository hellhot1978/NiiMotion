# NiiMotion hardware support status

Updated: 27 August 2026

This table separates implemented protocol support from final product hardware acceptance. “Implemented” never means that every Bluetooth adapter or hardware revision is guaranteed.

| Device | Supported reference | Software status | Final acceptance |
|---|---|---|---|
| Joy-Con L/R | Original Nintendo HID identities and calibrated IMU reports | Implemented and replay-tested | Final reconnect/long-session matrix pending |
| PS Move L/R | CECH-ZCM1 family with PSMoveAPI-assisted pairing | Implemented; dual controllers previously hardware-observed | Final single/mixed profile acceptance pending |
| Android phone | owoTrack UDP input; horizontal, screen toward chest, top edge left | Implemented and replay-tested | Placement and reconnect acceptance pending |
| Wii Balance Board | Standard extension protocol with factory load-cell calibration | Implemented and replay-tested | Walking start and weight-turn acceptance pending |
| Quest headset | Quest 3 reference through Virtual Desktop/SteamVR | Live HMD channel and readiness implemented | Repeated sleep/wake and launch acceptance pending |
| Hand tracking | Virtual Desktop controller emulation only | Configuration implemented | Controller interaction acceptance pending; never walking evidence |

## Explicit non-claims

- Third-party Joy-Con clones are rejected unless they satisfy the exact supported identity and report contract.
- PS Move revisions outside the validated CECH-ZCM1 contract are not implied to work.
- Quest 2/3S or non-Quest headsets may be observed during beta but are not supported merely because SteamVR detects them.
- Phone-only and Board-only locomotion remain experimental profiles.
- A user-added game is supported only after its real SteamVR/OpenXR action or executable mapping passes local validation.
