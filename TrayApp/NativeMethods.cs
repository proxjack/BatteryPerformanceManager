using System.Drawing;
using System.Runtime.InteropServices;

namespace BatteryChargeManager.TrayApp;

internal static class NativeMethods
{
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int CS_DROPSHADOW = 0x00020000;

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;

    public enum CornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    public static bool IsWindows11OrLater => Environment.OSVersion.Version.Build >= 22000;

    // Both attributes only exist on Windows 11: on Windows 10 the call fails harmlessly
    // and the window keeps square corners, with the border drawn by the app itself.
    public static void SetCornerPreference(IntPtr hwnd, CornerPreference preference)
    {
        int value = (int)preference;
        _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref value, sizeof(int));
    }

    public static void SetBorderColor(IntPtr hwnd, Color color)
    {
        int value = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref value, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
