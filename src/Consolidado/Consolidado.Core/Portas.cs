using Ledger.Contracts;

namespace Consolidado.Core;

public interface IProjetorDeSaldos
{
    /// <summary>
    /// Aplica o evento às projeções. Retorna false se o evento já tinha sido
    /// processado (duplicata): nada é alterado.
    /// </summary>
    Task<bool> ProjetarAsync(LancamentoRegistradoV1 evento, CancellationToken ct);
}

public interface IConsultasDeSaldo
{
    Task<PosicaoAtual?> ObterPosicaoAsync(Guid contaId, CancellationToken ct);

    Task<ConsolidadoDiario?> ObterConsolidadoAsync(Guid contaId, DateOnly dia, CancellationToken ct);

    Task<SaldoNoInstante?> ObterSaldoNoInstanteAsync(Guid contaId, DateTimeOffset instante, CancellationToken ct);
}

/// <summary>Cache de leitura. Implementações nunca devem propagar falha: cache é otimização.</summary>
public interface ICacheDeConsultas
{
    Task<string?> ObterAsync(string chave, CancellationToken ct);

    Task GravarAsync(string chave, string valor, TimeSpan validade, CancellationToken ct);

    Task RemoverAsync(string chave, CancellationToken ct);
}
