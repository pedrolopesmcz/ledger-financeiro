using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Consolidado.Core;
using Ledger.TestesComuns;
using static Consolidado.IntegrationTests.Eventos;

namespace Consolidado.IntegrationTests;

[Collection(ColecaoIntegracao.Nome)]
public sealed class ApiTests
{
    private readonly Containers _containers;

    public ApiTests(Containers containers) => _containers = containers;

    [Fact]
    public async Task Saldo_atual_e_invalidado_pelo_worker_quando_chega_um_novo_lancamento()
    {
        var conta = Guid.NewGuid();
        using var worker = _containers.CriarWorker();
        await worker.StartAsync();
        await using var api = new ApiFactory(_containers.Configuracao());
        using var client = api.ClienteBackoffice();

        await _containers.PublicarAsync(Novo(conta, 100m, Brasilia(18, 9)));
        await Esperar.AteAsync(async () => (await client.GetAsync($"/contas/{conta}/saldo")).IsSuccessStatusCode, TimeSpan.FromSeconds(30), "primeiro saldo");
        var primeiro = await client.GetFromJsonAsync<PosicaoAtual>($"/contas/{conta}/saldo", JsonSerializerOptions.Web);

        // A validade do cache no teste é de 1 hora: só a invalidação explica o valor novo.
        await _containers.PublicarAsync(Novo(conta, 50m, Brasilia(18, 10)));
        await Esperar.AteAsync(
            async () => (await client.GetFromJsonAsync<PosicaoAtual>($"/contas/{conta}/saldo", JsonSerializerOptions.Web))!.Saldo == 150m,
            TimeSpan.FromSeconds(30),
            "saldo atualizado após invalidação");

        Assert.Equal(100m, primeiro!.Saldo);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Consultas_de_consolidado_diario_e_saldo_no_instante()
    {
        var conta = Guid.NewGuid();
        using var worker = _containers.CriarWorker();
        await worker.StartAsync();
        await using var api = new ApiFactory(_containers.Configuracao());
        using var client = api.ClienteBackoffice();

        await _containers.PublicarAsync(Novo(conta, 300m, Brasilia(17, 9)));
        await _containers.PublicarAsync(Novo(conta, -50m, Brasilia(17, 15)));
        await Esperar.AteAsync(
            async () => (await client.GetFromJsonAsync<PosicaoAtual?>($"/contas/{conta}/saldo", JsonSerializerOptions.Web) is { Saldo: 250m }),
            TimeSpan.FromSeconds(30),
            "eventos consolidados");

        var dia = await client.GetFromJsonAsync<ConsolidadoDiario>($"/contas/{conta}/consolidado/2026-11-17", JsonSerializerOptions.Web);
        var instante = await client.GetFromJsonAsync<SaldoNoInstante>(
            $"/contas/{conta}/saldo?em={Uri.EscapeDataString(Brasilia(17, 12).ToString("O"))}", JsonSerializerOptions.Web);

        Assert.Equal((300m, 50m, 250m), (dia!.Creditos, dia.Debitos, dia.SaldoFinal));
        Assert.Equal(300m, instante!.Saldo);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Conta_sem_movimentos_retorna_404()
    {
        await using var api = new ApiFactory(_containers.Configuracao());
        using var client = api.ClienteBackoffice();

        var resposta = await client.GetAsync($"/contas/{Guid.NewGuid()}/saldo");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Com_o_redis_fora_a_consulta_continua_respondendo_pelo_banco()
    {
        var conta = Guid.NewGuid();
        using var worker = _containers.CriarWorker();
        await worker.StartAsync();
        await _containers.PublicarAsync(Novo(conta, 42m, Brasilia(18, 9)));

        await using var api = new ApiFactory(_containers.Configuracao(redis: "127.0.0.1:1,abortConnect=false,connectTimeout=200,syncTimeout=200,asyncTimeout=200"));
        using var client = api.ClienteBackoffice();

        await Esperar.AteAsync(async () => (await client.GetAsync($"/contas/{conta}/saldo")).IsSuccessStatusCode, TimeSpan.FromSeconds(30), "saldo sem redis");
        var posicao = await client.GetFromJsonAsync<PosicaoAtual>($"/contas/{conta}/saldo", JsonSerializerOptions.Web);

        Assert.Equal(42m, posicao!.Saldo);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Cliente_ve_o_proprio_saldo_e_recebe_404_para_conta_alheia()
    {
        var ana = Guid.NewGuid();
        var contaDaAna = Guid.NewGuid();
        var contaDoBruno = Guid.NewGuid();
        using var worker = _containers.CriarWorker();
        await worker.StartAsync();
        await _containers.PublicarAsync(Novo(contaDaAna, 90m, Brasilia(18, 9), titular: ana));
        await _containers.PublicarAsync(Novo(contaDoBruno, 70m, Brasilia(18, 9), titular: Guid.NewGuid()));

        await using var api = new ApiFactory(_containers.Configuracao());
        using var backoffice = api.ClienteBackoffice();
        await Esperar.AteAsync(
            async () => (await backoffice.GetAsync($"/contas/{contaDoBruno}/saldo")).IsSuccessStatusCode
                && (await backoffice.GetAsync($"/contas/{contaDaAna}/saldo")).IsSuccessStatusCode,
            TimeSpan.FromSeconds(30),
            "contas consolidadas");
        using var cliente = api.CreateClient().ComToken(Tokens.Cliente(ana));

        var propria = await cliente.GetAsync($"/contas/{contaDaAna}/saldo");
        var alheia = await cliente.GetAsync($"/contas/{contaDoBruno}/saldo");
        var alheiaDia = await cliente.GetAsync($"/contas/{contaDoBruno}/consolidado/2026-11-18");

        Assert.Equal(HttpStatusCode.OK, propria.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alheia.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, alheiaDia.StatusCode);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Sem_token_retorna_401()
    {
        await using var api = new ApiFactory(_containers.Configuracao());
        using var anonimo = api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonimo.GetAsync($"/contas/{Guid.NewGuid()}/saldo")).StatusCode);
    }
}
