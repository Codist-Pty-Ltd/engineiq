#!/usr/bin/env bash
# prod-shaped local admin: Postgres + RabbitMQ + migrations + engineiq-admin image (same Dockerfiles/compose keys as deploy).
# Does NOT start API, worker, Caddy, or static sites — only what Admin needs.
#
# Prerequisites: .env from .env.example with POSTGRES_PASSWORD, RABBITMQ_PASSWORD, ENGINEIQ_ADMIN_PASSWORD set.
#
# Images: defaults to LOCAL BUILD (does not use deploy's SKIP_PULL from .env — that often pulls placeholder GHCR paths).
#   ADMIN_PARITY_PULL_IMAGES=0 — docker compose build migrator + admin (default)
#   ADMIN_PARITY_PULL_IMAGES=1 — docker compose pull (real ENGINEIQ_REGISTRY + docker login if private)
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ ! -f .env ]]; then
  echo "Missing .env — copy .env.example to .env and set POSTGRES_PASSWORD, RABBITMQ_PASSWORD, ENGINEIQ_ADMIN_PASSWORD."
  exit 1
fi

# Preserve parity-mode env before .env overwrites (e.g. SKIP_PULL=0 for deploy).
_pull_images="${ADMIN_PARITY_PULL_IMAGES:-}"

set -a
# shellcheck disable=SC1091
source .env
set +a

if [[ -z "${_pull_images}" ]]; then
  ADMIN_PARITY_PULL_IMAGES="${ADMIN_PARITY_PULL_IMAGES:-0}"
else
  ADMIN_PARITY_PULL_IMAGES="${_pull_images}"
fi

if [[ "${ADMIN_PARITY_PULL_IMAGES}" == "1" ]]; then
  echo "Pulling engineiq-migrator + engineiq-admin from ENGINEIQ_REGISTRY (ADMIN_PARITY_PULL_IMAGES=1)…"
  docker compose pull engineiq-migrator engineiq-admin
else
  echo "Building engineiq-migrator + engineiq-admin locally (default). Set ADMIN_PARITY_PULL_IMAGES=1 to pull from registry."
  docker compose build engineiq-migrator engineiq-admin
fi

docker compose up -d postgres rabbitmq

echo "Waiting for postgres and rabbitmq to become ready…"
for _ in $(seq 1 90); do
  if docker compose exec -T postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' >/dev/null 2>&1 \
    && docker compose exec -T rabbitmq rabbitmq-diagnostics -q ping >/dev/null 2>&1; then
    break
  fi
  sleep 2
done

docker compose exec -T postgres sh -c 'pg_isready -U "$POSTGRES_USER" -d "$POSTGRES_DB"' >/dev/null

docker compose --profile migration run --rm engineiq-migrator

docker compose --profile platform up -d --build engineiq-admin

echo ""
echo "Admin (Docker, aligned with docker/Dockerfile.admin + compose): http://127.0.0.1:8081/admin/"
echo "Sign in with ENGINEIQ_ADMIN_USERNAME / ENGINEIQ_ADMIN_PASSWORD from .env"
echo "API smoke:"
echo "  curl -fsS -u \"\${ENGINEIQ_ADMIN_USERNAME}:\${ENGINEIQ_ADMIN_PASSWORD}\" http://127.0.0.1:8081/api/v1/admin/health"
echo ""
echo "Fast dotnet run + SPA on host: ./scripts/sync-admin-ui-wwwroot.sh then dotnet run --project src/EngineIQ.Admin"
