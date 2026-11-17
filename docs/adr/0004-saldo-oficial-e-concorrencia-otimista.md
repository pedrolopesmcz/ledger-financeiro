# ADR 0004: Saldo oficial em Lançamentos com concorrência otimista

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Para aceitar ou recusar um débito é preciso conhecer o saldo (ou o limite disponível) no momento da operação. Se Lançamentos consultasse o Consolidado, voltaria a depender dele, o que viola o requisito de disponibilidade. Além disso, duas compras simultâneas podem, juntas, ultrapassar o limite.

## Opções consideradas
1. **Consultar o Consolidado**: acoplamento síncrono e saldo eventualmente desatualizado. Descartada.
2. **Bloqueio pessimista** (`SELECT ... FOR UPDATE`): correto, mas segura a linha durante a transação e reduz throughput em contas muito movimentadas.
3. **Concorrência otimista**: cada conta tem uma versão; o UPDATE só acontece se a versão não mudou. Em conflito, refazemos a operação.

## Decisão
Lançamentos mantém o **saldo oficial de autorização** na própria tabela de contas, atualizado na mesma transação do lançamento, com concorrência otimista (coluna de versão `xmin` do PostgreSQL). Em conflito, até 3 novas tentativas com espera curta; depois, 409.

## Consequências
- (+) Decisão de autorização sem depender de outro serviço.
- (+) Sem bloqueios longos na maior parte dos casos.
- (-) Contas extremamente concorridas geram retentativas. Evolução: fila por conta ou bloqueio pessimista seletivo.
- (-) Dois "saldos" no sistema: o de autorização (forte) e o consolidado (eventual). Isso é intencional e documentado; uma conciliação periódica entre os dois é evolução prevista.
