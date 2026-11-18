using System.Runtime.InteropServices;

namespace SistecHub.UI;

internal static class Win32Dwm
{
    internal static void TryEnableRoundedCorners(Form form)
    {
        form.Load += (_, _) =>
        {
            if (AppThemeManager.IsDarkMode)
                ApplyTitleBarTheme(form.Handle, true);

            if (Environment.OSVersion.Version.Build < 22000)
                return;
            try
            {
                const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
                const int DWMWCP_ROUND = 2;
                int pref = DWMWCP_ROUND;
                DwmSetWindowAttribute(form.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, sizeof(int));
            }
            catch
            {
                // Ignorar se DWM não aplicar.
            }
        };
    }

    internal static void ApplyTitleBarTheme(IntPtr hwnd, bool isDark)
    {
        if (hwnd == IntPtr.Zero)
            return;

        try
        {
            const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
            const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            int value = isDark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref value, sizeof(int));
        }
        catch
        {
            // Ignorar em versões do Windows sem suporte ao atributo.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
