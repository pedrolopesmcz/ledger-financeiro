using Lancamentos.Domain.Contas;
using Ledger.Contracts;

namespace Lancamentos.Application;

/// <summary>Acesso a contas e lançamentos. Implementado na infraestrutura.</summary>
public interface IRepositorioDeContas
{
    Task<Conta?> ObterContaAsync(Guid contaId, CancellationToken ct);

    Task<Lancamento?> ObterLancamentoAsync(Guid contaId, Guid lancamentoId, CancellationToken ct);

    Task<Lancamento?> ObterPorChaveAsync(Guid contaId, string idempotencyKey, CancellationToken ct);

    Task<bool> JaFoiEstornadoAsync(Guid lancamentoId, CancellationToken ct);

    void Adicionar(Conta conta);

    void Adicionar(Lancamento lancamento);
}

/// <summary>Grava eventos na outbox, dentro da mesma transação da unidade de trabalho.</summary>
public interface IOutbox
{
    void Enfileirar(LancamentoRegistradoV1 evento);
}

/// <summary>
/// Unit of Work: tudo o que foi adicionado ou alterado é gravado numa única
/// transação, ou nada é gravado.
/// </summary>
public interface IUnidadeDeTrabalho
{
    /// <exception cref="ConflitoDeConcorrenciaException">A conta foi alterada por outra operação.</exception>
    /// <exception cref="ViolacaoDeUnicidadeException">Uma restrição única do banco foi violada.</exception>
    Task SalvarAsync(CancellationToken ct);

    /// <summary>Descarta alterações pendentes para que a operação possa ser refeita do zero.</summary>
    void Descartar();
}

public sealed class ConflitoDeConcorrenciaException : Exception
{
    public ConflitoDeConcorrenciaException()
    {
    }

    public ConflitoDeConcorrenciaException(string message)
        : base(message)
    {
    }

    public ConflitoDeConcorrenciaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class ViolacaoDeUnicidadeException : Exception
{
    public ViolacaoDeUnicidadeException()
    {
    }

    public ViolacaoDeUnicidadeException(string message)
        : base(message)
    {
    }

    public ViolacaoDeUnicidadeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public string? Restricao { get; init; }
}
