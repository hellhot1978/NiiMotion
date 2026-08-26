# NiiMotion agent rules

These instructions apply to the whole repository and are written for Codex, OpenCode, and other coding agents.

## Start here

1. Read `docs/OPENCODE_HANDOFF.md`.
2. Read `docs/standalone-acceptance.md` and `docs/upgrade-plan-v5.md`.
3. Run `powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1` before changing code.
4. Inspect `git status --short`. Runtime files and user data are not source changes.

`docs/current-state-audit.md` is historical. Do not use its old “NOT IMPLEMENTED” statements as current status.

## Product invariants

- Normal VR must behave as if NiiMotion is absent.
- Locomotion output starts at zero and fails closed on stop, error, disconnect, stale data, profile change, or shutdown.
- Never start a game until the selected profile, local calibration models, required live sensors, Quest/Virtual Desktop, and runtime order pass their gates.
- SteamVR must be launched through the verified Virtual Desktop sequence; do not add a direct launch path that can reintroduce Oculus error 1114.
- Hand tracking is controller emulation only and must not become walking evidence.
- Optional phone/board/HMD evidence must never create locomotion by itself unless the explicitly selected experimental phone-only or board-only profile allows it.
- Do not silently change a profile. Safe fallback requires an explicit user action.
- Runtime must not depend on OpenAI, Gemini, Anthropic, cloud inference, or an agent session.

## Data safety

- Treat `data/`, `logs/`, runtime `config/` files, model history, device identities, and native binding backups as user-owned.
- Do not delete, reset, normalize, commit, or upload personal recordings without explicit user authorization.
- Never overwrite the modified runtime file `native/openvr-driver/dist/resources/input/niirmotion_profile.json` merely to clean Git status. Inspect ownership and preserve it.
- Do not use destructive Git commands. Stage only files intentionally changed for the task.
- Keep the project below 15 GB and leave at least 10 GB free on C:. Do not rebuild the installer on every iteration.

## Editing and validation

- Use small, reversible changes and add a regression test for behavior changes.
- Build with the repository SDK at `.dotnet/dotnet.exe` when present.
- Required acceptance command: `powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1`.
- Use `-Publish` only when a runnable self-contained app is needed. Build the installer only for a requested release candidate.
- Replay/mock tests are software verification, never hardware verification.

## Architecture boundaries

- `NiiRMotion.Core`: deterministic sensor/gait/profile/safety logic; no Windows UI or device access.
- `NiiRMotion.Infrastructure`: HID/UDP, SteamVR/OpenXR, persistence, local analysis, diagnostics, launch orchestration.
- `NiiRMotion.App`: WPF presentation and user workflows; avoid embedding motion algorithms here.
- `native/`: OpenVR driver, OpenXR layer, and SteamVR overlay. Preserve their heartbeat and neutral-output contracts.
- Personal movement models and game mappings are separate. Game tuning must not rewrite calibration data.

## Completion standard

Report separately: implemented and automated-tested, replay-tested, owner hardware-verified, and still pending hardware validation. Do not claim production readiness from automation alone.
