<#
Installs Battery and Performance Manager for the current user.

Double-click Install.cmd (next to this script): Windows asks once for
administrator rights, then this script:
  1. copies the app to "C:\Program Files\Battery and Performance Manager" - a
     folder only administrators can change, which matters because the helper
     script runs elevated without a UAC prompt;
  2. installs the DellBIOSProvider PowerShell module if it's missing (needed
     for the battery charge profiles);
  3. registers the elevated helper task (setup-scheduled-tasks.ps1);
  4. adds a Start menu shortcut, an entry in Settings > Apps and auto-start at
     login, then starts the app.
Running it again upgrades an existing installation in place.
#>

$ErrorActionPreference = 'Stop'

$AppName = 'Battery and Performance Manager'
$ExeName = 'BatteryPerformanceManager.exe'
$InstallDir = Join-Path $env:ProgramFiles $AppName
$UninstallKey = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\BatteryPerformanceManager'
$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$RunValue = 'BatteryPerformanceManager'   # must match AutoStart.ValueName in the app
$ShortcutName = "$AppName.lnk"
$Payload = @(
    $ExeName,
    'BatteryPerformanceHelper.ps1',
    'setup-scheduled-tasks.ps1',
    'Set-DellBatteryChargeProfile.ps1',
    'Get-DellBatteryChargeState.ps1',
    'Uninstall.ps1',
    'Uninstall.cmd',
    'README.txt',
    'LICENSE.txt'
)

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-BeforeClosing {
    Write-Host ""
    Read-Host "Press Enter to close this window" | Out-Null
}

function New-Shortcut([string]$Path, [string]$Target) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.WorkingDirectory = Split-Path -Parent $Target
    $shortcut.IconLocation = "$Target,0"
    $shortcut.Description = $AppName
    $shortcut.Save()
}

function Install-DellBiosProvider {
    if (Get-Module -ListAvailable -Name DellBIOSProvider) {
        Write-Host "DellBIOSProvider module: already installed"
        return
    }

    Write-Host "Installing the DellBIOSProvider PowerShell module from the PowerShell Gallery..."
    try {
        # Windows PowerShell 5.1 doesn't enable TLS 1.2 by default, which the Gallery requires.
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        $nuget = Get-PackageProvider -ListAvailable -Name NuGet -ErrorAction SilentlyContinue |
            Where-Object { $_.Version -ge [version]'2.8.5.201' }
        if (-not $nuget) {
            Install-PackageProvider -Name NuGet -MinimumVersion 2.8.5.201 -Scope AllUsers -Force | Out-Null
        }
        Install-Module -Name DellBIOSProvider -Scope AllUsers -Force
    } catch {
        Write-Warning "Could not install DellBIOSProvider: $($_.Exception.Message)"
        Write-Warning "Battery charge profiles won't work until it's installed. As administrator: Install-Module -Name DellBIOSProvider -Scope AllUsers -Force"
    }
}

function Test-DellFeatures {
    try {
        Import-Module DellBIOSProvider -ErrorAction Stop
        if (-not (Test-Path DellSmbios:\PowerManagement\PrimaryBattChargeCfg)) {
            Write-Warning "This BIOS doesn't expose the battery charge settings: the Battery charge tiles won't work on this PC."
        }
    } catch {
        Write-Warning "DellBIOSProvider can't be loaded ($($_.Exception.Message)): the Battery charge tiles won't work until this is fixed."
    }

    if (-not (Test-Path (Join-Path $env:ProgramFiles 'Dell\DellOptimizer\do-cli.exe'))) {
        Write-Warning "Dell Optimizer isn't installed: the Performance tiles need it. Get it from Dell's support site for your model."
    }
}

if (-not (Test-IsAdministrator)) {
    try {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    } catch {
        Write-Warning "Administrator rights are required to install $AppName."
        Wait-BeforeClosing
    }
    exit
}

try {
    Write-Host "Installing $AppName" -ForegroundColor Cyan
    Write-Host ""

    if ((Get-CimInstance Win32_ComputerSystem).Manufacturer -notmatch 'Dell') {
        Write-Warning "This doesn't look like a Dell PC: the app only works with Dell BIOS and Dell Optimizer settings."
    }

    foreach ($file in $Payload) {
        if (-not (Test-Path (Join-Path $PSScriptRoot $file))) {
            throw "Missing '$file' next to Install.ps1: extract the whole zip before installing."
        }
    }

    # Upgrade: stop the running app and helper so their files can be replaced.
    Get-Process -Name ([IO.Path]::GetFileNameWithoutExtension($ExeName)) -ErrorAction SilentlyContinue | Stop-Process -Force
    Stop-ScheduledTask -TaskPath '\BatteryPerformanceManager\' -TaskName 'Helper' -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 1

    Write-Host "Copying the app to $InstallDir"
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    foreach ($file in $Payload) {
        $target = Join-Path $InstallDir $file
        Copy-Item -Path (Join-Path $PSScriptRoot $file) -Destination $target -Force
        # Files extracted from a downloaded zip carry the "from the internet" mark.
        Unblock-File -Path $target
    }
    $exePath = Join-Path $InstallDir $ExeName

    Install-DellBiosProvider
    Test-DellFeatures

    Write-Host "Registering the elevated helper task"
    $global:LASTEXITCODE = 0
    & (Join-Path $InstallDir 'setup-scheduled-tasks.ps1') -HelperScriptPath (Join-Path $InstallDir 'BatteryPerformanceHelper.ps1')
    if ($LASTEXITCODE -ne 0) {
        throw "The helper task could not be registered (see the message above)."
    }

    Write-Host "Adding the Start menu shortcut"
    New-Shortcut -Path (Join-Path ([Environment]::GetFolderPath('CommonPrograms')) $ShortcutName) -Target $exePath
    # A per-user shortcut of the same name (e.g. from a manual setup) would show up twice in search.
    $userShortcut = Join-Path ([Environment]::GetFolderPath('Programs')) $ShortcutName
    if (Test-Path $userShortcut) {
        Remove-Item -Path $userShortcut -Force
    }

    Write-Host "Adding the entry in Settings > Apps"
    $version = (Get-Item $exePath).VersionInfo.ProductVersion
    $sizeKb = [int]((Get-ChildItem $InstallDir -File | Measure-Object -Property Length -Sum).Sum / 1KB)
    New-Item -Path $UninstallKey -Force | Out-Null
    $entry = @{
        DisplayName     = $AppName
        DisplayVersion  = $version
        Publisher       = 'Jacopo Garau'
        InstallLocation = $InstallDir
        DisplayIcon     = "$exePath,0"
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $InstallDir 'Uninstall.ps1')`""
        URLInfoAbout    = 'https://github.com/proxjack/BatteryPerformanceManager'
    }
    foreach ($name in $entry.Keys) {
        Set-ItemProperty -Path $UninstallKey -Name $name -Value $entry[$name]
    }
    foreach ($name in 'NoModify', 'NoRepair', 'EstimatedSize') {
        $value = if ($name -eq 'EstimatedSize') { $sizeKb } else { 1 }
        New-ItemProperty -Path $UninstallKey -Name $name -Value $value -PropertyType DWord -Force | Out-Null
    }

    Write-Host "Turning on auto-start at login (you can turn it off in the app)"
    Set-ItemProperty -Path $RunKey -Name $RunValue -Value "`"$exePath`""
    Remove-ItemProperty -Path $RunKey -Name 'BatteryChargeManagerTrayApp' -ErrorAction SilentlyContinue

    Write-Host "Starting the app"
    # Through Explorer, so the app runs with normal (non-administrator) rights, as it's meant to.
    Start-Process -FilePath 'explorer.exe' -ArgumentList "`"$exePath`""

    Write-Host ""
    Write-Host "Done. Click the new icon in the system tray (next to the clock) to open the app." -ForegroundColor Green
} catch {
    Write-Host ""
    Write-Host "Installation failed: $($_.Exception.Message)" -ForegroundColor Red
}

Wait-BeforeClosing
