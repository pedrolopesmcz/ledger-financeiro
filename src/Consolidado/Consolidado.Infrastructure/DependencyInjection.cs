using Consolidado.Core;
using Ledger.Messaging;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Consolidado.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Dependências comuns à API e ao Worker.</summary>
    public static IServiceCollection AddConsolidadoComum(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var postgres = configuration.GetConnectionString("Consolidado")
            ?? throw new InvalidOperationException("ConnectionStrings:Consolidado não configurada.");
        var redis = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis não configurada.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(postgres));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new DiaContabil(configuration["DiaContabil:Fuso"] ?? DiaContabil.FusoPadrao));

        var opcoesDeCache = configuration.GetSection(OpcoesDeCache.Secao).Get<OpcoesDeCache>() ?? new OpcoesDeCache();
        services.AddSingleton(opcoesDeCache);

        // Timeouts curtos: Redis lento não pode segurar a requisição; o circuit breaker cuida do resto.
        services.AddStackExchangeRedisCache(o =>
        {
            o.Configuration = redis;
            o.InstanceName = "ledger:";
        });
        services.AddSingleton<CacheRedis>();
        services.AddSingleton<ICacheDeConsultas>(sp => new CacheResiliente(
            sp.GetRequiredService<CacheRedis>(),
            sp.GetRequiredService<TimeProvider>(),
            opcoesDeCache.PausaDoCircuito));

        return services;
    }

    public static IServiceCollection AddConsolidadoLeitura(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddConsolidadoComum(configuration);
        services.AddSingleton<IConsultasDeSaldo, ConsultasPostgres>();
        services.AddSingleton<ServicoDeConsulta>();
        return services;
    }

    public static IServiceCollection AddConsolidadoProjecao(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddConsolidadoComum(configuration);
        services.AddSingleton<IProjetorDeSaldos, ProjetorPostgres>();
        services.AddSingleton<InvalidadorDeCache>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Secao));
        services.Configure<OpcoesDoConsumidor>(configuration.GetSection(OpcoesDoConsumidor.Secao));
        services.AddSingleton(configuration.GetSection(PoliticaDeRetentativa.Secao).Get<PoliticaDeRetentativa>() ?? new PoliticaDeRetentativa());
        services.AddSingleton<ConsumidorDeLancamentos>();
        services.AddHostedService(sp => sp.GetRequiredService<ConsumidorDeLancamentos>());
        return services;
    }
}
