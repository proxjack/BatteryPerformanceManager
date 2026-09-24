namespace BatteryPerformanceManager.TrayApp;

/// Local plain-text error log (no popup/toast by explicit requirement: the interface
/// must stay minimal, just the tray flyout).
internal static class ErrorLog
{
    private static readonly string AppDataDir = AppDataFolder.DirectoryPath;

    private static readonly string LogPath = Path.Combine(AppDataDir, "errors.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // If even writing the log fails (disk full, permissions, etc.) there's no
            // other place to report it without breaking the "no popups" requirement.
        }
    }
}
