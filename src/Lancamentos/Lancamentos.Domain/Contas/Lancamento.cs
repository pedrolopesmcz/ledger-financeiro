using Lancamentos.Domain.Comum;

namespace Lancamentos.Domain.Contas;

/// <summary>
/// Registro imutável de uma movimentação. Não existe alteração nem exclusão:
/// correções são feitas por um novo lançamento de estorno (ADR 0010).
/// </summary>
public sealed class Lancamento
{
    public const int TamanhoMaximoChave = 100;
    public const int TamanhoMaximoDescricao = 140;

    // Construtor para o ORM.
    private Lancamento()
    {
        IdempotencyKey = string.Empty;
    }

    private Lancamento(
        Guid contaId,
        TipoOperacao operacao,
        Natureza natureza,
        decimal valor,
        decimal saldoApos,
        string idempotencyKey,
        string? descricao,
        Guid? lancamentoOriginalId,
        DateTimeOffset ocorridoEm)
    {
        Id = Guid.CreateVersion7(ocorridoEm);
        ContaId = contaId;
        Operacao = operacao;
        Natureza = natureza;
        Valor = valor;
        SaldoApos = saldoApos;
        IdempotencyKey = idempotencyKey;
        Descricao = descricao;
        LancamentoOriginalId = lancamentoOriginalId;
        OcorridoEm = ocorridoEm;
    }

    public Guid Id { get; private set; }

    public Guid ContaId { get; private set; }

    public TipoOperacao Operacao { get; private set; }

    public Natureza Natureza { get; private set; }

    /// <summary>Sempre positivo; o sinal é dado pela natureza.</summary>
    public decimal Valor { get; private set; }

    /// <summary>Saldo da conta logo após este lançamento. Base para extrato e conciliação.</summary>
    public decimal SaldoApos { get; private set; }

    public string IdempotencyKey { get; private set; }

    public string? Descricao { get; private set; }

    /// <summary>Preenchido apenas em estornos.</summary>
    public Guid? LancamentoOriginalId { get; private set; }

    public DateTimeOffset OcorridoEm { get; private set; }

    /// <summary>Efeito no saldo: positivo para crédito, negativo para débito.</summary>
    public decimal ValorComSinal => Natureza == Natureza.Credito ? Valor : -Valor;

    internal static Resultado<Lancamento> Criar(
        Guid contaId,
        TipoOperacao operacao,
        Natureza natureza,
        ValorMonetario valor,
        decimal saldoApos,
        string idempotencyKey,
        string? descricao,
        Guid? lancamentoOriginalId,
        DateTimeOffset ocorridoEm)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > TamanhoMaximoChave)
        {
            return Resultado<Lancamento>.Falha(
                "idempotency_key_invalida",
                $"A chave de idempotência é obrigatória e deve ter até {TamanhoMaximoChave} caracteres.");
        }

        if (descricao is { Length: > TamanhoMaximoDescricao })
        {
            return Resultado<Lancamento>.Falha(
                "descricao_invalida",
                $"A descrição deve ter até {TamanhoMaximoDescricao} caracteres.");
        }

        return Resultado<Lancamento>.Ok(new Lancamento(
            contaId,
            operacao,
            natureza,
            valor.Quantia,
            saldoApos,
            idempotencyKey,
            descricao?.Trim(),
            lancamentoOriginalId,
            ocorridoEm));
    }
}
