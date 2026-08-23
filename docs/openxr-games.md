# OpenXR game adapters

The game wizard detects SteamVR action manifests or an OpenXR loader. For OpenXR games it scans executable candidates without modifying game files, prioritizes Unreal `Win64-Shipping` binaries, detects common Unreal/Unity/native layouts, and stores at most two process names in a reversible local adapter.

The implicit API layer is enabled only while NiiMotion mode and an OpenXR adapter are selected. Locomotion shared memory is process-scoped so unrelated OpenXR applications receive no movement.
