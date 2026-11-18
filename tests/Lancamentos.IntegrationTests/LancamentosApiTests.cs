using System.Net;

namespace Lancamentos.IntegrationTests;

[Collection(ColecaoIntegracao.Nome)]
public sealed class LancamentosApiTests : IAsyncLifetime
{
    private readonly Containers _containers;
    private ApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LancamentosApiTests(Containers containers) => _containers = containers;

    private string Postgres => _containers.Postgres.GetConnectionString();

    public Task InitializeAsync()
    {
        _factory = new ApiFactory(Postgres, _containers.RabbitMq.GetConnectionString());
        _client = _factory.ClienteBackoffice();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Registrar_grava_lancamento_saldo_e_evento_na_mesma_transacao()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);

        var resposta = await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 150.75m }, "dep-1");

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var lancamento = await resposta.LerAsync<LancamentoResposta>();
        Assert.Equal(150.75m, lancamento.SaldoApos);
        Assert.Equal($"/contas/{contaId}/lancamentos/{lancamento.Id}", resposta.Headers.Location!.ToString());

        var conta = await (await _client.GetAsync($"/contas/{contaId}")).LerAsync<ContaResposta>();
        Assert.Equal(150.75m, conta.Saldo);
        Assert.Equal(1, await Banco.OutboxDaContaAsync(Postgres, contaId));
    }

    [Fact]
    public async Task Sem_chave_de_idempotencia_retorna_400()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);

        var resposta = await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 10m }, chave: null);

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("idempotency_key_obrigatoria", (await resposta.LerAsync<ProblemaResposta>()).Codigo);
    }

    [Fact]
    public async Task Reenvio_com_a_mesma_chave_devolve_o_original_sem_duplicar()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);
        var corpo = new { operacao = "Deposito", valor = 100m };

        var primeira = await _client.PostAsync($"/contas/{contaId}/lancamentos", corpo, "pedido-123");
        var segunda = await _client.PostAsync($"/contas/{contaId}/lancamentos", corpo, "pedido-123");

        Assert.Equal(HttpStatusCode.Created, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal((await primeira.LerAsync<LancamentoResposta>()).Id, (await segunda.LerAsync<LancamentoResposta>()).Id);
        Assert.Equal(1, await Banco.LancamentosDaContaAsync(Postgres, contaId));
        Assert.Equal(1, await Banco.OutboxDaContaAsync(Postgres, contaId));
    }

    [Fact]
    public async Task Mesma_chave_com_operacao_diferente_retorna_409()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);
        await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 100m }, "chave-x");

        var resposta = await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 999m }, "chave-x");

        Assert.Equal(HttpStatusCode.Conflict, resposta.StatusCode);
        Assert.Equal("idempotency_key_reutilizada", (await resposta.LerAsync<ProblemaResposta>()).Codigo);
    }

    [Fact]
    public async Task Reenvios_simultaneos_com_a_mesma_chave_geram_um_unico_lancamento()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);
        var corpo = new { operacao = "Deposito", valor = 50m };

        var respostas = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(_ => _client.PostAsync($"/contas/{contaId}/lancamentos", corpo, "mesma-chave")));

        Assert.All(respostas, r => Assert.True(r.IsSuccessStatusCode, $"status {r.StatusCode}"));
        Assert.Equal(1, await Banco.LancamentosDaContaAsync(Postgres, contaId));
        var conta = await (await _client.GetAsync($"/contas/{contaId}")).LerAsync<ContaResposta>();
        Assert.Equal(50m, conta.Saldo);
    }

    [Fact]
    public async Task Compras_simultaneas_nunca_ultrapassam_o_limite_do_cartao()
    {
        // 30 compras de 100 disputando um limite de 1.000: no máximo 10 podem passar.
        var contaId = await _client.AbrirContaAsync("CartaoCredito", 1000m);

        var respostas = await Task.WhenAll(Enumerable.Range(0, 30)
            .Select(i => _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Compra", valor = 100m }, $"compra-{i}")));

        var aprovadas = respostas.Count(r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(respostas, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity, HttpStatusCode.Conflict }));
        Assert.InRange(aprovadas, 1, 10);

        var conta = await (await _client.GetAsync($"/contas/{contaId}")).LerAsync<ContaResposta>();
        Assert.Equal(-100m * aprovadas, conta.Saldo);
        Assert.True(conta.Saldo >= -1000m);
        Assert.Equal(aprovadas, await Banco.LancamentosDaContaAsync(Postgres, contaId));
        Assert.Equal(aprovadas, await Banco.OutboxDaContaAsync(Postgres, contaId));
    }

    [Fact]
    public async Task Debito_recusado_nao_grava_lancamento_nem_evento()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);

        var resposta = await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Saque", valor = 10m }, "saque-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("saldo_insuficiente", (await resposta.LerAsync<ProblemaResposta>()).Codigo);
        Assert.Equal(0, await Banco.LancamentosDaContaAsync(Postgres, contaId));
        Assert.Equal(0, await Banco.OutboxDaContaAsync(Postgres, contaId));
    }

    [Fact]
    public async Task Operacao_de_outro_produto_retorna_422()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 1000m);

        var resposta = await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Compra", valor = 10m }, "c-1");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, resposta.StatusCode);
        Assert.Equal("operacao_nao_permitida", (await resposta.LerAsync<ProblemaResposta>()).Codigo);
    }

    [Fact]
    public async Task Conta_inexistente_retorna_404()
    {
        var resposta = await _client.PostAsync($"/contas/{Guid.NewGuid()}/lancamentos", new { operacao = "Deposito", valor = 10m }, "x");

        Assert.Equal(HttpStatusCode.NotFound, resposta.StatusCode);
    }

    [Fact]
    public async Task Estorno_devolve_o_limite_e_so_pode_acontecer_uma_vez()
    {
        var contaId = await _client.AbrirContaAsync("CartaoCredito", 500m);
        var compra = await (await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Compra", valor = 200m }, "compra")).LerAsync<LancamentoResposta>();

        var estorno = await _client.PostAsync($"/contas/{contaId}/lancamentos/{compra.Id}/estorno", new { motivo = "cobrança indevida" }, "estorno-1");
        var outroEstorno = await _client.PostAsync($"/contas/{contaId}/lancamentos/{compra.Id}/estorno", new { motivo = "de novo" }, "estorno-2");

        Assert.Equal(HttpStatusCode.Created, estorno.StatusCode);
        Assert.Equal(compra.Id, (await estorno.LerAsync<LancamentoResposta>()).LancamentoOriginalId);
        Assert.Equal(HttpStatusCode.Conflict, outroEstorno.StatusCode);
        Assert.Equal("ja_estornado", (await outroEstorno.LerAsync<ProblemaResposta>()).Codigo);

        var conta = await (await _client.GetAsync($"/contas/{contaId}")).LerAsync<ContaResposta>();
        Assert.Equal(0m, conta.Saldo);
    }

    [Fact]
    public async Task Valor_com_tres_casas_decimais_retorna_400()
    {
        var contaId = await _client.AbrirContaAsync("ContaDigital", 0);

        var resposta = await _client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 10.001m }, "v-1");

        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal("valor_invalido", (await resposta.LerAsync<ProblemaResposta>()).Codigo);
    }
}
