using Lancamentos.Domain.Contas;
using static Lancamentos.UnitTests.Fixtures;

namespace Lancamentos.UnitTests;

public class PoliticaContaDigitalTests
{
    [Theory]
    [InlineData(TipoOperacao.Deposito, 100)]
    [InlineData(TipoOperacao.PixRecebido, 100)]
    public void Creditos_aumentam_o_saldo(TipoOperacao operacao, decimal esperado)
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        Lancar(conta, operacao, 100m);

        Assert.Equal(esperado, conta.Saldo);
    }

    [Theory]
    [InlineData(TipoOperacao.Saque)]
    [InlineData(TipoOperacao.PixEnviado)]
    public void Debito_dentro_do_saldo_e_autorizado(TipoOperacao operacao)
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        Lancar(conta, TipoOperacao.Deposito, 100m);

        var resultado = conta.Registrar(ContaDigital, operacao, Reais(100m), Chave(), null, Agora);

        Assert.True(resultado.Sucesso);
        Assert.Equal(0m, conta.Saldo);
    }

    [Fact]
    public void Sem_cheque_especial_a_conta_nao_fica_negativa()
    {
        var conta = NovaConta(TipoConta.ContaDigital, limite: 0m);
        Lancar(conta, TipoOperacao.Deposito, 50m);

        var resultado = conta.Registrar(ContaDigital, TipoOperacao.PixEnviado, Reais(50.01m), Chave(), null, Agora);

        Assert.False(resultado.Sucesso);
        Assert.Equal("saldo_insuficiente", resultado.Erro!.Codigo);
        Assert.Equal(50m, conta.Saldo);
    }

    [Fact]
    public void Cheque_especial_permite_saldo_negativo_ate_o_limite()
    {
        var conta = NovaConta(TipoConta.ContaDigital, limite: 200m);
        Lancar(conta, TipoOperacao.Deposito, 50m);

        Assert.True(conta.Registrar(ContaDigital, TipoOperacao.Saque, Reais(250m), Chave(), null, Agora).Sucesso);
        Assert.Equal(-200m, conta.Saldo);

        var acimaDoLimite = conta.Registrar(ContaDigital, TipoOperacao.Saque, Reais(0.01m), Chave(), null, Agora);
        Assert.Equal("saldo_insuficiente", acimaDoLimite.Erro!.Codigo);
    }

    [Theory]
    [InlineData(TipoOperacao.Compra)]
    [InlineData(TipoOperacao.PagamentoFatura)]
    public void Operacoes_de_cartao_nao_sao_aceitas(TipoOperacao operacao)
    {
        var conta = NovaConta(TipoConta.ContaDigital, 1000m);

        var resultado = conta.Registrar(ContaDigital, operacao, Reais(10m), Chave(), null, Agora);

        Assert.Equal("operacao_nao_permitida", resultado.Erro!.Codigo);
        Assert.Equal(0m, conta.Saldo);
    }
}
