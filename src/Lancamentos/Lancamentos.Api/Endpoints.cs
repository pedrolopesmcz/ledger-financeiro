using System.Diagnostics.Metrics;
using System.Security.Claims;
using Lancamentos.Application;
using Lancamentos.Domain.Comum;
using Lancamentos.Domain.Contas;
using Ledger.Observabilidade;
using Ledger.Seguranca;
using Microsoft.AspNetCore.Mvc;

namespace Lancamentos.Api;

public sealed record AbrirContaRequest(Guid? Id, Guid TitularId, TipoConta Tipo, decimal Limite);

public sealed record AlterarLimiteRequest(decimal Limite);

public sealed record RegistrarLancamentoRequest(TipoOperacao Operacao, decimal Valor, string? Descricao);

public sealed record EstornarRequest(string? Motivo);

public static class Endpoints
{
    public const string CabecalhoIdempotencia = "Idempotency-Key";

    /// <summary>
    /// Operações que o próprio cliente pode iniciar com o token dele. Créditos
    /// (depósito, Pix recebido) e compras chegam por sistemas do banco (rede de
    /// cartões, SPI), que usam token de serviço com acesso a todas as contas.
    /// </summary>
    public static readonly IReadOnlySet<TipoOperacao> IniciadasPeloCliente = new HashSet<TipoOperacao>
    {
        TipoOperacao.PixEnviado,
        TipoOperacao.Saque,
        TipoOperacao.PagamentoFatura,
    };

    private static readonly Counter<long> Registrados = Telemetria.Medidor.CreateCounter<long>(
        "ledger.lancamentos.registrados", description: "Lançamentos gravados");

    private static readonly Counter<long> Recusados = Telemetria.Medidor.CreateCounter<long>(
        "ledger.lancamentos.recusados", description: "Lançamentos recusados por regra de negócio");

    public static IEndpointRouteBuilder MapLancamentosEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/contas", async (AbrirContaRequest req, ServicoDeLancamentos servico, CancellationToken ct) =>
        {
            var resultado = await servico.AbrirContaAsync(new AbrirConta(req.Id ?? Guid.CreateVersion7(), req.TitularId, req.Tipo, req.Limite), ct);
            return resultado.Sucesso
                ? Results.Created($"/contas/{resultado.Valor.Id}", resultado.Valor)
                : Problema(resultado.Erro!);
        }).RequireAuthorization(Politicas.ContasAdmin).WithTags("Contas");

        // Todas as rotas abaixo tratam de uma conta específica: passam pelo filtro de titularidade.
        var conta = app.MapGroup("/contas/{contaId:guid}")
            .WithTags("Contas")
            .AddEndpointFilter<FiltroDeTitularidade>();

        conta.MapGet("/", async (Guid contaId, ServicoDeLancamentos servico, CancellationToken ct) =>
            await servico.ConsultarContaAsync(contaId, ct) is { } dto
                ? Results.Ok(dto)
                : Problema(Erros.ContaNaoEncontrada))
            .RequireAuthorization(Politicas.SaldoRead);

        conta.MapPut("/limite", async (Guid contaId, AlterarLimiteRequest req, ServicoDeLancamentos servico, CancellationToken ct) =>
        {
            var resultado = await servico.AlterarLimiteAsync(contaId, req.Limite, ct);
            return resultado.Sucesso ? Results.Ok(resultado.Valor) : Problema(resultado.Erro!);
        }).RequireAuthorization(Politicas.ContasAdmin);

        conta.MapPost("/lancamentos", async (
            Guid contaId,
            RegistrarLancamentoRequest req,
            [FromHeader(Name = CabecalhoIdempotencia)] string? chave,
            ClaimsPrincipal usuario,
            ServicoDeLancamentos servico,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(chave))
            {
                return ChaveAusente();
            }

            if (!usuario.AcessaTodasAsContas() && !IniciadasPeloCliente.Contains(req.Operacao))
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status403Forbidden,
                    title: $"A operação {req.Operacao} não pode ser iniciada pelo cliente.",
                    type: "https://ledger-financeiro/erros/operacao_nao_autorizada",
                    extensions: new Dictionary<string, object?> { ["codigo"] = "operacao_nao_autorizada" });
            }

            var resultado = await servico.RegistrarAsync(new RegistrarLancamento(contaId, req.Operacao, req.Valor, req.Descricao, chave), ct);
            Medir(resultado, req.Operacao.ToString());
            return Resposta(resultado);
        }).RequireAuthorization(Politicas.LancamentosWrite);

        conta.MapGet("/lancamentos/{lancamentoId:guid}", async (Guid contaId, Guid lancamentoId, ServicoDeLancamentos servico, CancellationToken ct) =>
            await servico.ConsultarLancamentoAsync(contaId, lancamentoId, ct) is { } lancamento
                ? Results.Ok(lancamento)
                : Problema(Erros.LancamentoNaoEncontrado))
            .RequireAuthorization(Politicas.SaldoRead);

        conta.MapPost("/lancamentos/{lancamentoId:guid}/estorno", async (
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
            Medir(resultado, nameof(TipoOperacao.Estorno));
            return Resposta(resultado);
        }).RequireAuthorization(Politicas.LancamentosEstorno);

        return app;
    }

    private static void Medir(Resultado<RespostaLancamento> resultado, string operacao)
    {
        if (resultado.Sucesso && !resultado.Valor.Repetido)
        {
            Registrados.Add(1, new KeyValuePair<string, object?>("operacao", operacao));
        }
        else if (!resultado.Sucesso)
        {
            Recusados.Add(1, new KeyValuePair<string, object?>("codigo", resultado.Erro!.Codigo));
        }
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
