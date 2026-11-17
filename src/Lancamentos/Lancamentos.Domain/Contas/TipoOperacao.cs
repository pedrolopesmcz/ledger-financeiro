namespace Lancamentos.Domain.Contas;

/// <summary>
/// Operação de negócio que originou o lançamento. Quais operações são
/// aceitas depende do tipo de conta (ver políticas).
/// </summary>
public enum TipoOperacao
{
    // Conta digital
    Deposito = 1,
    Saque = 2,
    PixRecebido = 3,
    PixEnviado = 4,

    // Cartão de crédito
    Compra = 10,
    PagamentoFatura = 11,

    // Núcleo: válida para qualquer tipo de conta (correção contábil)
    Estorno = 99,
}

public static class TipoOperacaoExtensions
{
    /// <summary>
    /// Natureza de cada operação. O estorno não tem natureza própria:
    /// ela é sempre a inversa do lançamento estornado.
    /// </summary>
    public static Natureza NaturezaPadrao(this TipoOperacao operacao) => operacao switch
    {
        TipoOperacao.Deposito or TipoOperacao.PixRecebido or TipoOperacao.PagamentoFatura => Natureza.Credito,
        TipoOperacao.Saque or TipoOperacao.PixEnviado or TipoOperacao.Compra => Natureza.Debito,
        TipoOperacao.Estorno => throw new InvalidOperationException("A natureza do estorno depende do lançamento original."),
        _ => throw new ArgumentOutOfRangeException(nameof(operacao), operacao, "Operação desconhecida."),
    };
}
