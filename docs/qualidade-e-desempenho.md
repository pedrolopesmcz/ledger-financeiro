# Qualidade e desempenho

Resultados medidos na CI (GitHub Actions). Execução de referência: [run 44341012575](https://github.com/pedrolopesmcz/ledger-financeiro/actions/runs/44341012575). Os relatórios completos (k6 e Stryker) ficam como artefatos de cada execução.

## 1. Pirâmide de testes

| Camada | Projeto | Testes | O que prova |
|---|---|---|---|
| Unitário | `Lancamentos.UnitTests` | 51 | Regras de negócio: políticas por tipo de conta, limites, estorno, valores monetários |
| Unitário | `Consolidado.UnitTests` | 24 | Cache-aside, circuit breaker, invalidação, dia contábil, política de retentativa |
| Integração | `Lancamentos.IntegrationTests` | 23 | API + PostgreSQL + RabbitMQ reais (Testcontainers): concorrência, idempotência, outbox, broker fora, segurança |
| Integração | `Consolidado.IntegrationTests` | 16 | Worker + API + PostgreSQL + RabbitMQ + Redis reais: duplicatas, eventos atrasados, retry, DLQ, Redis fora, Consolidado fora |
| Integração | `Gateway.IntegrationTests` | 7 | Roteamento, autenticação e rate limiting com serviços falsos em portas reais |
| E2E | `scripts/e2e.sh` | 1 roteiro | Sistema completo via docker compose, tokens reais do Keycloak, Consolidado derrubado no meio |
| Carga | `tests/Carga/consolidado.js` | 3 cenários | Requisito de 50 req/s com no máximo 5% de perda |
| Mutação | Stryker.NET | 2 projetos | Se os testes unitários realmente detectam defeitos |

**Total: 121 testes automatizados**, mais o roteiro E2E, a carga e a mutação, todos executados a cada push.

## 2. Cenários de falha cobertos por teste

| Cenário | Onde | Resultado esperado e verificado |
|---|---|---|
| Consolidado fora do ar | Integração e E2E | Lançamentos responde 201; eventos esperam na fila; saldo converge quando o Consolidado volta |
| RabbitMQ fora do ar | Integração | Lançamento aceito; evento fica pendente na outbox; publicado quando o broker volta |
| Redis fora do ar | Integração | Consultas respondem pelo banco (circuit breaker) |
| Falha transitória no banco do Consolidado | Integração | Mensagem vai para retry com atraso e é processada uma única vez |
| Mensagem inválida | Integração | Vai para a DLQ sem travar a fila |
| Evento duplicado | Integração | Ignorado; o saldo não soma duas vezes |
| Evento atrasado (de um dia anterior) | Integração | Saldos dos dias seguintes recalculados |
| Reenvio do cliente (timeout) | Integração e E2E | Mesma chave devolve o lançamento original (200), sem duplicar |
| 10 reenvios simultâneos com a mesma chave | Integração | Um único lançamento |
| 30 compras simultâneas disputando o limite | Integração | O limite nunca é ultrapassado |
| 200 eventos simultâneos na mesma conta | Integração | Soma exata |
| Token inválido, expirado, de outro emissor | Integração | 401 |
| Conta de outro cliente | Integração e E2E | 404 (não revela que a conta existe) |
| Cliente tentando operação reservada a sistemas | Integração e E2E | 403 |
| Cliente abusivo | Integração (gateway) | 429 com `Retry-After`, sem afetar os demais |

## 3. Teste de carga

### Metodologia
- **Ferramenta:** k6, com executor de taxa constante (*constant-arrival-rate*). A carga é medida em requisições por segundo, não em usuários virtuais, que é a unidade do requisito.
- **Caminho completo:** gateway (validação de JWT e rate limiting), Consolidado API, Redis e PostgreSQL. Tokens reais do Keycloak.
- **Mistura de consultas:** 50% saldo atual (tende a cache hit), 30% consolidado diário de datas aleatórias dos últimos 60 dias (mistura de hit e miss) e 20% saldo em um instante aleatório (nunca cacheado, sempre no banco).
- **Escrita simultânea:** 10 lançamentos/s durante o pico, gerando eventos, projeção e invalidação de cache enquanto as leituras acontecem.
- **Estresse:** depois do pico, a taxa sobe de 50 até 1.000 req/s para procurar o limite.
- **Critérios de aceite (thresholds):** perda abaixo de 5% (meta interna: abaixo de 1%) e p95 abaixo de 200 ms no pico. Se não forem atendidos, a CI falha.

### Resultados

| Cenário | Requisições | Req/s (média) | Perda | p50 (ms) | p95 (ms) | p99 (ms) |
|---|---|---|---|---|---|---|
| Pico (requisito) | 3.001 | 50,0 | **0,00%** | 1,8 | **5,5** | 11,5 |
| Escrita simultânea | 601 | 10,0 | 0,00% | 4,2 | 10,0 | 17,1 |
| Estresse (50 até 1.000 req/s) | 65.595 | 596,3 | 0,00% | 1,7 | 6,3 | 74,4 |

**Requisito atendido com folga:** zero perda a 50 req/s e nenhuma perda até 1.000 req/s, cerca de 20 vezes o pico exigido. No estresse, o p99 subiu para 74 ms: é o primeiro sinal de saturação, ainda longe de virar perda.

### Ressalvas (o que estes números NÃO dizem)
- **Ambiente:** uma única máquina da CI, com todos os containers e o próprio k6 dividindo CPU, sem TLS e sem latência de rede real. Em produção, a latência absoluta será maior; o que se transporta é a **ordem de grandeza da folga** e o comportamento sem perda.
- **Poucos usuários:** dois clientes de teste simulam muitos clientes. Por isso, só durante a carga, o limite por cliente do gateway é elevado (`deploy/carga/docker-compose.carga.yml`). O comportamento do rate limiting é validado separadamente nos testes do gateway.
- **Cache favorável:** poucas contas significam muitos acertos no cache. Os 20% de consultas por instante passam sempre pelo banco e mantêm o teste honesto quanto ao caminho sem cache.
- **Primeira execução:** a primeira rodada mediu 0,37% de "perda", que eram 404 de uma conta sem nenhum lançamento consolidado (comportamento correto da API). O script passou a aquecer as contas antes de medir. Registrado aqui por transparência.

### Como reproduzir
```bash
docker compose up -d --build --wait
./scripts/e2e.sh      # cria as contas
./scripts/carga.sh    # resultado em resultados-carga/resumo.md
```

## 4. Testes de mutação (Stryker.NET)

O Stryker altera o código de produção (troca `<` por `<=`, remove instruções, inverte condições) e roda os testes. Se os testes continuam passando, o mutante "sobreviveu": existe um defeito que os testes não detectariam. É a medida de qualidade dos testes, enquanto a cobertura mede apenas quais linhas foram executadas.

| Projeto | Primeira execução | Depois dos ajustes | Mutantes válidos |
|---|---|---|---|
| `Lancamentos.Domain` | 71,4% | **98,8%** | 85 |
| `Consolidado.Core` | 67,3% | **100%** | 37 |

**O que foi feito com os sobreviventes:**
1. **Testes que faltavam de verdade:** limites exatos (chave com 100 caracteres, descrição com 140, limite zero, circuito fechando no segundo exato), `ValorComSinal` de crédito, cancelamento que deve propagar sem abrir o circuito, formato das chaves de cache (contrato entre API e Worker) e o laço de invalidação limitado a 31 dias.
2. **Mutantes equivalentes, ignorados na configuração:** `ConfigureAwait(false)` trocado por `true` não muda o comportamento observável.
3. **Mutações de texto, ignoradas no domínio de Lançamentos:** as mensagens de erro são para leitura humana; o contrato da API é o `codigo` estável, que é verificado nos testes.
4. **Único sobrevivente:** o construtor privado exigido pelo ORM, que nenhum teste de domínio deveria chamar.

A CI falha se o score de qualquer projeto cair abaixo de 80%.
