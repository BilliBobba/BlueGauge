# BlueGauge

**Live battery readings for supported wireless devices in the Windows tray.**

[Русская версия](README.ru.md) · [License: MIT](LICENSE) · [Third-party notices](THIRD-PARTY-NOTICES.md)

![BlueGauge dashboard in demo mode](assets/dashboard.png)

## How to use it

Run the portable `BlueGauge.exe` from a release or build it below. Left-click the tray icon to open the compact dashboard. Right-click to refresh, change settings or exit. Closing the dashboard leaves the app running. If Windows hides the icon under **^**, drag it next to the clock.

The tray displays the lowest known level. The dashboard lists only active devices that provide battery or charging data, not everything paired with Windows. Low-battery alerts have a configurable threshold. English and Russian are included. No account, subscription, cloud, telemetry or administrator rights are needed.

## Device support

| Device or protocol | Connection | Refresh |
| --- | --- | --- |
| ARDOR GAMING Phantom PRO V2 (`3554:F52D`) | 2.4 GHz USB receiver, tested with real hardware | Every 2 seconds |
| ARDOR GAMING Phantom PRO V2 (`3554:F52E`) | USB cable, ID confirmed in the vendor config; hardware reading not verified | Every 2 seconds |
| ARDOR GAMING Renegade (`25A7:FA4A`) | 2.4 GHz USB receiver; BatteryTray report and sample frame, not tested on this PC | Every 2 seconds; the mouse reports in 10% steps |
| Accessories exposing battery to Windows or via standard BLE GATT | Bluetooth Classic / BLE | Configurable: 30, 60 or 120 seconds |

The USB provider sends battery and link-status queries on known HID collections. Updates reflect the percentage the device reports, not an estimate; a sleeping or disconnected mouse may stop answering. Other ARDOR models and USB receivers are not implied to work. See [hardware checks](docs/testing.md) for verification steps.

## Demo, build and tests

The portable x64 release targets .NET 11 on Windows 10 (build 19041+) and Windows 11. It includes the .NET Desktop Runtime; there is nothing else to install. To build from source, install the .NET 11 SDK specified in `global.json` and run from the repository root:

    .\scripts\build.ps1 -Test
    .\artifacts\win-x64\BlueGauge.exe --demo --en

Use `-Live` instead of `-Test` to add Bluetooth API and USB HID checks. The first build restores Microsoft targeting/runtime packs. The portable executable is unsigned. The [Windows CI workflow](.github/workflows/windows.yml) runs the hardware-independent checks and publishes the x64 build.

The WinForms UI combines WinRT / SetupAPI / GATT Bluetooth readings with a separate, faster Windows HID polling path. Preferences are local in `%LOCALAPPDATA%/BlueGauge/settings.json`; optional autostart uses the current-user Run key.
