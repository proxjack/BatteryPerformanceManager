namespace BatteryChargeManager.TrayApp;

internal enum ThermalMode
{
    Optimized,
    Cool,
    Quiet,
    Ultra,
}

/// Metadata of a Dell Optimizer thermal mode. StateId is what gets sent to the elevated
/// helper (as "thermal:<StateId>", it must match the values accepted by Set-ThermalMode
/// in BatteryChargeHelper.ps1) and saved in state.json; DellValue is the name Dell
/// Optimizer itself uses, to recognize the active mode.
internal sealed record ThermalModeInfo(ThermalMode Id, string MenuText, string StateId, string DellValue);

internal static class ThermalModes
{
    // The order here determines the order of the items in the context menu (same as Dell Optimizer).
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
