using System.Text.Json.Serialization;
using Lancamentos.Api;
using Lancamentos.Infrastructure;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddLancamentos(builder.Configuration);
builder.Services.AddHealthChecks()
    .AddCheck<BancoDeDadosHealthCheck>("postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Configuration.GetValue("Banco:MigrarAoIniciar", true))
{
    await MigradorDeBanco.MigrarAsync(
        app.Services.GetRequiredService<NpgsqlDataSource>(),
        app.Logger,
        app.Lifetime.ApplicationStopping).ConfigureAwait(false);
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new() { Predicate = c => c.Tags.Contains("ready") });
app.MapLancamentosEndpoints();

await app.RunAsync().ConfigureAwait(false);
