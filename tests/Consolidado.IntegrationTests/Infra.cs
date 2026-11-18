using System.Text;
using System.Text.Json;
using Consolidado.Infrastructure;
using Ledger.Contracts;
using Ledger.Messaging;
using Ledger.Postgres;
using Ledger.TestesComuns;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using RabbitMQ.Client;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace Consolidado.IntegrationTests;

public sealed class Containers : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder().WithImage("postgres:17-alpine").WithDatabase("consolidado").Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder().WithImage("rabbitmq:4-management-alpine").Build();

    public RedisContainer Redis { get; } = new RedisBuilder().WithImage("redis:7-alpine").Build();

    public string RedisConnection => Redis.GetConnectionString() + ",abortConnect=false,connectTimeout=500,syncTimeout=500,asyncTimeout=500";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync(), Redis.StartAsync());
        await using var dataSource = NpgsqlDataSource.Create(Postgres.GetConnectionString());
        await MigradorDeBanco.MigrarAsync(dataSource, typeof(ProjetorPostgres).Assembly, NullLogger.Instance, CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
        await Redis.DisposeAsync();
    }

    public Dictionary<string, string?> Configuracao(string? redis = null) => new(Tokens.Configuracao)
    {
        ["ConnectionStrings:Consolidado"] = Postgres.GetConnectionString(),
        ["ConnectionStrings:Redis"] = redis ?? RedisConnection,
        ["RabbitMq:Uri"] = RabbitMq.GetConnectionString(),
        ["Retentativa:Atraso"] = "00:00:00.200",
        ["Retentativa:MaximoDeTentativas"] = "5",
        ["Consumidor:EsperaParaReconectar"] = "00:00:01",
        ["Banco:MigrarAoIniciar"] = "false",

        // Validade longa de propósito: se o saldo mudar, é porque a invalidação funcionou, não o TTL.
        ["Cache:ValidadeCurta"] = "01:00:00",
    };

    /// <summary>Sobe o Worker de consolidação com a mesma configuração de DI da aplicação real.</summary>
    public IHost CriarWorker(Action<IServiceCollection>? ajustes = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Configuration.AddInMemoryCollection(Configuracao());
        builder.Services.AddConsolidadoProjecao(builder.Configuration);
        ajustes?.Invoke(builder.Services);
        return builder.Build();
    }

    public async Task PublicarAsync(LancamentoRegistradoV1 evento)
    {
        await PublicarBrutoAsync(JsonSerializer.SerializeToUtf8Bytes(evento, JsonSerializerOptions.Web), evento.EventId.ToString());
    }

    public async Task PublicarBrutoAsync(byte[] corpo, string messageId)
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(RabbitMq.GetConnectionString()) };
        await using var conexao = await fabrica.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync(new CreateChannelOptions(true, true));
        await Topologia.DeclararConsolidadoAsync(canal, CancellationToken.None);
        var propriedades = new BasicProperties { MessageId = messageId, Type = LancamentoRegistradoV1.Tipo, DeliveryMode = DeliveryModes.Persistent };
        await canal.BasicPublishAsync(Topologia.Exchange, LancamentoRegistradoV1.Tipo, mandatory: true, propriedades, corpo);
    }

    public async Task<bool> MensagemNaDlqAsync(string messageId)
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(RabbitMq.GetConnectionString()) };
        await using var conexao = await fabrica.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();
        while (await canal.BasicGetAsync(Topologia.FilaConsolidadoDlq, autoAck: true) is { } mensagem)
        {
            if (mensagem.BasicProperties.MessageId == messageId)
            {
                return true;
            }
        }

        return false;
    }

    public async Task<uint> MensagensNaFilaAsync()
    {
        var fabrica = new ConnectionFactory { Uri = new Uri(RabbitMq.GetConnectionString()) };
        await using var conexao = await fabrica.CreateConnectionAsync();
        await using var canal = await conexao.CreateChannelAsync();
        return await canal.MessageCountAsync(Topologia.FilaConsolidado);
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoIntegracao : ICollectionFixture<Containers>
{
    public const string Nome = "consolidado";
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly Dictionary<string, string?> _config;

    public ApiFactory(Dictionary<string, string?> config) => _config = config;

    public HttpClient ClienteBackoffice() => CreateClient().ComToken(Tokens.Backoffice());

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (chave, valor) in _config)
        {
            builder.UseSetting(chave, valor);
        }
    }
}

public static class Eventos
{
    /// <summary>Data/hora em Brasília, para os testes lerem como o negócio lê.</summary>
    public static DateTimeOffset Brasilia(int dia, int hora, int minuto = 0) =>
        new(2026, 11, dia, hora, minuto, 0, TimeSpan.FromHours(-3));

    public static LancamentoRegistradoV1 Novo(Guid conta, decimal valorComSinal, DateTimeOffset quando, Guid? titular = null) => new(
        EventId: Guid.NewGuid(),
        LancamentoId: Guid.NewGuid(),
        ContaId: conta,
        TipoConta: "ContaDigital",
        Operacao: valorComSinal > 0 ? "Deposito" : "Saque",
        Natureza: valorComSinal > 0 ? "Credito" : "Debito",
        Valor: Math.Abs(valorComSinal),
        ValorComSinal: valorComSinal,
        SaldoApos: 0m,
        OcorridoEm: quando,
        LancamentoOriginalId: null,
        TitularId: titular ?? Guid.Empty);
}

public static class Esperar
{
    public static async Task AteAsync(Func<Task<bool>> condicao, TimeSpan limite, string descricao)
    {
        var fim = DateTime.UtcNow + limite;
        while (DateTime.UtcNow < fim)
        {
            if (await condicao())
            {
                return;
            }

            await Task.Delay(200);
        }

        throw new Xunit.Sdk.XunitException($"Tempo esgotado esperando: {descricao}");
    }

    public static string Texto(this byte[] bytes) => Encoding.UTF8.GetString(bytes);
}
