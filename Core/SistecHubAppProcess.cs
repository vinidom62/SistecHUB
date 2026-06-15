using System.Diagnostics;

namespace SistecHub.Core;

/// <summary>Detecta se o processo principal <c>SistecHub.exe</c> está em execução.</summary>
internal static class SistecHubAppProcess
{
    const string ProcessName = "SistecHub";

    public static bool IsRunning()
    {
        try
        {
            return Process.GetProcessesByName(ProcessName).Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
