using Consolidado.Core;

namespace Consolidado.UnitTests;

/// <summary>Relógio controlado pelo teste.</summary>
internal sealed class RelogioFalso : TimeProvider
{
    public RelogioFalso(DateTimeOffset agora) => Agora = agora;

    public DateTimeOffset Agora { get; set; }

    public override DateTimeOffset GetUtcNow() => Agora;

    public void Avancar(TimeSpan tempo) => Agora = Agora.Add(tempo);
}

/// <summary>Cache em memória que registra as chamadas e pode simular indisponibilidade.</summary>
internal sealed class CacheFalso : ICacheDeConsultas
{
    public Dictionary<string, (string Valor, TimeSpan Validade)> Itens { get; } = [];

    public List<string> Removidas { get; } = [];

    public bool Indisponivel { get; set; }

    public int Chamadas { get; private set; }

    public Task<string?> ObterAsync(string chave, CancellationToken ct)
    {
        Chamar();
        return Task.FromResult(Itens.TryGetValue(chave, out var item) ? item.Valor : null);
    }

    public Task GravarAsync(string chave, string valor, TimeSpan validade, CancellationToken ct)
    {
        Chamar();
        Itens[chave] = (valor, validade);
        return Task.CompletedTask;
    }

    public Task RemoverAsync(string chave, CancellationToken ct)
    {
        Chamar();
        Removidas.Add(chave);
        Itens.Remove(chave);
        return Task.CompletedTask;
    }

    private void Chamar()
    {
        Chamadas++;
        if (Indisponivel)
        {
            throw new TimeoutException("Redis fora do ar");
        }
    }
}

internal sealed class ConsultasFalsas : IConsultasDeSaldo
{
    public int Chamadas { get; private set; }

    public PosicaoAtual? Posicao { get; set; }

    public ConsolidadoDiario? Consolidado { get; set; }

    public Task<PosicaoAtual?> ObterPosicaoAsync(Guid contaId, CancellationToken ct)
    {
        Chamadas++;
        return Task.FromResult(Posicao);
    }

    public Task<ConsolidadoDiario?> ObterConsolidadoAsync(Guid contaId, DateOnly dia, CancellationToken ct)
    {
        Chamadas++;
        return Task.FromResult(Consolidado);
    }

    public Task<SaldoNoInstante?> ObterSaldoNoInstanteAsync(Guid contaId, DateTimeOffset instante, CancellationToken ct)
    {
        Chamadas++;
        return Task.FromResult<SaldoNoInstante?>(new SaldoNoInstante(contaId, instante, 1m, instante));
    }
}
