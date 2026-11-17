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
