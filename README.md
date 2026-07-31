# Battery Charge Manager

A Windows tray app that switches the Dell XPS 14 battery charge profile with a
single click from the system tray — no UAC prompts, and the tray app itself
never runs as administrator.

It relies on the existing, hand-tested PowerShell script
[`Set-DellBatteryChargeProfile.ps1`](Set-DellBatteryChargeProfile.ps1), which
talks to the BIOS through the `DellBIOSProvider` module.

## How it works (short version)

1. **One-time setup**, as administrator:
   [`setup-scheduled-tasks.ps1`](setup-scheduled-tasks.ps1) creates 4 Windows
   Scheduled Tasks (one per profile), configured to run with the highest
   privileges but with **no automatic trigger** — they only start on demand,
   and run silently (no visible console window).
2. **Day to day**: the tray app (`TrayApp.exe`), which runs WITHOUT admin
   privileges, starts the right Scheduled Task when you pick a profile from
   the menu. Since the task is already registered as elevated, the Task
   Scheduler service (which runs as SYSTEM) launches it with the user's
   elevated token directly, without showing a UAC prompt — the tray app
   itself never needs to be elevated.

## Project structure

```
/BatteryChargeManager
  /TrayApp                             <- C# WinForms project (.NET 8)
  Set-DellBatteryChargeProfile.ps1     <- existing script, not modified by the app
  setup-scheduled-tasks.ps1            <- one-time setup script
  Get-DellBatteryChargeState.ps1       <- read-only script to check the current charge state
  README.md
```

## 1. Build the tray app

Requires the .NET 8 SDK (not just the runtime). Check with:

```bash
dotnet --list-sdks
```

If no `8.0.x` line shows up, install the SDK from
https://aka.ms/dotnet/download (or `winget install Microsoft.DotNet.SDK.8`).

From the `TrayApp` folder:

```bash
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true
```

The standalone executable (single-file, no .NET dependency to install for the
end user) is produced at:

```
TrayApp\bin\Release\net8.0-windows\win-x64\publish\TrayApp.exe
```

If `dotnet restore` fails to resolve the `TaskScheduler` NuGet package at the
version pinned in `TrayApp.csproj`, bump it to the latest available version:

```bash
dotnet add TrayApp.csproj package TaskScheduler
```

## 2. One-time setup (as administrator)

Open PowerShell **as administrator** and run:

```powershell
cd "BatteryChargeManager"
.\setup-scheduled-tasks.ps1
```

The script creates 4 Scheduled Tasks under `\BatteryChargeManager\` (`60_65`,
`75_80`, `Standard`, `FastCharge`), each configured to run
`Set-DellBatteryChargeProfile.ps1` with the matching profile. It's
idempotent: rerunning it is safe (e.g. after moving the project folder or
changing the profile list) — it updates existing tasks and removes any left
over from a previous profile set, instead of duplicating or leaving stale
ones around.

If the source script lives somewhere else, pass `-ScriptPath`:

```powershell
.\setup-scheduled-tasks.ps1 -ScriptPath "D:\some\other\path\Set-DellBatteryChargeProfile.ps1"
```

## 3. Day-to-day use

Launch `TrayApp.exe` (copy it wherever you like, e.g.
`%LOCALAPPDATA%\BatteryChargeManager\`). An icon appears in the system tray.
Right-click for the menu:

- **60-65 (minimal wear)** / **75-80** / **Standard (charges up to 100%)** /
  **Fast charge** — applies the corresponding profile (no UAC prompt, no
  visible window)
- **Auto-start** — enables/disables the tray app starting at login (checkbox)
- **Exit**

The last successfully applied profile stays checked in the menu even after
restarting the app or the PC — it's purely a visual indicator, **it is never
reapplied automatically**.

### Checking the current state

To verify what's actually set on the BIOS right now (independent of the tray
app's own state file), run, as administrator:

```powershell
.\Get-DellBatteryChargeState.ps1
```

It prints `PrimaryBattChargeCfg`, `CustomChargeStart`, `CustomChargeStop`, and
which of the 4 profiles they currently match. Read-only, changes nothing.

### If something goes wrong

The interface is deliberately minimal: **no popups, no toast notifications**.
If a profile switch fails (task not found → setup hasn't been run yet, or the
script itself failed — BIOS path unavailable, values out of range), the
detail is written to:

```
%APPDATA%\BatteryChargeManager\errors.log
```

The last applied profile is stored in
`%APPDATA%\BatteryChargeManager\state.json`.

The detailed log the script writes on every run (state before/after) is the
existing `dell-battery-charge-log.jsonl`, next to the script.

## Known BIOS quirk: profile switch ordering

The DellBIOSProvider validates each write to `CustomChargeStart` /
`CustomChargeStop` against the *current* value of the other bound, not the
final pair — so moving from a lower custom range to a higher one (e.g.
60-65 → 75-80) fails if `Start` is written before `Stop` is raised. The
script handles this by writing `Stop` first, then `Start`, and retrying with
the opposite order if the first attempt fails — this covers both directions
(raising or lowering the range).

## What this app does NOT do

- It does not modify `Set-DellBatteryChargeProfile.ps1`'s core logic.
- It never runs the tray app itself with administrator privileges.
- It shows no toast notifications, confirmation popups, or per-profile icons.
- It never reapplies a profile automatically on app or PC startup.
