# ADR 0011: Cache Redis no Consolidado

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
O Consolidado precisa sustentar picos de 50 req/s com no máximo 5% de perda.

## Decisão
Cache-aside em Redis para consultas de saldo. O Worker invalida as chaves da conta após cada atualização. TTL curto para o dia corrente e mais longo para dias já encerrados. Se o Redis estiver indisponível, a API consulta o banco diretamente: o cache nunca é dependência.

## Consequências
- (+) Menos carga no banco e latência menor.
- (-) Risco de leitura desatualizada entre a atualização e a invalidação, limitado pelo TTL. Aceitável porque o consolidado já é eventualmente consistente e a resposta informa `posicaoEm`.
