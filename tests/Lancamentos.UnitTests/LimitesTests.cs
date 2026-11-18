using Lancamentos.Domain.Contas;
using Lancamentos.Domain.Politicas;
using static Lancamentos.UnitTests.Fixtures;

namespace Lancamentos.UnitTests;

/// <summary>
/// Testes de fronteira, escritos a partir dos mutantes que sobreviveram ao
/// Stryker: cada um prova um "&lt;" versus "&lt;=" ou uma validação de entrada.
/// </summary>
public class LimitesTests
{
    [Fact]
    public void Chave_de_idempotencia_com_exatamente_o_tamanho_maximo_e_aceita()
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        var chave = new string('k', Lancamento.TamanhoMaximoChave);

        Assert.True(conta.Registrar(ContaDigital, TipoOperacao.Deposito, Reais(1), chave, null, Agora).Sucesso);
    }

    [Fact]
    public void Descricao_com_exatamente_o_tamanho_maximo_e_aceita()
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        var descricao = new string('d', Lancamento.TamanhoMaximoDescricao);

        Assert.True(conta.Registrar(ContaDigital, TipoOperacao.Deposito, Reais(1), Chave(), descricao, Agora).Sucesso);
    }

    [Fact]
    public void Limite_zero_e_valido()
    {
        var conta = NovaConta(TipoConta.CartaoCredito, 500m);

        Assert.True(conta.AlterarLimite(0m).Sucesso);
        Assert.Equal(0m, conta.Limite);
    }

    [Fact]
    public void Credito_tem_valor_com_sinal_positivo()
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        var deposito = Lancar(conta, TipoOperacao.Deposito, 25m);

        Assert.Equal(25m, deposito.ValorComSinal);
    }

    [Fact]
    public void Entradas_nulas_sao_erro_de_programacao()
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        Assert.Throws<ArgumentNullException>(() => conta.Registrar(null!, TipoOperacao.Deposito, Reais(1), Chave(), null, Agora));
        Assert.Throws<ArgumentNullException>(() => conta.Estornar(null!, Chave(), null, Agora));
        Assert.Throws<ArgumentNullException>(() => new PoliticaContaDigital().AutorizarDebito(null!, Reais(1)));
        Assert.Throws<ArgumentNullException>(() => new PoliticaCartaoCredito().AutorizarDebito(null!, Reais(1)));
    }
}
