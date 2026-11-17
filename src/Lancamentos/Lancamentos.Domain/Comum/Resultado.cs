namespace Lancamentos.Domain.Comum;

/// <summary>
/// Erro de negócio com código estável (usado pela API para montar a resposta)
/// e mensagem legível.
/// </summary>
public sealed record Erro(string Codigo, string Mensagem);

/// <summary>
/// Resultado de uma operação de domínio. Regras de negócio violadas não são
/// exceções: são resultados esperados e fazem parte do fluxo normal.
/// Exceções ficam reservadas para falhas técnicas.
/// </summary>
public sealed class Resultado<T>
{
    private readonly T? _valor;

    private Resultado(T valor)
    {
        _valor = valor;
        Sucesso = true;
    }

    private Resultado(Erro erro)
    {
        Erro = erro;
        Sucesso = false;
    }

    public bool Sucesso { get; }

    public Erro? Erro { get; }

    public T Valor => Sucesso
        ? _valor!
        : throw new InvalidOperationException($"Resultado com erro não tem valor: {Erro!.Codigo}");

    public static Resultado<T> Ok(T valor) => new(valor);

    public static Resultado<T> Falha(Erro erro) => new(erro);

    public static Resultado<T> Falha(string codigo, string mensagem) => new(new Erro(codigo, mensagem));
}
