using Microsoft.Win32;

namespace BatteryPerformanceManager.TrayApp;

/// Handles auto-start at login through the per-user key
/// HKEY_CURRENT_USER\...\Run (NOT the system Startup folder, NOT HKLM):
/// this key needs no elevated privileges to read or write, consistent with the
/// requirement that the tray app must never run as administrator.
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BatteryPerformanceManager";

    // Value name used before the app was renamed from Battery Charge Manager; it
    // points at the old TrayApp.exe.
    private const string LegacyValueName = "BatteryChargeManagerTrayApp";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Enable()
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the path of the current executable.");

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// Replaces an auto-start entry left by the old name with one for this executable,
    /// so auto-start stays on across the rename.
    public static void MigrateLegacyEntry()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(LegacyValueName) is null)
        {
            return;
        }

        key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
        Enable();
    }

    public static void Toggle()
    {
        if (IsEnabled())
        {
            Disable();
        }
        else
        {
            Enable();
        }
    }
}
