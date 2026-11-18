using Consolidado.Core;
using Ledger.Contracts;
using Npgsql;

namespace Consolidado.Infrastructure;

/// <summary>
/// Aplica um evento às projeções numa única transação. A soma de créditos e
/// débitos é comutativa (a ordem de chegada não muda o total), então vários
/// consumidores podem processar em paralelo. A única parte sensível à ordem é
/// o "rolamento" do saldo para os dias seguintes quando chega um evento
/// atrasado; por isso eventos da MESMA conta são serializados por um advisory
/// lock da transação, enquanto contas diferentes seguem em paralelo.
/// </summary>
public sealed class ProjetorPostgres : IProjetorDeSaldos
{
    private const string SqlMovimento = """
        INSERT INTO movimentos (event_id, lancamento_id, conta_id, valor_com_sinal, ocorrido_em, dia)
        VALUES (@evento, @lancamento, @conta, @delta, @ocorrido, @dia)
        ON CONFLICT DO NOTHING
        """;

    private const string SqlDia = """
        INSERT INTO saldos_diarios (conta_id, dia, creditos, debitos, saldo_final, quantidade, atualizado_em)
        VALUES (
            @conta, @dia, @creditos, @debitos,
            COALESCE((SELECT s.saldo_final FROM saldos_diarios s WHERE s.conta_id = @conta AND s.dia < @dia ORDER BY s.dia DESC LIMIT 1), 0) + @delta,
            1, now())
        ON CONFLICT (conta_id, dia) DO UPDATE SET
            creditos      = saldos_diarios.creditos + EXCLUDED.creditos,
            debitos       = saldos_diarios.debitos + EXCLUDED.debitos,
            saldo_final   = saldos_diarios.saldo_final + @delta,
            quantidade    = saldos_diarios.quantidade + 1,
            atualizado_em = now()
        """;

    private const string SqlDiasSeguintes = """
        UPDATE saldos_diarios SET saldo_final = saldo_final + @delta, atualizado_em = now()
        WHERE conta_id = @conta AND dia > @dia
        """;

    private const string SqlPosicao = """
        INSERT INTO posicoes (conta_id, titular_id, saldo, posicao_em, atualizada_em)
        VALUES (@conta, @titular, @delta, @ocorrido, now())
        ON CONFLICT (conta_id) DO UPDATE SET
            saldo         = posicoes.saldo + @delta,
            posicao_em    = GREATEST(posicoes.posicao_em, EXCLUDED.posicao_em),
            atualizada_em = now()
        """;

    private readonly NpgsqlDataSource _dataSource;
    private readonly DiaContabil _diaContabil;

    public ProjetorPostgres(NpgsqlDataSource dataSource, DiaContabil diaContabil)
    {
        _dataSource = dataSource;
        _diaContabil = diaContabil;
    }

    public async Task<bool> ProjetarAsync(LancamentoRegistradoV1 evento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evento);

        var delta = evento.ValorComSinal;
        var dia = _diaContabil.De(evento.OcorridoEm);

        await using var conexao = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transacao = await conexao.BeginTransactionAsync(ct).ConfigureAwait(false);

        await using (var trava = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@conta::text, 0))", conexao, transacao))
        {
            trava.Parameters.AddWithValue("conta", evento.ContaId);
            await trava.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await using (var movimento = new NpgsqlCommand(SqlMovimento, conexao, transacao))
        {
            movimento.Parameters.AddWithValue("evento", evento.EventId);
            movimento.Parameters.AddWithValue("lancamento", evento.LancamentoId);
            movimento.Parameters.AddWithValue("conta", evento.ContaId);
            movimento.Parameters.AddWithValue("delta", delta);
            movimento.Parameters.AddWithValue("ocorrido", evento.OcorridoEm.ToUniversalTime());
            movimento.Parameters.AddWithValue("dia", dia);

            if (await movimento.ExecuteNonQueryAsync(ct).ConfigureAwait(false) == 0)
            {
                // Duplicata: o evento já foi aplicado. Nada a fazer.
                await transacao.RollbackAsync(ct).ConfigureAwait(false);
                return false;
            }
        }

        await ExecutarAsync(SqlDia, conexao, transacao, evento, dia, ct).ConfigureAwait(false);
        await ExecutarAsync(SqlDiasSeguintes, conexao, transacao, evento, dia, ct).ConfigureAwait(false);
        await ExecutarAsync(SqlPosicao, conexao, transacao, evento, dia, ct).ConfigureAwait(false);

        await transacao.CommitAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static async Task ExecutarAsync(string sql, NpgsqlConnection conexao, NpgsqlTransaction transacao, LancamentoRegistradoV1 evento, DateOnly dia, CancellationToken ct)
    {
        var delta = evento.ValorComSinal;
        await using var comando = new NpgsqlCommand(sql, conexao, transacao);
        comando.Parameters.AddWithValue("conta", evento.ContaId);
        comando.Parameters.AddWithValue("dia", dia);
        comando.Parameters.AddWithValue("delta", delta);
        comando.Parameters.AddWithValue("creditos", delta > 0 ? delta : 0m);
        comando.Parameters.AddWithValue("debitos", delta < 0 ? -delta : 0m);
        comando.Parameters.AddWithValue("ocorrido", evento.OcorridoEm.ToUniversalTime());
        comando.Parameters.AddWithValue("titular", evento.TitularId);
        await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
