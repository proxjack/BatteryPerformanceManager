namespace BatteryChargeManager.TrayApp;

internal enum ChargeProfile
{
    Profile6065,
    Profile7580,
    Standard,
    FastCharge,
}

/// Profile metadata: title, description and icon shown by the flyout, and the
/// identifier sent to the elevated helper over the named pipe (it must exactly match
/// the values accepted by BatteryChargeHelper.ps1) - the same identifier is also what
/// gets saved in state.json.
internal sealed record ChargeProfileInfo(ChargeProfile Id, string Title, string Description, string Glyph, string StateId);

internal static class Profiles
{
    // The order here determines the order of the tiles in the flyout.
    public static readonly IReadOnlyList<ChargeProfileInfo> All = new[]
    {
        new ChargeProfileInfo(ChargeProfile.Profile6065, "60-65", "Minimal wear", Glyphs.Leaf, "60_65"),
        new ChargeProfileInfo(ChargeProfile.Profile7580, "75-80", "Low wear", Glyphs.Battery8, "75_80"),
        new ChargeProfileInfo(ChargeProfile.Standard, "Standard", "Up to 100%", Glyphs.Battery10, "standard"),
        new ChargeProfileInfo(ChargeProfile.FastCharge, "Fast charge", "ExpressCharge", Glyphs.Bolt, "fastcharge"),
    };

    public static ChargeProfileInfo? FromStateId(string? stateId) =>
        stateId is null ? null : All.FirstOrDefault(p => p.StateId == stateId);
}
