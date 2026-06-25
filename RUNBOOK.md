# EngineIQ — operations runbook

Short procedures for operators. Production admin binds **`127.0.0.1:8081`** only — use an SSH tunnel from your laptop.

Credentials: **`ENGINEIQ_ADMIN_USERNAME`** / **`ENGINEIQ_ADMIN_PASSWORD`** from server **`.env`** (same values as `Admin:*` in compose).

---

## Tenant lost their portal API key

**Symptoms:** Tenant cannot log in at `https://app.engineiq.co.za/login` (invalid API key). Keys are shown **once** at registration; they are **not** stored in plaintext and **cannot** be looked up later.

**Effect of rotation:** The previous key stops working **immediately**. Portal, `X-Api-Key` tenant API calls, and any automation using the old key return **401** until the tenant uses the new key.

### 1. Open admin access

On your laptop (replace `<HETZNER_IP>`):

```bash
ssh -L 8081:127.0.0.1:8081 root@<HETZNER_IP>
```

Leave this session open. Admin API base: **`http://127.0.0.1:8081`**.

Optional: confirm admin is up:

```bash
curl -fsS -u "$ENGINEIQ_ADMIN_USERNAME:$ENGINEIQ_ADMIN_PASSWORD" \
  http://127.0.0.1:8081/api/v1/admin/health
```

### 2. Find the tenant UUID

If you already have **`tenant_id`** (from support email or admin UI), skip to step 3.

List tenants (JSON array; match **`name`** / support context):

```bash
curl -fsS -u "$ENGINEIQ_ADMIN_USERNAME:$ENGINEIQ_ADMIN_PASSWORD" \
  http://127.0.0.1:8081/api/v1/admin/tenants
```

Or open **`http://127.0.0.1:8081/admin`** in the browser (same tunnel), sign in, and copy the tenant UUID from the tenants page.

### 3. Rotate the key (operator)

```bash
TENANT_ID="<paste-tenant-guid>"

curl -fsS -u "$ENGINEIQ_ADMIN_USERNAME:$ENGINEIQ_ADMIN_PASSWORD" \
  -X POST "http://127.0.0.1:8081/api/v1/admin/tenants/${TENANT_ID}/rotate-key"
```

**Success:** HTTP **200** with body:

```json
{ "api_key": "<tenantId-without-dashes>.<64-hex-secret>" }
```

**Failure:** **404** if `TENANT_ID` does not exist.

**Important:** This response is the **only** time the new key exists in cleartext. EngineIQ stores **SHA256** of the key only. Do not paste the key into tickets, Slack, or logs.

### 4. Deliver the new key to the tenant (once)

Send **`tenant_id`** + **`api_key`** to the tenant contact over a **private channel** you already trust (e.g. email to **`contact_email`** on file, or a scheduled call). Ask them to:

1. Log in at **`https://app.engineiq.co.za/login`** with tenant UUID + new API key.
2. Update any scripts or CI that call **`https://api.engineiq.co.za/api/v1/tenant/{id}/*`** with header **`X-Api-Key`**.

Tell them the **old key no longer works** as soon as step 3 completed.

### 5. Verify (operator)

```bash
# New key — should return 200 + account JSON
curl -fsS -H "X-Api-Key: <new-api-key>" \
  "https://api.engineiq.co.za/api/v1/tenant/${TENANT_ID}/account"

# Old key — should return 401
curl -sS -o /dev/null -w "%{http_code}\n" -H "X-Api-Key: <old-api-key-if-known>" \
  "https://api.engineiq.co.za/api/v1/tenant/${TENANT_ID}/account"
```

### Notes

- **Self-serve (optional):** If the tenant still has a working key and wants to rotate themselves: **`POST /api/v1/tenant/{id}/rotate-key`** with **`X-Api-Key`** (public API). Use the admin path above when the key is **lost**.
- **Golden-four / demo tenants:** Preserve tenant rows; update **`scripts/demo-tenant-state.local.env`** after rotation so internal scripts stay aligned (**DEPLOYMENT.md §11.3**).
- **Security:** Never commit API keys. Never log **`Authorization`** or **`X-Api-Key`** values.
