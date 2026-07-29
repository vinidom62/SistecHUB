using SistecHub.Core;

namespace SistecHub.Modulos.Inventario;

/// <summary>Pede ao serviço Windows o envio de inventário (coleta elevada + POST GLPI).</summary>
internal static class InventarioAutoUploadCoordinator
{
    public static void Start()
    {
        // O ciclo de 30 min corre no serviço; aqui só força um envio imediato.
        InventarioServiceCoordinator.RequestUpload();
    }

    public static void Stop()
    {
        // Sem estado local — o worker vive no SistecHub.Service.
    }

    public static void RequestUploadNow() =>
        InventarioServiceCoordinator.RequestUpload();
}
