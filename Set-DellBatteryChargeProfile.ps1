<#
Applies a custom battery charge profile on the Dell XPS 14 through DellBIOSProvider.
See README.md for context.

Usage:
  .\Set-DellBatteryChargeProfile.ps1 -Profile 60_65
  .\Set-DellBatteryChargeProfile.ps1 -Profile 75_80
  .\Set-DellBatteryChargeProfile.ps1 -Profile standard      # normal Dell charging, up to 100%
  .\Set-DellBatteryChargeProfile.ps1 -Profile fastcharge    # ExpressCharge (fast charging)
  .\Set-DellBatteryChargeProfile.ps1 -Profile 60_65 -WhatIf   # shows what it would do without writing anything

Must be run from a PowerShell opened as Administrator.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('60_65', '75_80', 'standard', 'fastcharge')]
    [string]$Profile,

    [string]$LogPath
)

$ErrorActionPreference = 'Stop'

if (-not $LogPath) {
    # $PSScriptRoot isn't reliable as a parameter default value when the script
    # starts from a Scheduled Task (different working directory, e.g. System32):
    # we compute it here in the script body, where it always resolves correctly.
    $LogPath = Join-Path $PSScriptRoot 'dell-battery-charge-log.jsonl'
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Error "This script must be run as Administrator (the DellSmbios provider requires elevation). Reopen PowerShell with 'Run as administrator' and run the script again."
    exit 1
}

if (-not (Get-Module -ListAvailable -Name DellBIOSProvider)) {
    Write-Error "The DellBIOSProvider module is not installed. Run first: Install-Module -Name DellBIOSProvider -Scope AllUsers -Force"
    exit 1
}

Import-Module DellBIOSProvider

if (-not (Test-Path DellSmbios:\PowerManagement)) {
    Write-Error "The DellSmbios:\PowerManagement path is not available. Your model/BIOS may not expose these settings, or the BIOS may need an update."
    exit 1
}

function Get-CurrentChargeState {
    [PSCustomObject]@{
        Timestamp          = [datetime]::UtcNow.ToString('o')
        PrimaryBattChargeCfg = (Get-Item DellSmbios:\PowerManagement\PrimaryBattChargeCfg -ErrorAction SilentlyContinue).CurrentValue
        CustomChargeStart    = (Get-Item DellSmbios:\PowerManagement\CustomChargeStart -ErrorAction SilentlyContinue).CurrentValue
        CustomChargeStop     = (Get-Item DellSmbios:\PowerManagement\CustomChargeStop -ErrorAction SilentlyContinue).CurrentValue
    }
}

# Read and log the current state BEFORE changing anything
$before = Get-CurrentChargeState
Write-Host "Current state: PrimaryBattChargeCfg=$($before.PrimaryBattChargeCfg) CustomChargeStart=$($before.CustomChargeStart) CustomChargeStop=$($before.CustomChargeStop)"
($before | ConvertTo-Json -Compress) | Add-Content -Path $LogPath

switch ($Profile) {
    '60_65'      { $cfg = 'Custom'; $start = 60; $stop = 65 }
    '75_80'      { $cfg = 'Custom'; $start = 75; $stop = 80 }
    'standard'   { $cfg = 'Standard'; $start = $null; $stop = $null }   # normal charging, up to 100%
    'fastcharge' { $cfg = 'Express'; $start = $null; $stop = $null }    # ExpressCharge (fast charging)
}

if ($cfg -eq 'Custom') {
    if ($start -lt 50 -or $start -gt 95) {
        Write-Error "CustomChargeStart out of range (50-95): $start"
        exit 1
    }
    if ($stop -lt 55 -or $stop -gt 100) {
        Write-Error "CustomChargeStop out of range (55-100): $stop"
        exit 1
    }
    if (($stop - $start) -lt 5) {
        Write-Error "Start and stop must be at least 5 percentage points apart (current: $($stop - $start))"
        exit 1
    }
}

if ($PSCmdlet.ShouldProcess("DellSmbios:\PowerManagement", "Apply profile '$Profile' (PrimaryBattChargeCfg=$cfg, Start=$start, Stop=$stop)")) {
    Set-Item -Path DellSmbios:\PowerManagement\PrimaryBattChargeCfg -Value $cfg

    if ($cfg -eq 'Custom') {
        # The BIOS validates each single write against the CURRENT value of the
        # other bound (not against the final pair): when moving from a lower range
        # to a higher one (e.g. 60-65 -> 75-80), writing Start first fails because
        # at that moment Stop still has the old value, lower than the new Start.
        # If the first order fails for this reason, we try the opposite order.
        try {
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
        } catch {
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
        }
    }

    $after = Get-CurrentChargeState
    Write-Host "New state:     PrimaryBattChargeCfg=$($after.PrimaryBattChargeCfg) CustomChargeStart=$($after.CustomChargeStart) CustomChargeStop=$($after.CustomChargeStop)"
    ($after | ConvertTo-Json -Compress) | Add-Content -Path $LogPath
}
