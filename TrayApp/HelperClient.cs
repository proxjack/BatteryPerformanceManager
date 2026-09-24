using System.IO.Pipes;
using System.Text;
using Microsoft.Win32.TaskScheduler;
// Explicit alias: "Task" exists both in Microsoft.Win32.TaskScheduler (a scheduled task)
// and in System.Threading.Tasks (included by the project's implicit usings).
using Task = Microsoft.Win32.TaskScheduler.Task;

namespace BatteryPerformanceManager.TrayApp;

internal sealed record TaskRunResult(bool Success, string? ErrorDetail);

/// Talks to the persistent elevated helper (BatteryPerformanceHelper.ps1) over a named pipe,
/// instead of launching a new elevated process on every switch: on this machine,
/// creating a new elevated process cost ~10-12 seconds (verified independent of the
/// launch mechanism - likely real-time antivirus scanning of a freshly created
/// elevated process), while sending a command to an already running process is
/// almost instant.
///
/// If the helper isn't running yet (first switch of the session), it's started
/// through the elevated Scheduled Task (RunLevel=Highest, no UAC prompt) and we wait
/// for the pipe to become available - that first start still pays the full cost, but
/// the helper then stays active until logoff for every following request.
internal static class HelperClient
{
    private const string PipeName = "BatteryPerformanceManagerHelper";
    private const string TaskFolderPath = @"\BatteryPerformanceManager";
    private const string HelperTaskName = "Helper";

    private static readonly TimeSpan QuickConnectTimeout = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan FirstStartConnectTimeout = TimeSpan.FromSeconds(25);

    public static TaskRunResult SwitchProfile(string profileId) => SendRequest(profileId);

    public static TaskRunResult SetThermalMode(string modeId) => SendRequest($"thermal:{modeId}");

    private static TaskRunResult SendRequest(string request)
    {
        try
        {
            using NamedPipeClientStream? client = TryConnect(QuickConnectTimeout)
                ?? StartHelperAndConnect();

            if (client is null)
            {
                return new TaskRunResult(false,
                    "Could not connect to the elevated helper within the timeout. " +
                    "Make sure you ran setup-scheduled-tasks.ps1 as administrator.");
            }

            // leaveOpen: true on both - otherwise the first one to be disposed closes the
            // underlying pipe, and the second throws "Cannot access a closed pipe" during
            // its own Dispose, even though the request already succeeded.
            using var writer = new StreamWriter(client, Encoding.UTF8, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);

            writer.WriteLine(request);
            string? response = reader.ReadLine();

            if (response == "OK")
            {
                return new TaskRunResult(true, null);
            }

            return new TaskRunResult(false, response ?? "No response from the helper.");
        }
        catch (Exception ex)
        {
            return new TaskRunResult(false, $"Error communicating with the elevated helper: {ex.Message}");
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
            // If the task doesn't exist or doesn't start, the connection attempt below
            // will fail anyway and the caller gets a clear error message.
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
