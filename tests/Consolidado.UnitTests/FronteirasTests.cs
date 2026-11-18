using Consolidado.Core;
using Ledger.Contracts;

namespace Consolidado.UnitTests;

/// <summary>Testes escritos a partir dos mutantes sobreviventes do Stryker.</summary>
public class FronteirasTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static LancamentoRegistradoV1 Evento(Guid conta, DateTimeOffset quando) =>
        new(Guid.NewGuid(), Guid.NewGuid(), conta, "ContaDigital", "Deposito", "Credito", 10m, 10m, 10m, quando, null, Guid.NewGuid());

    [Fact]
    public void Chaves_de_cache_tem_formato_estavel()
    {
        // Contrato entre a API (que lê) e o Worker (que invalida): se divergirem, a invalidação para de funcionar.
        var conta = Guid.Parse("c0a00000-0000-4000-8000-000000000001");

        Assert.Equal("consolidado:v1:posicao:c0a00000-0000-4000-8000-000000000001", ChavesDeCache.Posicao(conta));
        Assert.Equal("consolidado:v1:dia:c0a00000-0000-4000-8000-000000000001:2026-11-05", ChavesDeCache.Dia(conta, new DateOnly(2026, 11, 5)));
    }

    [Fact]
    public async Task Circuito_fecha_exatamente_ao_fim_da_pausa()
    {
        var relogio = new RelogioFalso(Agora);
        var interno = new CacheFalso { Indisponivel = true };
        var cache = new CacheResiliente(interno, relogio, TimeSpan.FromSeconds(15));
        await cache.ObterAsync("k", CancellationToken.None);

        relogio.Avancar(TimeSpan.FromSeconds(15));

        Assert.False(cache.CircuitoAberto);
    }

    [Fact]
    public async Task Cancelamento_propaga_e_nao_abre_o_circuito()
    {
        var cache = new CacheResiliente(new CacheQueCancela(), new RelogioFalso(Agora), TimeSpan.FromSeconds(15));

        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.ObterAsync("k", CancellationToken.None));
        await Assert.ThrowsAsync<OperationCanceledException>(() => cache.GravarAsync("k", "v", TimeSpan.FromSeconds(1), CancellationToken.None));
        Assert.False(cache.CircuitoAberto);
    }

    [Fact]
    public async Task Evento_muito_atrasado_invalida_no_maximo_31_dias()
    {
        var cache = new CacheFalso();
        var conta = Guid.NewGuid();
        var invalidador = new InvalidadorDeCache(cache, new DiaContabil(), new RelogioFalso(Agora));

        await invalidador.InvalidarAsync(Evento(conta, Agora.AddDays(-40)), CancellationToken.None);

        var dias = cache.Removidas.Where(k => k.Contains(":dia:", StringComparison.Ordinal)).Distinct().ToList();
        Assert.Equal(InvalidadorDeCache.MaximoDeDias, dias.Count);
        Assert.DoesNotContain(ChavesDeCache.Dia(conta, new DateOnly(2026, 11, 18)), dias);
    }

    [Fact]
    public async Task Invalidacao_nao_avanca_alem_de_hoje()
    {
        var cache = new CacheFalso();
        var conta = Guid.NewGuid();
        var invalidador = new InvalidadorDeCache(cache, new DiaContabil(), new RelogioFalso(Agora));

        await invalidador.InvalidarAsync(Evento(conta, Agora.AddDays(-2)), CancellationToken.None);

        Assert.DoesNotContain(ChavesDeCache.Dia(conta, new DateOnly(2026, 11, 19)), cache.Removidas);
    }

    [Fact]
    public async Task Evento_com_data_a_frente_por_diferenca_de_relogio_tambem_invalida_o_seu_dia()
    {
        var cache = new CacheFalso();
        var conta = Guid.NewGuid();
        var invalidador = new InvalidadorDeCache(cache, new DiaContabil(), new RelogioFalso(Agora));

        await invalidador.InvalidarAsync(Evento(conta, Agora.AddDays(1)), CancellationToken.None);

        Assert.Contains(ChavesDeCache.Dia(conta, new DateOnly(2026, 11, 19)), cache.Removidas);
    }

    [Fact]
    public async Task Evento_nulo_e_erro_de_programacao()
    {
        var invalidador = new InvalidadorDeCache(new CacheFalso(), new DiaContabil(), new RelogioFalso(Agora));

        await Assert.ThrowsAsync<ArgumentNullException>(() => invalidador.InvalidarAsync(null!, CancellationToken.None));
    }

    private sealed class CacheQueCancela : ICacheDeConsultas
    {
        public Task<string?> ObterAsync(string chave, CancellationToken ct) => throw new OperationCanceledException();

        public Task GravarAsync(string chave, string valor, TimeSpan validade, CancellationToken ct) => throw new OperationCanceledException();

        public Task RemoverAsync(string chave, CancellationToken ct) => throw new OperationCanceledException();
    }
}
