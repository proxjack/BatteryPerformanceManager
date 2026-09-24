using System.ComponentModel;
using System.Windows.Forms;

namespace BatteryChargeManager.TrayApp;

/// Windowless application context: the UI is the tray icon, its right-click menu
/// (TrayMenu) and the flyout opened by a left click (QuickFlyout). Both show the same
/// state, kept here. No toast notifications and nothing opens on its own: the flyout
/// only appears when the icon is clicked.
internal sealed class TrayApplicationContext : ApplicationContext
{
    private const string AppName = "Battery Charge Manager";

    private readonly NotifyIcon _notifyIcon;
    private readonly TrayMenu _menu = new();
    private readonly QuickFlyout _flyout = new();

    private ChargeProfile? _activeProfile;
    private ThermalMode? _activeThermalMode;
    // The ChargeProfileInfo or ThermalModeInfo being applied right now, if any.
    private object? _busyItem;

    public TrayApplicationContext()
    {
        _menu.ProfileRequested += (_, profile) => ApplyProfile(profile);
        _menu.ThermalModeRequested += (_, mode) => ApplyThermalMode(mode);
        _menu.AutoStartToggleRequested += (_, _) => ToggleAutoStart();
        _menu.ExitRequested += (_, _) => ExitApplication();
        _menu.Opening += OnMenuOpening;

        _flyout.ProfileRequested += (_, profile) => ApplyProfile(profile);
        _flyout.ThermalModeRequested += (_, mode) => ApplyThermalMode(mode);
        _flyout.AutoStartToggleRequested += (_, _) => ToggleAutoStart();
        _flyout.ExitRequested += (_, _) => ExitApplication();

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Load(SystemInformation.SmallIconSize),
            Text = AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += OnNotifyIconClick;

        // The last successfully applied profile, read from state.json, is only shown as
        // active. Nothing is reapplied: no scheduled task is started when the app starts.
        _activeProfile = Profiles.FromStateId(StateStore.Load().LastProfile)?.Id;
        RefreshThermalMode();
        UpdateViews();
    }

    private void OnNotifyIconClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // A click on the icon while the flyout is open first makes it lose focus (and
        // close): that same click must not reopen it.
        if (_flyout.Visible || _flyout.WasJustHidden)
        {
            _flyout.HideFlyout();
            return;
        }

        RefreshThermalMode();
        UpdateViews();
        _flyout.ShowFlyout(Theme.Current);
    }

    private void OnMenuOpening(object? sender, CancelEventArgs e)
    {
        RefreshThermalMode();
        UpdateViews();
        _menu.ApplyTheme(Theme.Current);
    }

    private async void ApplyProfile(ChargeProfileInfo profile)
    {
        await RunHelperRequestAsync(
            profile,
            () => HelperClient.SwitchProfile(profile.StateId),
            onSuccess: () =>
            {
                StateStore.SaveLastProfile(profile.StateId);
                _activeProfile = profile.Id;
            },
            failureDescription: $"Switching to charge profile '{profile.Title}'");
    }

    private async void ApplyThermalMode(ThermalModeInfo mode)
    {
        await RunHelperRequestAsync(
            mode,
            () => HelperClient.SetThermalMode(mode.StateId),
            onSuccess: () =>
            {
                StateStore.SaveLastThermalMode(mode.StateId);
                _activeThermalMode = mode.Id;
            },
            failureDescription: $"Switching to thermal mode '{mode.Title}'");
    }

    // One request at a time: the helper serves them sequentially anyway, and the
    // flyout shows which one is in progress.
    private async Task RunHelperRequestAsync(object item, Func<TaskRunResult> request, Action onSuccess, string failureDescription)
    {
        if (_busyItem is not null)
        {
            return;
        }

        _busyItem = item;
        UpdateViews();
        try
        {
            TaskRunResult result = await Task.Run(request);

            if (result.Success)
            {
                onSuccess();
            }
            else
            {
                // No popup/toast by explicit requirement: the details only go to the
                // local log the user can read at %APPDATA%\BatteryChargeManager\errors.log,
                // the flyout just marks the tile.
                ErrorLog.Write($"{failureDescription} failed: {result.ErrorDetail}");
                _flyout.ShowFailure(item);
            }
        }
        finally
        {
            _busyItem = null;
            UpdateViews();
        }
    }

    private void ToggleAutoStart()
    {
        try
        {
            AutoStart.Toggle();
        }
        catch (Exception ex)
        {
            ErrorLog.Write($"Could not update auto-start: {ex.Message}");
        }

        UpdateViews();
    }

    // Prefers the mode actually set in Dell Optimizer (it can also change from Dell
    // Optimizer itself or the Windows power mode); if that can't be read, falls back
    // to the last one successfully applied by this app.
    private void RefreshThermalMode()
    {
        ThermalModeInfo? current = ThermalModes.FromDellValue(DellOptimizerState.TryReadThermalMode())
            ?? ThermalModes.FromStateId(StateStore.Load().LastThermalMode);

        if (current is not null)
        {
            _activeThermalMode = current.Id;
        }
    }

    private void UpdateViews()
    {
        bool autoStart = AutoStart.IsEnabled();
        _menu.UpdateState(_activeProfile, _activeThermalMode, autoStart);
        _flyout.UpdateState(_activeProfile, _activeThermalMode, _busyItem, autoStart);

        _notifyIcon.Text = _busyItem switch
        {
            ChargeProfileInfo profile => $"{AppName} - applying {profile.Title}…",
            ThermalModeInfo mode => $"{AppName} - applying {mode.Title}…",
            _ => AppName,
        };
    }

    private void ExitApplication()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _flyout.Dispose();
        _menu.Dispose();
        Application.Exit();
    }
}
