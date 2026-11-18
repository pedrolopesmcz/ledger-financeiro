using Ledger.Contracts;

namespace Consolidado.Core;

/// <summary>
/// Após projetar um evento, remove do cache o que ficou desatualizado: a
/// posição da conta e o dia do lançamento. Um evento atrasado altera também
/// os dias seguintes, que são invalidados até hoje (limitado a 31 dias; além
/// disso vale a validade do cache).
/// </summary>
public sealed class InvalidadorDeCache
{
    public const int MaximoDeDias = 31;

    private readonly ICacheDeConsultas _cache;
    private readonly DiaContabil _diaContabil;
    private readonly TimeProvider _relogio;

    public InvalidadorDeCache(ICacheDeConsultas cache, DiaContabil diaContabil, TimeProvider relogio)
    {
        _cache = cache;
        _diaContabil = diaContabil;
        _relogio = relogio;
    }

    public async Task InvalidarAsync(LancamentoRegistradoV1 evento, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(evento);

        await _cache.RemoverAsync(ChavesDeCache.Posicao(evento.ContaId), ct).ConfigureAwait(false);

        var dia = _diaContabil.De(evento.OcorridoEm);
        var hoje = _diaContabil.De(_relogio.GetUtcNow());
        for (var i = 0; i < MaximoDeDias && dia <= hoje; i++, dia = dia.AddDays(1))
        {
            await _cache.RemoverAsync(ChavesDeCache.Dia(evento.ContaId, dia), ct).ConfigureAwait(false);
        }

        // Garante a invalidação do dia do evento mesmo que ele esteja "no futuro" por diferença de relógio.
        await _cache.RemoverAsync(ChavesDeCache.Dia(evento.ContaId, _diaContabil.De(evento.OcorridoEm)), ct).ConfigureAwait(false);
    }
}
