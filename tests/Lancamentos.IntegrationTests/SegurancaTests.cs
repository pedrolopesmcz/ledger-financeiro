using System.Net;
using System.Net.Http.Headers;
using Ledger.TestesComuns;

namespace Lancamentos.IntegrationTests;

[Collection(ColecaoIntegracao.Nome)]
public sealed class SegurancaTests : IAsyncLifetime
{
    private readonly Containers _containers;
    private ApiFactory _factory = null!;
    private HttpClient _backoffice = null!;

    public SegurancaTests(Containers containers) => _containers = containers;

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(_containers.Postgres.GetConnectionString(), _containers.RabbitMq.GetConnectionString());
        _backoffice = _factory.ClienteBackoffice();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _backoffice.Dispose();
        await _factory.DisposeAsync();
    }

    private HttpClient ClienteDe(Guid titular) => _factory.CreateClient().ComToken(Tokens.Cliente(titular));

    [Fact]
    public async Task Sem_token_retorna_401()
    {
        using var anonimo = _factory.CreateClient();

        var resposta = await anonimo.GetAsync($"/contas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Fact]
    public async Task Health_check_e_anonimo()
    {
        using var anonimo = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/health/live")).StatusCode);
    }

    [Fact]
    public async Task Token_de_outro_emissor_ou_expirado_retorna_401()
    {
        using var outroEmissor = _factory.CreateClient().ComToken(Tokens.Gerar(Guid.NewGuid(), Tokens.EscoposDoBackoffice, emissor: "https://atacante.exemplo"));
        using var expirado = _factory.CreateClient().ComToken(Tokens.Gerar(Guid.NewGuid(), Tokens.EscoposDoBackoffice, validade: TimeSpan.FromMinutes(-5)));
        using var assinaturaFalsa = _factory.CreateClient().ComToken(Tokens.Gerar(Guid.NewGuid(), Tokens.EscoposDoBackoffice, chave: "outra-chave-qualquer-com-mais-de-32-bytes!!"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await outroEmissor.GetAsync($"/contas/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await expirado.GetAsync($"/contas/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await assinaturaFalsa.GetAsync($"/contas/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task Cliente_consulta_e_movimenta_a_propria_conta()
    {
        var ana = Guid.NewGuid();
        var conta = await _backoffice.AbrirContaAsync("ContaDigital", 0, ana);
        await _backoffice.PostAsync($"/contas/{conta}/lancamentos", new { operacao = "Deposito", valor = 500m }, "seed");
        using var cliente = ClienteDe(ana);

        var pix = await cliente.PostAsync($"/contas/{conta}/lancamentos", new { operacao = "PixEnviado", valor = 120m }, "pix-1");
        var saldo = await (await cliente.GetAsync($"/contas/{conta}")).LerAsync<ContaResposta>();

        Assert.Equal(HttpStatusCode.Created, pix.StatusCode);
        Assert.Equal(380m, saldo.Saldo);
    }

    [Fact]
    public async Task Conta_de_outro_cliente_responde_404_como_se_nao_existisse()
    {
        var bruno = Guid.NewGuid();
        var contaDoBruno = await _backoffice.AbrirContaAsync("ContaDigital", 0, bruno);
        using var ana = ClienteDe(Guid.NewGuid());

        var consulta = await ana.GetAsync($"/contas/{contaDoBruno}");
        var pix = await ana.PostAsync($"/contas/{contaDoBruno}/lancamentos", new { operacao = "PixEnviado", valor = 1m }, "x");
        var inexistente = await ana.GetAsync($"/contas/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, consulta.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, pix.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
        Assert.Equal(0, await Banco.LancamentosDaContaAsync(_containers.Postgres.GetConnectionString(), contaDoBruno));
    }

    [Theory]
    [InlineData("Deposito")]
    [InlineData("PixRecebido")]
    [InlineData("Compra")]
    public async Task Cliente_nao_inicia_operacoes_que_pertencem_a_sistemas_do_banco(string operacao)
    {
        var ana = Guid.NewGuid();
        var conta = await _backoffice.AbrirContaAsync("ContaDigital", 1000m, ana);
        using var cliente = ClienteDe(ana);

        var resposta = await cliente.PostAsync($"/contas/{conta}/lancamentos", new { operacao, valor = 10m }, "op");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }

    [Fact]
    public async Task Cliente_sem_escopo_administrativo_nao_abre_conta_nem_estorna()
    {
        var ana = Guid.NewGuid();
        var conta = await _backoffice.AbrirContaAsync("CartaoCredito", 1000m, ana);
        var compra = await (await _backoffice.PostAsync($"/contas/{conta}/lancamentos", new { operacao = "Compra", valor = 50m }, "c")).LerAsync<LancamentoResposta>();
        using var cliente = ClienteDe(ana);

        var abrir = await cliente.PostAsync("/contas", new { tipo = "ContaDigital", limite = 0, titularId = ana }, chave: null);
        var estorno = await cliente.PostAsync($"/contas/{conta}/lancamentos/{compra.Id}/estorno", new { motivo = "quero" }, "e");

        Assert.Equal(HttpStatusCode.Forbidden, abrir.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, estorno.StatusCode);
    }

    [Fact]
    public async Task Token_sem_o_escopo_de_escrita_nao_registra_lancamento()
    {
        var ana = Guid.NewGuid();
        var conta = await _backoffice.AbrirContaAsync("ContaDigital", 0, ana);
        using var somenteLeitura = _factory.CreateClient();
        somenteLeitura.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.Gerar(ana, ["saldo:read"]));

        var resposta = await somenteLeitura.PostAsync($"/contas/{conta}/lancamentos", new { operacao = "PixEnviado", valor = 1m }, "x");

        Assert.Equal(HttpStatusCode.Forbidden, resposta.StatusCode);
    }
}
