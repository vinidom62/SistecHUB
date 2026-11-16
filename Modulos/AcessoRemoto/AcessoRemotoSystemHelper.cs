using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using SistecHub.Core;

namespace SistecHub.Modulos.AcessoRemoto;

/// <summary>
/// Processo auxiliar em execução como LocalSystem na sessão interativa ativa (consola/RDP),
/// permitindo capturar e controlar o Secure Desktop (winsta0\Winlogon — UAC, Tela de Bloqueio, Ctrl+Alt+Del)
/// e janelas elevadas (UIPI bypass).
/// </summary>
internal static class AcessoRemotoSystemHelper
{
    public const string PipeName = "SistecHub.Remoto.SystemHelper";
    const string HelperMutexName = @"Local\SistecHub.Remoto.SystemHelper.Instance";

    const byte CmdCaptureFrame = 0x01;
    const byte CmdSendInput = 0x02;
    const byte CmdBlockInput = 0x03;
    const byte CmdSendSas = 0x04;

    static readonly object ClientLock = new();
    static NamedPipeClientStream? _clientPipe;

    public static void RunServer()
    {
        using var mutex = new Mutex(true, HelperMutexName, out bool createdNew);
        if (!createdNew)
            return;

        using var pidMarkerGlobal = TryCreatePidMarkerMutex(Environment.ProcessId, global: true);
        using var pidMarkerLocal = TryCreatePidMarkerMutex(Environment.ProcessId, global: false);

        var worker = new Thread(ServerLoop)
        {
            IsBackground = false,
            Name = "RemotoSystemHelperDesktopThread",
        };
        worker.Start();
        worker.Join();
    }

    static Mutex? TryCreatePidMarkerMutex(int pid, bool global)
    {
        try
        {
            return new Mutex(true, RemotoSasCoordinator.GetHelperPidMutexName(pid, global), out _);
        }
        catch
        {
            return null;
        }
    }

    static void ServerLoop()
    {
        using var capturer = new AcessoRemotoScreenCapturer(0, 70);
        int lastRefreshedMonitor = -999;

        while (true)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = CreateSecurePipeServer();
                server.WaitForConnection();

                HandleClientConnection(server, capturer, ref lastRefreshedMonitor);
            }
            catch
            {
                Thread.Sleep(150);
            }
            finally
            {
                if (server is not null)
                {
                    try { server.Dispose(); } catch { }
                }
            }
        }
    }

    static NamedPipeServerStream CreateSecurePipeServer()
    {
        try
        {
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl,
                AccessControlType.Allow));

            return NamedPipeServerStreamAcl.Create(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None,
                inBufferSize: 64 * 1024,
                outBufferSize: 512 * 1024,
                security);
        }
        catch
        {
            return new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                NamedPipeServerStream.MaxAllowedServerInstances,
                PipeTransmissionMode.Byte,
                PipeOptions.None);
        }
    }

    static void HandleClientConnection(
        NamedPipeServerStream stream,
        AcessoRemotoScreenCapturer capturer,
        ref int lastRefreshedMonitor)
    {
        Span<byte> cmdBuf = stackalloc byte[1];
        Span<byte> headerBuf = stackalloc byte[36];

        while (stream.IsConnected)
        {
            if (!TryReadExact(stream, cmdBuf))
                break;

            byte cmd = cmdBuf[0];
            switch (cmd)
            {
                case CmdCaptureFrame:
                {
                    var reqSpan = headerBuf[..8];
                    if (!TryReadExact(stream, reqSpan))
                        return;

                    int selectedMonitor = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[..4]);
                    int quality = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[4..8]);

                    AcessoRemotoScreenCapturer.AttachToInteractiveDesktop();
                    if (selectedMonitor != lastRefreshedMonitor)
                    {
                        capturer.RefreshMonitors();
                        capturer.SetSelectedMonitor(selectedMonitor);
                        lastRefreshedMonitor = selectedMonitor;
                    }
                    capturer.SetQuality(quality);

                    var frame = capturer.CaptureFrame(out _, out _);
                    var (ax, ay, aw, ah) = capturer.GetActiveBounds();

                    if (frame is null || frame.Length == 0)
                    {
                        cmdBuf[0] = 0;
                        stream.Write(cmdBuf);
                    }
                    else
                    {
                        AcessoRemotoCursorHelper.TryGetCurrentState(ax, ay, aw, ah, AcessoRemotoClientPriorityMonitor.IsClientActive, out var curState);
                        var curPacket = AcessoRemotoCursorHelper.CreatePacket(curState);

                        var respHeader = headerBuf[..32];
                        respHeader[0] = 1;
                        BinaryPrimitives.WriteInt32LittleEndian(respHeader[1..5], ax);
                        BinaryPrimitives.WriteInt32LittleEndian(respHeader[5..9], ay);
                        BinaryPrimitives.WriteInt32LittleEndian(respHeader[9..13], aw);
                        BinaryPrimitives.WriteInt32LittleEndian(respHeader[13..17], ah);
                        BinaryPrimitives.WriteInt32LittleEndian(respHeader[17..21], frame.Length);
                        curPacket.CopyTo(respHeader.Slice(21, 11));
                        stream.Write(respHeader);
                        stream.Write(frame);
                    }
                    stream.Flush();
                    break;
                }

                case CmdSendInput:
                {
                    var reqSpan = headerBuf[..20];
                    if (!TryReadExact(stream, reqSpan))
                        return;

                    int ax = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[..4]);
                    int ay = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[4..8]);
                    int aw = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[8..12]);
                    int ah = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[12..16]);
                    int len = BinaryPrimitives.ReadInt32LittleEndian(reqSpan[16..20]);

                    if (len <= 0 || len > 4096)
                        return;

                    byte[] packet = new byte[len];
                    if (!TryReadExact(stream, packet))
                        return;

                    AcessoRemotoInputHandler.HandleInputPacketDirect(packet, ax, ay, aw, ah);

                    // Amostra o cursor atualizado após o input processado no helper
                    AcessoRemotoCursorHelper.TryGetCurrentState(ax, ay, aw, ah, AcessoRemotoClientPriorityMonitor.IsClientActive, out var curState);
                    var curPacket = AcessoRemotoCursorHelper.CreatePacket(curState);

                    Span<byte> resp = stackalloc byte[12];
                    resp[0] = 1;
                    curPacket.CopyTo(resp[1..12]);
                    stream.Write(resp);
                    stream.Flush();
                    break;
                }

                case CmdBlockInput:
                {
                    if (!TryReadExact(stream, cmdBuf))
                        return;

                    bool block = cmdBuf[0] != 0;
                    var (ok, _) = AcessoRemotoInputHandler.BlockUserInputDirect(block);
                    cmdBuf[0] = ok ? (byte)1 : (byte)0;
                    stream.Write(cmdBuf);
                    stream.Flush();
                    break;
                }

                case CmdSendSas:
                {
                    RemotoSasCoordinator.TryTriggerSasDirectly();
                    cmdBuf[0] = 1;
                    stream.Write(cmdBuf);
                    stream.Flush();
                    break;
                }

                default:
                    return;
            }
        }
    }

    public static bool TryCaptureFrameViaHelper(
        int selectedMonitor,
        int quality,
        out int ax,
        out int ay,
        out int aw,
        out int ah,
        out byte[]? frame)
    {
        return TryCaptureFrameViaHelper(selectedMonitor, quality, out ax, out ay, out aw, out ah, out frame, out _);
    }

    public static bool TryCaptureFrameViaHelper(
        int selectedMonitor,
        int quality,
        out int ax,
        out int ay,
        out int aw,
        out int ah,
        out byte[]? frame,
        out byte[]? cursorPacket)
    {
        ax = 0;
        ay = 0;
        aw = 0;
        ah = 0;
        frame = null;
        cursorPacket = null;

        lock (ClientLock)
        {
            var pipe = EnsureConnectedClientLocked();
            if (pipe is null)
                return false;

            try
            {
                Span<byte> req = stackalloc byte[9];
                req[0] = CmdCaptureFrame;
                BinaryPrimitives.WriteInt32LittleEndian(req[1..5], selectedMonitor);
                BinaryPrimitives.WriteInt32LittleEndian(req[5..9], quality);
                pipe.Write(req);
                pipe.Flush();

                Span<byte> status = stackalloc byte[1];
                if (!TryReadExact(pipe, status))
                {
                    ResetClientLocked();
                    return false;
                }

                if (status[0] == 0)
                    return false;

                Span<byte> meta = stackalloc byte[31];
                if (!TryReadExact(pipe, meta))
                {
                    ResetClientLocked();
                    return false;
                }

                ax = BinaryPrimitives.ReadInt32LittleEndian(meta[..4]);
                ay = BinaryPrimitives.ReadInt32LittleEndian(meta[4..8]);
                aw = BinaryPrimitives.ReadInt32LittleEndian(meta[8..12]);
                ah = BinaryPrimitives.ReadInt32LittleEndian(meta[12..16]);
                int len = BinaryPrimitives.ReadInt32LittleEndian(meta[16..20]);
                cursorPacket = meta.Slice(20, 11).ToArray();

                if (len <= 0 || len > 16 * 1024 * 1024)
                {
                    ResetClientLocked();
                    return false;
                }

                var data = new byte[len];
                if (!TryReadExact(pipe, data))
                {
                    ResetClientLocked();
                    return false;
                }

                frame = data;
                return true;
            }
            catch
            {
                ResetClientLocked();
                return false;
            }
        }
    }

    public static bool TrySendInputViaHelper(
        ReadOnlySpan<byte> packet,
        int ax,
        int ay,
        int aw,
        int ah)
    {
        return TrySendInputViaHelper(packet, ax, ay, aw, ah, out _);
    }

    public static bool TrySendInputViaHelper(
        ReadOnlySpan<byte> packet,
        int ax,
        int ay,
        int aw,
        int ah,
        out byte[]? cursorPacket)
    {
        cursorPacket = null;
        if (packet.Length == 0 || packet.Length > 4096)
            return false;

        lock (ClientLock)
        {
            var pipe = EnsureConnectedClientLocked();
            if (pipe is null)
                return false;

            try
            {
                Span<byte> header = stackalloc byte[21];
                header[0] = CmdSendInput;
                BinaryPrimitives.WriteInt32LittleEndian(header[1..5], ax);
                BinaryPrimitives.WriteInt32LittleEndian(header[5..9], ay);
                BinaryPrimitives.WriteInt32LittleEndian(header[9..13], aw);
                BinaryPrimitives.WriteInt32LittleEndian(header[13..17], ah);
                BinaryPrimitives.WriteInt32LittleEndian(header[17..21], packet.Length);

                pipe.Write(header);
                pipe.Write(packet);
                pipe.Flush();

                Span<byte> resp = stackalloc byte[12];
                if (!TryReadExact(pipe, resp))
                {
                    ResetClientLocked();
                    return false;
                }

                if (resp[0] == 1)
                {
                    cursorPacket = resp[1..12].ToArray();
                    return true;
                }
                return false;
            }
            catch
            {
                ResetClientLocked();
                return false;
            }
        }
    }

    public static bool TryBlockInputViaHelper(bool block, out bool success)
    {
        success = false;

        lock (ClientLock)
        {
            var pipe = EnsureConnectedClientLocked();
            if (pipe is null)
                return false;

            try
            {
                Span<byte> req = stackalloc byte[2];
                req[0] = CmdBlockInput;
                req[1] = block ? (byte)1 : (byte)0;
                pipe.Write(req);
                pipe.Flush();

                Span<byte> ack = stackalloc byte[1];
                if (!TryReadExact(pipe, ack))
                {
                    ResetClientLocked();
                    return false;
                }

                success = ack[0] == 1;
                return true;
            }
            catch
            {
                ResetClientLocked();
                return false;
            }
        }
    }

    static NamedPipeClientStream? EnsureConnectedClientLocked()
    {
        if (_clientPipe is not null && _clientPipe.IsConnected)
            return _clientPipe;

        ResetClientLocked();

        try
        {
            var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.None);
            client.Connect(40);
            _clientPipe = client;
            return client;
        }
        catch
        {
            return null;
        }
    }

    static void ResetClientLocked()
    {
        if (_clientPipe is not null)
        {
            try { _clientPipe.Dispose(); } catch { }
            _clientPipe = null;
        }
    }

    static bool TryReadExact(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int n = stream.Read(buffer[total..]);
            if (n <= 0)
                return false;
            total += n;
        }
        return true;
    }
}

