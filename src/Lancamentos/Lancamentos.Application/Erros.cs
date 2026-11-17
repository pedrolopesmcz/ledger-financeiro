using Lancamentos.Domain.Comum;

namespace Lancamentos.Application;

public static class Erros
{
    public static readonly Erro ContaNaoEncontrada = new("conta_nao_encontrada", "Conta não encontrada.");

    public static readonly Erro ContaJaExiste = new("conta_ja_existe", "Já existe uma conta com este identificador.");

    public static readonly Erro LancamentoNaoEncontrado = new("lancamento_nao_encontrado", "Lançamento não encontrado.");

    public static readonly Erro JaEstornado = new("ja_estornado", "Este lançamento já foi estornado.");

    public static readonly Erro ChaveReutilizada = new(
        "idempotency_key_reutilizada",
        "Esta chave de idempotência já foi usada para uma operação diferente.");

    public static readonly Erro ConcorrenciaEsgotada = new(
        "conflito_concorrencia",
        "A conta está sendo alterada por outras operações. Tente novamente.");
}
