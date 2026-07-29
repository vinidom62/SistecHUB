using System.Text.Encodings.Web;
using System.Text.Json;
using SistecHub.Core;
using SistecHub.Modulos.GLPI;

namespace SistecHub.Modulos.Inventario;

internal static class InventarioGlpiInventoryUpload
{
    public static JsonSerializerOptions CreatePluginPayloadSerializerOptions() =>
        new()
        {
            WriteIndented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        };

    public static async Task<string> PostInventoryPayloadAsync(
        InventarioHardwareSnapshot snapshot,
        AppUserSettings settings,
        CancellationToken cancellationToken = default)
    {
        var machineId = InventarioPluginPayloadJson.ParsePluginMachineId(settings.GlpiMachineId);
        if (machineId <= 0)
        {
            throw new InvalidOperationException(
                "Configure o ID da máquina GLPI em Configurações antes de enviar o inventário.");
        }

        var entidade = int.TryParse(settings.EntityId?.Trim(), out var entityId) && entityId > 0
            ? entityId
            : 0;
        var dto = InventarioPluginPayloadJson.FromSnapshot(snapshot, machineId, entidade);
        var json = JsonSerializer.Serialize(dto, CreatePluginPayloadSerializerOptions());
        return await GlpiApiClient.PostPluginSistechubMachineInventoryAsync(settings, json, cancellationToken)
            .ConfigureAwait(false);
    }
}
