# Battery Charge Manager

A Windows tray app that switches the Dell XPS 14 battery charge profile and
the Dell Optimizer thermal mode (Optimized / Cool / Quiet / Ultra Performance)
with a single click from the system tray — no UAC prompts, and the tray app
itself never runs as administrator.

It relies on the existing, hand-tested PowerShell script
[`Set-DellBatteryChargeProfile.ps1`](Set-DellBatteryChargeProfile.ps1), which
talks to the BIOS through the `DellBIOSProvider` module, and on a persistent
elevated helper ([`BatteryChargeHelper.ps1`](BatteryChargeHelper.ps1)) that
applies the same logic without the per-click elevation cost — see
[Architecture](#architecture) below. The same helper also sets the thermal
mode, through Dell Optimizer's own CLI — see [Thermal mode](#thermal-mode).

## How it works (short version)

1. **One-time setup**, as administrator:
   [`setup-scheduled-tasks.ps1`](setup-scheduled-tasks.ps1) creates a single
   Windows Scheduled Task (`\BatteryChargeManager\Helper`), configured to run
   with the highest privileges but with **no automatic trigger** — it only
   starts on demand, and runs silently (no visible console window).
2. **Day to day**: the tray app (`TrayApp.exe`), which runs WITHOUT admin
   privileges, talks to that helper over a local named pipe when you pick a
   profile from the menu. The first switch of a login session starts the
   helper task (elevated, no UAC prompt — see below); every switch after that
   just sends a request to the already-running helper, no new process
   involved.

## Architecture

Earlier version of this app created one Scheduled Task per profile and ran a
brand-new elevated `powershell.exe` process for every single click. That
process creation cost **10-12 seconds** on real hardware — not the actual BIOS
write (`Import-Module DellBIOSProvider` + the `Set-Item` calls together take
well under a second), but the generic overhead of launching a *new elevated
process*, most likely real-time antivirus scanning of it. This was verified
to be independent of the Scheduled Task mechanism itself: manually elevating
the same command with `Start-Process -Verb RunAs` measured the same ~12s.

The fix: instead of a fresh process per click, a single persistent elevated
helper (`BatteryChargeHelper.ps1`) is started once — on the first profile
switch of a login session — and stays running in the background, listening
on a named pipe (`BatteryChargeManagerHelper`, ACL-restricted to the current
user's SID so no other local account/process can send it commands). It
imports `DellBIOSProvider` once and keeps the `DellSmbios:` drive mounted, so
every subsequent switch is just a round-trip over the pipe — typically under
2 seconds. The helper has no explicit time limit and simply exits when the
user logs off (it runs under a `LogonType Interactive` Scheduled Task, which
Windows terminates automatically at logoff).

Why the elevation still works without a UAC prompt: when a non-elevated
process starts an *already-registered* task with `RunLevel=Highest` through
the Task Scheduler API — not by launching the executable directly — the Task
Scheduler service (running as SYSTEM) creates the process with the user's
elevated token directly. It doesn't go through the interactive
AppInfo/consent-prompt path that triggers when a user manually elevates a
program. This only works because the current user is actually a member of
the local Administrators group.

## Thermal mode

The thermal mode is **not** a `DellBIOSProvider` attribute on this XPS 14
(`DellSmbios:\PowerManagement\ThermalManagement` does not exist). Dell
Optimizer sets it through the BIOS SMBIOS interface ("User Selectable Thermal
Tables"), so the helper goes through Dell Optimizer's official CLI instead:

```
"C:\Program Files\Dell\DellOptimizer\do-cli.exe" /configure -name=SystemPowerConfiguration.ThermalMode -value=<Optimized|Cool|Quiet|Ultra>
```

`do-cli.exe` requires administrator rights, which the helper already has. The
result is exactly the same as clicking the mode in Dell Optimizer's own UI —
including Dell Optimizer's sync with the Windows power mode (with "Sync with
Windows power mode" on, which is the default, Ultra also switches Windows to
"Best performance", and changing the Windows power mode can change the thermal
mode back). **Dell Optimizer must therefore stay installed.**

Over the pipe, a thermal request is `thermal:<mode>` (`thermal:optimized`,
`thermal:cool`, `thermal:quiet`, `thermal:ultra`); charge profile requests are
unchanged.

Since the mode can change outside this app (Dell Optimizer, Windows power
mode), the menu re-reads the current one every time it opens, without
elevation, from Dell Optimizer's
`%ProgramData%\{DataFolderName}\DellOptimizer\TelemetrySettings.json`
(`DataFolderName` is under `HKLM\SOFTWARE\DELL\DellOptimizer`). That's an
internal Dell file, not a documented interface: if it can't be read, the menu
falls back to the last mode applied by this app.

## Project structure

```
/BatteryChargeManager
  /TrayApp                             <- C# WinForms project (.NET 8)
  Set-DellBatteryChargeProfile.ps1     <- existing script, for manual/CLI use
  BatteryChargeHelper.ps1              <- persistent elevated helper (named pipe server): charge profiles + thermal mode
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

The script creates the `\BatteryChargeManager\Helper` Scheduled Task, pointing
at `BatteryChargeHelper.ps1`. It's idempotent: rerunning it is safe (e.g.
after moving the project folder) — it recreates the task and removes any
tasks left over from a previous version of this app (the old one-task-per-
profile layout), instead of duplicating or leaving stale ones around.

If the helper script lives somewhere else, pass `-HelperScriptPath`:

```powershell
.\setup-scheduled-tasks.ps1 -HelperScriptPath "D:\some\other\path\BatteryChargeHelper.ps1"
```

## 3. Day-to-day use

Launch `TrayApp.exe` (copy it wherever you like, e.g.
`%LOCALAPPDATA%\BatteryChargeManager\`). An icon appears in the system tray.
Right-click for the menu:

- **Battery charge**: **60-65 (minimal wear)** / **75-80** / **Standard
  (charges up to 100%)** / **Fast charge** — applies the corresponding profile
  (no UAC prompt, no visible window). The first switch of a session takes
  ~10-12s (the helper is starting up); every switch after that is typically
  under 2 seconds.
- **Performance** (thermal mode): **Optimized** / **Cool** / **Quiet** / **Ultra
  Performance** — same as picking it in Dell Optimizer. The checkmark shows
  the mode currently set in Dell Optimizer, even if it was changed from there.
- **Auto-start** — enables/disables the tray app starting at login (checkbox)
- **Exit**

The last successfully applied charge profile stays checked in the menu even
after restarting the app or the PC — it's purely a visual indicator, **it is
never reapplied automatically**. The same goes for the thermal mode: nothing
is reapplied at startup.

### Checking the current state

To verify what's actually set on the BIOS right now (independent of the tray
app's own state file), run, as administrator:

```powershell
.\Get-DellBatteryChargeState.ps1
```

It prints `PrimaryBattChargeCfg`, `CustomChargeStart`, `CustomChargeStop`, and
which of the 4 profiles they currently match. Read-only, changes nothing.

For the thermal mode, as administrator:

```powershell
& "C:\Program Files\Dell\DellOptimizer\do-cli.exe" /get -name=SystemPowerConfiguration.ThermalMode
```

### If something goes wrong

The interface is deliberately minimal: **no popups, no toast notifications**.
If a profile or thermal mode switch fails (helper task not found → setup
hasn't been run yet, connecting to the pipe timed out, or the helper rejected
the request — BIOS path unavailable, values out of range, `do-cli.exe`
missing or returning an error), the detail is written to:

```
%APPDATA%\BatteryChargeManager\errors.log
```

The last applied profile and thermal mode are stored in
`%APPDATA%\BatteryChargeManager\state.json`.

The detailed log the helper writes on every charge switch (state before/after)
is `dell-battery-charge-log.jsonl`, next to the scripts. Every thermal switch
(requested mode, `do-cli.exe` exit code, duration and output) goes to
`dell-thermal-mode-log.jsonl`, in the same folder.

After updating `BatteryChargeHelper.ps1`, an already-running helper keeps the
old code until it exits: log off and back in, or end it with
`schtasks /End /TN "\BatteryChargeManager\Helper"` (no admin needed) — the
next click starts it again.

## Known BIOS quirk: profile switch ordering

The DellBIOSProvider validates each write to `CustomChargeStart` /
`CustomChargeStop` against the *current* value of the other bound, not the
final pair — so moving from a lower custom range to a higher one (e.g.
60-65 → 75-80) fails if `Start` is written before `Stop` is raised. Both
`Set-DellBatteryChargeProfile.ps1` and `BatteryChargeHelper.ps1` handle this
by writing `Stop` first, then `Start`, and retrying with the opposite order
if the first attempt fails — this covers both directions (raising or
lowering the range).

## What this app does NOT do

- It does not modify `Set-DellBatteryChargeProfile.ps1`'s core logic.
- It never runs the tray app itself with administrator privileges.
- It shows no toast notifications, confirmation popups, or per-profile icons.
- It never reapplies a profile or thermal mode automatically on app or PC startup.
- It doesn't replace Dell Optimizer: the thermal mode goes through it.
