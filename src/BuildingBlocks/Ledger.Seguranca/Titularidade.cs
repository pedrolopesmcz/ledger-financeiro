using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Seguranca;

/// <summary>Cada serviço sabe responder quem é o titular de uma conta (ou null, se ela não existe para ele).</summary>
public interface IVerificadorDeTitularidade
{
    Task<Guid?> ObterTitularAsync(Guid contaId, CancellationToken ct);
}

/// <summary>
/// Filtro aplicado às rotas /contas/{contaId}/...: o usuário só acessa contas
/// das quais é titular. Conta de outra pessoa responde 404, igual a uma conta
/// inexistente, para não revelar que ela existe (proteção contra enumeração).
/// Sistemas internos com o escopo contas:todas não passam por essa checagem.
/// </summary>
public sealed class FiltroDeTitularidade : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var http = context.HttpContext;
        if (http.User.AcessaTodasAsContas())
        {
            return await next(context).ConfigureAwait(false);
        }

        if (!Guid.TryParse(http.Request.RouteValues["contaId"]?.ToString(), out var contaId) || http.User.Sujeito() is not { } usuario)
        {
            return Results.Forbid();
        }

        var verificador = http.RequestServices.GetRequiredService<IVerificadorDeTitularidade>();
        var titular = await verificador.ObterTitularAsync(contaId, http.RequestAborted).ConfigureAwait(false);

        return titular == usuario
            ? await next(context).ConfigureAwait(false)
            : Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Conta não encontrada.",
                type: "https://ledger-financeiro/erros/conta_nao_encontrada",
                extensions: new Dictionary<string, object?> { ["codigo"] = "conta_nao_encontrada" });
    }
}
