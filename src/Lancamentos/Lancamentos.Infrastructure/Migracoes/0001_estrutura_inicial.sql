-- Contas: guarda o saldo oficial de autorização (ADR 0004).
-- A concorrência otimista usa a coluna de sistema xmin do PostgreSQL,
-- que muda a cada UPDATE da linha.
CREATE TABLE contas (
    id          uuid           PRIMARY KEY,
    titular_id  uuid           NOT NULL,
    tipo        smallint       NOT NULL,
    saldo       numeric(18, 2) NOT NULL DEFAULT 0,
    limite      numeric(18, 2) NOT NULL CHECK (limite >= 0),
    aberta_em   timestamptz    NOT NULL
);

-- Contas de um cliente ("minhas contas").
CREATE INDEX ix_contas_titular ON contas (titular_id);

-- Lançamentos: imutáveis (ADR 0010). Só INSERT.
CREATE TABLE lancamentos (
    id                      uuid           PRIMARY KEY,
    conta_id                uuid           NOT NULL REFERENCES contas (id),
    operacao                smallint       NOT NULL,
    natureza                smallint       NOT NULL,
    valor                   numeric(18, 2) NOT NULL CHECK (valor > 0),
    saldo_apos              numeric(18, 2) NOT NULL,
    idempotency_key         varchar(100)   NOT NULL,
    descricao               varchar(140),
    lancamento_original_id  uuid           REFERENCES lancamentos (id),
    ocorrido_em             timestamptz    NOT NULL
);

-- Idempotência (ADR 0003): a mesma chave não gera dois lançamentos na mesma conta,
-- nem mesmo com duas requisições simultâneas.
CREATE UNIQUE INDEX ux_lancamentos_idempotencia ON lancamentos (conta_id, idempotency_key);

-- Um lançamento só pode ser estornado uma vez.
CREATE UNIQUE INDEX ux_lancamentos_estorno ON lancamentos (lancamento_original_id)
    WHERE lancamento_original_id IS NOT NULL;

-- Extrato por período.
CREATE INDEX ix_lancamentos_conta_data ON lancamentos (conta_id, ocorrido_em);

-- Outbox (ADR 0002): gravada na mesma transação do lançamento.
CREATE TABLE outbox (
    id            uuid         PRIMARY KEY,
    tipo          varchar(100) NOT NULL,
    payload       jsonb        NOT NULL,
    criada_em     timestamptz  NOT NULL,
    publicada_em  timestamptz,
    tentativas    integer      NOT NULL DEFAULT 0,
    ultimo_erro   varchar(500),
    trace_parent  varchar(100)
);

-- Índice parcial: só as pendentes. Fica pequeno mesmo com milhões de eventos publicados.
CREATE INDEX ix_outbox_pendentes ON outbox (criada_em) WHERE publicada_em IS NULL;
