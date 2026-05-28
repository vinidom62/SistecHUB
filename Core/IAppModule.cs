namespace SistecHub.Core;

/// <summary>
/// Contrato de um módulo carregável na área principal da aplicação.
/// </summary>
public interface IAppModule
{
    string Id { get; }
    string MenuText { get; }

    /// <summary>Ordem na barra lateral (menor = mais acima). O mesmo valor desempata por <see cref="MenuText"/>.</summary>
    int MenuOrder => 100;

    UserControl CreateContentView();
}
