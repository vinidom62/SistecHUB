using SistecHub.Core;

namespace SistecHub.Modulos.Inventario;

public sealed class InventarioModule : IAppModule
{
    public string Id => "inventario";

    public string MenuText => "Inventário";

    public UserControl CreateContentView() => new InventarioView();
}
