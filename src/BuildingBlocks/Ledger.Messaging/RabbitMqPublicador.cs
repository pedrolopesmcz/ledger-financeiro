using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Ledger.Messaging;

/// <summary>
/// Publicador com confirmação do broker. A conexão é criada sob demanda e
/// recriada após falha, para que o serviço suba mesmo com o broker fora.
/// </summary>
public sealed class RabbitMqPublicador : IPublicadorDeEventos, IAsyncDisposable
{
    private readonly RabbitMqOptions _options;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _conexao;
    private IChannel? _canal;

    public RabbitMqPublicador(IOptions<RabbitMqOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public async Task PublicarAsync(string tipo, Guid mensagemId, ReadOnlyMemory<byte> corpo, string? traceParent, CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var canal = await ObterCanalAsync(ct).ConfigureAwait(false);

            var propriedades = new BasicProperties
            {
                MessageId = mensagemId.ToString(),
                Type = tipo,
                ContentType = "application/json",
                DeliveryMode = DeliveryModes.Persistent,
                Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?>(),
            };

            if (traceParent is not null)
            {
                propriedades.Headers["traceparent"] = traceParent;
            }

            // mandatory: true faz o broker devolver mensagens que não chegam a nenhuma fila,
            // em vez de descartá-las em silêncio.
            await canal.BasicPublishAsync(Topologia.Exchange, tipo, mandatory: true, propriedades, corpo, ct).ConfigureAwait(false);
        }
        catch
        {
            await DescartarConexaoAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DescartarConexaoAsync().ConfigureAwait(false);
        _lock.Dispose();
    }

    private async Task<IChannel> ObterCanalAsync(CancellationToken ct)
    {
        if (_canal is { IsOpen: true })
        {
            return _canal;
        }

        await DescartarConexaoAsync().ConfigureAwait(false);

        var fabrica = new ConnectionFactory
        {
            Uri = new Uri(_options.Uri),
            ClientProvidedName = _options.NomeCliente,
            AutomaticRecoveryEnabled = false,
        };

        _conexao = await fabrica.CreateConnectionAsync(ct).ConfigureAwait(false);
        _canal = await _conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct).ConfigureAwait(false);

        await _canal.ExchangeDeclareAsync(Topologia.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct).ConfigureAwait(false);
        return _canal;
    }

    private async Task DescartarConexaoAsync()
    {
        if (_canal is not null)
        {
            try
            {
                await _canal.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Canal já quebrado: nada a fazer.
            }

            _canal = null;
        }

        if (_conexao is not null)
        {
            try
            {
                await _conexao.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Conexão já quebrada: nada a fazer.
            }

            _conexao = null;
        }
    }
}
