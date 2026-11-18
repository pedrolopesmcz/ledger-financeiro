using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Ledger.Seguranca;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.TestesComuns;

/// <summary>
/// Emite tokens JWT assinados com uma chave simétrica de teste, no mesmo
/// formato dos tokens do Keycloak (sub, scope, iss, aud). Os serviços aceitam
/// essa chave apenas nos ambientes Development e Testing.
/// </summary>
public static class Tokens
{
    public const string Chave = "chave-simetrica-de-teste-com-mais-de-32-bytes";
    public const string Emissor = "https://testes.ledger/realms/ledger";
    public const string Audiencia = "ledger-api";

    public static readonly string[] EscoposDoBackoffice =
    [
        Escopos.LancamentosWrite, Escopos.LancamentosEstorno, Escopos.SaldoRead, Escopos.ContasAdmin, Escopos.ContasTodas,
    ];

    public static readonly string[] EscoposDoCliente = [Escopos.LancamentosWrite, Escopos.SaldoRead];

    public static IReadOnlyDictionary<string, string?> Configuracao { get; } = new Dictionary<string, string?>
    {
        ["Autenticacao:ChaveDeTeste"] = Chave,
        ["Autenticacao:Emissor"] = Emissor,
        ["Autenticacao:Audiencia"] = Audiencia,
    };

    public static string Gerar(Guid sujeito, IEnumerable<string> escopos, string emissor = Emissor, TimeSpan? validade = null, string chave = Chave)
    {
        var agora = DateTime.UtcNow;
        var expira = agora + (validade ?? TimeSpan.FromMinutes(5));
        var descricao = new SecurityTokenDescriptor
        {
            Issuer = emissor,
            Audience = Audiencia,
            IssuedAt = expira < agora ? expira.AddMinutes(-1) : agora,
            NotBefore = expira < agora ? expira.AddMinutes(-1) : agora,
            Expires = expira,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", sujeito.ToString()),
                new Claim("scope", string.Join(' ', escopos)),
            ]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descricao);
    }

    public static string Backoffice() => Gerar(Guid.NewGuid(), EscoposDoBackoffice);

    public static string Cliente(Guid titular) => Gerar(titular, EscoposDoCliente);

    public static HttpClient ComToken(this HttpClient client, string token)
    {
        ArgumentNullException.ThrowIfNull(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
