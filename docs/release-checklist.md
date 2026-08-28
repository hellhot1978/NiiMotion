# Release candidate checklist

## Repository and legal

- [x] Source code license selected: PolyForm Noncommercial License 1.0.0 (source-available, noncommercial).
- [x] Required third-party notice names and bundled license files are enforced by `scripts/verify-release-readiness.ps1`.
- [ ] Perform a human review of the final distribution against `THIRD_PARTY_NOTICES.md`.
- [ ] Update version and changelog.
- [ ] Confirm no personal recordings, logs, device identities, runtime config, or binding backups are staged.

## Automated acceptance

- [x] `scripts/verify-development.ps1 -UiSmoke` passes on the development machine.
- [x] Release build has zero warnings and errors on the development machine.
- [ ] Standalone package manifest and hashes pass integrity checks.
- [ ] Project is below 15 GB and C: retains at least 10 GB free.

## Clean Windows acceptance

- [ ] Install from a standard non-developer account without a separate .NET install.
- [ ] Verify first-run device selection, calibration, Normal VR, and safe stop.
- [ ] Verify SteamVR launch only through the Virtual Desktop readiness sequence.
- [ ] Verify update staging, uninstall, driver removal, and rollback.
- [ ] Verify Turkish/English UI at 100%, 125%, and 150% Windows scaling.

## Physical acceptance

- [ ] Label every result as automated-tested, replay-tested, or owner hardware-verified.
- [ ] Test every supported sensor profile and selected mixed profiles.
- [ ] Test sensor sleep, disconnect, reconnect, stale input, game exit, and app shutdown.
- [ ] Verify supported games and the SteamVR dashboard overlay on real hardware.

The required profile/scenario/game inventory is stored in `docs/hardware-acceptance-matrix.json`; do not mark an item hardware-verified from replay or automation.

Build the installer and checksums once, only after all required checks above pass.
