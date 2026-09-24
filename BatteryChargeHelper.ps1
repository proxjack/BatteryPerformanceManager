<#
Helper elevato persistente per Battery Charge Manager.

Perché esiste: lanciare un NUOVO processo PowerShell elevato ad ogni cambio
profilo (come faceva l'approccio precedente, una Scheduled Task per profilo)
costa circa 10-12 secondi su questa macchina — non per il modulo DellBIOSProvider
in sé (Import-Module + mount di DellSmbios: costano pochi millisecondi), ma per
l'overhead generico di creare un processo elevato (verificato: identico anche
lanciando manualmente con "Esegui come amministratore", quindi indipendente dal
Task Scheduler — molto probabilmente la scansione antivirus in tempo reale su
un processo elevato appena creato).

Questo script parte UNA VOLTA (via la Scheduled Task '\BatteryChargeManager\Helper',
avviata su richiesta dalla tray app al primo cambio profilo) e resta in ascolto
su una named pipe locale per il resto della sessione di login, applicando i
cambi di profilo in-process. Si chiude da solo al logout (le Scheduled Task con
LogonType=Interactive vengono terminate automaticamente da Windows al logout).

Non sostituisce Set-DellBatteryChargeProfile.ps1 (che resta utilizzabile da
riga di comando per test manuali) — ne duplica la logica di validazione/scrittura
perché quello script è pensato per un'esecuzione singola e termina con exit,
mentre questo deve girare indefinitamente in loop.

Gestisce anche la modalità termica di Dell Optimizer (Optimized/Cool/Quiet/Ultra).
Protocollo: una riga per richiesta — un id profilo di ricarica ('60_65', ...)
oppure 'thermal:<modo>' ('thermal:quiet', ...). Risposta: 'OK' o 'ERROR: ...'.
#>

$ErrorActionPreference = 'Stop'
$PipeName = 'BatteryChargeManagerHelper'
$LogPath = Join-Path $PSScriptRoot 'dell-battery-charge-log.jsonl'
$ThermalLogPath = Join-Path $PSScriptRoot 'dell-thermal-mode-log.jsonl'

# La modalità termica NON è esposta da DellBIOSProvider su questo XPS 14
# (DellSmbios:\PowerManagement\ThermalManagement non esiste): Dell Optimizer la
# imposta via interfaccia SMBIOS ("User Selectable Thermal Tables") e la tiene
# sincronizzata con la modalità energetica di Windows. Passando dalla sua CLI
# ufficiale (richiede admin, quindi va bene qui) l'effetto è identico a un click
# nell'interfaccia di Dell Optimizer, sincronizzazione compresa.
$DoCliPath = Join-Path $env:ProgramFiles 'Dell\DellOptimizer\do-cli.exe'
$DoCliTimeoutMs = 60000

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Error "Questo script deve essere eseguito come Amministratore."
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

function Get-CurrentChargeState {
    [PSCustomObject]@{
        Timestamp            = [datetime]::UtcNow.ToString('o')
        PrimaryBattChargeCfg = (Get-Item DellSmbios:\PowerManagement\PrimaryBattChargeCfg -ErrorAction SilentlyContinue).CurrentValue
        CustomChargeStart    = (Get-Item DellSmbios:\PowerManagement\CustomChargeStart -ErrorAction SilentlyContinue).CurrentValue
        CustomChargeStop     = (Get-Item DellSmbios:\PowerManagement\CustomChargeStop -ErrorAction SilentlyContinue).CurrentValue
    }
}

function Set-ChargeProfile {
    param([Parameter(Mandatory = $true)][string]$Profile)

    switch ($Profile) {
        '60_65'      { $cfg = 'Custom'; $start = 60; $stop = 65 }
        '75_80'      { $cfg = 'Custom'; $start = 75; $stop = 80 }
        'standard'   { $cfg = 'Standard'; $start = $null; $stop = $null }
        'fastcharge' { $cfg = 'Express'; $start = $null; $stop = $null }
        default      { throw "Profilo sconosciuto: '$Profile'" }
    }

    if ($cfg -eq 'Custom') {
        if ($start -lt 50 -or $start -gt 95) { throw "CustomChargeStart fuori range (50-95): $start" }
        if ($stop -lt 55 -or $stop -gt 100) { throw "CustomChargeStop fuori range (55-100): $stop" }
        if (($stop - $start) -lt 5) { throw "Differenza minima tra start e stop deve essere di 5 punti percentuali (attuale: $($stop - $start))" }
    }

    $before = Get-CurrentChargeState
    ($before | ConvertTo-Json -Compress) | Add-Content -Path $LogPath

    Set-Item -Path DellSmbios:\PowerManagement\PrimaryBattChargeCfg -Value $cfg

    if ($cfg -eq 'Custom') {
        # Il BIOS valida ogni singola scrittura rispetto al valore CORRENTE dell'altro
        # estremo (non rispetto alla coppia finale): vedi Set-DellBatteryChargeProfile.ps1
        # per la spiegazione completa. Stessa fix qui: proviamo un ordine, e se fallisce
        # (l'intervallo si sta spostando nella direzione opposta) proviamo l'altro.
        try {
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
        } catch {
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
        }
    }

    $after = Get-CurrentChargeState
    ($after | ConvertTo-Json -Compress) | Add-Content -Path $LogPath
}

function Set-ThermalMode {
    param([Parameter(Mandatory = $true)][string]$Mode)

    # Whitelist: solo questi valori arrivano mai sulla riga di comando di do-cli.
    # Sono i nomi esatti usati da Dell Optimizer (vedi il suo Service.log).
    switch ($Mode) {
        'optimized' { $value = 'Optimized' }
        'cool'      { $value = 'Cool' }
        'quiet'     { $value = 'Quiet' }
        'ultra'     { $value = 'Ultra' }
        default     { throw "Modalita termica sconosciuta: '$Mode'" }
    }

    if (-not (Test-Path $DoCliPath)) {
        throw "Dell Optimizer CLI non trovata in '$DoCliPath': Dell Optimizer e' installato?"
    }

    # Process invece di '& do-cli 2>&1': con $ErrorActionPreference='Stop', in
    # Windows PowerShell 5.1 una riga su stderr di un eseguibile nativo diventa
    # un'eccezione. Serve anche per avere un timeout.
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $DoCliPath
    $psi.Arguments = "/configure -name=SystemPowerConfiguration.ThermalMode -value=$value"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($DoCliTimeoutMs)) {
        $process.Kill()
        throw "do-cli non ha risposto entro $($DoCliTimeoutMs / 1000) secondi."
    }
    $process.WaitForExit()

    # Una sola riga: la risposta sulla pipe è line-based.
    $output = (($stdout.Result + ' ' + $stderr.Result) -replace '\s+', ' ').Trim()

    [PSCustomObject]@{
        Timestamp  = [datetime]::UtcNow.ToString('o')
        Requested  = $value
        ExitCode   = $process.ExitCode
        DurationMs = $stopwatch.ElapsedMilliseconds
        Output     = $output
    } | ConvertTo-Json -Compress | Add-Content -Path $ThermalLogPath

    if ($process.ExitCode -ne 0) {
        throw "do-cli ha restituito il codice $($process.ExitCode): $output"
    }
}

Add-Type -AssemblyName System.Core

# Named pipe ristretta al solo utente corrente: nessun altro processo/utente
# locale può inviare comandi a questo helper elevato.
$currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$pipeSecurity = New-Object System.IO.Pipes.PipeSecurity
$rule = New-Object System.IO.Pipes.PipeAccessRule($currentUserSid, [System.IO.Pipes.PipeAccessRights]::ReadWrite, [System.Security.AccessControl.AccessControlType]::Allow)
$pipeSecurity.AddAccessRule($rule)

Write-Host "Helper avviato, in ascolto sulla pipe '$PipeName'. Resta attivo fino al logout."

while ($true) {
    $pipe = $null
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeServerStream(
            $PipeName,
            [System.IO.Pipes.PipeDirection]::InOut,
            1,
            [System.IO.Pipes.PipeTransmissionMode]::Byte,
            [System.IO.Pipes.PipeOptions]::None,
            1024, 1024,
            $pipeSecurity)

        $pipe.WaitForConnection()

        $reader = New-Object System.IO.StreamReader($pipe)
        $writer = New-Object System.IO.StreamWriter($pipe)
        $writer.AutoFlush = $true

        $request = $reader.ReadLine()
        try {
            if ($request -like 'thermal:*') {
                Set-ThermalMode -Mode $request.Substring('thermal:'.Length)
            } else {
                Set-ChargeProfile -Profile $request
            }
            $writer.WriteLine("OK")
        } catch {
            $writer.WriteLine("ERROR: $($_.Exception.Message)")
        }
    } catch {
        # Un'altra istanza del helper è già in ascolto sulla stessa pipe, o un
        # client si è disconnesso a metà: esci se la pipe è già occupata,
        # altrimenti riparti col prossimo giro.
        if ($_.Exception -is [System.IO.IOException] -and $_.Exception.Message -match 'already exist') {
            Write-Host "Un'altra istanza dell'helper è già in ascolto. Esco."
            exit 0
        }
    } finally {
        if ($pipe) { $pipe.Dispose() }
    }
}
