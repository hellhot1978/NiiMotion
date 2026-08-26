# Contributing to NiiMotion

NiiMotion is safety-sensitive software: a bad input path can move a VR character unexpectedly. Keep changes small, reversible, and fail-closed.

## Before changing code

1. Read `AGENTS.md` and `docs/OPENCODE_HANDOFF.md`.
2. Run `powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1`.
3. Check `git status --short`; never reset user recordings, runtime configuration, device identities, or binding backups.

## Pull requests

- Keep motion logic in Core, device/runtime access in Infrastructure, and presentation in App.
- Add a deterministic regression test for behavior changes.
- Do not add cloud inference or require an AI agent at runtime.
- Report automated, replay, and real-hardware validation separately.
- Run `scripts/verify-development.ps1 -UiSmoke` for UI or localization changes.
- Do not rebuild or commit installer output except for an explicitly requested release candidate.

## Safety requirements

Normal VR must remain unaffected. Locomotion starts at zero and returns to zero on stop, error, stale data, disconnect, profile change, or shutdown. A game must not launch until its local readiness gates pass.
