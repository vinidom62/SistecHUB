using SistecHub.Core;

namespace SistecHub.Modulos.GLPI;

public sealed class GLPIModule : IAppModule
{
    public string Id => "glpi";

    public string MenuText => "Chamados";

    public UserControl CreateContentView() => new GLPIView();
}
