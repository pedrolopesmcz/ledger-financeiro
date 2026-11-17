using Lancamentos.Domain.Contas;
using static Lancamentos.UnitTests.Fixtures;

namespace Lancamentos.UnitTests;

public class EstornoTests
{
    [Fact]
    public void Estorno_de_compra_devolve_o_limite()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);
        var compra = Lancar(cartao, TipoOperacao.Compra, 300m);

        var estorno = cartao.Estornar(compra, Chave(), "cobrança indevida", Agora).Valor;

        Assert.Equal(TipoOperacao.Estorno, estorno.Operacao);
        Assert.Equal(Natureza.Credito, estorno.Natureza);
        Assert.Equal(300m, estorno.Valor);
        Assert.Equal(compra.Id, estorno.LancamentoOriginalId);
        Assert.Equal(0m, cartao.Saldo);
    }

    [Fact]
    public void Estorno_de_credito_e_debito_e_nao_passa_pela_autorizacao_de_limite()
    {
        // Correção contábil: um depósito feito por engano é revertido mesmo
        // que o cliente já tenha gasto o dinheiro.
        var conta = NovaConta(TipoConta.ContaDigital, limite: 0m);
        var deposito = Lancar(conta, TipoOperacao.Deposito, 100m);
        Lancar(conta, TipoOperacao.Saque, 80m);

        var estorno = conta.Estornar(deposito, Chave(), "depósito em duplicidade", Agora);

        Assert.True(estorno.Sucesso);
        Assert.Equal(Natureza.Debito, estorno.Valor.Natureza);
        Assert.Equal(-80m, conta.Saldo);
    }

    [Fact]
    public void Nao_e_possivel_estornar_lancamento_de_outra_conta()
    {
        var contaA = NovaConta(TipoConta.ContaDigital);
        var contaB = NovaConta(TipoConta.ContaDigital);
        var deposito = Lancar(contaA, TipoOperacao.Deposito, 100m);

        var resultado = contaB.Estornar(deposito, Chave(), null, Agora);

        Assert.Equal("estorno_invalido", resultado.Erro!.Codigo);
        Assert.Equal(0m, contaB.Saldo);
    }

    [Fact]
    public void Nao_e_possivel_estornar_um_estorno()
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        var deposito = Lancar(conta, TipoOperacao.Deposito, 100m);
        var estorno = conta.Estornar(deposito, Chave(), null, Agora).Valor;

        var resultado = conta.Estornar(estorno, Chave(), null, Agora);

        Assert.Equal("estorno_invalido", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Natureza_padrao_do_estorno_nao_existe()
    {
        Assert.Throws<InvalidOperationException>(() => TipoOperacao.Estorno.NaturezaPadrao());
    }
}
