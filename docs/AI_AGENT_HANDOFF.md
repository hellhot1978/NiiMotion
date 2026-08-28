# NiiMotion model-independent AI handoff

Updated: 27 August 2026

This is the canonical handoff entry point for Codex, OpenCode, Claude Code, Gemini CLI and other coding agents. Repository-wide rules remain authoritative in `AGENTS.md`.

## Required startup sequence

1. Read `AGENTS.md` completely.
2. Read this file, `docs/standalone-acceptance.md`, `docs/upgrade-plan-v5.md` and `docs/hardware-acceptance-matrix.json`.
3. Run `git status --short`. Preserve unrelated and user-owned changes.
4. Run:

   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1
   ```

5. Do not edit until the baseline passes or the failure has been accurately reported.

## Current verified baseline

- Repository: `C:\NiirMotion`, branch `main`.
- Windows WPF application: .NET 10, self-contained `win-x64`.
- Automated regression: 108/108 passing after the model-independent handoff contract; the current command is always authoritative.
- Release build: zero warnings and zero errors.
- English localization audit: zero uncovered unique UI strings.
- UI smoke: four viewports, two Getting Started variants and ten dialog renders.
- Release contracts: privacy, legal files, installer safety, update hashing, package integrity and disk budget are automated.
- Current installable development build: `artifacts/installer/NiiMotion-Setup-0.6.0-x64.exe`; artifacts are generated and not source.
- Runtime has no OpenAI, Gemini, Anthropic or cloud-inference dependency.

## Implemented product surface

- Joy-Con, PS Move, owoTrack phone and Wii Balance Board discovery, calibration and individual/mixed profiles.
- Three guided five-minute base phases per motion device, pause/retry/delete and offline personal-model generation.
- Versioned optional training recordings, segment repair, backup, rollback and explicit learned-data reset.
- Fail-closed OpenVR analog driver, process-scoped OpenXR layer and SteamVR dashboard overlay.
- Virtual Desktop-safe launch order that prevents a direct SteamVR path from reintroducing Oculus error 1114.
- Local game catalog/wizard, reversible bindings, game-specific motion profiles and generic bounded adaptation.
- Optional HMD turn evidence; hand tracking remains controller emulation and never walking evidence.
- Privacy-safe diagnostics and session-health summary without raw recordings.

## The only legitimate remaining product gates

These require the owner, real hardware, a real game or a clean Windows environment. Never mark them complete from mocks or replay:

1. Execute `docs/hardware-acceptance-matrix.json` in one owner hardware session.
2. Tune only from measured failures: Move stopping/step distance, hybrid timing, Board start/turn, phone placement and HMD turn suppression.
3. Validate Alyx, Arizona Sunshine 2 and Metro Awakening/OpenXR in real play.
4. Validate overlay controls, sleep/disconnect/reconnect and safe zero.
5. Test install/update/uninstall/rollback at 100%, 125% and 150% DPI on a clean standard-user Windows account.
6. Configure a real code-signing certificate before public/commercial distribution.

## Important dirty-file rule

`native/openvr-driver/dist/resources/input/niirmotion_profile.json` may be modified by local game binding work. Never reset, normalize or overwrite it merely to make Git clean. Inspect intent and preserve it. The same protection applies to `data/`, `logs/`, runtime `config/`, recordings, model history, device identities and backups.

## Change protocol

- Keep Core deterministic, Infrastructure responsible for devices/runtime/persistence, and App responsible for WPF workflows.
- Add a regression test for every behavior change.
- Preserve Normal VR as complete NiiMotion absence.
- Preserve zero-on-start/stop/error/disconnect/stale/profile-change/shutdown.
- Never silently switch profiles, fabricate an action mapping or treat process/log presence as proof of a live device.
- Do not rebuild the installer during normal iterations. Use `scripts/build-installer.ps1` only when the user explicitly requests an installable candidate.
- Report separately: automated-tested, replay-tested, owner-hardware-verified and pending hardware validation.

## Completion commands

For normal changes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1
```

For UI/localization changes:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1 -UiSmoke
```

For an explicitly requested installable candidate:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-installer.ps1
```

Never claim production readiness solely because these commands pass.
