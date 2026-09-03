using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SistecHub.Core;

/// <summary>Relança o SistecHub na sessão interactiva do utilizador (a partir do serviço Windows).</summary>
public static class InteractiveUserAppLauncher
{
    const uint TokenAssignPrimary = 0x0001;
    const uint TokenDuplicate = 0x0002;
    const uint TokenQuery = 0x0008;
    const uint TokenAdjustDefault = 0x0080;
    const uint TokenAdjustSessionId = 0x0100;
    const int SecurityImpersonation = 2;
    const int TokenPrimary = 1;
    const uint CreateUnicodeEnvironment = 0x00000400;

    public static bool TryLaunchMainAppInActiveSession(string? reason = null)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        if (SistecHubAppProcess.IsRunning(onlyInteractiveSession: true))
        {
            UpdateActivityLog.Info("Update", "SistecHub já está em execução na sessão do utilizador — relançamento ignorado.");
            return true;
        }

        var exePath = ResolveMainExecutablePath();
        if (exePath is null)
        {
            UpdateActivityLog.Error("Update", "SistecHub.exe não encontrado para relançar.");
            return false;
        }

        var sessionId = TryGetInteractiveSessionId();
        if (sessionId is null)
        {
            UpdateActivityLog.Warn("Update", "Nenhuma sessão interactiva encontrada para relançar o SistecHub.");
            return false;
        }

        try
        {
            if (!WTSQueryUserToken(sessionId.Value, out var userToken))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WTSQueryUserToken falhou.");

            try
            {
                if (!DuplicateTokenEx(
                        userToken,
                        TokenAssignPrimary | TokenDuplicate | TokenQuery | TokenAdjustDefault | TokenAdjustSessionId,
                        IntPtr.Zero,
                        SecurityImpersonation,
                        TokenPrimary,
                        out var primaryToken))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "DuplicateTokenEx falhou.");
                }

                try
                {
                    _ = CreateEnvironmentBlock(out var environment, primaryToken, false);
                    try
                    {
                        var startupInfo = new STARTUPINFO
                        {
                            cb = Marshal.SizeOf<STARTUPINFO>(),
                            lpDesktop = @"winsta0\default",
                        };

                        var processInfo = default(PROCESS_INFORMATION);
                        var settings = AppSettingsStore.Load();
                        var autostartFlag = settings.IniciarMinimizado ? " --autostart" : "";
                        var commandLine = $"\"{exePath}\"{autostartFlag}";
                        var workingDirectory = Path.GetDirectoryName(exePath)!;
                        var creationFlags = environment != IntPtr.Zero ? CreateUnicodeEnvironment : 0u;

                        if (!CreateProcessAsUser(
                                primaryToken,
                                exePath,
                                commandLine,
                                IntPtr.Zero,
                                IntPtr.Zero,
                                false,
                                creationFlags,
                                environment,
                                workingDirectory,
                                ref startupInfo,
                                out processInfo))
                        {
                            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateProcessAsUser falhou.");
                        }

                        if (processInfo.hProcess != IntPtr.Zero)
                            CloseHandle(processInfo.hProcess);
                        if (processInfo.hThread != IntPtr.Zero)
                            CloseHandle(processInfo.hThread);

                        UpdateActivityLog.Info(
                            "Update",
                            $"SistecHub relançado na sessão {sessionId}{(reason is null ? "" : $" ({reason})")}.");
                        return true;
                    }
                    finally
                    {
                        if (environment != IntPtr.Zero)
                            DestroyEnvironmentBlock(environment);
                    }
                }
                finally
                {
                    CloseHandle(primaryToken);
                }
            }
            finally
            {
                CloseHandle(userToken);
            }
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao relançar SistecHub na sessão do utilizador.");
            return false;
        }
    }

    static uint? TryGetInteractiveSessionId()
    {
        // 1. Enumera sessões interativas com utilizador logado (suporta tanto RDP quanto consola local)
        if (WTSEnumerateSessions(IntPtr.Zero, 0, 1, out var sessionInfo, out var count))
        {
            try
            {
                var iter = sessionInfo;
                uint? fallbackConnectedSession = null;

                for (var i = 0; i < count; i++)
                {
                    var session = Marshal.PtrToStructure<WTS_SESSION_INFO>(iter);
                    iter = IntPtr.Add(iter, Marshal.SizeOf<WTS_SESSION_INFO>());

                    // Sessão 0 é reservada para serviços do Windows e não é interativa
                    if (session.SessionId == 0)
                        continue;

                    var sId = (uint)session.SessionId;

                    // Prioridade máxima: sessão com estado Ativo (WTSActive) que possui token de utilizador válido
                    if (session.State == WTS_CONNECTSTATE_CLASS.WTSActive)
                    {
                        if (WTSQueryUserToken(sId, out var testToken))
                        {
                            CloseHandle(testToken);
                            return sId;
                        }
                    }
                    else if (session.State == WTS_CONNECTSTATE_CLASS.WTSConnected && fallbackConnectedSession is null)
                    {
                        if (WTSQueryUserToken(sId, out var testToken))
                        {
                            CloseHandle(testToken);
                            fallbackConnectedSession = sId;
                        }
                    }
                }

                if (fallbackConnectedSession is not null)
                    return fallbackConnectedSession;
            }
            finally
            {
                WTSFreeMemory(sessionInfo);
            }
        }

        // 2. Fallback: consola física, validando se possui token interativo
        var consoleSession = WTSGetActiveConsoleSessionId();
        if (consoleSession is not 0xFFFFFFFF and not 0)
        {
            if (WTSQueryUserToken(consoleSession, out var testToken))
            {
                CloseHandle(testToken);
                return consoleSession;
            }
        }

        return null;
    }

    static string? ResolveMainExecutablePath()
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
            return null;

        var dir = Path.GetDirectoryName(processPath);
        if (dir is null)
            return null;

        var mainExe = Path.Combine(dir, AppReleaseConfig.MainExeName);
        return File.Exists(mainExe) ? mainExe : null;
    }

    [DllImport("kernel32.dll")]
    static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    static extern bool WTSEnumerateSessions(
        IntPtr hServer,
        int reserved,
        int version,
        out IntPtr ppSessionInfo,
        out int pCount);

    [DllImport("wtsapi32.dll")]
    static extern void WTSFreeMemory(IntPtr pointer);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

    [DllImport("userenv.dll", SetLastError = true)]
    static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(
        IntPtr hExistingToken,
        uint dwDesiredAccess,
        IntPtr lpTokenAttributes,
        int impersonationLevel,
        int tokenType,
        out IntPtr phNewToken);

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

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WTS_SESSION_INFO
    {
        public int SessionId;
        public IntPtr pWinStationName;
        public WTS_CONNECTSTATE_CLASS State;
    }

    enum WTS_CONNECTSTATE_CLASS
    {
        WTSActive,
        WTSConnected,
        WTSConnectQuery,
        WTSShadow,
        WTSDisconnected,
        WTSIdle,
        WTSListen,
        WTSReset,
        WTSDown,
        WTSInit,
    }
}
