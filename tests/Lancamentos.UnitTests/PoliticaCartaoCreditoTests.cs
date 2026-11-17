using Lancamentos.Domain.Contas;
using static Lancamentos.UnitTests.Fixtures;

namespace Lancamentos.UnitTests;

public class PoliticaCartaoCreditoTests
{
    [Fact]
    public void Compra_dentro_do_limite_consome_limite()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);

        Lancar(cartao, TipoOperacao.Compra, 400m);

        Assert.Equal(-400m, cartao.Saldo);
    }

    [Fact]
    public void Compra_que_usa_exatamente_o_limite_e_autorizada()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);

        var resultado = cartao.Registrar(Cartao, TipoOperacao.Compra, Reais(1000m), Chave(), null, Agora);

        Assert.True(resultado.Sucesso);
        Assert.Equal(-1000m, cartao.Saldo);
    }

    [Fact]
    public void Compra_acima_do_limite_disponivel_e_recusada()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);
        Lancar(cartao, TipoOperacao.Compra, 900m);

        var resultado = cartao.Registrar(Cartao, TipoOperacao.Compra, Reais(100.01m), Chave(), null, Agora);

        Assert.Equal("limite_insuficiente", resultado.Erro!.Codigo);
        Assert.Equal(-900m, cartao.Saldo);
    }

    [Fact]
    public void Pagamento_de_fatura_devolve_limite()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);
        Lancar(cartao, TipoOperacao.Compra, 1000m);

        Lancar(cartao, TipoOperacao.PagamentoFatura, 600m);

        Assert.Equal(-400m, cartao.Saldo);
        Assert.True(cartao.Registrar(Cartao, TipoOperacao.Compra, Reais(600m), Chave(), null, Agora).Sucesso);
    }

    [Fact]
    public void Pagamento_a_maior_gera_saldo_credor_que_amplia_o_disponivel()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);

        Lancar(cartao, TipoOperacao.PagamentoFatura, 200m);

        Assert.Equal(200m, cartao.Saldo);
        Assert.True(cartao.Registrar(Cartao, TipoOperacao.Compra, Reais(1200m), Chave(), null, Agora).Sucesso);
    }

    [Theory]
    [InlineData(TipoOperacao.Deposito)]
    [InlineData(TipoOperacao.Saque)]
    [InlineData(TipoOperacao.PixRecebido)]
    [InlineData(TipoOperacao.PixEnviado)]
    public void Operacoes_de_conta_digital_nao_sao_aceitas(TipoOperacao operacao)
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);

        var resultado = cartao.Registrar(Cartao, operacao, Reais(10m), Chave(), null, Agora);

        Assert.Equal("operacao_nao_permitida", resultado.Erro!.Codigo);
    }
}
