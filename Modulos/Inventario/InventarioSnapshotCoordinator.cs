using SistecHub.Core;

namespace SistecHub.Modulos.Inventario;

/// <summary>Consome o snapshot de inventário escrito pelo serviço Windows (coleta elevada).</summary>
internal static class InventarioSnapshotCoordinator
{
    public const int RefreshIntervalMs = 2000;

    static readonly object LifecycleLock = new();
    static readonly object SnapshotLock = new();

    static bool _started;
    static System.Windows.Forms.Timer? _timer;
    static InventarioHardwareSnapshot? _latest;
    static DateTimeOffset? _lastUiSnapshotStamp;
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

    static void PollFromService()
    {
        try
        {
            var ui = InventarioServiceCoordinator.TryReadUiSnapshot();
            if (ui is null)
                return;

            if (_lastUiSnapshotStamp == ui.CollectedAt)
                return;

            _lastUiSnapshotStamp = ui.CollectedAt;
            var snap = ToDisplaySnapshot(ui);
            lock (SnapshotLock)
                _latest = snap;
            SnapshotUpdated?.Invoke(null, EventArgs.Empty);
        }
        catch
        {
            // Mantém snapshot anterior.
        }
    }

    static InventarioHardwareSnapshot ToDisplaySnapshot(InventarioUiSnapshot ui) =>
        new(
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
            new SistemaOperacionalInventario("—", "—", "—", null),
            new AcessoRemotoInventario(null),
            new PostoTrabalhoInventario("—", null, "—", null, "—"));
}
