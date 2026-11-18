using RabbitMQ.Client;

namespace Ledger.Messaging;

/// <summary>
/// Nomes da topologia no RabbitMQ. A mesma topologia é provisionada como
/// infraestrutura (deploy/rabbitmq/definitions.json), para que as filas
/// existam antes de qualquer serviço subir: se o Consolidado nunca tiver
/// rodado, os eventos ainda assim ficam guardados na fila.
/// </summary>
public static class Topologia
{
    public const string Exchange = "ledger.events";

    public const string FilaConsolidado = "consolidado.lancamentos";

    public const string FilaConsolidadoRetry = "consolidado.lancamentos.retry";

    public const string FilaConsolidadoDlq = "consolidado.lancamentos.dlq";

    public const string ExchangeRetry = "ledger.events.retry";

    public const string ExchangeDlq = "ledger.events.dlq";

    /// <summary>
    /// Declara a topologia do Consolidado. Declarações são idempotentes: se já
    /// existir (provisionada pelo definitions.json), nada muda. Os argumentos
    /// precisam ser idênticos aos do arquivo, senão o broker recusa.
    /// </summary>
    public static async Task DeclararConsolidadoAsync(IChannel canal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(canal);

        await canal.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct).ConfigureAwait(false);
        await canal.ExchangeDeclareAsync(ExchangeRetry, ExchangeType.Direct, durable: true, cancellationToken: ct).ConfigureAwait(false);
        await canal.ExchangeDeclareAsync(ExchangeDlq, ExchangeType.Direct, durable: true, cancellationToken: ct).ConfigureAwait(false);

        await canal.QueueDeclareAsync(FilaConsolidado, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-dead-letter-exchange"] = ExchangeDlq,
            ["x-dead-letter-routing-key"] = FilaConsolidadoDlq,
        }, cancellationToken: ct).ConfigureAwait(false);

        await canal.QueueDeclareAsync(FilaConsolidadoRetry, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
            ["x-dead-letter-exchange"] = string.Empty,
            ["x-dead-letter-routing-key"] = FilaConsolidado,
        }, cancellationToken: ct).ConfigureAwait(false);

        await canal.QueueDeclareAsync(FilaConsolidadoDlq, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?>
        {
            ["x-queue-type"] = "quorum",
        }, cancellationToken: ct).ConfigureAwait(false);

        await canal.QueueBindAsync(FilaConsolidado, Exchange, "lancamento.registrado.*", cancellationToken: ct).ConfigureAwait(false);
        await canal.QueueBindAsync(FilaConsolidadoRetry, ExchangeRetry, FilaConsolidadoRetry, cancellationToken: ct).ConfigureAwait(false);
        await canal.QueueBindAsync(FilaConsolidadoDlq, ExchangeDlq, FilaConsolidadoDlq, cancellationToken: ct).ConfigureAwait(false);
    }
}
