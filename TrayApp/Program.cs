using System.Windows.Forms;

namespace BatteryChargeManager.TrayApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Un'unica istanza: evita menu tray duplicati se l'app viene avviata due volte
        // (es. login + doppio click manuale).
        using var singleInstanceMutex = new Mutex(true, "BatteryChargeManager.TrayApp.SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        // Nessuna finestra principale: l'intera UI è il NotifyIcon + il suo menu contestuale.
        Application.Run(new TrayApplicationContext());
    }
}
