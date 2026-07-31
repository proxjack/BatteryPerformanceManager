using Microsoft.Win32;

namespace BatteryChargeManager.TrayApp;

/// Gestisce l'avvio automatico al login tramite la chiave utente
/// HKEY_CURRENT_USER\...\Run (NON la cartella Startup di sistema, NON HKLM):
/// questa chiave non richiede privilegi elevati né per leggerla né per scriverla,
/// coerentemente con il vincolo che la tray app non deve mai girare da amministratore.
internal static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BatteryChargeManagerTrayApp";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void Enable()
    {
        string exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Impossibile determinare il percorso dell'eseguibile corrente.");

        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
    }

    public static void Disable()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
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
