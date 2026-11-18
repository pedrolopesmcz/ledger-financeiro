using System.Security.Claims;

namespace Ledger.Seguranca;

/// <summary>
/// Escopos OAuth 2.0: o que o APLICATIVO pode fazer. Quem é o usuário vem
/// do claim "sub"; de quem é a conta, o banco decide (titular_id).
/// </summary>
public static class Escopos
{
    public const string LancamentosWrite = "lancamentos:write";
    public const string LancamentosEstorno = "lancamentos:estorno";
    public const string SaldoRead = "saldo:read";
    public const string ContasAdmin = "contas:admin";

    /// <summary>Acesso a qualquer conta, sem checar titularidade. Só para sistemas internos (backoffice).</summary>
    public const string ContasTodas = "contas:todas";

    public static bool Tem(this ClaimsPrincipal usuario, string escopo)
    {
        ArgumentNullException.ThrowIfNull(usuario);

        // O Keycloak envia "scope" como uma string separada por espaços.
        return usuario.FindAll("scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Contains(escopo, StringComparer.Ordinal);
    }

    public static bool AcessaTodasAsContas(this ClaimsPrincipal usuario) => usuario.Tem(ContasTodas);

    public static Guid? Sujeito(this ClaimsPrincipal usuario)
    {
        ArgumentNullException.ThrowIfNull(usuario);
        return Guid.TryParse(usuario.FindFirst("sub")?.Value, out var sub) ? sub : null;
    }
}
