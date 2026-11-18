using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using Ledger.Observabilidade;
using Ledger.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Lancamentos.Infrastructure;

public sealed class OutboxOptions
{
    public const string Secao = "Outbox";

    public bool Habilitado { get; set; } = true;

    public int TamanhoDoLote { get; set; } = 100;

    public TimeSpan Intervalo { get; set; } = TimeSpan.FromMilliseconds(200);

    public TimeSpan EsperaMaximaEmFalha { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Relay da outbox (ADR 0002). Lê eventos pendentes com FOR UPDATE SKIP LOCKED,
/// o que permite várias instâncias em paralelo sem publicar o mesmo evento
/// duas vezes ao mesmo tempo. Só marca como publicado após a confirmação do
/// broker. Se o broker estiver fora, espera com backoff exponencial e tenta
/// de novo: nenhum evento se perde, os lançamentos seguem sendo aceitos.
/// </summary>
public sealed partial class PublicadorOutbox : BackgroundService
{
    private const string SqlPendentes = """
        SELECT id, tipo, payload::text, trace_parent
        FROM outbox
        WHERE publicada_em IS NULL
        ORDER BY criada_em
        LIMIT @lote
        FOR UPDATE SKIP LOCKED
        """;

    private static readonly Counter<long> Publicados = Telemetria.Medidor.CreateCounter<long>("ledger.outbox.publicados", description: "Eventos publicados no broker");
    private static readonly Counter<long> Falhas = Telemetria.Medidor.CreateCounter<long>("ledger.outbox.falhas", description: "Falhas ao publicar no broker");

    private readonly NpgsqlDataSource _dataSource;
    private readonly IPublicadorDeEventos _publicador;
    private readonly OutboxOptions _options;
    private readonly ILogger<PublicadorOutbox> _logger;

    private long _pendentes;
    private double _idadeSegundos;

    public PublicadorOutbox(
        NpgsqlDataSource dataSource,
        IPublicadorDeEventos publicador,
        IOptions<OutboxOptions> options,
        ILogger<PublicadorOutbox> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _dataSource = dataSource;
        _publicador = publicador;
        _options = options.Value;
        _logger = logger;

        // Sinais de saúde da outbox: se crescerem, o broker está fora ou o relay não dá conta.
        Telemetria.Medidor.CreateObservableGauge("ledger.outbox.pendentes", () => Interlocked.Read(ref _pendentes), description: "Eventos aguardando publicação");
        Telemetria.Medidor.CreateObservableGauge("ledger.outbox.idade", () => Volatile.Read(ref _idadeSegundos), unit: "s", description: "Idade do evento pendente mais antigo");
    }

    /// <summary>Publica um lote. Retorna quantos eventos foram publicados. Público para testes.</summary>
    public async Task<int> PublicarLoteAsync(CancellationToken ct)
    {
        await using var conexao = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var transacao = await conexao.BeginTransactionAsync(ct).ConfigureAwait(false);

        var pendentes = new List<(Guid Id, string Tipo, string Payload, string? TraceParent)>();
        await using (var consulta = new NpgsqlCommand(SqlPendentes, conexao, transacao))
        {
            consulta.Parameters.AddWithValue("lote", _options.TamanhoDoLote);
            await using var leitor = await consulta.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await leitor.ReadAsync(ct).ConfigureAwait(false))
            {
                pendentes.Add((
                    leitor.GetGuid(0),
                    leitor.GetString(1),
                    leitor.GetString(2),
                    await leitor.IsDBNullAsync(3, ct).ConfigureAwait(false) ? null : leitor.GetString(3)));
            }
        }

        var publicados = new List<Guid>();
        Exception? falha = null;
        Guid? idComFalha = null;

        foreach (var mensagem in pendentes)
        {
            // Continua o trace iniciado na requisição HTTP que gerou o lançamento.
            using var atividade = Telemetria.Fonte.StartActivity("outbox publicar", ActivityKind.Producer, Telemetria.ContextoPai(mensagem.TraceParent));
            atividade?.SetTag("messaging.message.id", mensagem.Id);
            try
            {
                await _publicador.PublicarAsync(mensagem.Tipo, mensagem.Id, Encoding.UTF8.GetBytes(mensagem.Payload), atividade?.Id ?? mensagem.TraceParent, ct)
                    .ConfigureAwait(false);
                publicados.Add(mensagem.Id);
                Publicados.Add(1);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                atividade?.SetStatus(ActivityStatusCode.Error, ex.Message);
                Falhas.Add(1);
                // Para no primeiro erro: o broker provavelmente está fora, não adianta insistir no lote.
                falha = ex;
                idComFalha = mensagem.Id;
                break;
            }
        }

        if (publicados.Count > 0)
        {
            await using var marca = new NpgsqlCommand("UPDATE outbox SET publicada_em = now() WHERE id = ANY(@ids)", conexao, transacao);
            marca.Parameters.AddWithValue("ids", publicados.ToArray());
            await marca.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        if (idComFalha is { } id)
        {
            await using var registra = new NpgsqlCommand(
                "UPDATE outbox SET tentativas = tentativas + 1, ultimo_erro = left(@erro, 500) WHERE id = @id", conexao, transacao);
            registra.Parameters.AddWithValue("erro", falha!.Message);
            registra.Parameters.AddWithValue("id", id);
            await registra.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transacao.CommitAsync(ct).ConfigureAwait(false);

        if (falha is not null)
        {
            throw new InvalidOperationException("Falha ao publicar evento da outbox.", falha);
        }

        return publicados.Count;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Habilitado)
        {
            return;
        }

        var espera = _options.Intervalo;
        var proximaMedicao = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (DateTimeOffset.UtcNow >= proximaMedicao)
            {
                await MedirAsync(stoppingToken).ConfigureAwait(false);
                proximaMedicao = DateTimeOffset.UtcNow.AddSeconds(5);
            }

            try
            {
                var quantidade = await PublicarLoteAsync(stoppingToken).ConfigureAwait(false);
                espera = _options.Intervalo;

                // Lote cheio: provavelmente há mais pendentes, segue sem esperar.
                if (quantidade == _options.TamanhoDoLote)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                espera = TimeSpan.FromTicks(Math.Min(espera.Ticks * 2, _options.EsperaMaximaEmFalha.Ticks));
                LogFalhaNaPublicacao(_logger, ex, espera.TotalMilliseconds);
            }

            try
            {
                await Task.Delay(espera, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task MedirAsync(CancellationToken ct)
    {
        try
        {
            await using var comando = _dataSource.CreateCommand(
                "SELECT count(*), COALESCE(EXTRACT(EPOCH FROM now() - min(criada_em)), 0)::float8 FROM outbox WHERE publicada_em IS NULL");
            await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (await leitor.ReadAsync(ct).ConfigureAwait(false))
            {
                Interlocked.Exchange(ref _pendentes, leitor.GetInt64(0));
                Volatile.Write(ref _idadeSegundos, leitor.GetDouble(1));
            }
        }
        catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
        {
            LogFalhaNaMedicao(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Não foi possível medir a outbox")]
    private static partial void LogFalhaNaMedicao(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao publicar a outbox; nova tentativa em {EsperaMs} ms")]
    private static partial void LogFalhaNaPublicacao(ILogger logger, Exception ex, double esperaMs);
}
