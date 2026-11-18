using System.Net;
using System.Text.Json;
using Lancamentos.Infrastructure;
using Ledger.Contracts;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Lancamentos.IntegrationTests;

[Collection(ColecaoIntegracao.Nome)]
public sealed class OutboxTests
{
    private readonly Containers _containers;

    public OutboxTests(Containers containers) => _containers = containers;

    private string Postgres => _containers.Postgres.GetConnectionString();

    [Fact]
    public async Task Relay_publica_no_broker_com_confirmacao_e_marca_como_publicado()
    {
        var fila = await CriarFilaDeTesteAsync();
        await using var factory = new ApiFactory(Postgres, _containers.RabbitMq.GetConnectionString());
        using var client = factory.ClienteBackoffice();
        var contaId = await client.AbrirContaAsync("ContaDigital", 0);
        var lancamento = await (await client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 42m }, "relay-1"))
            .LerAsync<LancamentoResposta>();

        var relay = factory.Services.GetRequiredService<PublicadorOutbox>();
        await relay.PublicarLoteAsync(CancellationToken.None);

        Assert.Equal(0, await Banco.OutboxPendenteDaContaAsync(Postgres, contaId));
        var evento = await ConsumirEventoDaContaAsync(fila, contaId);
        Assert.Equal(lancamento.Id, evento.LancamentoId);
        Assert.Equal(42m, evento.ValorComSinal);
        Assert.Equal("Credito", evento.Natureza);
    }

    [Fact]
    public async Task Com_o_broker_fora_o_lancamento_e_aceito_e_o_evento_fica_pendente()
    {
        // Porta onde não há broker: simula o RabbitMQ indisponível.
        await using var factory = new ApiFactory(Postgres, "amqp://guest:guest@127.0.0.1:1/");
        using var client = factory.ClienteBackoffice();
        var contaId = await client.AbrirContaAsync("ContaDigital", 0);

        var resposta = await client.PostAsync($"/contas/{contaId}/lancamentos", new { operacao = "Deposito", valor = 10m }, "sem-broker");

        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);

        var relay = factory.Services.GetRequiredService<PublicadorOutbox>();
        await Assert.ThrowsAnyAsync<Exception>(() => relay.PublicarLoteAsync(CancellationToken.None));
        Assert.Equal(1, await Banco.OutboxPendenteDaContaAsync(Postgres, contaId));

        // Broker de volta: o mesmo evento é publicado, nada se perdeu.
        await using var factoryComBroker = new ApiFactory(Postgres, _containers.RabbitMq.GetConnectionString());
        var relayComBroker = factoryComBroker.Services.GetRequiredService<PublicadorOutbox>();
        await CriarFilaDeTesteAsync();
        while (await relayComBroker.PublicarLoteAsync(CancellationToken.None) > 0)
        {
        }

        Assert.Equal(0, await Banco.OutboxPendenteDaContaAsync(Postgres, contaId));
    }

    private async Task<string> CriarFilaDeTesteAsync()
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(_containers.RabbitMq.GetConnectionString()) };
        await using var conexao = await fabrica.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();
        await canal.ExchangeDeclareAsync(Ledger.Messaging.Topologia.Exchange, ExchangeType.Topic, durable: true);
        const string fila = "teste.lancamentos";
        await canal.QueueDeclareAsync(fila, durable: true, exclusive: false, autoDelete: false);
        await canal.QueueBindAsync(fila, Ledger.Messaging.Topologia.Exchange, "lancamento.registrado.*");
        return fila;
    }

    private async Task<LancamentoRegistradoV1> ConsumirEventoDaContaAsync(string fila, Guid contaId)
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(_containers.RabbitMq.GetConnectionString()) };
        await using var conexao = await fabrica.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();

        while (await canal.BasicGetAsync(fila, autoAck: true) is { } mensagem)
        {
            var evento = JsonSerializer.Deserialize<LancamentoRegistradoV1>(mensagem.Body.Span, JsonSerializerOptions.Web)!;
            if (evento.ContaId == contaId)
            {
                Assert.Equal(evento.EventId.ToString(), mensagem.BasicProperties.MessageId);
                return evento;
            }
        }

        throw new Xunit.Sdk.XunitException("Evento da conta não encontrado na fila.");
    }
}
