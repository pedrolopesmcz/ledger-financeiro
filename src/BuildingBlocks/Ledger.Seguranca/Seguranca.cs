using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Ledger.Seguranca;

public static class Politicas
{
    public const string LancamentosWrite = Escopos.LancamentosWrite;
    public const string LancamentosEstorno = Escopos.LancamentosEstorno;
    public const string SaldoRead = Escopos.SaldoRead;
    public const string ContasAdmin = Escopos.ContasAdmin;
}

public static class Seguranca
{
    /// <summary>
    /// Validação de JWT (assinatura, emissor, audiência, expiração) e políticas
    /// por escopo. Usada pelo gateway E por cada serviço: defesa em
    /// profundidade, o gateway não é o único ponto de segurança.
    /// Toda rota exige usuário autenticado, a menos que seja marcada como anônima.
    /// </summary>
    public static IServiceCollection AddLedgerSeguranca(this IServiceCollection services, IConfiguration configuration, IHostEnvironment ambiente)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(ambiente);

        var opcoes = configuration.GetSection(OpcoesDeAutenticacao.Secao).Get<OpcoesDeAutenticacao>() ?? new OpcoesDeAutenticacao();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // Mantém os nomes originais dos claims ("sub", "scope"), sem o mapeamento legado da Microsoft.
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = opcoes.Emissor,
                    ValidAudience = opcoes.Audiencia,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    NameClaimType = "sub",
                    ClockSkew = TimeSpan.FromSeconds(30),
                };

                if (!string.IsNullOrEmpty(opcoes.ChaveDeTeste))
                {
                    if (!ambiente.IsDevelopment() && !ambiente.IsEnvironment("Testing"))
                    {
                        throw new InvalidOperationException("Chave de teste não pode ser usada fora de Development/Testing.");
                    }

                    o.TokenValidationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoes.ChaveDeTeste));
                    return;
                }

                o.MetadataAddress = opcoes.EnderecoDeMetadados ?? $"{opcoes.Emissor.TrimEnd('/')}/.well-known/openid-configuration";
                o.RequireHttpsMetadata = opcoes.ExigirHttps;
            });

        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Politicas.LancamentosWrite, p => p.RequireAuthenticatedUser().RequireAssertion(c => c.User.Tem(Escopos.LancamentosWrite)))
            .AddPolicy(Politicas.LancamentosEstorno, p => p.RequireAuthenticatedUser().RequireAssertion(c => c.User.Tem(Escopos.LancamentosEstorno)))
            .AddPolicy(Politicas.SaldoRead, p => p.RequireAuthenticatedUser().RequireAssertion(c => c.User.Tem(Escopos.SaldoRead)))
            .AddPolicy(Politicas.ContasAdmin, p => p.RequireAuthenticatedUser().RequireAssertion(c => c.User.Tem(Escopos.ContasAdmin)));

        return services;
    }
}
