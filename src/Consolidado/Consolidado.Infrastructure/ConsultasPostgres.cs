using Consolidado.Core;
using Npgsql;

namespace Consolidado.Infrastructure;

public sealed class ConsultasPostgres : IConsultasDeSaldo
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly DiaContabil _diaContabil;

    public ConsultasPostgres(NpgsqlDataSource dataSource, DiaContabil diaContabil)
    {
        _dataSource = dataSource;
        _diaContabil = diaContabil;
    }

    public async Task<PosicaoAtual?> ObterPosicaoAsync(Guid contaId, CancellationToken ct)
    {
        await using var comando = _dataSource.CreateCommand("SELECT saldo, posicao_em, atualizada_em FROM posicoes WHERE conta_id = @c");
        comando.Parameters.AddWithValue("c", contaId);
        await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);

        return await leitor.ReadAsync(ct).ConfigureAwait(false)
            ? new PosicaoAtual(contaId, leitor.GetDecimal(0), leitor.GetFieldValue<DateTimeOffset>(1), leitor.GetFieldValue<DateTimeOffset>(2))
            : null;
    }

    public async Task<ConsolidadoDiario?> ObterConsolidadoAsync(Guid contaId, DateOnly dia, CancellationToken ct)
    {
        var posicao = await ObterPosicaoAsync(contaId, ct).ConfigureAwait(false);
        if (posicao is null)
        {
            return null;
        }

        // Último dia com movimento até a data pedida (ele mesmo, ou o anterior mais recente).
        await using var comando = _dataSource.CreateCommand("""
            SELECT dia, creditos, debitos, saldo_final, quantidade
            FROM saldos_diarios
            WHERE conta_id = @c AND dia <= @d
            ORDER BY dia DESC
            LIMIT 1
            """);
        comando.Parameters.AddWithValue("c", contaId);
        comando.Parameters.AddWithValue("d", dia);
        await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);

        if (!await leitor.ReadAsync(ct).ConfigureAwait(false))
        {
            // A conta só teve movimentos depois desta data.
            return new ConsolidadoDiario(contaId, dia, 0m, 0m, 0m, 0m, 0, posicao.PosicaoEm);
        }

        var diaEncontrado = leitor.GetFieldValue<DateOnly>(0);
        var saldoFinal = leitor.GetDecimal(3);

        if (diaEncontrado < dia)
        {
            // Dia sem movimento: o saldo apenas é carregado do dia anterior.
            return new ConsolidadoDiario(contaId, dia, saldoFinal, 0m, 0m, saldoFinal, 0, posicao.PosicaoEm);
        }

        var creditos = leitor.GetDecimal(1);
        var debitos = leitor.GetDecimal(2);
        return new ConsolidadoDiario(contaId, dia, saldoFinal - creditos + debitos, creditos, debitos, saldoFinal, leitor.GetInt32(4), posicao.PosicaoEm);
    }

    public async Task<SaldoNoInstante?> ObterSaldoNoInstanteAsync(Guid contaId, DateTimeOffset instante, CancellationToken ct)
    {
        var posicao = await ObterPosicaoAsync(contaId, ct).ConfigureAwait(false);
        if (posicao is null)
        {
            return null;
        }

        var dia = _diaContabil.De(instante);
        await using var comando = _dataSource.CreateCommand("""
            SELECT
                COALESCE((SELECT s.saldo_final FROM saldos_diarios s WHERE s.conta_id = @c AND s.dia < @d ORDER BY s.dia DESC LIMIT 1), 0)
              + COALESCE((SELECT sum(m.valor_com_sinal) FROM movimentos m WHERE m.conta_id = @c AND m.dia = @d AND m.ocorrido_em <= @i), 0)
            """);
        comando.Parameters.AddWithValue("c", contaId);
        comando.Parameters.AddWithValue("d", dia);
        comando.Parameters.AddWithValue("i", instante.ToUniversalTime());

        var saldo = (decimal)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
        return new SaldoNoInstante(contaId, instante, saldo, posicao.PosicaoEm);
    }
}
