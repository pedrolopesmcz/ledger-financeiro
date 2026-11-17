using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;

namespace Lancamentos.Domain.Politicas;

/// <summary>
/// Conta digital: o saldo é dinheiro do cliente. O limite é o cheque
/// especial (zero significa que a conta não pode ficar negativa).
/// </summary>
public sealed class PoliticaContaDigital : IPoliticaDeConta
{
    private static readonly HashSet<TipoOperacao> Operacoes =
    [
        TipoOperacao.Deposito,
        TipoOperacao.Saque,
        TipoOperacao.PixRecebido,
        TipoOperacao.PixEnviado,
        TipoOperacao.Estorno,
    ];

    public TipoConta Tipo => TipoConta.ContaDigital;

    public bool Aceita(TipoOperacao operacao) => Operacoes.Contains(operacao);

    public Erro? AutorizarDebito(Conta conta, ValorMonetario valor)
    {
        ArgumentNullException.ThrowIfNull(conta);

        var disponivel = conta.Saldo + conta.Limite;
        return disponivel >= valor.Quantia
            ? null
            : new Erro("saldo_insuficiente", $"Saldo disponível ({disponivel:N2}) insuficiente para o débito.");
    }
}
