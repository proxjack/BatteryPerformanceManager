<#
Setup una tantum per Battery Charge Manager.

Crea 4 Attivita' Pianificate di Windows (una per profilo di ricarica), configurate per
girare con i privilegi piu' elevati ("RunLevel = Highest") ma SENZA trigger automatici:
si avviano solo su richiesta (Start-ScheduledTask / schtasks /Run / Task.Run() via COM).

Perche' questo evita il prompt UAC: quando un processo NON elevato avvia una task
già registrata con RunLevel=Highest tramite l'API del Task Scheduler (non doppio click
sull'exe), è il servizio "Task Scheduler" (che gira come SYSTEM) a creare direttamente
il processo con il token elevato dell'utente. Non passa dal percorso "AppInfo/consenso
UAC interattivo" che scatta invece quando un utente prova a elevare manualmente un
programma. Per questo la tray app (TrayApp, non amministratore) può avviare queste
task senza mostrare alcun prompt, a patto che l'utente corrente sia effettivamente
membro del gruppo Amministratori locali.

Esegui questo script UNA SOLA VOLTA, come amministratore (clic destro sul file
o sulla console PowerShell > "Esegui come amministratore"). Rilanciarlo in seguito
è sicuro: rimuove ed ricrea le 4 task, quindi aggiorna eventuali percorsi/versioni
cambiate invece di duplicarle o fallire.
#>

[CmdletBinding()]
param(
    # Percorso allo script esistente Set-DellBatteryChargeProfile.ps1 (non modificarlo).
    # Di default si assume che stia nella stessa cartella di questo script di setup.
    [string]$ScriptPath = (Join-Path $PSScriptRoot 'Set-DellBatteryChargeProfile.ps1')
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

if (-not (Test-Path $ScriptPath)) {
    Write-Error "Script non trovato: '$ScriptPath'. Passa il percorso corretto con -ScriptPath, es.: .\setup-scheduled-tasks.ps1 -ScriptPath 'C:\percorso\Set-DellBatteryChargeProfile.ps1'"
    exit 1
}
$ScriptPath = (Resolve-Path $ScriptPath).Path

$taskFolderPath = '\BatteryChargeManager\'

# Nome task (visibile in Utilità di pianificazione) -> nome profilo passato allo script (-Profile)
$profileTasks = [ordered]@{
    '60_65'      = '60_65'
    '75_80'      = '75_80'
    'Standard'   = 'standard'
    'FastCharge' = 'fastcharge'
}

$currentUser = "$env:USERDOMAIN\$env:USERNAME"

Write-Host "Script richiamato dalle attività: $ScriptPath"
Write-Host "Utente proprietario delle attività: $currentUser"
Write-Host "Creazione/aggiornamento attività in '$taskFolderPath'..." -ForegroundColor Cyan
Write-Host ""

# Rimuovi eventuali task rimaste da un set di profili precedente (es. rinominato/ridotto),
# così la cartella '\BatteryChargeManager\' contiene sempre e solo i profili attuali.
$obsolete = Get-ScheduledTask -TaskPath $taskFolderPath -ErrorAction SilentlyContinue |
    Where-Object { $_.TaskName -notin $profileTasks.Keys }
foreach ($task in $obsolete) {
    Write-Host "Rimuovo attività obsoleta: $taskFolderPath$($task.TaskName)" -ForegroundColor DarkYellow
    Unregister-ScheduledTask -TaskName $task.TaskName -TaskPath $taskFolderPath -Confirm:$false
}

# Register-ScheduledTask crea da sé la cartella '\BatteryChargeManager\' se non esiste ancora.
$results = foreach ($taskName in $profileTasks.Keys) {
    $profileArg = $profileTasks[$taskName]
    # -WindowStyle Hidden: nessuna finestra PowerShell visibile durante il cambio profilo,
    # coerente con l'interfaccia minimale della tray app (solo menu, nessun artefatto visivo).
    $arguments = "-NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$ScriptPath`" -Profile $profileArg"

    # Idempotenza: rimuovi la task esistente (se c'è) prima di ricrearla, così qualsiasi
    # modifica a percorso/argomenti/principal viene applicata in modo pulito.
    $existing = Get-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath -ErrorAction SilentlyContinue
    if ($existing) {
        Unregister-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath -Confirm:$false
    }

    try {
        # WorkingDirectory esplicita: senza, la task parte con CWD in System32, il che ha
        # causato in passato un fallimento di $PSScriptRoot nel valore di default di un
        # parametro dello script (vedi commento in Set-DellBatteryChargeProfile.ps1).
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $arguments -WorkingDirectory (Split-Path -Parent $ScriptPath)

        # Nessun -Trigger: la task non parte mai da sola, solo su richiesta esplicita
        # (Start-ScheduledTask / schtasks /Run / Task Scheduler COM API dalla tray app).
        $principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest

        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -MultipleInstances IgnoreNew `
            -ExecutionTimeLimit (New-TimeSpan -Minutes 2)

        Register-ScheduledTask -TaskName $taskName -TaskPath $taskFolderPath `
            -Action $action -Principal $principal -Settings $settings | Out-Null

        [PSCustomObject]@{ Task = "$taskFolderPath$taskName"; Esito = 'OK' }
    } catch {
        [PSCustomObject]@{ Task = "$taskFolderPath$taskName"; Esito = "ERRORE: $($_.Exception.Message)" }
    }
}

$results | Format-Table -AutoSize | Out-String | Write-Host

$okCount = ($results | Where-Object { $_.Esito -eq 'OK' }).Count
$totalCount = $profileTasks.Count

Write-Host ""
if ($okCount -eq $totalCount) {
    Write-Host "Setup completato: $okCount/$totalCount attività create correttamente in '$taskFolderPath'." -ForegroundColor Green
    Write-Host "Ora puoi avviare TrayApp.exe senza privilegi amministrativi: il cambio profilo dal menu non mostrerà prompt UAC."
} else {
    Write-Warning "Setup completato con errori: solo $okCount/$totalCount attività create correttamente. Controlla i messaggi sopra."
    exit 1
}
