# Exact OpenCode continuation prompt

Copy the prompt below into a new OpenCode agent opened at `C:\NiirMotion`.

---

You are continuing the existing NiiMotion project in `C:\NiirMotion`. Do not redesign or restart it. Preserve all currently working behavior and continue from the verified source state.

Before doing any work:

1. Read `AGENTS.md` completely. It is authoritative for the entire repository.
2. Read `docs/AI_AGENT_HANDOFF.md`, `docs/OPENCODE_HANDOFF.md`, `docs/standalone-acceptance.md`, `docs/upgrade-plan-v5.md`, and `docs/hardware-acceptance-matrix.json` completely.
3. Run `git status --short` and `git log -5 --oneline`.
4. Confirm that the expected baseline is branch `main`, commit `4eb9d511a29bc2474f847828b27cc540e87440de` or a later intentional commit, application version `0.6.1`, and public prerelease `v0.6.1-beta.1`.
5. Run `powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1` before editing. The verified baseline at handoff is 111/111 tests, zero build warnings/errors, zero uncovered English UI strings, and passing release-readiness contracts. If the result differs, diagnose and report the difference before changing code.

Critical preservation rules:

- Never reset, checkout, overwrite, stage, commit, upload, delete, normalize, or “clean” `native/openvr-driver/dist/resources/input/niirmotion_profile.json`. It contains a user/runtime modification and is intentionally dirty.
- Treat `data/`, `logs/`, runtime `config/`, recordings, calibration phases, device identities, model history, backups, diagnostic packages, and artifacts as user-owned. Do not alter or upload them without explicit permission.
- Never use destructive Git commands. Stage only the exact source files intentionally changed.
- Do not rebuild the installer during ordinary development. Only build it when explicitly asked for a new release candidate.
- Do not introduce OpenAI, Gemini, Anthropic, cloud inference, or any online AI dependency into the application runtime. NiiMotion must remain fully standalone.

Non-negotiable product invariants:

- Normal VR behaves as if NiiMotion is absent.
- Locomotion output starts at zero and fails closed to zero on stop, stale input, disconnect, error, profile change, shutdown, or incomplete calibration.
- Never launch a game until the selected profile, required individual models, the selected combination model, required live sensors, Quest/Virtual Desktop session, runtime order, and game adapter pass their gates.
- SteamVR must be launched only through the verified Virtual Desktop sequence. Never add a direct path that can restore Oculus error 1114.
- Hand tracking is controller emulation only and never walking evidence.
- Phone, Balance Board, or HMD optional evidence cannot create locomotion by itself except in the explicitly selected experimental phone-only or board-only profiles.
- Never silently switch profiles or fabricate a game action mapping.
- Personal gait calibration and per-game tuning remain separate; game settings must never rewrite personal calibration.

Current implemented system that must not regress:

- Self-contained .NET 10 WPF application for Windows x64.
- Joy-Con, PS Move, owoTrack Android phone, Wii Balance Board, Quest/Virtual Desktop and optional HMD support.
- Three guided five-minute base phases per motion device with pause, retake/delete and local offline model generation.
- Three guided two-minute combined phases per multi-device profile with pause, phase retake, learned-value summary, per-combination health diagnostics, bounded model history and rollback.
- Deterministic multi-sensor fusion with per-combination local models and fail-closed launch/runtime gates.
- OpenVR analog driver, process-scoped OpenXR layer and SteamVR dashboard overlay.
- Local game catalog and wizard, reversible bindings, Alyx telemetry, generic bounded game tuning and verified Virtual Desktop launch order.
- Turkish/English UI with automated localization coverage, local diagnostics, privacy-safe support packages, model backup/restore and explicit learned-data reset.
- Public beta assets and source at `https://github.com/hellhot1978/NiiMotion/releases/tag/v0.6.1-beta.1`.

Architecture boundaries:

- Put deterministic gait, sensor fusion, model and safety logic in `NiiRMotion.Core`.
- Put HID/UDP, persistence, calibration analysis, SteamVR/OpenXR and launch orchestration in `NiiRMotion.Infrastructure`.
- Keep `NiiRMotion.App` focused on WPF presentation and workflows; do not embed motion algorithms there.
- Preserve native heartbeat and neutral-output contracts under `native/`.

How to continue:

- Do not invent a new development task. First ask the user what change or hardware test they want next, unless their message already contains a concrete request.
- For a code change, inspect the relevant implementation and tests, make the smallest reversible change, and add or update a regression test.
- For UI/localization changes, finish with `powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1 -UiSmoke` after closing any already-running NiiMotion instance that would block the smoke-test process; reopen the current app afterward if appropriate.
- For ordinary changes, finish with `powershell -ExecutionPolicy Bypass -File scripts/verify-development.ps1`.
- For hardware work, distinguish owner-hardware-verified results from replay tests and software-only checks. Never claim hardware success from mocks.
- Report separately: implemented/automated-tested, replay-tested, owner-hardware-verified, and still pending physical validation.

The legitimate next gates are physical rather than speculative rewrites:

1. Execute the scenarios in `docs/hardware-acceptance-matrix.json` with real Joy-Con, PS Move, phone, Board and selected combinations.
2. Tune Move stop timing, physical-step/game-distance, Joy-Con+Move alignment, Board behavior, phone placement and HMD suppression only from measured failures.
3. Validate Alyx, Arizona Sunshine 2 and Metro Awakening/OpenXR in real play.
4. Validate sleep/disconnect/reconnect, overlay commands and safe-zero behavior.
5. Validate install/update/uninstall/rollback on a clean standard-user Windows account at 100%, 125% and 150% DPI.
6. Configure a real code-signing certificate before any claim of signed/public production readiness.

Start by reporting the baseline verification result, current intentional dirty files, and the exact next task you understand from the user's request. Do not change anything until those facts are established.

---
