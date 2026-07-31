<#
Legge e mostra lo stato attuale della configurazione di ricarica batteria via
DellBIOSProvider, senza modificare nulla. Utile per verificare che il profilo
applicato dalla tray app (o dallo script) corrisponda a quanto atteso.

Uso:
  .\Get-DellBatteryChargeState.ps1

Deve essere eseguito in una PowerShell aperta come Amministratore
(il provider DellSmbios richiede elevazione anche solo per leggere).
#>

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Error "Questo script deve essere eseguito come Amministratore (il provider DellSmbios richiede elevazione). Riapri PowerShell con 'Esegui come amministratore' e rilancia lo script."
    exit 1
}

if (-not (Get-Module -ListAvailable -Name DellBIOSProvider)) {
    Write-Error "Il modulo DellBIOSProvider non è installato. Esegui prima: Install-Module -Name DellBIOSProvider -Scope AllUsers -Force"
    exit 1
}

Import-Module DellBIOSProvider

if (-not (Test-Path DellSmbios:\PowerManagement)) {
    Write-Error "Il percorso DellSmbios:\PowerManagement non è disponibile su questo BIOS."
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

$profile = switch ($cfg) {
    'Custom'   { if ($start -eq 60 -and $stop -eq 65) { '60_65' } elseif ($start -eq 75 -and $stop -eq 80) { '75_80' } else { "Custom non standard ($start-$stop)" } }
    'Standard' { 'standard' }
    'Express'  { 'fastcharge' }
    default    { "sconosciuto ($cfg)" }
}
Write-Host "Corrisponde al profilo: $profile" -ForegroundColor Green
