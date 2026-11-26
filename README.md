# BlueGauge

**See your Bluetooth battery levels at a glance — right in the Windows tray.**

[Русская версия](README.ru.md) · [License: MIT](LICENSE)

![BlueGauge in demo mode: four paired devices, with a controller at 16%](assets/dashboard.png)

## What it does

- Shows the lowest known battery percentage among connected accessories in the tray. Left-click for the dashboard; right-click for refresh, settings and exit.
- Shows connected, disconnected and unavailable readings separately. No made-up percentages.
- Sends a low-battery notification at a configurable threshold. To avoid repeat alerts, a device must recover by five percentage points above the threshold before it can alert again.
- Supports refresh intervals of 30, 60 or 120 seconds, manual refresh, optional start with Windows and English/Russian UI.
- Runs locally: no accounts, telemetry, ads, subscriptions or administrator rights.

## Run on Windows

Download and extract `BlueGauge-v1.0.0-portable.zip` from **Releases**, or build from source with the steps below. Run `BlueGauge.exe` and look for the icon near the clock; if Windows hides it under **^**, drag it out to make it visible. Closing the dashboard leaves the app active in the tray. Choose **Exit** to stop it. The executable is unsigned.

Windows 10/11 with .NET Framework 4.8 or later is required. BlueGauge works with Bluetooth Classic and Bluetooth LE devices that expose battery data to Windows. It does not implement vendor-specific protocols, headset component breakdowns, USB or 2.4 GHz dongle monitoring.

## Demo and screenshot

```powershell
.\BlueGauge.exe --demo
.\BlueGauge.exe --snapshot assets\dashboard.png --en
```

Demo mode does not read Bluetooth hardware, show low-battery notifications or change settings on disk. The language follows Windows; use `--en` or `--ru` to override it.

## Build and test

Requirements: Visual Studio 2022 or Build Tools with **.NET desktop development**, the **.NET Framework 4.8 targeting pack**, and a Windows 10/11 SDK. Use Windows PowerShell from the repository root:

```powershell
.\scripts\build.ps1 -Test
```

The result is `artifacts\BlueGauge.exe`. There are no NuGet packages. Use `-Live` instead of `-Test` to also exercise real Windows device discovery and PnP enumeration. The [Windows CI workflow](.github/workflows/windows.yml) builds the executable and runs tests without Bluetooth hardware.

## How it works

| Layer | Design |
| --- | --- |
| Discovery | WinRT enumerates paired Bluetooth Classic and LE Association Endpoints. Addresses and device container IDs merge duplicate endpoints rather than relying on device names. |
| Battery data | SetupAPI reads the Windows Bluetooth PnP battery property when present. For connected LE devices without it, BlueGauge tries GATT Battery Service (`180F` / `2A19`) with cancellation and bounded concurrency. |
| App | The WinForms UI uses a battery-source interface shared by live and demo modes. Background refreshes do not block the UI. Preferences live in `%LOCALAPPDATA%\BlueGauge\settings.json`. Optional autostart is a current-user Run entry. |

The Windows PnP battery property is an implementation detail of the driver stack and may be unavailable for some devices. See the [hardware test checklist](docs/testing.md) to verify a model or report a compatibility issue.
