using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SistecHub.Modulos.AcessoRemoto;

internal sealed class RemoteCursorState
{
    public bool Visible { get; set; } = true;
    public RemoteCursorType Type { get; set; } = RemoteCursorType.Arrow;
    public string TypeName { get; set; } = "arrow";
    public float NormX { get; set; }
    public float NormY { get; set; }
    public int ScreenX { get; set; }
    public int ScreenY { get; set; }
    public bool ClientActive { get; set; }
}

internal static class AcessoRemotoCursorHelper
{
    private const int CURSOR_SHOWING = 0x00000001;

    private static readonly Dictionary<IntPtr, (RemoteCursorType Type, string Name)> _systemCursors = new();
    private static readonly object _syncLock = new();

    static AcessoRemotoCursorHelper()
    {
        InitSystemCursors();
    }

    private static void InitSystemCursors()
    {
        (int id, RemoteCursorType type, string name)[] standard =
        {
            (32512, RemoteCursorType.Arrow, "arrow"),
            (32513, RemoteCursorType.IBeam, "ibeam"),
            (32514, RemoteCursorType.Wait, "wait"),
            (32515, RemoteCursorType.Cross, "cross"),
            (32516, RemoteCursorType.UpArrow, "uparrow"),
            (32642, RemoteCursorType.SizeNWSE, "sizenwse"),
            (32643, RemoteCursorType.SizeNESW, "sizenesw"),
            (32644, RemoteCursorType.SizeWE, "sizewe"),
            (32645, RemoteCursorType.SizeNS, "sizens"),
            (32646, RemoteCursorType.SizeAll, "sizeall"),
            (32648, RemoteCursorType.No, "no"),
            (32649, RemoteCursorType.Hand, "hand"),
            (32650, RemoteCursorType.AppStarting, "appstarting"),
            (32651, RemoteCursorType.Help, "help")
        };

        foreach (var item in standard)
        {
            try
            {
                var h = LoadCursor(IntPtr.Zero, item.id);
                if (h != IntPtr.Zero)
                {
                    _systemCursors[h] = (item.type, item.name);
                }
            }
            catch
            {
                // Ignora falhas em cursores não suportados
            }
        }
    }

    public static (RemoteCursorType Type, string Name) ResolveCursor(IntPtr hCursor)
    {
        if (hCursor == IntPtr.Zero)
            return (RemoteCursorType.Arrow, "arrow");

        lock (_syncLock)
        {
            if (_systemCursors.TryGetValue(hCursor, out var found))
                return found;
        }

        // Se o handle não for conhecido diretamente (ex: tema customizado ou cursor criado por navegador),
        // inspecionamos os metadados do ícone/cursor via GetIconInfo
        var detected = InspectIconHotspot(hCursor);
        lock (_syncLock)
        {
            _systemCursors[hCursor] = detected;
        }
        return detected;
    }

    private static (RemoteCursorType Type, string Name) InspectIconHotspot(IntPtr hCursor)
    {
        var ii = new ICONINFO();
        if (!GetIconInfo(hCursor, out ii))
            return (RemoteCursorType.Arrow, "arrow");

        try
        {
            int hx = ii.xHotspot;
            int hy = ii.yHotspot;

            int curW = 32;
            int curH = 32;
            if (ii.hbmMask != IntPtr.Zero && GetObject(ii.hbmMask, Marshal.SizeOf<BITMAP>(), out var bm) > 0)
            {
                curW = Math.Max(1, bm.bmWidth);
                curH = Math.Max(1, ii.hbmColor == IntPtr.Zero ? bm.bmHeight / 2 : bm.bmHeight);
            }

            float normHx = (float)hx / curW;
            float normHy = (float)hy / curH;

            // Seta padrão clássica: hotspot no canto superior esquerdo (0,0 ou até 2px)
            if (hx <= 2 && hy <= 2)
                return (RemoteCursorType.Arrow, "arrow");

            // Mãozinha / Link: ponta do indicador no topo (normHy <= 0.16 ou hy <= 4) e deslocado horizontalmente
            if ((normHy <= 0.16f || hy <= 4) && ((normHx >= 0.10f && normHx <= 0.50f) || (hx >= 4 && hx <= 20)))
                return (RemoteCursorType.Hand, "hand");

            // IBeam / Texto: barra vertical com hotspot no centro superior/médio (normHx ~ 0.18..0.65, normHy ~ 0.18..0.65)
            if (normHx >= 0.18f && normHx <= 0.65f && normHy >= 0.18f && normHy <= 0.65f)
                return (RemoteCursorType.IBeam, "ibeam");

            // Redimensionamento horizontal (SizeWE)
            if (normHx >= 0.25f && normHx <= 0.75f && normHy >= 0.08f && normHy <= 0.25f)
                return (RemoteCursorType.SizeWE, "sizewe");

            // Redimensionamento vertical (SizeNS)
            if (normHx >= 0.08f && normHx <= 0.25f && normHy >= 0.25f && normHy <= 0.75f)
                return (RemoteCursorType.SizeNS, "sizens");

            // Cursores de redimensionamento diagonal ou movimento geral
            if (hx > 6 && hy > 6)
                return (RemoteCursorType.SizeAll, "sizeall");

            return (RemoteCursorType.Arrow, "arrow");
        }
        finally
        {
            if (ii.hbmMask != IntPtr.Zero)
                DeleteObject(ii.hbmMask);
            if (ii.hbmColor != IntPtr.Zero)
                DeleteObject(ii.hbmColor);
        }
    }

    public static bool TryGetCurrentState(
        int activeX,
        int activeY,
        int activeW,
        int activeH,
        bool isClientActive,
        out RemoteCursorState state)
    {
        AcessoRemotoScreenCapturer.AttachToInteractiveDesktop();

        if (activeW <= 0 || activeH <= 0)
        {
            (activeX, activeY, activeW, activeH) = AcessoRemotoScreenCapturer.GetVirtualDesktopBounds();
        }

        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(out ci))
        {
            state = new RemoteCursorState
            {
                Visible = false,
                ClientActive = isClientActive,
                Type = RemoteCursorType.Arrow,
                TypeName = "arrow"
            };
            return false;
        }

        bool isVisible = (ci.flags & CURSOR_SHOWING) != 0 && ci.hCursor != IntPtr.Zero;
        int sx = ci.ptScreenPos.x;
        int sy = ci.ptScreenPos.y;

        // Se o cursor estiver fora dos limites do monitor ativo, consideramos não visível neste monitor
        bool withinMonitor = sx >= activeX && sx <= activeX + activeW && sy >= activeY && sy <= activeY + activeH;
        if (!withinMonitor)
            isVisible = false;

        float normX = activeW > 0 ? (float)(sx - activeX) / activeW : 0f;
        float normY = activeH > 0 ? (float)(sy - activeY) / activeH : 0f;
        normX = Math.Clamp(normX, 0f, 1f);
        normY = Math.Clamp(normY, 0f, 1f);

        var (cType, cName) = ResolveCursor(ci.hCursor);

        state = new RemoteCursorState
        {
            Visible = isVisible,
            Type = cType,
            TypeName = cName,
            NormX = normX,
            NormY = normY,
            ScreenX = sx,
            ScreenY = sy,
            ClientActive = isClientActive
        };

        return true;
    }

    public static bool HasMeaningfulChange(RemoteCursorState? prev, RemoteCursorState current)
    {
        if (prev is null) return true;
        if (prev.Visible != current.Visible) return true;
        if (prev.Type != current.Type) return true;
        if (prev.ClientActive != current.ClientActive) return true;

        if (current.Visible)
        {
            float dx = Math.Abs(prev.NormX - current.NormX);
            float dy = Math.Abs(prev.NormY - current.NormY);
            if (dx > 0.0006f || dy > 0.0006f) // ~1 pixel em monitores Full HD
                return true;
        }

        return false;
    }

    public static byte[] CreatePacket(RemoteCursorState state)
    {
        var packet = new byte[11];
        packet[0] = AcessoRemotoProtocol.OpCursor;
        packet[1] = (byte)((state.Visible ? 1 : 0) | (state.ClientActive ? 2 : 0));
        packet[2] = (byte)state.Type;
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(3, 4), state.NormX);
        BinaryPrimitives.WriteSingleBigEndian(packet.AsSpan(7, 4), state.NormY);
        return packet;
    }

    public static bool TryParsePacket(ReadOnlySpan<byte> packet, out RemoteCursorState state)
    {
        if (packet.Length < 11 || packet[0] != AcessoRemotoProtocol.OpCursor)
        {
            state = new RemoteCursorState();
            return false;
        }

        byte flags = packet[1];
        bool visible = (flags & 1) != 0;
        bool clientActive = (flags & 2) != 0;

        byte typeByte = packet[2];
        var type = Enum.IsDefined(typeof(RemoteCursorType), typeByte)
            ? (RemoteCursorType)typeByte
            : RemoteCursorType.Arrow;

        float normX = BinaryPrimitives.ReadSingleBigEndian(packet.Slice(3, 4));
        float normY = BinaryPrimitives.ReadSingleBigEndian(packet.Slice(7, 4));

        normX = Math.Clamp(normX, 0f, 1f);
        normY = Math.Clamp(normY, 0f, 1f);

        state = new RemoteCursorState
        {
            Visible = visible,
            ClientActive = clientActive,
            Type = type,
            TypeName = type.ToString().ToLowerInvariant(),
            NormX = normX,
            NormY = normY
        };

        return true;
    }

    public static AcessoRemotoMessage CreateMessage(RemoteCursorState state)
    {
        return new AcessoRemotoMessage
        {
            Type = AcessoRemotoProtocol.TypeCursorUpdate,
            CursorType = state.TypeName,
            CursorX = state.NormX,
            CursorY = state.NormY,
            CursorVisible = state.Visible,
            ClientActive = state.ClientActive
        };
    }

    public static bool TryParseMessage(JsonElement root, out RemoteCursorState state)
    {
        state = new RemoteCursorState();

        if (!root.TryGetProperty("type", out var tProp) || tProp.GetString() != AcessoRemotoProtocol.TypeCursorUpdate)
            return false;

        string cTypeStr = root.TryGetProperty("cursor_type", out var ctProp) ? ctProp.GetString() ?? "arrow" : "arrow";
        float x = root.TryGetProperty("cursor_x", out var cxProp) ? cxProp.GetSingle() : 0f;
        float y = root.TryGetProperty("cursor_y", out var cyProp) ? cyProp.GetSingle() : 0f;
        bool visible = !root.TryGetProperty("cursor_visible", out var cvProp) || cvProp.GetBoolean();
        bool clientActive = root.TryGetProperty("client_active", out var caProp) && caProp.GetBoolean();

        RemoteCursorType type = cTypeStr.ToLowerInvariant() switch
        {
            "hand" => RemoteCursorType.Hand,
            "ibeam" => RemoteCursorType.IBeam,
            "wait" => RemoteCursorType.Wait,
            "cross" => RemoteCursorType.Cross,
            "sizewe" => RemoteCursorType.SizeWE,
            "sizens" => RemoteCursorType.SizeNS,
            "sizenwse" => RemoteCursorType.SizeNWSE,
            "sizenesw" => RemoteCursorType.SizeNESW,
            "sizeall" => RemoteCursorType.SizeAll,
            "no" => RemoteCursorType.No,
            "appstarting" => RemoteCursorType.AppStarting,
            "help" => RemoteCursorType.Help,
            "uparrow" => RemoteCursorType.UpArrow,
            _ => RemoteCursorType.Arrow
        };

        state = new RemoteCursorState
        {
            Visible = visible,
            ClientActive = clientActive,
            Type = type,
            TypeName = cTypeStr,
            NormX = Math.Clamp(x, 0f, 1f),
            NormY = Math.Clamp(y, 0f, 1f)
        };

        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hCursor;
        public POINT ptScreenPos;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO
    {
        public bool fIcon;
        public int xHotspot;
        public int yHotspot;
        public IntPtr hbmMask;
        public IntPtr hbmColor;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetCursorInfo(out CURSORINFO pci);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO piconinfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAP
    {
        public int bmType;
        public int bmWidth;
        public int bmHeight;
        public int bmWidthBytes;
        public ushort bmPlanes;
        public ushort bmBitsPixel;
        public IntPtr bmBits;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int GetObject(IntPtr hgdiobj, int cbBuffer, out BITMAP lpvObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

