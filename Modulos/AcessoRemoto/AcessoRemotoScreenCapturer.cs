using System.Buffers.Binary;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace SistecHub.Modulos.AcessoRemoto;

internal sealed class AcessoRemotoScreenCapturer : IDisposable
{
    const uint SRCCOPY = 0x00CC0020;
    const uint CAPTUREBLT = 0x40000000;
    const uint MAXIMUM_ALLOWED = 0x02000000;
    const int SM_CXSCREEN = 0;
    const int SM_CYSCREEN = 1;
    const int SM_XVIRTUALSCREEN = 76;
    const int SM_YVIRTUALSCREEN = 77;
    const int SM_CXVIRTUALSCREEN = 78;
    const int SM_CYVIRTUALSCREEN = 79;

    readonly object _lock = new();
    int _quality;
    int _selectedMonitor;
    List<AcessoRemotoMonitorInfo> _monitors = new();

    Bitmap? _captureBitmap;
    int _bitmapWidth;
    int _bitmapHeight;
    readonly MemoryStream _jpegStream = new(128 * 1024);
    readonly ImageCodecInfo? _jpegCodec;
    EncoderParameters? _encoderParams;

    public AcessoRemotoScreenCapturer(int displayIndex = 0, int quality = 70)
    {
        _quality = quality is > 0 and <= 100 ? quality : 70;
        _selectedMonitor = displayIndex;

        _jpegCodec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => string.Equals(c.MimeType, "image/jpeg", StringComparison.OrdinalIgnoreCase));
        UpdateEncoderParams();

        AttachToInteractiveDesktop();
        RefreshMonitors();
    }

    void UpdateEncoderParams()
    {
        _encoderParams?.Dispose();
        _encoderParams = new EncoderParameters(1);
        _encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, (long)_quality);
    }

    static IntPtr _processWindowStation;
    [ThreadStatic]
    static IntPtr _threadDesktop;

    public static void AttachToInteractiveDesktop()
    {
        try
        {
            if (_processWindowStation == IntPtr.Zero)
            {
                var hwinsta = OpenWindowStationW("winsta0", false, MAXIMUM_ALLOWED);
                if (hwinsta != IntPtr.Zero)
                {
                    if (SetProcessWindowStation(hwinsta))
                        _processWindowStation = hwinsta;
                }
            }

            var hdesk = OpenInputDesktop(0, false, MAXIMUM_ALLOWED);
            if (hdesk == IntPtr.Zero)
                hdesk = OpenDesktopW("Winlogon", 0, false, MAXIMUM_ALLOWED);
            if (hdesk == IntPtr.Zero)
                hdesk = OpenDesktopW("default", 0, false, MAXIMUM_ALLOWED);

            if (hdesk != IntPtr.Zero)
            {
                if (SetThreadDesktop(hdesk))
                {
                    if (_threadDesktop != IntPtr.Zero && _threadDesktop != hdesk)
                        CloseDesktop(_threadDesktop);
                    _threadDesktop = hdesk;
                }
                else
                {
                    CloseDesktop(hdesk);
                }
            }
        }
        catch
        {
            // Ignora falhas se já estiver na estação/desktop interativo padrão.
        }
    }

    public List<AcessoRemotoMonitorInfo> RefreshMonitors()
    {
        lock (_lock)
        {
            var found = new List<AcessoRemotoMonitorInfo>();

            bool Callback(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData)
            {
                var mi = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfoW(hMonitor, ref mi))
                {
                    int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    bool isPrimary = (mi.dwFlags & 1) != 0;
                    int idx = found.Count;
                    string name = $"Monitor {idx + 1} ({w}x{h})";
                    if (isPrimary)
                        name += " [Principal]";

                    found.Add(new AcessoRemotoMonitorInfo
                    {
                        Index = idx,
                        Name = name,
                        X = mi.rcMonitor.Left,
                        Y = mi.rcMonitor.Top,
                        Width = w,
                        Height = h,
                        Primary = isPrimary,
                    });
                }
                return true;
            }

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, Callback, IntPtr.Zero);
            }
            catch
            {
                // Fallback abaixo
            }

            if (found.Count == 0)
            {
                int w = GetSystemMetrics(SM_CXSCREEN);
                int h = GetSystemMetrics(SM_CYSCREEN);
                if (w <= 0) w = 1920;
                if (h <= 0) h = 1080;

                found.Add(new AcessoRemotoMonitorInfo
                {
                    Index = 0,
                    Name = $"Monitor 1 ({w}x{h}) [Principal]",
                    X = 0,
                    Y = 0,
                    Width = w,
                    Height = h,
                    Primary = true,
                });
            }

            _monitors = found;
            return new List<AcessoRemotoMonitorInfo>(_monitors);
        }
    }

    public List<AcessoRemotoMonitorInfo> GetMonitors()
    {
        lock (_lock)
        {
            return new List<AcessoRemotoMonitorInfo>(_monitors);
        }
    }

    public int SelectedMonitor
    {
        get
        {
            lock (_lock)
                return _selectedMonitor;
        }
    }

    public void SetSelectedMonitor(int index)
    {
        lock (_lock)
        {
            _selectedMonitor = index;
            DisposeBitmap();
        }
    }

    public void SetQuality(int quality)
    {
        lock (_lock)
        {
            if (quality is >= 10 and <= 100 && _quality != quality)
            {
                _quality = quality;
                UpdateEncoderParams();
            }
        }
    }

    public (int X, int Y, int Width, int Height) GetActiveBounds()
    {
        lock (_lock)
        {
            if (_selectedMonitor >= 0 && _selectedMonitor < _monitors.Count)
            {
                var m = _monitors[_selectedMonitor];
                return (m.X, m.Y, m.Width, m.Height);
            }

            return GetVirtualDesktopBounds();
        }
    }

    public static (int X, int Y, int Width, int Height) GetVirtualDesktopBounds()
    {
        int vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        int vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        int vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        int vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        if (vw <= 0 || vh <= 0)
        {
            int w = GetSystemMetrics(SM_CXSCREEN);
            int h = GetSystemMetrics(SM_CYSCREEN);
            return (0, 0, w, h);
        }

        return (vx, vy, vw, vh);
    }

    public byte[]? CaptureFrame(out int width, out int height)
    {
        AttachToInteractiveDesktop();

        var (srcX, srcY, w, h) = GetActiveBounds();
        width = w;
        height = h;

        if (w <= 0 || h <= 0)
            return null;

        lock (_lock)
        {
            if (_captureBitmap is null || _bitmapWidth != w || _bitmapHeight != h)
            {
                DisposeBitmap();
                _captureBitmap = new Bitmap(w, h, PixelFormat.Format24bppRgb);
                _bitmapWidth = w;
                _bitmapHeight = h;
            }

            var hdcScreen = GetDC(IntPtr.Zero);
            if (hdcScreen == IntPtr.Zero)
                return null;

            try
            {
                using var g = Graphics.FromImage(_captureBitmap);
                var hdcMem = g.GetHdc();
                try
                {
                    bool ok = BitBlt(hdcMem, 0, 0, w, h, hdcScreen, srcX, srcY, SRCCOPY | CAPTUREBLT);
                    if (!ok)
                    {
                        ok = BitBlt(hdcMem, 0, 0, w, h, hdcScreen, srcX, srcY, SRCCOPY);
                        if (!ok)
                            return null;
                    }
                }
                finally
                {
                    g.ReleaseHdc(hdcMem);
                }
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, hdcScreen);
            }

            _jpegStream.Position = 0;
            _jpegStream.SetLength(0);

            // Header de 9 bytes: [OpFrame (1 byte)][Width (4 bytes BE)][Height (4 bytes BE)]
            Span<byte> header = stackalloc byte[9];
            header[0] = AcessoRemotoProtocol.OpFrame;
            BinaryPrimitives.WriteUInt32BigEndian(header[1..5], (uint)w);
            BinaryPrimitives.WriteUInt32BigEndian(header[5..9], (uint)h);
            _jpegStream.Write(header);

            if (_jpegCodec is not null && _encoderParams is not null)
                _captureBitmap.Save(_jpegStream, _jpegCodec, _encoderParams);
            else
                _captureBitmap.Save(_jpegStream, ImageFormat.Jpeg);

            return _jpegStream.ToArray();
        }
    }

    void DisposeBitmap()
    {
        _captureBitmap?.Dispose();
        _captureBitmap = null;
        _bitmapWidth = 0;
        _bitmapHeight = 0;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            DisposeBitmap();
            _encoderParams?.Dispose();
            _encoderParams = null;
            _jpegStream.Dispose();
        }
    }

    delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenWindowStationW(string lpszWinSta, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetProcessWindowStation(IntPtr hWinSta);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr OpenDesktopW(string lpszDesktop, uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr OpenInputDesktop(uint dwFlags, bool fInherit, uint dwDesiredAccess);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetThreadDesktop(IntPtr hDesktop);

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool CloseDesktop(IntPtr hDesktop);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    static extern bool BitBlt(
        IntPtr hdcDest,
        int nXDest,
        int nYDest,
        int nWidth,
        int nHeight,
        IntPtr hdcSrc,
        int nXSrc,
        int nYSrc,
        uint dwRop);

    [DllImport("user32.dll")]
    static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    static extern bool EnumDisplayMonitors(
        IntPtr hdc,
        IntPtr lprcClip,
        MonitorEnumProc lpfnEnum,
        IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    static extern bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);
}

