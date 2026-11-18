using Lancamentos.Domain.Contas;
using static Lancamentos.UnitTests.Fixtures;

namespace Lancamentos.UnitTests;

public class ContaTests
{
    [Fact]
    public void Abrir_conta_inicia_com_saldo_zero()
    {
        var conta = NovaConta(TipoConta.ContaDigital, 100m);

        Assert.Equal(0m, conta.Saldo);
        Assert.Equal(100m, conta.Limite);
    }

    [Fact]
    public void Abrir_conta_rejeita_id_vazio()
    {
        var resultado = Conta.Abrir(Guid.Empty, Guid.NewGuid(), TipoConta.ContaDigital, 0, Agora);

        Assert.Equal("conta_invalida", resultado.Erro!.Codigo);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10.123)]
    public void Abrir_conta_rejeita_limite_invalido(decimal limite)
    {
        var resultado = Conta.Abrir(Guid.NewGuid(), Guid.NewGuid(), TipoConta.CartaoCredito, limite, Agora);

        Assert.Equal("limite_invalido", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Abrir_conta_exige_titular()
    {
        var resultado = Conta.Abrir(Guid.NewGuid(), Guid.Empty, TipoConta.ContaDigital, 0, Agora);

        Assert.Equal("titular_invalido", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Abrir_conta_rejeita_tipo_desconhecido()
    {
        var resultado = Conta.Abrir(Guid.NewGuid(), Guid.NewGuid(), (TipoConta)42, 0, Agora);

        Assert.Equal("tipo_conta_invalido", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Aplicar_politica_de_outro_tipo_e_erro_de_programacao()
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        Assert.Throws<InvalidOperationException>(() =>
            conta.Registrar(Cartao, TipoOperacao.Compra, Reais(10), Chave(), null, Agora));
    }

    [Fact]
    public void Lancamento_registra_saldo_apos_e_dados_da_operacao()
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        Lancar(conta, TipoOperacao.Deposito, 100m);

        var lancamento = conta.Registrar(ContaDigital, TipoOperacao.Saque, Reais(30m), "chave-1", "  caixa eletrônico ", Agora).Valor;

        Assert.Equal(conta.Id, lancamento.ContaId);
        Assert.Equal(Natureza.Debito, lancamento.Natureza);
        Assert.Equal(30m, lancamento.Valor);
        Assert.Equal(-30m, lancamento.ValorComSinal);
        Assert.Equal(70m, lancamento.SaldoApos);
        Assert.Equal("chave-1", lancamento.IdempotencyKey);
        Assert.Equal("caixa eletrônico", lancamento.Descricao);
        Assert.Equal(Agora, lancamento.OcorridoEm);
        Assert.Null(lancamento.LancamentoOriginalId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Rejeita_chave_de_idempotencia_vazia(string chave)
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        var resultado = conta.Registrar(ContaDigital, TipoOperacao.Deposito, Reais(10), chave, null, Agora);

        Assert.Equal("idempotency_key_invalida", resultado.Erro!.Codigo);
        Assert.Equal(0m, conta.Saldo);
    }

    [Fact]
    public void Rejeita_chave_de_idempotencia_longa_demais()
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        var chave = new string('x', Lancamento.TamanhoMaximoChave + 1);

        var resultado = conta.Registrar(ContaDigital, TipoOperacao.Deposito, Reais(10), chave, null, Agora);

        Assert.Equal("idempotency_key_invalida", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Rejeita_descricao_longa_demais_sem_alterar_saldo()
    {
        var conta = NovaConta(TipoConta.ContaDigital);
        var descricao = new string('x', Lancamento.TamanhoMaximoDescricao + 1);

        var resultado = conta.Registrar(ContaDigital, TipoOperacao.Deposito, Reais(10), Chave(), descricao, Agora);

        Assert.Equal("descricao_invalida", resultado.Erro!.Codigo);
        Assert.Equal(0m, conta.Saldo);
    }

    [Fact]
    public void Registrar_nao_aceita_estorno_sem_lancamento_original()
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        var resultado = conta.Registrar(ContaDigital, TipoOperacao.Estorno, Reais(10), Chave(), null, Agora);

        Assert.Equal("operacao_invalida", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Alterar_limite_para_baixo_do_utilizado_bloqueia_novos_debitos()
    {
        var cartao = NovaConta(TipoConta.CartaoCredito, 1000m);
        Lancar(cartao, TipoOperacao.Compra, 800m);

        Assert.True(cartao.AlterarLimite(500m).Sucesso);
        var resultado = cartao.Registrar(Cartao, TipoOperacao.Compra, Reais(0.01m), Chave(), null, Agora);

        Assert.Equal("limite_insuficiente", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Alterar_limite_rejeita_valor_negativo()
    {
        var conta = NovaConta(TipoConta.ContaDigital);

        Assert.Equal("limite_invalido", conta.AlterarLimite(-1).Erro!.Codigo);
    }
}
