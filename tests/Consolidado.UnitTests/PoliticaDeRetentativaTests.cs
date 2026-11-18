using Consolidado.Core;

namespace Consolidado.UnitTests;

public class PoliticaDeRetentativaTests
{
    private readonly PoliticaDeRetentativa _politica = new() { MaximoDeTentativas = 3 };

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Falha_transitoria_dentro_do_limite_volta_para_retry(int tentativas)
    {
        Assert.Equal(DestinoDaFalha.Retentar, _politica.Decidir(tentativas, mensagemInvalida: false));
    }

    [Fact]
    public void Tentativas_esgotadas_vao_para_a_dlq()
    {
        Assert.Equal(DestinoDaFalha.FilaDeMensagensMortas, _politica.Decidir(3, mensagemInvalida: false));
    }

    [Fact]
    public void Mensagem_invalida_vai_direto_para_a_dlq()
    {
        Assert.Equal(DestinoDaFalha.FilaDeMensagensMortas, _politica.Decidir(0, mensagemInvalida: true));
    }
}
