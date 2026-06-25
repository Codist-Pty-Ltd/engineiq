#!/usr/bin/env bash
# One-time Paystack plan bootstrap for EngineIQ billing tiers (ZAR, monthly).
#
# Creates three plans namespaced with the "EngineIQ" prefix so they do not collide
# with existing Billable product plans on the same Paystack account.
#
# API reference: https://paystack.com/docs/api/plan/
#   POST /plan  — body: name, interval, amount (subunit), currency (ZAR = cents)
#   GET  /plan  — list plans (paginated; filter client-side by exact name)
#
# Usage:
#   export PAYSTACK_SECRET_KEY=sk_live_...
#   ./scripts/paystack/create-plans.sh
#
# Idempotent: skips creation when a plan with the exact name already exists.
# DRY_RUN=1 validates env and prints intended actions without calling Paystack.
#
set -euo pipefail

API_BASE="${PAYSTACK_API_BASE:-https://api.paystack.co}"

if [[ -z "${PAYSTACK_SECRET_KEY:-}" ]]; then
  echo "ERROR: PAYSTACK_SECRET_KEY is required (Paystack Dashboard → Settings → API Keys)." >&2
  exit 1
fi

# name|amount_cents (ZAR subunits: Rand × 100)
readonly -a PLAN_ROWS=(
  "EngineIQ Starter|250000"
  "EngineIQ Growth|550000"
  "EngineIQ Scale|1200000"
)

paystack_curl() {
  local method="$1"
  local path="$2"
  local body="${3:-}"

  if [[ -n "$body" ]]; then
    curl -sS "${API_BASE}${path}" \
      -H "Authorization: Bearer ${PAYSTACK_SECRET_KEY}" \
      -H "Content-Type: application/json" \
      -H "Accept: application/json" \
      -X "$method" \
      -d "$body"
  else
    curl -sS -G "${API_BASE}${path}" \
      -H "Authorization: Bearer ${PAYSTACK_SECRET_KEY}" \
      -H "Accept: application/json"
  fi
}

# Prints plan_code for an exact plan name, or empty string if not found.
find_plan_code_by_name() {
  local want_name="$1"
  python3 - "$want_name" <<'PY'
import json
import os
import sys
import urllib.error
import urllib.request

want_name = sys.argv[1]
api_base = os.environ.get("PAYSTACK_API_BASE", "https://api.paystack.co")
secret = os.environ["PAYSTACK_SECRET_KEY"]

page = 1
page_count = 1

while page <= page_count:
    url = f"{api_base}/plan?perPage=100&page={page}"
    req = urllib.request.Request(
        url,
        headers={
            "Authorization": f"Bearer {secret}",
            "Accept": "application/json",
        },
    )
    with urllib.request.urlopen(req) as resp:
        payload = json.load(resp)

    if not payload.get("status"):
        print(payload.get("message", "Paystack list plans failed"), file=sys.stderr)
        sys.exit(1)

    for plan in payload.get("data") or []:
        if plan.get("name") == want_name:
            print(plan.get("plan_code") or "")
            sys.exit(0)

    meta = payload.get("meta") or {}
    page_count = int(meta.get("pageCount") or 1)
    page += 1

sys.exit(0)
PY
}

create_plan() {
  local name="$1"
  local amount_cents="$2"

  local body
  body=$(python3 - "$name" "$amount_cents" <<'PY'
import json
import sys

name, amount = sys.argv[1], int(sys.argv[2])
print(json.dumps({
    "name": name,
    "interval": "monthly",
    "amount": amount,
    "currency": "ZAR",
    "description": f"EngineIQ SaaS subscription — {name}",
}))
PY
)

  local response
  response=$(paystack_curl POST "/plan" "$body")

  python3 - "$response" <<'PY'
import json
import sys

payload = json.loads(sys.argv[1])
if not payload.get("status"):
    print(payload.get("message", "Paystack create plan failed"), file=sys.stderr)
    sys.exit(1)

data = payload.get("data") or {}
plan_code = data.get("plan_code")
if not plan_code:
    print("Paystack response missing plan_code", file=sys.stderr)
    sys.exit(1)

print(plan_code)
PY
}

ensure_plan() {
  local name="$1"
  local amount_cents="$2"

  if [[ "${DRY_RUN:-}" == "1" ]]; then
    echo "[dry-run] would ensure plan: ${name} (${amount_cents} cents ZAR, monthly)" >&2
    return 0
  fi

  local existing
  existing=$(find_plan_code_by_name "$name")
  if [[ -n "$existing" ]]; then
    echo "SKIP  ${name} — already exists (plan_code=${existing})" >&2
    echo "$existing"
    return 0
  fi

  echo "CREATE ${name} — ${amount_cents} cents ZAR, interval=monthly, currency=ZAR" >&2
  local created
  created=$(create_plan "$name" "$amount_cents")
  echo "OK    ${name} — plan_code=${created}" >&2
  echo "$created"
}

main() {
  echo "EngineIQ Paystack plan setup (account plans are separate from Billable product plans)."
  echo "API base: ${API_BASE}"
  echo ""

  local -a codes=()
  for row in "${PLAN_ROWS[@]}"; do
    local name="${row%%|*}"
    local amount="${row##*|}"
    codes+=("$(ensure_plan "$name" "$amount")")
    echo ""
  done

  if [[ "${DRY_RUN:-}" == "1" ]]; then
    echo "Dry run complete — no Paystack API calls made."
    exit 0
  fi

  cat <<EOM

Copy these plan codes into .env (see .env.example):

  PAYSTACK_PLAN_STARTER=${codes[0]}
  PAYSTACK_PLAN_GROWTH=${codes[1]}
  PAYSTACK_PLAN_SCALE=${codes[2]}

Also set PAYSTACK_PUBLIC_KEY and PAYSTACK_WEBHOOK_SECRET from the Paystack Dashboard.
See DEPLOYMENT.md §2.4.
EOM
}

main "$@"
