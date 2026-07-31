<#
Applica un profilo di ricarica batteria custom sul Dell XPS 14 via DellBIOSProvider.
Vedi dell-battery-charge-test.md per il contesto.

Uso:
  .\Set-DellBatteryChargeProfile.ps1 -Profile 60_65
  .\Set-DellBatteryChargeProfile.ps1 -Profile 75_80
  .\Set-DellBatteryChargeProfile.ps1 -Profile standard      # ricarica normale Dell, fino al 100%
  .\Set-DellBatteryChargeProfile.ps1 -Profile fastcharge    # ExpressCharge (ricarica rapida)
  .\Set-DellBatteryChargeProfile.ps1 -Profile 60_65 -WhatIf   # mostra cosa farebbe senza scrivere nulla

Deve essere eseguito in una PowerShell aperta come Amministratore.
#>

[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('60_65', '75_80', 'standard', 'fastcharge')]
    [string]$Profile,

    [string]$LogPath = (Join-Path $PSScriptRoot 'dell-battery-charge-log.jsonl')
)

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
    Write-Error "Il percorso DellSmbios:\PowerManagement non è disponibile. Il tuo modello/BIOS potrebbe non esporre queste impostazioni, o il BIOS necessita di un aggiornamento."
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

# Passo 3: leggi e logga lo stato attuale PRIMA di modificare qualsiasi cosa
$before = Get-CurrentChargeState
Write-Host "Stato attuale: PrimaryBattChargeCfg=$($before.PrimaryBattChargeCfg) CustomChargeStart=$($before.CustomChargeStart) CustomChargeStop=$($before.CustomChargeStop)"
($before | ConvertTo-Json -Compress) | Add-Content -Path $LogPath

switch ($Profile) {
    '60_65'      { $cfg = 'Custom'; $start = 60; $stop = 65 }
    '75_80'      { $cfg = 'Custom'; $start = 75; $stop = 80 }
    'standard'   { $cfg = 'Standard'; $start = $null; $stop = $null }   # ricarica normale, fino al 100%
    'fastcharge' { $cfg = 'Express'; $start = $null; $stop = $null }    # ExpressCharge (ricarica rapida)
}

if ($cfg -eq 'Custom') {
    if ($start -lt 50 -or $start -gt 95) {
        Write-Error "CustomChargeStart fuori range (50-95): $start"
        exit 1
    }
    if ($stop -lt 55 -or $stop -gt 100) {
        Write-Error "CustomChargeStop fuori range (55-100): $stop"
        exit 1
    }
    if (($stop - $start) -lt 5) {
        Write-Error "Differenza minima tra start e stop deve essere di 5 punti percentuali (attuale: $($stop - $start))"
        exit 1
    }
}

if ($PSCmdlet.ShouldProcess("DellSmbios:\PowerManagement", "Applica profilo '$Profile' (PrimaryBattChargeCfg=$cfg, Start=$start, Stop=$stop)")) {
    Set-Item -Path DellSmbios:\PowerManagement\PrimaryBattChargeCfg -Value $cfg

    if ($cfg -eq 'Custom') {
        Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
        Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
    }

    $after = Get-CurrentChargeState
    Write-Host "Nuovo stato:  PrimaryBattChargeCfg=$($after.PrimaryBattChargeCfg) CustomChargeStart=$($after.CustomChargeStart) CustomChargeStop=$($after.CustomChargeStop)"
    ($after | ConvertTo-Json -Compress) | Add-Content -Path $LogPath
}
