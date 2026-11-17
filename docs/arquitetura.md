# Documento de Arquitetura

> Status: rascunho para validação (checkpoint 1)

## 1. O problema

Um banco precisa:

1. **Registrar movimentações financeiras** (créditos e débitos) nas contas dos clientes;
2. **Consultar a posição consolidada** (saldo) de um cliente em um determinado momento;
3. **Continuar operando** com instabilidade, picos de demanda ou falhas parciais, sem gerar inconsistência.

O sistema atual cresceu sem planejamento, é lento em horário de pico e difícil de manter. A proposta é repensar a solução do zero.

### 1.1 Reconciliação entre o enunciado e as orientações recebidas

O enunciado (PDF, versão set/26) fala em "movimentações" e "posição consolidada em um determinado momento". As orientações recebidas por e-mail usam outro vocabulário: "serviço de controle de lançamentos" e "consolidado diário". Tratamos os dois como o mesmo problema:

| Enunciado | Orientações | Nesta solução |
|---|---|---|
| Registrar movimentações | Controle de lançamentos | Serviço **Lançamentos** (lado de escrita) |
| Posição consolidada em um momento | Consolidado diário | Serviço **Consolidado** (lado de leitura) |

O Consolidado guarda uma "fotografia" por conta e por dia. O saldo em um instante qualquer é calculado como:

```
saldo(conta, instante) = saldo no fim do dia anterior + movimentos do dia até o instante
```

Assim atendemos ao "consolidado diário" e ao "saldo em um determinado momento" com o mesmo modelo.

### 1.2 Contexto do negócio

O cliente é um banco ligado a uma rede varejista, cujo principal produto é cartão de crédito, com produtos de conta/carteira digital em expansão. A solução trata os dois tipos de conta:

| | Conta digital | Cartão de crédito |
|---|---|---|
| Créditos | depósito, Pix recebido | pagamento de fatura, estorno de compra |
| Débitos | saque, Pix enviado, pagamento | compra |
| Limite | cheque especial (pode ser zero) | limite de crédito |

O núcleo (registro, saldo, concorrência, publicação de eventos) é único. As regras de cada produto ficam em **políticas por tipo de conta** (ver [ADR 0005](adr/0005-politicas-por-tipo-de-conta.md)).

## 2. Requisitos não funcionais e metas

| Requisito | Meta | Como verificamos |
|---|---|---|
| Disponibilidade de Lançamentos independente do Consolidado | Lançamentos aceita movimentações com o Consolidado fora do ar | Teste E2E de falha |
| Throughput do Consolidado | 50 req/s sustentados | Teste de carga k6 |
| Perda de requisições no pico | no máximo 5% (meta interna: abaixo de 1%) | Threshold do k6 |
| Latência de consulta | p95 abaixo de 200 ms | Threshold do k6 |
| Durabilidade | Nenhum lançamento confirmado (HTTP 201) é perdido | Outbox na mesma transação; testes de integração |
| Idempotência | Reenvio de um mesmo lançamento não duplica valores | Testes de API e de consumidor |
| Defasagem do consolidado | p95 abaixo de 2 s em operação normal | Métrica de lag e teste E2E |
| Consistência do saldo de autorização | Duas operações simultâneas não ultrapassam o limite | Teste de concorrência |

> "Perda", aqui, é qualquer requisição que não recebe resposta de sucesso (erro 5xx, timeout ou rejeição por sobrecarga).

## 3. Visão de containers

```mermaid
flowchart LR
    canal[Canais do banco<br/>app, POS, backoffice]
    idp[Keycloak<br/>OAuth 2.0 / OIDC]
    gw[API Gateway<br/>YARP + rate limiting]

    subgraph LAN[Lançamentos]
        lapi[Lançamentos API]
        relay[Outbox Relay<br/>BackgroundService]
        ldb[(PostgreSQL<br/>lancamentos)]
    end

    mq{{RabbitMQ<br/>ledger.events}}

    subgraph CON[Consolidado]
        worker[Consolidado Worker]
        capi[Consolidado API]
        cdb[(PostgreSQL<br/>consolidado)]
        cache[(Redis)]
    end

    otel[Observabilidade<br/>OpenTelemetry + Aspire Dashboard]

    canal -->|token| idp
    canal -->|HTTPS + JWT| gw
    gw --> lapi
    gw --> capi
    lapi -->|transação única:<br/>lançamento + saldo + outbox| ldb
    relay -->|lê pendentes| ldb
    relay -->|publica com confirmação| mq
    mq -->|at-least-once| worker
    worker --> cdb
    worker -->|invalida| cache
    capi --> cache
    capi --> cdb
    lapi -.-> otel
    capi -.-> otel
    worker -.-> otel
    gw -.-> otel
```

| Container | Responsabilidade | Escala |
|---|---|---|
| API Gateway | Ponto único de entrada: valida token, limita taxa, roteia | Horizontal, sem estado |
| Lançamentos API | Recebe movimentações, aplica regras, grava lançamento, saldo e evento | Horizontal, sem estado |
| Outbox Relay | Publica eventos pendentes no broker | Horizontal (com `SKIP LOCKED`) |
| Consolidado Worker | Consome eventos e atualiza saldos diários | Horizontal (projeção comutativa) |
| Consolidado API | Responde consultas de saldo | Horizontal, sem estado, com cache |

## 4. Fluxos principais

### 4.1 Registrar um lançamento

```mermaid
sequenceDiagram
    autonumber
    participant C as Canal
    participant G as Gateway
    participant L as Lançamentos API
    participant DB as Postgres (lancamentos)
    C->>G: POST /lancamentos (JWT, Idempotency-Key)
    G->>G: valida token, escopo e rate limit
    G->>L: encaminha
    L->>DB: Idempotency-Key já existe?
    alt já processado
        L-->>C: 200 com o resultado original
    else novo
        L->>L: política do tipo de conta valida a operação
        L->>DB: BEGIN
        L->>DB: INSERT lançamento
        L->>DB: UPDATE conta (saldo, versão)
        L->>DB: INSERT outbox (LancamentoRegistrado)
        L->>DB: COMMIT
        L-->>C: 201 Created
    end
```

Se a versão da conta mudou entre a leitura e a gravação (outra operação concorrente), o banco rejeita o UPDATE, a transação é desfeita e a operação é refeita com o saldo atualizado. Ver [ADR 0004](adr/0004-saldo-oficial-e-concorrencia-otimista.md).

### 4.2 Propagar para o Consolidado

```mermaid
sequenceDiagram
    autonumber
    participant R as Outbox Relay
    participant DB as Postgres (lancamentos)
    participant MQ as RabbitMQ
    participant W as Consolidado Worker
    participant CDB as Postgres (consolidado)
    participant K as Redis
    loop a cada intervalo
        R->>DB: SELECT pendentes FOR UPDATE SKIP LOCKED
        R->>MQ: publica (publisher confirm)
        R->>DB: marca como publicado
    end
    MQ->>W: entrega LancamentoRegistrado
    W->>CDB: BEGIN
    W->>CDB: INSERT movimento (PK = EventId)
    alt EventId já existe
        W->>CDB: ROLLBACK
        W->>MQ: ACK (duplicata ignorada)
    else novo
        W->>CDB: UPSERT saldo do dia e dias seguintes
        W->>CDB: COMMIT
        W->>K: invalida chaves da conta
        W->>MQ: ACK
    end
```

Falhas transitórias geram nova tentativa com espera crescente. Depois de N tentativas a mensagem vai para a fila de mensagens mortas (DLQ) para análise e reprocessamento manual.

### 4.3 Consultar saldo

```mermaid
sequenceDiagram
    autonumber
    participant C as Canal
    participant A as Consolidado API
    participant K as Redis
    participant DB as Postgres (consolidado)
    C->>A: GET /contas/{id}/saldo?em=...
    A->>K: busca no cache
    alt hit
        K-->>A: saldo
    else miss ou Redis indisponível
        A->>DB: consulta indexada
        A->>K: grava no cache (se disponível)
    end
    A-->>C: saldo + "posicaoEm" (até quando está atualizado)
```

A resposta informa até qual momento o saldo está consolidado. Isso deixa explícita a consistência eventual para quem consome.

### 4.4 Consolidado fora do ar

```mermaid
sequenceDiagram
    participant C as Canal
    participant L as Lançamentos
    participant MQ as RabbitMQ
    participant W as Consolidado (fora)
    C->>L: POST /lancamentos
    L-->>C: 201 (continua funcionando)
    L->>MQ: eventos acumulam na fila durável
    Note over W: serviço volta
    MQ->>W: entrega o acumulado
    W->>W: processa em lote, de forma idempotente
    Note over W: saldo converge
```

## 5. Modos de falha

| Falha | Efeito | Mitigação |
|---|---|---|
| Consolidado API ou Worker fora | Consultas falham; lançamentos seguem | Fila durável; Worker reprocessa ao voltar |
| RabbitMQ fora | Eventos ficam na outbox; lançamentos seguem | Relay tenta de novo; nada se perde |
| Postgres de Lançamentos fora | Novos lançamentos são recusados (503) | Não aceitamos o que não conseguimos gravar; réplica e failover em produção |
| Redis fora | Consultas vão direto ao banco | Cache é otimização, nunca dependência |
| Mensagem duplicada | Nenhum efeito | Chave única por EventId |
| Mensagem que sempre falha | Isolada | DLQ após N tentativas, sem travar a fila |
| Cliente reenvia após timeout | Nenhum efeito | Idempotency-Key |
| Duas compras simultâneas no limite | Uma é recusada | Concorrência otimista na conta |
| Pico acima da capacidade | Excesso recebe 429 | Rate limiting no gateway; escala horizontal |

## 6. Segurança

- **Autenticação**: OAuth 2.0 com tokens JWT de vida curta emitidos pelo Keycloak.
- **Autorização**: escopos por capacidade (`lancamentos:write`, `saldo:read`, `contas:admin`) validados no gateway e de novo em cada serviço (defesa em profundidade).
- **Entrada**: validação de todos os campos; valores monetários em `decimal` com duas casas; limites de tamanho.
- **Dados sensíveis**: o razão guarda apenas identificadores de conta, sem dados pessoais. Logs não registram corpo de requisição nem token. Em produção: TLS em todo tráfego, criptografia em repouso e segredos em cofre (ex.: Azure Key Vault).
- **Proteção contra abuso**: rate limiting por cliente no gateway.

## 7. Escalabilidade

- Todos os serviços HTTP são sem estado e escalam horizontalmente.
- O Worker escala horizontalmente porque a projeção é **comutativa**: somar créditos e débitos dá o mesmo resultado em qualquer ordem. Não precisamos garantir ordem de entrega por conta.
- A consulta de saldo é uma leitura por chave indexada `(conta_id, dia)` e passa pelo cache.

## 8. Decisões arquiteturais (ADRs)

| # | Decisão |
|---|---|
| [0001](adr/0001-dois-servicos-e-broker.md) | Dois serviços independentes comunicando por broker |
| [0002](adr/0002-outbox-e-entrega-at-least-once.md) | Outbox Pattern e entrega pelo menos uma vez |
| [0003](adr/0003-idempotencia-ponta-a-ponta.md) | Idempotência na API e no consumidor |
| [0004](adr/0004-saldo-oficial-e-concorrencia-otimista.md) | Saldo oficial em Lançamentos com concorrência otimista |
| [0005](adr/0005-politicas-por-tipo-de-conta.md) | Regras separadas por tipo de conta |
| [0006](adr/0006-rabbitmq.md) | RabbitMQ com client oficial |
| [0007](adr/0007-postgresql-banco-por-servico.md) | PostgreSQL, um banco por serviço |
| [0008](adr/0008-api-gateway.md) | API Gateway com YARP e rate limiting |
| [0009](adr/0009-autenticacao-oauth2-keycloak.md) | OAuth 2.0 / JWT com Keycloak e escopos |
| [0010](adr/0010-lancamentos-imutaveis.md) | Lançamentos imutáveis, correção por estorno |
| [0011](adr/0011-cache-redis.md) | Cache Redis no Consolidado |
| [0012](adr/0012-observabilidade.md) | Observabilidade com OpenTelemetry |

## 9. Fora do escopo e evoluções

- Ciclo de fatura, parcelamento e juros do cartão.
- Transferência entre contas como operação atômica (hoje seriam dois lançamentos coordenados; evolução: saga).
- Fechamento contábil diário com conciliação automática entre o saldo de Lançamentos e o do Consolidado.
- Particionamento da tabela de lançamentos por data quando o volume crescer.
- Kubernetes com autoscaling por métrica de fila (KEDA) para o Worker.
