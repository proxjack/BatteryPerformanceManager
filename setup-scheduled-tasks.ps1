<#
One-time setup for Battery and Performance Manager.

Creates ONE Scheduled Task ('\BatteryChargeManager\Helper') configured to run
with the highest privileges ("RunLevel = Highest") but WITHOUT any automatic
trigger: it only starts on demand (Start-ScheduledTask / Task.Run() via COM),
the first time you pick a profile or thermal mode from the tray app. From then
on it stays active in the background for the whole login session (it exits on
its own at logoff) and applies later switches in-process, without creating a
new elevated process on every click - which on this machine cost ~10-12
seconds (most likely because of real-time antivirus scanning of a freshly
created elevated process: verified independent of the Task Scheduler,
identical when launching manually with "Run as administrator").

Why this avoids the UAC prompt: when a NON-elevated process starts an already
registered task with RunLevel=Highest through the Task Scheduler API (not by
double-clicking the exe), it's the "Task Scheduler" service (running as
SYSTEM) that directly creates the process with the user's elevated token. It
doesn't go through the interactive "AppInfo/UAC consent" path that kicks in
when a user tries to elevate a program manually. That's why the tray app
(TrayApp, not administrator) can start this task without any prompt, as long
as the current user is actually a member of the local Administrators group.

Run this script ONCE, as administrator (right-click the file or the PowerShell
console > "Run as administrator"). Running it again later is safe: it removes
and recreates the task, so it updates any changed paths instead of
duplicating it or failing.
#>

[CmdletBinding()]
param(
    # Path to the helper script the task runs (don't change it).
    # By default it's assumed to be in the same folder as this setup script.
    [string]$HelperScriptPath = (Join-Path $PSScriptRoot 'BatteryChargeHelper.ps1')
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Error "This script must be run as Administrator. Reopen PowerShell with 'Run as administrator' and run it again."
    exit 1
}

if (-not (Test-Path $HelperScriptPath)) {
    Write-Error "Script not found: '$HelperScriptPath'. Pass the correct path with -HelperScriptPath, e.g.: .\setup-scheduled-tasks.ps1 -HelperScriptPath 'C:\path\to\BatteryChargeHelper.ps1'"
    exit 1
}
$HelperScriptPath = (Resolve-Path $HelperScriptPath).Path

$taskFolderPath = '\BatteryChargeManager\'
$taskName = 'Helper'
$currentUser = "$env:USERDOMAIN\$env:USERNAME"

Write-Host "Helper script run by the task: $HelperScriptPath"
Write-Host "Task owner: $currentUser"
Write-Host "Creating/updating task in '$taskFolderPath'..." -ForegroundColor Cyan
Write-Host ""

# Remove any tasks left over from a previous architecture (one per profile:
# 60_65/75_80/Standard/FastCharge), so the '\BatteryChargeManager\' folder
# always contains only the current task.
$obsolete = Get-ScheduledTask -TaskPath $taskFolderPath -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -ne $taskName }
foreach ($task in $obsolete) {
    Write-Host "Removing obsolete task: $taskFolderPath$($task.TaskName)" -ForegroundColor DarkYellow
    Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $taskFolderPath -Confirm:$false
}

# Idempotency: remove the existing task (if any) before recreating it, so any
# change to path/arguments/principal is applied cleanly.
$existing = Get-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath -ErrorAction SilentlyContinue
if ($existing) {
    Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath -Confirm:$false
}

$result = try {
    # -WindowStyle Hidden: no visible PowerShell window.
    $arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$HelperScriptPath`""

    # Explicit WorkingDirectory: without it, the task starts with its CWD in
    # System32, which in the past made $PSScriptRoot fail in a parameter default
    # value (see the comment in Set-DellBatteryChargeProfile.ps1).
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory (Split-Path -Parent $HelperScriptPath)

    # No -Trigger: the task never starts on its own, only on explicit request
    # (Start-ScheduledTask / Task Scheduler COM API from the tray app).
    $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest

    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit ([TimeSpan]::Zero)   # no time limit: it must stay active for the whole session

    Register-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath `
        -Action $action -Principal $principal -Settings $settings | Out-Null

    [PSCustomObject]@{ Task = "$taskFolderPath$taskName"; Result = 'OK' }
} catch {
    [PSCustomObject]@{ Task = "$taskFolderPath$taskName"; Result = "ERROR: $($_.Exception.Message)" }
}

$result | Format-Table -AutoSize | Out-String | Write-Host

if ($result.Result -eq 'OK') {
    Write-Host "Setup complete: task '$taskFolderPath$taskName' created successfully." -ForegroundColor Green
    Write-Host "You can now start TrayApp.exe without administrator privileges: on the first switch, the elevated helper will start in the background (no UAC prompt) and stay active for the rest of the session."
} else {
    Write-Warning "Setup failed. Check the message above."
    exit 1
}
