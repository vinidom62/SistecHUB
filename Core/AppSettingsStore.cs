using System.Text.Json;
using Microsoft.Win32;

namespace SistecHub.Core;

/// <summary>Definições persistidas em disco (JSON em AppData).</summary>
public sealed class AppUserSettings
{
    public const string DefaultGlpiBaseUrl = "https://sistecsistema.online/angelus";
    public const string DefaultGroqModel = "openai/gpt-oss-120b";
    public const double DefaultGroqTemperature = 0.5;
    public const string DefaultGroqApiBaseUrl = "";

    public string EntityId { get; set; } = "";

    /// <summary>URL base da instância GLPI (ex.: https://dominio/caminho, sem /apirest.php).</summary>
    public string GlpiBaseUrl { get; set; } = DefaultGlpiBaseUrl;

    public string GlpiAppToken { get; set; } = "";

    public string GlpiUserToken { get; set; } = "";

    /// <summary>Chave API Groq (preferir variável de ambiente GROQ_API_KEY).</summary>
    public string GroqApiKey { get; set; } = "";

    /// <summary>Modelo padrão Groq (ex.: openai/gpt-oss-120b).</summary>
    public string GroqModel { get; set; } = DefaultGroqModel;

    /// <summary>Temperatura padrão (0–2, conforme documentação do modelo).</summary>
    public double GroqTemperature { get; set; } = DefaultGroqTemperature;

    /// <summary>URL completa do endpoint de chat (opcional; predefinido: API OpenAI-compatível da Groq).</summary>
    public string GroqApiBaseUrl { get; set; } = "";
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

    static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SistecHub");

    static string SettingsFilePath => Path.Combine(SettingsDirectory, "settings.json");

    static AppUserSettings ApplyFixedDefaults(AppUserSettings settings)
    {
        settings.GlpiBaseUrl = AppUserSettings.DefaultGlpiBaseUrl;
        settings.GroqModel = AppUserSettings.DefaultGroqModel;
        settings.GroqTemperature = AppUserSettings.DefaultGroqTemperature;
        settings.GroqApiBaseUrl = AppUserSettings.DefaultGroqApiBaseUrl;
        ApplyInstallerEntityId(settings);
        return settings;
    }

    static void ApplyInstallerEntityId(AppUserSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.EntityId))
            return;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\SistecHub");
            var entityId = key?.GetValue("EntityId") as string;
            if (!string.IsNullOrWhiteSpace(entityId))
            {
                settings.EntityId = entityId.Trim();
                return;
            }

            using var machineKey = Registry.LocalMachine.OpenSubKey(@"Software\SistecHub");
            var machineEntityId = machineKey?.GetValue("EntityId") as string;
            if (!string.IsNullOrWhiteSpace(machineEntityId))
                settings.EntityId = machineEntityId.Trim();
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
            if (!File.Exists(SettingsFilePath))
                return ApplyFixedDefaults(new AppUserSettings());

            var json = File.ReadAllText(SettingsFilePath);
            var parsed = JsonSerializer.Deserialize<AppUserSettings>(json, JsonOptions);
            var s = parsed ?? new AppUserSettings();
            return ApplyFixedDefaults(s);
        }
        catch
        {
            return ApplyFixedDefaults(new AppUserSettings());
        }
    }

    public static void Save(AppUserSettings settings)
    {
        Directory.CreateDirectory(SettingsDirectory);
        var json = JsonSerializer.Serialize(ApplyFixedDefaults(settings), JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
