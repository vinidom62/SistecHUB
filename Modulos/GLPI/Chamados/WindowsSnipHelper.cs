using System.Diagnostics;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SistecHub.Modulos.GLPI.Chamados;

/// <summary>
/// Abre a captura de ecrã integrada do Windows (Snip &amp; Sketch / recorte) e expõe o número de sequência da área de transferência.
/// </summary>
internal static class WindowsSnipHelper
{
    const uint KEYEVENTF_KEYUP = 0x0002;

    [DllImport("user32.dll")]
    internal static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    /// <summary>Tenta abrir o recorte moderno (<c>ms-screenclip:</c>); se falhar, simula Win+Shift+S.</summary>
    internal static void TryLaunchSnippingUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-screenclip:",
                UseShellExecute = true,
            });
            return;
        }
        catch
        {
            // fallback
        }

        // Win+Shift+S — ordem: Win↓ Shift↓ S↓ S↑ Shift↑ Win↑
        const byte vkLWin = 0x5B;
        const byte vkShift = 0x10;
        const byte vkS = 0x53;

        keybd_event(vkLWin, 0, 0, UIntPtr.Zero);
        keybd_event(vkShift, 0, 0, UIntPtr.Zero);
        keybd_event(vkS, 0, 0, UIntPtr.Zero);
        keybd_event(vkS, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(vkShift, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        keybd_event(vkLWin, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
    }

    /// <summary>Grava a imagem da área de transferência num PNG temporário e devolve o caminho, ou <c>null</c>.</summary>
    internal static string? TrySaveClipboardImageToTempPng()
    {
        try
        {
            if (!Clipboard.ContainsImage())
                return null;
            using var img = Clipboard.GetImage();
            if (img is null)
                return null;
            var path = Path.Combine(Path.GetTempPath(), $"sistechub-captura-{Guid.NewGuid():N}.png");
            img.Save(path, ImageFormat.Png);
            return path;
        }
        catch
        {
            return null;
        }
    }
}
