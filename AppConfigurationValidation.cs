using SistecHub.Core;
using SistecHub.Modulos.GLPI;
using SistecHub.Modulos.IA;

namespace SistecHub;

/// <summary>Validação remota das credenciais GLPI e Groq antes de gravar / arrancar.</summary>
public static class AppConfigurationValidation
{
    public static async Task ValidateAllAsync(AppUserSettings settings, CancellationToken cancellationToken = default)
    {
        await GlpiApiClient.ValidateGlpiAndEntityAsync(settings, cancellationToken).ConfigureAwait(false);
        await GroqClient.ValidateApiKeyConnectionAsync(settings, cancellationToken).ConfigureAwait(false);
    }
}
