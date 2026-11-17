using Lancamentos.Domain.Comum;

namespace Lancamentos.UnitTests;

public class ValorMonetarioTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Rejeita_valor_zero_ou_negativo(decimal quantia)
    {
        var resultado = ValorMonetario.Criar(quantia);

        Assert.False(resultado.Sucesso);
        Assert.Equal("valor_invalido", resultado.Erro!.Codigo);
    }

    [Fact]
    public void Rejeita_mais_de_duas_casas_decimais()
    {
        Assert.False(ValorMonetario.Criar(10.001m).Sucesso);
    }

    [Fact]
    public void Rejeita_valor_acima_do_maximo()
    {
        Assert.False(ValorMonetario.Criar(ValorMonetario.Maximo + 0.01m).Sucesso);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(10.5)]
    [InlineData(1000000000)]
    public void Aceita_valores_validos(decimal quantia)
    {
        var resultado = ValorMonetario.Criar(quantia);

        Assert.True(resultado.Sucesso);
        Assert.Equal(quantia, resultado.Valor.Quantia);
    }

    [Fact]
    public void Acessar_valor_de_resultado_com_erro_lanca_excecao()
    {
        var resultado = ValorMonetario.Criar(0);

        Assert.Throws<InvalidOperationException>(() => resultado.Valor);
    }
}
