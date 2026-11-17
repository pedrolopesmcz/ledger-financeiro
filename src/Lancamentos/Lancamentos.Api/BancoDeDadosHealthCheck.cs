using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace Lancamentos.Api;

internal sealed class BancoDeDadosHealthCheck : IHealthCheck
{
    private readonly NpgsqlDataSource _dataSource;

    public BancoDeDadosHealthCheck(NpgsqlDataSource dataSource) => _dataSource = dataSource;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var comando = _dataSource.CreateCommand("SELECT 1");
            await comando.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL indisponível", ex);
        }
    }
}
