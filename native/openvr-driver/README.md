# NiiRMotion OpenVR driver

The driver exposes only `/input/joystick/x` and `/input/joystick/y`. It reads fixed 12-byte `NMR1` packets from the local `NiiRMotion.VrOutput.v1` named pipe.

Safety behavior: values start at zero, are clamped to `[-1,1]`, return to zero 250 ms after the last packet, on pipe disconnect, standby, deactivation, and cleanup. It does not implement keyboard input or system-wide interception.

The treadmill publishes a valid stationary pose every SteamVR frame. This keeps its action source active without claiming a hand role or introducing spatial movement; hand controllers remain independent.

The `dist` directory is the driver root expected by Valve's `vrpathreg`. Registration is deliberately not part of the build.

Rebuild the driver with `scripts/build-openvr-driver.ps1`. Run `scripts/verify-native-rebuild.ps1` to rebuild and validate the driver, OpenXR layer and SteamVR overlay together without modifying the committed distribution binaries.
