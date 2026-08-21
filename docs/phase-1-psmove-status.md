# Phase 1 — PS Move Read-Only Discovery

Date: 2026-08-21

## Scope

This milestone adds only safe identification and read-only HID capability probing for the owner's two Sony PS Move CECH-ZCM1E controllers. It does not pair controllers, send output reports, alter locomotion, add PS Move to a gameplay profile, or change the VR launch chain.

## Verified protocol facts

- Original CECH-ZCM1 is identified by Sony VID `054C` and PID `03D5`.
- USB is used for Bluetooth pairing and controller configuration.
- Sensor and button input is read over Bluetooth; a USB VID/PID match alone must not be reported as sensor-ready.
- A persistent controller identity should ultimately use its Bluetooth address rather than discovery order.
- Windows pairing is a separate, state-changing operation and remains outside this read-only milestone.

Primary references:

- PS Move API public interface and model/connection documentation: https://github.com/thp/psmoveapi/blob/master/include/psmove.h
- PS Move API pairing documentation: https://psmoveapi.readthedocs.io/en/latest/pairing.html
- PS Move API source repository: https://github.com/thp/psmoveapi

## Added

- `PsMoveDeviceDescriptor`: strict CECH-ZCM1 VID/PID identity and conservative transport classification.
- `HidDeviceEnumerator.FindPsMoves()`: Windows HID discovery without fake devices.
- `PsMoveDiagnosticsService`: metadata-only handle open and HID input/output/feature report length probe. No controller writes are issued.
- `--psmove-discovery`: owner/developer diagnostic command.
- PS Move identity regression test.

## Verification result

- Release build: passed, 0 warnings, 0 errors.
- Automated tests: 49/49 passed.
- Hardware discovery on this PC: 0 PS Move devices detected.
- Hardware status: **not yet verified** because neither controller was connected during this phase.

## Exact next hardware gate

Connect one CECH-ZCM1E to the PC with a data-capable Mini-USB cable. Run the read-only discovery diagnostic and record its HID report lengths. Then disconnect USB, connect the same controller over Bluetooth, repeat the diagnostic, and compare the two paths. Do not implement pairing or IMU parsing until these real reports are captured.

