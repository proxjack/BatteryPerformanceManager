<#
Setup una tantum per Battery Charge Manager.

Crea UNA Attività Pianificata ('\BatteryChargeManager\Helper') configurata per
girare con i privilegi più elevati ("RunLevel = Highest") ma SENZA trigger
automatici: si avvia solo su richiesta (Start-ScheduledTask / Task.Run() via COM),
la prima volta che scegli un profilo dalla tray app. Da quel momento resta attiva
in background per tutta la sessione di login (si chiude da sola al logout) e
applica i cambi di profilo successivi in-process, senza dover creare un nuovo
processo elevato ad ogni click — che su questa macchina costava ~10-12 secondi
(molto probabilmente per via della scansione antivirus in tempo reale su un
processo elevato appena creato: verificato indipendente dal Task Scheduler,
identico anche lanciando manualmente con "Esegui come amministratore").

Perché questo evita il prompt UAC: quando un processo NON elevato avvia una task
già registrata con RunLevel=Highest tramite l'API del Task Scheduler (non doppio
click sull'exe), è il servizio "Task Scheduler" (che gira come SYSTEM) a creare
direttamente il processo con il token elevato dell'utente. Non passa dal percorso
"AppInfo/consenso UAC interattivo" che scatta invece quando un utente prova a
elevare manualmente un programma. Per questo la tray app (TrayApp, non
amministratore) può avviare questa task senza mostrare alcun prompt, a patto che
l'utente corrente sia effettivamente membro del gruppo Amministratori locali.

Esegui questo script UNA SOLA VOLTA, come amministratore (clic destro sul file
o sulla console PowerShell > "Esegui come amministratore"). Rilanciarlo in
seguito è sicuro: rimuove e ricrea la task, quindi aggiorna eventuali percorsi
cambiati invece di duplicarla o fallire.
#>

[CmdletBinding()]
param(
    # Percorso allo script helper che la task richiama (non modificarlo).
    # Di default si assume che stia nella stessa cartella di questo script di setup.
    [string]$HelperScriptPath = (Join-Path $PSScriptRoot 'BatteryChargeHelper.ps1')
)

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Error "Questo script deve essere eseguito come Amministratore. Riapri PowerShell con 'Esegui come amministratore' e rilancialo."
    exit 1
}

if (-not (Test-Path $HelperScriptPath)) {
    Write-Error "Script non trovato: '$HelperScriptPath'. Passa il percorso corretto con -HelperScriptPath, es.: .\setup-scheduled-tasks.ps1 -HelperScriptPath 'C:\percorso\BatteryChargeHelper.ps1'"
    exit 1
}
$HelperScriptPath = (Resolve-Path $HelperScriptPath).Path

$taskFolderPath = '\BatteryChargeManager\'
$taskName = 'Helper'
$currentUser = "$env:USERDOMAIN\$env:USERNAME"

Write-Host "Script helper richiamato dalla task: $HelperScriptPath"
Write-Host "Utente proprietario della task: $currentUser"
Write-Host "Creazione/aggiornamento attività in '$taskFolderPath'..." -ForegroundColor Cyan
Write-Host ""

# Rimuovi eventuali task rimaste da un'architettura precedente (una per profilo:
# 60_65/75_80/Standard/FastCharge), così la cartella '\BatteryChargeManager\'
# contiene sempre e solo la task attuale.
$obsolete = Get-ScheduledTask -TaskPath $taskFolderPath -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -ne $taskName }
foreach ($task in $obsolete) {
    Write-Host "Rimuovo attività obsoleta: $taskFolderPath$($task.TaskName)" -ForegroundColor DarkYellow
    Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $taskFolderPath -Confirm:$false
}

# Idempotenza: rimuovi la task esistente (se c'è) prima di ricrearla, così qualsiasi
# modifica a percorso/argomenti/principal viene applicata in modo pulito.
$existing = Get-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath -ErrorAction SilentlyContinue
if ($existing) {
    Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath -Confirm:$false
}

$result = try {
    # -WindowStyle Hidden: nessuna finestra PowerShell visibile.
    $arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$HelperScriptPath`""

    # WorkingDirectory esplicita: senza, la task parte con CWD in System32, il che ha
    # causato in passato un fallimento di $PSScriptRoot in un valore di default di
    # parametro (vedi commento in Set-DellBatteryChargeProfile.ps1).
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory (Split-Path -Parent $HelperScriptPath)

    # Nessun -Trigger: la task non parte mai da sola, solo su richiesta esplicita
    # (Start-ScheduledTask / Task Scheduler COM API dalla tray app).
    $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest

    $settings = New-ScheduledTaskSettingsSet `
        -AllowStartIfOnBatteries `
        -DontStopIfGoingOnBatteries `
        -MultipleInstances IgnoreNew `
        -ExecutionTimeLimit ([TimeSpan]::Zero)   # nessun limite di tempo: deve restare attiva per tutta la sessione

    Register-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath `
        -Action $action -Principal $principal -Settings $settings | Out-Null

    [PSCustomObject]@{ Task = "$taskFolderPath$taskName"; Esito = 'OK' }
} catch {
    [PSCustomObject]@{ Task = "$taskFolderPath$taskName"; Esito = "ERRORE: $($_.Exception.Message)" }
}

$result | Format-Table -AutoSize | Out-String | Write-Host

if ($result.Esito -eq 'OK') {
    Write-Host "Setup completato: attività '$taskFolderPath$taskName' creata correttamente." -ForegroundColor Green
    Write-Host "Ora puoi avviare TrayApp.exe senza privilegi amministrativi: al primo cambio profilo, l'helper elevato partirà in background (senza prompt UAC) e resterà attivo per il resto della sessione."
} else {
    Write-Warning "Setup fallito. Controlla il messaggio sopra."
    exit 1
}
