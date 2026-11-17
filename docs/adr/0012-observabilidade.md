# ADR 0012: Observabilidade com OpenTelemetry

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Com processamento assíncrono, um lançamento atravessa gateway, API, outbox, broker e worker. Sem rastreamento, é difícil saber onde algo atrasou ou falhou.

## Decisão
OpenTelemetry para traces, métricas e logs, com o contexto de trace propagado nos cabeçalhos das mensagens do RabbitMQ. Visualização local com o Aspire Dashboard. Health checks de liveness e readiness em todos os serviços. Métricas de negócio: lançamentos por segundo, tamanho da outbox pendente, lag de consolidação, mensagens na DLQ.

## Consequências
- (+) Um único trace mostra o caminho de ponta a ponta de um lançamento.
- (+) Alertas possíveis sobre outbox crescendo ou DLQ com mensagens.
