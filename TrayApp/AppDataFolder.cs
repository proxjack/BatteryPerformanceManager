namespace BatteryPerformanceManager.TrayApp;

/// %APPDATA%\BatteryPerformanceManager, where state.json and errors.log live.
internal static class AppDataFolder
{
    // Folder name used before the app was renamed from Battery Charge Manager.
    private const string LegacyName = "BatteryChargeManager";

    public static readonly string DirectoryPath = Resolve();

    // Carries the old folder over on the first start after the rename, so the last
    // applied profile and the error log aren't lost.
    private static string Resolve()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string current = Path.Combine(root, "BatteryPerformanceManager");
        string legacy = Path.Combine(root, LegacyName);

        try
        {
            if (!Directory.Exists(current) && Directory.Exists(legacy))
            {
                Directory.Move(legacy, current);
            }
        }
        catch
        {
            // Not fatal: the app starts with a fresh state, like on a first install.
        }

        return current;
    }
}
