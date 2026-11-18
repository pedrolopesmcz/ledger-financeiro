using System.Globalization;
using System.Threading.RateLimiting;
using Ledger.Gateway;
using Ledger.Observabilidade;
using Ledger.Seguranca;

var builder = WebApplication.CreateBuilder(args);

builder.AddLedgerObservabilidade("gateway");
builder.Services.AddLedgerSeguranca(builder.Configuration, builder.Environment);
builder.Services.AddHealthChecks();

var limites = builder.Configuration.GetSection(OpcoesDeLimite.Secao).Get<OpcoesDeLimite>() ?? new OpcoesDeLimite();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.OnRejected = (contexto, _) =>
    {
        if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar))
        {
            contexto.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(esperar.TotalSeconds).ToString(CultureInfo.InvariantCulture);
        }

        return ValueTask.CompletedTask;
    };

    // Token bucket por cliente: permite rajadas curtas e limita a taxa sustentada.
    // Um cliente abusivo recebe 429 sem afetar os demais nem sobrecarregar os serviços.
    o.AddPolicy(OpcoesDeLimite.PoliticaPorCliente, http => RateLimitPartition.GetTokenBucketLimiter(
        OpcoesDeLimite.ChaveDoCliente(http),
        _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = limites.Rajada,
            TokensPerPeriod = limites.PorSegundo,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});

builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health/live").AllowAnonymous();
app.MapReverseProxy();

await app.RunAsync().ConfigureAwait(false);
