using System.IO;
using Microsoft.Win32;

namespace SistecHub.Core;

/// <summary>
/// Regista o protocolo URI personalizado <c>sistechub://</c> no Registro do Windows
/// para permitir a inicialização direta de conexões de acesso remoto a partir do GLPI (ex: <c>sistechub://remoto/123456789</c>).
/// </summary>
internal static class WindowsUriSchemeRegistration
{
    const string UriScheme = "sistechub";
    const string ClassesSubKey = @"Software\Classes\" + UriScheme;

    public static void EnsureRegistered(string? customExePath = null)
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            var targetExe = customExePath ?? Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(targetExe) || !File.Exists(targetExe))
                return;

            using var key = Registry.CurrentUser.CreateSubKey(ClassesSubKey);
            if (key is null)
                return;

            key.SetValue("", "URL:SistecHub Protocol");
            key.SetValue("URL Protocol", "");

            using var defaultIcon = key.CreateSubKey("DefaultIcon");
            defaultIcon?.SetValue("", $"\"{targetExe}\",0");

            using var command = key.CreateSubKey(@"shell\open\command");
            command?.SetValue("", $"\"{targetExe}\" \"%1\"");

            AppDebugLog.Info("UriScheme", "Protocolo URI sistechub:// registado com sucesso em HKCU.");
        }
        catch (Exception ex)
        {
            AppDebugLog.Warn("UriScheme", "Não foi possível registar protocolo URI sistechub: " + ex.Message);
        }
    }

    public static void Uninstall()
    {
        if (!OperatingSystem.IsWindows())
            return;

        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(ClassesSubKey, throwOnMissingSubKey: false);
            AppDebugLog.Info("UriScheme", "Protocolo URI sistechub:// removido de HKCU.");
        }
        catch (Exception ex)
        {
            AppDebugLog.Warn("UriScheme", "Falha ao desinstalar protocolo URI sistechub: " + ex.Message);
        }
    }
}
