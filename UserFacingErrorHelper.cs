using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using SistecHub.Core;

namespace SistecHub;

/// <summary>Converte exceções técnicas em textos claros em português e mostra em diálogo.</summary>
public static class UserFacingErrorHelper
{
    public const string ValidationErrorTitle = "Não foi possível validar";

    public static void ShowErrorFromException(IWin32Window? owner, Exception ex, string? title = null)
    {
        AppDebugLog.LogException("Erro", ex, title ?? ValidationErrorTitle);
        MessageBox.Show(
            owner,
            FormatForUser(ex),
            title ?? ValidationErrorTitle,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    /// <summary>Texto seguro para o utilizador, sem jargão desnecessário.</summary>
    public static string FormatForUser(Exception ex)
    {
        ex = Unwrap(ex);

        if (ex is OperationCanceledException or TaskCanceledException)
        {
            return "O pedido excedeu o tempo limite. Verifique a internet e tente de novo.";
        }

        if (ex is HttpRequestException http)
        {
            return FormatHttpRequestException(http, ex);
        }

        if (ex is HttpListenerException or IOException)
        {
            return "Não foi possível completar a ligação de rede. Verifique a internet e o firewall.";
        }

        if (ex is InvalidOperationException)
        {
            return FormatInvalidOperation(ex.Message);
        }

        if (ex is JsonException)
        {
            return "A resposta do servidor não veio no formato esperado. Tente de novo ou verifique a versão do GLPI.";
        }

        var msg = ex?.Message;
        if (string.IsNullOrWhiteSpace(msg))
        {
            return "Ocorreu um erro inesperado. Tente de novo ou contacte o suporte.";
        }

        if (msg.Length > 500)
            msg = msg[..497] + "…";
        return "Não foi possível concluir a operação.\n\n" + msg;
    }

    static Exception Unwrap(Exception ex)
    {
        if (ex is AggregateException agg)
        {
            var flat = agg.Flatten();
            if (flat.InnerExceptions.Count > 0)
                ex = flat.InnerExceptions[0];
        }
        if (ex.InnerException is { } inner && ex is not InvalidOperationException)
        {
            if (inner is HttpRequestException or IOException or TaskCanceledException)
                return inner;
        }
        return ex;
    }

    static string FormatHttpRequestException(HttpRequestException http, Exception original)
    {
        if (http.InnerException is TaskCanceledException)
        {
            return "O pedido excedeu o tempo limite. Verifique a internet e tente de novo.";
        }

        var m = (http.Message ?? "") + (http.InnerException?.Message ?? "");
        if (m.Contains("SSL", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("certificado", StringComparison.OrdinalIgnoreCase))
        {
            return "Falha na ligação segura (SSL) com o servidor. " +
                "Verifique a data do computador e o certificado do site do GLPI.";
        }

        if (original.Message.Contains("groq", StringComparison.OrdinalIgnoreCase) ||
            m.Contains("groq", StringComparison.OrdinalIgnoreCase))
        {
            return "Não foi possível contactar a API da Groq. Verifique a internet e a chave de API.";
        }

        return "Não foi possível contactar o servidor (rede). " +
            "Verifique a internet, o firewall e se o endereço do GLPI na configuração está correto.";
    }

    static string FormatInvalidOperation(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "A operação não pôde ser concluída. Verifique os dados e tente de novo.";

        var s = raw;

        if (s.Contains("A URL base do GLPI", StringComparison.OrdinalIgnoreCase))
        {
            return "A ligação ao GLPI não está correta. A URL base do servidor deve estar configurada.";
        }

        if (s.Contains("User token", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Configure o User token", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        if (s.Contains("App token e o User token", StringComparison.OrdinalIgnoreCase) ||
            (s.Contains("App token", StringComparison.OrdinalIgnoreCase) && s.Contains("User token", StringComparison.OrdinalIgnoreCase)))
        {
            return "São necessários o App token e o User token do GLPI. " +
                "Confirme o user token e se a API REST está ativa no servidor.";
        }

        if (s.Contains("Não existe entidade com o id", StringComparison.OrdinalIgnoreCase) ||
            s.Contains("Não foi encontrada uma entidade", StringComparison.OrdinalIgnoreCase))
        {
            return "Não existe nenhuma entidade no GLPI com o ID que indicou. " +
                "No GLPI, em Configurar > Entidades, confira o número (ID) da entidade e use o mesmo aqui.";
        }

        if (s.Contains("O ID da entidade tem de ser", StringComparison.OrdinalIgnoreCase) ||
            (s.Contains("número inteiro", StringComparison.OrdinalIgnoreCase) && s.Contains("entidade", StringComparison.OrdinalIgnoreCase)))
        {
            return s;
        }

        if (s.Contains("session_token", StringComparison.OrdinalIgnoreCase) ||
            (s.Contains("sessão GLPI", StringComparison.OrdinalIgnoreCase) && s.Contains("Falha", StringComparison.OrdinalIgnoreCase)))
        {
            if (s.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) || s.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase))
            {
                return "O GLPI recusou o acesso (user token inválido ou API desativada). " +
                    "Confirme o User token e se a API REST está ativa em Configurar → Geral → API.";
            }
            return "Não foi possível abrir sessão no GLPI. " +
                "Verifique o User token e se a API REST está ativa no servidor.";
        }

        if ((s.Contains("Falha na", StringComparison.OrdinalIgnoreCase) && s.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase)) ||
            (s.Contains("HTTP 401", StringComparison.OrdinalIgnoreCase) && s.Contains("GLPI", StringComparison.OrdinalIgnoreCase)))
        {
            return "O GLPI recusou o acesso: user token inválido ou sessão expirada. " +
                "Atualize o User token nas configurações.";
        }

        if (s.Contains("HTTP 403", StringComparison.OrdinalIgnoreCase) && s.Contains("GLPI", StringComparison.OrdinalIgnoreCase))
        {
            return "O GLPI recusou esta operação (sem permissão). " +
                "Verifique o perfil associado ao User token no GLPI.";
        }

        if (s.Contains("consulta Entity", StringComparison.OrdinalIgnoreCase) ||
            (s.Contains("Falha na", StringComparison.OrdinalIgnoreCase) && s.Contains("Entity", StringComparison.Ordinal)))
        {
            if (s.Contains("HTTP 404", StringComparison.OrdinalIgnoreCase) || s.Contains("NotFound", StringComparison.OrdinalIgnoreCase))
            {
                return "Não foi encontrada a entidade com o ID indicado. Confira o ID no GLPI.";
            }
        }

        if (s.Contains("Groq", StringComparison.OrdinalIgnoreCase) || s.Contains("groq", StringComparison.OrdinalIgnoreCase))
        {
            if (s.Contains("401", StringComparison.OrdinalIgnoreCase) || s.Contains("não autorizada", StringComparison.OrdinalIgnoreCase) || s.Contains("recusada", StringComparison.OrdinalIgnoreCase))
            {
                return "A chave da API Groq foi recusada. " +
                    "Verifique a configuração do plugin Groq SistecHub no GLPI.";
            }
            if (s.Contains("HTTP", StringComparison.OrdinalIgnoreCase))
            {
                return "O serviço de IA (Groq) devolveu um erro. Verifique a chave e tente mais tarde.";
            }
        }

        if (s.Contains("GROQ_API_KEY", StringComparison.OrdinalIgnoreCase) && s.Contains("Defina", StringComparison.OrdinalIgnoreCase))
        {
            return "Não foi possível obter a chave Groq. Configure o plugin Groq SistecHub no GLPI ou defina GROQ_API_KEY.";
        }

        if (s.Contains("plugin Groq", StringComparison.OrdinalIgnoreCase)
            || s.Contains("PluginGroqSistechubconfig", StringComparison.OrdinalIgnoreCase)
            || s.Contains("configuração Groq", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        if (s.Contains("Resposta: {", StringComparison.OrdinalIgnoreCase) || s.Contains("Resposta: [", StringComparison.OrdinalIgnoreCase) ||
            (s.Contains("Falha na", StringComparison.OrdinalIgnoreCase) && s.Length > 200))
        {
            return "O GLPI respondeu com um erro. Verifique os tokens, a URL e se a API REST está acessível. " +
                "Se o problema continuar, peça ajuda ao administrador do GLPI.";
        }

        if (s.Contains("Falha na", StringComparison.OrdinalIgnoreCase) && s.Contains("HTTP", StringComparison.OrdinalIgnoreCase))
        {
            return "O servidor GLPI respondeu com erro. Verifique a ligação, os tokens e se o serviço GLPI está online.";
        }

        if (s.Length < 200 && !s.Contains("http", StringComparison.OrdinalIgnoreCase))
        {
            return s;
        }

        return "Não foi possível validar a configuração. " +
            "Se precisar de detalhe técnico: " + TruncateOneLine(s, 280);
    }

    static string TruncateOneLine(string s, int max)
    {
        s = s.Replace('\r', ' ').Replace('\n', ' ');
        if (s.Length <= max) return s;
        return s[..max] + "…";
    }
}
