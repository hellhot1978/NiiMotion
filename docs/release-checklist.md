# Release candidate checklist

## Repository and legal

- [x] Source code license selected: PolyForm Noncommercial License 1.0.0 (source-available, noncommercial).
- [x] Required third-party notice names and bundled license files are enforced by `scripts/verify-release-readiness.ps1`.
- [x] GitHub CodeQL, dependency review and weekly dependency update configuration are present.
- [x] Release component inventory can be generated locally with `scripts/export-component-inventory.ps1`.
- [x] A single manual candidate pipeline builds the installer, verifies its lifecycle, exports the component inventory, and writes a source-bound integrity manifest.
- [ ] Perform a human review of the final distribution against `THIRD_PARTY_NOTICES.md`.
- [ ] Update version and changelog.
- [ ] Confirm no personal recordings, logs, device identities, runtime config, or binding backups are staged.

## Automated acceptance

- [x] `scripts/verify-development.ps1 -UiSmoke` passes on the development machine.
- [x] Release build has zero warnings and errors on the development machine.
- [ ] Standalone package manifest and hashes pass integrity checks.
- [ ] Project is below 15 GB and C: retains at least 10 GB free.

## Clean Windows acceptance

Run `scripts/verify-installer-smoke.ps1 -Installer <path>` on a clean Windows account, or dispatch the manual `installer acceptance` GitHub workflow. This automation proves the packaging lifecycle but does not replace the owner hardware checks below.

- [ ] Install from a standard non-developer account without a separate .NET install.
- [ ] Verify first-run device selection, calibration, Normal VR, and safe stop.
- [ ] Verify SteamVR launch only through the Virtual Desktop readiness sequence.
- [ ] Verify update staging, uninstall, driver removal, and rollback.
- [ ] Verify Turkish/English UI at 100%, 125%, and 150% Windows scaling.

The automated UI matrix renders both languages at 1000x650, 1100x700 and 1200x760 and includes the calibration center and setup dialogs. Real Windows 100%/125%/150% scaling remains a clean-machine visual acceptance item because viewport simulation is not a substitute for operating-system DPI.
Run this matrix locally with `scripts/verify-development.ps1 -UiSmoke`; hosted GitHub Windows workers do not provide a reliable interactive WPF desktop and therefore run the non-visual canonical gate.

## Physical acceptance

- [ ] Label every result as automated-tested, replay-tested, or owner hardware-verified.
- [ ] Test every supported sensor profile and selected mixed profiles.
- [ ] Test sensor sleep, disconnect, reconnect, stale input, game exit, and app shutdown.
- [ ] Verify supported games and the SteamVR dashboard overlay on real hardware.

The required profile/scenario/game inventory is stored in `docs/hardware-acceptance-matrix.json`; do not mark an item hardware-verified from replay or automation.

Build the installer and checksums once, only after all required checks above pass. The canonical command is:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/build-release-candidate.ps1
```

It produces the installer checksum, `component-inventory.json`, and a commit-bound `release-candidate.json` plus its checksum. It deliberately records hardware acceptance and code signing as external gates; automation must not silently mark either one complete.

The headless GitHub Windows workflow calls the same command with `-SkipUiSmoke` because hosted runners do not expose a reliable interactive WPF desktop. It still verifies silent install, standalone files, uninstall and personal-data preservation. The UI omission is written into the candidate manifest and never reported as a visual pass; the local UI matrix remains mandatory before promotion. Every launched installer/application process has a bounded timeout so CI cannot hang indefinitely.
