using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryChargeManager.TrayApp;

/// Right-click menu of the tray icon, drawn in the Windows 11 style: an icon per item,
/// the description in the shortcut column, the active profile/mode marked with an
/// accent pill and icon, and a switch for auto-start.
internal sealed class TrayMenu : ContextMenuStrip
{
    // Reserves the icon area of an item; the renderer draws the glyph in it instead.
    private static readonly Bitmap GlyphPlaceholder = new(1, 1);

    private readonly FluentMenuRenderer _renderer = new();
    private readonly Dictionary<ChargeProfile, FluentMenuItem> _profileItems = new();
    private readonly Dictionary<ThermalMode, FluentMenuItem> _thermalItems = new();
    private readonly FluentMenuItem _autoStartItem;
    private Font? _itemFont;
    private Font? _headerFont;
    private int _metricsDpi;

    public event EventHandler<ChargeProfileInfo>? ProfileRequested;
    public event EventHandler<ThermalModeInfo>? ThermalModeRequested;
    public event EventHandler? AutoStartToggleRequested;
    public event EventHandler? ExitRequested;

    public TrayMenu()
    {
        Renderer = _renderer;
        ShowCheckMargin = false;
        ShowImageMargin = true;

        AddHeader("Battery charge");
        foreach (ChargeProfileInfo profile in Profiles.All)
        {
            _profileItems[profile.Id] = AddItem(profile.Title, profile.Description, profile.Glyph,
                () => ProfileRequested?.Invoke(this, profile));
        }

        Items.Add(new ToolStripSeparator());

        AddHeader("Performance");
        foreach (ThermalModeInfo mode in ThermalModes.All)
        {
            _thermalItems[mode.Id] = AddItem(mode.Title, mode.Description, mode.Glyph,
                () => ThermalModeRequested?.Invoke(this, mode));
        }

        Items.Add(new ToolStripSeparator());

        _autoStartItem = AddItem("Auto-start", null, Glyphs.Power,
            () => AutoStartToggleRequested?.Invoke(this, EventArgs.Empty));
        _autoStartItem.IsToggle = true;

        AddItem("Exit", null, Glyphs.Close, () => ExitRequested?.Invoke(this, EventArgs.Empty));
    }

    public void UpdateState(ChargeProfile? activeProfile, ThermalMode? activeMode, bool autoStartEnabled)
    {
        foreach ((ChargeProfile id, FluentMenuItem item) in _profileItems)
        {
            item.Checked = id == activeProfile;
        }

        foreach ((ThermalMode id, FluentMenuItem item) in _thermalItems)
        {
            item.Checked = id == activeMode;
        }

        _autoStartItem.Checked = autoStartEnabled;
    }

    /// Applies colors and DPI-dependent metrics; called right before the menu opens.
    public void ApplyTheme(Theme theme)
    {
        _renderer.Theme = theme;
        if (_metricsDpi != DeviceDpi)
        {
            UpdateMetrics();
        }

        if (IsHandleCreated)
        {
            NativeMethods.SetBorderColor(Handle, theme.Border);
        }

        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.SetCornerPreference(Handle, NativeMethods.CornerPreference.RoundSmall);
        NativeMethods.SetBorderColor(Handle, _renderer.Theme.Border);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _itemFont?.Dispose();
            _headerFont?.Dispose();
            _renderer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void AddHeader(string text) => Items.Add(new FluentMenuItem(text) { IsHeader = true, Enabled = false });

    private FluentMenuItem AddItem(string text, string? description, string glyph, Action onClick)
    {
        var item = new FluentMenuItem(text)
        {
            Glyph = glyph,
            Image = GlyphPlaceholder,
            ImageScaling = ToolStripItemImageScaling.SizeToFit,
            ShortcutKeyDisplayString = description,
        };
        item.Click += (_, _) => onClick();
        Items.Add(item);
        return item;
    }

    private void UpdateMetrics()
    {
        _metricsDpi = DeviceDpi;
        int S(int logical) => FluentStyle.Scale(logical, DeviceDpi);

        _itemFont?.Dispose();
        _headerFont?.Dispose();
        _itemFont = FluentStyle.TextFont(13, DeviceDpi);
        _headerFont = FluentStyle.TextFont(12, DeviceDpi, semibold: true);
        _renderer.UpdateMetrics(DeviceDpi);

        Font = _itemFont;
        // Wider than the 16 px glyph: room for the accent pill on the left and a gap before the text.
        ImageScalingSize = new Size(S(32), S(16));
        Padding = new Padding(0, S(4), 0, S(4));
        MinimumSize = new Size(S(260), 0);

        foreach (ToolStripItem item in Items)
        {
            if (item is FluentMenuItem { IsHeader: true })
            {
                item.Font = _headerFont;
                item.Padding = new Padding(0, S(6), 0, S(2));
            }
            else if (item is FluentMenuItem)
            {
                item.Font = _itemFont;
                item.Padding = new Padding(0, S(6), 0, S(6));
            }
        }
    }
}

internal sealed class FluentMenuItem : ToolStripMenuItem
{
    public FluentMenuItem(string text) : base(text)
    {
    }

    public string? Glyph { get; init; }

    /// Non-clickable section title ("Battery charge", "Performance").
    public bool IsHeader { get; init; }

    /// Shows Checked as an on/off switch on the right instead of the accent pill.
    public bool IsToggle { get; set; }
}

internal sealed class FluentMenuRenderer : ToolStripRenderer, IDisposable
{
    private Font? _iconFont;
    private int _dpi = 96;

    public Theme Theme { get; set; } = Theme.Light;

    public void UpdateMetrics(int dpi)
    {
        _dpi = dpi;
        _iconFont?.Dispose();
        _iconFont = FluentStyle.IconFont(16, dpi);
    }

    public void Dispose() => _iconFont?.Dispose();

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var brush = new SolidBrush(Theme.Background);
        e.Graphics.FillRectangle(brush, e.AffectedBounds);
    }

    // On Windows 11 DWM draws the rounded border (see TrayMenu.OnHandleCreated).
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        if (NativeMethods.IsWindows11OrLater)
        {
            return;
        }

        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    // No gray icon gutter: the background is already painted.
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
    {
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Theme.Border);
        e.Graphics.DrawLine(pen, 0, y, e.Item.Width, y);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Enabled || !(e.Item.Selected || e.Item.Pressed))
        {
            return;
        }

        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(S(4), S(1), e.Item.Width - S(8), e.Item.Height - S(2));
        using GraphicsPath path = FluentStyle.RoundedRect(bounds, S(4));
        using var brush = new SolidBrush(e.Item.Pressed ? Theme.Pressed : Theme.Hover);
        g.FillPath(brush, path);
    }

    // The active item is shown by OnRenderItemImage (accent pill + icon), not by a check.
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
    }

    protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is not FluentMenuItem { Glyph: { } glyph } item || _iconFont is null)
        {
            return;
        }

        Graphics g = e.Graphics;
        bool active = item.Checked && !item.IsToggle;
        if (active)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float height = S(16);
            var pill = new RectangleF(S(5), (item.Height - height) / 2f, S(3), height);
            using GraphicsPath path = FluentStyle.RoundedRect(pill, pill.Width / 2);
            using var brush = new SolidBrush(Theme.AccentText);
            g.FillPath(brush, path);
        }

        // Fixed position rather than e.ImageRectangle, so the glyph never touches the pill or the
        // edge; nudged up because Segoe Fluent Icons glyphs sit slightly below the text's center.
        var glyphArea = new Rectangle((int)S(10), -(int)S(1.5f), (int)S(20), item.Height);
        TextRenderer.DrawText(g, glyph, _iconFont, glyphArea, active ? Theme.AccentText : Theme.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        // Called once for the text and once for the shortcut column, which holds the description.
        bool isDescription = e.Item is ToolStripMenuItem menuItem
            && e.Text == menuItem.ShortcutKeyDisplayString && e.Text != menuItem.Text;
        Color color = !e.Item.Enabled || isDescription ? Theme.SecondaryText : Theme.Text;
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, color, e.TextFormat);

        if (e.Item is FluentMenuItem { IsToggle: true } toggle && !isDescription)
        {
            DrawToggle(e.Graphics, toggle);
        }
    }

    private void DrawToggle(Graphics g, FluentMenuItem item)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float width = S(40), height = S(20);
        var track = new RectangleF(item.Width - S(14) - width, (item.Height - height) / 2f, width, height);
        ToggleSwitch.DrawSwitch(g, track, item.Checked, hovered: false, Theme, _dpi);
    }

    private float S(float logical) => logical * _dpi / 96f;
}
