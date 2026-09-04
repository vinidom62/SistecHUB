using System.Text.Json.Serialization;
using SistecHub.Core;

namespace SistecHub.Modulos.Inventario;

/// <summary>Bloco <c>memoria_ram</c> do relatório JSON.</summary>
internal sealed class MemoriaRamRelatorioJson
{
    [JsonPropertyName("total_sistema")]
    public string TotalSistema { get; init; } = "";

    [JsonPropertyName("quantidade_modulos_instalados")]
    public int QuantidadeModulosInstalados { get; init; }

    [JsonPropertyName("modulos")]
    public IReadOnlyList<MemoriaModuloRelatorioItemJson> Modulos { get; init; } =
        Array.Empty<MemoriaModuloRelatorioItemJson>();
}

internal sealed class MemoriaModuloRelatorioItemJson
{
    [JsonPropertyName("localizador")]
    public string? Localizador { get; init; }

    [JsonPropertyName("banco")]
    public string? Banco { get; init; }

    [JsonPropertyName("capacidade_gb")]
    public double CapacidadeGb { get; init; }

    [JsonPropertyName("arquitetura_memoria")]
    public string? ArquiteturaMemoria { get; init; }

    [JsonPropertyName("frequencia_mts")]
    public int? FrequenciaMts { get; init; }
}

internal sealed class ProcessadorRelatorioJson
{
    [JsonPropertyName("modelo")]
    public string Modelo { get; init; } = "";

    [JsonPropertyName("temperatura_c")]
    public float? TemperaturaC { get; init; }

    [JsonPropertyName("sensor_temperatura")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SensorTemperatura { get; init; }

    [JsonPropertyName("nucleos")]
    public int? Nucleos { get; init; }

    [JsonPropertyName("threads")]
    public int? Threads { get; init; }
}

internal sealed class PlacaMaeRelatorioJson
{
    [JsonPropertyName("modelo")]
    public string? Modelo { get; init; }

    [JsonPropertyName("numero_serie")]
    public string? NumeroSerie { get; init; }
}

internal sealed class PlacaVideoRelatorioJson
{
    [JsonPropertyName("gpus")]
    public IReadOnlyList<PlacaVideoGpuRelatorioJson> Gpus { get; init; } =
        Array.Empty<PlacaVideoGpuRelatorioJson>();
}

internal sealed class PlacaVideoGpuRelatorioJson
{
    [JsonPropertyName("nome")]
    public string Nome { get; init; } = "";

    [JsonPropertyName("memoria_gb")]
    public float? MemoriaGb { get; init; }

    [JsonPropertyName("temperatura_c")]
    public float? TemperaturaC { get; init; }

    [JsonPropertyName("sensor_temperatura")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SensorTemperatura { get; init; }
}

internal sealed class DiscoRigidoRelatorioJson
{
    [JsonPropertyName("nome")]
    public string Nome { get; init; } = "";

    [JsonPropertyName("tipo")]
    public string Tipo { get; init; } = "";

    [JsonPropertyName("numero_serie")]
    public string? NumeroSerie { get; init; }

    [JsonPropertyName("saude")]
    public string Saude { get; init; } = "desconhecida";

    [JsonPropertyName("vida_percent")]
    public float? VidaPercent { get; init; }

    [JsonPropertyName("armazenamento_total_gb")]
    public float? ArmazenamentoTotalGb { get; init; }

    [JsonPropertyName("armazenamento_usado_gb")]
    public float? ArmazenamentoUsadoGb { get; init; }
}

internal sealed class MonitorRelatorioJson
{
    [JsonPropertyName("modelo")]
    public string? Modelo { get; init; }

    [JsonPropertyName("numero_serie")]
    public string? NumeroSerie { get; init; }
}

internal sealed class SistemaOperacionalRelatorioJson
{
    [JsonPropertyName("nome_produto")]
    public string NomeProduto { get; init; } = "";

    [JsonPropertyName("arquitetura")]
    public string Arquitetura { get; init; } = "";

    [JsonPropertyName("versao_atual")]
    public string VersaoAtual { get; init; } = "";

    [JsonPropertyName("data_instalacao")]
    public string? DataInstalacao { get; init; }

    [JsonPropertyName("status_ativacao")]
    public string StatusAtivacao { get; init; } = "";

    [JsonPropertyName("chave_ativacao")]
    public string? ChaveAtivacao { get; init; }

    [JsonPropertyName("canal_licenca")]
    public string? CanalLicenca { get; init; }
}

internal sealed class AcessoRemotoRelatorioJson
{
    [JsonPropertyName("anydesk_id")]
    public string? AnydeskId { get; init; }
}

internal sealed class PostoTrabalhoRelatorioJson
{
    [JsonPropertyName("hostname")]
    public string Hostname { get; init; } = "";

    [JsonPropertyName("entidade")]
    public int Entidade { get; init; }

    [JsonPropertyName("utilizador_dominio")]
    public string UtilizadorDominio { get; init; } = "";

    [JsonPropertyName("tipo_computador")]
    public string TipoComputador { get; init; } = "";

    [JsonPropertyName("modelo_computador")]
    public string? ModeloComputador { get; init; }

    [JsonPropertyName("numero_serie")]
    public string? NumeroSerie { get; init; }

    [JsonPropertyName("utilizador")]
    public string Utilizador { get; init; } = "";

    [JsonPropertyName("dominio")]
    public string? Dominio { get; init; }

    [JsonPropertyName("sistema_operacional")]
    public SistemaOperacionalRelatorioJson SistemaOperacional { get; init; } = new();
}

/// <summary>Informações do SistecHub e estado de actualização.</summary>
internal sealed class SistecHubRelatorioJson
{
    [JsonPropertyName("versao_instalada")]
    public string VersaoInstalada { get; init; } = "";

    [JsonPropertyName("tem_erro_atualizacao")]
    public bool TemErroAtualizacao { get; init; }

    [JsonPropertyName("erro_atualizacao")]
    public string? ErroAtualizacao { get; init; }

    [JsonPropertyName("status_atualizacao")]
    public string StatusAtualizacao { get; init; } = "";
}

/// <summary>Bloco <c>inventory</c> do payload PluginSistechubMachineInventory.</summary>
internal sealed class InventarioRelatorioJson
{
    [JsonPropertyName("processador")]
    public ProcessadorRelatorioJson Processador { get; init; } = new();

    [JsonPropertyName("memoria_ram")]
    public MemoriaRamRelatorioJson MemoriaRam { get; init; } = new();

    [JsonPropertyName("placa_mae")]
    public PlacaMaeRelatorioJson PlacaMae { get; init; } = new();

    [JsonPropertyName("discos_rigidos")]
    public IReadOnlyList<DiscoRigidoRelatorioJson> DiscosRigidos { get; init; } =
        Array.Empty<DiscoRigidoRelatorioJson>();

    [JsonPropertyName("placa_video")]
    public PlacaVideoRelatorioJson PlacaVideo { get; init; } = new();

    [JsonPropertyName("monitores")]
    public IReadOnlyList<MonitorRelatorioJson> Monitores { get; init; } =
        Array.Empty<MonitorRelatorioJson>();

    [JsonPropertyName("posto_trabalho")]
    public PostoTrabalhoRelatorioJson PostoTrabalho { get; init; } = new();

    [JsonPropertyName("acesso_remoto")]
    public AcessoRemotoRelatorioJson AcessoRemoto { get; init; } = new();

    [JsonPropertyName("sistechub")]
    public SistecHubRelatorioJson SistecHub { get; init; } = new();

    public static InventarioRelatorioJson FromSnapshot(
        in InventarioHardwareSnapshot snapshot,
        int? entidade = null)
    {
        var modulos = snapshot.ModulosMemoria
            .Select(m => new MemoriaModuloRelatorioItemJson
            {
                Localizador = m.Localizador,
                Banco = m.Banco,
                CapacidadeGb = m.CapacidadeGb,
                ArquiteturaMemoria = m.ArquiteturaMemoria,
                FrequenciaMts = m.FrequenciaMts,
            })
            .ToList();

        var p = snapshot.ProcessadorInfo;
        var so = snapshot.SistemaOperacional;
        var pt = snapshot.PostoTrabalho;
        var entidadeId = entidade ?? ResolveEntidadeId();
        var updateStatus = UpdateServiceCoordinator.TryReadStatus();
        var versaoInstalada = VelopackUpdateEngine.DisplayVersion;
        var temErroAtualizacao = updateStatus?.Phase == UpdateServicePhase.Error;
        var erroAtualizacao = temErroAtualizacao ? updateStatus?.Message : null;
        var statusDescricao = UpdateServiceCoordinator.DescribeStatusForUi(updateStatus);

        return new InventarioRelatorioJson
        {
            Processador = new ProcessadorRelatorioJson
            {
                Modelo = p.Modelo,
                TemperaturaC = p.TemperaturaC,
                SensorTemperatura = p.SensorTemperatura,
                Nucleos = p.Nucleos,
                Threads = p.Threads,
            },
            MemoriaRam = new MemoriaRamRelatorioJson
            {
                TotalSistema = snapshot.Ram,
                QuantidadeModulosInstalados = modulos.Count,
                Modulos = modulos,
            },
            PlacaMae = new PlacaMaeRelatorioJson
            {
                Modelo = snapshot.PlacaMaeInfo.Modelo,
                NumeroSerie = snapshot.PlacaMaeInfo.NumeroSerie,
            },
            DiscosRigidos = snapshot.DiscosRigidos
                .Select(d => new DiscoRigidoRelatorioJson
                {
                    Nome = d.Nome,
                    Tipo = d.Tipo,
                    NumeroSerie = d.NumeroSerie,
                    Saude = d.Saude,
                    VidaPercent = d.VidaPercent,
                    ArmazenamentoTotalGb = d.ArmazenamentoTotalGb,
                    ArmazenamentoUsadoGb = d.ArmazenamentoUsadoGb,
                })
                .ToList(),
            PlacaVideo = new PlacaVideoRelatorioJson
            {
                Gpus = snapshot.PlacasVideo
                    .Select(g => new PlacaVideoGpuRelatorioJson
                    {
                        Nome = g.Nome,
                        MemoriaGb = g.MemoriaGb,
                        TemperaturaC = g.TemperaturaC,
                        SensorTemperatura = g.SensorTemperatura,
                    })
                    .ToList(),
            },
            Monitores = snapshot.Monitores
                .Select(m => new MonitorRelatorioJson
                {
                    Modelo = m.Modelo,
                    NumeroSerie = m.NumeroSerie,
                })
                .ToList(),
            PostoTrabalho = new PostoTrabalhoRelatorioJson
            {
                Hostname = Environment.MachineName?.Trim() ?? "",
                Entidade = entidadeId,
                UtilizadorDominio = pt.UtilizadorDominio,
                TipoComputador = pt.TipoComputador,
                ModeloComputador = pt.ModeloComputador,
                NumeroSerie = pt.NumeroSerie,
                Utilizador = pt.Utilizador,
                Dominio = pt.Dominio,
                SistemaOperacional = new SistemaOperacionalRelatorioJson
                {
                    NomeProduto = so.NomeProduto,
                    Arquitetura = so.Arquitetura,
                    VersaoAtual = so.VersaoAtual,
                    DataInstalacao = so.DataInstalacao,
                    StatusAtivacao = so.StatusAtivacao,
                    ChaveAtivacao = so.ChaveAtivacao,
                    CanalLicenca = so.CanalLicenca,
                },
            },
            AcessoRemoto = new AcessoRemotoRelatorioJson
            {
                AnydeskId = NormalizeAnydeskId(snapshot.AcessoRemoto.AnyDeskId),
            },
            SistecHub = new SistecHubRelatorioJson
            {
                VersaoInstalada = versaoInstalada,
                TemErroAtualizacao = temErroAtualizacao,
                ErroAtualizacao = erroAtualizacao,
                StatusAtualizacao = statusDescricao,
            },
        };
    }

    static int ResolveEntidadeId()
    {
        var raw = AppSettingsStore.Load().EntityId;
        return int.TryParse(raw?.Trim(), out var id) && id > 0 ? id : 0;
    }

    static string? NormalizeAnydeskId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        var digits = new string(raw.Where(char.IsAsciiDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}

/// <summary>Conteúdo de <c>input</c> no POST PluginSistechubMachineInventory.</summary>
internal sealed class InventarioPluginInputJson
{
    [JsonPropertyName("plugin_sistechub_machines_id")]
    public int PluginSistechubMachinesId { get; init; }

    [JsonPropertyName("inventory")]
    public InventarioRelatorioJson Inventory { get; init; } = new();
}

/// <summary>Raiz do payload: <c>{ "input": { ... } }</c>.</summary>
internal sealed class InventarioPluginPayloadJson
{
    [JsonPropertyName("input")]
    public InventarioPluginInputJson Input { get; init; } = new();

    public static InventarioPluginPayloadJson FromSnapshot(
        in InventarioHardwareSnapshot snapshot,
        int pluginSistechubMachinesId,
        int? entidade = null) =>
        new()
        {
            Input = new InventarioPluginInputJson
            {
                PluginSistechubMachinesId = pluginSistechubMachinesId,
                Inventory = InventarioRelatorioJson.FromSnapshot(snapshot, entidade),
            },
        };

    public static int ParsePluginMachineId(string? glpiMachineId)
    {
        if (string.IsNullOrWhiteSpace(glpiMachineId))
            return 0;
        return int.TryParse(glpiMachineId.Trim(), out var id) ? id : 0;
    }
}
