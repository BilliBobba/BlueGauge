# Changelog

## 1.2.0

- Added an optional Windows 11 battery panel to the left of the centered Start button.
- Connected devices without a battery percentage are shown with a dash; disconnected pairings stay hidden.
- The taskbar panel uses a `+N` indicator and a full device tooltip when space is limited.
- Fixed taskbar detection in the published single-file executable and rounded the panel's window shape.
- Ignore stale Bluetooth connection flags; identify the wired HECATE G2 II as USB-powered.
- Keep sleeping mice visible while their receiver is plugged in and mark the last known percentage with `*`.
- Smooth the panel edges with per-pixel transparency and size the panel to the actual labels instead of fixed columns.
- Detect attached USB keyboards and add a setting to hide wired USB devices without hiding 2.4 GHz receivers.

## 1.1.0

- Moved the WinForms app and test harness to .NET 11, with a self-contained x64 release.
- Replaced the dashboard with a compact dark window without the standard title bar.
- Show live battery readings instead of listing all paired devices.
- Added Windows HID battery polling every two seconds for supported ARDOR receivers, including Phantom PRO V2 (3554:F52D).
- Added a Phantom cable profile and an Areson profile for the Renegade, with protocol and visibility tests.

## 1.0.0

- Initial Windows release: tray percentage, per-device dashboard, and low-battery notifications.
- Windows PnP and BLE GATT battery data, with explicit unavailable and disconnected states.
- Local preferences, optional autostart, English and Russian UI, and a hardware-independent demo mode.
- Portable build, reproducible screenshot, and automated tests on Windows.
