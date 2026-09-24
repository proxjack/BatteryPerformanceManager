using System.Drawing;
using System.Windows.Forms;

namespace BatteryPerformanceManager.TrayApp;

/// Windows 11 style flyout opened by left-clicking the tray icon: battery status,
/// a tile per charge profile and per thermal mode, auto-start and exit. It closes
/// as soon as it loses focus, like the system's own tray flyouts, and stays open
/// while a switch is being applied.
internal sealed class QuickFlyout : Form
{
    private const string AppName = "Battery and Performance Manager";
    private const int LogicalWidth = 360;
    private const int LogicalPadding = 16;
    private const int LogicalLogoSize = 28;
    private const int LogicalTileHeight = 56;
    private const int LogicalTileGap = 8;
    private const int LogicalFooterHeight = 56;

    private readonly List<(ChargeProfile Id, TileButton Tile)> _profileTiles = new();
    private readonly List<(ThermalMode Id, TileButton Tile)> _thermalTiles = new();
    private readonly ToggleSwitch _autoStartToggle;
    private readonly FooterButton _exitButton;
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 4000 };

    private Theme _theme = Theme.Light;
    private Icon? _logo;
    private Font? _titleFont;
    private Font? _statusFont;
    private Font? _sectionFont;
    private Font? _tileTitleFont;
    private Font? _tileSubtitleFont;
    private Font? _glyphFont;
    private Font? _footerFont;
    private Font? _footerGlyphFont;
    private Rectangle _logoRect;
    private Rectangle _titleRect;
    private Rectangle _statusRect;
    private Rectangle _chargeLabelRect;
    private Rectangle _performanceLabelRect;
    private int _footerTop;
    private string _batteryStatus = "";
    private long _hiddenAtTicks;

    public event EventHandler<ChargeProfileInfo>? ProfileRequested;
    public event EventHandler<ThermalModeInfo>? ThermalModeRequested;
    public event EventHandler? AutoStartToggleRequested;
    public event EventHandler? ExitRequested;

    public QuickFlyout()
    {
        Text = AppName;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        // Layout is done by hand in logical pixels, scaled for the monitor's DPI.
        AutoScaleMode = AutoScaleMode.None;

        foreach (ChargeProfileInfo profile in Profiles.All)
        {
            var tile = new TileButton { Glyph = profile.Glyph, Title = profile.Title, Subtitle = profile.Description, AccessibleName = profile.Title };
            tile.Click += (_, _) => ProfileRequested?.Invoke(this, profile);
            _profileTiles.Add((profile.Id, tile));
            Controls.Add(tile);
        }

        foreach (ThermalModeInfo mode in ThermalModes.All)
        {
            var tile = new TileButton { Glyph = mode.Glyph, Title = mode.ShortTitle, Subtitle = mode.Description, AccessibleName = mode.Title };
            tile.Click += (_, _) => ThermalModeRequested?.Invoke(this, mode);
            _thermalTiles.Add((mode.Id, tile));
            Controls.Add(tile);
        }

        _autoStartToggle = new ToggleSwitch { Text = "Auto-start", AccessibleName = "Auto-start" };
        _autoStartToggle.Click += (_, _) => AutoStartToggleRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_autoStartToggle);

        _exitButton = new FooterButton { Text = "Exit", Glyph = Glyphs.Close, AccessibleName = "Exit" };
        _exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        Controls.Add(_exitButton);

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            foreach (TileButton tile in AllTiles())
            {
                tile.StatusText = null;
            }
        };
    }

    /// True right after the flyout closed: a click on the tray icon that made it lose
    /// focus must not reopen it straight away.
    public bool WasJustHidden => Environment.TickCount64 - _hiddenAtTicks < 400;

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= NativeMethods.WS_EX_TOOLWINDOW;   // not listed in Alt+Tab
            cp.ClassStyle |= NativeMethods.CS_DROPSHADOW;
            return cp;
        }
    }

    public void ShowFlyout(Theme theme)
    {
        _batteryStatus = DescribeBattery();
        ApplyTheme(theme);
        Relayout();
        PositionNearTray();
        Show();
        Activate();
    }

    public void HideFlyout()
    {
        if (!Visible)
        {
            return;
        }

        Hide();
        _hiddenAtTicks = Environment.TickCount64;
    }

    public void UpdateState(ChargeProfile? activeProfile, ThermalMode? activeMode, object? busyItem, bool autoStartEnabled)
    {
        foreach ((ChargeProfile id, TileButton tile) in _profileTiles)
        {
            tile.IsSelected = id == activeProfile;
            tile.IsBusy = busyItem is ChargeProfileInfo profile && profile.Id == id;
        }

        foreach ((ThermalMode id, TileButton tile) in _thermalTiles)
        {
            tile.IsSelected = id == activeMode;
            tile.IsBusy = busyItem is ThermalModeInfo mode && mode.Id == id;
        }

        _autoStartToggle.Checked = autoStartEnabled;
    }

    /// Shows a short error on the tile of a switch that failed (details go to errors.log).
    public void ShowFailure(object item)
    {
        TileButton? tile = item switch
        {
            ChargeProfileInfo profile => _profileTiles.FirstOrDefault(t => t.Id == profile.Id).Tile,
            ThermalModeInfo mode => _thermalTiles.FirstOrDefault(t => t.Id == mode.Id).Tile,
            _ => null,
        };

        if (tile is not null)
        {
            tile.StatusText = "Couldn't apply";
            _statusTimer.Stop();
            _statusTimer.Start();
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        HideFlyout();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            HideFlyout();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    // Alt+F4 hides the flyout instead of destroying it.
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            HideFlyout();
        }

        base.OnFormClosing(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetCornerPreference(Handle, NativeMethods.CornerPreference.Round);
        NativeMethods.SetBorderColor(Handle, _theme.Border);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Relayout();
        PositionNearTray();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(_theme.Background);
        if (_titleFont is null || _statusFont is null || _sectionFont is null)
        {
            return;   // not laid out yet
        }

        if (_logo is not null)
        {
            g.DrawIcon(_logo, _logoRect);
        }

        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine
            | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;
        TextRenderer.DrawText(g, AppName, _titleFont, _titleRect, _theme.Text, flags);
        TextRenderer.DrawText(g, _batteryStatus, _statusFont, _statusRect, _theme.SecondaryText, flags);
        TextRenderer.DrawText(g, "Battery charge", _sectionFont, _chargeLabelRect, _theme.SecondaryText, flags);
        TextRenderer.DrawText(g, "Performance", _sectionFont, _performanceLabelRect, _theme.SecondaryText, flags);

        using var footerBrush = new SolidBrush(_theme.FooterBackground);
        g.FillRectangle(footerBrush, 0, _footerTop, ClientSize.Width, ClientSize.Height - _footerTop);
        using var borderPen = new Pen(_theme.Border);
        g.DrawLine(borderPen, 0, _footerTop, ClientSize.Width, _footerTop);

        if (!NativeMethods.IsWindows11OrLater)
        {
            g.DrawRectangle(borderPen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _statusTimer.Dispose();
            DisposeResources();
        }

        base.Dispose(disposing);
    }

    private IEnumerable<TileButton> AllTiles() =>
        _profileTiles.Select(t => t.Tile).Concat(_thermalTiles.Select(t => t.Tile));

    private void ApplyTheme(Theme theme)
    {
        _theme = theme;
        BackColor = theme.Background;

        foreach (TileButton tile in AllTiles())
        {
            tile.Theme = theme;
            tile.BackColor = theme.Background;
        }

        foreach (FlyoutControl control in new FlyoutControl[] { _autoStartToggle, _exitButton })
        {
            control.Theme = theme;
            control.BackColor = theme.FooterBackground;
        }

        if (IsHandleCreated)
        {
            NativeMethods.SetBorderColor(Handle, theme.Border);
        }

        Invalidate();
    }

    private void Relayout()
    {
        int dpi = DeviceDpi;
        int S(int logical) => FluentStyle.Scale(logical, dpi);

        DisposeResources();
        _titleFont = FluentStyle.TextFont(14, dpi, semibold: true);
        _statusFont = FluentStyle.TextFont(12, dpi);
        _sectionFont = FluentStyle.TextFont(12, dpi, semibold: true);
        _tileTitleFont = FluentStyle.TextFont(13, dpi, semibold: true);
        _tileSubtitleFont = FluentStyle.TextFont(12, dpi);
        _glyphFont = FluentStyle.IconFont(18, dpi);
        _footerFont = FluentStyle.TextFont(13, dpi);
        _footerGlyphFont = FluentStyle.IconFont(14, dpi);
        _logo = AppIcon.Load(new Size(S(LogicalLogoSize), S(LogicalLogoSize)));

        int width = S(LogicalWidth);
        int pad = S(LogicalPadding);
        int y = pad;

        int textBlockHeight = _titleFont.Height + S(2) + _statusFont.Height;
        int headerHeight = Math.Max(S(LogicalLogoSize), textBlockHeight);
        _logoRect = new Rectangle(pad, y + (headerHeight - S(LogicalLogoSize)) / 2, S(LogicalLogoSize), S(LogicalLogoSize));
        int textX = _logoRect.Right + S(12);
        int textTop = y + (headerHeight - textBlockHeight) / 2;
        _titleRect = new Rectangle(textX, textTop, width - textX - pad, _titleFont.Height);
        _statusRect = new Rectangle(textX, _titleRect.Bottom + S(2), width - textX - pad, _statusFont.Height);
        y += headerHeight + S(20);

        y = LayoutSection(ref _chargeLabelRect, _profileTiles.Select(t => t.Tile).ToList(), y, width, pad);
        y += S(16);
        y = LayoutSection(ref _performanceLabelRect, _thermalTiles.Select(t => t.Tile).ToList(), y, width, pad);
        y += S(20);

        _footerTop = y;
        int footerHeight = S(LogicalFooterHeight);
        int controlHeight = S(36);
        int controlTop = _footerTop + (footerHeight - controlHeight) / 2;

        _autoStartToggle.LabelFont = _footerFont;
        _autoStartToggle.Bounds = new Rectangle(pad - S(8), controlTop, _autoStartToggle.PreferredWidth, controlHeight);

        _exitButton.LabelFont = _footerFont;
        _exitButton.GlyphFont = _footerGlyphFont;
        int exitWidth = _exitButton.PreferredWidth;
        _exitButton.Bounds = new Rectangle(width - pad + S(8) - exitWidth, controlTop, exitWidth, controlHeight);

        ClientSize = new Size(width, _footerTop + footerHeight);
        Invalidate();
    }

    // Section label followed by its tiles in a 2-column grid; returns the y below them.
    private int LayoutSection(ref Rectangle labelRect, IReadOnlyList<TileButton> tiles, int y, int width, int pad)
    {
        int S(int logical) => FluentStyle.Scale(logical, DeviceDpi);

        labelRect = new Rectangle(pad, y, width - 2 * pad, _sectionFont!.Height);
        y = labelRect.Bottom + S(8);

        int gap = S(LogicalTileGap);
        int tileWidth = (width - 2 * pad - gap) / 2;
        int tileHeight = S(LogicalTileHeight);
        for (int i = 0; i < tiles.Count; i++)
        {
            TileButton tile = tiles[i];
            tile.GlyphFont = _glyphFont;
            tile.TitleFont = _tileTitleFont;
            tile.SubtitleFont = _tileSubtitleFont;
            tile.Bounds = new Rectangle(pad + (i % 2) * (tileWidth + gap), y + (i / 2) * (tileHeight + gap), tileWidth, tileHeight);
        }

        int rows = (tiles.Count + 1) / 2;
        return y + rows * tileHeight + (rows - 1) * gap;
    }

    // Like the system flyouts: bottom-right corner of the working area, above the
    // taskbar (or next to it when the taskbar is at the top or on the left).
    private void PositionNearTray()
    {
        Screen screen = Screen.FromPoint(Cursor.Position);
        Rectangle area = screen.WorkingArea;
        int margin = FluentStyle.Scale(12, DeviceDpi);

        int x = area.Right - Width - margin;
        int y = area.Bottom - Height - margin;
        if (area.Top > screen.Bounds.Top)
        {
            y = area.Top + margin;
        }

        if (area.Left > screen.Bounds.Left)
        {
            x = area.Left + margin;
        }

        Location = new Point(x, Math.Max(area.Top + margin, y));
    }

    private static string DescribeBattery()
    {
        PowerStatus power = SystemInformation.PowerStatus;
        if (power.BatteryChargeStatus == BatteryChargeStatus.Unknown)
        {
            return "Battery status unknown";
        }

        if (power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery))
        {
            return "No battery detected";
        }

        int percent = (int)Math.Round(power.BatteryLifePercent * 100);
        string source = power.PowerLineStatus switch
        {
            PowerLineStatus.Online when power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.Charging) => "charging",
            PowerLineStatus.Online => "plugged in",
            _ => "on battery",
        };

        return $"Battery {percent}% · {source}";
    }

    private void DisposeResources()
    {
        _logo?.Dispose();
        _titleFont?.Dispose();
        _statusFont?.Dispose();
        _sectionFont?.Dispose();
        _tileTitleFont?.Dispose();
        _tileSubtitleFont?.Dispose();
        _glyphFont?.Dispose();
        _footerFont?.Dispose();
        _footerGlyphFont?.Dispose();
    }
}
