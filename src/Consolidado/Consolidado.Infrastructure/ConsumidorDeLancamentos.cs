using System.Text;
using System.Text.Json;
using Consolidado.Core;
using Ledger.Contracts;
using Ledger.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Consolidado.Infrastructure;

public sealed class OpcoesDoConsumidor
{
    public const string Secao = "Consumidor";

    public ushort Prefetch { get; set; } = 32;

    public ushort Paralelismo { get; set; } = 8;

    public TimeSpan EsperaParaReconectar { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Consome LancamentoRegistradoV1. Confirma (ack) só depois do commit no
/// banco: se o processo cair no meio, a mensagem volta para a fila.
/// Falha transitória: republica na fila de retry, que devolve a mensagem
/// depois de um atraso. Mensagem inválida ou tentativas esgotadas: DLQ.
/// </summary>
public sealed partial class ConsumidorDeLancamentos : BackgroundService
{
    public const string CabecalhoTentativas = "x-tentativas";
    public const string CabecalhoErro = "x-ultimo-erro";

    private readonly IServiceScopeFactory _scopes;
    private readonly RabbitMqOptions _rabbit;
    private readonly OpcoesDoConsumidor _opcoes;
    private readonly PoliticaDeRetentativa _politica;
    private readonly ILogger<ConsumidorDeLancamentos> _logger;

    public ConsumidorDeLancamentos(
        IServiceScopeFactory scopes,
        IOptions<RabbitMqOptions> rabbit,
        IOptions<OpcoesDoConsumidor> opcoes,
        PoliticaDeRetentativa politica,
        ILogger<ConsumidorDeLancamentos> logger)
    {
        ArgumentNullException.ThrowIfNull(rabbit);
        ArgumentNullException.ThrowIfNull(opcoes);
        _scopes = scopes;
        _rabbit = rabbit.Value;
        _opcoes = opcoes.Value;
        _politica = politica;
        _logger = logger;
    }

    public bool Conectado { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumirAteDesconectarAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogBrokerIndisponivel(_logger, ex);
            }

            Conectado = false;
            try
            {
                await Task.Delay(_opcoes.EsperaParaReconectar, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ConsumirAteDesconectarAsync(CancellationToken ct)
    {
        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(_rabbit.Uri),
            ClientProvidedName = _rabbit.NomeCliente,
            ConsumerDispatchConcurrency = _opcoes.Paralelismo,
        };

        await using var conexao = await fabrica.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct).ConfigureAwait(false);

        await Topologia.DeclararConsolidadoAsync(canal, ct).ConfigureAwait(false);
        await canal.BasicQosAsync(0, _opcoes.Prefetch, global: false, ct).ConfigureAwait(false);

        var desconectou = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        conexao.ConnectionShutdownAsync += (_, _) =>
        {
            desconectou.TrySetResult();
            return Task.CompletedTask;
        };

        var consumidor = new AsyncEventingBasicConsumer(canal);
        consumidor.ReceivedAsync += (_, entrega) => ProcessarAsync(canal, entrega, ct);
        await canal.BasicConsumeAsync(Topologia.FilaConsolidado, autoAck: false, consumidor, ct).ConfigureAwait(false);

        Conectado = true;
        LogConectado(_logger, Topologia.FilaConsolidado);
        await desconectou.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private async Task ProcessarAsync(IChannel canal, BasicDeliverEventArgs entrega, CancellationToken ct)
    {
        var tentativas = LerTentativas(entrega.BasicProperties.Headers);
        LancamentoRegistradoV1? evento = null;
        try
        {
            evento = JsonSerializer.Deserialize<LancamentoRegistradoV1>(entrega.Body.Span, JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            // tratado abaixo como mensagem inválida
        }

        if (evento is null || evento.EventId == Guid.Empty)
        {
            await EncaminharAsync(canal, entrega, DestinoDaFalha.FilaDeMensagensMortas, tentativas, "mensagem inválida", ct).ConfigureAwait(false);
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            await using var escopo = _scopes.CreateAsyncScope();
            var projetor = escopo.ServiceProvider.GetRequiredService<IProjetorDeSaldos>();

            if (await projetor.ProjetarAsync(evento, ct).ConfigureAwait(false))
            {
                await escopo.ServiceProvider.GetRequiredService<InvalidadorDeCache>().InvalidarAsync(evento, ct).ConfigureAwait(false);
            }
            else
            {
                LogDuplicata(_logger, evento.EventId);
            }

            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var destino = _politica.Decidir(tentativas + 1, mensagemInvalida: false);
            LogFalhaAoProjetar(_logger, ex, evento.EventId, tentativas + 1, destino);
            await EncaminharAsync(canal, entrega, destino, tentativas + 1, ex.Message, ct).ConfigureAwait(false);
            await canal.BasicAckAsync(entrega.DeliveryTag, multiple: false, ct).ConfigureAwait(false);
        }
    }

    private async Task EncaminharAsync(IChannel canal, BasicDeliverEventArgs entrega, DestinoDaFalha destino, int tentativas, string erro, CancellationToken ct)
    {
        var original = entrega.BasicProperties;
        var cabecalhos = original.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(original.Headers);
        cabecalhos[CabecalhoTentativas] = tentativas;
        cabecalhos[CabecalhoErro] = erro.Length > 300 ? erro[..300] : erro;

        var propriedades = new BasicProperties
        {
            MessageId = original.MessageId,
            Type = original.Type,
            ContentType = original.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            Headers = cabecalhos,
        };

        if (destino == DestinoDaFalha.Retentar)
        {
            // TTL por mensagem: ao expirar na fila de retry, ela volta para a fila principal.
            propriedades.Expiration = ((long)_politica.Atraso.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await canal.BasicPublishAsync(Topologia.ExchangeRetry, Topologia.FilaConsolidadoRetry, mandatory: true, propriedades, entrega.Body, ct).ConfigureAwait(false);
        }
        else
        {
            await canal.BasicPublishAsync(Topologia.ExchangeDlq, Topologia.FilaConsolidadoDlq, mandatory: true, propriedades, entrega.Body, ct).ConfigureAwait(false);
        }
    }

    private static int LerTentativas(IDictionary<string, object?>? cabecalhos)
    {
        if (cabecalhos is null || !cabecalhos.TryGetValue(CabecalhoTentativas, out var valor) || valor is null)
        {
            return 0;
        }

        return valor switch
        {
            int i => i,
            long l => (int)l,
            byte[] b when int.TryParse(Encoding.UTF8.GetString(b), out var n) => n,
            _ => 0,
        };
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consumindo a fila {Fila}")]
    private static partial void LogConectado(ILogger logger, string fila);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Broker indisponível; nova tentativa de conexão em instantes")]
    private static partial void LogBrokerIndisponivel(ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Evento {EventId} já processado; ignorado")]
    private static partial void LogDuplicata(ILogger logger, Guid eventId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Falha ao projetar {EventId} (tentativa {Tentativa}); destino: {Destino}")]
    private static partial void LogFalhaAoProjetar(ILogger logger, Exception ex, Guid eventId, int tentativa, DestinoDaFalha destino);
}
