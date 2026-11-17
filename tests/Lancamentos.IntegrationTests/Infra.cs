using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;

namespace Lancamentos.IntegrationTests;

/// <summary>Sobe PostgreSQL e RabbitMQ reais em containers, uma vez para todos os testes.</summary>
public sealed class Containers : IAsyncLifetime
{
    public PostgreSqlContainer Postgres { get; } = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("lancamentos")
        .Build();

    public RabbitMqContainer RabbitMq { get; } = new RabbitMqBuilder()
        .WithImage("rabbitmq:4-management-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(Postgres.StartAsync(), RabbitMq.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Postgres.DisposeAsync();
        await RabbitMq.DisposeAsync();
    }
}

[CollectionDefinition(Nome)]
public sealed class ColecaoIntegracao : ICollectionFixture<Containers>
{
    public const string Nome = "integracao";
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _postgres;
    private readonly string _rabbitMq;

    public ApiFactory(string postgres, string rabbitMq)
    {
        _postgres = postgres;
        _rabbitMq = rabbitMq;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Lancamentos", _postgres);
        builder.UseSetting("RabbitMq:Uri", _rabbitMq);

        // O relay é acionado manualmente nos testes, para controlar o momento da publicação.
        builder.UseSetting("Outbox:Habilitado", "false");
    }
}

public static class Http
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<HttpResponseMessage> PostAsync(this HttpClient client, string url, object corpo, string? chave)
    {
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(corpo, options: Json),
        };

        if (chave is not null)
        {
            requisicao.Headers.Add("Idempotency-Key", chave);
        }

        return await client.SendAsync(requisicao);
    }

    public static async Task<T> LerAsync<T>(this HttpResponseMessage resposta) =>
        (await resposta.Content.ReadFromJsonAsync<T>(Json))!;

    public static async Task<Guid> AbrirContaAsync(this HttpClient client, string tipo, decimal limite)
    {
        var resposta = await client.PostAsJsonAsync("/contas", new { tipo, limite }, Json);
        resposta.EnsureSuccessStatusCode();
        return (await resposta.LerAsync<ContaResposta>()).Id;
    }
}

public sealed record ContaResposta(Guid Id, string Tipo, decimal Saldo, decimal Limite, decimal Disponivel);

public sealed record LancamentoResposta(Guid Id, Guid ContaId, string Operacao, string Natureza, decimal Valor, decimal SaldoApos, Guid? LancamentoOriginalId);

public sealed record ProblemaResposta(int Status, string Title, string Codigo);

public static class Banco
{
    public static async Task<long> ContarAsync(string connectionString, string sql, Guid contaId)
    {
        await using var conexao = new NpgsqlConnection(connectionString);
        await conexao.OpenAsync();
        await using var comando = new NpgsqlCommand(sql, conexao);
        comando.Parameters.AddWithValue("c", contaId);
        return (long)(await comando.ExecuteScalarAsync())!;
    }

    public static Task<long> LancamentosDaContaAsync(string cs, Guid contaId) =>
        ContarAsync(cs, "SELECT count(*) FROM lancamentos WHERE conta_id = @c", contaId);

    public static Task<long> OutboxDaContaAsync(string cs, Guid contaId) =>
        ContarAsync(cs, "SELECT count(*) FROM outbox WHERE (payload->>'contaId')::uuid = @c", contaId);

    public static Task<long> OutboxPendenteDaContaAsync(string cs, Guid contaId) =>
        ContarAsync(cs, "SELECT count(*) FROM outbox WHERE (payload->>'contaId')::uuid = @c AND publicada_em IS NULL", contaId);
}
