using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using SistecHub.Core;
using SistecHub.Modulos.Inventario;

namespace SistecHub.Service;

/// <summary>Coleta inventário com privilégios do serviço e envia ao GLPI periodicamente.</summary>
public sealed class InventarioWorker
{
    static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(15);
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    static readonly TimeSpan CollectInterval = TimeSpan.FromMinutes(30);
    static readonly TimeSpan UploadInterval = TimeSpan.FromMinutes(30);

    static readonly JsonSerializerOptions ReportJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    readonly ILogger<InventarioWorker> _logger;
    readonly SemaphoreSlim _gate = new(1, 1);

    public InventarioWorker(ILogger<InventarioWorker> logger) =>
        _logger = logger;

    public async Task RunLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Inventário worker activo. Coleta: {Collect}min | Upload: {Upload}min.",
            CollectInterval.TotalMinutes,
            UploadInterval.TotalMinutes);

        await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

        try
        {
            // Safety net para clientes antigos quando o ServiceSetup não correu ou o setup não vinha no pacote.
            PawnIoInstaller.EnsureInstalled();
            await EnsureMachineRegisteredAsync(stoppingToken).ConfigureAwait(false);
            var initSnapshot = await CollectAndPersistAsync(stoppingToken).ConfigureAwait(false);
            await TryUploadAsync(initSnapshot, force: true, stoppingToken).ConfigureAwait(false);
            MemoryOptimizer.TrimWorkingSet();
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Arranque do inventário falhou.");
            WriteErrorStatus(ex.Message);
        }

        var lastCollect = DateTime.UtcNow;
        var lastUpload = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            var refreshRequested = InventarioServiceCoordinator.TryConsumeRefreshRequest();
            var uploadRequested = InventarioServiceCoordinator.TryConsumeUploadRequest();
            var collectDue = DateTime.UtcNow - lastCollect >= CollectInterval;
            var uploadDue = DateTime.UtcNow - lastUpload >= UploadInterval;

            if (refreshRequested || uploadRequested)
            {
                var s = AppSettingsStore.Load();
                if (AppSettingsStore.IsInitialSetupComplete(s) && !InventarioMachineRegistration.HasMachineId(s))
                {
                    try
                    {
                        await EnsureMachineRegisteredAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Tentativa de registo de máquina no loop falhou.");
                    }
                }
            }

            InventarioHardwareSnapshot? latestSnapshot = null;
            var didWork = false;

            if (refreshRequested || collectDue || uploadRequested || uploadDue)
            {
                if (refreshRequested)
                {
                    InventarioMonitorOsReader.InvalidateCache();
                    InventarioPostoReader.InvalidateCache();
                }

                try
                {
                    latestSnapshot = await CollectAndPersistAsync(stoppingToken).ConfigureAwait(false);
                    didWork = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Coleta de inventário falhou.");
                    WriteErrorStatus(ex.Message);
                }
                finally
                {
                    lastCollect = DateTime.UtcNow;
                }
            }

            if (uploadRequested || uploadDue)
            {
                try
                {
                    var uploaded = await TryUploadAsync(latestSnapshot, force: uploadRequested, stoppingToken)
                        .ConfigureAwait(false);
                    if (uploaded || uploadDue)
                        lastUpload = DateTime.UtcNow;
                    didWork = true;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Upload de inventário falhou.");
                    WriteErrorStatus(ex.Message);
                    if (uploadDue)
                        lastUpload = DateTime.UtcNow;
                }
            }

            if (didWork)
            {
                MemoryOptimizer.TrimWorkingSet();
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    async Task EnsureMachineRegisteredAsync(CancellationToken cancellationToken)
    {
        var settings = AppSettingsStore.Load();
        if (InventarioMachineRegistration.HasMachineId(settings))
            return;

        if (!AppSettingsStore.IsInitialSetupComplete(settings))
        {
            _logger.LogInformation("Inventário: setup incompleto — registo de máquina adiado.");
            return;
        }

        InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
        {
            Phase = InventarioServicePhase.Collecting,
            Message = "A registar máquina no GLPI…",
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        var createdId = await InventarioMachineRegistration.EnsureRegisteredAsync(cancellationToken)
            .ConfigureAwait(false);

        if (createdId is int id)
        {
            _logger.LogInformation("Máquina registada no GLPI com ID {MachineId}.", id);
            InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
            {
                Phase = InventarioServicePhase.Registered,
                Message = $"Máquina inventáriada, ID: {id}",
                NewlyRegisteredMachineId = id,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
    }

    async Task<InventarioHardwareSnapshot?> CollectAndPersistAsync(CancellationToken cancellationToken)
    {
        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return null;

        try
        {
            InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
            {
                Phase = InventarioServicePhase.Collecting,
                Message = "A recolher inventário…",
                LastCollectUtc = InventarioServiceCoordinator.TryReadStatus()?.LastCollectUtc,
                LastUploadUtc = InventarioServiceCoordinator.TryReadStatus()?.LastUploadUtc,
                NewlyRegisteredMachineId = InventarioServiceCoordinator.TryReadStatus()?.NewlyRegisteredMachineId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            var snapshot = await Task.Run(InventarioHardwareReader.ReadInventory, cancellationToken)
                .ConfigureAwait(false);

            var settings = AppSettingsStore.Load();
            var entidade = int.TryParse(settings.EntityId?.Trim(), out var entityId) && entityId > 0
                ? entityId
                : 0;
            var report = InventarioRelatorioJson.FromSnapshot(snapshot, entidade);
            var reportJson = JsonSerializer.Serialize(report, ReportJsonOptions);

            InventarioServiceCoordinator.WriteReportJson(reportJson);
            InventarioServiceCoordinator.WriteUiSnapshot(ToUiSnapshot(snapshot));

            var prev = InventarioServiceCoordinator.TryReadStatus();
            InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
            {
                Phase = InventarioServicePhase.Idle,
                Message = "Inventário actualizado.",
                LastCollectUtc = DateTimeOffset.UtcNow,
                LastUploadUtc = prev?.LastUploadUtc,
                NewlyRegisteredMachineId = prev?.NewlyRegisteredMachineId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            return snapshot;
        }
        finally
        {
            _gate.Release();
        }
    }

    async Task<bool> TryUploadAsync(InventarioHardwareSnapshot? preCollectedSnapshot, bool force, CancellationToken cancellationToken)
    {
        var settings = AppSettingsStore.Load();
        if (!AppSettingsStore.IsInitialSetupComplete(settings))
            return false;

        if (InventarioPluginPayloadJson.ParsePluginMachineId(settings.GlpiMachineId) <= 0)
        {
            await EnsureMachineRegisteredAsync(cancellationToken).ConfigureAwait(false);
            settings = AppSettingsStore.Load();
        }

        if (InventarioPluginPayloadJson.ParsePluginMachineId(settings.GlpiMachineId) <= 0)
        {
            if (force)
                _logger.LogWarning("Upload de inventário pedido sem ID de máquina.");
            return false;
        }

        if (!await _gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return false;

        try
        {
            var prev = InventarioServiceCoordinator.TryReadStatus();
            InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
            {
                Phase = InventarioServicePhase.Uploading,
                Message = "A enviar inventário ao servidor…",
                LastCollectUtc = prev?.LastCollectUtc,
                LastUploadUtc = prev?.LastUploadUtc,
                NewlyRegisteredMachineId = prev?.NewlyRegisteredMachineId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            var snapshot = preCollectedSnapshot ?? await Task.Run(InventarioHardwareReader.ReadInventory, cancellationToken)
                .ConfigureAwait(false);

            _ = await InventarioGlpiInventoryUpload.PostInventoryPayloadAsync(snapshot, settings, cancellationToken)
                .ConfigureAwait(false);

            InventarioServiceCoordinator.WriteUiSnapshot(ToUiSnapshot(snapshot));

            var entidade = int.TryParse(settings.EntityId?.Trim(), out var entityId) && entityId > 0
                ? entityId
                : 0;
            var report = InventarioRelatorioJson.FromSnapshot(snapshot, entidade);
            InventarioServiceCoordinator.WriteReportJson(JsonSerializer.Serialize(report, ReportJsonOptions));

            InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
            {
                Phase = InventarioServicePhase.Idle,
                Message = "Inventário enviado.",
                LastCollectUtc = DateTimeOffset.UtcNow,
                LastUploadUtc = DateTimeOffset.UtcNow,
                NewlyRegisteredMachineId = prev?.NewlyRegisteredMachineId,
                UpdatedAt = DateTimeOffset.UtcNow,
            });

            _logger.LogInformation("Inventário enviado ao GLPI.");
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    void WriteErrorStatus(string error)
    {
        var prev = InventarioServiceCoordinator.TryReadStatus();
        InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
        {
            Phase = InventarioServicePhase.Error,
            Message = "Erro: " + error,
            LastCollectUtc = prev?.LastCollectUtc,
            LastUploadUtc = prev?.LastUploadUtc,
            NewlyRegisteredMachineId = prev?.NewlyRegisteredMachineId,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
    }

    static InventarioUiSnapshot ToUiSnapshot(in InventarioHardwareSnapshot snapshot) =>
        new()
        {
            Cpu = snapshot.Cpu,
            Ram = snapshot.Ram,
            Gpu = snapshot.Gpu,
            Motherboard = snapshot.Motherboard,
            CpuTemperatureLine = snapshot.CpuTemperatureLine,
            RamUsageLine = snapshot.RamUsageLine,
            GpuTemperatureLine = snapshot.GpuTemperatureLine,
            MotherboardSerialLine = snapshot.MotherboardSerialLine,
            OsNome = snapshot.SistemaOperacional.NomeProduto,
            OsVersao = snapshot.SistemaOperacional.VersaoAtual,
            OsArquitetura = snapshot.SistemaOperacional.Arquitetura,
            OsStatusAtivacao = snapshot.SistemaOperacional.StatusAtivacao,
            OsChaveAtivacao = snapshot.SistemaOperacional.ChaveAtivacao,
            OsCanalLicenca = snapshot.SistemaOperacional.CanalLicenca,
            Discos = snapshot.DiscosRigidos
                .Select(d => new InventarioUiDiscoSnapshot
                {
                    Nome = d.Nome,
                    Tipo = d.Tipo,
                    NumeroSerie = d.NumeroSerie,
                    Saude = d.Saude,
                    VidaPercent = d.VidaPercent,
                    ArmazenamentoTotalGb = d.ArmazenamentoTotalGb,
                    ArmazenamentoUsadoGb = d.ArmazenamentoUsadoGb,
                })
                .ToArray(),
            CollectedAt = DateTimeOffset.UtcNow,
        };
}
