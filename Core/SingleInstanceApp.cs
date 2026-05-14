using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SistecHub.Core;

/// <summary>Evita mais do que um processo do SistecHub; pedidos seguintes reativam a janela já aberta (ou na bandeja).</summary>
public static class SingleInstanceApp
{
    const string MutexName = "Local\\SistecHub_B5E0D6C2-8C4A-4A6F-9A2E-SingleInstance";
    const string ShowInstanceMessageName = "SistecHub.Coop.InstanceShowV1";

    static Mutex? _mutex;
    static uint? _instanceMessage;

    /// <summary>Valor devolvido por <c>RegisterWindowMessage</c>; igual em qualquer instância do SistecHub (mesma sessão).</summary>
    public static uint InstanceActivateMessage =>
        _instanceMessage ??= RegisterWindowMessage(ShowInstanceMessageName);

    public static bool TryEnterFirstInstance()
    {
        try
        {
            _mutex = new Mutex(true, MutexName, out bool created);
            return created;
        }
        catch
        {
            return false;
        }
    }

    public static void ReleaseFirstInstance()
    {
        try
        {
            _mutex?.ReleaseMutex();
        }
        catch
        {
        }
        _mutex?.Dispose();
        _mutex = null;
    }

    public static void TryActivateExisting()
    {
        var currentId = Process.GetCurrentProcess().Id;
        var processName = Path.GetFileNameWithoutExtension(
            System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "SistecHub");

        foreach (var p in Process.GetProcessesByName(processName))
        {
            if (p.Id == currentId)
                continue;

            try
            {
                p.Refresh();
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    if (RequestExistingInstanceToShow(p.MainWindowHandle))
                        return;
                }
                else if (TryFindTopLevelWindow(p, out var hwnd) && RequestExistingInstanceToShow(hwnd))
                {
                    return;
                }
            }
            catch
            {
            }
        }
    }

    static bool RequestExistingInstanceToShow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return false;
        // Primeiro: pede à janela para voltar a mostrar-se (fila de mensagens; WinForms ignora o estado interno de Hide()).
        PostMessage(hWnd, InstanceActivateMessage, IntPtr.Zero, IntPtr.Zero);
        // Reforça foco/visibilidade a nível Win32.
        if (IsIconic(hWnd))
            ShowWindow(hWnd, 9);
        if (!IsWindowVisible(hWnd))
            ShowWindow(hWnd, 5);
        return SetForegroundWindow(hWnd);
    }

    static bool TryFindTopLevelWindow(Process p, out IntPtr hwnd)
    {
        hwnd = IntPtr.Zero;
        uint target = (uint)p.Id;
        IntPtr fallback = IntPtr.Zero;
        var found = IntPtr.Zero;

        EnumWindows((h, _) =>
        {
            GetWindowThreadProcessId(h, out var pid);
            if (pid != target)
                return true;
            if (GetParent(h) != IntPtr.Zero)
                return true;

            var text = new StringBuilder(512);
            if (GetWindowTextW(h, text, text.Capacity) > 0)
            {
                if (text.ToString().Contains("SistecHub", StringComparison.Ordinal))
                {
                    found = h;
                    return false;
                }
            }

            if (fallback == IntPtr.Zero)
                fallback = h;
            return true;
        }, IntPtr.Zero);

        if (found != IntPtr.Zero)
        {
            hwnd = found;
            return true;
        }
        if (fallback != IntPtr.Zero)
        {
            hwnd = fallback;
            return true;
        }
        return false;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", ExactSpelling = true)]
    static extern IntPtr GetParent(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern int GetWindowTextW(IntPtr hWnd, StringBuilder lpString, int nMaxCount);
}
