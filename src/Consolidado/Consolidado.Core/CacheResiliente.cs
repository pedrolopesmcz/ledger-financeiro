namespace Consolidado.Core;

/// <summary>
/// Decorator com circuit breaker em volta do cache real. Se o Redis falhar,
/// o circuito abre e, durante o intervalo de pausa, o cache nem é consultado:
/// as leituras vão direto ao banco sem pagar o timeout do Redis a cada
/// requisição. Passada a pausa, uma chamada testa se ele voltou.
/// </summary>
public sealed class CacheResiliente : ICacheDeConsultas
{
    private readonly ICacheDeConsultas _interno;
    private readonly TimeProvider _relogio;
    private readonly TimeSpan _pausa;
    private long _abertoAteTicks;

    public CacheResiliente(ICacheDeConsultas interno, TimeProvider relogio, TimeSpan pausa)
    {
        _interno = interno;
        _relogio = relogio;
        _pausa = pausa;
    }

    public bool CircuitoAberto => _relogio.GetUtcNow().UtcTicks < Interlocked.Read(ref _abertoAteTicks);

    public async Task<string?> ObterAsync(string chave, CancellationToken ct)
    {
        if (CircuitoAberto)
        {
            return null;
        }

        try
        {
            return await _interno.ObterAsync(chave, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Abrir();
            return null;
        }
    }

    public Task GravarAsync(string chave, string valor, TimeSpan validade, CancellationToken ct) =>
        Executar(token => _interno.GravarAsync(chave, valor, validade, token), ct);

    public Task RemoverAsync(string chave, CancellationToken ct) =>
        Executar(token => _interno.RemoverAsync(chave, token), ct);

    private async Task Executar(Func<CancellationToken, Task> operacao, CancellationToken ct)
    {
        if (CircuitoAberto)
        {
            return;
        }

        try
        {
            await operacao(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Abrir();
        }
    }

    private void Abrir() =>
        Interlocked.Exchange(ref _abertoAteTicks, _relogio.GetUtcNow().Add(_pausa).UtcTicks);
}
