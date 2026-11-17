using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;

namespace Lancamentos.Domain.Politicas;

/// <summary>
/// Strategy: regras de um produto (tipo de conta). O núcleo do razão
/// (Conta e Lancamento) não conhece produtos; ele pergunta à política.
/// Um produto novo é uma política nova, sem alterar o núcleo.
/// </summary>
public interface IPoliticaDeConta
{
    TipoConta Tipo { get; }

    bool Aceita(TipoOperacao operacao);

    /// <summary>Retorna null se o débito pode ser autorizado, ou o erro que o impede.</summary>
    Erro? AutorizarDebito(Conta conta, ValorMonetario valor);
}
