using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace SistecHub.Core;

/// <summary>Detecta e gere processos <c>SistecHub.exe</c> em execução.</summary>
internal static class SistecHubAppProcess
{
    const string ProcessName = "SistecHub";
    const uint TokenQuery = 0x0008;

    /// <summary>
    /// Verifica se o aplicativo principal SistecHub do utilizador está em execução.
    /// <para>
    /// Por predefinição (<paramref name="includeSystemHelper"/> = <c>false</c>), ignora o processo auxiliar
    /// <c>--remoto-system-helper</c> mantido pelo serviço Windows como LocalSystem.
    /// </para>
    /// <para>
    /// Se <paramref name="onlyInteractiveSession"/> for <c>true</c>, filtra apenas instâncias
    /// em sessões gráficas de utilizadores (SessionId > 0), ignorando a Sessão 0 onde são
    /// executados os hooks transitórios do instalador Velopack (--veloapp-updated).
    /// </para>
    /// </summary>
    public static bool IsRunning(bool onlyInteractiveSession = false, bool includeSystemHelper = false)
    {
        try
        {
            var currentPid = Environment.ProcessId;
            var processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.HasExited)
                        continue;

                    if (!includeSystemHelper && IsSystemHelperProcess(p))
                        continue;

                    if (!onlyInteractiveSession || p.SessionId > 0)
                        return true;
                }
                catch
                {
                    // Se o processo já terminou ou não temos acesso, ignora.
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Aguarda que todos os processos SistecHub terminem dentro do tempo limite especificado.
    /// </summary>
    public static async Task<bool> WaitForAllExitAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        bool includeSystemHelper = false)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (!IsRunning(onlyInteractiveSession: false, includeSystemHelper: includeSystemHelper))
                return true;

            try
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return !IsRunning(onlyInteractiveSession: false, includeSystemHelper: includeSystemHelper);
            }
        }

        return !IsRunning(onlyInteractiveSession: false, includeSystemHelper: includeSystemHelper);
    }

    /// <summary>
    /// Termina os processos SistecHub em execução na máquina (excepto o processo actual).
    /// Primeiro tenta fecho gracioso via CloseMainWindow; se não responderem, força encerramento via Kill().
    /// </summary>
    public static void TerminateAll(bool includeSystemHelper = false)
    {
        try
        {
            if (includeSystemHelper)
                RemotoSasCoordinator.SuspendAndStopHelperForUpdate();

            var currentPid = Environment.ProcessId;
            var processes = Process.GetProcessesByName(ProcessName);
            var waitedForGraceful = false;

            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.HasExited)
                        continue;

                    if (!includeSystemHelper && IsSystemHelperProcess(p))
                        continue;

                    if (IsSystemHelperProcess(p))
                        continue;

                    UpdateActivityLog.Info("Update", $"A solicitar fecho do processo SistecHub (PID {p.Id}, Sessão {p.SessionId})...");
                    if (p.CloseMainWindow())
                        waitedForGraceful = true;
                }
                catch
                {
                    // Ignora falhas em instâncias inacessíveis ou já terminadas.
                }
            }

            if (waitedForGraceful)
            {
                // Aguarda até 1.5s para fecho gracioso
                Thread.Sleep(1500);
            }

            // Força encerramento para qualquer processo que ainda esteja pendente
            processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.HasExited)
                        continue;

                    if (!includeSystemHelper && IsSystemHelperProcess(p))
                        continue;

                    UpdateActivityLog.Warn("Update", $"A forçar encerramento (Kill) do processo SistecHub (PID {p.Id}, Sessão {p.SessionId}).");
                    p.Kill(entireProcessTree: true);
                    p.WaitForExit(2000);
                }
                catch (Exception ex)
                {
                    UpdateActivityLog.Warn("Update", $"Não foi possível forçar encerramento do PID {p.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            UpdateActivityLog.LogException("Update", ex, "Falha ao terminar processos SistecHub.");
        }
    }

    static bool IsSystemHelperProcess(Process process)
    {
        try
        {
            var pid = process.Id;
            if (pid <= 0)
                return false;

            if (pid == RemotoSasCoordinator.CurrentHelperProcessId)
                return true;

            if (HasHelperPidMutex(pid, global: true) || HasHelperPidMutex(pid, global: false))
                return true;

            if (IsLocalSystemProcess(process))
                return true;

            return false;
        }
        catch
        {
            return false;
        }
    }

    static bool HasHelperPidMutex(int pid, bool global)
    {
        try
        {
            if (Mutex.TryOpenExisting(RemotoSasCoordinator.GetHelperPidMutexName(pid, global), out var existing))
            {
                existing.Dispose();
                return true;
            }
        }
        catch
        {
            // Ignora se não existir ou acesso negado.
        }

        return false;
    }

    static bool IsLocalSystemProcess(Process process)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        try
        {
            if (!OpenProcessToken(process.Handle, TokenQuery, out var tokenHandle))
                return false;

            try
            {
                using var identity = new WindowsIdentity(tokenHandle);
                return identity.IsSystem;
            }
            finally
            {
                CloseHandle(tokenHandle);
            }
        }
        catch
        {
            return false;
        }
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool CloseHandle(IntPtr hObject);
}

