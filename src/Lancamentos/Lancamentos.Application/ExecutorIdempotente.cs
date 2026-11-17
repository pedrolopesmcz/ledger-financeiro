using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;

namespace Lancamentos.Application;

/// <summary>
/// Esqueleto comum dos comandos que geram lançamento:
/// 1. se a chave de idempotência já existe, devolve o resultado original;
/// 2. senão executa a operação e grava tudo numa transação;
/// 3. se outra operação alterou a conta no meio (concorrência otimista) ou
///    gravou a mesma chave antes (corrida), descarta e refaz do zero.
/// </summary>
public sealed class ExecutorIdempotente
{
    public const int MaximoTentativas = 4;

    private readonly IRepositorioDeContas _repositorio;
    private readonly IUnidadeDeTrabalho _unidade;

    public ExecutorIdempotente(IRepositorioDeContas repositorio, IUnidadeDeTrabalho unidade)
    {
        _repositorio = repositorio;
        _unidade = unidade;
    }

    public async Task<Resultado<RespostaLancamento>> ExecutarAsync(
        Guid contaId,
        string idempotencyKey,
        Func<Lancamento, bool> mesmaOperacao,
        Func<CancellationToken, Task<Resultado<Lancamento>>> operacao,
        CancellationToken ct)
    {
        for (var tentativa = 1; tentativa <= MaximoTentativas; tentativa++)
        {
            if (!string.IsNullOrWhiteSpace(idempotencyKey)
                && await _repositorio.ObterPorChaveAsync(contaId, idempotencyKey, ct).ConfigureAwait(false) is { } existente)
            {
                return mesmaOperacao(existente)
                    ? Resultado<RespostaLancamento>.Ok(new RespostaLancamento(LancamentoDto.De(existente), Repetido: true))
                    : Resultado<RespostaLancamento>.Falha(Erros.ChaveReutilizada);
            }

            var resultado = await operacao(ct).ConfigureAwait(false);
            if (!resultado.Sucesso)
            {
                _unidade.Descartar();
                return Resultado<RespostaLancamento>.Falha(resultado.Erro!);
            }

            try
            {
                await _unidade.SalvarAsync(ct).ConfigureAwait(false);
                return Resultado<RespostaLancamento>.Ok(new RespostaLancamento(LancamentoDto.De(resultado.Valor), Repetido: false));
            }
            catch (ConflitoDeConcorrenciaException)
            {
                _unidade.Descartar();
            }
            catch (ViolacaoDeUnicidadeException)
            {
                // Outra requisição gravou a mesma chave (ou estornou o mesmo lançamento)
                // primeiro. Na próxima volta o laço encontra o resultado gravado.
                _unidade.Descartar();
            }

            // Espera curta e aleatória para que as operações concorrentes não colidam de novo.
            await Task.Delay(Random.Shared.Next(5, 20 * tentativa), ct).ConfigureAwait(false);
        }

        return Resultado<RespostaLancamento>.Falha(Erros.ConcorrenciaEsgotada);
    }
}
