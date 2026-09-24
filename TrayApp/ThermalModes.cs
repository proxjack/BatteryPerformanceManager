namespace BatteryPerformanceManager.TrayApp;

internal enum ThermalMode
{
    Optimized,
    Cool,
    Quiet,
    Ultra,
}

/// Metadata of a Dell Optimizer thermal mode. Title is the full name (tooltip, logs,
/// screen readers), ShortTitle the one that fits a flyout tile. StateId is what gets
/// sent to the elevated helper (as "thermal:<StateId>", it must match the values
/// accepted by Set-ThermalMode in BatteryPerformanceHelper.ps1) and saved in state.json;
/// DellValue is the name Dell Optimizer itself uses, to recognize the active mode.
internal sealed record ThermalModeInfo(
    ThermalMode Id, string Title, string ShortTitle, string Description, string Glyph, string StateId, string DellValue);

internal static class ThermalModes
{
    // The order here determines the order of the tiles in the flyout (same as Dell Optimizer).
    public static readonly IReadOnlyList<ThermalModeInfo> All = new[]
    {
        new ThermalModeInfo(ThermalMode.Optimized, "Optimized", "Optimized", "Balanced", Glyphs.SpeedMedium, "optimized", "Optimized"),
        new ThermalModeInfo(ThermalMode.Cool, "Cool", "Cool", "Cooler surface", Glyphs.Thermometer, "cool", "Cool"),
        new ThermalModeInfo(ThermalMode.Quiet, "Quiet", "Quiet", "Lower fan noise", Glyphs.Moon, "quiet", "Quiet"),
        new ThermalModeInfo(ThermalMode.Ultra, "Ultra Performance", "Ultra", "Max performance", Glyphs.SpeedHigh, "ultra", "Ultra"),
    };

    public static ThermalModeInfo? FromStateId(string? stateId) =>
        stateId is null ? null : All.FirstOrDefault(m => m.StateId == stateId);

    public static ThermalModeInfo? FromDellValue(string? dellValue) =>
        dellValue is null ? null : All.FirstOrDefault(m => string.Equals(m.DellValue, dellValue, StringComparison.OrdinalIgnoreCase));
}
