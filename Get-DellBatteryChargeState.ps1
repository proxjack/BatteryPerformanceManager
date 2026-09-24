<#
Reads and shows the current battery charge configuration through
DellBIOSProvider, without changing anything. Useful to check that the profile
applied by the tray app (or by the script) matches what's expected.

Usage:
  .\Get-DellBatteryChargeState.ps1

Must be run from a PowerShell opened as Administrator
(the DellSmbios provider requires elevation even just to read).
#>

$ErrorActionPreference = 'Stop'

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
    Write-Error "The DellSmbios:\PowerManagement path is not available on this BIOS."
    exit 1
}

$cfg   = (Get-Item DellSmbios:\PowerManagement\PrimaryBattChargeCfg -ErrorAction SilentlyContinue).CurrentValue
$start = (Get-Item DellSmbios:\PowerManagement\CustomChargeStart -ErrorAction SilentlyContinue).CurrentValue
$stop  = (Get-Item DellSmbios:\PowerManagement\CustomChargeStop -ErrorAction SilentlyContinue).CurrentValue

Write-Host ""
Write-Host "PrimaryBattChargeCfg : $cfg" -ForegroundColor Cyan
Write-Host "CustomChargeStart    : $start" -ForegroundColor Cyan
Write-Host "CustomChargeStop     : $stop" -ForegroundColor Cyan
Write-Host ""

$matchedProfile = switch ($cfg) {
    'Custom'   { if ($start -eq 60 -and $stop -eq 65) { '60_65' } elseif ($start -eq 75 -and $stop -eq 80) { '75_80' } else { "non-standard Custom ($start-$stop)" } }
    'Standard' { 'standard' }
    'Express'  { 'fastcharge' }
    default    { "unknown ($cfg)" }
}
Write-Host "Matches profile: $matchedProfile" -ForegroundColor Green
