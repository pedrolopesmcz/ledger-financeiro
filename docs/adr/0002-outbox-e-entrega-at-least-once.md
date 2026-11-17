# ADR 0002: Outbox Pattern e entrega pelo menos uma vez

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Gravar no banco e publicar no broker são duas operações em sistemas diferentes. Se o serviço grava o lançamento e cai antes de publicar, o consolidado nunca fica sabendo (perda). Se publica antes de gravar e a gravação falha, o consolidado conta um lançamento que não existe (fantasma).

## Opções consideradas
1. **Publicar direto após o commit**: simples, mas perde eventos em caso de queda entre as duas operações.
2. **Transação distribuída (2PC)**: garante atomicidade, mas acopla a disponibilidade de Lançamentos à do broker, que é exatamente o que queremos evitar.
3. **Outbox**: gravar o evento numa tabela na mesma transação do lançamento; um processo separado publica depois.
4. **CDC (Debezium lendo o log do banco)**: robusto, mas exige infraestrutura extra (Kafka Connect).

## Decisão
Outbox com relay por polling usando `SELECT ... FOR UPDATE SKIP LOCKED`, o que permite várias instâncias do relay sem publicar o mesmo evento em paralelo. Publicação com *publisher confirms* do RabbitMQ: só marcamos como publicado depois da confirmação do broker.

## Consequências
- (+) Nenhum lançamento confirmado deixa de gerar evento.
- (+) Lançamentos segue funcionando com o broker fora: os eventos esperam na outbox.
- (-) Entrega **pelo menos uma vez**: se o relay cair após publicar e antes de marcar, o evento é publicado de novo. Por isso o consumidor precisa ser idempotente ([ADR 0003](0003-idempotencia-ponta-a-ponta.md)).
- (-) Latência extra igual ao intervalo de polling. Evolução: CDC.
