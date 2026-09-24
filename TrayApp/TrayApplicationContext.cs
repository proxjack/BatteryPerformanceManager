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
    private readonly Dictionary<ThermalMode, ToolStripMenuItem> _thermalItems = new();
    private readonly ToolStripMenuItem _autoStartItem;
    private bool _busy;

    public TrayApplicationContext()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(CreateSectionHeader("Battery charge"));
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

        menu.Items.Add(CreateSectionHeader("Performance"));
        foreach (ThermalModeInfo mode in ThermalModes.All)
        {
            var item = new ToolStripMenuItem(mode.MenuText)
            {
                Tag = mode,
            };
            item.Click += OnThermalModeClicked;
            menu.Items.Add(item);
            _thermalItems[mode.Id] = item;
        }

        // La modalità termica può cambiare anche fuori da questa app (Dell Optimizer,
        // modalità energetica di Windows): la si rilegge ogni volta che si apre il menu.
        menu.Opening += (_, _) => RefreshCheckedThermalMode();

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

        RefreshCheckedThermalMode();
    }

    private async void OnProfileClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: ChargeProfileInfo profile })
        {
            return;
        }

        await RunHelperRequestAsync(
            () => HelperClient.SwitchProfile(profile.StateId),
            onSuccess: () =>
            {
                StateStore.SaveLastProfile(profile.StateId);
                SetCheckedProfile(profile.Id);
            },
            failureDescription: $"Cambio profilo '{profile.MenuText}'");
    }

    private async void OnThermalModeClicked(object? sender, EventArgs e)
    {
        if (sender is not ToolStripMenuItem { Tag: ThermalModeInfo mode })
        {
            return;
        }

        await RunHelperRequestAsync(
            () => HelperClient.SetThermalMode(mode.StateId),
            onSuccess: () =>
            {
                StateStore.SaveLastThermalMode(mode.StateId);
                SetCheckedThermalMode(mode.Id);
            },
            failureDescription: $"Cambio modalità termica '{mode.MenuText}'");
    }

    // Una richiesta alla volta: l'helper le serve comunque in sequenza, e un secondo
    // click durante il primo cambio verrebbe solo accodato senza alcun riscontro visivo.
    private async Task RunHelperRequestAsync(Func<TaskRunResult> request, Action onSuccess, string failureDescription)
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        try
        {
            TaskRunResult result = await Task.Run(request);

            if (result.Success)
            {
                onSuccess();
            }
            else
            {
                // Niente popup/toast per richiesta esplicita: l'errore finisce solo nel
                // log locale leggibile dall'utente in %APPDATA%\BatteryChargeManager\errors.log.
                ErrorLog.Write($"{failureDescription} fallito: {result.ErrorDetail}");
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

    // Intestazione di sezione non cliccabile, per distinguere i profili di ricarica
    // dalle modalità termiche (es. "Standard" vs "Optimized").
    private static ToolStripMenuItem CreateSectionHeader(string text) => new(text)
    {
        Enabled = false,
    };

    private void SetCheckedProfile(ChargeProfile active)
    {
        foreach ((ChargeProfile id, ToolStripMenuItem item) in _profileItems)
        {
            item.Checked = id == active;
        }
    }

    // Preferisce la modalità effettivamente impostata in Dell Optimizer; se non è
    // leggibile, ripiega sull'ultima applicata con successo da questa app.
    private void RefreshCheckedThermalMode()
    {
        ThermalModeInfo? current = ThermalModes.FromDellValue(DellOptimizerState.TryReadThermalMode())
            ?? ThermalModes.FromStateId(StateStore.Load().LastThermalMode);

        if (current is not null)
        {
            SetCheckedThermalMode(current.Id);
        }
    }

    private void SetCheckedThermalMode(ThermalMode active)
    {
        foreach ((ThermalMode id, ToolStripMenuItem item) in _thermalItems)
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
