using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.IA;

namespace SistecHub;

/// <summary>Validação remota das credenciais GLPI e Groq antes de gravar / arrancar.</summary>
public static class AppConfigurationValidation
{
    public static Task ValidateGlpiUserTokenAsync(AppUserSettings settings, CancellationToken cancellationToken = default) =>
        GlpiApiClient.ValidateGlpiUserTokenAsync(settings, cancellationToken);

    public static Task ValidateGlpiAndEntityAsync(AppUserSettings settings, CancellationToken cancellationToken = default) =>
        GlpiApiClient.ValidateGlpiAndEntityAsync(settings, cancellationToken);

    public static async Task SyncGroqApiKeyFromGlpiAsync(
        AppUserSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GROQ_API_KEY")))
            return;

        var key = await GlpiApiClient.GetGroqApiKeyFromPluginAsync(settings, cancellationToken)
            .ConfigureAwait(false);
        settings.GroqApiKey = key;
    }

    public static async Task ValidateAllAsync(AppUserSettings settings, CancellationToken cancellationToken = default)
    {
        await GlpiApiClient.ValidateGlpiAndEntityAsync(settings, cancellationToken).ConfigureAwait(false);
        await SyncGroqApiKeyFromGlpiAsync(settings, cancellationToken).ConfigureAwait(false);
        await GroqClient.ValidateApiKeyConnectionAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
