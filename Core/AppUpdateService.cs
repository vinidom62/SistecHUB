using Velopack;
using Velopack.Sources;

namespace SistecHub.Core;

/// <summary>Verificação e instalação de atualizações via Velopack (GitHub Releases).</summary>
public static class AppUpdateService
{
    static UpdateManager? _manager;

    static UpdateManager Manager => _manager ??= new UpdateManager(
        new GithubSource(AppReleaseConfig.GitHubRepoUrl, accessToken: null, prerelease: false));

    /// <summary>True quando o app foi instalado pelo instalador Velopack (não em debug direto).</summary>
    public static bool IsUpdateSupported => Manager.IsInstalled;

    public static string DisplayVersion =>
        IsUpdateSupported && Manager.CurrentVersion is { } v
            ? v.ToString()
            : AppVersion.Current;

    public static async Task CheckAndPromptAsync(
        IWin32Window? owner,
        bool silentIfUpToDate,
        CancellationToken cancellationToken = default)
    {
        if (!IsUpdateSupported)
        {
            if (!silentIfUpToDate)
            {
                MessageBox.Show(
                    owner,
                    "Atualizações automáticas só funcionam quando o SistecHub foi instalado pelo instalador (.msi ou Setup.exe).\n\n"
                    + "Se estiver a testar com dotnet run ou a copiar ficheiros manualmente, reinstale pela release no GitHub.",
                    "Atualização",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        if (Manager.UpdatePendingRestart is { } pending)
        {
            var pendingAnswer = MessageBox.Show(
                owner,
                "Há uma atualização pronta para instalar. Reiniciar o SistecHub agora?",
                "Atualização pendente",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (pendingAnswer == DialogResult.Yes)
                Manager.ApplyUpdatesAndRestart(pending);
            return;
        }

        UpdateInfo? update;
        try
        {
            update = await Manager.CheckForUpdatesAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            if (!silentIfUpToDate)
            {
                MessageBox.Show(
                    owner,
                    $"Não foi possível verificar atualizações.\n\n{ex.Message}",
                    "Atualização",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            return;
        }

        if (update is null)
        {
            if (!silentIfUpToDate)
            {
                MessageBox.Show(
                    owner,
                    $"O SistecHub já está na versão mais recente ({DisplayVersion}).\n\n"
                    + "Se esperava uma versão nova, confira no GitHub se o .nupkg publicado tem a versão correta "
                    + "(releases.win.json deve coincidir com a tag da release).",
                    "Atualização",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            return;
        }

        var newVersion = update.TargetFullRelease.Version.ToString();
        var answer = MessageBox.Show(
            owner,
            $"Está disponível a versão {newVersion}.\nVersão instalada: {DisplayVersion}.\n\nDeseja baixar e instalar agora?",
            "Atualização disponível",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (answer != DialogResult.Yes)
            return;

        try
        {
            await Manager.DownloadUpdatesAsync(update, progress: null, cancelToken: cancellationToken)
                .ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                owner,
                $"Falha ao baixar a atualização.\n\n{ex.Message}",
                "Atualização",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        var restart = MessageBox.Show(
            owner,
            "Download concluído. Reiniciar agora para aplicar a atualização?",
            "Atualização",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Information);
        if (restart == DialogResult.Yes)
            Manager.ApplyUpdatesAndRestart(update);
    }
}
