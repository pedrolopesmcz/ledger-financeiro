#!/usr/bin/env bash
# Executa o teste de carga (k6 em container) contra o ambiente do docker compose.
# Pré-requisito: ambiente no ar e contas criadas (./scripts/e2e.sh).
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/dados-demo.env
KC=${KC:-http://localhost:8080}
TOKEN_URL="$KC/realms/ledger/protocol/openid-connect/token"

# Limite por cliente elevado apenas para a carga (ver deploy/carga/docker-compose.carga.yml).
docker compose -f docker-compose.yml -f deploy/carga/docker-compose.carga.yml up -d --wait gateway

token() { curl -sf "$TOKEN_URL" "$@" | jq -r .access_token; }
TOKEN_BACKOFFICE=$(token -d grant_type=client_credentials -d client_id=backoffice -d client_secret=backoffice-secret-dev)
TOKEN_ANA=$(token -d grant_type=password -d client_id=app-canais -d username=ana -d password=ana123)
TOKEN_BRUNO=$(token -d grant_type=password -d client_id=app-canais -d username=bruno -d password=bruno123)

mkdir -p resultados-carga
docker run --rm --network host \
  -v "$PWD/tests/Carga:/scripts:ro" -v "$PWD/resultados-carga:/resultados" \
  -e GW=http://localhost:5000 \
  -e TOKEN_ANA="$TOKEN_ANA" -e TOKEN_BRUNO="$TOKEN_BRUNO" -e TOKEN_BACKOFFICE="$TOKEN_BACKOFFICE" \
  -e CONTA_ANA="$CONTA_ANA" -e CONTA_BRUNO="$CONTA_BRUNO" \
  grafana/k6:latest run --quiet /scripts/consolidado.js
