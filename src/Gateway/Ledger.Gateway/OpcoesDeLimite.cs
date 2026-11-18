namespace Ledger.Gateway;

public sealed class OpcoesDeLimite
{
    public const string Secao = "Limites";
    public const string PoliticaPorCliente = "por-cliente";

    /// <summary>Requisições por segundo sustentadas por cliente.</summary>
    public int PorSegundo { get; set; } = 100;

    /// <summary>Tamanho máximo de uma rajada.</summary>
    public int Rajada { get; set; } = 200;

    /// <summary>Partição do limite: o usuário (sub) quando autenticado; senão, o IP.</summary>
    public static string ChaveDoCliente(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.User.FindFirst("sub")?.Value
            ?? http.Connection.RemoteIpAddress?.ToString()
            ?? "anonimo";
    }
}
