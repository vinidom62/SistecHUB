using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SistecHub.Core;

namespace SistecHub.Modulos.AcessoRemoto;

internal static class AcessoRemotoInputHandler
{
    const uint INPUT_MOUSE = 0;
    const uint INPUT_KEYBOARD = 1;

    const uint MOUSEEVENTF_MOVE = 0x0001;
    const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    const uint MOUSEEVENTF_LEFTUP = 0x0004;
    const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    const uint MOUSEEVENTF_WHEEL = 0x0800;
    const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;
    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;

    const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    const uint KEYEVENTF_KEYUP = 0x0002;

    const ushort VK_CONTROL = 0x11;
    const ushort VK_MENU = 0x12;
    const ushort VK_DELETE = 0x2E;
    const ushort VK_LWIN = 0x5B;
    const ushort VK_D = 0x44;

    public static void HandleInputPacket(
        ReadOnlySpan<byte> packet,
        int activeX,
        int activeY,
        int activeW,
        int activeH)
    {
        if (packet.Length < 2 || packet[0] != AcessoRemotoProtocol.OpInput)
            return;

        if (AcessoRemotoClientPriorityMonitor.IsClientActive)
            return;

        if (AcessoRemotoSystemHelper.TrySendInputViaHelper(packet, activeX, activeY, activeW, activeH, out var helperCursorPacket))
        {
            if (helperCursorPacket != null && helperCursorPacket.Length >= 11)
            {
                AcessoRemotoAgentService.SendCursorPacket(helperCursorPacket);
            }
            else
            {
                AcessoRemotoAgentService.NotifyRemoteCursorPositionChanged(activeX, activeY, activeW, activeH);
            }
            return;
        }

        HandleInputPacketDirect(packet, activeX, activeY, activeW, activeH);
    }

    public static void HandleInputPacketDirect(
        ReadOnlySpan<byte> packet,
        int activeX,
        int activeY,
        int activeW,
        int activeH)
    {
        if (packet.Length < 2 || packet[0] != AcessoRemotoProtocol.OpInput)
            return;

        if (AcessoRemotoClientPriorityMonitor.IsClientActive)
            return;

        byte subType = packet[1];
        var payload = packet[2..];

        switch (subType)
        {
            case AcessoRemotoProtocol.InputMouseMove:
            {
                if (payload.Length < 8)
                    return;
                float normX = BinaryPrimitives.ReadSingleBigEndian(payload[0..4]);
                float normY = BinaryPrimitives.ReadSingleBigEndian(payload[4..8]);
                SetCursorNormalized(activeX, activeY, activeW, activeH, normX, normY);
                break;
            }

            case AcessoRemotoProtocol.InputMouseDown:
            {
                if (payload.Length < 1)
                    return;
                byte btn = payload[0];

                if (payload.Length >= 9)
                {
                    float normX = BinaryPrimitives.ReadSingleBigEndian(payload[1..5]);
                    float normY = BinaryPrimitives.ReadSingleBigEndian(payload[5..9]);
                    SetCursorNormalized(activeX, activeY, activeW, activeH, normX, normY);
                }

                switch (btn)
                {
                    case 1:
                        SendMouse(MOUSEEVENTF_LEFTDOWN, 0, 0, 0);
                        break;
                    case 2:
                        SendMouse(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0);
                        break;
                    case 3:
                        SendMouse(MOUSEEVENTF_MIDDLEDOWN, 0, 0, 0);
                        break;
                }
                break;
            }

            case AcessoRemotoProtocol.InputMouseUp:
            {
                if (payload.Length < 1)
                    return;
                byte btn = payload[0];

                if (payload.Length >= 9)
                {
                    float normX = BinaryPrimitives.ReadSingleBigEndian(payload[1..5]);
                    float normY = BinaryPrimitives.ReadSingleBigEndian(payload[5..9]);
                    SetCursorNormalized(activeX, activeY, activeW, activeH, normX, normY);
                }

                switch (btn)
                {
                    case 1:
                        SendMouse(MOUSEEVENTF_LEFTUP, 0, 0, 0);
                        break;
                    case 2:
                        SendMouse(MOUSEEVENTF_RIGHTUP, 0, 0, 0);
                        break;
                    case 3:
                        SendMouse(MOUSEEVENTF_MIDDLEUP, 0, 0, 0);
                        break;
                }
                break;
            }

            case AcessoRemotoProtocol.InputMouseWheel:
            {
                if (payload.Length < 4)
                    return;
                int delta = BinaryPrimitives.ReadInt32BigEndian(payload[0..4]);

                if (payload.Length >= 12)
                {
                    float normX = BinaryPrimitives.ReadSingleBigEndian(payload[4..8]);
                    float normY = BinaryPrimitives.ReadSingleBigEndian(payload[8..12]);
                    SetCursorNormalized(activeX, activeY, activeW, activeH, normX, normY);
                }

                SendMouse(MOUSEEVENTF_WHEEL, 0, 0, unchecked((uint)delta));
                break;
            }

            case AcessoRemotoProtocol.InputKeyDown:
            {
                if (payload.Length < 2)
                    return;
                ushort vk = BinaryPrimitives.ReadUInt16BigEndian(payload[0..2]);
                SendKeyboard(vk, 0);
                break;
            }

            case AcessoRemotoProtocol.InputKeyUp:
            {
                if (payload.Length < 2)
                    return;
                ushort vk = BinaryPrimitives.ReadUInt16BigEndian(payload[0..2]);
                SendKeyboard(vk, KEYEVENTF_KEYUP);
                break;
            }

            case AcessoRemotoProtocol.InputSpecialCad:
            {
                ExecuteQuickAction("cad");
                break;
            }
        }
    }

    static void SetCursorNormalized(int activeX, int activeY, int activeW, int activeH, float normX, float normY)
    {
        AcessoRemotoScreenCapturer.AttachToInteractiveDesktop();

        if (float.IsNaN(normX) || float.IsInfinity(normX))
            normX = 0f;
        if (float.IsNaN(normY) || float.IsInfinity(normY))
            normY = 0f;

        normX = Math.Clamp(normX, 0f, 1f);
        normY = Math.Clamp(normY, 0f, 1f);

        int targetX = (int)(activeX + (double)normX * activeW);
        int targetY = (int)(activeY + (double)normY * activeH);

        // Mapeia coordenadas absolutas normalizadas (0..65535) sobre o desktop virtual para o SendInput
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);
        if (vw <= 1) vw = Math.Max(1, GetSystemMetrics(SM_CXSCREEN));
        if (vh <= 1) vh = Math.Max(1, GetSystemMetrics(SM_CYSCREEN));

        int absX = (int)Math.Round((targetX - vx) * 65535.0 / Math.Max(1, vw - 1));
        int absY = (int)Math.Round((targetY - vy) * 65535.0 / Math.Max(1, vh - 1));
        absX = Math.Clamp(absX, 0, 65535);
        absY = Math.Clamp(absY, 0, 65535);

        // Notifica o monitor de prioridade ANTES para que os hooks de mouse reconheçam o movimento como remoto
        AcessoRemotoClientPriorityMonitor.NotifyRemoteCursorMoved(targetX, targetY);

        // Dispara movimento absoluto real para que o Windows despache WM_MOUSEMOVE e WM_SETCURSOR para a janela sob o cursor
        SendMouse(MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK, absX, absY, 0);
        SetCursorPos(targetX, targetY);

        AcessoRemotoAgentService.NotifyRemoteCursorPositionChanged(activeX, activeY, activeW, activeH);
    }

    static void SendMouse(uint flags, int dx, int dy, uint mouseData)
    {
        AcessoRemotoScreenCapturer.AttachToInteractiveDesktop();

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            u = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = dx,
                    dy = dy,
                    mouseData = mouseData,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = AcessoRemotoClientPriorityMonitor.RemoteInputMarker,
                },
            },
        };

        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    static bool IsExtendedKey(ushort vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true, // PRIOR, NEXT, END, HOME
        0x25 or 0x26 or 0x27 or 0x28 => true, // LEFT, UP, RIGHT, DOWN
        0x2C or 0x2D or 0x2E => true,         // SNAPSHOT, INSERT, DELETE
        0x5B or 0x5C or 0x5D => true,         // LWIN, RWIN, APPS
        0x6F or 0x90 => true,                 // DIVIDE, NUMLOCK
        0xA3 or 0xA5 => true,                 // RCONTROL, RMENU
        _ => false
    };

    static void SendKeyboard(ushort vk, uint flags)
    {
        AcessoRemotoScreenCapturer.AttachToInteractiveDesktop();

        if (IsExtendedKey(vk))
            flags |= KEYEVENTF_EXTENDEDKEY;

        ushort scanCode = (ushort)MapVirtualKey(vk, 0);

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            u = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = AcessoRemotoClientPriorityMonitor.RemoteInputMarker,
                },
            },
        };

        SendInput(1, ref input, Marshal.SizeOf<INPUT>());
    }

    public static (bool Success, string ErrorMessage) BlockUserInput(bool block)
    {
        if (AcessoRemotoSystemHelper.TryBlockInputViaHelper(block, out bool helperOk) && helperOk)
            return (true, "");

        return BlockUserInputDirect(block);
    }

    public static (bool Success, string ErrorMessage) BlockUserInputDirect(bool block)
    {
        AcessoRemotoScreenCapturer.AttachToInteractiveDesktop();
        try
        {
            if (!BlockInput(block))
            {
                int err = Marshal.GetLastWin32Error();
                var winEx = new Win32Exception(err);
                return (false, $"BlockInput falhou (requer privilégios de Administrador): {winEx.Message}");
            }
            return (true, "");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static void ExecuteQuickAction(string? action)
    {
        if (string.IsNullOrWhiteSpace(action))
            return;

        try
        {
            switch (action.Trim().ToLowerInvariant())
            {
                case "cad":
                    SendCtrlAltDel();
                    break;

                case "taskmgr":
                    Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
                    break;

                case "lock":
                    LockWorkStation();
                    break;

                case "desktop":
                    SendKeyboard(VK_LWIN, 0);
                    SendKeyboard(VK_D, 0);
                    SendKeyboard(VK_D, KEYEVENTF_KEYUP);
                    SendKeyboard(VK_LWIN, KEYEVENTF_KEYUP);
                    break;

                case "cmd":
                    Process.Start(new ProcessStartInfo("cmd.exe", "/c start cmd.exe") { UseShellExecute = true });
                    break;

                case "reboot":
                    Process.Start(new ProcessStartInfo(
                        "shutdown",
                        "/r /t 10 /c \"Reinicialização solicitada pelo técnico de suporte remoto\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    break;

                case "cancel_reboot":
                    Process.Start(new ProcessStartInfo("shutdown", "/a")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    });
                    break;
            }
        }
        catch
        {
            // Ignora falhas de execução de ação rápida.
        }
    }

    static void SendCtrlAltDel()
    {
        RemotoSasCoordinator.RequestSendSas();

        SendKeyboard(VK_CONTROL, 0);
        SendKeyboard(VK_MENU, 0);
        SendKeyboard(VK_DELETE, KEYEVENTF_EXTENDEDKEY);
        SendKeyboard(VK_DELETE, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP);
        SendKeyboard(VK_MENU, KEYEVENTF_KEYUP);
        SendKeyboard(VK_CONTROL, KEYEVENTF_KEYUP);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern uint SendInput(uint nInputs, ref INPUT pInputs, int cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool BlockInput([MarshalAs(UnmanagedType.Bool)] bool fBlockIt);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool LockWorkStation();

    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);
}

