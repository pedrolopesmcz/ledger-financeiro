using Consolidado.Core;
using Ledger.Contracts;

namespace Consolidado.UnitTests;

public class InvalidadorTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static LancamentoRegistradoV1 Evento(Guid conta, DateTimeOffset quando) =>
        new(Guid.NewGuid(), Guid.NewGuid(), conta, "ContaDigital", "Deposito", "Credito", 10m, 10m, 10m, quando, null);

    [Fact]
    public async Task Evento_de_hoje_invalida_a_posicao_e_o_dia()
    {
        var cache = new CacheFalso();
        var conta = Guid.NewGuid();
        var invalidador = new InvalidadorDeCache(cache, new DiaContabil(), new RelogioFalso(Agora));

        await invalidador.InvalidarAsync(Evento(conta, Agora), CancellationToken.None);

        Assert.Contains(ChavesDeCache.Posicao(conta), cache.Removidas);
        Assert.Contains(ChavesDeCache.Dia(conta, new DateOnly(2026, 11, 18)), cache.Removidas);
    }

    [Fact]
    public async Task Evento_atrasado_invalida_todos_os_dias_seguintes_ate_hoje()
    {
        var cache = new CacheFalso();
        var conta = Guid.NewGuid();
        var invalidador = new InvalidadorDeCache(cache, new DiaContabil(), new RelogioFalso(Agora));

        await invalidador.InvalidarAsync(Evento(conta, Agora.AddDays(-3)), CancellationToken.None);

        for (var dia = new DateOnly(2026, 11, 15); dia <= new DateOnly(2026, 11, 18); dia = dia.AddDays(1))
        {
            Assert.Contains(ChavesDeCache.Dia(conta, dia), cache.Removidas);
        }
    }
}
