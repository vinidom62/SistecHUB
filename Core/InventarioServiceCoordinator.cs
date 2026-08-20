using System.Text.Json;
using System.Text.Json.Serialization;

namespace SistecHub.Core;

/// <summary>Pedidos e estado de inventário partilhados entre app e serviço (ProgramData).</summary>
public static class InventarioServiceCoordinator
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string UploadRequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "inventario-upload.request");

    public static string RefreshRequestFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "inventario-refresh.request");

    public static string StatusFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "inventario-status.json");

    public static string UiSnapshotFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "inventario-ui.json");

    public static string ReportFilePath =>
        Path.Combine(SharedMachineStorage.RootPath, "inventario-report.json");

    public static void RequestUpload()
    {
        SharedMachineStorage.EnsureDirectory();
        File.WriteAllText(UploadRequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    public static void RequestRefresh()
    {
        SharedMachineStorage.EnsureDirectory();
        File.WriteAllText(RefreshRequestFilePath, DateTimeOffset.UtcNow.ToString("O"));
    }

    public static bool TryConsumeUploadRequest() => TryConsumeFile(UploadRequestFilePath);

    public static bool TryConsumeRefreshRequest() => TryConsumeFile(RefreshRequestFilePath);

    public static void WriteStatus(InventarioServiceStatus status)
    {
        SharedMachineStorage.EnsureDirectory();
        var json = JsonSerializer.Serialize(status, JsonOptions);
        File.WriteAllText(StatusFilePath, json);
    }

    public static InventarioServiceStatus? TryReadStatus()
    {
        try
        {
            if (!File.Exists(StatusFilePath))
                return null;
            var json = File.ReadAllText(StatusFilePath);
            return JsonSerializer.Deserialize<InventarioServiceStatus>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteUiSnapshot(InventarioUiSnapshot snapshot)
    {
        SharedMachineStorage.EnsureDirectory();
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        File.WriteAllText(UiSnapshotFilePath, json);
    }

    public static InventarioUiSnapshot? TryReadUiSnapshot()
    {
        try
        {
            if (!File.Exists(UiSnapshotFilePath))
                return null;
            var json = File.ReadAllText(UiSnapshotFilePath);
            return JsonSerializer.Deserialize<InventarioUiSnapshot>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void WriteReportJson(string inventoryJson)
    {
        SharedMachineStorage.EnsureDirectory();
        File.WriteAllText(ReportFilePath, inventoryJson);
    }

    public static string? TryReadReportJson()
    {
        try
        {
            return File.Exists(ReportFilePath) ? File.ReadAllText(ReportFilePath) : null;
        }
        catch
        {
            return null;
        }
    }

    static bool TryConsumeFile(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public enum InventarioServicePhase
{
    Idle,
    Collecting,
    Uploading,
    Registered,
    Error,
}

public sealed class InventarioServiceStatus
{
    public InventarioServicePhase Phase { get; set; } = InventarioServicePhase.Idle;

    public string Message { get; set; } = "";

    public DateTimeOffset? LastCollectUtc { get; set; }

    public DateTimeOffset? LastUploadUtc { get; set; }

    /// <summary>Preenchido uma vez quando o serviço cria o ID da máquina; a UI mostra o aviso e limpa.</summary>
    public int? NewlyRegisteredMachineId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Campos de cartão da UI, gravados pelo serviço após coleta elevada.</summary>
public sealed class InventarioUiSnapshot
{
    public string Cpu { get; set; } = "—";

    public string Ram { get; set; } = "—";

    public string Gpu { get; set; } = "—";

    public string Motherboard { get; set; } = "—";

    public string CpuTemperatureLine { get; set; } = "Temperatura: —";

    public string RamUsageLine { get; set; } = "Uso: —";

    public string GpuTemperatureLine { get; set; } = "Temperatura: —";

    public string MotherboardSerialLine { get; set; } = "N.º de série: —";

    public InventarioUiDiscoSnapshot[] Discos { get; set; } = [];

    public DateTimeOffset CollectedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Disco no snapshot da HUD (serviço → app).</summary>
public sealed class InventarioUiDiscoSnapshot
{
    public string Nome { get; set; } = "";

    public string Tipo { get; set; } = "";

    public string? NumeroSerie { get; set; }

    public string Saude { get; set; } = "desconhecida";

    public float? VidaPercent { get; set; }

    public float? ArmazenamentoTotalGb { get; set; }

    public float? ArmazenamentoUsadoGb { get; set; }
}
