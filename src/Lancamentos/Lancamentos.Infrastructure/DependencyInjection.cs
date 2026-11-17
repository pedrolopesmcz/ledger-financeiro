using Lancamentos.Application;
using Lancamentos.Domain.Politicas;
using Ledger.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Lancamentos.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddLancamentos(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("Lancamentos")
            ?? throw new InvalidOperationException("ConnectionStrings:Lancamentos não configurada.");

        services.AddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddDbContext<LancamentosDbContext>((sp, options) =>
            options.UseNpgsql(sp.GetRequiredService<NpgsqlDataSource>()));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IPoliticaDeConta, PoliticaContaDigital>();
        services.AddSingleton<IPoliticaDeConta, PoliticaCartaoCredito>();
        services.AddSingleton<CatalogoDePoliticas>();

        services.AddScoped<IRepositorioDeContas, RepositorioDeContas>();
        services.AddScoped<IUnidadeDeTrabalho, UnidadeDeTrabalhoEf>();
        services.AddScoped<IOutbox, OutboxEf>();
        services.AddScoped<ExecutorIdempotente>();
        services.AddScoped<ServicoDeLancamentos>();

        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.Secao));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Secao));
        services.AddSingleton<IPublicadorDeEventos, RabbitMqPublicador>();
        services.AddSingleton<PublicadorOutbox>();
        services.AddHostedService(sp => sp.GetRequiredService<PublicadorOutbox>());

        return services;
    }
}
