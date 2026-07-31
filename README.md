# Battery Charge Manager

Tray app per Windows che cambia il profilo di ricarica della batteria del Dell XPS 14
con un click dal system tray, senza prompt UAC e senza far girare la tray app come
amministratore.

Si appoggia allo script PowerShell già esistente e testato manualmente,
[`Set-DellBatteryChargeProfile.ps1`](Set-DellBatteryChargeProfile.ps1) (non modificarlo),
che parla col BIOS tramite il modulo `DellBIOSProvider`.

## Come funziona (in breve)

1. **Una tantum**, da amministratore: [`setup-scheduled-tasks.ps1`](setup-scheduled-tasks.ps1)
   crea 4 Attività Pianificate (una per profilo) configurate per girare con privilegi
   elevati ma **senza trigger automatici** — partono solo su richiesta.
2. **Ogni giorno**: la tray app (`TrayApp.exe`), che gira SENZA privilegi amministrativi,
   avvia l'Attività Pianificata giusta quando scegli un profilo dal menu. Poiché la task
   è già registrata come elevata, il Task Scheduler (che gira come SYSTEM) la esegue con
   il token elevato dell'utente senza mostrare il prompt UAC — non serve elevare la
   tray app stessa.

## Struttura del progetto

```
/BatteryChargeManager
  /TrayApp                             <- progetto C# WinForms (.NET 8)
  Set-DellBatteryChargeProfile.ps1     <- script esistente, NON modificato
  setup-scheduled-tasks.ps1            <- script di setup una tantum
  README.md
```

## 1. Compilare la tray app

Richiede il .NET 8 SDK (non solo il runtime). Verifica con:

```bash
dotnet --list-sdks
```

Se non compare una riga `8.0.x`, installa l'SDK da https://aka.ms/dotnet/download
(o `winget install Microsoft.DotNet.SDK.8`).

Dalla cartella `TrayApp`:

```bash
dotnet restore
dotnet publish -c Release -r win-x64 --self-contained true
```

L'eseguibile standalone (single-file, nessuna dipendenza .NET da installare per
l'utente finale) viene generato in:

```
TrayApp\bin\Release\net8.0-windows\win-x64\publish\TrayApp.exe
```

Se `dotnet restore` fallisce a risolvere il pacchetto NuGet `TaskScheduler` alla
versione indicata in `TrayApp.csproj`, aggiorna alla versione più recente disponibile con:

```bash
dotnet add TrayApp.csproj package TaskScheduler
```

## 2. Setup una tantum (da amministratore)

Apri PowerShell **come amministratore** e lancia:

```powershell
cd "BatteryChargeManager"
.\setup-scheduled-tasks.ps1
```

Lo script crea le 4 Attività Pianificate in `\BatteryChargeManager\` (60_65, 75_80,
Standard, FastCharge), ognuna configurata per eseguire
`Set-DellBatteryChargeProfile.ps1` col profilo corrispondente. È idempotente: puoi
rilanciarlo in sicurezza (es. dopo aver spostato la cartella del progetto o cambiato
l'elenco profili) — aggiorna le task esistenti e rimuove quelle di un set di profili
precedente, invece di duplicarle o lasciarle in giro.

Se cambi percorso allo script sorgente, passa `-ScriptPath`:

```powershell
.\setup-scheduled-tasks.ps1 -ScriptPath "D:\altro\percorso\Set-DellBatteryChargeProfile.ps1"
```

## 3. Uso quotidiano

Avvia `TrayApp.exe` (copialo dove preferisci, es. `%LOCALAPPDATA%\BatteryChargeManager\`).
Compare un'icona nella system tray. Click destro per il menu:

- **60-65 (usura minima)** / **75-80** / **Standard (ricarica fino al 100%)** / **Fast charge**
  — applica il profilo corrispondente (nessun prompt UAC)
- **Avvio automatico** — abilita/disabilita l'avvio della tray app al login (checkbox)
- **Esci**

Il profilo applicato con successo l'ultima volta resta marcato nel menu (con la spunta)
anche dopo aver riavviato l'app o il PC — è solo un'indicazione visiva, **non viene mai
riapplicato automaticamente**.

### Se qualcosa va storto

L'interfaccia è volutamente minimale: **nessun popup, nessuna notifica toast**. Se un
cambio profilo fallisce (task non trovata → setup non ancora eseguito, oppure script
fallito per BIOS non disponibile / valori fuori range), il dettaglio finisce in:

```
%APPDATA%\BatteryChargeManager\errors.log
```

Lo stato dell'ultimo profilo è in `%APPDATA%\BatteryChargeManager\state.json`.

Il log dettagliato prodotto dallo script ad ogni esecuzione (stato prima/dopo) resta
quello già esistente, `dell-battery-charge-log.jsonl`, accanto allo script.

## Cosa NON fa questa app

- Non modifica `Set-DellBatteryChargeProfile.ps1`.
- Non fa mai girare la tray app con privilegi di amministratore.
- Non mostra notifiche toast, popup di conferma o icone diverse per profilo.
- Non riapplica automaticamente un profilo all'avvio dell'app o del PC.
