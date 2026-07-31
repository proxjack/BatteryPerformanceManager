namespace BatteryChargeManager.TrayApp;

internal enum ChargeProfile
{
    Profilo6065,
    Profilo7580,
    Standard,
    FastCharge,
}

/// Metadati di un profilo: testo del menu, nome della Scheduled Task corrispondente
/// (deve combaciare esattamente con quanto crea setup-scheduled-tasks.ps1) e nome
/// salvato in state.json.
internal sealed record ChargeProfileInfo(ChargeProfile Id, string MenuText, string TaskName, string StateId);

internal static class Profiles
{
    // L'ordine qui determina l'ordine delle voci nel menu contestuale.
    public static readonly IReadOnlyList<ChargeProfileInfo> All = new[]
    {
        new ChargeProfileInfo(ChargeProfile.Profilo6065, "60-65 (usura minima)", "60_65", "60_65"),
        new ChargeProfileInfo(ChargeProfile.Profilo7580, "75-80", "75_80", "75_80"),
        new ChargeProfileInfo(ChargeProfile.Standard, "Standard (ricarica fino al 100%)", "Standard", "standard"),
        new ChargeProfileInfo(ChargeProfile.FastCharge, "Fast charge", "FastCharge", "fastcharge"),
    };

    public static ChargeProfileInfo? FromStateId(string? stateId) =>
        stateId is null ? null : All.FirstOrDefault(p => p.StateId == stateId);
}
