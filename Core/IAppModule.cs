namespace SistecHub.Core;

/// <summary>
/// Contrato de um módulo carregável na área principal da aplicação.
/// </summary>
public interface IAppModule
{
    string Id { get; }
    string MenuText { get; }
    UserControl CreateContentView();
}
