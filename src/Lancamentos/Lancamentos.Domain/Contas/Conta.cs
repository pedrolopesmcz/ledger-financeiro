using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Politicas;

namespace Lancamentos.Domain.Contas;

/// <summary>
/// Agregado raiz. Mantém o saldo oficial de autorização (ADR 0004).
/// Toda mudança de saldo passa por aqui e gera exatamente um lançamento.
/// A proteção contra concorrência (versão da linha) é feita na persistência.
/// </summary>
public sealed class Conta
{
    // Construtor para o ORM.
    private Conta()
    {
    }

    private Conta(Guid id, Guid titularId, TipoConta tipo, decimal limite, DateTimeOffset abertaEm)
    {
        Id = id;
        TitularId = titularId;
        Tipo = tipo;
        Limite = limite;
        AbertaEm = abertaEm;
    }

    public Guid Id { get; private set; }

    /// <summary>
    /// Cliente dono da conta (o "sub" do token). O provedor de identidade diz
    /// quem é o usuário; quem diz de quem é a conta é o banco.
    /// </summary>
    public Guid TitularId { get; private set; }

    public TipoConta Tipo { get; private set; }

    /// <summary>
    /// Conta digital: dinheiro do cliente (negativo = usando cheque especial).
    /// Cartão: negativo = valor utilizado do limite; positivo = saldo credor.
    /// </summary>
    public decimal Saldo { get; private set; }

    /// <summary>Cheque especial (conta digital) ou limite de crédito (cartão). Nunca negativo.</summary>
    public decimal Limite { get; private set; }

    public DateTimeOffset AbertaEm { get; private set; }

    public static Resultado<Conta> Abrir(Guid id, Guid titularId, TipoConta tipo, decimal limite, DateTimeOffset agora)
    {
        if (id == Guid.Empty)
        {
            return Resultado<Conta>.Falha("conta_invalida", "O identificador da conta é obrigatório.");
        }

        if (titularId == Guid.Empty)
        {
            return Resultado<Conta>.Falha("titular_invalido", "O titular da conta é obrigatório.");
        }

        if (!Enum.IsDefined(tipo))
        {
            return Resultado<Conta>.Falha("tipo_conta_invalido", "Tipo de conta desconhecido.");
        }

        if (limite < 0 || decimal.Round(limite, 2) != limite)
        {
            return Resultado<Conta>.Falha("limite_invalido", "O limite deve ser zero ou positivo, com até duas casas decimais.");
        }

        return Resultado<Conta>.Ok(new Conta(id, titularId, tipo, limite, agora));
    }

    public Resultado<Lancamento> Registrar(
        IPoliticaDeConta politica,
        TipoOperacao operacao,
        ValorMonetario valor,
        string idempotencyKey,
        string? descricao,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(politica);
        GarantirPoliticaCompativel(politica);

        if (operacao == TipoOperacao.Estorno)
        {
            return Resultado<Lancamento>.Falha("operacao_invalida", "Estornos devem referenciar o lançamento original.");
        }

        if (!politica.Aceita(operacao))
        {
            return Resultado<Lancamento>.Falha(
                "operacao_nao_permitida",
                $"A operação {operacao} não é permitida para {Tipo}.");
        }

        var natureza = operacao.NaturezaPadrao();
        if (natureza == Natureza.Debito && politica.AutorizarDebito(this, valor) is { } erro)
        {
            return Resultado<Lancamento>.Falha(erro);
        }

        return Aplicar(operacao, natureza, valor, idempotencyKey, descricao, null, agora);
    }

    /// <summary>
    /// Estorno: lançamento de natureza inversa ao original. Não passa pela
    /// autorização de limite porque é uma correção, não uma nova operação
    /// do cliente. A garantia de que um lançamento é estornado só uma vez
    /// fica com a aplicação e com um índice único no banco.
    /// </summary>
    public Resultado<Lancamento> Estornar(
        Lancamento original,
        string idempotencyKey,
        string? motivo,
        DateTimeOffset agora)
    {
        ArgumentNullException.ThrowIfNull(original);

        if (original.ContaId != Id)
        {
            return Resultado<Lancamento>.Falha("estorno_invalido", "O lançamento não pertence a esta conta.");
        }

        if (original.Operacao == TipoOperacao.Estorno)
        {
            return Resultado<Lancamento>.Falha("estorno_invalido", "Não é possível estornar um estorno.");
        }

        var natureza = original.Natureza == Natureza.Credito ? Natureza.Debito : Natureza.Credito;
        var valor = ValorMonetario.Criar(original.Valor).Valor;

        return Aplicar(TipoOperacao.Estorno, natureza, valor, idempotencyKey, motivo, original.Id, agora);
    }

    public Resultado<Conta> AlterarLimite(decimal novoLimite)
    {
        if (novoLimite < 0 || decimal.Round(novoLimite, 2) != novoLimite)
        {
            return Resultado<Conta>.Falha("limite_invalido", "O limite deve ser zero ou positivo, com até duas casas decimais.");
        }

        // Reduzir o limite abaixo do já utilizado é permitido: apenas bloqueia novos débitos.
        Limite = novoLimite;
        return Resultado<Conta>.Ok(this);
    }

    private Resultado<Lancamento> Aplicar(
        TipoOperacao operacao,
        Natureza natureza,
        ValorMonetario valor,
        string idempotencyKey,
        string? descricao,
        Guid? originalId,
        DateTimeOffset agora)
    {
        var efeito = natureza == Natureza.Credito ? valor.Quantia : -valor.Quantia;
        var resultado = Lancamento.Criar(Id, operacao, natureza, valor, Saldo + efeito, idempotencyKey, descricao, originalId, agora);

        if (resultado.Sucesso)
        {
            Saldo += efeito;
        }

        return resultado;
    }

    private void GarantirPoliticaCompativel(IPoliticaDeConta politica)
    {
        if (politica.Tipo != Tipo)
        {
            throw new InvalidOperationException($"Política de {politica.Tipo} aplicada a uma conta {Tipo}.");
        }
    }
}
