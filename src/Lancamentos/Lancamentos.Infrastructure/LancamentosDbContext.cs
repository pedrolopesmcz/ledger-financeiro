using Lancamentos.Domain.Contas;
using Microsoft.EntityFrameworkCore;

namespace Lancamentos.Infrastructure;

public sealed class LancamentosDbContext : DbContext
{
    public LancamentosDbContext(DbContextOptions<LancamentosDbContext> options)
        : base(options)
    {
    }

    public DbSet<Conta> Contas => Set<Conta>();

    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();

    public DbSet<MensagemOutbox> Outbox => Set<MensagemOutbox>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Conta>(b =>
        {
            b.ToTable("contas");
            b.HasKey(c => c.Id);
            b.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(c => c.Tipo).HasColumnName("tipo").HasConversion<short>();
            b.Property(c => c.Saldo).HasColumnName("saldo").HasPrecision(18, 2);
            b.Property(c => c.Limite).HasColumnName("limite").HasPrecision(18, 2);
            b.Property(c => c.AbertaEm).HasColumnName("aberta_em");

            // Concorrência otimista (ADR 0004): o UPDATE leva "WHERE xmin = <valor lido>".
            // Se outra transação alterou a linha antes, nenhuma linha é afetada e o EF
            // lança DbUpdateConcurrencyException. Propriedade de sombra: o domínio não sabe disso.
            b.Property<uint>("xmin")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });

        modelBuilder.Entity<Lancamento>(b =>
        {
            b.ToTable("lancamentos");
            b.HasKey(l => l.Id);
            b.Property(l => l.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(l => l.ContaId).HasColumnName("conta_id");
            b.Property(l => l.Operacao).HasColumnName("operacao").HasConversion<short>();
            b.Property(l => l.Natureza).HasColumnName("natureza").HasConversion<short>();
            b.Property(l => l.Valor).HasColumnName("valor").HasPrecision(18, 2);
            b.Property(l => l.SaldoApos).HasColumnName("saldo_apos").HasPrecision(18, 2);
            b.Property(l => l.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(Lancamento.TamanhoMaximoChave);
            b.Property(l => l.Descricao).HasColumnName("descricao").HasMaxLength(Lancamento.TamanhoMaximoDescricao);
            b.Property(l => l.LancamentoOriginalId).HasColumnName("lancamento_original_id");
            b.Property(l => l.OcorridoEm).HasColumnName("ocorrido_em");
            b.Ignore(l => l.ValorComSinal);
        });

        modelBuilder.Entity<MensagemOutbox>(b =>
        {
            b.ToTable("outbox");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            b.Property(m => m.Tipo).HasColumnName("tipo");
            b.Property(m => m.Payload).HasColumnName("payload").HasColumnType("jsonb");
            b.Property(m => m.CriadaEm).HasColumnName("criada_em");
            b.Property(m => m.PublicadaEm).HasColumnName("publicada_em");
            b.Property(m => m.Tentativas).HasColumnName("tentativas");
            b.Property(m => m.UltimoErro).HasColumnName("ultimo_erro");
            b.Property(m => m.TraceParent).HasColumnName("trace_parent");
        });
    }
}

public sealed class MensagemOutbox
{
    public Guid Id { get; set; }

    public string Tipo { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset CriadaEm { get; set; }

    public DateTimeOffset? PublicadaEm { get; set; }

    public int Tentativas { get; set; }

    public string? UltimoErro { get; set; }

    public string? TraceParent { get; set; }
}
