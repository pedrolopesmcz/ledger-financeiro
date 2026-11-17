namespace Ledger.Messaging;

public sealed class RabbitMqOptions
{
    public const string Secao = "RabbitMq";

    public string Uri { get; set; } = "amqp://guest:guest@localhost:5672/";

    public string NomeCliente { get; set; } = "ledger";
}
