using Microsoft.Extensions.Logging;
using Npgsql;

namespace Lancamentos.Infrastructure;

/// <summary>
/// Aplica os scripts SQL embutidos (Migracoes/*.sql) em ordem, uma única vez.
/// Um advisory lock impede que duas instâncias migrem ao mesmo tempo.
/// Em produção isso rodaria como um job de deploy separado, antes da aplicação.
/// </summary>
public static partial class MigradorDeBanco
{
    private const long ChaveDoLock = 7_310_001;

    public static async Task MigrarAsync(NpgsqlDataSource dataSource, ILogger logger, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        await using var conexao = await dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);

        await ExecutarAsync(conexao, $"SELECT pg_advisory_lock({ChaveDoLock})", ct).ConfigureAwait(false);
        try
        {
            await ExecutarAsync(
                conexao,
                "CREATE TABLE IF NOT EXISTS schema_migracoes (versao varchar(200) PRIMARY KEY, aplicada_em timestamptz NOT NULL DEFAULT now())",
                ct).ConfigureAwait(false);

            var assembly = typeof(MigradorDeBanco).Assembly;
            var scripts = assembly.GetManifestResourceNames()
                .Where(n => n.EndsWith(".sql", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal);

            foreach (var nome in scripts)
            {
                await using var verifica = new NpgsqlCommand("SELECT 1 FROM schema_migracoes WHERE versao = @v", conexao);
                verifica.Parameters.AddWithValue("v", nome);
                if (await verifica.ExecuteScalarAsync(ct).ConfigureAwait(false) is not null)
                {
                    continue;
                }

                await using var stream = assembly.GetManifestResourceStream(nome)!;
                using var leitor = new StreamReader(stream);
                var sql = await leitor.ReadToEndAsync(ct).ConfigureAwait(false);

                await using var transacao = await conexao.BeginTransactionAsync(ct).ConfigureAwait(false);
                await using (var comando = new NpgsqlCommand(sql, conexao, transacao))
                {
                    await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await using (var registra = new NpgsqlCommand("INSERT INTO schema_migracoes (versao) VALUES (@v)", conexao, transacao))
                {
                    registra.Parameters.AddWithValue("v", nome);
                    await registra.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                }

                await transacao.CommitAsync(ct).ConfigureAwait(false);
                LogMigracaoAplicada(logger, nome);
            }
        }
        finally
        {
            await ExecutarAsync(conexao, $"SELECT pg_advisory_unlock({ChaveDoLock})", CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task ExecutarAsync(NpgsqlConnection conexao, string sql, CancellationToken ct)
    {
        await using var comando = new NpgsqlCommand(sql, conexao);
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Migração aplicada: {Migracao}")]
    private static partial void LogMigracaoAplicada(ILogger logger, string migracao);
}
