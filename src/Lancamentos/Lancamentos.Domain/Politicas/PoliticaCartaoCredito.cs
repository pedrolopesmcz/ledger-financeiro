using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;

namespace Lancamentos.Domain.Politicas;

/// <summary>
/// Cartão de crédito: o saldo negativo representa o valor utilizado do
/// limite. Uma compra é autorizada se couber no limite disponível.
/// Pagamento de fatura é crédito e devolve limite; pagar a mais gera
/// saldo credor para o cliente.
/// </summary>
public sealed class PoliticaCartaoCredito : IPoliticaDeConta
{
    private static readonly HashSet<TipoOperacao> Operacoes =
    [
        TipoOperacao.Compra,
        TipoOperacao.PagamentoFatura,
        TipoOperacao.Estorno,
    ];

    public TipoConta Tipo => TipoConta.CartaoCredito;

    public bool Aceita(TipoOperacao operacao) => Operacoes.Contains(operacao);

    public Erro? AutorizarDebito(Conta conta, ValorMonetario valor)
    {
        ArgumentNullException.ThrowIfNull(conta);

        var limiteDisponivel = conta.Limite + conta.Saldo;
        return limiteDisponivel >= valor.Quantia
            ? null
            : new Erro("limite_insuficiente", $"Limite disponível ({limiteDisponivel:N2}) insuficiente para a compra.");
    }
}
