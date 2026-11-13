# X-Power 2.0

A clean, independent Xbox Series X UWP application foundation.

## Project boundaries
This repository is the new X-Power project. It does not reuse or modify the legacy X-Files UWP codebase.

## Foundation milestone
- Minimal UWP application shell targeting Xbox Series X Developer Mode
- Hybrid navigation shell: sidebar + top bar
- Starter dashboard with storage, recent-items, and quick-access placeholders
- Basic section navigation and controller-friendly focusable buttons
- Responsive XAML layout that scales with the available display area
- Initial dark visual theme

This milestone intentionally does **not** implement actual drive enumeration, file operations, phone pairing, file transfer, Download from URL, media playback, or a light-theme switch. Those will be added after the first Xbox build and runtime test pass.

## Design workspaces
- Figma UI file: https://www.figma.com/design/HBekBbLnmyb6gTPw0zQweT
- MagicPath foundation board: https://www.magicpath.ai/files/471841162215460864

## Build
Open `XPower.sln` in Visual Studio with the Universal Windows Platform development workload and Windows 10 SDK installed. Build the `x64` configuration.

GitHub Actions contains an initial Windows x64 compile workflow. A successful run still needs to be confirmed before treating this foundation as build-verified.

## Platform notes
This is a UWP app, not a Win32 desktop app. Storage access is subject to Xbox/UWP capabilities and user-granted file access. No unrestricted drive access is assumed.
