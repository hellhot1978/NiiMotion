# Troubleshooting

- **PS Move slept:** press its large Move button. The sensor source searches for the assigned stable identity every 750 ms and reconnects automatically. Left is red; right is blue.
- **Phone data is stale:** keep owoTrack open, verify the PC address and UDP port, then use Phone Connection again.
- **Virtual Desktop appears open but VR is unavailable:** connect the Quest to the PC inside Virtual Desktop before asking NiiMotion to start SteamVR.
- **SteamVR error 1114:** do not launch SteamVR first. Let NiiMotion wait for the stable Virtual Desktop session and start it in order.
- **Unexpected shutdown:** reopen NiiMotion. Output is forced to zero before recovery and the previous unclean session is reported.

System Diagnostics produces a privacy-redacted support archive without raw motion recordings.
