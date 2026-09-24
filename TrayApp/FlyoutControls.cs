using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace BatteryPerformanceManager.TrayApp;

/// Base for the owner-drawn controls of the flyout: hover/pressed state, keyboard
/// activation (Space/Enter) and the focus cue, which Windows only shows after
/// keyboard use.
internal abstract class FlyoutControl : Control
{
    private Theme _theme = Theme.Light;

    protected FlyoutControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
    }

    public Theme Theme
    {
        get => _theme;
        set
        {
            _theme = value;
            Invalidate();
        }
    }

    protected bool IsHovered { get; private set; }

    protected bool IsPressed { get; private set; }

    protected float S(float logical) => logical * DeviceDpi / 96f;

    protected override void OnMouseEnter(EventArgs e)
    {
        IsHovered = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        IsHovered = false;
        IsPressed = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            IsPressed = true;
            Invalidate();
        }

        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        IsPressed = false;
        Invalidate();
        base.OnMouseUp(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Enter || base.IsInputKey(keyData);

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            OnClick(EventArgs.Empty);
        }
    }

    protected Graphics BeginPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        return e.Graphics;
    }

    protected void DrawFocusCue(Graphics g, RectangleF bounds, float radius)
    {
        if (!Focused || !ShowFocusCues)
        {
            return;
        }

        using var pen = new Pen(Theme.Text, S(2));
        bounds.Inflate(-S(1), -S(1));
        using GraphicsPath path = FluentStyle.RoundedRect(bounds, radius);
        g.DrawPath(pen, path);
    }
}

/// A charge profile or thermal mode: icon, title and description, filled with the
/// accent color when active, with a spinner and "Applying…" while it's being set.
internal sealed class TileButton : FlyoutControl
{
    private readonly System.Windows.Forms.Timer _spinnerTimer = new() { Interval = 30 };
    private float _spinnerAngle;
    private bool _isSelected;
    private bool _isBusy;
    private string? _statusText;

    public TileButton()
    {
        AccessibleRole = AccessibleRole.PushButton;
        _spinnerTimer.Tick += (_, _) =>
        {
            _spinnerAngle = (_spinnerAngle + 12) % 360;
            Invalidate();
        };
    }

    public string Glyph { get; init; } = "";

    public string Title { get; init; } = "";

    public string Subtitle { get; init; } = "";

    public Font? GlyphFont { get; set; }

    public Font? TitleFont { get; set; }

    public Font? SubtitleFont { get; set; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            AccessibleDescription = value ? $"{Subtitle}, active" : Subtitle;
            Invalidate();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            _spinnerTimer.Enabled = value;
            Invalidate();
        }
    }

    /// Temporary text shown instead of the subtitle (e.g. after a failure); null clears it.
    public string? StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = BeginPaint(e);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        float radius = S(6);

        Color fill = IsSelected
            ? IsPressed ? Theme.AccentPressed : IsHovered ? Theme.AccentHover : Theme.Accent
            : IsPressed ? Theme.TilePressed : IsHovered ? Theme.TileHover : Theme.Tile;

        using (GraphicsPath path = FluentStyle.RoundedRect(bounds, radius))
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
            if (!IsSelected)
            {
                using var pen = new Pen(Theme.TileBorder);
                g.DrawPath(pen, path);
            }
        }

        Color primary = IsSelected ? Theme.OnAccent : Theme.Text;
        Color secondary = IsSelected ? Theme.OnAccentSecondary : Theme.SecondaryText;

        var iconArea = new Rectangle((int)S(12), 0, (int)S(24), Height);
        if (IsBusy)
        {
            DrawSpinner(g, iconArea, primary);
        }
        else if (GlyphFont is not null)
        {
            TextRenderer.DrawText(g, Glyph, GlyphFont, iconArea, primary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (TitleFont is not null && SubtitleFont is not null)
        {
            string subtitle = IsBusy ? "Applying…" : StatusText ?? Subtitle;
            int textX = (int)S(46);
            int textWidth = Width - textX - (int)S(10);
            int gap = (int)S(1);
            int top = (Height - TitleFont.Height - gap - SubtitleFont.Height) / 2;
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine
                | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(g, Title, TitleFont, new Rectangle(textX, top, textWidth, TitleFont.Height), primary, flags);
            TextRenderer.DrawText(g, subtitle, SubtitleFont,
                new Rectangle(textX, top + TitleFont.Height + gap, textWidth, SubtitleFont.Height), secondary, flags);
        }

        DrawFocusCue(g, bounds, radius);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _spinnerTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void DrawSpinner(Graphics g, Rectangle area, Color color)
    {
        float radius = S(7);
        float centerX = area.Left + area.Width / 2f;
        float centerY = area.Top + area.Height / 2f;
        using var pen = new Pen(color, S(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawArc(pen, centerX - radius, centerY - radius, radius * 2, radius * 2, _spinnerAngle, 110);
    }
}

/// Label followed by a Windows 11 style on/off switch; the whole control is clickable.
internal sealed class ToggleSwitch : FlyoutControl
{
    private bool _checked;

    public ToggleSwitch()
    {
        AccessibleRole = AccessibleRole.CheckButton;
    }

    public bool Checked
    {
        get => _checked;
        set
        {
            if (_checked == value)
            {
                return;
            }

            _checked = value;
            AccessibleDescription = value ? "On" : "Off";
            Invalidate();
        }
    }

    public Font? LabelFont { get; set; }

    public int PreferredWidth => LabelFont is null
        ? 0
        : TextRenderer.MeasureText(Text, LabelFont, Size.Empty, TextFormatFlags.NoPadding).Width + (int)S(8 + 12 + 40 + 8);

    private static void DrawSwitch(Graphics g, RectangleF track, bool isOn, bool hovered, Theme theme, int dpi)
    {
        float S(float logical) => logical * dpi / 96f;
        using GraphicsPath path = FluentStyle.RoundedRect(track, track.Height / 2);
        float knob = hovered ? S(14) : S(12);
        float centerY = track.Top + track.Height / 2;

        if (isOn)
        {
            using var fill = new SolidBrush(hovered ? theme.AccentHover : theme.Accent);
            g.FillPath(fill, path);
            float centerX = track.Right - track.Height / 2;
            using var knobBrush = new SolidBrush(theme.OnAccent);
            g.FillEllipse(knobBrush, centerX - knob / 2, centerY - knob / 2, knob, knob);
        }
        else
        {
            using var pen = new Pen(theme.ToggleOff, S(1));
            g.DrawPath(pen, path);
            float centerX = track.Left + track.Height / 2;
            using var knobBrush = new SolidBrush(theme.ToggleOff);
            g.FillEllipse(knobBrush, centerX - knob / 2, centerY - knob / 2, knob, knob);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = BeginPaint(e);
        if (LabelFont is null)
        {
            return;
        }

        int labelX = (int)S(8);
        Size labelSize = TextRenderer.MeasureText(Text, LabelFont, Size.Empty, TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, Text, LabelFont, new Rectangle(labelX, 0, labelSize.Width, Height), Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        float width = S(40), height = S(20);
        var track = new RectangleF(labelX + labelSize.Width + S(12), (Height - height) / 2f, width, height);
        DrawSwitch(g, track, Checked, IsHovered, Theme, DeviceDpi);

        DrawFocusCue(g, new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), S(4));
    }
}

/// Small icon + text button for the flyout footer (Exit).
internal sealed class FooterButton : FlyoutControl
{
    public FooterButton()
    {
        AccessibleRole = AccessibleRole.PushButton;
    }

    public string Glyph { get; init; } = "";

    public Font? GlyphFont { get; set; }

    public Font? LabelFont { get; set; }

    public int PreferredWidth => LabelFont is null
        ? 0
        : TextRenderer.MeasureText(Text, LabelFont, Size.Empty, TextFormatFlags.NoPadding).Width + (int)S(12 + 16 + 8 + 12);

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = BeginPaint(e);
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

        if (IsHovered || IsPressed)
        {
            using GraphicsPath path = FluentStyle.RoundedRect(bounds, S(4));
            using var brush = new SolidBrush(IsPressed ? Theme.Pressed : Theme.Hover);
            g.FillPath(brush, path);
        }

        if (GlyphFont is not null)
        {
            TextRenderer.DrawText(g, Glyph, GlyphFont, new Rectangle((int)S(12), 0, (int)S(16), Height), Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        if (LabelFont is not null)
        {
            int textX = (int)S(12 + 16 + 8);
            TextRenderer.DrawText(g, Text, LabelFont, new Rectangle(textX, 0, Width - textX, Height), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        DrawFocusCue(g, bounds, S(4));
    }
}
