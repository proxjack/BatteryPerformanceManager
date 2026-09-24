using System.Text.Json;
using System.Text.Json.Serialization;

namespace BatteryChargeManager.TrayApp;

internal sealed class AppState
{
    [JsonPropertyName("lastProfile")]
    public string? LastProfile { get; set; }

    [JsonPropertyName("lastThermalMode")]
    public string? LastThermalMode { get; set; }

    [JsonPropertyName("lastUpdatedUtc")]
    public string? LastUpdatedUtc { get; set; }
}

/// Legge/scrive %APPDATA%\BatteryChargeManager\state.json — l'unico scopo è mostrare
/// visivamente nel menu qual è l'ultimo profilo di ricarica (e l'ultima modalità termica)
/// applicato con successo. Non viene mai usato per riapplicare automaticamente nulla all'avvio.
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

    public static void SaveLastProfile(string stateId) => Update(state => state.LastProfile = stateId);

    public static void SaveLastThermalMode(string stateId) => Update(state => state.LastThermalMode = stateId);

    // Legge-modifica-scrive: salvare il profilo di ricarica non deve cancellare la
    // modalità termica salvata, e viceversa.
    private static void Update(Action<AppState> change)
    {
        Directory.CreateDirectory(AppDataDir);

        AppState state = Load();
        change(state);
        state.LastUpdatedUtc = DateTime.UtcNow.ToString("o");

        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(StatePath, json);
    }
}
