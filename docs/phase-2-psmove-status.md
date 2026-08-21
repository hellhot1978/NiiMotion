# Phase 2 — PS Move Dual Diagnostics and Identity

Date: 2026-08-21

## Implemented and verified

- Both CECH-ZCM1E controllers were discovered simultaneously over Bluetooth.
- Windows HID parent relationships are resolved natively and the Bluetooth address is used as the stable controller identity.
- Two collection-1 input streams were opened and read concurrently without controller writes.
- Three-second live result: `0007041EFC1E` produced 252 reports; `0006F7173E9C` produced 246 reports. All captured reports were live 49-byte `0x01` reports.
- User-controlled button identification assigned:
  - Left: `0007041EFC1E`
  - Right: `0006F7173E9C`
- Assignment storage is schema-versioned, atomic, personal-data excluded from Git, and independent of enumeration order.
- Parser exposes sequence, buttons, trigger, battery, dual accel frames, dual gyro frames, timestamp, and ZCM1 magnetometer raw values.

## Safety boundary

- No pairing operation was performed.
- No LED, rumble, feature, or output report was sent.
- Raw IMU values are not yet used for locomotion.
- Existing Joy-Con, phone, Board, VR output, profiles, and launch chain are unchanged.

## Pending before Phase 2 completion

- Disconnect/reconnect both controllers and prove the same stable identities return.
- Add calibrated sensor conversion after reading the real factory calibration feature reports.
- Add owner-facing Move diagnostics and explicit L/R reassignment to Test and Calibration, not the primary overview.
- Verify battery and button state changes live.

