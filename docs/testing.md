# Testing BlueGauge

Run `.\scripts\build.ps1 -Test` from the repository root (Windows PowerShell). That runs the battery model, visibility, notification, address parsing, tray icon, demo and USB frame checks without requiring devices.

Use `-Live` to also exercise Windows Bluetooth discovery and the USB HID reader. An empty result is valid when no supported device is active. To confirm a USB device, look for its name and percentage in the diagnostic output rather than just `PASS LiveHid`.

## Manual hardware checklist

| Scenario | Expected outcome |
| --- | --- |
| Active Phantom PRO V2 through USB receiver `3554:F52D` | A single card marked **2.4 GHz receiver** appears. The app requests the level every two seconds; compare with the vendor percentage while the mouse is awake. |
| Switch off the mouse or unplug the receiver | The card and its contribution to the tray number disappear after a poll; a sleeping mouse may return after movement. |
| Phantom connected by cable `3554:F52E` | The cable profile is tried before its receiver, so only one Phantom card can be shown. This connection still needs hardware verification. |
| Renegade through `25A7:FA4A` | Compatible firmware should report in increments of ten. This profile is based on the sample in BatteryTray issue #18; it has not been tested on a physical Renegade in BlueGauge. |
| Bluetooth device is only paired, disconnected, or does not report a battery level | It does not appear in the dashboard. |
| A connected Bluetooth device reports a level through Windows or BLE GATT | It is shown once with its actual percentage. |
| Close with the custom × and open again from the tray | The dashboard is hidden, monitoring continues, and reopening does not start another instance. |

## Comparing with the ARDOR utility

BlueGauge shows the percentage from the Phantom receiver's `GetPower` reply without rounding or applying an offset. For example, a verified reply contained `0x37` (55 decimal), which BlueGauge displays as 55%. The vendor utility may refresh at a different time or process the reading differently; its rounding and refresh behavior have not been verified. Do not assume a fixed correction from one comparison.

For a side-by-side check, wake the mouse and read both apps without changing the connection or charging state. Refresh BlueGauge and compare again over several polls. The HID-only diagnostic command is:

```powershell
dotnet run --project tests/BlueGauge.Tests/BlueGauge.Tests.csproj -c Release -- --hid
```

For compatibility reports, include the device model, transport, Windows version, and both readings with their timestamps. Do not post Bluetooth addresses or serial numbers.
