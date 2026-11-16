using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;

namespace SistecHub.Modulos.AcessoRemoto;

/// <summary>
/// Envia logs de início/tentativa de sessão de acesso remoto para a API do Angelus/SistecHub.
/// </summary>
public static class AcessoRemotoLogService
{
    const string LogEndpointUrl = "https://angelus.sisteconsultoria.com.br/angelus/plugins/sistechub/front/api.php/remote-access/logs";
    const string AppToken = "HIdUB6NQVzatXVpLNlQCSZAUKtMhVQm97mRHErZ8";

    static readonly HttpClient _httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    /// <summary>
    /// Envia o log de tentativa/início de sessão remota para o servidor Angelus em segundo plano.
    /// </summary>
    /// <param name="targetId">ID da máquina remota alvo.</param>
    public static async Task SendLogAsync(string targetId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(targetId))
                return;

            var formattedTargetId = AcessoRemotoAgentService.FormatId(targetId);

            object payload;
            if (GlpiTechnicianSession.IsAuthenticated)
            {
                var operatorLogin = !string.IsNullOrWhiteSpace(GlpiTechnicianSession.TechnicianLogin)
                    ? GlpiTechnicianSession.TechnicianLogin
                    : GlpiTechnicianSession.TechnicianName;

                payload = new
                {
                    target_id = formattedTargetId,
                    @operator = operatorLogin
                };
            }
            else
            {
                payload = new
                {
                    target_id = formattedTargetId,
                    client_machine = Environment.MachineName,
                    client_user = Environment.UserName
                };
            }

            var json = JsonSerializer.Serialize(payload);
            using var req = new HttpRequestMessage(HttpMethod.Post, LogEndpointUrl)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            req.Headers.TryAddWithoutValidation("App-Token", AppToken);

            using var resp = await _httpClient.SendAsync(req).ConfigureAwait(false);
            AppDebugLog.Info("AcessoRemoto", $"Log de acesso remoto enviado para Angelus ({formattedTargetId}): HTTP {(int)resp.StatusCode}");
        }
        catch (Exception ex)
        {
            AppDebugLog.Warn("AcessoRemoto", $"Falha ao enviar log de acesso remoto para Angelus: {ex.Message}");
        }
    }
}
