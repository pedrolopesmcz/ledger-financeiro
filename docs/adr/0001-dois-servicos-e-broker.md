# ADR 0001: Dois serviços independentes comunicando por broker

- **Status**: Aceita
- **Data**: 2026-11-17

## Contexto
O requisito central é que o controle de lançamentos continue disponível mesmo com o consolidado indisponível. Os dois lados também têm perfis de carga diferentes: escrita transacional de um lado, leitura intensa (50 req/s no pico) do outro.

## Opções consideradas
1. **Monólito modular**: um deploy, módulos isolados. Simples de operar, mas se o processo cai, os dois lados caem juntos; e não dá para escalar a leitura sem escalar a escrita.
2. **Dois serviços + broker**: Lançamentos e Consolidado em processos e bancos separados, ligados por mensageria.
3. **Microsserviços finos** (contas, autenticação, relatórios separados): complexidade operacional sem ganho para o escopo atual.

## Decisão
Opção 2. Cada serviço tem seu banco e só se comunica com o outro por eventos assíncronos. Dentro de cada serviço, camadas de Domínio, Aplicação, Infraestrutura e API.

## Consequências
- (+) Falha isolada: a queda do Consolidado não afeta Lançamentos.
- (+) Escala independente de leitura e escrita.
- (-) Consistência eventual entre o saldo de autorização e o consolidado.
- (-) Mais peças para operar (broker, dois bancos). Mitigado com docker compose e observabilidade.
