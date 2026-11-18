using Consolidado.Core;

namespace Consolidado.Api;

public static class Endpoints
{
    public static IEndpointRouteBuilder MapConsolidadoEndpoints(this IEndpointRouteBuilder app)
    {
        var contas = app.MapGroup("/contas/{contaId:guid}").WithTags("Consolidado");

        // Sem "em": saldo atual (com cache). Com "em": saldo naquele instante.
        contas.MapGet("/saldo", async (Guid contaId, DateTimeOffset? em, ServicoDeConsulta servico, CancellationToken ct) =>
        {
            if (em is { } instante)
            {
                return await servico.ObterSaldoNoInstanteAsync(contaId, instante, ct) is { } saldo
                    ? Results.Ok(saldo)
                    : SemMovimentos();
            }

            return await servico.ObterPosicaoAsync(contaId, ct) is { } posicao
                ? Results.Ok(posicao)
                : SemMovimentos();
        });

        contas.MapGet("/consolidado/{dia}", async (Guid contaId, DateOnly dia, ServicoDeConsulta servico, CancellationToken ct) =>
            await servico.ObterConsolidadoAsync(contaId, dia, ct) is { } consolidado
                ? Results.Ok(consolidado)
                : SemMovimentos());

        return app;
    }

    private static IResult SemMovimentos() => Results.Problem(
        statusCode: StatusCodes.Status404NotFound,
        title: "Nenhum lançamento consolidado para esta conta.",
        type: "https://ledger-financeiro/erros/sem_movimentos",
        extensions: new Dictionary<string, object?> { ["codigo"] = "sem_movimentos" });
}
