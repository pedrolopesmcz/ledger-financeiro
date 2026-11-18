-- Movimentos recebidos. A chave primária é o EventId: é ela que garante a
-- idempotência do consumidor (ADR 0003). Um evento repetido não entra de novo.
CREATE TABLE movimentos (
    event_id         uuid           PRIMARY KEY,
    lancamento_id    uuid           NOT NULL UNIQUE,
    conta_id         uuid           NOT NULL,
    valor_com_sinal  numeric(18, 2) NOT NULL,
    ocorrido_em      timestamptz    NOT NULL,
    dia              date           NOT NULL,
    processado_em    timestamptz    NOT NULL DEFAULT now()
);

-- Saldo em um instante: soma dos movimentos do dia até o instante.
CREATE INDEX ix_movimentos_conta_dia ON movimentos (conta_id, dia, ocorrido_em) INCLUDE (valor_com_sinal);

-- Consolidado diário: uma linha por conta e dia contábil.
CREATE TABLE saldos_diarios (
    conta_id       uuid           NOT NULL,
    dia            date           NOT NULL,
    creditos       numeric(18, 2) NOT NULL,
    debitos        numeric(18, 2) NOT NULL,
    saldo_final    numeric(18, 2) NOT NULL,
    quantidade     integer        NOT NULL,
    atualizado_em  timestamptz    NOT NULL,
    PRIMARY KEY (conta_id, dia)
);

-- Posição mais recente por conta: consulta de saldo atual em uma leitura por chave.
-- titular_id vem no evento: permite checar a titularidade sem chamar o serviço de Lançamentos.
CREATE TABLE posicoes (
    conta_id       uuid           PRIMARY KEY,
    titular_id     uuid           NOT NULL,
    saldo          numeric(18, 2) NOT NULL,
    posicao_em     timestamptz    NOT NULL,
    atualizada_em  timestamptz    NOT NULL
);
