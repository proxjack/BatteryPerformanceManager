using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeManager.TrayApp;

/// Contesto applicativo senza finestra: tutta l'interfaccia è il NotifyIcon nella
/// system tray e il suo menu contestuale. Nessun popup, nessuna notifica toast,
/// nessun cambio icona per profilo (richiesta esplicita: interfaccia minimale).
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly Dictionary<ChargeProfile, ToolStripMenuItem> _profileItems = new();
    private readonly ToolStripMenuItem _autoStartItem;
    private bool _busy;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        foreach (ChargeProfileInfo profile in Profiles.All)
        {
            var item = new ToolStripMenuItem(profile.MenuText)
            {
                Tag = profile,
            };
            item.Click += OnProfileClicked;
            menu.Items.Add(item);
            _profileItems[profile.Id] = item;
        }

        menu.Items.Add(new ToolStripSeparator());

        _autoStartItem = new ToolStripMenuItem("Avvio automatico")
        {
            CheckOnClick = false,
            Checked = AutoStart.IsEnabled(),
        };
        _autoStartItem.Click += OnAutoStartClicked;
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Esci");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Battery Charge Manager",
            ContextMenuStrip = menu,
            Visible = true,
        };

        // Marca visivamente (solo a scopo informativo) l'ultimo profilo applicato con
        // successo, letto da state.json. Non riapplica nulla: nessuna scheduled task
        // viene avviata all'avvio dell'app.
        ChargeProfileInfo? lastProfile = Profiles.FromStateId(StateStore.Load().LastProfile);
        if (lastProfile is not null)
        {
            SetCheckedProfile(lastProfile.Id);
        }
    }

    private async void OnProfileClicked(object? sender, EventArgs e)
    {
        if (_busy)
        {
            return;
        }

        if (sender is not ToolStripMenuItem { Tag: ChargeProfileInfo profile })
        {
            return;
        }

        _busy = true;
        try
        {
            TaskRunResult result = await Task.Run(() => HelperClient.SwitchProfile(profile.StateId));

            if (result.Success)
            {
                StateStore.SaveLastProfile(profile.StateId);
                SetCheckedProfile(profile.Id);
            }
            else
            {
                // Niente popup/toast per richiesta esplicita: l'errore finisce solo nel
                // log locale leggibile dall'utente in %APPDATA%\BatteryChargeManager\errors.log.
                ErrorLog.Write($"Cambio profilo '{profile.MenuText}' fallito: {result.ErrorDetail}");
            }
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnAutoStartClicked(object? sender, EventArgs e)
    {
        try
        {
            AutoStart.Toggle();
            _autoStartItem.Checked = AutoStart.IsEnabled();
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Impossibile aggiornare l'avvio automatico: {ex.Message}");
        }
    }

    // Carica battery.ico dalla risorsa incorporata a 32x32 (dimensione nativa della tray
    // a DPI standard), invece di lasciare che Windows scali una dimensione non ottimale.
    private static Icon LoadTrayIcon()
    {
        try
        {
            using Stream? stream = typeof(TrayApplicationContext).Assembly
                .GetManifestResourceStream("BatteryChargeManager.TrayApp.battery.ico");

            return stream is not null ? new Icon(stream, new Size(32, 32)) : SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void SetCheckedProfile(ChargeProfile active)
    {
        foreach ((ChargeProfile id, ToolStripMenuItem item) in _profileItems)
        {
            item.Checked = id == active;
        }
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        Application.Exit();
    }
}
