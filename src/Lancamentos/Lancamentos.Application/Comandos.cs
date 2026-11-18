using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;

namespace Lancamentos.Application;

public sealed record RegistrarLancamento(Guid ContaId, TipoOperacao Operacao, decimal Valor, string? Descricao, string IdempotencyKey);

public sealed record EstornarLancamento(Guid ContaId, Guid LancamentoId, string? Motivo, string IdempotencyKey);

public sealed record AbrirConta(Guid ContaId, Guid TitularId, TipoConta Tipo, decimal Limite);

public sealed class ServicoDeLancamentos
{
    private readonly IRepositorioDeContas _repositorio;
    private readonly IUnidadeDeTrabalho _unidade;
    private readonly IOutbox _outbox;
    private readonly CatalogoDePoliticas _politicas;
    private readonly ExecutorIdempotente _executor;
    private readonly TimeProvider _relogio;

    public ServicoDeLancamentos(
        IRepositorioDeContas repositorio,
        IUnidadeDeTrabalho unidade,
        IOutbox outbox,
        CatalogoDePoliticas politicas,
        ExecutorIdempotente executor,
        TimeProvider relogio)
    {
        _repositorio = repositorio;
        _unidade = unidade;
        _outbox = outbox;
        _politicas = politicas;
        _executor = executor;
        _relogio = relogio;
    }

    public Task<Resultado<RespostaLancamento>> RegistrarAsync(RegistrarLancamento comando, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comando);

        return _executor.ExecutarAsync(
            comando.ContaId,
            comando.IdempotencyKey,
            existente => existente.Operacao == comando.Operacao && existente.Valor == comando.Valor,
            async token =>
            {
                var valor = ValorMonetario.Criar(comando.Valor);
                if (!valor.Sucesso)
                {
                    return Resultado<Lancamento>.Falha(valor.Erro!);
                }

                var conta = await _repositorio.ObterContaAsync(comando.ContaId, token).ConfigureAwait(false);
                if (conta is null)
                {
                    return Resultado<Lancamento>.Falha(Erros.ContaNaoEncontrada);
                }

                var resultado = conta.Registrar(
                    _politicas.Para(conta.Tipo),
                    comando.Operacao,
                    valor.Valor,
                    comando.IdempotencyKey,
                    comando.Descricao,
                    _relogio.GetUtcNow());

                Registrar(resultado, conta);
                return resultado;
            },
            ct);
    }

    public Task<Resultado<RespostaLancamento>> EstornarAsync(EstornarLancamento comando, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comando);

        return _executor.ExecutarAsync(
            comando.ContaId,
            comando.IdempotencyKey,
            existente => existente.Operacao == TipoOperacao.Estorno && existente.LancamentoOriginalId == comando.LancamentoId,
            async token =>
            {
                var conta = await _repositorio.ObterContaAsync(comando.ContaId, token).ConfigureAwait(false);
                if (conta is null)
                {
                    return Resultado<Lancamento>.Falha(Erros.ContaNaoEncontrada);
                }

                var original = await _repositorio.ObterLancamentoAsync(comando.ContaId, comando.LancamentoId, token).ConfigureAwait(false);
                if (original is null)
                {
                    return Resultado<Lancamento>.Falha(Erros.LancamentoNaoEncontrado);
                }

                if (await _repositorio.JaFoiEstornadoAsync(original.Id, token).ConfigureAwait(false))
                {
                    return Resultado<Lancamento>.Falha(Erros.JaEstornado);
                }

                var resultado = conta.Estornar(original, comando.IdempotencyKey, comando.Motivo, _relogio.GetUtcNow());
                Registrar(resultado, conta);
                return resultado;
            },
            ct);
    }

    public async Task<Resultado<ContaDto>> AbrirContaAsync(AbrirConta comando, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(comando);

        var conta = Conta.Abrir(comando.ContaId, comando.TitularId, comando.Tipo, comando.Limite, _relogio.GetUtcNow());
        if (!conta.Sucesso)
        {
            return Resultado<ContaDto>.Falha(conta.Erro!);
        }

        _repositorio.Adicionar(conta.Valor);
        try
        {
            await _unidade.SalvarAsync(ct).ConfigureAwait(false);
        }
        catch (ViolacaoDeUnicidadeException)
        {
            _unidade.Descartar();
            return Resultado<ContaDto>.Falha(Erros.ContaJaExiste);
        }

        return Resultado<ContaDto>.Ok(ContaDto.De(conta.Valor));
    }

    public async Task<Resultado<ContaDto>> AlterarLimiteAsync(Guid contaId, decimal novoLimite, CancellationToken ct)
    {
        for (var tentativa = 1; tentativa <= ExecutorIdempotente.MaximoTentativas; tentativa++)
        {
            var conta = await _repositorio.ObterContaAsync(contaId, ct).ConfigureAwait(false);
            if (conta is null)
            {
                return Resultado<ContaDto>.Falha(Erros.ContaNaoEncontrada);
            }

            var resultado = conta.AlterarLimite(novoLimite);
            if (!resultado.Sucesso)
            {
                _unidade.Descartar();
                return Resultado<ContaDto>.Falha(resultado.Erro!);
            }

            try
            {
                await _unidade.SalvarAsync(ct).ConfigureAwait(false);
                return Resultado<ContaDto>.Ok(ContaDto.De(conta));
            }
            catch (ConflitoDeConcorrenciaException)
            {
                _unidade.Descartar();
            }
        }

        return Resultado<ContaDto>.Falha(Erros.ConcorrenciaEsgotada);
    }

    public async Task<ContaDto?> ConsultarContaAsync(Guid contaId, CancellationToken ct)
    {
        var conta = await _repositorio.ObterContaAsync(contaId, ct).ConfigureAwait(false);
        return conta is null ? null : ContaDto.De(conta);
    }

    public async Task<LancamentoDto?> ConsultarLancamentoAsync(Guid contaId, Guid lancamentoId, CancellationToken ct)
    {
        var lancamento = await _repositorio.ObterLancamentoAsync(contaId, lancamentoId, ct).ConfigureAwait(false);
        return lancamento is null ? null : LancamentoDto.De(lancamento);
    }

    private void Registrar(Resultado<Lancamento> resultado, Conta conta)
    {
        if (resultado.Sucesso)
        {
            _repositorio.Adicionar(resultado.Valor);
            _outbox.Enfileirar(Eventos.De(resultado.Valor, conta));
        }
    }
}
