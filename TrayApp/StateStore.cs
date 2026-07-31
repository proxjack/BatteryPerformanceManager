using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryChargeManager.TrayApp;

internal sealed class AppState
{
    [JsonPropertyName("lastProfile")]
    public string? LastProfile { get; set; }

    [JsonPropertyName("lastUpdatedUtc")]
    public string? LastUpdatedUtc { get; set; }
}

/// Legge/scrive %APPDATA%\BatteryChargeManager\state.json — l'unico scopo è mostrare
/// visivamente nel menu qual è l'ultimo profilo applicato con successo. Non viene mai
/// usato per riapplicare automaticamente un profilo all'avvio.
internal static class StateStore
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BatteryChargeManager");

    private static readonly string StatePath = Path.Combine(AppDataDir, "state.json");

    public static AppState Load()
    {
        try
        {
            if (!File.Exists(StatePath))
            {
                return new AppState();
            }

            string json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<AppState>(json) ?? new AppState();
        }
        catch
        {
            // state.json corrotto o illeggibile: non è un errore fatale, semplicemente
            // nessun profilo risulterà marcato nel menu finché non se ne sceglie uno.
            return new AppState();
        }
    }

    public static void SaveLastProfile(string stateId)
    {
        Directory.CreateDirectory(AppDataDir);

        var state = new AppState
        {
            LastProfile = stateId,
            LastUpdatedUtc = DateTime.UtcNow.ToString("o"),
        };

        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StatePath, json);
    }
}
