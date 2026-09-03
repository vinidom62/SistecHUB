using System.Diagnostics;

namespace SistecHub.Core;

/// <summary>Detecta se o processo principal <c>SistecHub.exe</c> está em execução.</summary>
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
            var processes = Process.GetProcessesByName(ProcessName);
            if (!onlyInteractiveSession)
                return processes.Length > 0;

            foreach (var p in processes)
            {
                try
                {
                    if (p.SessionId > 0 && !p.HasExited)
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
}
