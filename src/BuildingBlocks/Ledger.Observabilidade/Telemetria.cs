using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Ledger.Observabilidade;

/// <summary>Fontes de traces e métricas de negócio, compartilhadas pelos serviços.</summary>
public static class Telemetria
{
    public const string Nome = "Ledger";

    public static readonly ActivitySource Fonte = new(Nome);

    public static readonly Meter Medidor = new(Nome);

    /// <summary>Lê o traceparent gravado na outbox ou recebido no cabeçalho da mensagem.</summary>
    public static ActivityContext ContextoPai(string? traceParent) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, null, out var contexto) ? contexto : default;

    /// <summary>
    /// Traces, métricas e logs via OpenTelemetry. Se OTEL_EXPORTER_OTLP_ENDPOINT
    /// estiver definido, exporta por OTLP (no docker compose, para o Aspire
    /// Dashboard); senão, só registra localmente. Logs também saem em JSON no console.
    /// </summary>
    public static IHostApplicationBuilder AddLedgerObservabilidade(this IHostApplicationBuilder builder, string servico)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var exportar = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        builder.Logging.AddJsonConsole(o => o.IncludeScopes = true);
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(servico))
            .WithTracing(t => t
                .AddSource(Nome)
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal))
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(m => m
                .AddMeter(Nome)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        if (exportar)
        {
            otel.UseOtlpExporter();
        }

        return builder;
    }
}
