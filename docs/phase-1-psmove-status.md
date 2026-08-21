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
- Automated tests: 50/50 passed after the first parser contract was added.
- USB: one physical CECH-ZCM1E produced three Windows HID collections. Collection 1 exposed 49/49/49-byte input/output/feature reports; collections 2 and 3 exposed 23-byte and 35-byte feature reports.
- Bluetooth: the same collection layout was observed through the Bluetooth HID path.
- Live Bluetooth input: 247 reports were read in the first three-second capture and 255 reports in the verification capture. Reports were live, distinct, 49 bytes long, and used report ID `0x01`.
- The first real report was decoded into sequence, battery, trigger, two accel frames, two gyro frames, and ZCM1 magnetometer values without writing to the controller.
- Hardware status: **single-controller USB and Bluetooth discovery/input verified**.

## Exact next hardware gate

Connect the second CECH-ZCM1E over Bluetooth while leaving the first one connected. Verify two independent collection-1 paths and two simultaneous live report streams. Then capture stable Bluetooth identities and add explicit left/right assignment; do not rely on enumeration order.
