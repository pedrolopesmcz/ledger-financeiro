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
}
