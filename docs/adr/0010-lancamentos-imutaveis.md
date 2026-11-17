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

## Regras de negócio confirmadas
- **Estorno não passa pela validação de limite.** É correção contábil, não nova operação do cliente: um depósito feito por engano é revertido mesmo que deixe a conta negativa.
- **Um lançamento só pode ser estornado uma vez**, e um estorno não pode ser estornado. Garantido no domínio e por índice único no banco (`lancamento_original_id`).
- **Pagamento de fatura a maior** gera saldo credor no cartão, que amplia o limite disponível.
- **Redução de limite abaixo do utilizado** é permitida: apenas bloqueia novos débitos.
