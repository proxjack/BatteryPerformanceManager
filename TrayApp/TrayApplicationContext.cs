using System.Drawing;
using System.Windows.Forms;

namespace BatteryChargeManager.TrayApp;

/// Windowless application context: the whole UI is the NotifyIcon in the system
/// tray and its context menu. No popups, no toast notifications, no per-profile
/// icon changes (explicit requirement: minimal interface).
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

        // The thermal mode can also change outside this app (Dell Optimizer, Windows
        // power mode): it's re-read every time the menu opens.
        menu.Opening += (_, _) => RefreshCheckedThermalMode();

        menu.Items.Add(new ToolStripSeparator());

        _autoStartItem = new ToolStripMenuItem("Auto-start")
        {
            CheckOnClick = false,
            Checked = AutoStart.IsEnabled(),
        };
        _autoStartItem.Click += OnAutoStartClicked;
        menu.Items.Add(_autoStartItem);

        menu.Items.Add(new ToolStripSeparator());

        var exitItem = new ToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "Battery Charge Manager",
            ContextMenuStrip = menu,
            Visible = true,
        };

        // Visually marks (for information only) the last successfully applied profile,
        // read from state.json. Nothing is reapplied: no scheduled task is started when
        // the app starts.
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
            failureDescription: $"Switching to charge profile '{profile.MenuText}'");
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
            failureDescription: $"Switching to thermal mode '{mode.MenuText}'");
    }

    // One request at a time: the helper serves them sequentially anyway, and a second
    // click during the first switch would just be queued without any visual feedback.
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
                // No popup/toast by explicit requirement: the error only goes to the
                // local log the user can read at %APPDATA%\BatteryChargeManager\errors.log.
                ErrorLog.Write($"{failureDescription} failed: {result.ErrorDetail}");
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
            ErrorLog.Write($"Could not update auto-start: {ex.Message}");
        }
    }

    // Loads the app.ico frame matching the tray's icon size at the current display scaling
    // (16 px at 100%, 20 at 125%, 24 at 150%, 28 at 175%, 32 at 200%...: the process is
    // DPI-aware, so SmallIconSize is already scaled), instead of letting Windows shrink a
    // bigger frame - the small frames are simplified by hand to stay readable.
    private static Icon LoadTrayIcon()
    {
        try
        {
            using Stream? stream = typeof(TrayApplicationContext).Assembly
                .GetManifestResourceStream("BatteryChargeManager.TrayApp.app.ico");

            return stream is not null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    // Non-clickable section header, to tell charge profiles apart from thermal
    // modes (e.g. "Standard" vs "Optimized").
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

    // Prefers the mode actually set in Dell Optimizer; if that can't be read, falls
    // back to the last one successfully applied by this app.
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
