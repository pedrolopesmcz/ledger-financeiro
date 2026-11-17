# ADR 0010: Lançamentos imutáveis, correção por estorno

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Em contabilidade, registros não são apagados nem editados; erros são corrigidos com um lançamento contrário.

## Decisão
Não existem UPDATE nem DELETE de lançamentos. Correções são feitas por um novo lançamento de estorno que referencia o original. Um lançamento só pode ser estornado uma vez.

## Consequências
- (+) Trilha de auditoria completa; o saldo é sempre reconstruível a partir dos lançamentos.
- (+) Simplifica a projeção do Consolidado: só existem inserções.
