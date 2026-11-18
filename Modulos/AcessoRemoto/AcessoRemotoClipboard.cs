using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using SistecHub.Core;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace SistecHub.Modulos.AcessoRemoto;

internal static class AcessoRemotoClipboard
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public const string MarkerFormatName = "SistecHub_RemoteClipboard_InternalMarker";
    public const string CfstrFileDescriptorW = "FileGroupDescriptorW";
    public const string CfstrFileContents = "FileContents";
    public const string CfstrPreferredDropEffect = "Preferred DropEffect";

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);

    [DllImport("ole32.dll")]
    public static extern int OleSetClipboard(ComIDataObject? pDataObj);

    [DllImport("ole32.dll")]
    public static extern int OleFlushClipboard();

    [DllImport("ole32.dll")]
    public static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GlobalFree(IntPtr hMem);

    public const uint GMEM_MOVEABLE = 0x0002;
    public const uint GMEM_ZEROINIT = 0x0040;
    public const uint GHND = GMEM_MOVEABLE | GMEM_ZEROINIT;

    private static readonly object StaLock = new();
    private static Thread? _staThread;
    private static Control? _staInvoker;
    private static readonly ManualResetEventSlim StaReady = new(false);
    private static VirtualFileDataObject? _currentVirtualDataObject;

    private static string _lastAppliedText = "";
    private static long _ignoreClipboardUntilTicks;

    private static void EnsurePersistentStaThread()
    {
        lock (StaLock)
        {
            if (_staThread is { IsAlive: true } && _staInvoker is { IsHandleCreated: true })
                return;

            StaReady.Reset();
            _staThread = new Thread(() =>
            {
                try
                {
                    _staInvoker = new Control();
                    _ = _staInvoker.Handle;
                    StaReady.Set();
                    Application.Run();
                }
                catch (Exception ex)
                {
                    AppDebugLog.Debug("Clipboard", $"Loop STA encerrado: {ex.Message}");
                    StaReady.Set();
                }
            })
            {
                IsBackground = true,
                Name = "SistecHubClipboardOleStaThread"
            };
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.Start();
        }

        StaReady.Wait(3000);
    }

    public static void DispatchToPersistentStaThread(Action action)
    {
        EnsurePersistentStaThread();

        if (Thread.CurrentThread == _staThread)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppDebugLog.Debug("Clipboard", $"Erro na operação STA direta: {ex.Message}");
            }
            return;
        }

        var invoker = _staInvoker;
        if (invoker is { IsHandleCreated: true })
        {
            try
            {
                invoker.BeginInvoke(action);
            }
            catch (Exception ex)
            {
                AppDebugLog.Debug("Clipboard", $"Erro ao despachar para thread STA: {ex.Message}");
            }
        }
    }

    public static void RunOnStaThread(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppDebugLog.Debug("Clipboard", $"Erro na operação STA direta: {ex.Message}");
            }
            return;
        }

        EnsurePersistentStaThread();
        var invoker = _staInvoker;
        if (invoker is { IsHandleCreated: true })
        {
            try
            {
                invoker.Invoke(action);
            }
            catch (Exception ex)
            {
                AppDebugLog.Debug("Clipboard", $"Erro na thread STA: {ex.Message}");
            }
        }
    }

    public static bool ShouldIgnoreClipboardChange()
    {
        return Environment.TickCount64 < Interlocked.Read(ref _ignoreClipboardUntilTicks);
    }

    public static void SuppressClipboardNotifications(int milliseconds = 900)
    {
        Interlocked.Exchange(ref _ignoreClipboardUntilTicks, Environment.TickCount64 + milliseconds);
    }

    public static bool IsAutomatedFileTransferChatMessage(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();
        return t.Contains("Arquivo baixado", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Arquivo recebido", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Arquivo enviado", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Arquivo copiado", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Arquivo colado", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Transferência de arquivo", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Download concluído:", StringComparison.OrdinalIgnoreCase)
            || t.Contains("Upload concluído:", StringComparison.OrdinalIgnoreCase);
    }

    public static List<AcessoRemotoClipboardFileItem> BuildClipboardFilesAdvertisement(IEnumerable<string> paths)
    {
        var result = new List<AcessoRemotoClipboardFileItem>();
        int idx = 0;

        foreach (var rawPath in paths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                continue;

            try
            {
                var fullPath = Path.GetFullPath(rawPath);
                if (File.Exists(fullPath))
                {
                    var fi = new FileInfo(fullPath);
                    result.Add(new AcessoRemotoClipboardFileItem
                    {
                        Index = idx++,
                        Name = fi.Name,
                        RelativePath = fi.Name,
                        FullPath = fi.FullName,
                        Size = fi.Length,
                        IsDir = false,
                        LastWriteUtcTicks = fi.LastWriteTimeUtc.Ticks
                    });
                }
                else if (Directory.Exists(fullPath))
                {
                    var rootDir = new DirectoryInfo(fullPath);
                    string baseParent = rootDir.Parent?.FullName ?? rootDir.FullName;

                    result.Add(new AcessoRemotoClipboardFileItem
                    {
                        Index = idx++,
                        Name = rootDir.Name,
                        RelativePath = rootDir.Name,
                        FullPath = rootDir.FullName,
                        Size = 0,
                        IsDir = true,
                        LastWriteUtcTicks = rootDir.LastWriteTimeUtc.Ticks
                    });

                    foreach (var entry in rootDir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories))
                    {
                        if (result.Count >= 500)
                            break;

                        try
                        {
                            string rel = Path.GetRelativePath(baseParent, entry.FullName);
                            bool isSubDir = (entry.Attributes & FileAttributes.Directory) != 0;
                            long size = (!isSubDir && entry is FileInfo sfi) ? sfi.Length : 0;

                            result.Add(new AcessoRemotoClipboardFileItem
                            {
                                Index = idx++,
                                Name = entry.Name,
                                RelativePath = rel,
                                FullPath = entry.FullName,
                                Size = size,
                                IsDir = isSubDir,
                                LastWriteUtcTicks = entry.LastWriteTimeUtc.Ticks
                            });
                        }
                        catch
                        {
                            // Ignora sub-entradas sem permissão
                        }
                    }
                }
            }
            catch
            {
                // Ignora caminhos inválidos
            }
        }

        return result;
    }

    public static (string? Text, List<string>? Files) ReadLocalClipboard()
    {
        if (ShouldIgnoreClipboardChange())
            return (null, null);

        string? foundText = null;
        List<string>? foundFiles = null;

        RunOnStaThread(() =>
        {
            try
            {
                var dataObj = System.Windows.Forms.Clipboard.GetDataObject();
                if (dataObj == null)
                    return;

                if (dataObj.GetDataPresent(MarkerFormatName))
                    return;

                if (System.Windows.Forms.Clipboard.ContainsFileDropList())
                {
                    var dropList = System.Windows.Forms.Clipboard.GetFileDropList();
                    if (dropList != null && dropList.Count > 0)
                    {
                        var list = new List<string>(dropList.Count);
                        foreach (var item in dropList)
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                                list.Add(item);
                        }
                        if (list.Count > 0)
                        {
                            foundFiles = list;
                            return;
                        }
                    }
                }

                if (System.Windows.Forms.Clipboard.ContainsText())
                {
                    var txt = System.Windows.Forms.Clipboard.GetText();
                    if (!string.IsNullOrEmpty(txt) && !string.Equals(txt, _lastAppliedText, StringComparison.Ordinal))
                    {
                        _lastAppliedText = txt;
                        foundText = txt;
                    }
                }
            }
            catch
            {
                // Clipboard pode estar temporariamente bloqueado por outro processo
            }
        });

        return (foundText, foundFiles);
    }

    public static void ApplyRemoteTextToLocalClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;

        _lastAppliedText = text;
        SuppressClipboardNotifications(900);

        DispatchToPersistentStaThread(() =>
        {
            try
            {
                var dataObject = new System.Windows.Forms.DataObject();
                dataObject.SetData(MarkerFormatName, true);
                dataObject.SetText(text);
                System.Windows.Forms.Clipboard.SetDataObject(dataObject, copy: true);
            }
            catch (Exception ex)
            {
                AppDebugLog.Debug("Clipboard", $"Falha ao definir texto no clipboard: {ex.Message}");
            }
        });
    }

    public static string SanitizeRelativePath(string? relativePath, string? fallbackName)
    {
        var raw = string.IsNullOrWhiteSpace(relativePath) ? fallbackName : relativePath;
        if (string.IsNullOrWhiteSpace(raw))
            return "arquivo";

        var invalidChars = Path.GetInvalidFileNameChars();
        var segments = raw.Replace('/', '\\')
            .Split('\\', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var safeSegments = new List<string>(segments.Length);
        foreach (var seg in segments)
        {
            if (seg == "." || seg == "..")
                continue;

            // Remove caracteres inválidos ou separadores de volume (ex.: C:) e pontos finais proibidos no Windows
            var cleaned = new string(seg.Where(ch => !invalidChars.Contains(ch) && ch != ':').ToArray()).Trim().TrimEnd('.', ' ');
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned == "." || cleaned == "..")
                continue;

            safeSegments.Add(cleaned);
        }

        if (safeSegments.Count == 0)
        {
            var fallback = Path.GetFileName(fallbackName ?? "");
            var cleanedFallback = new string(fallback.Where(ch => !invalidChars.Contains(ch) && ch != ':').ToArray()).Trim().TrimEnd('.', ' ');
            if (!string.IsNullOrWhiteSpace(cleanedFallback) && cleanedFallback != "." && cleanedFallback != "..")
                safeSegments.Add(cleanedFallback);
            else
                safeSegments.Add("arquivo");
        }

        var result = string.Join("\\", safeSegments);
        if (result.Length > 259)
            result = result[..259].TrimEnd('\\', '/', '.', ' ');

        return string.IsNullOrWhiteSpace(result) ? "arquivo" : result;
    }

    public static void ApplyRemoteFilesToLocalClipboard(
        IReadOnlyList<AcessoRemotoClipboardFileItem> items,
        Func<int, Stream?> streamFactory)
    {
        if (items == null || items.Count == 0)
            return;

        SuppressClipboardNotifications(1200);

        DispatchToPersistentStaThread(() =>
        {
            try
            {
                _currentVirtualDataObject = new VirtualFileDataObject(items, streamFactory);
                OleSetClipboard(_currentVirtualDataObject);
            }
            catch (Exception ex)
            {
                AppDebugLog.Debug("Clipboard", $"Falha ao registrar VirtualFileDataObject no clipboard: {ex.Message}");
            }
        });
    }
}

internal sealed class ClipboardNotificationWindow : NativeWindow, IDisposable
{
    private readonly Action _onClipboardChanged;
    private bool _listening;

    public ClipboardNotificationWindow(Action onClipboardChanged)
    {
        _onClipboardChanged = onClipboardChanged;
        CreateHandle(new CreateParams
        {
            Caption = "SistecHubClipboardListener",
            Parent = new IntPtr(-3) // HWND_MESSAGE
        });

        if (Handle != IntPtr.Zero)
        {
            _listening = AcessoRemotoClipboard.AddClipboardFormatListener(Handle);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == AcessoRemotoClipboard.WM_CLIPBOARDUPDATE)
        {
            if (!AcessoRemotoClipboard.ShouldIgnoreClipboardChange())
            {
                try
                {
                    _onClipboardChanged();
                }
                catch
                {
                    // Ignora falhas de callback
                }
            }
            m.Result = IntPtr.Zero;
            return;
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero)
        {
            if (_listening)
            {
                try { AcessoRemotoClipboard.RemoveClipboardFormatListener(Handle); } catch { }
                _listening = false;
            }
            try { DestroyHandle(); } catch { }
        }
    }
}

/// <summary>
/// Implementa COM IDataObject para colagem direta (Ctrl+V) no Windows Explorer
/// utilizando FileGroupDescriptorW e FileContents (IStream sob demanda).
/// </summary>
[ComVisible(true)]
internal sealed class VirtualFileDataObject : ComIDataObject
{
    private const int DV_E_FORMATETC = unchecked((int)0x80040064);
    private const int DV_E_TYMED = unchecked((int)0x80040069);
    private const int DV_E_DVASPECT = unchecked((int)0x8004006B);
    private const int DV_E_LINDEX = unchecked((int)0x80040068);
    private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
    private const int DATA_S_SAMEFORMATETC = 0x00040130;
    private const int S_OK = 0;
    private const int S_FALSE = 1;

    private const uint FD_ATTRIBUTES = 0x00000004;
    private const uint FD_WRITESTIME = 0x00000020;
    private const uint FD_FILESIZE = 0x00000040;
    private const uint FD_PROGRESSUI = 0x00004000;
    private const uint FD_UNICODE = 0x80000000;

    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    private readonly IReadOnlyList<AcessoRemotoClipboardFileItem> _items;
    private readonly Func<int, Stream?> _streamFactory;

    private readonly short _fmtFileGroupDescriptorW;
    private readonly short _fmtFileContents;
    private readonly short _fmtPreferredDropEffect;
    private readonly short _fmtMarker;

    public VirtualFileDataObject(
        IReadOnlyList<AcessoRemotoClipboardFileItem> items,
        Func<int, Stream?> streamFactory)
    {
        _items = items;
        _streamFactory = streamFactory;

        _fmtFileGroupDescriptorW = unchecked((short)AcessoRemotoClipboard.RegisterClipboardFormat(AcessoRemotoClipboard.CfstrFileDescriptorW));
        _fmtFileContents = unchecked((short)AcessoRemotoClipboard.RegisterClipboardFormat(AcessoRemotoClipboard.CfstrFileContents));
        _fmtPreferredDropEffect = unchecked((short)AcessoRemotoClipboard.RegisterClipboardFormat(AcessoRemotoClipboard.CfstrPreferredDropEffect));
        _fmtMarker = unchecked((short)AcessoRemotoClipboard.RegisterClipboardFormat(AcessoRemotoClipboard.MarkerFormatName));
    }

    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        medium = default;

        if (format.cfFormat == _fmtFileGroupDescriptorW)
        {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0)
                Marshal.ThrowExceptionForHR(DV_E_TYMED);

            byte[] descriptorBytes = BuildFileGroupDescriptorBytes();
            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = AllocHGlobalFromBytes(descriptorBytes);
            medium.pUnkForRelease = null;
            return;
        }

        if (format.cfFormat == _fmtPreferredDropEffect)
        {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0)
                Marshal.ThrowExceptionForHR(DV_E_TYMED);

            // DROPEFFECT_COPY = 1
            byte[] effectBytes = BitConverter.GetBytes(1u);
            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = AllocHGlobalFromBytes(effectBytes);
            medium.pUnkForRelease = null;
            return;
        }

        if (format.cfFormat == _fmtMarker)
        {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0)
                Marshal.ThrowExceptionForHR(DV_E_TYMED);

            medium.tymed = TYMED.TYMED_HGLOBAL;
            medium.unionmember = AllocHGlobalFromBytes(new byte[] { 1 });
            medium.pUnkForRelease = null;
            return;
        }

        if (format.cfFormat == _fmtFileContents)
        {
            int index = format.lindex;
            if (index < 0 || index >= _items.Count)
                Marshal.ThrowExceptionForHR(DV_E_LINDEX);

            var item = _items[index];
            if (item.IsDir)
            {
                Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
            }

            if ((format.tymed & TYMED.TYMED_ISTREAM) != 0)
            {
                var stream = _streamFactory(index) ?? Stream.Null;
                var comStream = new ManagedComStream(stream);
                IntPtr iStreamPtr = Marshal.GetComInterfaceForObject(comStream, typeof(IStream));
                medium.tymed = TYMED.TYMED_ISTREAM;
                medium.unionmember = iStreamPtr;
                medium.pUnkForRelease = null;
                return;
            }

            if ((format.tymed & TYMED.TYMED_HGLOBAL) != 0)
            {
                using var stream = _streamFactory(index);
                using var ms = new MemoryStream();
                stream?.CopyTo(ms);
                medium.tymed = TYMED.TYMED_HGLOBAL;
                medium.unionmember = AllocHGlobalFromBytes(ms.ToArray());
                medium.pUnkForRelease = null;
                return;
            }

            Marshal.ThrowExceptionForHR(DV_E_TYMED);
        }

        Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
    {
        Marshal.ThrowExceptionForHR(DV_E_FORMATETC);
    }

    public int QueryGetData(ref FORMATETC format)
    {
        if ((format.dwAspect & DVASPECT.DVASPECT_CONTENT) == 0)
            return DV_E_DVASPECT;

        if (format.cfFormat == _fmtFileGroupDescriptorW ||
            format.cfFormat == _fmtPreferredDropEffect ||
            format.cfFormat == _fmtMarker)
        {
            return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? S_OK : DV_E_TYMED;
        }

        if (format.cfFormat == _fmtFileContents)
        {
            if (format.lindex >= 0 && format.lindex < _items.Count && _items[format.lindex].IsDir)
                return DV_E_FORMATETC;
            return (format.tymed & (TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL)) != 0 ? S_OK : DV_E_TYMED;
        }

        return DV_E_FORMATETC;
    }

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return DATA_S_SAMEFORMATETC;
    }

    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        if (release)
        {
            try
            {
                AcessoRemotoClipboard.ReleaseStgMedium(ref medium);
            }
            catch
            {
                if (medium.tymed == TYMED.TYMED_HGLOBAL && medium.unionmember != IntPtr.Zero)
                    AcessoRemotoClipboard.GlobalFree(medium.unionmember);
            }
        }
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET)
            throw new NotImplementedException();

        var formats = new[]
        {
            new FORMATETC
            {
                cfFormat = _fmtFileGroupDescriptorW,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            },
            new FORMATETC
            {
                cfFormat = _fmtFileContents,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_ISTREAM | TYMED.TYMED_HGLOBAL
            },
            new FORMATETC
            {
                cfFormat = _fmtPreferredDropEffect,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            },
            new FORMATETC
            {
                cfFormat = _fmtMarker,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL
            }
        };

        return new ManagedEnumFormatEtc(formats);
    }

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
    {
        connection = 0;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    public void DUnadvise(int connection)
    {
        Marshal.ThrowExceptionForHR(OLE_E_ADVISENOTSUPPORTED);
    }

    public int EnumDAdvise(out IEnumSTATDATA? enumAdvise)
    {
        enumAdvise = null;
        return OLE_E_ADVISENOTSUPPORTED;
    }

    private byte[] BuildFileGroupDescriptorBytes()
    {
        // FILEGROUPDESCRIPTORW: uint cItems (4 bytes) + FILEDESCRIPTORW[cItems] (592 bytes cada)
        const int descriptorSize = 592;
        int count = _items.Count;
        byte[] buffer = new byte[4 + count * descriptorSize];

        BitConverter.GetBytes((uint)count).CopyTo(buffer, 0);

        for (int i = 0; i < count; i++)
        {
            var item = _items[i];
            int baseOffset = 4 + i * descriptorSize;

            uint flags = FD_ATTRIBUTES | FD_WRITESTIME | FD_PROGRESSUI | FD_UNICODE;
            if (!item.IsDir)
                flags |= FD_FILESIZE;

            BitConverter.GetBytes(flags).CopyTo(buffer, baseOffset + 0);

            uint attrs = item.IsDir ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL;
            BitConverter.GetBytes(attrs).CopyTo(buffer, baseOffset + 36);

            long fileTime = 0;
            try
            {
                var dt = item.LastWriteUtcTicks > 0
                    ? new DateTime(item.LastWriteUtcTicks, DateTimeKind.Utc)
                    : DateTime.UtcNow;
                fileTime = dt.ToFileTimeUtc();
            }
            catch
            {
                fileTime = DateTime.UtcNow.ToFileTimeUtc();
            }

            uint ftLow = (uint)(fileTime & 0xFFFFFFFF);
            uint ftHigh = (uint)((ulong)fileTime >> 32);
            // ftLastWriteTime fica no offset 56 (low) e 60 (high)
            BitConverter.GetBytes(ftLow).CopyTo(buffer, baseOffset + 56);
            BitConverter.GetBytes(ftHigh).CopyTo(buffer, baseOffset + 60);

            ulong size = item.IsDir ? 0UL : (ulong)Math.Max(0L, item.Size);
            uint sizeHigh = (uint)(size >> 32);
            uint sizeLow = (uint)(size & 0xFFFFFFFF);
            BitConverter.GetBytes(sizeHigh).CopyTo(buffer, baseOffset + 64);
            BitConverter.GetBytes(sizeLow).CopyTo(buffer, baseOffset + 68);

            string relPath = AcessoRemotoClipboard.SanitizeRelativePath(item.RelativePath, item.Name);

            byte[] nameBytes = Encoding.Unicode.GetBytes(relPath);
            Array.Copy(nameBytes, 0, buffer, baseOffset + 72, Math.Min(nameBytes.Length, 518));
        }

        return buffer;
    }

    private static IntPtr AllocHGlobalFromBytes(byte[] data)
    {
        IntPtr hGlobal = AcessoRemotoClipboard.GlobalAlloc(AcessoRemotoClipboard.GHND, (UIntPtr)Math.Max(1, data.Length));
        if (hGlobal == IntPtr.Zero)
            throw new OutOfMemoryException();

        IntPtr ptr = AcessoRemotoClipboard.GlobalLock(hGlobal);
        if (ptr == IntPtr.Zero)
        {
            AcessoRemotoClipboard.GlobalFree(hGlobal);
            throw new OutOfMemoryException();
        }

        try
        {
            if (data.Length > 0)
                Marshal.Copy(data, 0, ptr, data.Length);
        }
        finally
        {
            AcessoRemotoClipboard.GlobalUnlock(hGlobal);
        }

        return hGlobal;
    }
}

[ComVisible(true)]
internal sealed class ManagedEnumFormatEtc : IEnumFORMATETC
{
    private readonly FORMATETC[] _formats;
    private int _currentIndex;

    public ManagedEnumFormatEtc(FORMATETC[] formats, int startIndex = 0)
    {
        _formats = formats;
        _currentIndex = startIndex;
    }

    public int Next(int celt, FORMATETC[] rgelt, int[]? pceltFetched)
    {
        int fetched = 0;
        while (fetched < celt && _currentIndex < _formats.Length)
        {
            rgelt[fetched] = _formats[_currentIndex];
            fetched++;
            _currentIndex++;
        }

        if (pceltFetched != null && pceltFetched.Length > 0)
            pceltFetched[0] = fetched;

        return fetched == celt ? 0 : 1;
    }

    public int Skip(int celt)
    {
        _currentIndex += celt;
        return _currentIndex <= _formats.Length ? 0 : 1;
    }

    public int Reset()
    {
        _currentIndex = 0;
        return 0;
    }

    public void Clone(out IEnumFORMATETC newEnum)
    {
        newEnum = new ManagedEnumFormatEtc(_formats, _currentIndex);
    }
}

[ComVisible(true)]
internal sealed class ManagedComStream : IStream, IDisposable
{
    private readonly Stream _inner;

    public ManagedComStream(Stream inner)
    {
        _inner = inner;
    }

    public void Read(byte[] pv, int cb, IntPtr pcbRead)
    {
        int read = _inner.Read(pv, 0, cb);
        if (pcbRead != IntPtr.Zero)
            Marshal.WriteInt32(pcbRead, read);
    }

    public void Write(byte[] pv, int cb, IntPtr pcbWritten)
    {
        _inner.Write(pv, 0, cb);
        if (pcbWritten != IntPtr.Zero)
            Marshal.WriteInt32(pcbWritten, cb);
    }

    public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
    {
        long pos = 0;
        if (_inner.CanSeek)
        {
            pos = _inner.Seek(dlibMove, (SeekOrigin)dwOrigin);
        }
        else
        {
            try
            {
                pos = (SeekOrigin)dwOrigin switch
                {
                    SeekOrigin.Begin when dlibMove == 0 => _inner.Position,
                    SeekOrigin.Current when dlibMove == 0 => _inner.Position,
                    SeekOrigin.End when dlibMove == 0 => _inner.Length,
                    _ => _inner.Position
                };
            }
            catch
            {
                pos = 0;
            }
        }
        if (plibNewPosition != IntPtr.Zero)
            Marshal.WriteInt64(plibNewPosition, pos);
    }

    public void SetSize(long libNewSize)
    {
        if (_inner.CanSeek)
            _inner.SetLength(libNewSize);
    }

    public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
    {
        byte[] buffer = new byte[AcessoRemotoProtocol.DefaultFileChunkSize];
        long totalRead = 0;
        long totalWritten = 0;

        while (totalRead < cb)
        {
            int toRead = (int)Math.Min(buffer.Length, cb - totalRead);
            int read = _inner.Read(buffer, 0, toRead);
            if (read <= 0)
                break;

            totalRead += read;
            pstm.Write(buffer, read, IntPtr.Zero);
            totalWritten += read;
        }

        if (pcbRead != IntPtr.Zero)
            Marshal.WriteInt64(pcbRead, totalRead);
        if (pcbWritten != IntPtr.Zero)
            Marshal.WriteInt64(pcbWritten, totalWritten);
    }

    public void Commit(int grfCommitFlags) => _inner.Flush();
    public void Revert() { }
    public void LockRegion(long libOffset, long cb, int dwLockType) { }
    public void UnlockRegion(long libOffset, long cb, int dwLockType) { }

    public void Stat(out STATSTG pstatstg, int grfStatFlag)
    {
        long size = 0;
        try
        {
            size = _inner.Length;
        }
        catch
        {
            size = 0;
        }

        pstatstg = new STATSTG
        {
            type = 2, // STGTY_STREAM
            cbSize = size,
            grfMode = 0 // STGM_READ
        };
    }

    public void Clone(out IStream ppstm)
    {
        ppstm = this;
    }

    public void Dispose()
    {
        _inner.Dispose();
    }
}

/// <summary>
/// Stream sob demanda alimentado por chunks recebidos via WebSocket para que o Windows Explorer
/// grave os bytes diretamente no destino do Ctrl+V à medida que chegam da rede.
/// </summary>
internal sealed class RemoteClipboardPullStream : Stream
{
    private readonly BlockingCollection<byte[]> _chunks = new();
    private readonly long _totalSize;
    private byte[] _currentChunk = Array.Empty<byte>();
    private int _currentChunkOffset;
    private long _position;
    private Exception? _error;

    public RemoteClipboardPullStream(long totalSize)
    {
        _totalSize = Math.Max(0, totalSize);
    }

    public void PushChunk(byte[] data, bool isLast, string? errorMessage = null)
    {
        if (!string.IsNullOrEmpty(errorMessage))
        {
            _error = new IOException(errorMessage);
            try { _chunks.CompleteAdding(); } catch { }
            return;
        }

        if (data.Length > 0)
        {
            try { _chunks.Add(data); } catch { }
        }

        if (isLast)
        {
            try { _chunks.CompleteAdding(); } catch { }
        }
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => _totalSize;

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (_error != null)
            throw _error;

        int totalCopied = 0;
        while (totalCopied < count)
        {
            if (_currentChunkOffset < _currentChunk.Length)
            {
                int available = _currentChunk.Length - _currentChunkOffset;
                int toCopy = Math.Min(available, count - totalCopied);
                Array.Copy(_currentChunk, _currentChunkOffset, buffer, offset + totalCopied, toCopy);
                _currentChunkOffset += toCopy;
                totalCopied += toCopy;
                _position += toCopy;
                continue;
            }

            if (_chunks.IsCompleted)
                break;

            if (!_chunks.TryTake(out var nextChunk, TimeSpan.FromSeconds(30)))
            {
                if (_chunks.IsCompleted)
                    break;
                throw new IOException("Tempo limite excedido ao transferir arquivo da área de transferência remota.");
            }

            if (_error != null)
                throw _error;

            _currentChunk = nextChunk;
            _currentChunkOffset = 0;
        }

        return totalCopied;
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _chunks.CompleteAdding(); } catch { }
            _chunks.Dispose();
        }
        base.Dispose(disposing);
    }
}
