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
