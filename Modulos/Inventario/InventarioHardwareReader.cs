using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;

namespace SistecHub.Modulos.Inventario;

/// <summary>Um módulo RAM (SMBIOS tipo 17). <c>ArquiteturaMemoria</c>: DDR3, DDR4, DDR5, LPDDR… quando reportado.</summary>
internal sealed record MemoriaModuloInventario(
    string? Localizador,
    string? Banco,
    double CapacidadeGb,
    int? FrequenciaMts,
    string? ArquiteturaMemoria);

/// <summary>Dados do processador recolhidos via sensores LHM + SMBIOS da lib.</summary>
internal sealed record ProcessadorDetalheInventario(
    string Modelo,
    double? Ghz,
    float? TemperaturaC,
    int? Nucleos,
    int? Threads);

/// <summary>Placa-mãe a partir de SMBIOS (LibreHardwareMonitor).</summary>
internal sealed record PlacaMaeDetalheInventario(
    string? NumeroSerie,
    string? Modelo);

/// <summary>Uma GPU física (sensores LHM).</summary>
internal sealed record PlacaVideoDetalheInventario(
    string Nome,
    float? MemoriaGb,
    float? TemperaturaC);

/// <summary>Disco físico (grupo Storage do LibreHardwareMonitor).</summary>
internal sealed record DiscoRigidoInventario(
    string Nome,
    string Tipo,
    string? NumeroSerie,
    float? VidaPercent,
    float? ArmazenamentoTotalGb,
    float? ArmazenamentoUsadoGb);

/// <summary>Monitor físico (WMI EDID).</summary>
internal sealed record MonitorInventario(
    string? Modelo,
    string? NumeroSerie);

/// <summary>Windows (registo + ambiente).</summary>
internal sealed record SistemaOperacionalInventario(
    string NomeProduto,
    string Arquitetura,
    string VersaoAtual,
    string? DataInstalacao);

/// <summary>Ferramentas de acesso remoto detetadas localmente.</summary>
internal sealed record AcessoRemotoInventario(string? AnyDeskId);

/// <summary>Form factor do equipamento, modelo e sessão (WMI / ambiente).</summary>
internal sealed record PostoTrabalhoInventario(
    string TipoComputador,
    string? ModeloComputador,
    string Utilizador,
    string? Dominio,
    string UtilizadorDominio);

/// <summary>Resultado de <see cref="InventarioHardwareReader.ReadInventory"/>.</summary>
internal readonly record struct InventarioHardwareSnapshot(
    string Cpu,
    string Ram,
    string Gpu,
    string Motherboard,
    string CpuTemperatureLine,
    string RamUsageLine,
    string GpuTemperatureLine,
    string MotherboardSerialLine,
    IReadOnlyList<MemoriaModuloInventario> ModulosMemoria,
    ProcessadorDetalheInventario ProcessadorInfo,
    PlacaMaeDetalheInventario PlacaMaeInfo,
    IReadOnlyList<PlacaVideoDetalheInventario> PlacasVideo,
    IReadOnlyList<DiscoRigidoInventario> DiscosRigidos,
    IReadOnlyList<MonitorInventario> Monitores,
    SistemaOperacionalInventario SistemaOperacional,
    AcessoRemotoInventario AcessoRemoto,
    PostoTrabalhoInventario PostoTrabalho);

/// <summary>Lê informação de hardware via <see cref="LibreHardwareMonitor.Hardware.Computer"/> (LibreHardwareMonitorLib).</summary>
internal static class InventarioHardwareReader
{
    static readonly string[] GpuNameExcludeSubstrings =
    {
        "microsoft basic",
        "microsoft remote display",
        "virtual display",
        "parsec virtual",
        "sunlogin",
        "teamviewer",
        "anyviewer",
    };

    static readonly string[] OemSerialPlaceholders =
    {
        "to be filled by o.e.m.",
        "default string",
        "not specified",
        "n/a",
    };

    /// <summary>Uma sessão <see cref="Computer"/> aberta: CPU, GPU, memória e placa-mãe.</summary>
    public static InventarioHardwareSnapshot ReadInventory()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
        };

        try
        {
            computer.Open();
        }
        catch
        {
            return EmptySnapshot();
        }

        try
        {
            foreach (var hw in computer.Hardware)
                UpdateRecursive(hw);

            var flat = EnumerateDepthFirst(computer.Hardware).ToList();

            var cpu = GetCpuName(flat);
            var modulosMem = GetMemoriaModulosFromSmbios(computer);
            var ram = GetRamDisplay(flat, modulosMem);
            var gpu = GetGpuDisplay(flat);
            var mb = GetMotherboardDisplay(flat);

            var cpuTemp = FormatCpuTemperatureLine(GetCpuTemperatureCelsius(flat));
            var ramUse = FormatRamUsageLine(GetRamLoadPercent(flat));
            var gpuTemp = FormatGpuTemperatureLine(GetGpuTemperatureCelsius(flat));
            var mbSerial = FormatMotherboardSerialLine(computer);

            var procInfo = BuildProcessadorDetalhe(flat, computer, cpu);
            var mbInfo = BuildPlacaMaeDetalhe(computer);
            var gpusInfo = BuildPlacasVideoDetalhe(flat);
            var discos = BuildDiscosRigidos(flat);
            var monitores = InventarioMonitorOsReader.ReadMonitors();
            var so = InventarioMonitorOsReader.ReadSistemaOperacional();
            var remoto = InventarioAcessoRemotoReader.ReadAcessoRemoto();
            var posto = InventarioPostoReader.ReadPostoTrabalho();

            return new InventarioHardwareSnapshot(
                cpu, ram, gpu, mb,
                cpuTemp, ramUse, gpuTemp, mbSerial,
                modulosMem,
                procInfo,
                mbInfo,
                gpusInfo,
                discos,
                monitores,
                so,
                remoto,
                posto);
        }
        finally
        {
            try
            {
                computer.Close();
            }
            catch
            {
                // ignorar
            }
        }
    }

    static InventarioHardwareSnapshot EmptySnapshot()
    {
        var remoto = InventarioAcessoRemotoReader.ReadAcessoRemoto();
        var posto = InventarioPostoReader.ReadPostoTrabalho();
        return new InventarioHardwareSnapshot(
            "—", "—", "—", "—",
            "Temperatura: —",
            "Uso: —",
            "Temperatura: —",
            "N.º de série: —",
            Array.Empty<MemoriaModuloInventario>(),
            new ProcessadorDetalheInventario("—", null, null, null, null),
            new PlacaMaeDetalheInventario(null, null),
            Array.Empty<PlacaVideoDetalheInventario>(),
            Array.Empty<DiscoRigidoInventario>(),
            Array.Empty<MonitorInventario>(),
            new SistemaOperacionalInventario("—", "—", "—", null),
            remoto,
            posto);
    }

    static ProcessadorDetalheInventario BuildProcessadorDetalhe(
        IReadOnlyList<IHardware> flat,
        Computer computer,
        string modeloFallback)
    {
        var temp = GetCpuTemperatureCelsius(flat);
        var ghz = GetCpuClockGhz(flat, computer);
        var (cores, threads) = GetCpuCoresThreadsFromSmbios(computer);
        return new ProcessadorDetalheInventario(
            string.IsNullOrWhiteSpace(modeloFallback) ? "—" : modeloFallback,
            ghz,
            temp,
            cores,
            threads);
    }

    /// <summary>Maior relógio (MHz) nos sensores Clock da CPU → GHz; senão SMBIOS CurrentSpeed/MaxSpeed (MHz).</summary>
    static double? GetCpuClockGhz(IReadOnlyList<IHardware> flat, Computer computer)
    {
        float? maxMhz = null;
        foreach (var h in flat)
        {
            if (h.HardwareType != HardwareType.Cpu)
                continue;
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Clock || s.Value is null)
                    continue;
                var v = s.Value.Value;
                if (maxMhz is null || v > maxMhz)
                    maxMhz = v;
            }
        }

        if (maxMhz is > 0)
            return Math.Round(maxMhz.Value / 1000.0, 2);

        try
        {
            var p = computer.SMBios.Processors;
            if (p is { Length: > 0 })
            {
                if (p[0].CurrentSpeed > 0)
                    return Math.Round(p[0].CurrentSpeed / 1000.0, 2);
                if (p[0].MaxSpeed > 0)
                    return Math.Round(p[0].MaxSpeed / 1000.0, 2);
            }
        }
        catch
        {
            // ignorar
        }

        return null;
    }

    static (int? Cores, int? Threads) GetCpuCoresThreadsFromSmbios(Computer computer)
    {
        try
        {
            var p = computer.SMBios.Processors;
            if (p is null || p.Length == 0)
                return (null, null);
            var x = p[0];
            int? c = x.CoreCount > 0 ? x.CoreCount : null;
            int? t = x.ThreadCount > 0 ? x.ThreadCount : null;
            return (c, t);
        }
        catch
        {
            return (null, null);
        }
    }

    static PlacaMaeDetalheInventario BuildPlacaMaeDetalhe(Computer computer)
    {
        try
        {
            var b = computer.SMBios.Board;
            if (b is null)
                return new PlacaMaeDetalheInventario(null, null);

            var sn = b.SerialNumber?.Trim();
            if (sn is null || sn.Length == 0 || IsPlaceholderSerial(sn))
                sn = null;

            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(b.ManufacturerName))
                parts.Add(b.ManufacturerName.Trim());
            if (!string.IsNullOrWhiteSpace(b.ProductName))
                parts.Add(b.ProductName.Trim());
            if (!string.IsNullOrWhiteSpace(b.Version))
                parts.Add(b.Version.Trim());
            var modelo = parts.Count > 0 ? string.Join(" ", parts) : null;

            return new PlacaMaeDetalheInventario(sn, modelo);
        }
        catch
        {
            return new PlacaMaeDetalheInventario(null, null);
        }
    }

    static IReadOnlyList<PlacaVideoDetalheInventario> BuildPlacasVideoDetalhe(IReadOnlyList<IHardware> flat)
    {
        var list = new List<PlacaVideoDetalheInventario>();
        foreach (var h in flat)
        {
            if (h.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                continue;
            var name = h.Name.Trim();
            if (name.Length == 0 || ShouldExcludeGpuName(name))
                continue;

            float? tempMax = null;
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || s.Value is null)
                    continue;
                var v = s.Value.Value;
                if (tempMax is null || v > tempMax)
                    tempMax = v;
            }

            var vramGb = TryGetGpuDedicatedMemoryGb(h);
            list.Add(new PlacaVideoDetalheInventario(name, vramGb, tempMax));
        }

        return list;
    }

    static IReadOnlyList<DiscoRigidoInventario> BuildDiscosRigidos(IReadOnlyList<IHardware> flat)
    {
        var lhmDisks = new List<(IHardware Hardware, string Nome, float? LifePct, float? TotalGb, float? UsedGb, string? Serial, string? Bus)>();
        foreach (var h in flat)
        {
            if (h.HardwareType != HardwareType.Storage)
                continue;
            var nome = h.Name.Trim();
            if (nome.Length == 0)
                continue;

            float? totalGb = null;
            float? usedPct = null;
            float? freeGb = null;

            foreach (var s in AllSensors(h))
            {
                if (s.Value is null)
                    continue;

                var name = s.Name ?? "";
                if (s.SensorType == SensorType.Data
                    && name.Equals("Total Space", StringComparison.OrdinalIgnoreCase))
                    totalGb = s.Value.Value;
                else if (s.SensorType == SensorType.Load
                         && name.Equals("Used Space", StringComparison.OrdinalIgnoreCase))
                    usedPct = s.Value.Value;
                else if (s.SensorType == SensorType.Data
                         && name.Equals("Free Space", StringComparison.OrdinalIgnoreCase))
                    freeGb = s.Value.Value;
            }

            float? usedGb = null;
            if (totalGb is not null && usedPct is not null)
                usedGb = (float)Math.Round(totalGb.Value * (usedPct.Value / 100.0), 2);
            else if (totalGb is not null && freeGb is not null)
                usedGb = (float)Math.Round(Math.Max(0, totalGb.Value - freeGb.Value), 2);

            var report = TryGetHardwareReport(h);
            ParseStorageReport(report, out var busLine, out var serialFromReport);
            if (h is StorageDevice storageDevice)
            {
                try
                {
                    var toolkitBus = storageDevice.Storage.BusType.ToString();
                    if (!string.IsNullOrWhiteSpace(toolkitBus))
                        busLine ??= toolkitBus;
                }
                catch
                {
                    // manter bus do relatório
                }
            }

            var serial = TryGetStorageSerial(h) ?? serialFromReport;
            if (serial is not null && IsPlaceholderSerial(serial))
                serial = null;

            var lifePct = TryExtractDiskLifePercent(h, report);

            lhmDisks.Add((h, nome, lifePct, totalGb, usedGb, serial, busLine));
        }

        var wmiDisks = CollectWmiPhysicalDisks();
        var usedByIndex = CollectUsedBytesByDiskIndex();
        var result = new List<DiscoRigidoInventario>();
        var matchedLhm = new HashSet<int>();

        foreach (var wmi in wmiDisks)
        {
            var bestIdx = -1;
            var bestScore = 0;
            for (var i = 0; i < lhmDisks.Count; i++)
            {
                if (matchedLhm.Contains(i))
                    continue;
                var score = NameMatchScore(wmi.Model, lhmDisks[i].Nome);
                if (!string.IsNullOrEmpty(wmi.Serial)
                    && string.Equals(wmi.Serial, lhmDisks[i].Serial, StringComparison.OrdinalIgnoreCase))
                    score += 100;
                if (wmi.Index is int wmiIdx
                    && TryGetStorageIndex(lhmDisks[i].Hardware) is int lhmIdx
                    && wmiIdx == lhmIdx)
                    score += 80;
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIdx = i;
                }
            }

            float? life = null;
            float? totalGb = wmi.SizeBytes > 0
                ? (float)Math.Round(wmi.SizeBytes / (1024d * 1024d * 1024d), 2)
                : null;
            float? usedGb = null;
            string? serial = wmi.Serial;
            var tipo = wmi.Tipo;

            if (bestIdx >= 0 && bestScore > 0)
            {
                matchedLhm.Add(bestIdx);
                var lhm = lhmDisks[bestIdx];
                life = lhm.LifePct;
                if (totalGb is null)
                    totalGb = lhm.TotalGb is not null ? (float)Math.Round(lhm.TotalGb.Value, 2) : null;
                usedGb = lhm.UsedGb;
                if (string.IsNullOrEmpty(serial))
                    serial = lhm.Serial;
                if (tipo is "Desconhecido" or "")
                    tipo = InferTipoDisco(lhm.Hardware, lhm.Bus, life is not null);
            }

            if (usedGb is null && wmi.Index is int idx && usedByIndex.TryGetValue(idx, out var usedBytes))
                usedGb = (float)Math.Round(usedBytes / (1024d * 1024d * 1024d), 2);

            if (serial is not null && IsPlaceholderSerial(serial))
                serial = null;

            result.Add(new DiscoRigidoInventario(
                wmi.Model ?? "Disco",
                NormalizeTipoDisco(tipo),
                serial,
                life,
                totalGb,
                usedGb));
        }

        for (var i = 0; i < lhmDisks.Count; i++)
        {
            if (matchedLhm.Contains(i))
                continue;
            var lhm = lhmDisks[i];
            result.Add(new DiscoRigidoInventario(
                lhm.Nome,
                NormalizeTipoDisco(InferTipoDisco(lhm.Hardware, lhm.Bus, lhm.LifePct is not null)),
                lhm.Serial,
                lhm.LifePct,
                lhm.TotalGb is not null ? (float)Math.Round(lhm.TotalGb.Value, 2) : null,
                lhm.UsedGb));
        }

        return result;
    }

    static IEnumerable<ISensor> AllSensors(IHardware hardware)
    {
        foreach (var s in hardware.Sensors)
            yield return s;
        foreach (var sub in hardware.SubHardware)
        {
            foreach (var s in AllSensors(sub))
                yield return s;
        }
    }

    /// <summary>
    /// Vida útil restante (0–100). LHM muitas vezes não cria sensor Level para SSDs genéricos
    /// (ex.: Endurance Remaining só aparece na tabela SMART) — por isso lemos sensores, NVMe e SMART.
    /// </summary>
    static float? TryExtractDiskLifePercent(IHardware hardware, string? report)
    {
        float? fromSensors = null;
        float? fromUsed = null;

        foreach (var s in AllSensors(hardware))
        {
            if (s.Value is null)
                continue;

            var name = s.Name ?? "";
            if (IsRemainingLifeSensorName(name))
            {
                fromSensors = ClampLifePercent(s.Value.Value);
                break;
            }

            if (IsConsumedLifeSensorName(name))
                fromUsed ??= ClampLifePercent(100f - s.Value.Value);
        }

        if (fromSensors is not null)
            return fromSensors;
        if (fromUsed is not null)
            return fromUsed;

        if (hardware is StorageDevice device)
        {
            var fromDevice = TryReadLifeFromStorageDevice(device);
            if (fromDevice is not null)
                return fromDevice;
        }

        return TryParseLifeFromStorageReport(report);
    }

    /// <summary>LHM 0.9.5+ unifica ATA/NVMe em <see cref="StorageDevice"/> (DiskInfoToolkit).</summary>
    static float? TryReadLifeFromStorageDevice(StorageDevice device)
    {
        try
        {
            var life = device.Storage.Smart?.Life;
            if (life.HasValue)
                return ClampLifePercent(life.Value);

            foreach (var attr in device.Attributes)
            {
                if (attr is null)
                    continue;

                if (IsRemainingLifeSensorName(attr.Name) || IsKnownWearAttributeId(attr.Id))
                {
                    // SmartAttribute.Value expõe RawValueULong; só aceitar se parecer % 0–100.
                    if (attr.Value is >= 0 and <= 100)
                        return ClampLifePercent(attr.Value);
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    static bool IsRemainingLifeSensorName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        if (name.Equals("Life", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Remaining", StringComparison.OrdinalIgnoreCase))
            return true;

        return name.Contains("Remaining Life", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Endurance Remaining", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Media Wear Out", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Media Wearout", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Wear Leveling Count", StringComparison.OrdinalIgnoreCase)
               || name.Contains("SSD Life", StringComparison.OrdinalIgnoreCase)
               || name.Contains("Wear Out Indicator", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsConsumedLifeSensorName(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.Contains("Percentage Used", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Percent Lifetime Used", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Percentage Lifetime Used", StringComparison.OrdinalIgnoreCase));

    static float? ClampLifePercent(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            return null;
        if (value < 0 || value > 100)
            return null;
        return (float)Math.Round(value, 1);
    }

    static bool TryParseInvariantFloat(string text, out float value) =>
        float.TryParse(
            text.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value);

    static bool IsKnownWearAttributeId(byte id) =>
        id is 0xA9 // SSD life left (vários vendors)
            or 0xAD // Wear Leveling Count
            or 0xB1 // Wear Leveling Count
            or 0xE8; // Endurance Remaining / Available Reserved Space

    static float? TryParseLifeFromStorageReport(string? report)
    {
        if (string.IsNullOrWhiteSpace(report))
            return null;

        // NVMe: "Percentage Used: 12%"
        var usedMatch = Regex.Match(
            report,
            @"Percentage\s+Used\s*:\s*(\d+(?:[.,]\d+)?)\s*%?",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (usedMatch.Success
            && TryParseInvariantFloat(usedMatch.Groups[1].Value, out var used))
            return ClampLifePercent(100f - used);

        // Linhas tipo "Remaining Life: 88" / "Endurance Remaining: 88"
        foreach (Match m in Regex.Matches(
                     report,
                     @"(?:Remaining\s+Life|Endurance\s+Remaining)\s*:\s*(\d+(?:[.,]\d+)?)\s*%?",
                     RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            if (TryParseInvariantFloat(m.Groups[1].Value, out var life))
                return ClampLifePercent(life);
        }

        // Relatório LHM 0.9.6: "ID, Description, Value, Threshold" ou linhas com Remaining Life.
        foreach (var rawLine in report.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length < 8 || !IsRemainingLifeSensorName(line))
                continue;

            var numbers = Regex.Matches(line, @"\d+(?:[.,]\d+)?");
            if (numbers.Count < 2)
                continue;

            // Preferir o campo Value (penúltimo numérico na linha antiga; 3.º na nova).
            if (numbers.Count >= 3
                && TryParseInvariantFloat(numbers[2].Value, out var value)
                && value is >= 0 and <= 100)
                return ClampLifePercent(value);

            if (TryParseInvariantFloat(numbers[^1].Value, out var last)
                && last is >= 0 and <= 100)
                return ClampLifePercent(last);

            if (TryParseInvariantFloat(numbers[^2].Value, out var prev)
                && prev is >= 0 and <= 100)
                return ClampLifePercent(prev);
        }

        return null;
    }

    sealed class WmiDiskRow
    {
        public int? Index { get; init; }
        public string? Model { get; set; }
        public string? Serial { get; set; }
        public long SizeBytes { get; set; }
        public string Tipo { get; set; } = "Desconhecido";
    }

    static List<WmiDiskRow> CollectWmiPhysicalDisks()
    {
        var byIndex = new Dictionary<int, WmiDiskRow>();

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT Index, Model, SerialNumber, Size, InterfaceType, MediaType FROM Win32_DiskDrive");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var index = TryGetInt(obj, "Index") ?? -1;
                    var model = CleanDiskModel(TryGetString(obj, "Model"));
                    var row = new WmiDiskRow
                    {
                        Index = index >= 0 ? index : null,
                        Model = model,
                        Serial = NormalizeDiskSerial(TryGetString(obj, "SerialNumber")),
                        SizeBytes = (long)TryGetUInt64(obj, "Size"),
                        Tipo = InferTipoFromWmi(
                            TryGetString(obj, "InterfaceType"),
                            TryGetString(obj, "MediaType"),
                            model),
                    };
                    byIndex[index >= 0 ? index : byIndex.Count + 1000] = row;
                }
            }
        }
        catch
        {
            // WMI indisponível
        }

        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\microsoft\windows\storage",
                "SELECT FriendlyName, MediaType, BusType, Size, SerialNumber FROM MSFT_PhysicalDisk");
            using var results = searcher.Get();
            foreach (ManagementObject obj in results)
            {
                using (obj)
                {
                    var friendly = CleanDiskModel(TryGetString(obj, "FriendlyName"));
                    var mediaType = TryGetInt(obj, "MediaType");
                    var busType = TryGetInt(obj, "BusType");
                    var tipo = MapMsftTipo(mediaType, busType, friendly);
                    var serial = NormalizeDiskSerial(TryGetString(obj, "SerialNumber"));
                    var size = (long)TryGetUInt64(obj, "Size");

                    var match = byIndex.Values.FirstOrDefault(d =>
                        NameMatchScore(d.Model, friendly) >= 2
                        || (!string.IsNullOrEmpty(serial)
                            && string.Equals(d.Serial, serial, StringComparison.OrdinalIgnoreCase)));

                    if (match is not null)
                    {
                        if (!string.IsNullOrEmpty(tipo) && tipo != "Desconhecido")
                            match.Tipo = tipo;
                        if (string.IsNullOrEmpty(match.Serial))
                            match.Serial = serial;
                        if (match.SizeBytes <= 0 && size > 0)
                            match.SizeBytes = size;
                        if (string.IsNullOrWhiteSpace(match.Model))
                            match.Model = friendly;
                    }
                }
            }
        }
        catch
        {
            // Storage namespace indisponível
        }

        return byIndex.Values
            .OrderBy(d => d.Index ?? int.MaxValue)
            .ThenBy(d => d.Model)
            .ToList();
    }

    static Dictionary<int, long> CollectUsedBytesByDiskIndex()
    {
        var result = new Dictionary<int, long>();
        try
        {
            using var drives = new ManagementObjectSearcher(
                @"root\cimv2",
                "SELECT DeviceID, Index FROM Win32_DiskDrive");
            using var driveResults = drives.Get();
            foreach (ManagementObject drive in driveResults)
            {
                using (drive)
                {
                    var diskIndex = TryGetInt(drive, "Index");
                    var deviceId = TryGetString(drive, "DeviceID");
                    if (diskIndex is null || string.IsNullOrEmpty(deviceId))
                        continue;

                    long used = 0;
                    var escaped = EscapeWmi(deviceId);
                    using var parts = new ManagementObjectSearcher(
                        @"root\cimv2",
                        $"ASSOCIATORS OF {{Win32_DiskDrive.DeviceID=\"{escaped}\"}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");
                    using var partResults = parts.Get();
                    foreach (ManagementObject part in partResults)
                    {
                        using (part)
                        {
                            var partId = TryGetString(part, "DeviceID");
                            if (string.IsNullOrEmpty(partId))
                                continue;

                            var escapedPart = EscapeWmi(partId);
                            using var logicals = new ManagementObjectSearcher(
                                @"root\cimv2",
                                $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID=\"{escapedPart}\"}} WHERE AssocClass=Win32_LogicalDiskToPartition");
                            using var logicalResults = logicals.Get();
                            foreach (ManagementObject logical in logicalResults)
                            {
                                using (logical)
                                {
                                    var size = TryGetUInt64(logical, "Size");
                                    var free = TryGetUInt64(logical, "FreeSpace");
                                    if (size > 0)
                                        used += (long)(size - free);
                                }
                            }
                        }
                    }

                    if (used > 0)
                        result[diskIndex.Value] = used;
                }
            }
        }
        catch
        {
            // melhor esforço
        }

        return result;
    }

    static string InferTipoFromWmi(string? interfaceType, string? mediaType, string? model)
    {
        var blob = $"{interfaceType} {mediaType} {model}";
        if (blob.Contains("NVMe", StringComparison.OrdinalIgnoreCase)
            || blob.Contains("NVM Express", StringComparison.OrdinalIgnoreCase))
            return "NVMe";
        if (blob.Contains("SSD", StringComparison.OrdinalIgnoreCase))
            return "SSD";
        if (blob.Contains("HDD", StringComparison.OrdinalIgnoreCase)
            || (mediaType?.Contains("Fixed hard disk", StringComparison.OrdinalIgnoreCase) ?? false))
            return "HD";
        return "Desconhecido";
    }

    static string MapMsftTipo(int? mediaType, int? busType, string? name)
    {
        // BusType 17 = NVMe
        if (busType == 17 || (name?.Contains("NVMe", StringComparison.OrdinalIgnoreCase) ?? false))
            return "NVMe";

        // MediaType: 3 = HDD, 4 = SSD
        return mediaType switch
        {
            4 => "SSD",
            3 => "HD",
            _ => InferTipoFromWmi(null, null, name),
        };
    }

    static string NormalizeTipoDisco(string? tipo)
    {
        if (string.IsNullOrWhiteSpace(tipo))
            return "Desconhecido";
        var t = tipo.Trim();
        if (t.Equals("HDD", StringComparison.OrdinalIgnoreCase)
            || t.Equals("Hard Disk", StringComparison.OrdinalIgnoreCase))
            return "HD";
        return t;
    }

    static string? CleanDiskModel(string? model) =>
        string.IsNullOrWhiteSpace(model) ? null : Regex.Replace(model.Trim(), @"\s+", " ");

    static string? NormalizeDiskSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
            return null;
        var trimmed = serial.Trim();
        return IsPlaceholderSerial(trimmed) ? null : trimmed;
    }

    static int NameMatchScore(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            return 0;

        var na = NormalizeDiskName(a);
        var nb = NormalizeDiskName(b);
        if (na == nb)
            return 100;
        if (na.Contains(nb, StringComparison.Ordinal) || nb.Contains(na, StringComparison.Ordinal))
            return 50;

        var tokensA = na.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var tokensB = nb.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokensA.Count(t => t.Length >= 3 && tokensB.Any(x => x.Contains(t, StringComparison.Ordinal)));
    }

    static string NormalizeDiskName(string value) =>
        Regex.Replace(value.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();

    static string? TryGetString(ManagementBaseObject obj, string property)
    {
        try
        {
            return obj[property]?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    static ulong TryGetUInt64(ManagementBaseObject obj, string property)
    {
        try
        {
            var value = obj[property];
            return value switch
            {
                null => 0,
                ulong u => u,
                long l when l > 0 => (ulong)l,
                int i when i > 0 => (ulong)i,
                uint ui => ui,
                string s when ulong.TryParse(s, out var parsed) => parsed,
                _ => Convert.ToUInt64(value),
            };
        }
        catch
        {
            return 0;
        }
    }

    static int? TryGetInt(ManagementBaseObject obj, string property)
    {
        try
        {
            var value = obj[property];
            return value switch
            {
                null => null,
                int i => i,
                uint ui => (int)ui,
                short s => s,
                ushort us => us,
                long l => (int)l,
                string str when int.TryParse(str, out var parsed) => parsed,
                _ => Convert.ToInt32(value),
            };
        }
        catch
        {
            return null;
        }
    }

    static int? TryGetStorageIndex(IHardware hardware)
    {
        if (hardware is StorageDevice device)
            return device.Storage.DriveNumber;

        var id = hardware.Identifier?.ToString();
        if (string.IsNullOrEmpty(id))
            return null;

        var parts = id.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^1], out var index))
            return index;

        return null;
    }

    static string EscapeWmi(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);

    static string? TryGetHardwareReport(IHardware h)
    {
        try
        {
            return h.GetReport();
        }
        catch
        {
            return null;
        }
    }

    static void ParseStorageReport(string? report, out string? busType, out string? serial)
    {
        busType = null;
        serial = null;
        if (string.IsNullOrWhiteSpace(report))
            return;

        foreach (var raw in report.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.StartsWith("Bus type:", StringComparison.OrdinalIgnoreCase))
            {
                busType = line["Bus type:".Length..].Trim();
                continue;
            }

            if (!line.Contains(':', StringComparison.Ordinal))
                continue;
            if (!line.Contains("Serial", StringComparison.OrdinalIgnoreCase))
                continue;
            if (line.Contains("Revision", StringComparison.OrdinalIgnoreCase))
                continue;

            var idx = line.IndexOf(':');
            if (idx < 0 || idx >= line.Length - 1)
                continue;
            var val = line[(idx + 1)..].Trim();
            if (val.Length > 0)
                serial = val;
        }
    }

    static string? TryGetStorageSerial(IHardware h)
    {
        try
        {
            if (h is StorageDevice device)
            {
                var sn = device.Storage.SerialNumber?.Trim();
                if (!string.IsNullOrEmpty(sn))
                    return sn;
            }

            foreach (var kv in h.Properties)
            {
                var k = kv.Key?.ToString() ?? "";
                if (!k.Contains("Serial", StringComparison.OrdinalIgnoreCase))
                    continue;
                var v = kv.Value?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(v))
                    return v;
            }
        }
        catch
        {
            // ignorar
        }

        return null;
    }

    /// <summary>Classifica NVMe / SSD / HD com base no DiskInfoToolkit / relatório LHM e no nome do disco.</summary>
    static string InferTipoDisco(IHardware h, string? busReport, bool temSensorVida)
    {
        if (h is StorageDevice device)
        {
            try
            {
                if (device.Storage.IsNVMe)
                    return "NVMe";
                if (device.Storage.IsSSD)
                    return "SSD";

                var toolkitBus = device.Storage.BusType.ToString();
                if (!string.IsNullOrWhiteSpace(toolkitBus))
                    busReport = string.IsNullOrWhiteSpace(busReport) ? toolkitBus : busReport;
            }
            catch
            {
                // fallback heurístico abaixo
            }
        }

        var bus = (busReport ?? "").Trim();
        var bn = bus.ToLowerInvariant();
        var name = h.Name.ToLowerInvariant();
        var id = h.Identifier.ToString().ToLowerInvariant();

        if (bn.Contains("nvme", StringComparison.Ordinal) || id.Contains("nvme", StringComparison.Ordinal))
            return "NVMe";
        if (bn.Contains("usb", StringComparison.Ordinal))
            return "USB";
        if (bn.Contains("raid", StringComparison.Ordinal))
            return "RAID";

        if (name.Contains("nvme", StringComparison.OrdinalIgnoreCase))
            return "NVMe";

        if (name.Contains("ssd", StringComparison.Ordinal) ||
            name.Contains("solid state", StringComparison.Ordinal))
            return "SSD";

        if (bn.Contains("sata", StringComparison.Ordinal) ||
            bn.Contains("ata", StringComparison.Ordinal) ||
            bn.Contains("scsi", StringComparison.Ordinal) ||
            bn.Contains("ahci", StringComparison.Ordinal))
        {
            if (temSensorVida || name.Contains("ssd", StringComparison.Ordinal))
                return "SSD";
            return "HD";
        }

        if (temSensorVida)
            return "SSD";

        if (name.Contains("hdd", StringComparison.Ordinal) ||
            name.Contains("hard disk", StringComparison.Ordinal) ||
            name.Contains("rpm", StringComparison.Ordinal))
            return "HD";

        return string.IsNullOrEmpty(bus) ? "Desconhecido" : bus.Trim();
    }

    /// <summary>VRAM dedicada total (GB) a partir de sensores SmallData/Data da GPU (LHM).</summary>
    static float? TryGetGpuDedicatedMemoryGb(IHardware h)
    {
        float? mb = null;

        foreach (var s in h.Sensors)
        {
            if (s.Value is null)
                continue;
            if (s.SensorType is not (SensorType.SmallData or SensorType.Data))
                continue;

            var n = s.Name;
            if (!n.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                continue;

            var lower = n.ToLowerInvariant();
            var isTotalLike =
                lower.Contains("total") ||
                (lower.Contains("dedicated") && (lower.Contains("d3d") || lower.Contains("gpu"))) ||
                (lower.Contains("d3d") && lower.Contains("memory")) ||
                n.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase) ||
                n.Equals("D3D Dedicated Memory", StringComparison.OrdinalIgnoreCase);

            if (!isTotalLike)
                continue;

            mb = s.Value.Value;
            break;
        }

        if (mb is null)
        {
            foreach (var s in h.Sensors)
            {
                if (s.Value is null || s.SensorType != SensorType.SmallData)
                    continue;
                if (s.Name.Equals("GPU Memory Total", StringComparison.OrdinalIgnoreCase) ||
                    s.Name.Equals("D3D Dedicated Memory", StringComparison.OrdinalIgnoreCase))
                {
                    mb = s.Value.Value;
                    break;
                }
            }
        }

        if (mb is null)
            return null;

        var v = mb.Value;
        // Valores típicos em MB (ex.: 8192); se já parecer GB (≤64 inteiro), devolver como está
        if (v > 64 || v != MathF.Floor(v))
            return MathF.Round(v / 1024f, 2);

        return MathF.Round(v, 2);
    }

    /// <summary>Módulos com <see cref="LibreHardwareMonitor.Hardware.MemoryDevice.Size"/> &gt; 0 (SMBIOS).</summary>
    static IReadOnlyList<MemoriaModuloInventario> GetMemoriaModulosFromSmbios(Computer computer)
    {
        try
        {
            var devices = computer.SMBios.MemoryDevices;
            if (devices is null || devices.Length == 0)
                return Array.Empty<MemoriaModuloInventario>();

            var list = new List<MemoriaModuloInventario>();
            foreach (var md in devices)
            {
                if (md.Size == 0 || md.Size == 0xFFFF)
                    continue;

                var gb = md.Size / 1024.0;
                if (gb <= 0)
                    continue;

                int? mts = null;
                if (md.ConfiguredSpeed > 0)
                    mts = md.ConfiguredSpeed;
                else if (md.Speed > 0)
                    mts = md.Speed;

                var loc = string.IsNullOrWhiteSpace(md.DeviceLocator) ? null : md.DeviceLocator.Trim();
                var bank = string.IsNullOrWhiteSpace(md.BankLocator) ? null : md.BankLocator.Trim();
                var arch = MapMemoryTypeToArquiteturaMemoria(md.Type);

                list.Add(new MemoriaModuloInventario(
                    loc,
                    bank,
                    Math.Round(gb, 2),
                    mts,
                    arch));
            }

            return list;
        }
        catch
        {
            return Array.Empty<MemoriaModuloInventario>();
        }
    }

    /// <summary>Mapeia <see cref="MemoryType"/> (SMBIOS tipo 17) para texto legível (DDR3, DDR4, …).</summary>
    static string? MapMemoryTypeToArquiteturaMemoria(MemoryType type)
    {
        if (!Enum.IsDefined(type))
            return null;

        return type switch
        {
            MemoryType.Unknown => null,
            MemoryType.Other => null,
            MemoryType.DDR => "DDR",
            MemoryType.DDR2 => "DDR2",
            MemoryType.DDR2_FBDIMM => "DDR2 FB-DIMM",
            MemoryType.DDR3 => "DDR3",
            MemoryType.FBD2 => "FBD2",
            MemoryType.DDR4 => "DDR4",
            MemoryType.LPDDR => "LPDDR",
            MemoryType.LPDDR2 => "LPDDR2",
            MemoryType.LPDDR3 => "LPDDR3",
            MemoryType.LPDDR4 => "LPDDR4",
            MemoryType.LogicalNonVolatileDevice => "NVDIMM-L",
            MemoryType.HBM => "HBM",
            MemoryType.HBM2 => "HBM2",
            MemoryType.DDR5 => "DDR5",
            MemoryType.LPDDR5 => "LPDDR5",
            _ => type.ToString().Replace("_", "-", StringComparison.Ordinal),
        };
    }

    static void UpdateRecursive(IHardware hardware)
    {
        hardware.Update();
        foreach (var sub in hardware.SubHardware)
            UpdateRecursive(sub);
    }

    static IEnumerable<IHardware> EnumerateDepthFirst(IEnumerable<IHardware> roots)
    {
        foreach (var h in roots)
        {
            yield return h;
            foreach (var nested in EnumerateDepthFirst(h.SubHardware))
                yield return nested;
        }
    }

    static string GetCpuName(IReadOnlyList<IHardware> hardware)
    {
        var cpus = hardware.Where(h => h.HardwareType == HardwareType.Cpu).Select(h => h.Name.Trim())
            .Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return cpus.Count == 0 ? "—" : string.Join(" · ", cpus);
    }

    static string GetRamDisplay(
        IReadOnlyList<IHardware> hardware,
        IReadOnlyList<MemoriaModuloInventario> modulosSmbios)
    {
        var fromLhm = TryGetRamTotalGbFromLhm(hardware);
        if (fromLhm is > 0)
            return FormatGbLabel(fromLhm.Value);

        if (modulosSmbios.Count > 0)
        {
            var sum = modulosSmbios.Sum(m => m.CapacidadeGb);
            if (sum > 0)
                return FormatGbLabel(sum);
        }

        var fromOs = TryGetPhysicalMemoryStatus();
        if (fromOs is { TotalBytes: > 0 })
            return FormatGbLabel(fromOs.Value.TotalBytes / (1024d * 1024d * 1024d));

        return "—";
    }

    static double? TryGetRamTotalGbFromLhm(IReadOnlyList<IHardware> hardware)
    {
        foreach (var h in hardware)
        {
            if (h.HardwareType != HardwareType.Memory)
                continue;

            float? used = null;
            float? avail = null;
            float? total = null;

            foreach (var s in h.Sensors)
            {
                if (s.Value is null)
                    continue;
                if (s.SensorType is not (SensorType.Data or SensorType.SmallData))
                    continue;

                var name = s.Name ?? "";
                if (name.Equals("Memory Used", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Used Memory", StringComparison.OrdinalIgnoreCase))
                    used = s.Value.Value;
                else if (name.Equals("Memory Available", StringComparison.OrdinalIgnoreCase)
                         || name.Equals("Available Memory", StringComparison.OrdinalIgnoreCase))
                    avail = s.Value.Value;
                else if (name.Equals("Memory Total", StringComparison.OrdinalIgnoreCase)
                         || name.Equals("Total Memory", StringComparison.OrdinalIgnoreCase)
                         || name.Contains("Physical Memory", StringComparison.OrdinalIgnoreCase))
                    total = s.Value.Value;
            }

            if (total is > 0)
                return total.Value;
            if (used is not null && avail is not null)
            {
                var sum = used.Value + avail.Value;
                if (sum > 0)
                    return sum;
            }
        }

        return null;
    }

    /// <summary>Carga de memória física (0–100). LHM primeiro; fallback Windows.</summary>
    static float? GetRamLoadPercent(IReadOnlyList<IHardware> hardware)
    {
        foreach (var h in hardware)
        {
            if (h.HardwareType != HardwareType.Memory)
                continue;

            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Load || s.Value is null)
                    continue;

                var name = s.Name ?? "";
                if (name.Equals("Memory", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("Memory Load", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Memory", StringComparison.OrdinalIgnoreCase))
                    return s.Value.Value;
            }
        }

        var status = TryGetPhysicalMemoryStatus();
        if (status is { TotalBytes: > 0 })
        {
            var used = status.Value.TotalBytes - status.Value.AvailableBytes;
            if (used < 0)
                used = 0;
            return (float)(used * 100d / status.Value.TotalBytes);
        }

        return null;
    }

    static string FormatGbLabel(double totalGb) =>
        $"{totalGb.ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))} GB";

    static (ulong TotalBytes, ulong AvailableBytes)? TryGetPhysicalMemoryStatus()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (!GlobalMemoryStatusEx(ref status))
                return null;
            if (status.TotalPhys == 0)
                return null;
            return (status.TotalPhys, status.AvailPhys);
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    static string GetGpuDisplay(IReadOnlyList<IHardware> hardware)
    {
        var names = new List<string>();
        foreach (var h in hardware)
        {
            if (h.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                continue;
            var n = h.Name.Trim();
            if (n.Length == 0 || names.Contains(n, StringComparer.OrdinalIgnoreCase))
                continue;
            names.Add(n);
        }

        var filtered = names.Where(n => !ShouldExcludeGpuName(n)).ToList();
        var use = filtered.Count > 0 ? filtered : names;
        return use.Count == 0 ? "—" : string.Join(" · ", use);
    }

    static string GetMotherboardDisplay(IReadOnlyList<IHardware> hardware)
    {
        foreach (var h in hardware)
        {
            if (h.HardwareType != HardwareType.Motherboard)
                continue;
            var n = h.Name.Trim();
            if (n.Length > 0)
                return n;
        }

        return "—";
    }

    /// <summary>Maior temperatura reportada nos sensores da CPU (°C).</summary>
    static float? GetCpuTemperatureCelsius(IReadOnlyList<IHardware> hardware)
    {
        float? max = null;
        foreach (var h in hardware)
        {
            if (h.HardwareType != HardwareType.Cpu)
                continue;
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || s.Value is null)
                    continue;
                var v = s.Value.Value;
                if (max is null || v > max.Value)
                    max = v;
            }
        }

        return max;
    }

    /// <summary>Por cada GPU não virtual, usa a maior temperatura; várias GPUs: valores separados por ·.</summary>
    static string? GetGpuTemperatureCelsius(IReadOnlyList<IHardware> hardware)
    {
        var parts = new List<string>();
        foreach (var h in hardware)
        {
            if (h.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
                continue;
            var name = h.Name.Trim();
            if (name.Length == 0 || ShouldExcludeGpuName(name))
                continue;

            float? max = null;
            foreach (var s in h.Sensors)
            {
                if (s.SensorType != SensorType.Temperature || s.Value is null)
                    continue;
                var v = s.Value.Value;
                if (max is null || v > max.Value)
                    max = v;
            }

            if (max is not null)
                parts.Add($"{max.Value:0} °C");
        }

        if (parts.Count == 0)
            return null;
        return string.Join(" · ", parts);
    }

    static string FormatCpuTemperatureLine(float? celsius) =>
        celsius is null
            ? "Temperatura: —"
            : $"Temperatura: {celsius.Value:0} °C";

    static string FormatGpuTemperatureLine(string? celsiusParts) =>
        string.IsNullOrWhiteSpace(celsiusParts)
            ? "Temperatura: —"
            : $"Temperatura: {celsiusParts}";

    static string FormatRamUsageLine(float? loadPercent) =>
        loadPercent is null
            ? "Uso: —"
            : $"Uso: {loadPercent.Value:0} %";

    static string FormatMotherboardSerialLine(Computer computer)
    {
        try
        {
            var sn = computer.SMBios.Board?.SerialNumber?.Trim() ?? "";
            if (sn.Length == 0 || IsPlaceholderSerial(sn))
                return "N.º de série: —";
            return $"N.º de série: {sn}";
        }
        catch
        {
            return "N.º de série: —";
        }
    }

    static bool IsPlaceholderSerial(string sn)
    {
        var t = sn.Trim();
        foreach (var p in OemSerialPlaceholders)
        {
            if (t.Equals(p, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static bool ShouldExcludeGpuName(string name)
    {
        var n = name.ToLowerInvariant();
        foreach (var sub in GpuNameExcludeSubstrings)
        {
            if (n.Contains(sub, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
