namespace BatteryChargeManager.TrayApp;

/// Log locale in testo semplice per gli errori (nessun popup/toast per richiesta esplicita:
/// l'interfaccia deve restare minimale, solo il menu contestuale).
internal static class ErrorLog
{
    private static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BatteryChargeManager");

    private static readonly string LogPath = Path.Combine(AppDataDir, "errors.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(AppDataDir);
            string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line);
        }
        catch
        {
            // Se anche scrivere il log fallisce (disco pieno, permessi, ecc.) non c'è
            // altro posto dove segnalarlo senza violare il vincolo "niente popup".
        }
    }
}
