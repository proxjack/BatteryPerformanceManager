using System.Text.Json;
using Microsoft.Win32;

namespace BatteryChargeManager.TrayApp;

/// Legge, SENZA privilegi elevati, la modalità termica attualmente impostata in Dell
/// Optimizer. La modalità può cambiare anche fuori da questa app (interfaccia di Dell
/// Optimizer, oppure la modalità energetica di Windows, che Dell Optimizer tiene
/// sincronizzata con quella termica): leggerla da qui evita di mostrare nel menu una
/// spunta non più vera. do-cli.exe richiede l'amministratore, quindi si usa il file
/// %ProgramData%\{DataFolderName}\DellOptimizer\TelemetrySettings.json, leggibile da
/// tutti gli utenti e aggiornato da Dell Optimizer a ogni cambio.
///
/// È un file interno di Dell, non un'interfaccia documentata: in qualunque caso
/// imprevisto si restituisce null e il chiamante ripiega sull'ultimo valore applicato.
internal static class DellOptimizerState
{
    private const string RegistryKeyPath = @"SOFTWARE\DELL\DellOptimizer";
    private const string DataFolderValueName = "DataFolderName";

    public static string? TryReadThermalMode()
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(RegistryKeyPath, writable: false);
            if (key?.GetValue(DataFolderValueName) is not string dataFolder || dataFolder.Length == 0)
            {
                return null;
            }

            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                dataFolder, "DellOptimizer", "TelemetrySettings.json");

            if (!File.Exists(path))
            {
                return null;
            }

            // FileShare.ReadWrite: Dell Optimizer può star scrivendo il file proprio ora.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument doc = JsonDocument.Parse(stream);
            return FindSettingValue(doc.RootElement, "ThermalMode");
        }
        catch
        {
            return null;
        }
    }

    // Il file è un albero di { "name", "value", "subSettings": [...] }: la posizione
    // esatta dell'impostazione non è garantita, quindi la si cerca in tutto l'albero.
    private static string? FindSettingValue(JsonElement element, string settingName)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("name", out JsonElement name)
                    && name.ValueKind == JsonValueKind.String
                    && name.GetString() == settingName
                    && element.TryGetProperty("value", out JsonElement value)
                    && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString();
                }

                foreach (JsonProperty property in element.EnumerateObject())
                {
                    string? found = FindSettingValue(property.Value, settingName);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            case JsonValueKind.Array:
                foreach (JsonElement item in element.EnumerateArray())
                {
                    string? found = FindSettingValue(item, settingName);
                    if (found is not null)
                    {
                        return found;
                    }
                }

                return null;

            default:
                return null;
        }
    }
}
