# Verdant

A native Windows 11 mouse and macro manager built with C# and WPF.

## Features

- Assign standard mouse buttons to mouse actions, keys, shortcuts, macros, profiles, or layers.
- Toggle a held action with one click, then release it with the next click.
- Record and edit macros with delays and balanced key/button releases.
- Repeat an extra key while a toggle is active.
- Use optional hardware keyboard playback on the USB Razer Basilisk V3 (`1532:0099`).
- Pause from the editor, tray, or **Ctrl+Alt+Pause**.
- Recover owned inputs after a process crash through a companion Verdant recovery process.

No accounts, telemetry, cloud service, Synapse, or separate macro application are required. The executable requests administrator access for compatibility with elevated applications. Builds are unsigned.

## Build

Requirements: Windows 11 x64 and the .NET 10 SDK.

```powershell
./build.ps1
```

This runs the core tests, publishes a self-contained application, and creates `Verdant-0.1.0-win-x64.zip`. It does not install or launch Verdant.

To run the core tests separately:

```powershell
dotnet run --project tests/Verdant.Tests -c Release
```

Open `Verdant.slnx` to develop. Native input checks require an interactive Windows desktop and, for hardware checks, the supported physical mouse. They are not run by the build script.

## Run

Run the built `release/Verdant.exe` and approve the Windows administrator prompt. Alternatively, extract the generated package and run `Install.cmd` for a per-user installation and Start menu shortcut.

Settings are stored locally in `%LOCALAPPDATA%\Verdant`. This repository contains no saved user settings, captured input logs, device-instance identifiers, screenshots, or precompiled binaries.

## Mouse toggle and extra key

1. Open **Customize** and select a mouse button.
2. Set its desired mouse or keyboard action and enable **Toggle**.
3. Click once outside the editor to hold the action; click again to release it.
4. To repeat a separate key, expand **Extra key**, enable it, choose the shortcut and timing, then save.

Opening the editor, pausing, or changing profiles stops active actions.

### Optional Basilisk V3 hardware playback

For the USB Basilisk V3, assign the front side button to its own Forward mouse action in Toggle mode, then enable **Use mouse hardware** for the extra key.

The mouse emits a keyboard-only sequence. Verdant recognizes the first key from that device, creates a single Windows mouse hold, and stops both on the next physical click. Other keyboard input does not start the hold. The original button mapping is restored during playback so the OFF click remains available.

Allow a moment for hardware preparation after leaving the editor. Hardware playback is opt-in and disabled in a new configuration. Compatibility with a particular game or application must be tested there.

## Scope

Standard mouse assignments apply globally across mice. The device selector is informational. General onboard profile editing, vendor-only buttons, hardware DPI, polling rate, and lighting are not implemented. No kernel driver is included.

This is a prerelease. Device disconnects, operating-system failure, other mouse models, and every possible input race are not covered by the existing validation.

See [architecture](docs/ARCHITECTURE.md), [validation](docs/VALIDATION.md), and [third-party notices](THIRD-PARTY-NOTICES.txt).
