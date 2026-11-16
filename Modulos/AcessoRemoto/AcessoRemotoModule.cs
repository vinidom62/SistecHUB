using SistecHub.Core;

namespace SistecHub.Modulos.AcessoRemoto;

public sealed class AcessoRemotoModule : IAppModule
{
    public string Id => "acesso-remoto";

    public string MenuText => "Acesso remoto";

    public UserControl CreateContentView() => new AcessoRemotoView();
}

