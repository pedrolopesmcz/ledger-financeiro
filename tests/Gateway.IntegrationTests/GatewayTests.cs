using System.Net;
using Ledger.TestesComuns;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.IntegrationTests;

/// <summary>
/// Sobe dois serviços falsos (stubs) em portas reais e o gateway apontando
/// para eles: prova roteamento, autenticação e rate limiting de ponta a ponta.
/// </summary>
public sealed class GatewayTests : IAsyncLifetime
{
    private WebApplication _lancamentos = null!;
    private WebApplication _consolidado = null!;
    private GatewayFactory _gateway = null!;

    public async Task InitializeAsync()
    {
        (_lancamentos, var urlLancamentos) = await StubAsync("lancamentos");
        (_consolidado, var urlConsolidado) = await StubAsync("consolidado");
        _gateway = new GatewayFactory(urlLancamentos, urlConsolidado, porSegundo: 1, rajada: 3);
    }

    public async Task DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _lancamentos.DisposeAsync();
        await _consolidado.DisposeAsync();
    }

    [Fact]
    public async Task Sem_token_o_gateway_responde_401_sem_chamar_os_servicos()
    {
        using var anonimo = _gateway.CreateClient();

        var resposta = await anonimo.GetAsync($"/contas/{Guid.NewGuid()}/saldo");

        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
    }

    [Theory]
    [InlineData("/contas/{0}/saldo", "consolidado")]
    [InlineData("/contas/{0}/consolidado/2026-11-18", "consolidado")]
    [InlineData("/contas/{0}", "lancamentos")]
    [InlineData("/contas/{0}/lancamentos/abc", "lancamentos")]
    public async Task Rotas_sao_encaminhadas_ao_servico_certo_com_o_token(string modelo, string servico)
    {
        using var cliente = _gateway.CreateClient().ComToken(Tokens.Cliente(Guid.NewGuid()));
        var caminho = string.Format(System.Globalization.CultureInfo.InvariantCulture, modelo, Guid.NewGuid());

        var corpo = await cliente.GetStringAsync(caminho);

        Assert.Equal($"{servico}|{caminho}|Bearer", corpo);
    }

    [Fact]
    public async Task Rate_limit_por_cliente_retorna_429_sem_afetar_outro_cliente()
    {
        using var abusivo = _gateway.CreateClient().ComToken(Tokens.Cliente(Guid.NewGuid()));
        using var outro = _gateway.CreateClient().ComToken(Tokens.Cliente(Guid.NewGuid()));
        var caminho = $"/contas/{Guid.NewGuid()}/saldo";

        var respostas = new List<HttpResponseMessage>();
        for (var i = 0; i < 8; i++)
        {
            respostas.Add(await abusivo.GetAsync(caminho));
        }

        Assert.Contains(respostas, r => r.StatusCode == HttpStatusCode.TooManyRequests);
        Assert.True(respostas.First(r => r.StatusCode == HttpStatusCode.TooManyRequests).Headers.Contains("Retry-After"));
        Assert.Equal(HttpStatusCode.OK, (await outro.GetAsync(caminho)).StatusCode);
    }

    [Fact]
    public async Task Health_check_e_anonimo()
    {
        using var anonimo = _gateway.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await anonimo.GetAsync("/health/live")).StatusCode);
    }

    private static async Task<(WebApplication App, string Url)> StubAsync(string nome)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.MapFallback((HttpContext c) =>
            Results.Text($"{nome}|{c.Request.Path}|{c.Request.Headers.Authorization.ToString().Split(' ')[0]}"));
        await app.StartAsync();

        var url = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First();
        return (app, url);
    }

    private sealed class GatewayFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _config;

        public GatewayFactory(string lancamentos, string consolidado, int porSegundo, int rajada)
        {
            _config = new Dictionary<string, string?>(Tokens.Configuracao)
            {
                ["ReverseProxy:Clusters:lancamentos:Destinations:principal:Address"] = lancamentos,
                ["ReverseProxy:Clusters:consolidado:Destinations:principal:Address"] = consolidado,
                ["Limites:PorSegundo"] = porSegundo.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Limites:Rajada"] = rajada.ToString(System.Globalization.CultureInfo.InvariantCulture),
            };
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            foreach (var (chave, valor) in _config)
            {
                builder.UseSetting(chave, valor);
            }
        }
    }
}
