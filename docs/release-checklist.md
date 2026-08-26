# Release candidate checklist

## Repository and legal

- [ ] Choose and add the source-code license; do not infer this choice from third-party notices.
- [ ] Review `THIRD_PARTY_NOTICES.md` and include all required license files.
- [ ] Update version and changelog.
- [ ] Confirm no personal recordings, logs, device identities, runtime config, or binding backups are staged.

## Automated acceptance

- [ ] `scripts/verify-development.ps1 -UiSmoke` passes.
- [ ] Release build has zero warnings and errors.
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

Build the installer and checksums once, only after all required checks above pass.
