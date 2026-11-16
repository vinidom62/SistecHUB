using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace SistecHub.Core;

/// <summary>
/// Coordena o envio da sequência de segurança Ctrl+Alt+Del (SAS) e mantém o processo auxiliar LocalSystem
/// na sessão interativa ativa para captura e controle de telas UAC e bloqueio (winsta0\Winlogon).
/// </summary>
public static class RemotoSasCoordinator
{
    const string GlobalEventName = @"Global\SistecHub.Remoto.SendSAS";
    const string SystemPolicyKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
    const string SoftwareSasValueName = "SoftwareSASGeneration";

    const uint TokenAssignPrimary = 0x0001;
    const uint TokenDuplicate = 0x0002;
    const uint TokenQuery = 0x0008;
    const uint TokenAdjustDefault = 0x0080;
    const uint TokenAdjustSessionId = 0x0100;
    const uint MaximumAllowed = 0x02000000;
    const int SecurityImpersonation = 2;
    const int TokenPrimary = 1;
    const int TokenSessionId = 12;
    const uint CreateNoWindow = 0x08000000;

    static int _helperPid;
    static uint _helperSessionId = 0xFFFFFFFF;
    static volatile bool _suspendedForUpdate;

    public static int CurrentHelperProcessId => Volatile.Read(ref _helperPid);

    public static string GetHelperPidMutexName(int pid, bool global) =>
        (global ? @"Global\" : @"Local\") + $"SistecHub.Remoto.SystemHelper.Pid.{pid}";

    /// <summary>
    /// Suspende a recriação do processo auxiliar <c>--remoto-system-helper</c> e encerra a instância atual
    /// para que o <c>Update.exe</c> possa substituir <c>SistecHub.exe</c> sem bloqueio de ficheiro.
    /// </summary>
    public static void SuspendAndStopHelperForUpdate()
    {
        _suspendedForUpdate = true;
        StopSystemHelperProcess();
    }

    public static void ResumeHelperAfterUpdateFailure()
    {
        _suspendedForUpdate = false;
    }

    public static string SasRequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "remoto-sendsas.request");

    /// <summary>
    /// Solicita o envio de Ctrl+Alt+Del (SAS) ao serviço LocalSystem e tenta também diretamente no processo atual.
    /// </summary>
    public static void RequestSendSas()
    {
        try
        {
            SharedMachineStorage.EnsureDirectory();
            File.WriteAllText(SasRequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
        }
        catch
        {
            // Ignora falha ao gravar ficheiro de pedido.
        }

        try
        {
            if (EventWaitHandle.TryOpenExisting(GlobalEventName, out var evt))
            {
                using (evt)
                {
                    evt.Set();
                }
            }
        }
        catch
        {
            // Ignora se o evento global ainda não tiver sido criado pelo serviço.
        }

        TryTriggerSasDirectly();
    }

    public static void TryTriggerSasDirectly()
    {
        EnsureSoftwareSasPolicy();

        try
        {
            SendSAS(false);
        }
        catch
        {
            // Ignora se sas.dll falhar.
        }

        try
        {
            SendSAS(true);
        }
        catch
        {
            // Ignora se sas.dll falhar.
        }
    }

    public static void EnsureSoftwareSasPolicy()
    {
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(SystemPolicyKeyPath, writable: true);
            key?.SetValue(SoftwareSasValueName, 3, RegistryValueKind.DWord);
        }
        catch
        {
            // Requer privilégios de administrador / LocalSystem.
        }
    }

    public static async Task RunServiceListenerLoopAsync(CancellationToken stoppingToken)
    {
        EnsureSoftwareSasPolicy();

        EventWaitHandle? sasEvent = TryCreateGlobalSasEvent();
        long lastHelperCheckTicks = 0;

        try
        {
            using (sasEvent)
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    long nowTicks = Environment.TickCount64;
                    if (!_suspendedForUpdate && (nowTicks - lastHelperCheckTicks >= 2000 || lastHelperCheckTicks == 0))
                    {
                        lastHelperCheckTicks = nowTicks;
                        EnsureSystemHelperRunningInActiveSession();
                    }

                    bool triggered = false;

                    if (sasEvent is not null)
                    {
                        try
                        {
                            triggered = await Task.Run(() => sasEvent.WaitOne(250), stoppingToken)
                                .WaitAsync(stoppingToken)
                                .ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            break;
                        }
                        catch
                        {
                            await Task.Delay(250, stoppingToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        await Task.Delay(250, stoppingToken).ConfigureAwait(false);
                    }

                    if (TryConsumeSasRequestFile())
                        triggered = true;

                    if (triggered)
                        TryTriggerSasDirectly();
                }
            }
        }
        finally
        {
            StopSystemHelperProcess();
        }
    }

    static uint? TryGetTargetSessionId()
    {
        uint consoleSession = WTSGetActiveConsoleSessionId();
        if (consoleSession is not 0xFFFFFFFF and not 0)
            return consoleSession;

        if (WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessionInfo, out var count))
        {
            try
            {
                var iter = sessionInfo;
                for (int i = 0; i < count; i++)
                {
                    var s = Marshal.PtrToStructure<WTS_SESSION_INFO>(iter);
                    iter = IntPtr.Add(iter, Marshal.SizeOf<WTS_SESSION_INFO>());
                    if (s.SessionId != 0 && s.State == 0 /* WTSActive */)
                        return (uint)s.SessionId;
                }
            }
            finally
            {
                WTSFreeMemory(sessionInfo);
            }
        }

        return null;
    }

    static void EnsureSystemHelperRunningInActiveSession()
    {
        if (_suspendedForUpdate)
            return;

        try
        {
            var targetSession = TryGetTargetSessionId();
            if (targetSession is null)
                return;
            uint sessionId = targetSession.Value;

            if (_helperPid > 0 && _helperSessionId == sessionId)
            {
                try
                {
                    using var existing = Process.GetProcessById(_helperPid);
                    if (!existing.HasExited && (uint)existing.SessionId == sessionId)
                        return;
                }
                catch
                {
                    // Processo já encerrou.
                }
            }

            StopSystemHelperProcess();

            var processPath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath))
                return;

            var dir = Path.GetDirectoryName(processPath);
            if (dir is null)
                return;

            var mainExe = Path.Combine(dir, AppReleaseConfig.MainExeName);
            if (!File.Exists(mainExe))
                return;

            if (!OpenProcessToken(
                    GetCurrentProcess(),
                    TokenAssignPrimary | TokenDuplicate | TokenQuery | TokenAdjustDefault | TokenAdjustSessionId | MaximumAllowed,
                    out var currentToken))
            {
                return;
            }

            try
            {
                if (!DuplicateTokenEx(
                        currentToken,
                        MaximumAllowed,
                        IntPtr.Zero,
                        SecurityImpersonation,
                        TokenPrimary,
                        out var primaryToken))
                {
                    return;
                }

                try
                {
                    if (!SetTokenInformation(primaryToken, TokenSessionId, ref sessionId, (uint)sizeof(uint)))
                        return;

                    var startupInfo = new STARTUPINFO
                    {
                        cb = Marshal.SizeOf<STARTUPINFO>(),
                        lpDesktop = @"winsta0\default",
                    };

                    var commandLine = $"\"{mainExe}\" --remoto-system-helper";
                    if (CreateProcessAsUser(
                            primaryToken,
                            mainExe,
                            commandLine,
                            IntPtr.Zero,
                            IntPtr.Zero,
                            false,
                            CreateNoWindow,
                            IntPtr.Zero,
                            dir,
                            ref startupInfo,
                            out var procInfo))
                    {
                        _helperPid = procInfo.dwProcessId;
                        _helperSessionId = sessionId;

                        if (procInfo.hProcess != IntPtr.Zero)
                            CloseHandle(procInfo.hProcess);
                        if (procInfo.hThread != IntPtr.Zero)
                            CloseHandle(procInfo.hThread);
                    }
                }
                finally
                {
                    CloseHandle(primaryToken);
                }
            }
            finally
            {
                CloseHandle(currentToken);
            }
        }
        catch
        {
            // Ignora falhas transitórias de sessão.
        }
    }

    static void StopSystemHelperProcess()
    {
        int pid = _helperPid;
        _helperPid = 0;
        _helperSessionId = 0xFFFFFFFF;

        if (pid <= 0)
            return;

        try
        {
            using var proc = Process.GetProcessById(pid);
            if (!proc.HasExited)
            {
                proc.Kill();
                proc.WaitForExit(2000);
            }
        }
        catch
        {
            // Ignora se já tiver terminado.
        }
    }

    static bool TryConsumeSasRequestFile()
    {
        try
        {
            var path = SasRequestFilePath;
            if (!File.Exists(path))
                return false;

            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    static EventWaitHandle? TryCreateGlobalSasEvent()
    {
        try
        {
            var security = new EventWaitHandleSecurity();
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                EventWaitHandleRights.Modify | EventWaitHandleRights.Synchronize,
                AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                EventWaitHandleRights.FullControl,
                AccessControlType.Allow));
            security.AddAccessRule(new EventWaitHandleAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                EventWaitHandleRights.FullControl,
                AccessControlType.Allow));

            return EventWaitHandleAcl.Create(
                initialState: false,
                mode: EventResetMode.AutoReset,
                name: GlobalEventName,
                createdNew: out _,
                eventSecurity: security);
        }
        catch
        {
            try
            {
                return new EventWaitHandle(false, EventResetMode.AutoReset, GlobalEventName);
            }
            catch
            {
                return null;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public int State;
    }

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pointer);

    [DllImport("sas.dll", SetLastError = true)]
    static extern void SendSAS([MarshalAs(UnmanagedType.Bool)] bool asUser);

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr phNewToken);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool SetTokenInformation(
        IntPtr tokenHandle,
        int tokenInformationClass,
        ref uint tokenInformation,
        uint tokenInformationLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessAsUser(
        IntPtr hToken,
        string lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string lpCurrentDirectory,
        ref STARTUPINFO lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}
