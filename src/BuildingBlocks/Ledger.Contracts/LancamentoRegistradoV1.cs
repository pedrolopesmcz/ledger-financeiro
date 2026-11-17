namespace Ledger.Contracts;

/// <summary>
/// Evento publicado pelo serviço de Lançamentos sempre que um lançamento é
/// gravado. Enums viajam como texto para não acoplar o consumidor ao domínio
/// do produtor.
/// </summary>
public sealed record LancamentoRegistradoV1(
    Guid EventId,
    Guid LancamentoId,
    Guid ContaId,
    string TipoConta,
    string Operacao,
    string Natureza,
    decimal Valor,
    decimal ValorComSinal,
    decimal SaldoApos,
    DateTimeOffset OcorridoEm,
    Guid? LancamentoOriginalId)
{
    /// <summary>Usado como tipo da mensagem e como routing key no broker.</summary>
    public const string Tipo = "lancamento.registrado.v1";
}
