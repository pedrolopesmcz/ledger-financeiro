#!/usr/bin/env bash
# Teste ponta a ponta contra o ambiente do docker compose (tokens reais do Keycloak,
# gateway, dois serviços, broker). Também derruba o Consolidado no meio para provar
# o requisito de disponibilidade.
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/dados-demo.env

GW=${GW:-http://localhost:5000}
KC=${KC:-http://localhost:8080}
TOKEN_URL="$KC/realms/ledger/protocol/openid-connect/token"
CORPO=$(mktemp)

passo()  { printf '\n\033[1m%s\033[0m\n' "$1"; }
ok()     { printf '  \033[32mOK\033[0m %s\n' "$1"; }
falhar() { printf '  \033[31mFALHOU\033[0m %s\n' "$1"; cat "$CORPO" 2>/dev/null || true; exit 1; }

# req METODO CAMINHO TOKEN [CORPO_JSON] [IDEMPOTENCY_KEY] -> imprime o status HTTP; corpo em $CORPO
req() {
  local args=(-s -o "$CORPO" -w '%{http_code}' -X "$1" "$GW$2" -H 'Content-Type: application/json')
  [[ -n "${3:-}" ]] && args+=(-H "Authorization: Bearer $3")
  [[ -n "${4:-}" ]] && args+=(-d "$4")
  [[ -n "${5:-}" ]] && args+=(-H "Idempotency-Key: $5")
  curl "${args[@]}"
}

espera_status() { # descricao status_esperado segundos comando...
  local desc=$1 esperado=$2 limite=$3; shift 3
  for _ in $(seq 1 "$limite"); do
    [[ "$("$@")" == "$esperado" ]] && { ok "$desc"; return 0; }
    sleep 1
  done
  falhar "$desc (esperado $esperado)"
}

saldo_consolidado() { req GET "/contas/$1/saldo" "$2" > /dev/null; jq -r '.saldo // empty' "$CORPO"; }

espera_saldo() { # descricao conta token valor segundos
  for _ in $(seq 1 "$5"); do
    [[ "$(saldo_consolidado "$2" "$3")" == "$4" ]] && { ok "$1"; return 0; }
    sleep 1
  done
  falhar "$1 (esperado $4, obtido $(saldo_consolidado "$2" "$3"))"
}

passo "1. Ambiente no ar"
espera_status "gateway respondendo" 200 120 curl -s -o /dev/null -w '%{http_code}' "$GW/health/live"
espera_status "sem token: 401" 401 60 req GET "/contas/$CONTA_ANA" ""

passo "2. Tokens reais do Keycloak"
BACKOFFICE=$(curl -sf "$TOKEN_URL" -d grant_type=client_credentials -d client_id=backoffice -d client_secret=backoffice-secret-dev | jq -r .access_token)
ANA=$(curl -sf "$TOKEN_URL" -d grant_type=password -d client_id=app-canais -d username=ana -d password=ana123 | jq -r .access_token)
BRUNO=$(curl -sf "$TOKEN_URL" -d grant_type=password -d client_id=app-canais -d username=bruno -d password=bruno123 | jq -r .access_token)
[[ -n "$BACKOFFICE" && "$BACKOFFICE" != null ]] && ok "token do backoffice (client credentials)" || falhar "token do backoffice"
[[ -n "$ANA" && "$ANA" != null ]] && ok "token da Ana (usuário final)" || falhar "token da Ana"

passo "3. Contas da demonstração (backoffice)"
for dados in "$CONTA_ANA ContaDigital 0 $ANA_ID" "$CARTAO_ANA CartaoCredito 2000 $ANA_ID" "$CONTA_BRUNO ContaDigital 0 $BRUNO_ID"; do
  read -r id tipo limite titular <<< "$dados"
  status=$(req POST /contas "$BACKOFFICE" "{\"id\":\"$id\",\"tipo\":\"$tipo\",\"limite\":$limite,\"titularId\":\"$titular\"}")
  [[ "$status" == 201 || "$status" == 409 ]] && ok "conta $tipo $id ($status)" || falhar "abrir conta $id: $status"
done

# Execuções repetidas: chaves únicas por rodada e saldo inicial lido da própria conta.
RODADA=$(date +%s)
req GET "/contas/$CONTA_ANA" "$ANA" > /dev/null
INICIAL=$(jq -r .saldo "$CORPO")

passo "4. Lançamentos"
[[ $(req POST "/contas/$CONTA_ANA/lancamentos" "$BACKOFFICE" '{"operacao":"Deposito","valor":500}' "dep-$RODADA") == 201 ]] && ok "backoffice deposita 500" || falhar "depósito"
[[ $(req POST "/contas/$CONTA_ANA/lancamentos" "$BACKOFFICE" '{"operacao":"Deposito","valor":500}' "dep-$RODADA") == 200 ]] && ok "reenvio com a mesma chave: 200, sem duplicar" || falhar "idempotência"
[[ $(req POST "/contas/$CONTA_ANA/lancamentos" "$ANA" '{"operacao":"PixEnviado","valor":120}' "pix-$RODADA") == 201 ]] && ok "Ana envia Pix de 120" || falhar "pix"
[[ $(req POST "/contas/$CONTA_ANA/lancamentos" "$ANA" '{"operacao":"Deposito","valor":1000}' "fraude-$RODADA") == 403 ]] && ok "Ana não consegue se dar um depósito: 403" || falhar "depósito pelo cliente"
[[ $(req POST "/contas/$CONTA_ANA/lancamentos" "$ANA" '{"operacao":"Saque","valor":999999}' "saque-$RODADA") == 422 ]] && ok "saque acima do saldo: 422" || falhar "saldo insuficiente"
ESPERADO=$(jq -n "$INICIAL + 380")
req GET "/contas/$CONTA_ANA" "$ANA" > /dev/null
[[ $(jq -r .saldo "$CORPO") == "$ESPERADO" ]] && ok "saldo de autorização: $ESPERADO" || falhar "saldo de autorização"

passo "5. Titularidade"
[[ $(req GET "/contas/$CONTA_BRUNO" "$ANA") == 404 ]] && ok "Ana não enxerga a conta do Bruno (404)" || falhar "conta alheia no Lançamentos"
[[ $(req GET "/contas/$CONTA_ANA/saldo" "$BRUNO") == 404 ]] && ok "Bruno não enxerga o saldo da Ana (404)" || falhar "conta alheia no Consolidado"

passo "6. Consolidado"
espera_saldo "consolidado convergiu para $ESPERADO" "$CONTA_ANA" "$ANA" "$ESPERADO" 30
HOJE=$(TZ=America/Sao_Paulo date +%F)
[[ $(req GET "/contas/$CONTA_ANA/consolidado/$HOJE" "$ANA") == 200 ]] && ok "consolidado do dia $HOJE: $(jq -c '{saldoInicial,creditos,debitos,saldoFinal}' "$CORPO")" || falhar "consolidado diário"

passo "7. Consolidado fora do ar"
docker compose stop consolidado-worker consolidado-api > /dev/null 2>&1
ok "consolidado-worker e consolidado-api parados"
[[ $(req POST "/contas/$CONTA_ANA/lancamentos" "$BACKOFFICE" '{"operacao":"Deposito","valor":100}' "fora-$RODADA") == 201 ]] && ok "Lançamentos continua aceitando: 201" || falhar "lançamento com consolidado fora"
docker compose start consolidado-worker consolidado-api > /dev/null 2>&1
ok "consolidado de volta"
ESPERADO=$(jq -n "$ESPERADO + 100")
espera_saldo "eventos acumulados processados, saldo convergiu para $ESPERADO" "$CONTA_ANA" "$ANA" "$ESPERADO" 60

printf '\n\033[32mE2E concluído com sucesso.\033[0m\n'
