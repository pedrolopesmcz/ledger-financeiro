using Consolidado.Core;

namespace Consolidado.UnitTests;

public class ServicoDeConsultaTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Conta = Guid.NewGuid();

    private readonly CacheFalso _cache = new();
    private readonly ConsultasFalsas _banco = new();
    private readonly OpcoesDeCache _opcoes = new();
    private readonly ServicoDeConsulta _servico;

    public ServicoDeConsultaTests() =>
        _servico = new ServicoDeConsulta(_banco, _cache, new DiaContabil(), new RelogioFalso(Agora), _opcoes);

    [Fact]
    public async Task Cache_miss_consulta_o_banco_e_grava_no_cache_com_validade_curta()
    {
        _banco.Posicao = new PosicaoAtual(Conta, 100m, Agora, Agora);

        var posicao = await _servico.ObterPosicaoAsync(Conta, CancellationToken.None);

        Assert.Equal(100m, posicao!.Saldo);
        Assert.Equal(1, _banco.Chamadas);
        Assert.Equal(_opcoes.ValidadeCurta, _cache.Itens[ChavesDeCache.Posicao(Conta)].Validade);
    }

    [Fact]
    public async Task Cache_hit_nao_consulta_o_banco()
    {
        _banco.Posicao = new PosicaoAtual(Conta, 100m, Agora, Agora);
        await _servico.ObterPosicaoAsync(Conta, CancellationToken.None);

        var posicao = await _servico.ObterPosicaoAsync(Conta, CancellationToken.None);

        Assert.Equal(100m, posicao!.Saldo);
        Assert.Equal(1, _banco.Chamadas);
    }

    [Fact]
    public async Task Conta_sem_movimentos_nao_e_cacheada()
    {
        Assert.Null(await _servico.ObterPosicaoAsync(Conta, CancellationToken.None));
        Assert.Empty(_cache.Itens);
    }

    [Fact]
    public async Task Dia_encerrado_usa_validade_longa_e_dia_corrente_usa_curta()
    {
        var ontem = new DateOnly(2026, 11, 17);
        var hoje = new DateOnly(2026, 11, 18);
        _banco.Consolidado = new ConsolidadoDiario(Conta, ontem, 0m, 10m, 0m, 10m, 1, Agora);

        await _servico.ObterConsolidadoAsync(Conta, ontem, CancellationToken.None);
        await _servico.ObterConsolidadoAsync(Conta, hoje, CancellationToken.None);

        Assert.Equal(_opcoes.ValidadeLonga, _cache.Itens[ChavesDeCache.Dia(Conta, ontem)].Validade);
        Assert.Equal(_opcoes.ValidadeCurta, _cache.Itens[ChavesDeCache.Dia(Conta, hoje)].Validade);
    }

    [Fact]
    public async Task Saldo_em_instante_arbitrario_nao_passa_pelo_cache()
    {
        await _servico.ObterSaldoNoInstanteAsync(Conta, Agora, CancellationToken.None);

        Assert.Equal(0, _cache.Chamadas);
        Assert.Equal(1, _banco.Chamadas);
    }
}
