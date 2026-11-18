# ADR 0013: Projeção do Consolidado

- **Status**: Aceita
- **Data**: 2026-11-18

## Contexto
O Consolidado recebe eventos de forma assíncrona, possivelmente duplicados, fora de ordem e com atraso (quando ficou fora do ar). Precisa responder três perguntas: saldo atual, consolidado de um dia e saldo em um instante qualquer.

## Decisões

**1. Três tabelas, cada uma servindo uma consulta.**
- `movimentos`: um registro por evento, com `event_id` como chave primária (idempotência). Responde o saldo em um instante.
- `saldos_diarios`: uma linha por conta e dia, com créditos, débitos e saldo final. Responde o consolidado diário.
- `posicoes`: uma linha por conta. Responde o saldo atual com uma leitura por chave.

Tudo é atualizado na mesma transação: ou o evento é aplicado nas três, ou em nenhuma.

**2. Saldo em um instante = saldo final do dia anterior + movimentos do dia até o instante.** A consulta nunca soma o histórico inteiro, só um dia.

**3. Dia contábil no fuso do banco (America/Sao_Paulo), configurável.** Uma compra às 22h em Brasília pertence àquele dia, mesmo já sendo o dia seguinte em UTC.

**4. Processamento paralelo com serialização por conta.** Somar é comutativo, então a ordem de chegada não muda os totais e vários consumidores podem rodar em paralelo. A exceção é um evento atrasado, que precisa "rolar" o saldo para os dias seguintes. Para isso, cada transação pega um *advisory lock* da conta: eventos da mesma conta são aplicados um de cada vez, contas diferentes seguem em paralelo. Alternativa descartada: partições ordenadas por conta (como no Kafka), que exigiria outro broker.

**5. Retry com atraso por TTL de mensagem.** Falha transitória: a mensagem é republicada na fila de retry com `expiration` configurável; ao expirar, volta para a fila principal. Depois de N tentativas, ou se a mensagem for inválida, vai para a DLQ. A confirmação (ack) da original só acontece depois de republicar, então nada se perde no meio do caminho.

**6. Cache com circuit breaker.** Se o Redis falhar, o circuito abre por um intervalo e as leituras vão direto ao banco, sem pagar o timeout do Redis a cada requisição.

## Consequências
- (+) Duplicatas, atrasos e desordem não corrompem o saldo (provado em teste de integração).
- (+) Consolidado fora do ar só atrasa a consolidação; ao voltar, converge.
- (-) Um evento muito atrasado atualiza todos os dias seguintes da conta. Aceitável: é raro e o volume por conta é pequeno.
- (-) `posicao_em` indica o último evento aplicado, não garante que todos os anteriores já chegaram. Para isso existe a conciliação periódica (evolução prevista).
