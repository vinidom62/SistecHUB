using SistecHub.Core;

namespace SistecHub.Modulos.Inventario;

/// <summary>Consome o snapshot de inventário escrito pelo serviço Windows (coleta elevada).</summary>
internal static class InventarioSnapshotCoordinator
{
    public const int RefreshIntervalMs = 30000;

    static readonly object LifecycleLock = new();
    static readonly object SnapshotLock = new();

    static bool _started;
    static System.Windows.Forms.Timer? _timer;
    static FileSystemWatcher? _watcher;
    static InventarioHardwareSnapshot? _latest;
    static DateTimeOffset? _lastUiSnapshotStamp;
    static DateTime _lastFileWriteTimeUtc = DateTime.MinValue;
    static int? _lastShownRegisteredMachineId;

    public static event EventHandler? SnapshotUpdated;

    public static InventarioHardwareSnapshot? TryGetLatest()
    {
        lock (SnapshotLock)
            return _latest;
    }

    public static void Start()
    {
        lock (LifecycleLock)
        {
            if (_started)
                return;
            _started = true;
            _timer = new System.Windows.Forms.Timer { Interval = RefreshIntervalMs };
            _timer.Tick += (_, _) => PollFromService();
            _timer.Start();

            try
            {
                var dir = InventarioServiceCoordinator.DataDirectory;
                if (Directory.Exists(dir))
                {
                    _watcher = new FileSystemWatcher(dir, "inventario-ui.json")
                    {
                        NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                        EnableRaisingEvents = true,
                    };
                    _watcher.Changed += (_, _) => PollFromService();
                    _watcher.Created += (_, _) => PollFromService();
                }
            }
            catch
            {
                // Fallback seguro permanece no timer.
            }
        }

        InventarioServiceCoordinator.RequestRefresh();
        PollFromService();
    }

    public static void Stop()
    {
        lock (LifecycleLock)
        {
            if (!_started)
                return;
            _started = false;
            if (_timer is not null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            if (_watcher is not null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }
        }
    }

    /// <summary>Se o serviço registou a máquina, devolve o ID uma vez para a UI mostrar o aviso.</summary>
    public static int? TryConsumeNewlyRegisteredMachineId()
    {
        var status = InventarioServiceCoordinator.TryReadStatus();
        var id = status?.NewlyRegisteredMachineId;
        if (id is null or <= 0)
            return null;

        if (_lastShownRegisteredMachineId == id)
            return null;

        _lastShownRegisteredMachineId = id;

        InventarioServiceCoordinator.WriteStatus(new InventarioServiceStatus
        {
            Phase = status!.Phase,
            Message = status.Message,
            LastCollectUtc = status.LastCollectUtc,
            LastUploadUtc = status.LastUploadUtc,
            NewlyRegisteredMachineId = null,
            UpdatedAt = DateTimeOffset.UtcNow,
        });

        return id;
    }

    public static DateTimeOffset? LastCollectedAt
    {
        get
        {
            lock (SnapshotLock)
                return _lastUiSnapshotStamp;
        }
    }

    public static void RequestRefreshNow()
    {
        InventarioMonitorOsReader.InvalidateCache();
        InventarioPostoReader.InvalidateCache();

        lock (SnapshotLock)
        {
            if (_latest is { } current)
            {
                var freshSo = InventarioMonitorOsReader.ReadSistemaOperacional();
                _latest = current with { SistemaOperacional = freshSo };
                SnapshotUpdated?.Invoke(null, EventArgs.Empty);
            }
        }

        InventarioServiceCoordinator.RequestRefresh();
        PollFromService();
    }

    internal static bool PollFromService()
    {
        try
        {
            var filePath = InventarioServiceCoordinator.UiSnapshotFilePath;
            if (!File.Exists(filePath))
                return false;

            var writeTime = File.GetLastWriteTimeUtc(filePath);
            if (writeTime == _lastFileWriteTimeUtc && _latest is not null)
                return false;

            var ui = InventarioServiceCoordinator.TryReadUiSnapshot();
            if (ui is null)
                return false;

            _lastFileWriteTimeUtc = writeTime;

            if (_lastUiSnapshotStamp == ui.CollectedAt && _latest is not null)
                return false;

            lock (SnapshotLock)
            {
                _lastUiSnapshotStamp = ui.CollectedAt;
                _latest = ToDisplaySnapshot(ui);
            }
            SnapshotUpdated?.Invoke(null, EventArgs.Empty);
            return true;
        }
        catch
        {
            // Mantém snapshot anterior.
            return false;
        }
    }

    static InventarioHardwareSnapshot ToDisplaySnapshot(InventarioUiSnapshot ui)
    {
        var hasValidOs = !string.IsNullOrWhiteSpace(ui.OsNome)
            && ui.OsNome != "—"
            && !string.IsNullOrWhiteSpace(ui.OsStatusAtivacao)
            && ui.OsStatusAtivacao != "Desconhecido";

        var so = hasValidOs
            ? new SistemaOperacionalInventario(
                ui.OsNome,
                ui.OsArquitetura,
                ui.OsVersao,
                null,
                ui.OsStatusAtivacao,
                ui.OsChaveAtivacao,
                ui.OsCanalLicenca)
            : InventarioMonitorOsReader.ReadSistemaOperacional();

        return new(
            ui.Cpu,
            ui.Ram,
            ui.Gpu,
            ui.Motherboard,
            ui.CpuTemperatureLine,
            ui.RamUsageLine,
            ui.GpuTemperatureLine,
            ui.MotherboardSerialLine,
            Array.Empty<MemoriaModuloInventario>(),
            new ProcessadorDetalheInventario(ui.Cpu, null, null, null, null),
            new PlacaMaeDetalheInventario(null, null),
            Array.Empty<PlacaVideoDetalheInventario>(),
            (ui.Discos ?? []).Select(d => new DiscoRigidoInventario(
                string.IsNullOrWhiteSpace(d.Nome) ? "Disco" : d.Nome,
                string.IsNullOrWhiteSpace(d.Tipo) ? "Desconhecido" : d.Tipo,
                d.NumeroSerie,
                string.IsNullOrWhiteSpace(d.Saude) ? "desconhecida" : d.Saude,
                d.VidaPercent,
                d.ArmazenamentoTotalGb,
                d.ArmazenamentoUsadoGb)).ToList(),
            Array.Empty<MonitorInventario>(),
            so,
            new AcessoRemotoInventario(null),
            new PostoTrabalhoInventario("—", null, null, "—", null, "—"));
    }
}
