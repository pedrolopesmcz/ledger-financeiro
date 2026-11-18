using Consolidado.Core;
using Microsoft.Extensions.Caching.Distributed;

namespace Consolidado.Infrastructure;

public sealed class CacheRedis : ICacheDeConsultas
{
    private readonly IDistributedCache _cache;

    public CacheRedis(IDistributedCache cache) => _cache = cache;

    public Task<string?> ObterAsync(string chave, CancellationToken ct) => _cache.GetStringAsync(chave, ct);

    public Task GravarAsync(string chave, string valor, TimeSpan validade, CancellationToken ct) =>
        _cache.SetStringAsync(chave, valor, new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = validade }, ct);

    public Task RemoverAsync(string chave, CancellationToken ct) => _cache.RemoveAsync(chave, ct);
}
