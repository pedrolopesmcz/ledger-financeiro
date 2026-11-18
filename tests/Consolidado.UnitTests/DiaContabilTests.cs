using Consolidado.Core;

namespace Consolidado.UnitTests;

public class DiaContabilTests
{
    private readonly DiaContabil _dia = new();

    [Fact]
    public void Compra_as_22h_de_brasilia_pertence_ao_mesmo_dia_mesmo_ja_sendo_outro_dia_em_utc()
    {
        // 17/11 22:30 em Brasília = 18/11 01:30 UTC
        var instante = new DateTimeOffset(2026, 11, 18, 1, 30, 0, TimeSpan.Zero);

        Assert.Equal(new DateOnly(2026, 11, 17), _dia.De(instante));
    }

    [Fact]
    public void Meia_noite_de_brasilia_inicia_o_novo_dia()
    {
        var instante = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.FromHours(-3));

        Assert.Equal(new DateOnly(2026, 11, 18), _dia.De(instante));
    }
}
