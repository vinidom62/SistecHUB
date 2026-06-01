namespace SistecHub.Core;

/// <summary>Configuração fixa do Velopack e do repositório de releases.</summary>
public static class AppReleaseConfig
{
    public const string GitHubRepoUrl = "https://github.com/vinidom62/SistecHUB";

    /// <summary>Id único do pacote (deve ser o mesmo em <c>vpk pack --packId</c>).</summary>
    public const string PackId = "Sistec.SistecHub";

    public const string PackTitle = "SistecHub";

    /// <summary>Editor no MSI; pasta em Program Files: <c>Program Files\{PackAuthors}\{PackTitle}</c>.</summary>
    public const string PackAuthors = "Sistec";

    public const string MainExeName = "SistecHub.exe";

    /// <summary>Atalhos do instalador; <c>Startup</c> inicia o app com o Windows.</summary>
    public const string PackShortcuts = "Desktop,StartMenuRoot,Startup";
}
