#!/usr/bin/env bash
# Golden-path smoke: API health, tenant portal endpoints, optional golden-four credentials.
# Does not open a real PR — run that manually after this passes.
#
# Usage:
#   ./scripts/verify-golden-path.sh
#   ENGINEIQ_API_URL=http://localhost:5056 ./scripts/verify-golden-path.sh
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

API="${ENGINEIQ_API_URL:-http://localhost:5056}"
API="${API%/}"

echo "=== EngineIQ golden-path smoke ==="
echo "API: $API"
echo ""

failed=0

check() {
  local label="$1"
  local url="$2"
  local expect="${3:-200}"
  local code
  if ! code="$(curl -sS -o /dev/null -w "%{http_code}" "$url" 2>/dev/null)"; then
    echo "  FAIL $label → curl error"
    failed=1
    return
  fi
  if [[ "$code" != "$expect" ]]; then
    echo "  FAIL $label → HTTP $code (expected $expect)"
    failed=1
  else
    echo "  OK   $label → HTTP $code"
  fi
}

check "GET /security" "$API/security"

# First tenant from golden-four env when present
ENV_FILE="${1:-scripts/demo-tenant-state.local.env}"
TENANT_ID=""
API_KEY=""
if [[ -f "$ENV_FILE" ]]; then
  # shellcheck disable=SC1090
  set -a
  source "$ENV_FILE"
  set +a
  TENANT_ID="${TENANT_MYBILLABLE:-${TENANT_WARROOM:-}}"
  API_KEY="${API_KEY_MYBILLABLE:-${API_KEY_WARROOM:-}}"
fi

if [[ -n "$TENANT_ID" && -n "$API_KEY" ]]; then
  echo ""
  echo "=== Portal tenant API ($TENANT_ID) ==="
  for path in \
    "/status" \
    "/account" \
    "/preferences" \
    "/notifications?take=5" \
    "/jobs?status=Completed&take=3" \
    "/onboarding/install-url"; do
    code="$(curl -sS -o /dev/null -w "%{http_code}" -H "X-Api-Key: $API_KEY" "$API/api/v1/tenant/$TENANT_ID$path" || echo "000")"
    if [[ "$path" == "/onboarding/install-url" ]]; then
      if [[ "$code" == "200" || "$code" == "409" ]]; then
        echo "  OK   $path → HTTP $code"
      else
        echo "  FAIL $path → HTTP $code"
        failed=1
      fi
    elif [[ "$code" == "200" ]]; then
      echo "  OK   $path → HTTP $code"
    else
      echo "  FAIL $path → HTTP $code"
      failed=1
    fi
  done
else
  echo ""
  echo "SKIP tenant API checks — set scripts/demo-tenant-state.local.env (see demo-tenant-state.example.env)"
fi

if [[ -x "$ROOT/scripts/verify-golden-four-api.sh" && -f "$ENV_FILE" ]]; then
  echo ""
  "$ROOT/scripts/verify-golden-four-api.sh" "$ENV_FILE" || failed=1
fi

echo ""
if [[ "$failed" -ne 0 ]]; then
  echo "Golden-path smoke FAILED."
  exit 1
fi

echo "Golden-path smoke passed."
echo ""
echo "Manual PR check (required for full loop):"
echo "  1. Ensure worker + RabbitMQ + API are running (docker compose --profile platform)."
echo "  2. Apply migration Session9_PortalPreferences if not yet applied."
echo "  3. Open or sync a non-draft PR on a connected repo."
echo "  4. Confirm GitHub PR comment + portal Dashboard job + Notifications activity."
