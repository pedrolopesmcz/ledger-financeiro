# ADR 0003: Idempotência na API e no consumidor

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Em sistemas distribuídos, repetição é normal: o cliente reenvia após um timeout, o broker reentrega uma mensagem, o relay republica após uma queda. Em dados financeiros, processar duas vezes significa cobrar duas vezes.

## Decisão
**Na API**: `POST /lancamentos` exige o cabeçalho `Idempotency-Key`. Há uma restrição única `(conta_id, idempotency_key)`. Se a chave já existe com o mesmo conteúdo, devolvemos o resultado original (200). Se existe com conteúdo diferente, devolvemos 409 (uso indevido da chave).

**No consumidor**: cada evento tem um `EventId` (UUID gerado no Lançamentos). A tabela de movimentos do Consolidado usa `EventId` como chave primária e o insert acontece na mesma transação da atualização do saldo. Duplicata gera violação de chave, a transação é desfeita e a mensagem é confirmada sem efeito.

## Consequências
- (+) Retentativas seguras em todas as camadas, o que permite políticas agressivas de retry.
- (-) O cliente precisa gerar e guardar a chave. É prática comum em APIs de pagamento.
