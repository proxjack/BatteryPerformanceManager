using System.Drawing;
using Microsoft.Win32;

namespace BatteryPerformanceManager.TrayApp;

/// Colors of the tray flyout. They follow the Windows light/dark setting
/// for system surfaces (taskbar, Start, tray flyouts) rather than the one for apps,
/// since this UI lives next to the taskbar, and the Windows accent color, picked the
/// way Windows 11 itself does: a darker shade of it in light mode, a lighter one in
/// dark mode.
internal sealed record Theme(
    Color Background,
    Color FooterBackground,
    Color Border,
    Color Text,
    Color SecondaryText,
    Color Hover,
    Color Pressed,
    Color Tile,
    Color TileHover,
    Color TilePressed,
    Color TileBorder,
    Color ToggleOff,
    Color Accent,
    Color AccentHover,
    Color AccentPressed,
    Color OnAccent,
    Color OnAccentSecondary,
    Color AccentText)
{
    private const string AccentKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    // The accent here is only the fallback (the logo's teal) for when the Windows
    // accent palette can't be read.
    public static readonly Theme Light = WithAccent(new Theme(
        Background: Rgb(0xF9F9F9),
        FooterBackground: Rgb(0xF0F0F0),
        Border: Rgb(0xE0E0E0),
        Text: Rgb(0x1B1B1B),
        SecondaryText: Rgb(0x5F5F5F),
        Hover: Rgb(0xEAEAEA),
        Pressed: Rgb(0xE2E2E2),
        Tile: Rgb(0xFFFFFF),
        TileHover: Rgb(0xF5F5F5),
        TilePressed: Rgb(0xEEEEEE),
        TileBorder: Rgb(0xE3E3E3),
        ToggleOff: Rgb(0x868686),
        Accent: default, AccentHover: default, AccentPressed: default,
        OnAccent: default, OnAccentSecondary: default, AccentText: default),
        fill: Rgb(0x0F7A5F), text: Rgb(0x0F7A5F));

    public static readonly Theme Dark = WithAccent(new Theme(
        Background: Rgb(0x2B2B2B),
        FooterBackground: Rgb(0x232323),
        Border: Rgb(0x3D3D3D),
        Text: Rgb(0xFFFFFF),
        SecondaryText: Rgb(0xC2C2C2),
        Hover: Rgb(0x383838),
        Pressed: Rgb(0x323232),
        Tile: Rgb(0x333333),
        TileHover: Rgb(0x3A3A3A),
        TilePressed: Rgb(0x2F2F2F),
        TileBorder: Rgb(0x404040),
        ToggleOff: Rgb(0xA0A0A0),
        Accent: default, AccentHover: default, AccentPressed: default,
        OnAccent: default, OnAccentSecondary: default, AccentText: default),
        fill: Rgb(0x5DCAA5), text: Rgb(0x9FE1CB));

    /// Read on every opening, so a change of theme or accent color applies without
    /// restarting the app.
    public static Theme Current
    {
        get
        {
            bool dark = IsSystemDark();
            Theme theme = dark ? Dark : Light;
            return TryReadAccent(dark) is { } accent ? WithAccent(theme, accent.Fill, accent.Text) : theme;
        }
    }

    private static Theme WithAccent(Theme theme, Color fill, Color text)
    {
        // Text on the accent: black or white, whichever reads better (e.g. black on
        // the light yellow of a gold accent in dark mode).
        Color onAccent = Contrast(Color.Black, fill) >= Contrast(Color.White, fill) ? Color.Black : Color.White;
        return theme with
        {
            Accent = fill,
            AccentHover = Blend(fill, theme.Background, 0.9f),
            AccentPressed = Blend(fill, theme.Background, 0.8f),
            OnAccent = onAccent,
            OnAccentSecondary = Blend(onAccent, fill, 0.75f),
            AccentText = text,
        };
    }

    // AccentPalette holds 8 RGBA entries from lightest to darkest: Light3, Light2,
    // Light1, Base, Dark1, Dark2, Dark3 (+ one unused). Like Windows 11, fills use
    // Dark1 in light mode and Light2 in dark mode; accent-colored text/icons use
    // Dark2 and Light3.
    private static (Color Fill, Color Text)? TryReadAccent(bool dark)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(AccentKeyPath);
            if (key?.GetValue("AccentPalette") is not byte[] { Length: >= 28 } palette)
            {
                return null;
            }

            Color Entry(int index) => Color.FromArgb(palette[index * 4], palette[index * 4 + 1], palette[index * 4 + 2]);
            return dark ? (Entry(1), Entry(0)) : (Entry(4), Entry(5));
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSystemDark()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return key?.GetValue("SystemUsesLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private static Color Blend(Color top, Color bottom, float topWeight) => Color.FromArgb(
        (int)Math.Round(top.R * topWeight + bottom.R * (1 - topWeight)),
        (int)Math.Round(top.G * topWeight + bottom.G * (1 - topWeight)),
        (int)Math.Round(top.B * topWeight + bottom.B * (1 - topWeight)));

    // WCAG contrast ratio between two colors.
    private static double Contrast(Color a, Color b)
    {
        double la = Luminance(a), lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static Color Rgb(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);
}
