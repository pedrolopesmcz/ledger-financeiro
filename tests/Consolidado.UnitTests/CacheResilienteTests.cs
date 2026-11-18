using Consolidado.Core;

namespace Consolidado.UnitTests;

public class CacheResilienteTests
{
    private readonly RelogioFalso _relogio = new(new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero));
    private readonly CacheFalso _interno = new();
    private readonly CacheResiliente _cache;

    public CacheResilienteTests() => _cache = new CacheResiliente(_interno, _relogio, TimeSpan.FromSeconds(15));

    [Fact]
    public async Task Falha_do_cache_vira_cache_miss_e_nao_excecao()
    {
        _interno.Indisponivel = true;

        Assert.Null(await _cache.ObterAsync("k", CancellationToken.None));
        Assert.True(_cache.CircuitoAberto);
    }

    [Fact]
    public async Task Com_o_circuito_aberto_o_cache_nem_e_chamado()
    {
        _interno.Indisponivel = true;
        await _cache.ObterAsync("k", CancellationToken.None);
        var chamadasAntes = _interno.Chamadas;

        await _cache.ObterAsync("k", CancellationToken.None);
        await _cache.GravarAsync("k", "v", TimeSpan.FromSeconds(1), CancellationToken.None);
        await _cache.RemoverAsync("k", CancellationToken.None);

        Assert.Equal(chamadasAntes, _interno.Chamadas);
    }

    [Fact]
    public async Task Depois_da_pausa_o_circuito_testa_de_novo_e_fecha_se_o_cache_voltou()
    {
        _interno.Indisponivel = true;
        await _cache.ObterAsync("k", CancellationToken.None);

        _interno.Indisponivel = false;
        _relogio.Avancar(TimeSpan.FromSeconds(16));
        await _cache.GravarAsync("k", "v", TimeSpan.FromSeconds(1), CancellationToken.None);

        Assert.False(_cache.CircuitoAberto);
        Assert.Equal("v", await _cache.ObterAsync("k", CancellationToken.None));
    }

    [Fact]
    public async Task Falha_ao_gravar_ou_remover_tambem_nao_propaga()
    {
        _interno.Indisponivel = true;

        await _cache.GravarAsync("k", "v", TimeSpan.FromSeconds(1), CancellationToken.None);
        _relogio.Avancar(TimeSpan.FromSeconds(16));
        await _cache.RemoverAsync("k", CancellationToken.None);

        Assert.True(_cache.CircuitoAberto);
    }
}
