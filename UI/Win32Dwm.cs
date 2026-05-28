using System.Runtime.InteropServices;

namespace SistecHub.UI;

internal static class Win32Dwm
{
    internal static void TryEnableRoundedCorners(Form form)
    {
        form.Load += (_, _) =>
        {
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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);
}
