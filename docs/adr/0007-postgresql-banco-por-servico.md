# ADR 0007: PostgreSQL, um banco por serviço

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Dados financeiros exigem transações ACID, restrições de unicidade e controle de concorrência confiável.

## Decisão
PostgreSQL com EF Core (Npgsql). Cada serviço tem seu próprio banco (`lancamentos` e `consolidado`); nenhum serviço lê o banco do outro. Localmente os dois bancos rodam na mesma instância por economia; em produção seriam instâncias separadas.

Valores monetários em `numeric(18,2)` e `decimal` no C#, nunca ponto flutuante.

## Consequências
- (+) Transações, `xmin` para concorrência otimista, `SKIP LOCKED` para a outbox.
- (-) Escrita vertical. Evolução: particionamento por data e réplicas de leitura.
