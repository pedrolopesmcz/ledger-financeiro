using Lancamentos.Domain.Contas;
using Ledger.Contracts;

namespace Lancamentos.Application;

public sealed record ContaDto(Guid Id, Guid TitularId, TipoConta Tipo, decimal Saldo, decimal Limite, decimal Disponivel, DateTimeOffset AbertaEm)
{
    public static ContaDto De(Conta conta)
    {
        ArgumentNullException.ThrowIfNull(conta);
        return new(conta.Id, conta.TitularId, conta.Tipo, conta.Saldo, conta.Limite, conta.Saldo + conta.Limite, conta.AbertaEm);
    }
}

public sealed record LancamentoDto(
    Guid Id,
    Guid ContaId,
    TipoOperacao Operacao,
    Natureza Natureza,
    decimal Valor,
    decimal SaldoApos,
    string? Descricao,
    Guid? LancamentoOriginalId,
    DateTimeOffset OcorridoEm)
{
    public static LancamentoDto De(Lancamento l)
    {
        ArgumentNullException.ThrowIfNull(l);
        return new(l.Id, l.ContaId, l.Operacao, l.Natureza, l.Valor, l.SaldoApos, l.Descricao, l.LancamentoOriginalId, l.OcorridoEm);
    }
}

/// <summary>
/// Resultado de um comando idempotente. Repetido = true quando a mesma
/// chave já tinha sido processada e devolvemos o resultado original.
/// </summary>
public sealed record RespostaLancamento(LancamentoDto Lancamento, bool Repetido);

internal static class Eventos
{
    public static LancamentoRegistradoV1 De(Lancamento l, Conta conta) => new(
        EventId: Guid.CreateVersion7(),
        LancamentoId: l.Id,
        ContaId: l.ContaId,
        TipoConta: conta.Tipo.ToString(),
        Operacao: l.Operacao.ToString(),
        Natureza: l.Natureza.ToString(),
        Valor: l.Valor,
        ValorComSinal: l.ValorComSinal,
        SaldoApos: l.SaldoApos,
        OcorridoEm: l.OcorridoEm,
        LancamentoOriginalId: l.LancamentoOriginalId,
        TitularId: conta.TitularId);
}
