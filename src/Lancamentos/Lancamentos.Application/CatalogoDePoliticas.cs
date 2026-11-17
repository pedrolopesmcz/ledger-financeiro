using Lancamentos.Domain.Contas;
using Lancamentos.Domain.Politicas;

namespace Lancamentos.Application;

/// <summary>Resolve a política (Strategy) de cada tipo de conta, registradas via injeção de dependência.</summary>
public sealed class CatalogoDePoliticas
{
    private readonly Dictionary<TipoConta, IPoliticaDeConta> _politicas;

    public CatalogoDePoliticas(IEnumerable<IPoliticaDeConta> politicas)
    {
        _politicas = politicas.ToDictionary(p => p.Tipo);
    }

    public IPoliticaDeConta Para(TipoConta tipo) =>
        _politicas.TryGetValue(tipo, out var politica)
            ? politica
            : throw new InvalidOperationException($"Nenhuma política registrada para {tipo}.");
}
