using System.IO.Pipes;
using Microsoft.Win32.TaskScheduler;
// Alias esplicito: "Task" esiste sia in Microsoft.Win32.TaskScheduler (una scheduled task)
// sia in System.Threading.Tasks (incluso dagli implicit usings del progetto).
using Task = Microsoft.Win32.TaskScheduler.Task;

namespace BatteryChargeManager.TrayApp;

internal sealed record TaskRunResult(bool Success, string? ErrorDetail);

/// Comunica con l'helper elevato persistente (BatteryChargeHelper.ps1) via named pipe,
/// invece di lanciare un nuovo processo elevato ad ogni cambio profilo: su questa
/// macchina, creare un nuovo processo elevato costava ~10-12 secondi (verificato
/// indipendente dal meccanismo di lancio — probabile scansione antivirus in tempo
/// reale su un processo elevato appena creato), mentre inviare un comando a un
/// processo già in esecuzione è quasi istantaneo.
///
/// Se l'helper non è ancora attivo (primo cambio profilo della sessione), lo si avvia
/// tramite la stessa Scheduled Task elevata di prima (RunLevel=Highest, nessun prompt
/// UAC) e si attende che la pipe diventi disponibile — quel primo avvio paga ancora il
/// costo pieno, ma resta poi attivo fino al logout per tutte le richieste successive.
internal static class HelperClient
{
    private const string PipeName = "BatteryChargeManagerHelper";
    private const string TaskFolderPath = @"\BatteryChargeManager";
    private const string HelperTaskName = "Helper";

    private static readonly TimeSpan QuickConnectTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FirstStartConnectTimeout = TimeSpan.FromSeconds(25);

    public static TaskRunResult SwitchProfile(string profileId)
    {
        try
        {
            using NamedPipeClientStream? client = TryConnect(QuickConnectTimeout)
                ?? StartHelperAndConnect();

            if (client is null)
            {
                return new TaskRunResult(false,
                    "Impossibile connettersi all'helper elevato entro il timeout. " +
                    "Assicurati di aver eseguito setup-scheduled-tasks.ps1 come amministratore.");
            }

            using var writer = new StreamWriter(client) { AutoFlush = true };
            using var reader = new StreamReader(client);

            writer.WriteLine(profileId);
            string? response = reader.ReadLine();

            if (response == "OK")
            {
                return new TaskRunResult(true, null);
            }

            return new TaskRunResult(false, response ?? "Nessuna risposta dall'helper.");
        }
        catch (Exception ex)
        {
            return new TaskRunResult(false, $"Errore comunicando con l'helper elevato: {ex.Message}");
        }
    }

    private static NamedPipeClientStream? StartHelperAndConnect()
    {
        try
        {
            using var taskService = new TaskService();
            TaskFolder folder = taskService.GetFolder(TaskFolderPath);
            Task? task = folder.GetTasks().FirstOrDefault(t => t.Name == HelperTaskName);
            task?.Run();
        }
        catch
        {
            // Se la task non esiste/non si avvia, il tentativo di connessione sottostante
            // fallirà comunque e il chiamante riceverà un messaggio d'errore chiaro.
        }

        return TryConnect(FirstStartConnectTimeout);
    }

    private static NamedPipeClientStream? TryConnect(TimeSpan timeout)
    {
        var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
        try
        {
            client.Connect((int)timeout.TotalMilliseconds);
            return client;
        }
        catch (TimeoutException)
        {
            client.Dispose();
            return null;
        }
    }
}
