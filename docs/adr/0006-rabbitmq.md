# ADR 0006: RabbitMQ com client oficial

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Precisamos de um broker durável, com reentrega, retry e fila de mensagens mortas, para um volume de dezenas de mensagens por segundo.

## Opções consideradas
1. **RabbitMQ**: filas duráveis (quorum queues), DLX nativo, leve para rodar localmente, protocolo AMQP.
2. **Kafka**: log durável com replay, excelente em volumes muito altos; mais pesado para operar e exagerado para o volume atual.
3. **Serviço gerenciado** (Azure Service Bus, SQS): ótimo em produção, mas impede rodar tudo localmente.

Sobre a biblioteca: MassTransit facilita, mas passou a ter licença comercial na versão 9. Usamos o client oficial `RabbitMQ.Client` com uma abstração própria fina, o que também deixa explícito como retry, confirmação e DLQ funcionam.

## Decisão
RabbitMQ com exchange `ledger.events` (topic), fila quorum `consolidado.lancamentos`, fila de retry com TTL e DLQ `consolidado.lancamentos.dlq`. Confirmação manual (ack) só após o commit no banco.

## Consequências
- (+) Simples de rodar e de explicar.
- (-) Replay histórico não é nativo. Se necessário no futuro, a outbox (que guardamos) permite republicar.
