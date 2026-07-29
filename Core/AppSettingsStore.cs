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
}

/// <summary>Conteúdo público persistido em <c>settings.json</c> (sem credenciais).</summary>
sealed class PersistedAppSettings
{
    public string EntityId { get; set; } = "";

    public string GlpiMachineId { get; set; } = "";
}

/// <summary>Carrega e grava <see cref="AppUserSettings"/>.</summary>
public static class AppSettingsStore
{
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
        return settings;
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

            return ApplyFixedDefaults(settings);
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

        WritePublicSettingsFile(normalized.EntityId, normalized.GlpiMachineId);
    }

    static void WritePublicSettingsFile(string entityId, string glpiMachineId)
    {
        var json = JsonSerializer.Serialize(
            new PersistedAppSettings
            {
                EntityId = entityId,
                GlpiMachineId = glpiMachineId ?? "",
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
            WritePublicSettingsFile(settings.EntityId, settings.GlpiMachineId);

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
