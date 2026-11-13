# X-Power 2.0

A new, independent Xbox Series X UWP application for Developer Mode. This repository does not reuse or modify the legacy X-Files codebase.

## Current application scope

- Xbox-first XAML interface with Overview, File Explorer, Phone Link, Media Player, and Preferences.
- Directional focus navigation designed for Xbox controller input.
- D-pad and left thumbstick navigation use native UWP XY focus; A selects and B returns.
- File picker selection with duplicate detection based on StorageFile identity. Selection is session-only and does not modify files.
- Local audio playback for selected MP3, WAV, WMA, and M4A files.
- Dark and light palettes applied to resource-based and formerly hard-coded UI colors.
- LAN health probe using an OS-selected TCP port and a read-only `GET /health` response.

## Phone Link and security boundaries

Phone Link is currently a LAN health check only. Xbox UWP does not support the NetworkInformation connectivity APIs, so the user enters the console IPv4 address from **Settings > Network > Advanced settings**. The app asks the OS to select an available TCP port and displays the health URL.

The health endpoint uses HTTP and returns non-sensitive status information only. It does **not** provide HTTPS/WSS, device pairing, authentication, remote control, or file transfer. Do not send secrets or private data to it. Those capabilities require a separately designed and Xbox-tested secure transport before they can be enabled.

## Platform notes

- This is a UWP app, not a GDK title. Do not add `MicrosoftGame.config`, XVC packaging, or GDK-only networking APIs.
- `Application.RequiresPointerMode = WhenRequested` makes XY focus navigation the default Xbox interaction mode. UWP maps D-pad and left-stick directions to arrow-key navigation.
- File access is limited to files explicitly selected through the system picker. No broad file-system access is assumed.
- The local listener requires `privateNetworkClientServer`. Actual inbound LAN reachability must still be verified on a physical Xbox and the user's network.

## Design workspaces

- Figma: https://www.figma.com/design/g5XvK79Ews947gwcKNZW14
- MagicPath: https://www.magicpath.ai/files/471841162215460864

## Build and validation

Open `XPower.sln` in Visual Studio with the Universal Windows Platform development workload and Windows 10 SDK installed. Build the `x64` configuration.

GitHub Actions validates project XML/XAML and packages the UWP x64 app for Xbox Developer Mode. A successful CI package is not a substitute for installing and testing the app on a physical Xbox Series X.
