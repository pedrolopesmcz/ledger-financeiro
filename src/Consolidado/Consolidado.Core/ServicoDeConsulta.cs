using System.Text.Json;

namespace Consolidado.Core;

public sealed class OpcoesDeCache
{
    public const string Secao = "Cache";

    /// <summary>Saldo atual e dia corrente mudam a todo momento: validade curta.</summary>
    public TimeSpan ValidadeCurta { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Dias encerrados quase nunca mudam (só com evento atrasado, que invalida a chave).</summary>
    public TimeSpan ValidadeLonga { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan PausaDoCircuito { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Cache-aside (ADR 0011): procura no cache; se não achar, consulta o banco
/// e guarda o resultado. O saldo em um instante arbitrário não é cacheado:
/// a combinação conta + instante quase nunca se repete, e a consulta já é
/// indexada.
/// </summary>
public sealed class ServicoDeConsulta
{
    private readonly IConsultasDeSaldo _consultas;
    private readonly ICacheDeConsultas _cache;
    private readonly DiaContabil _diaContabil;
    private readonly TimeProvider _relogio;
    private readonly OpcoesDeCache _opcoes;

    public ServicoDeConsulta(
        IConsultasDeSaldo consultas,
        ICacheDeConsultas cache,
        DiaContabil diaContabil,
        TimeProvider relogio,
        OpcoesDeCache opcoes)
    {
        _consultas = consultas;
        _cache = cache;
        _diaContabil = diaContabil;
        _relogio = relogio;
        _opcoes = opcoes;
    }

    public Task<PosicaoAtual?> ObterPosicaoAsync(Guid contaId, CancellationToken ct) =>
        ComCacheAsync(
            ChavesDeCache.Posicao(contaId),
            _opcoes.ValidadeCurta,
            token => _consultas.ObterPosicaoAsync(contaId, token),
            ct);

    public Task<ConsolidadoDiario?> ObterConsolidadoAsync(Guid contaId, DateOnly dia, CancellationToken ct)
    {
        var hoje = _diaContabil.De(_relogio.GetUtcNow());
        var validade = dia < hoje ? _opcoes.ValidadeLonga : _opcoes.ValidadeCurta;

        return ComCacheAsync(
            ChavesDeCache.Dia(contaId, dia),
            validade,
            token => _consultas.ObterConsolidadoAsync(contaId, dia, token),
            ct);
    }

    public Task<SaldoNoInstante?> ObterSaldoNoInstanteAsync(Guid contaId, DateTimeOffset instante, CancellationToken ct) =>
        _consultas.ObterSaldoNoInstanteAsync(contaId, instante, ct);

    private async Task<T?> ComCacheAsync<T>(string chave, TimeSpan validade, Func<CancellationToken, Task<T?>> origem, CancellationToken ct)
        where T : class
    {
        var emCache = await _cache.ObterAsync(chave, ct).ConfigureAwait(false);
        if (emCache is not null)
        {
            return JsonSerializer.Deserialize<T>(emCache, JsonSerializerOptions.Web);
        }

        var valor = await origem(ct).ConfigureAwait(false);

        // "Não encontrado" não vai para o cache: a conta pode receber o primeiro lançamento a qualquer momento.
        if (valor is not null)
        {
            await _cache.GravarAsync(chave, JsonSerializer.Serialize(valor, JsonSerializerOptions.Web), validade, ct).ConfigureAwait(false);
        }

        return valor;
    }
}
