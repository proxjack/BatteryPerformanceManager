namespace BatteryChargeManager.TrayApp;

internal enum ThermalMode
{
    Optimized,
    Cool,
    Quiet,
    Ultra,
}

/// Metadati di una modalità termica di Dell Optimizer. StateId è quanto viene inviato
/// all'helper elevato (come "thermal:<StateId>", deve combaciare con i valori accettati
/// da Set-ThermalMode in BatteryChargeHelper.ps1) e salvato in state.json; DellValue è
/// il nome usato da Dell Optimizer stesso, per riconoscere la modalità attiva.
internal sealed record ThermalModeInfo(ThermalMode Id, string MenuText, string StateId, string DellValue);

internal static class ThermalModes
{
    // L'ordine qui determina l'ordine delle voci nel menu contestuale (lo stesso di Dell Optimizer).
    public static readonly IReadOnlyList<ThermalModeInfo> All = new[]
    {
        new ThermalModeInfo(ThermalMode.Optimized, "Optimized", "optimized", "Optimized"),
        new ThermalModeInfo(ThermalMode.Cool, "Cool", "cool", "Cool"),
        new ThermalModeInfo(ThermalMode.Quiet, "Quiet", "quiet", "Quiet"),
        new ThermalModeInfo(ThermalMode.Ultra, "Ultra Performance", "ultra", "Ultra"),
    };

    public static ThermalModeInfo? FromStateId(string? stateId) =>
        stateId is null ? null : All.FirstOrDefault(m => m.StateId == stateId);

    public static ThermalModeInfo? FromDellValue(string? dellValue) =>
        dellValue is null ? null : All.FirstOrDefault(m => string.Equals(m.DellValue, dellValue, StringComparison.OrdinalIgnoreCase));
}
