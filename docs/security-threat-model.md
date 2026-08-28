# NiiMotion security and privacy threat model

Updated: 27 August 2026

## Protected assets

- Raw motion recordings and personal calibration models
- Bluetooth identities, local IP addresses and Windows user paths
- OpenVR bindings, OpenXR registration and their rollback copies
- Update packages and native runtime binaries

## Trust boundaries and controls

| Boundary | Main risk | Current control | Release evidence |
|---|---|---|---|
| owoTrack UDP | Unrelated or replayed packets | Session token, sequence validation, stale-data timeout | Automated UDP and stale-input tests |
| HID devices | Clone or wrong-side controller | Strict VID/PID/report validation and persistent L/R identity | Automated parser/identity tests; hardware matrix pending |
| OpenVR/OpenXR | Stale or cross-process movement | Process scope, heartbeat and fail-closed zero | Native contract and output lifecycle tests |
| Updates | Modified download | HTTPS metadata plus required SHA-256 verification | Automated update verification |
| Diagnostics | Identity or recording disclosure | IP/device/user-path redaction; raw recordings excluded | Automated diagnostic-redaction test |
| Installation | Orphaned privileged registration | Per-user registration where possible, explicit uninstall and rollback contracts | Automated installer contract; clean Windows acceptance pending |

## Release rules

1. Never embed an online AI credential, IGDB/Twitch client secret or signing private key.
2. Never place raw recordings or stable device identities in a support package.
3. Never accept stale sensor, HMD or locomotion heartbeat data as live.
4. Never silently replace a profile, binding or personal model.
5. Sign the final installer and native binaries outside the repository; publish checksums separately.
6. Run `scripts/verify-development.ps1 -UiSmoke` and the clean Windows checklist before a release candidate.

## Remaining manual security acceptance

- Verify installer elevation and rollback on a clean standard-user Windows account.
- Verify update signature/key operations once production signing is configured.
- Review the final distribution contents against `THIRD_PARTY_NOTICES.md`.
- Confirm that a captured support package contains no raw sample, IP address, Bluetooth identity or personal path.
