using Consolidado.Api;
using Consolidado.Infrastructure;
using Ledger.Postgres;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddConsolidadoLeitura(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("Banco:MigrarAoIniciar", true))
{
    await MigradorDeBanco.MigrarAsync(
        app.Services.GetRequiredService<NpgsqlDataSource>(),
        typeof(ProjetorPostgres).Assembly,
        app.Logger,
        app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
app.MapConsolidadoEndpoints();

await app.RunAsync().ConfigureAwait(false);
