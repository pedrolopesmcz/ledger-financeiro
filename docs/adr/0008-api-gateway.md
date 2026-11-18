# ADR 0008: API Gateway com YARP e rate limiting

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
Os canais precisam de um ponto único de entrada com autenticação, controle de taxa e roteamento. Em produção o banco usaria uma plataforma de gestão de APIs (ex.: Apigee).

## Decisão
Gateway em .NET com YARP: valida o JWT, aplica rate limiting por cliente (token bucket) e roteia para os serviços. Requisições acima do limite recebem 429 em vez de sobrecarregar os serviços.

## Consequências
- (+) Protege os serviços de picos e abuso; centraliza políticas transversais.
- (+) Papel equivalente ao Apigee, executável localmente.
- (-) Mais um salto de rede. Os serviços revalidam o token (defesa em profundidade), então o gateway não é ponto único de segurança.

## Detalhes da implementação
- **Partição do limite:** por usuário (`sub`); sem token, por IP. Um cliente abusivo recebe 429 com `Retry-After` sem afetar os demais (provado em teste).
- **Token bucket:** 100 req/s sustentadas e rajadas de até 200 por cliente (configurável em `Limites`).
- **Ordem do pipeline:** autenticação, autorização e só então rate limiting, para que o limite seja por usuário autenticado. Requisições sem token são barradas com 401 antes de chegar aos serviços.
- **Roteamento:** `/contas/{id}/saldo` e `/contas/{id}/consolidado/*` vão para o Consolidado; o restante de `/contas` vai para Lançamentos.
