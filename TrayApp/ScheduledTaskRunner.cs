using System.Diagnostics;
using Microsoft.Win32.TaskScheduler;
// Alias esplicito: "Task" esiste sia in Microsoft.Win32.TaskScheduler (una scheduled task)
// sia in System.Threading.Tasks (incluso dagli implicit usings del progetto) — senza
// questo alias il nome sarebbe ambiguo in questo file.
using Task = Microsoft.Win32.TaskScheduler.Task;

namespace BatteryChargeManager.TrayApp;

internal sealed record TaskRunResult(bool Success, string? ErrorDetail);

/// Avvia le Scheduled Task create da setup-scheduled-tasks.ps1 tramite la libreria
/// Microsoft.Win32.TaskScheduler (NuGet "TaskScheduler") invece di shellare schtasks.exe:
/// permette di leggere in modo affidabile lo stato ("Running"/"Ready") e il codice di
/// uscita reale dello script PowerShell sottostante (Task.LastTaskResult), che è la stessa
/// cosa restituita da 'exit 1' / implicito 0 in Set-DellBatteryChargeProfile.ps1.
///
/// Questa chiamata gira in un processo NON elevato: avviare una task già registrata con
/// RunLevel=Highest tramite questa API (invece di lanciare l'exe direttamente) non fa
/// comparire il prompt UAC, perché è il servizio Task Scheduler (SYSTEM) a creare il
/// processo con il token elevato dell'utente — vedi il commento in cima a
/// setup-scheduled-tasks.ps1 per i dettagli.
internal static class ScheduledTaskRunner
{
    private const string TaskFolderPath = @"\BatteryChargeManager";
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    public static TaskRunResult Run(string taskName)
    {
        try
        {
            using var taskService = new TaskService();
            TaskFolder folder = taskService.GetFolder(TaskFolderPath);

            Task? task = folder.GetTasks().FirstOrDefault(t => t.Name == taskName);
            if (task is null)
            {
                return new TaskRunResult(false,
                    $"Attività '{TaskFolderPath}\\{taskName}' non trovata. Esegui prima setup-scheduled-tasks.ps1 come amministratore.");
            }

            task.Run();

            var stopwatch = Stopwatch.StartNew();
            while (task.State == TaskState.Running && stopwatch.Elapsed < RunTimeout)
            {
                Thread.Sleep(PollInterval);
            }

            if (task.State == TaskState.Running)
            {
                return new TaskRunResult(false,
                    $"L'attività '{TaskFolderPath}\\{taskName}' non è terminata entro {RunTimeout.TotalSeconds:0} secondi.");
            }

            int exitCode = task.LastTaskResult;
            if (exitCode == 0)
            {
                return new TaskRunResult(true, null);
            }

            return new TaskRunResult(false,
                $"Lo script per il profilo '{taskName}' è terminato con codice {exitCode} " +
                "(probabile validazione fallita o percorso BIOS non disponibile — controlla dell-battery-charge-log.jsonl).");
        }
        catch (Exception ex)
        {
            // Qualsiasi eccezione qui (cartella/task non trovata, servizio Task Scheduler
            // non raggiungibile, ecc.) è quasi sempre dovuta al setup non ancora eseguito.
            return new TaskRunResult(false,
                $"Impossibile avviare l'attività '{TaskFolderPath}\\{taskName}': {ex.Message}. " +
                "Assicurati di aver eseguito setup-scheduled-tasks.ps1 come amministratore.");
        }
    }
}
