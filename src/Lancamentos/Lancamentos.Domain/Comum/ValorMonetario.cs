namespace Lancamentos.Domain.Comum;

/// <summary>
/// Valor monetário positivo com no máximo duas casas decimais.
/// Dinheiro é sempre decimal: ponto flutuante gera erros de arredondamento.
/// </summary>
public readonly record struct ValorMonetario
{
    public const decimal Maximo = 1_000_000_000m;

    private ValorMonetario(decimal quantia) => Quantia = quantia;

    public decimal Quantia { get; }

    public static Resultado<ValorMonetario> Criar(decimal quantia)
    {
        if (quantia <= 0)
        {
            return Resultado<ValorMonetario>.Falha("valor_invalido", "O valor deve ser maior que zero.");
        }

        if (quantia > Maximo)
        {
            return Resultado<ValorMonetario>.Falha("valor_invalido", $"O valor não pode ultrapassar {Maximo:N2}.");
        }

        if (decimal.Round(quantia, 2) != quantia)
        {
            return Resultado<ValorMonetario>.Falha("valor_invalido", "O valor deve ter no máximo duas casas decimais.");
        }

        return Resultado<ValorMonetario>.Ok(new ValorMonetario(quantia));
    }

    public override string ToString() => Quantia.ToString("N2", System.Globalization.CultureInfo.InvariantCulture);
}
