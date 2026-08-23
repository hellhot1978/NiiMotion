# NiiMotion release security

NiiMotion update packages are never executed while downloading. The desktop app accepts only HTTPS manifests and packages, enforces a 512 MB limit, verifies the declared byte size when present, and compares the complete SHA-256 digest before moving a package into the staged-update directory.

Production releases must additionally be Authenticode-signed by the NiiMotion publisher. The release pipeline creates an integrity manifest for the desktop executable and the OpenVR/OpenXR bridge components. Installation remains an explicit user action; a failed check leaves the running installation unchanged.
