using System.Diagnostics;
using System.Text.Json;
using Lancamentos.Application;
using Lancamentos.Domain.Contas;
using Ledger.Contracts;
using Microsoft.EntityFrameworkCore;
using Ledger.Seguranca;
using Npgsql;

namespace Lancamentos.Infrastructure;

public sealed class RepositorioDeContas : IRepositorioDeContas
{
    private readonly LancamentosDbContext _db;

    public RepositorioDeContas(LancamentosDbContext db) => _db = db;

    public Task<Conta?> ObterContaAsync(Guid contaId, CancellationToken ct) =>
        _db.Contas.FirstOrDefaultAsync(c => c.Id == contaId, ct);

    public Task<Lancamento?> ObterLancamentoAsync(Guid contaId, Guid lancamentoId, CancellationToken ct) =>
        _db.Lancamentos.AsNoTracking().FirstOrDefaultAsync(l => l.ContaId == contaId && l.Id == lancamentoId, ct);

    public Task<Lancamento?> ObterPorChaveAsync(Guid contaId, string idempotencyKey, CancellationToken ct) =>
        _db.Lancamentos.AsNoTracking().FirstOrDefaultAsync(l => l.ContaId == contaId && l.IdempotencyKey == idempotencyKey, ct);

    public Task<bool> JaFoiEstornadoAsync(Guid lancamentoId, CancellationToken ct) =>
        _db.Lancamentos.AnyAsync(l => l.LancamentoOriginalId == lancamentoId, ct);

    public async Task<Guid?> ObterTitularAsync(Guid contaId, CancellationToken ct) =>
        await _db.Contas.AsNoTracking()
            .Where(c => c.Id == contaId)
            .Select(c => (Guid?)c.TitularId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

    public void Adicionar(Conta conta) => _db.Contas.Add(conta);

    public void Adicionar(Lancamento lancamento) => _db.Lancamentos.Add(lancamento);
}

public sealed class OutboxEf : IOutbox
{
    private readonly LancamentosDbContext _db;
    private readonly TimeProvider _relogio;

    public OutboxEf(LancamentosDbContext db, TimeProvider relogio)
    {
        _db = db;
        _relogio = relogio;
    }

    public void Enfileirar(LancamentoRegistradoV1 evento)
    {
        ArgumentNullException.ThrowIfNull(evento);

        _db.Outbox.Add(new MensagemOutbox
        {
            Id = evento.EventId,
            Tipo = LancamentoRegistradoV1.Tipo,
            Payload = JsonSerializer.Serialize(evento, JsonSerializerOptions.Web),
            CriadaEm = _relogio.GetUtcNow(),

            // Guarda o contexto de rastreamento para que o trace continue no consumidor.
            TraceParent = Activity.Current?.Id,
        });
    }
}

public sealed class UnidadeDeTrabalhoEf : IUnidadeDeTrabalho
{
    private readonly LancamentosDbContext _db;

    public UnidadeDeTrabalhoEf(LancamentosDbContext db) => _db = db;

    public async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            // SaveChanges já envolve todos os INSERT/UPDATE numa única transação.
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflitoDeConcorrenciaException("A conta foi alterada por outra operação.", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pg)
        {
            throw new ViolacaoDeUnicidadeException($"Restrição única violada: {pg.ConstraintName}", ex) { Restricao = pg.ConstraintName };
        }
    }

    public void Descartar() => _db.ChangeTracker.Clear();
}

public sealed class VerificadorDeTitularidade : IVerificadorDeTitularidade
{
    private readonly IRepositorioDeContas _repositorio;

    public VerificadorDeTitularidade(IRepositorioDeContas repositorio) => _repositorio = repositorio;

    public Task<Guid?> ObterTitularAsync(Guid contaId, CancellationToken ct) => _repositorio.ObterTitularAsync(contaId, ct);
}
