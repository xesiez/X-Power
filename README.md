# X-Power 2.0

A clean, independent Xbox Series X UWP application foundation.

## Current scope
This repository is the new X-Power project. It does not reuse or modify the legacy X-Files UWP codebase.

The first milestone is intentionally small:
- Stable UWP application shell for Xbox Series X Developer Mode
- Hybrid navigation: sidebar + top bar
- Minimal dashboard and a starter file-explorer screen
- Xbox-controller-friendly focus and navigation
- Dark-first foundation with a light theme toggle
- Responsive layout targeting 3840×2160 and 1920×1080

Advanced capabilities (phone control, file transfer, Download from URL, dual-pane explorer, and media playback) are planned for later milestones after the foundation is built and tested.

## Build
Open `XPower.sln` in Visual Studio with the Universal Windows Platform development workload and Windows 10 SDK installed. Build the `x64` configuration.

GitHub Actions runs the initial x64 compile check on pushes and pull requests.

## Platform notes
This is a UWP app, not a Win32 desktop app. Storage access is subject to Xbox/UWP capabilities and user-granted file access. No unrestricted drive access is assumed.
