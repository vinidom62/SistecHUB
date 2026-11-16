using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace SistecHub.Core;

/// <summary>Definições em memória da aplicação.</summary>
public sealed class AppUserSettings
{
    public string EntityId { get; set; } = "";

    /// <summary>User token do GLPI para autenticação na API REST.</summary>
    public string GlpiUserToken { get; set; } = "";

    /// <summary>Chave API Groq (obtida do plugin GLPI; variável de ambiente <c>GROQ_API_KEY</c> tem prioridade).</summary>
    public string GroqApiKey { get; set; } = "";

    /// <summary>Id do computador no plugin SistecHub Machines (GLPI).</summary>
    public string GlpiMachineId { get; set; } = "";

    /// <summary>Quando ativo, o SistecHub inicia em segundo plano ao arrancar com o Windows.</summary>
    public bool IniciarMinimizado { get; set; } = true;

    /// <summary>URL do servidor relay WebSocket para o módulo de Acesso remoto.</summary>
    public string RemotoServerUrl { get; set; } = AppSettingsStore.DefaultRemotoServerUrl;

    /// <summary>Nome completo do operador/técnico para identificação no acesso remoto.</summary>
    public string TecnicoNome { get; set; } = "";

    /// <summary>Foto de perfil do operador/técnico em base64.</summary>
    public string TecnicoAvatarBase64 { get; set; } = "";

    /// <summary>Login do operador/técnico no GLPI.</summary>
    public string TecnicoGlpiLogin { get; set; } = "";

    /// <summary>Indica se o login do técnico foi salvo nesta máquina para persistir entre sessões.</summary>
    public bool TecnicoLembrarLogin { get; set; } = false;

    /// <summary>ID fixo exclusivo do computador para o SistecHUB Acesso Remoto (9 dígitos).</summary>
    public string RemotoId { get; set; } = "";

    /// <summary>Senha fixa para acesso remoto não supervisionado (opcional).</summary>
    public string RemotoUnattendedPassword { get; set; } = "";

    /// <summary>Token de identidade exclusivo da máquina para o SistecHUB Acesso Remoto (Host Token).</summary>
    public string RemotoHostToken { get; set; } = "";

    /// <summary>Quando verdadeiro, o sistema de acesso remoto fica totalmente bloqueado/desabilitado nesta máquina.</summary>
    public bool RemotoBloqueado { get; set; } = false;
}

/// <summary>Conteúdo público persistido em <c>settings.json</c> (sem credenciais).</summary>
sealed class PersistedAppSettings
{
    public string EntityId { get; set; } = "";

    public string GlpiMachineId { get; set; } = "";

    public bool IniciarMinimizado { get; set; } = true;

    public string RemotoServerUrl { get; set; } = AppSettingsStore.DefaultRemotoServerUrl;

    public string TecnicoNome { get; set; } = "";

    public string TecnicoAvatarBase64 { get; set; } = "";

    public string TecnicoGlpiLogin { get; set; } = "";

    public bool TecnicoLembrarLogin { get; set; } = false;

    public string RemotoId { get; set; } = "";

    public string RemotoUnattendedPassword { get; set; } = "";

    public string RemotoHostToken { get; set; } = "";

    public bool RemotoBloqueado { get; set; } = false;
}

/// <summary>Carrega e grava <see cref="AppUserSettings"/>.</summary>
public static class AppSettingsStore
{
    public const string DefaultRemotoServerUrl = "wss://angelus.sisteconsultoria.com.br/remoto-ws";
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    static string SettingsDirectory => SharedMachineStorage.RootPath;

    static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    static AppUserSettings ApplyFixedDefaults(AppUserSettings settings)
    {
        ApplyInstallerEntityId(settings);
        if (string.IsNullOrWhiteSpace(settings.RemotoServerUrl))
            settings.RemotoServerUrl = DefaultRemotoServerUrl;
        else
            settings.RemotoServerUrl = settings.RemotoServerUrl.Trim();

        EnsureRemotoId(settings, persistIfGenerated: false);
        EnsureRemotoHostToken(settings, persistIfGenerated: false);
        return settings;
    }

    /// <summary>
    /// Assegura que o computador possui um Host Token exclusivo (32 bytes em hexadecimal). Se ausente, gera e opcionalmente persiste.
    /// </summary>
    public static string EnsureRemotoHostToken(AppUserSettings settings, bool persistIfGenerated = true)
    {
        var raw = (settings.RemotoHostToken ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            settings.RemotoHostToken = raw;
            return raw;
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        var generated = Convert.ToHexString(bytes).ToLowerInvariant();
        settings.RemotoHostToken = generated;
        if (persistIfGenerated)
        {
            try
            {
                Save(settings);
            }
            catch
            {
            }
        }
        return generated;
    }

    /// <summary>
    /// Assegura que o computador possui um ID fixo exclusivo de 9 dígitos. Se ausente, gera e opcionalmente persiste.
    /// </summary>
    public static string EnsureRemotoId(AppUserSettings settings, bool persistIfGenerated = true)
    {
        var raw = (settings.RemotoId ?? "").Trim().Replace(" ", "").Replace("-", "");
        if (raw.Length >= 8 && raw.Length <= 10 && long.TryParse(raw, out _))
        {
            settings.RemotoId = raw;
            return raw;
        }

        var generated = RandomNumberGenerator.GetInt32(100_000_000, 1_000_000_000).ToString();
        settings.RemotoId = generated;
        if (persistIfGenerated)
        {
            try
            {
                Save(settings);
            }
            catch
            {
            }
        }
        return generated;
    }

    static void ApplyInstallerEntityId(AppUserSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.EntityId))
            return;

        try
        {
            using var machineKey = Registry.LocalMachine.OpenSubKey(@"Software\SistecHub");
            var machineEntityId = machineKey?.GetValue("EntityId") as string;
            if (!string.IsNullOrWhiteSpace(machineEntityId))
            {
                settings.EntityId = machineEntityId.Trim();
                return;
            }

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SistecHub");
            var entityId = key?.GetValue("EntityId") as string;
            if (!string.IsNullOrWhiteSpace(entityId))
                settings.EntityId = entityId.Trim();
        }
        catch
        {
            // Ignora falhas de acesso ao Registro e mantém fluxo normal.
        }
    }

    public static AppUserSettings Load()
    {
        try
        {
            var settings = TryLoadFromDirectory(SettingsDirectory);
            if (!HasConfiguredData(settings))
            {
                var legacy = TryLoadFromDirectory(SharedMachineStorage.LegacyUserDirectory);
                if (HasConfiguredData(legacy))
                {
                    settings = legacy;
                    Save(settings);
                }
            }

            var beforeId = settings.RemotoId;
            var beforeToken = settings.RemotoHostToken;
            var normalized = ApplyFixedDefaults(settings);
            if ((string.IsNullOrWhiteSpace(beforeId) && !string.IsNullOrWhiteSpace(normalized.RemotoId))
                || (string.IsNullOrWhiteSpace(beforeToken) && !string.IsNullOrWhiteSpace(normalized.RemotoHostToken)))
            {
                try { Save(normalized); } catch { }
            }

            return normalized;
        }
        catch
        {
            return ApplyFixedDefaults(new AppUserSettings());
        }
    }

    public static bool IsInitialSetupComplete() => IsInitialSetupComplete(Load());

    public static bool IsInitialSetupComplete(AppUserSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.GlpiUserToken))
            return false;

        if (!int.TryParse(settings.EntityId?.Trim(), out var entityId) || entityId < 1)
            return false;

        return HasGroqApiKeyConfigured(settings);
    }

    static bool HasGroqApiKeyConfigured(AppUserSettings settings)
    {
        var env = Environment.GetEnvironmentVariable("GROQ_API_KEY");
        if (!string.IsNullOrWhiteSpace(env))
            return true;

        return !string.IsNullOrWhiteSpace(settings.GroqApiKey);
    }

    public static void Save(AppUserSettings settings)
    {
        SharedMachineStorage.EnsureDirectory();
        var normalized = ApplyFixedDefaults(settings);
        SecureCredentialStore.Save(
            SettingsDirectory,
            normalized.GlpiUserToken,
            normalized.GroqApiKey);

        WritePublicSettingsFile(
            normalized.EntityId,
            normalized.GlpiMachineId,
            normalized.IniciarMinimizado,
            normalized.RemotoServerUrl,
            normalized.TecnicoNome,
            normalized.TecnicoAvatarBase64,
            normalized.TecnicoGlpiLogin,
            normalized.TecnicoLembrarLogin,
            normalized.RemotoId,
            normalized.RemotoUnattendedPassword,
            normalized.RemotoHostToken,
            normalized.RemotoBloqueado);
    }

    static void WritePublicSettingsFile(
        string entityId,
        string glpiMachineId,
        bool iniciarMinimizado,
        string remotoServerUrl,
        string tecnicoNome = "",
        string tecnicoAvatarBase64 = "",
        string tecnicoGlpiLogin = "",
        bool tecnicoLembrarLogin = false,
        string remotoId = "",
        string remotoUnattendedPassword = "",
        string remotoHostToken = "",
        bool remotoBloqueado = false)
    {
        var json = JsonSerializer.Serialize(
            new PersistedAppSettings
            {
                EntityId = entityId,
                GlpiMachineId = glpiMachineId ?? "",
                IniciarMinimizado = iniciarMinimizado,
                RemotoServerUrl = string.IsNullOrWhiteSpace(remotoServerUrl)
                    ? DefaultRemotoServerUrl
                    : remotoServerUrl.Trim(),
                TecnicoNome = tecnicoNome ?? "",
                TecnicoAvatarBase64 = tecnicoAvatarBase64 ?? "",
                TecnicoGlpiLogin = tecnicoGlpiLogin ?? "",
                TecnicoLembrarLogin = tecnicoLembrarLogin,
                RemotoId = remotoId ?? "",
                RemotoUnattendedPassword = remotoUnattendedPassword ?? "",
                RemotoHostToken = remotoHostToken ?? "",
                RemotoBloqueado = remotoBloqueado,
            },
            JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }

    static AppUserSettings TryLoadFromDirectory(string directory)
    {
        var settings = new AppUserSettings();
        var settingsPath = Path.Combine(directory, "settings.json");
        var usedLegacySecrets = false;
        var needsSettingsSanitize = false;

        if (File.Exists(settingsPath))
        {
            var json = File.ReadAllText(settingsPath);
            var parsed = JsonSerializer.Deserialize<PersistedAppSettings>(json, JsonOptions);
            if (parsed is not null)
            {
                settings.EntityId = parsed.EntityId ?? "";
                settings.GlpiMachineId = parsed.GlpiMachineId ?? "";
                settings.IniciarMinimizado = parsed.IniciarMinimizado;
                settings.RemotoServerUrl = string.IsNullOrWhiteSpace(parsed.RemotoServerUrl)
                    ? DefaultRemotoServerUrl
                    : parsed.RemotoServerUrl.Trim();
                settings.TecnicoNome = parsed.TecnicoNome ?? "";
                settings.TecnicoAvatarBase64 = parsed.TecnicoAvatarBase64 ?? "";
                settings.TecnicoGlpiLogin = parsed.TecnicoGlpiLogin ?? "";
                settings.TecnicoLembrarLogin = parsed.TecnicoLembrarLogin;
                settings.RemotoId = parsed.RemotoId ?? "";
                settings.RemotoUnattendedPassword = parsed.RemotoUnattendedPassword ?? "";
                settings.RemotoHostToken = parsed.RemotoHostToken ?? "";
                settings.RemotoBloqueado = parsed.RemotoBloqueado;
            }

            var legacySecrets = TryReadLegacySecrets(json);
            if (!string.IsNullOrEmpty(legacySecrets.GlpiUserToken)
                || !string.IsNullOrEmpty(legacySecrets.GroqApiKey))
            {
                settings.GlpiUserToken = legacySecrets.GlpiUserToken;
                settings.GroqApiKey = legacySecrets.GroqApiKey;
                usedLegacySecrets = true;
            }

            if (directory == SettingsDirectory && SettingsFileContainsLegacySecretKeys(json))
                needsSettingsSanitize = true;
        }

        var (secureGlpi, secureGroq) = SecureCredentialStore.Load(directory);
        if (!string.IsNullOrEmpty(secureGlpi) || !string.IsNullOrEmpty(secureGroq))
        {
            settings.GlpiUserToken = secureGlpi;
            settings.GroqApiKey = secureGroq;
            usedLegacySecrets = false;
        }

        if (usedLegacySecrets && directory == SettingsDirectory)
            Save(settings);
        else if (needsSettingsSanitize)
            WritePublicSettingsFile(
                settings.EntityId,
                settings.GlpiMachineId,
                settings.IniciarMinimizado,
                settings.RemotoServerUrl,
                settings.TecnicoNome,
                settings.TecnicoAvatarBase64,
                settings.TecnicoGlpiLogin,
                settings.TecnicoLembrarLogin,
                settings.RemotoId,
                settings.RemotoUnattendedPassword,
                settings.RemotoHostToken,
                settings.RemotoBloqueado);

        return settings;
    }

    static bool SettingsFileContainsLegacySecretKeys(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return root.TryGetProperty("glpiPassword", out _)
                || root.TryGetProperty("glpiUserToken", out _)
                || root.TryGetProperty("groqApiKey", out _);
        }
        catch
        {
            return false;
        }
    }

    static bool HasConfiguredData(AppUserSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.EntityId)
        || !string.IsNullOrWhiteSpace(settings.GlpiUserToken)
        || !string.IsNullOrWhiteSpace(settings.GroqApiKey);

    static (string GlpiUserToken, string GroqApiKey) TryReadLegacySecrets(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var glpi = root.TryGetProperty("glpiUserToken", out var tokenEl)
                ? tokenEl.GetString()?.Trim() ?? ""
                : root.TryGetProperty("glpiPassword", out var passEl)
                    ? passEl.GetString()?.Trim() ?? ""
                    : "";
            var groq = root.TryGetProperty("groqApiKey", out var groqEl) ? groqEl.GetString()?.Trim() ?? "" : "";
            return (glpi, groq);
        }
        catch
        {
            return ("", "");
        }
    }
}
