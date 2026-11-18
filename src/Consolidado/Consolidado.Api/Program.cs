using Consolidado.Api;
using Consolidado.Infrastructure;
using Ledger.Observabilidade;
using Ledger.Postgres;
using Ledger.Seguranca;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.AddLedgerObservabilidade("consolidado-api");

builder.Services.AddProblemDetails();
builder.Services.AddConsolidadoLeitura(builder.Configuration);
builder.Services.AddLedgerSeguranca(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks()
    .AddCheck<PostgresHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue("Banco:MigrarAoIniciar", true))
{
    await MigradorDeBanco.MigrarAsync(
        app.Services.GetRequiredService<NpgsqlDataSource>(),
        typeof(ProjetorPostgres).Assembly,
        app.Logger,
        app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapConsolidadoEndpoints();

await app.RunAsync().ConfigureAwait(false);
