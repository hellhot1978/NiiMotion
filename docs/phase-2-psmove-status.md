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
- Real LED identification was verified: assigned left displays red, assigned right displays blue, with rumble forced to zero.
- Test and Calibration now contains a focused PS Move card and an eight-second `Kontrolcüleri Tanıt` action; the primary Overview page is unchanged.
- Disconnect/reconnect returned the same stable identities and both concurrent streams resumed successfully.
- The 143-byte factory calibration was read over USB for each controller and stored separately by stable identity outside Git.
- Factory accelerometer and gyroscope mapping follows the PS Move API ZCM1 calibration model and converts raw values to `g` and `rad/s`.
- Final calibrated Bluetooth health capture passed for both devices: approximately 82–84 report Hz, full battery, stationary acceleration near 1 g, and low stationary gyro magnitude. Windows scheduling/sequence-loss metrics remain visible as diagnostics rather than being hidden.
- The PS Move card now performs both color identification and a calibrated health capture, with concise status on the card and technical detail in the result strip.

## Safety boundary

- No pairing operation was performed.
- No LED, rumble, feature, or output report was sent.
- Raw IMU values are not yet used for locomotion.
- Existing Joy-Con, phone, Board, VR output, profiles, and launch chain are unchanged.

## Pending before Phase 2 completion

- Add explicit L/R reassignment flow and detailed live sensor health to Test and Calibration.
- Verify battery and button state changes live.
