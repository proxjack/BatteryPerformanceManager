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

/// Reads/writes %APPDATA%\BatteryChargeManager\state.json - its only purpose is to show
/// in the menu which charge profile (and thermal mode) was last applied successfully.
/// It's never used to automatically reapply anything at startup.
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
            // Corrupted or unreadable state.json: not a fatal error, simply no profile
            // will be checked in the menu until one is picked.
            return new AppState();
        }
    }

    public static void SaveLastProfile(string stateId) => Update(state => state.LastProfile = stateId);

    public static void SaveLastThermalMode(string stateId) => Update(state => state.LastThermalMode = stateId);

    // Read-modify-write: saving the charge profile must not wipe the saved thermal
    // mode, and vice versa.
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
