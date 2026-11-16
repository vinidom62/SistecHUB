using System.Diagnostics;
using System.Runtime.InteropServices;
using SistecHub.Core;

namespace SistecHub.Modulos.AcessoRemoto;

/// <summary>
/// Monitor de prioridade de entrada do cliente durante a sessão de acesso remoto.
/// Quando o cliente físico na máquina host move o mouse ou digita no teclado,
/// este monitor detecta a atividade local e concede prioridade temporária ao cliente,
/// bloqueando as entradas do técnico remoto pelo período configurado (1,0s).
/// </summary>
internal static class AcessoRemotoClientPriorityMonitor
{
    public const nint RemoteInputMarker = 0x53495354; // 'SIST'
    private const int PriorityTimeoutMs = 1000; // 1,0 segundo de inatividade para liberar o técnico

    private const int WH_KEYBOARD_LL = 13;
    private const int WH_MOUSE_LL = 14;

    private const int WM_MOUSEMOVE = 0x0200;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONDOWN = 0x0204;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_MBUTTONDOWN = 0x0207;
    private const int WM_MBUTTONUP = 0x0208;
    private const int WM_MOUSEWHEEL = 0x020A;
    private const int WM_MOUSEHWHEEL = 0x020E;

    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;

    private const uint LLMHF_INJECTED = 0x00000001;
    private const uint LLKHF_INJECTED = 0x00000010;

    private const uint WM_QUIT = 0x0012;

    private static readonly object SyncRoot = new();
    private static Thread? _hookThread;
    private static uint _hookThreadId;
    private static nint _mouseHook;
    private static nint _keyboardHook;
    private static HookProc? _mouseProcDelegate;
    private static HookProc? _keyboardProcDelegate;

    private static System.Threading.Timer? _debounceTimer;
    private static long _lastPhysicalInputTick;
    private static bool _isClientActive;
    private static bool _isUserBlockedLocally;

    private static int _lastRemoteCursorX = -9999;
    private static int _lastRemoteCursorY = -9999;
    private static long _lastRemoteCursorTick;

    private static int _lastPhysicalX = -9999;
    private static int _lastPhysicalY = -9999;

    public static event Action<bool>? PriorityChanged;
    public static event Action<int, int>? ClientPhysicalActivityOccurred;

    /// <summary>
    /// Indica se o cliente físico interagiu recentemente com o mouse ou teclado (cooldown de 1,0s).
    /// </summary>
    public static bool HasRecentPhysicalActivity
    {
        get
        {
            lock (SyncRoot)
            {
                return _isClientActive && !_isUserBlockedLocally;
            }
        }
    }

    /// <summary>
    /// Indica se o cliente local está atualmente ativo com o mouse ou teclado,
    /// exigindo que as entradas do técnico sejam descartadas.
    /// </summary>
    public static bool IsClientActive
    {
        get
        {
            lock (SyncRoot)
            {
                return _isClientActive && !_isUserBlockedLocally;
            }
        }
    }

    /// <summary>
    /// Notifica que o técnico ativou ou desativou a opção "Bloquear entrada do usuário".
    /// Se a opção estiver ativada, a prioridade do cliente é desativada (as entradas do cliente permanecem bloqueadas).
    /// </summary>
    public static void SetUserBlockedLocally(bool isBlocked)
    {
        bool changed = false;
        lock (SyncRoot)
        {
            _isUserBlockedLocally = isBlocked;
            if (isBlocked && _isClientActive)
            {
                _isClientActive = false;
                changed = true;
            }
        }

        if (changed)
        {
            AppDebugLog.Debug("AcessoRemoto", "Entrada do cliente bloqueada pelo técnico: prioridade desativada.");
            try
            {
                PriorityChanged?.Invoke(false);
            }
            catch { }
        }
    }

    /// <summary>
    /// Inicia o monitoramento de baixo nível na máquina local.
    /// </summary>
    public static void Start()
    {
        lock (SyncRoot)
        {
            if (_hookThread is { IsAlive: true })
                return;

            _isClientActive = false;
            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

            _hookThread = new Thread(HookThreadLoop)
            {
                IsBackground = true,
                Name = "SistecHubClientPriorityMonitorThread"
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }
    }

    /// <summary>
    /// Interrompe o monitoramento e remove os hooks do sistema.
    /// </summary>
    public static void Stop()
    {
        lock (SyncRoot)
        {
            _isClientActive = false;
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            if (_hookThreadId != 0)
            {
                PostThreadMessage(_hookThreadId, WM_QUIT, 0, 0);
                _hookThreadId = 0;
            }

            _hookThread = null;
        }

        try
        {
            PriorityChanged?.Invoke(false);
        }
        catch { }
    }

    /// <summary>
    /// Registra que a posição do cursor foi alterada programaticamente pelo técnico remoto via SetCursorPos.
    /// </summary>
    public static void NotifyRemoteCursorMoved(int x, int y)
    {
        _lastRemoteCursorX = x;
        _lastRemoteCursorY = y;
        _lastRemoteCursorTick = Environment.TickCount64;
    }

    private static void HookThreadLoop()
    {
        _hookThreadId = GetCurrentThreadId();

        _mouseProcDelegate = MouseHookCallback;
        _keyboardProcDelegate = KeyboardHookCallback;

        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        nint hMod = curModule != null ? GetModuleHandle(curModule.ModuleName) : IntPtr.Zero;

        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProcDelegate, hMod, 0);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProcDelegate, hMod, 0);

        if (_mouseHook == IntPtr.Zero || _keyboardHook == IntPtr.Zero)
        {
            int err = Marshal.GetLastWin32Error();
            AppDebugLog.Warn("AcessoRemoto", $"Falha ao instalar hooks de prioridade do cliente (Erro Win32 {err}).");
        }
        else
        {
            AppDebugLog.Debug("AcessoRemoto", "Hooks de prioridade do cliente instalados com sucesso.");
        }

        try
        {
            while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }
        finally
        {
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            _mouseProcDelegate = null;
            _keyboardProcDelegate = null;
            AppDebugLog.Debug("AcessoRemoto", "Hooks de prioridade do cliente desinstalados.");
        }
    }

    private static nint MouseHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

            bool isInjected = (data.flags & LLMHF_INJECTED) != 0 || data.dwExtraInfo == RemoteInputMarker;

            if (!isInjected)
            {
                int msg = (int)wParam;
                if (msg == WM_MOUSEMOVE)
                {
                    // Verifica se o movimento foi gerado pelo SetCursorPos/SendMouse remoto recente
                    long tickDiff = Environment.TickCount64 - _lastRemoteCursorTick;
                    bool isRemoteSetPos = tickDiff < 250 &&
                                          Math.Abs(data.pt.X - _lastRemoteCursorX) <= 4 &&
                                          Math.Abs(data.pt.Y - _lastRemoteCursorY) <= 4;

                    if (!isRemoteSetPos)
                    {
                        // Filtro de jitter: ignora vibrações microscópicas do sensor do mouse (< 3 pixels)
                        int dx = Math.Abs(data.pt.X - _lastPhysicalX);
                        int dy = Math.Abs(data.pt.Y - _lastPhysicalY);

                        if (dx >= 3 || dy >= 3)
                        {
                            _lastPhysicalX = data.pt.X;
                            _lastPhysicalY = data.pt.Y;
                            RecordClientPhysicalActivity();
                        }
                    }
                }
                else if (msg is WM_LBUTTONDOWN or WM_LBUTTONUP or
                                WM_RBUTTONDOWN or WM_RBUTTONUP or
                                WM_MBUTTONDOWN or WM_MBUTTONUP or
                                WM_MOUSEWHEEL or WM_MOUSEHWHEEL)
                {
                    RecordClientPhysicalActivity();
                }
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static nint KeyboardHookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

            bool isInjected = (data.flags & LLKHF_INJECTED) != 0 || data.dwExtraInfo == RemoteInputMarker;

            if (!isInjected)
            {
                int msg = (int)wParam;
                if (msg is WM_KEYDOWN or WM_KEYUP or WM_SYSKEYDOWN or WM_SYSKEYUP)
                {
                    RecordClientPhysicalActivity();
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private static void RecordClientPhysicalActivity()
    {
        bool changedToActive = false;

        lock (SyncRoot)
        {
            // Se a opção "Bloquear entrada do usuário" estiver ativada, a prioridade não é concedida
            if (_isUserBlockedLocally)
                return;

            _lastPhysicalInputTick = Environment.TickCount64;

            if (!_isClientActive)
            {
                _isClientActive = true;
                changedToActive = true;
            }

            // Reinicia o timer de debounce para 1.000 ms
            _debounceTimer?.Change(PriorityTimeoutMs, Timeout.Infinite);
        }

        if (changedToActive)
        {
            AppDebugLog.Debug("AcessoRemoto", "Prioridade do cliente ativada: entradas do técnico bloqueadas temporariamente.");
            try
            {
                PriorityChanged?.Invoke(true);
            }
            catch { }
        }

        try
        {
            ClientPhysicalActivityOccurred?.Invoke(_lastPhysicalX, _lastPhysicalY);
        }
        catch { }
    }

    private static void OnDebounceTimerElapsed(object? state)
    {
        bool changedToInactive = false;

        lock (SyncRoot)
        {
            long elapsed = Environment.TickCount64 - _lastPhysicalInputTick;
            if (elapsed < PriorityTimeoutMs)
            {
                // Ainda dentro da janela de atividade, reagenda para o restante
                long remaining = PriorityTimeoutMs - elapsed;
                _debounceTimer?.Change((int)Math.Max(50, remaining), Timeout.Infinite);
                return;
            }

            if (_isClientActive)
            {
                _isClientActive = false;
                changedToInactive = true;
            }
        }

        if (changedToInactive)
        {
            AppDebugLog.Debug("AcessoRemoto", "Prioridade do cliente liberada: entradas do técnico reativadas.");
            try
            {
                PriorityChanged?.Invoke(false);
            }
            catch { }
        }
    }

    private delegate nint HookProc(int nCode, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, HookProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage([In] ref MSG lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint msg, nint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern nint GetModuleHandle(string? lpModuleName);
}
