using System.Text.Json.Serialization;
using Lancamentos.Api;
using Lancamentos.Infrastructure;
using Ledger.Observabilidade;
using Ledger.Postgres;
using Ledger.Seguranca;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.AddLedgerObservabilidade("lancamentos-api");

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddLancamentos(builder.Configuration);
builder.Services.AddLedgerSeguranca(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks()
    .AddCheck<BancoDeDadosHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue("Banco:MigrarAoIniciar", true))
{
    await MigradorDeBanco.MigrarAsync(
        app.Services.GetRequiredService<NpgsqlDataSource>(),
        typeof(LancamentosDbContext).Assembly,
        app.Logger,
        app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false }).AllowAnonymous();
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") }).AllowAnonymous();
app.MapLancamentosEndpoints();

await app.RunAsync().ConfigureAwait(false);
