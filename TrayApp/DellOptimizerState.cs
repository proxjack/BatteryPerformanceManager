using System.Text.Json;
using Microsoft.Win32;

namespace BatteryChargeManager.TrayApp;

/// Reads, WITHOUT elevated privileges, the thermal mode currently set in Dell
/// Optimizer. The mode can also change outside this app (the Dell Optimizer UI, or
/// the Windows power mode, which Dell Optimizer keeps in sync with the thermal one):
/// reading it from here avoids marking a mode as active in the flyout when it no
/// longer is. do-cli.exe requires administrator rights, so this uses
/// %ProgramData%\{DataFolderName}\DellOptimizer\TelemetrySettings.json instead,
/// which every user can read and Dell Optimizer updates on every change.
///
/// It's an internal Dell file, not a documented interface: in any unexpected case
/// this returns null and the caller falls back to the last applied value.
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

            // FileShare.ReadWrite: Dell Optimizer may be writing the file right now.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using JsonDocument doc = JsonDocument.Parse(stream);
            return FindSettingValue(doc.RootElement, "ThermalMode");
        }
        catch
        {
            return null;
        }
    }

    // The file is a tree of { "name", "value", "subSettings": [...] }: the exact
    // position of the setting isn't guaranteed, so it's searched in the whole tree.
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
