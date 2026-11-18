using Consolidado.Core;
using Consolidado.Infrastructure;
using Ledger.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Consolidado.IntegrationTests.Eventos;

namespace Consolidado.IntegrationTests;

[Collection(ColecaoIntegracao.Nome)]
public sealed class WorkerTests
{
    private readonly Containers _containers;

    public WorkerTests(Containers containers) => _containers = containers;

    private async Task<decimal?> SaldoAsync(Guid conta)
    {
        await using var dataSource = NpgsqlDataSource.Create(_containers.Postgres.GetConnectionString());
        return (await new ConsultasPostgres(dataSource, new DiaContabil()).ObterPosicaoAsync(conta, CancellationToken.None))?.Saldo;
    }

    [Fact]
    public async Task Com_o_consolidado_fora_os_eventos_esperam_na_fila_e_convergem_quando_ele_volta()
    {
        var conta = Guid.NewGuid();
        var eventos = Enumerable.Range(1, 20).Select(i => Novo(conta, i, Brasilia(18, 9, i))).ToList();

        // Consolidado fora do ar: os eventos chegam e ficam guardados na fila durável.
        foreach (var evento in eventos)
        {
            await _containers.PublicarAsync(evento);
        }

        Assert.True(await _containers.MensagensNaFilaAsync() >= 20);
        Assert.Null(await SaldoAsync(conta));

        // Consolidado volta: processa o acumulado.
        using var worker = _containers.CriarWorker();
        await worker.StartAsync();

        await Esperar.AteAsync(async () => await SaldoAsync(conta) == 210m, TimeSpan.FromSeconds(30), "saldo convergir para 210");
        await worker.StopAsync();
    }

    [Fact]
    public async Task Falha_transitoria_e_retentada_e_o_evento_e_processado_uma_unica_vez()
    {
        var conta = Guid.NewGuid();
        ProjetorQueFalha? projetorComFalha = null;
        using var worker = _containers.CriarWorker(services =>
            services.AddSingleton<IProjetorDeSaldos>(sp =>
                projetorComFalha = new ProjetorQueFalha(new ProjetorPostgres(sp.GetRequiredService<NpgsqlDataSource>(), sp.GetRequiredService<DiaContabil>()), falhasIniciais: 2)));
        await worker.StartAsync();

        await _containers.PublicarAsync(Novo(conta, 75m, Brasilia(18, 10)));

        await Esperar.AteAsync(async () => await SaldoAsync(conta) == 75m, TimeSpan.FromSeconds(30), "evento processado após retentativas");
        Assert.True(projetorComFalha!.Chamadas >= 3);
        await worker.StopAsync();
    }

    [Fact]
    public async Task Mensagem_invalida_vai_para_a_dlq_sem_travar_a_fila()
    {
        var conta = Guid.NewGuid();
        using var worker = _containers.CriarWorker();
        await worker.StartAsync();

        var idInvalida = Guid.NewGuid().ToString();
        await _containers.PublicarBrutoAsync("isto não é json"u8.ToArray(), idInvalida);
        await _containers.PublicarAsync(Novo(conta, 10m, Brasilia(18, 11)));

        await Esperar.AteAsync(async () => await SaldoAsync(conta) == 10m, TimeSpan.FromSeconds(30), "evento válido seguinte processado");
        await Esperar.AteAsync(() => _containers.MensagemNaDlqAsync(idInvalida), TimeSpan.FromSeconds(30), "mensagem inválida na DLQ");
        await worker.StopAsync();
    }

    private sealed class ProjetorQueFalha : IProjetorDeSaldos
    {
        private readonly IProjetorDeSaldos _real;
        private int _falhasRestantes;
        private int _chamadas;

        public ProjetorQueFalha(IProjetorDeSaldos real, int falhasIniciais)
        {
            _real = real;
            _falhasRestantes = falhasIniciais;
        }

        public int Chamadas => _chamadas;

        public Task<bool> ProjetarAsync(LancamentoRegistradoV1 evento, CancellationToken ct)
        {
            Interlocked.Increment(ref _chamadas);
            if (Interlocked.Decrement(ref _falhasRestantes) >= 0)
            {
                throw new TimeoutException("banco indisponível (simulado)");
            }

            return _real.ProjetarAsync(evento, ct);
        }
    }
}
