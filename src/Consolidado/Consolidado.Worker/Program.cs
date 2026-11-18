using Consolidado.Infrastructure;
using Ledger.Observabilidade;
using Ledger.Postgres;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.AddLedgerObservabilidade("consolidado-worker");

builder.Services.AddConsolidadoProjecao(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Configuration.GetValue("Banco:MigrarAoIniciar", true))
{
    await MigradorDeBanco.MigrarAsync(
        app.Services.GetRequiredService<NpgsqlDataSource>(),
        typeof(ProjetorPostgres).Assembly,
        app.Logger,
        app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapGet("/health/ready", (ConsumidorDeLancamentos consumidor) =>
    consumidor.Conectado ? Results.Ok("Healthy") : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));

await app.RunAsync().ConfigureAwait(false);
