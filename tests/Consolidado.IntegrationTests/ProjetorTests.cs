using Consolidado.Core;
using Consolidado.Infrastructure;
using Npgsql;
using static Consolidado.IntegrationTests.Eventos;

namespace Consolidado.IntegrationTests;

[Collection(ColecaoIntegracao.Nome)]
public sealed class ProjetorTests : IAsyncLifetime
{
    private readonly Containers _containers;
    private NpgsqlDataSource _dataSource = null!;
    private ProjetorPostgres _projetor = null!;
    private ConsultasPostgres _consultas = null!;

    public ProjetorTests(Containers containers) => _containers = containers;

    public Task InitializeAsync()
    {
        _dataSource = NpgsqlDataSource.Create(_containers.Postgres.GetConnectionString());
        var dia = new DiaContabil();
        _projetor = new ProjetorPostgres(_dataSource, dia);
        _consultas = new ConsultasPostgres(_dataSource, dia);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _dataSource.DisposeAsync();

    [Fact]
    public async Task Evento_atualiza_posicao_e_consolidado_do_dia()
    {
        var conta = Guid.NewGuid();
        await _projetor.ProjetarAsync(Novo(conta, 100m, Brasilia(17, 10)), CancellationToken.None);
        await _projetor.ProjetarAsync(Novo(conta, -30m, Brasilia(17, 11)), CancellationToken.None);

        var posicao = await _consultas.ObterPosicaoAsync(conta, CancellationToken.None);
        var dia = await _consultas.ObterConsolidadoAsync(conta, new DateOnly(2026, 11, 17), CancellationToken.None);

        Assert.Equal(70m, posicao!.Saldo);
        Assert.Equal(Brasilia(17, 11), posicao.PosicaoEm);
        Assert.Equal(new ConsolidadoDiario(conta, new DateOnly(2026, 11, 17), 0m, 100m, 30m, 70m, 2, Brasilia(17, 11)), dia);
    }

    [Fact]
    public async Task Evento_duplicado_e_ignorado_e_nao_soma_duas_vezes()
    {
        var conta = Guid.NewGuid();
        var evento = Novo(conta, 100m, Brasilia(17, 10));

        Assert.True(await _projetor.ProjetarAsync(evento, CancellationToken.None));
        Assert.False(await _projetor.ProjetarAsync(evento, CancellationToken.None));

        Assert.Equal(100m, (await _consultas.ObterPosicaoAsync(conta, CancellationToken.None))!.Saldo);
    }

    [Fact]
    public async Task Evento_atrasado_recalcula_o_saldo_dos_dias_seguintes()
    {
        var conta = Guid.NewGuid();
        await _projetor.ProjetarAsync(Novo(conta, 100m, Brasilia(15, 9)), CancellationToken.None);
        await _projetor.ProjetarAsync(Novo(conta, 50m, Brasilia(17, 9)), CancellationToken.None);

        // Chega depois um débito do dia 16 (ex.: consolidado ficou fora do ar na virada do dia).
        await _projetor.ProjetarAsync(Novo(conta, -30m, Brasilia(16, 18)), CancellationToken.None);

        var dia16 = await _consultas.ObterConsolidadoAsync(conta, new DateOnly(2026, 11, 16), CancellationToken.None);
        var dia17 = await _consultas.ObterConsolidadoAsync(conta, new DateOnly(2026, 11, 17), CancellationToken.None);

        Assert.Equal((100m, 0m, 30m, 70m), (dia16!.SaldoInicial, dia16.Creditos, dia16.Debitos, dia16.SaldoFinal));
        Assert.Equal((70m, 50m, 0m, 120m), (dia17!.SaldoInicial, dia17.Creditos, dia17.Debitos, dia17.SaldoFinal));
        Assert.Equal(120m, (await _consultas.ObterPosicaoAsync(conta, CancellationToken.None))!.Saldo);
    }

    [Fact]
    public async Task Dia_sem_movimento_carrega_o_saldo_do_dia_anterior()
    {
        var conta = Guid.NewGuid();
        await _projetor.ProjetarAsync(Novo(conta, 80m, Brasilia(10, 9)), CancellationToken.None);

        var dia = await _consultas.ObterConsolidadoAsync(conta, new DateOnly(2026, 11, 12), CancellationToken.None);

        Assert.Equal((80m, 0m, 0m, 80m, 0), (dia!.SaldoInicial, dia.Creditos, dia.Debitos, dia.SaldoFinal, dia.Quantidade));
    }

    [Fact]
    public async Task Saldo_em_um_instante_considera_so_os_movimentos_ate_ele()
    {
        var conta = Guid.NewGuid();
        await _projetor.ProjetarAsync(Novo(conta, 500m, Brasilia(16, 20)), CancellationToken.None);
        await _projetor.ProjetarAsync(Novo(conta, -100m, Brasilia(17, 10)), CancellationToken.None);
        await _projetor.ProjetarAsync(Novo(conta, -200m, Brasilia(17, 14)), CancellationToken.None);

        var meioDia = await _consultas.ObterSaldoNoInstanteAsync(conta, Brasilia(17, 12), CancellationToken.None);
        var antesDeTudo = await _consultas.ObterSaldoNoInstanteAsync(conta, Brasilia(16, 8), CancellationToken.None);

        Assert.Equal(400m, meioDia!.Saldo);
        Assert.Equal(0m, antesDeTudo!.Saldo);
    }

    [Fact]
    public async Task Duzentos_eventos_em_paralelo_na_mesma_conta_somam_corretamente()
    {
        var conta = Guid.NewGuid();
        var eventos = Enumerable.Range(1, 200)
            .Select(i => Novo(conta, i % 2 == 0 ? 10m : -3m, Brasilia(17 + (i % 2), 10, i % 60)))
            .ToList();

        await Task.WhenAll(eventos.Select(e => _projetor.ProjetarAsync(e, CancellationToken.None)));

        var esperado = eventos.Sum(e => e.ValorComSinal);
        Assert.Equal(esperado, (await _consultas.ObterPosicaoAsync(conta, CancellationToken.None))!.Saldo);
        var dia18 = await _consultas.ObterConsolidadoAsync(conta, new DateOnly(2026, 11, 18), CancellationToken.None);
        Assert.Equal(esperado, dia18!.SaldoFinal);
    }

    [Fact]
    public async Task Conta_sem_movimentos_nao_existe_no_consolidado()
    {
        Assert.Null(await _consultas.ObterPosicaoAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await _consultas.ObterConsolidadoAsync(Guid.NewGuid(), new DateOnly(2026, 11, 17), CancellationToken.None));
    }
}
