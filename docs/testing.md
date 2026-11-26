# Testing BlueGauge

## Automated checks

Run .\scripts\build.ps1 -Test in Windows PowerShell. The test executable covers battery bounds, unknown and disconnected values, the lowest-level selection, notification hysteresis and per-device state, Bluetooth address parsing, demo mode, and rendering tray icons. No test framework download is required.

Run .\scripts\build.ps1 -Live on a Windows machine to also exercise WinRT enumeration and the native PnP reader. A passing live check with no battery readings does not establish device compatibility.

## Manual hardware checklist

| Scenario | Expected outcome |
| --- | --- |
| Paired device is turned off | The card shows disconnected and does not contribute a value to the tray. |
| A supported Classic headset reports battery in Windows | After refresh, the headset appears once with the battery reading. |
| A connected BLE device exposes the standard Battery Service | The app displays a 0–100% reading or an unavailable state if the device denies access. |
| Several accessories are connected | The tray shows the lowest known percentage; each card has its own state. |
| A level reaches the configured threshold | One system notification is requested; stable low readings do not keep repeating it. |
| Dashboard is closed and reopened from the tray | Monitoring continues without launching another instance. |
| Autostart enabled, then disabled | Next sign-in starts the live app in the tray only while the option is enabled. |

For a compatibility report, include the device make/model, Windows version, whether Windows Bluetooth Settings shows the battery level, and the expected versus actual behavior. Avoid posting Bluetooth addresses or other identifiers.
