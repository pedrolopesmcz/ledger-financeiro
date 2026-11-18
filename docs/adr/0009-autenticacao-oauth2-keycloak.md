# ADR 0009: OAuth 2.0 / JWT com Keycloak e escopos

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Os endpoints movimentam dinheiro e expõem saldos. É preciso identificar quem chama e limitar o que cada chamador pode fazer.

## Decisão
Keycloak como provedor de identidade (OAuth 2.0 / OIDC), com tokens JWT de vida curta (5 minutos). Fluxo *client credentials* para os canais internos. Escopos:
- `lancamentos:write`: registrar movimentações;
- `saldo:read`: consultar saldo e consolidado;
- `contas:admin`: abrir contas e alterar limites.

Validação de assinatura (JWKS), emissor, audiência e expiração no gateway e em cada serviço.

## Consequências
- (+) Padrão de mercado, sem segredos compartilhados entre serviços.
- (+) Tokens curtos limitam o estrago de um vazamento.
- (-) Mais um container. Nos testes automatizados usamos uma chave de assinatura de teste para não depender do Keycloak.

## Usuário final e titularidade (decisão do checkpoint 5)

**Contexto.** Além de sistemas internos, o próprio cliente acessa a API pelo aplicativo. Próximas operações (transferências, limites, cartões adicionais) vão exigir saber quem é o usuário.

**Opções para "esta conta é do usuário?"**
1. **Lista de contas dentro do token** (claim `contas`): simples, mas o token carrega uma verdade que envelhece (conta aberta ou encerrada só aparece no próximo login), incha a cada conta nova e coloca no provedor de identidade uma informação que pertence ao banco.
2. **O token identifica, o domínio autoriza**: o token traz só `sub` e escopos; cada conta guarda `titular_id`; cada serviço confere `sub == titular`.

**Decisão: opção 2.**
- Lançamentos guarda `titular_id` na tabela de contas. O Consolidado recebe o `TitularId` no evento e guarda na tabela de posições, então verifica sem chamar o outro serviço.
- Conta de outra pessoa responde **404, igual a uma inexistente**: o 403 revelaria que a conta existe (enumeração).
- Sistemas internos usam o escopo `contas:todas`, que dispensa a checagem.
- **Operações iniciadas pelo cliente** são limitadas a Pix enviado, saque e pagamento de fatura. Créditos e compras chegam por sistemas do banco (SPI, rede de cartões) com token de serviço: o cliente não consegue "se dar" um depósito.

**Fluxos OAuth.** `backoffice`: client credentials. `app-canais`: cliente público com Authorization Code + PKCE. O *password grant* está habilitado **apenas para a demonstração local e o E2E**; em produção ele fica desligado (é descontinuado no OAuth 2.1). Para aplicações web, a evolução é o padrão BFF, com tokens fora do navegador.

**Emissor e rede interna.** O token é emitido com `iss = http://localhost:8080/realms/ledger` (endereço que o cliente enxerga), mas os serviços buscam as chaves em `http://keycloak:8080` (rede do docker). Por isso emissor e endereço de metadados são configurados separadamente, e o Keycloak usa `KC_HOSTNAME_BACKCHANNEL_DYNAMIC`.

**Defesa em profundidade.** O gateway valida o token e aplica rate limiting; cada serviço valida o token de novo e faz a autorização (escopo e titularidade). Um serviço exposto por engano continua protegido.
