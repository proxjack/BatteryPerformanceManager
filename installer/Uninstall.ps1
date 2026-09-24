<#
Uninstalls Battery and Performance Manager: stops the app, removes the elevated
helper task, the Start menu shortcut, the Settings > Apps entry, auto-start and
the program folder.

Left as they are: your settings folder (%APPDATA%\BatteryPerformanceManager),
the DellBIOSProvider module (other Dell tools may use it) and the charge profile
and thermal mode currently set.
#>

$ErrorActionPreference = 'Stop'

$AppName = 'Battery and Performance Manager'
$InstallDir = Join-Path $env:ProgramFiles $AppName
$UninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BatteryPerformanceManager'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$ShortcutName = "$AppName.lnk"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-BeforeClosing {
    Write-Host ""
    Read-Host "Press Enter to close this window" | Out-Null
}

if (-not (Test-IsAdministrator)) {
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    } catch {
        Write-Warning "Administrator rights are required to uninstall $AppName."
        Wait-BeforeClosing
    }
    exit
}

try {
    Write-Host "Uninstalling $AppName" -ForegroundColor Cyan
    Write-Host ""

    Get-Process -Name 'BatteryPerformanceManager' -ErrorAction SilentlyContinue | Stop-Process -Force

    Write-Host "Removing the elevated helper task"
    Stop-ScheduledTask -TaskPath '\BatteryPerformanceManager\' -TaskName 'Helper' -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskPath '\BatteryPerformanceManager\' -TaskName 'Helper' -Confirm:$false -ErrorAction SilentlyContinue
    try {
        $scheduler = New-Object -ComObject Schedule.Service
        $scheduler.Connect()
        $scheduler.GetFolder('\').DeleteFolder('BatteryPerformanceManager', 0)
    } catch {
        # Folder already gone.
    }

    Write-Host "Removing auto-start, the Start menu shortcut and the Settings > Apps entry"
    Remove-ItemProperty -Path $RunKey -Name 'BatteryPerformanceManager' -ErrorAction SilentlyContinue
    foreach ($folder in 'CommonPrograms', 'Programs') {
        $shortcut = Join-Path ([Environment]::GetFolderPath($folder)) $ShortcutName
        if (Test-Path $shortcut) {
            Remove-Item -Path $shortcut -Force
        }
    }
    if (Test-Path $UninstallKey) {
        Remove-Item -Path $UninstallKey -Recurse -Force
    }

    Write-Host "Removing $InstallDir"
    # This script lives in that folder: PowerShell has already read it, so it can go too.
    Start-Sleep -Seconds 1
    Set-Location -Path $env:TEMP
    if (Test-Path $InstallDir) {
        Remove-Item -Path $InstallDir -Recurse -Force
    }

    Write-Host ""
    Write-Host "$AppName has been uninstalled." -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "Uninstall failed: $($_.Exception.Message)" -ForegroundColor Red
}

Wait-BeforeClosing
