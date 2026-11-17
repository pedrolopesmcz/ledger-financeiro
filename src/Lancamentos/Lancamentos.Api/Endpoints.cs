using Lancamentos.Application;
using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;
using Microsoft.AspNetCore.Mvc;

namespace Lancamentos.Api;

public sealed record AbrirContaRequest(Guid? Id, TipoConta Tipo, decimal Limite);

public sealed record AlterarLimiteRequest(decimal Limite);

public sealed record RegistrarLancamentoRequest(TipoOperacao Operacao, decimal Valor, string? Descricao);

public sealed record EstornarRequest(string? Motivo);

public static class Endpoints
{
    public const string CabecalhoIdempotencia = "Idempotency-Key";

    public static IEndpointRouteBuilder MapLancamentosEndpoints(this IEndpointRouteBuilder app)
    {
        var contas = app.MapGroup("/contas").WithTags("Contas");

        contas.MapPost("/", async (AbrirContaRequest req, ServicoDeLancamentos servico, CancellationToken ct) =>
        {
            var resultado = await servico.AbrirContaAsync(new AbrirConta(req.Id ?? Guid.CreateVersion7(), req.Tipo, req.Limite), ct);
            return resultado.Sucesso
                ? Results.Created($"/contas/{resultado.Valor.Id}", resultado.Valor)
                : Problema(resultado.Erro!);
        });

        contas.MapGet("/{contaId:guid}", async (Guid contaId, ServicoDeLancamentos servico, CancellationToken ct) =>
            await servico.ConsultarContaAsync(contaId, ct) is { } conta
                ? Results.Ok(conta)
                : Problema(Erros.ContaNaoEncontrada));

        contas.MapPut("/{contaId:guid}/limite", async (Guid contaId, AlterarLimiteRequest req, ServicoDeLancamentos servico, CancellationToken ct) =>
        {
            var resultado = await servico.AlterarLimiteAsync(contaId, req.Limite, ct);
            return resultado.Sucesso ? Results.Ok(resultado.Valor) : Problema(resultado.Erro!);
        });

        contas.MapPost("/{contaId:guid}/lancamentos", async (
            Guid contaId,
            RegistrarLancamentoRequest req,
            [FromHeader(Name = CabecalhoIdempotencia)] string? chave,
            ServicoDeLancamentos servico,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(chave))
            {
                return ChaveAusente();
            }

            var resultado = await servico.RegistrarAsync(new RegistrarLancamento(contaId, req.Operacao, req.Valor, req.Descricao, chave), ct);
            return Resposta(resultado);
        });

        contas.MapGet("/{contaId:guid}/lancamentos/{lancamentoId:guid}", async (Guid contaId, Guid lancamentoId, ServicoDeLancamentos servico, CancellationToken ct) =>
            await servico.ConsultarLancamentoAsync(contaId, lancamentoId, ct) is { } lancamento
                ? Results.Ok(lancamento)
                : Problema(Erros.LancamentoNaoEncontrado));

        contas.MapPost("/{contaId:guid}/lancamentos/{lancamentoId:guid}/estorno", async (
            Guid contaId,
            Guid lancamentoId,
            EstornarRequest? req,
            [FromHeader(Name = CabecalhoIdempotencia)] string? chave,
            ServicoDeLancamentos servico,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(chave))
            {
                return ChaveAusente();
            }

            var resultado = await servico.EstornarAsync(new EstornarLancamento(contaId, lancamentoId, req?.Motivo, chave), ct);
            return Resposta(resultado);
        });

        return app;
    }

    private static IResult Resposta(Resultado<RespostaLancamento> resultado)
    {
        if (!resultado.Sucesso)
        {
            return Problema(resultado.Erro!);
        }

        var lancamento = resultado.Valor.Lancamento;
        var local = $"/contas/{lancamento.ContaId}/lancamentos/{lancamento.Id}";

        // Repetição de uma chave já processada devolve o resultado original com 200,
        // não 201: o cliente sabe que nada novo foi criado.
        return resultado.Valor.Repetido ? Results.Ok(lancamento) : Results.Created(local, lancamento);
    }

    private static IResult ChaveAusente() =>
        Problema(new Erro("idempotency_key_obrigatoria", $"O cabeçalho {CabecalhoIdempotencia} é obrigatório."));

    /// <summary>Converte erros de negócio em respostas RFC 7807 (Problem Details) com código estável.</summary>
    internal static IResult Problema(Erro erro)
    {
        var status = erro.Codigo switch
        {
            "conta_nao_encontrada" or "lancamento_nao_encontrado" => StatusCodes.Status404NotFound,
            "conta_ja_existe" or "ja_estornado" or "idempotency_key_reutilizada" or "conflito_concorrencia" => StatusCodes.Status409Conflict,
            "saldo_insuficiente" or "limite_insuficiente" or "operacao_nao_permitida" or "estorno_invalido" => StatusCodes.Status422UnprocessableEntity,
            _ => StatusCodes.Status400BadRequest,
        };

        return Results.Problem(
            statusCode: status,
            title: erro.Mensagem,
            type: $"https://ledger-financeiro/erros/{erro.Codigo}",
            extensions: new Dictionary<string, object?> { ["codigo"] = erro.Codigo });
    }
}
