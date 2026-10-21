using System.Diagnostics;

namespace SistecHub.Core;

/// <summary>Detecta e gere processos <c>SistecHub.exe</c> em execução.</summary>
internal static class SistecHubAppProcess
{
    const string ProcessName = "SistecHub";

    /// <summary>
    /// Verifica se o processo SistecHub está em execução.
    /// <para>
    /// Se <paramref name="onlyInteractiveSession"/> for <c>true</c>, filtra apenas instâncias
    /// em sessões gráficas de utilizadores (SessionId > 0), ignorando a Sessão 0 onde são
    /// executados os hooks transitórios do instalador Velopack (--veloapp-updated).
    /// </para>
    /// </summary>
    public static bool IsRunning(bool onlyInteractiveSession = false)
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
    public static async Task<bool> WaitForAllExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !cancellationToken.IsCancellationRequested)
        {
            if (!IsRunning(onlyInteractiveSession: false))
                return true;

            try
            {
                await Task.Delay(300, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return !IsRunning(onlyInteractiveSession: false);
            }
        }

        return !IsRunning(onlyInteractiveSession: false);
    }

    /// <summary>
    /// Termina todos os processos SistecHub em execução na máquina (excepto o processo actual).
    /// Primeiro tenta fecho gracioso via CloseMainWindow; se não responderem, força encerramento via Kill().
    /// </summary>
    public static void TerminateAll()
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

                    UpdateActivityLog.Info("Update", $"A solicitar fecho do processo SistecHub (PID {p.Id}, Sessão {p.SessionId})...");
                    p.CloseMainWindow();
                }
                catch
                {
                    // Ignora falhas em instâncias inacessíveis ou já terminadas.
                }
            }

            // Aguarda até 1.5s para fecho gracioso
            Thread.Sleep(1500);

            // Força encerramento para qualquer processo que ainda esteja pendente
            processes = Process.GetProcessesByName(ProcessName);
            foreach (var p in processes)
            {
                try
                {
                    if (p.Id == currentPid || p.HasExited)
                        continue;

                    UpdateActivityLog.Warn("Update", $"A forçar encerramento (Kill) do processo SistecHub (PID {p.Id}, Sessão {p.SessionId}).");
                    p.Kill(entireProcessTree: true);
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
}

