using Microsoft.Win32;

namespace AddressBar;

public static class ThemeHelper
{
    public static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int i && i == 0;
        }
        catch { return false; }
    }

    public static Color GetSystemBackColor() =>
        IsDarkMode() ? Color.FromArgb(32, 32, 32) : Color.FromArgb(243, 243, 243);

    public static Color GetSystemForeColor() =>
        IsDarkMode() ? Color.FromArgb(255, 255, 255) : Color.FromArgb(0, 0, 0);

    public static Color GetTextBoxBackColor() =>
        IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;

    public static Color GetButtonBackColor() =>
        IsDarkMode() ? Color.FromArgb(55, 55, 55) : Color.FromArgb(225, 225, 225);

    public static Color GetBorderColor() =>
        IsDarkMode() ? Color.FromArgb(70, 70, 70) : Color.FromArgb(200, 200, 200);

    public static Color GetDropdownHoverColor() =>
        IsDarkMode() ? Color.FromArgb(60, 60, 60) : Color.FromArgb(220, 220, 220);

    public static Color GetDropdownBackColor() =>
        IsDarkMode() ? Color.FromArgb(45, 45, 45) : Color.White;

    public static Color GetSelectionBackColor() =>
        Color.FromArgb(0, 120, 215);

    public static Color GetHoverBackColor() =>
        IsDarkMode() ? Color.FromArgb(65, 65, 65) : Color.FromArgb(230, 230, 230);
}
