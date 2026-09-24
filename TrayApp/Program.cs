using System.Windows.Forms;

namespace BatteryChargeManager.TrayApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Single instance: avoids duplicate tray icons if the app is started twice
        // (e.g. login + manual double-click).
        using var singleInstanceMutex = new Mutex(true, "BatteryChargeManager.TrayApp.SingleInstance", out bool isNewInstance);
        if (!isNewInstance)
        {
            return;
        }

        ApplicationConfiguration.Initialize();

        // No main window: the whole UI is the NotifyIcon + the flyout it opens.
        Application.Run(new TrayApplicationContext());
    }
}
