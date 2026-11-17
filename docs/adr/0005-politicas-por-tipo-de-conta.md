# ADR 0005: Regras separadas por tipo de conta

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
O banco opera cartão de crédito como produto principal e conta/carteira digital como produto em expansão. As operações permitidas e a regra de autorização diferem entre os dois.

## Opções consideradas
1. **Modelo único**: uma regra só (`saldo - valor >= -limite`), com o limite zero para carteira e o limite de crédito para cartão. Menos código, mas esconde diferenças reais do negócio (operações válidas, semântica do saldo).
2. **Separar por tipo de conta**: núcleo comum de razão e uma política por produto.

A opção 1 foi a recomendação inicial. A decisão final foi pela opção 2, por representar melhor o negócio do cliente e facilitar a evolução de cada produto de forma independente.

## Decisão
Analisando o contexto do cliente, entendi que o banco atende duas necessidades distintas: o cartão de crédito, seu produto principal, e a conta digital, em expansão. Os dois produtos registram créditos e débitos, mas não aceitam as mesmas operações nem autorizam um débito pelo mesmo critério. Tratar os dois com uma regra única esconderia essas diferenças e tornaria cada evolução de produto um risco para o outro.

Por isso mantive um núcleo único de razão (lançamento, saldo, versão e outbox), que não conhece produtos, e separei as regras em uma política por tipo de conta, usando o padrão **Strategy**. A interface `IPoliticaDeConta` define quais operações o tipo de conta aceita e se um débito pode ser autorizado:
- `PoliticaContaDigital`: depósito, saque, Pix enviado e recebido; débito autorizado se `saldo + chequeEspecial >= valor`.
- `PoliticaCartaoCredito`: compra, pagamento de fatura e estorno; compra autorizada se `limite - utilizado >= valor`.

Um novo produto passa a ser uma nova política, sem alterar o núcleo (princípio aberto/fechado).

## Consequências
- (+) Regras de cada produto testadas isoladamente.
- (+) Extensível para novos produtos.
- (-) Ciclo de fatura, parcelamento e juros ficam fora do escopo desta entrega.
