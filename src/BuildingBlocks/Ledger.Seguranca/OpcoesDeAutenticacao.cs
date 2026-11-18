namespace Ledger.Seguranca;

public sealed class OpcoesDeAutenticacao
{
    public const string Secao = "Autenticacao";

    /// <summary>Emissor esperado no token (claim "iss"). É o endereço que o cliente usa para obter o token.</summary>
    public string Emissor { get; set; } = "http://localhost:8080/realms/ledger";

    /// <summary>
    /// Onde o serviço busca as chaves públicas (JWKS). Dentro da rede do
    /// docker, o Keycloak é "keycloak:8080", diferente do emissor público;
    /// por isso os dois endereços são configurados separadamente.
    /// </summary>
    public string? EnderecoDeMetadados { get; set; }

    public string Audiencia { get; set; } = "ledger-api";

    public bool ExigirHttps { get; set; } = true;

    /// <summary>Chave simétrica para testes automatizados. Recusada fora de Development/Testing.</summary>
    public string? ChaveDeTeste { get; set; }
}
