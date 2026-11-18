namespace Consolidado.Core;

/// <summary>
/// Saldo mais recente conhecido pelo Consolidado.
/// PosicaoEm: momento do último lançamento já consolidado. Deixa explícita a
/// consistência eventual para quem consome (ver ADR 0001 e 0011).
/// </summary>
public sealed record PosicaoAtual(Guid ContaId, decimal Saldo, DateTimeOffset PosicaoEm, DateTimeOffset AtualizadaEm);

/// <summary>Fotografia de um dia contábil de uma conta.</summary>
public sealed record ConsolidadoDiario(
    Guid ContaId,
    DateOnly Dia,
    decimal SaldoInicial,
    decimal Creditos,
    decimal Debitos,
    decimal SaldoFinal,
    int Quantidade,
    DateTimeOffset PosicaoEm);

/// <summary>Saldo em um instante: saldo do fim do dia anterior + movimentos do dia até o instante.</summary>
public sealed record SaldoNoInstante(Guid ContaId, DateTimeOffset Instante, decimal Saldo, DateTimeOffset PosicaoEm);
