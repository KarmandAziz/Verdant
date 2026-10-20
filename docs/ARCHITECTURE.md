# Architecture

## Projects

`Verdant.Core` contains configuration validation and persistence, profile resolution, input ownership, macro scheduling, and hardware macro encoding.

`Verdant.App` contains the WPF editor, native Windows input adapters, device enumeration, tray integration, hardware coordination, and recovery process.

`Verdant.Tests` is a standalone core test executable. It uses a recording input sink and does not send real keyboard or mouse input.

## Input ownership

Each action owns the keys and mouse buttons it holds. Shared ownership prevents one action from releasing another action's output. Cancellation releases owned outputs and prevents further scheduled presses.

Ordinary same-button mouse toggles adopt the original physical mouse-down. The next physical click supplies the release. The editor remains usable outside its own remapping rules.

Software extra keys run as companion actions linked to the main toggle and stop with it.

## Hardware extra keys

The supported USB Basilisk V3 path uses HID feature reports directly, without installing another application or driver. Protocol adaptation is credited in the third-party notice.

A worker allocates and verifies a temporary macro, records a recovery lease, and arms the front side button. The repeating payload contains only balanced keyboard presses. A Raw Input receiver attributes the first key to the supported mouse and starts a single Windows mouse hold.

The worker restores the normal button mapping during playback. The next click releases the mouse hold. A verified device-mode cycle cancels keyboard playback before the session is rearmed. Restoring the mapping alone is not sufficient to stop firmware playback.

Pause, profile changes, editor focus, and shutdown restore the device and remove the owned macro. The guardian attempts the same recovery after parent-process termination. Recovery depends on the device remaining accessible.

## Persistence and privacy

Configuration lives under `%LOCALAPPDATA%\Verdant`. Writes use temporary files and replacement with backups. Recovery leases and local diagnostic logs stay on the user's machine.

No production configuration, test capture, device-instance path, or local build output is part of the public source tree. Generic USB vendor/product identifiers are protocol constants, not individual-device identifiers.
