using SistecHub.Core;

namespace SistecHub.Modulos.GLPI;

/// <summary>
/// Gerencia a sessão do técnico autenticado no GLPI para autorização de acesso remoto não supervisionado.
/// </summary>
public static class GlpiTechnicianSession
{
    static readonly object _lock = new();
    static bool _initialized;

    public static bool IsAuthenticated { get; private set; }
    public static string TechnicianName { get; private set; } = "";
    public static string TechnicianAvatarBase64 { get; private set; } = "";
    public static string TechnicianLogin { get; private set; } = "";
    public static bool RememberLogin { get; private set; }

    public static event EventHandler? StateChanged;

    public static void Initialize()
    {
        lock (_lock)
        {
            if (_initialized)
                return;
            _initialized = true;

            try
            {
                var settings = AppSettingsStore.Load();
                if (settings.TecnicoLembrarLogin && !string.IsNullOrWhiteSpace(settings.TecnicoNome))
                {
                    IsAuthenticated = true;
                    TechnicianName = settings.TecnicoNome.Trim();
                    TechnicianAvatarBase64 = settings.TecnicoAvatarBase64 ?? "";
                    TechnicianLogin = settings.TecnicoGlpiLogin ?? "";
                    RememberLogin = true;
                }
                else
                {
                    IsAuthenticated = false;
                    TechnicianName = "";
                    TechnicianAvatarBase64 = "";
                    TechnicianLogin = "";
                    RememberLogin = false;

                    // Limpa dados residuais de login anterior caso Lembrar Login não esteja ativo
                    if (!string.IsNullOrEmpty(settings.TecnicoNome) || !string.IsNullOrEmpty(settings.TecnicoAvatarBase64))
                    {
                        settings.TecnicoNome = "";
                        settings.TecnicoAvatarBase64 = "";
                        settings.TecnicoGlpiLogin = "";
                        settings.TecnicoLembrarLogin = false;
                        AppSettingsStore.Save(settings);
                    }
                }
            }
            catch
            {
                IsAuthenticated = false;
            }
        }
    }

    public static void SetAuthenticated(GlpiTechnicianProfile profile, string password, bool rememberLogin)
    {
        lock (_lock)
        {
            IsAuthenticated = true;
            TechnicianName = profile.FullName;
            TechnicianAvatarBase64 = profile.AvatarBase64;
            TechnicianLogin = profile.Login;
            RememberLogin = rememberLogin;

            try
            {
                var settings = AppSettingsStore.Load();
                if (rememberLogin)
                {
                    settings.TecnicoNome = profile.FullName;
                    settings.TecnicoAvatarBase64 = profile.AvatarBase64;
                    settings.TecnicoGlpiLogin = profile.Login;
                    settings.TecnicoLembrarLogin = true;
                }
                else
                {
                    settings.TecnicoNome = "";
                    settings.TecnicoAvatarBase64 = "";
                    settings.TecnicoGlpiLogin = "";
                    settings.TecnicoLembrarLogin = false;
                }
                AppSettingsStore.Save(settings);

                if (rememberLogin && !string.IsNullOrEmpty(password))
                {
                    SecureCredentialStore.SaveTecnicoPassword(SharedMachineStorage.RootPath, password);
                }
                else
                {
                    SecureCredentialStore.SaveTecnicoPassword(SharedMachineStorage.RootPath, "");
                }
            }
            catch
            {
                // Melhor esforço ao gravar definições
            }
        }

        StateChanged?.Invoke(null, EventArgs.Empty);
    }

    public static void Logout()
    {
        lock (_lock)
        {
            IsAuthenticated = false;
            TechnicianName = "";
            TechnicianAvatarBase64 = "";
            TechnicianLogin = "";
            RememberLogin = false;

            try
            {
                var settings = AppSettingsStore.Load();
                settings.TecnicoNome = "";
                settings.TecnicoAvatarBase64 = "";
                settings.TecnicoGlpiLogin = "";
                settings.TecnicoLembrarLogin = false;
                AppSettingsStore.Save(settings);

                SecureCredentialStore.SaveTecnicoPassword(SharedMachineStorage.RootPath, "");
            }
            catch
            {
                // Melhor esforço
            }
        }

        StateChanged?.Invoke(null, EventArgs.Empty);
    }
}

