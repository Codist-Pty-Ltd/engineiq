# EngineIQ — Production deployment handover (Hetzner + engineiq.co.za)

This document is the **single source of truth** for bringing an empty Hetzner server and an empty GitHub repo to a **working production stack**, including TLS, database migrations, Docker topology, and onboarding **four** GitHub organisations as tenants.

**Canonical code repo:** [github.com/Codist-Pty-Ltd/engineiq](https://github.com/Codist-Pty-Ltd/engineiq)

---

## 1. What you are deploying

| Public hostname | Purpose |
|-----------------|--------|
| `https://engineiq.co.za` | Marketing site (Next.js static) |
| `https://app.engineiq.co.za` | Client portal (Next.js static) |
| `https://api.engineiq.co.za` | EngineIQ API (webhooks, onboarding, tenant API, `GET /security`) |

**GitHub App webhook (must be this path on the API host):**

`https://api.engineiq.co.za/webhooks/github`

### 1.1 Internal topology (Docker Compose)

Nothing on the host binds PostgreSQL/RabbitMQ/Redis to the public internet. Only **Caddy** listens on **80** and **443**.

| Service | Role | Host exposure |
|---------|------|----------------|
| `caddy` | TLS (Let’s Encrypt), reverse proxy | **0.0.0.0:80, 443** |
| `engineiq-api` | .NET 8 API | Internal `:5000` only |
| `engineiq-worker` | .NET 8 queue consumer | No public port |
| `engineiq-admin` | Internal admin — React **`/admin`** + **`/api/v1/admin`** JSON | **127.0.0.1:8081** only |
| `engineiq-marketing` | nginx + static export | Internal `:80` |
| `engineiq-portal` | nginx + static export | Internal `:80` |
| `postgres` | PostgreSQL 16 | Volume only |
| `rabbitmq` | RabbitMQ 3 + management | Internal only |
| `redis` | Redis 7 | Internal only |
| `engineiq-reporting` | Python 3.12 + scheduled jobs | No public port |
| `engineiq-migrator` | EF migrations (profile `migration`) | Run via `deploy.sh`, not long-running |

### 1.2 Data & “seeding”

- **Schema:** applied by **`engineiq-migrator`** (Docker image from `docker/Dockerfile.migrator`).
- **Reference tenant row:** migrations seed one internal **`Billable`** tenant row (`f1111111-1111-1111-1111-111111111111`) for schema/RLS consistency — **it does not have a portal API key**. Do **not** use it for the client portal.
- **Real tenants:** created only via **`POST /api/v1/onboarding/register`** (marketing sign-up or API). Each tenant gets an API key once; GitHub App install links that tenant to an installation.

---

## 2. Prerequisites checklist (before touching the server)

Complete these **in order**; missing any item breaks “first shot” deploy.

### 2.1 Accounts & artefacts

- [ ] **GitHub organisation access** for onboarding installs: **therecord**, **billable**, **war-room**, **skillbay** (exact slugs must match [GitHub organisation URLs](https://docs.github.com/en/organizations/collaborating-with-groups-in-organizations/about-organizations); confirm **`war-room`** is correct vs `war_room`).
- [ ] **Anthropic** API key ([Anthropic Console](https://console.anthropic.com/)).
- [ ] **SendGrid** (or accept that welcome/live emails are skipped until API key + template IDs are set).
- [ ] **Slack incoming webhook** (optional; `deploy.sh` notifications).
- [ ] **Hetzner** account + SSH public key.
- [ ] **Domain** `engineiq.co.za` DNS editable (cPanel zone editor or registrar DNS).

### 2.2 GitHub App (single app, multiple installs)

Create **one** GitHub App (organisation or user-owned — typically under **Codist-Pty-Ltd** / this repo):

1. **GitHub → Settings → Developer settings → GitHub Apps → New GitHub App**
2. **Webhook URL:** `https://api.engineiq.co.za/webhooks/github`
3. **Webhook secret:** generate a long random string → becomes `GITHUB_WEBHOOK_SECRET` in `.env`.
4. **Permissions:** Repository permissions — **Pull requests: Read & write**, **Contents: Read**.
5. **Subscribe to events:** at minimum what your webhook handles (e.g. pull request events — align with `WebhookController` in this repo).
6. Save **App ID**, **App slug**, generate and download **Private Key (PEM)** → `GITHUB_PRIVATE_KEY_PEM`.

After deploy, each customer org installs **this same app**; EngineIQ creates **one tenant per registration** and binds **one installation ID** per tenant.

### 2.3 SendGrid dynamic templates (welcome email)

Welcome mail sends dynamic fields:

- `company_name`
- `install_url`
- `dpa_pdf_url`
- `webhook_secret`

Create templates in SendGrid and put template IDs in `.env` (`SENDGRID_TEMPLATE_*`). If templates are empty, sending is skipped (API still works).

### 2.4 Paystack — EngineIQ subscription plans (separate from Billable)

EngineIQ billing uses the **same Paystack merchant account** as other Codist products (e.g. Billable), but **distinct plan names** so nothing touches Billable’s existing plans.

| Plan | Monthly (ZAR) | Paystack amount (cents) |
|------|---------------|-------------------------|
| EngineIQ Starter | R 2,500 | `250000` |
| EngineIQ Growth | R 5,500 | `550000` |
| EngineIQ Scale | R 12,000 | `1200000` |

**Run once per environment** (test vs live) after `PAYSTACK_SECRET_KEY` is set:

```bash
chmod +x scripts/paystack/create-plans.sh
export PAYSTACK_SECRET_KEY=sk_live_...   # or sk_test_... for sandbox
./scripts/paystack/create-plans.sh
```

The script is **idempotent**: it lists existing plans via `GET /plan` and skips any row whose **name** already matches (`EngineIQ Starter`, `EngineIQ Growth`, `EngineIQ Scale`). On create it calls `POST /plan` with `name`, `interval` (`monthly`), `amount` (ZAR cents), and `currency` (`ZAR`) per [Paystack Plan API](https://paystack.com/docs/api/plan/).

Copy the printed `plan_code` values into `.env`:

- `PAYSTACK_PLAN_STARTER`
- `PAYSTACK_PLAN_GROWTH`
- `PAYSTACK_PLAN_SCALE`

Also set `PAYSTACK_PUBLIC_KEY` and `PAYSTACK_WEBHOOK_SECRET` from the Paystack Dashboard. **Do not** rename or delete Billable plans in Paystack — EngineIQ only adds the three prefixed plans above.

---

## 3. DNS (cPanel or registrar)

Point these to the **Hetzner server IPv4** (and IPv6 **AAAA** if you use it):

| Host / name | Type | Value |
|-------------|------|--------|
| `@` (root) | A | `<HETZNER_SERVER_IP>` |
| `www` | A | `<HETZNER_SERVER_IP>` |
| `api` | A | `<HETZNER_SERVER_IP>` |
| `app` | A | `<HETZNER_SERVER_IP>` |

TTL: **300** seconds until stable, then increase.

**Do not** park the apex on shared hosting if the API must hit Hetzner — the **A records** must resolve to the VM that runs Caddy.

Propagation: allow up to **48 hours** globally; often **5–30 minutes**. Let’s Encrypt will fail until `api.engineiq.co.za` resolves to this server.

---

## 4. Hetzner server bootstrap (Ubuntu 22.04)

### 4.1 Create the server

- Image: **Ubuntu 22.04**
- Location: **Nuremberg** or **Johannesburg** (if available on your account) — pick closest to users.
- SSH keys only (disable password auth).
- Firewall (Hetzner Cloud): **TCP 22** (your IP or VPN), **TCP 80**, **TCP 443**.

### 4.2 Base packages

SSH in as root or sudo user, then:

```bash
sudo apt-get update && sudo apt-get upgrade -y
sudo apt-get install -y ca-certificates curl git python3 python3-pip ufw

# Docker (official convenience script — verify Docker docs if you prefer package install)
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker "$USER"
# log out and back in so docker group applies
```

Enable UFW if not using only Hetzner firewall:

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
```

---

## 5. Code on the server

```bash
sudo mkdir -p /opt/engineiq && sudo chown "$USER:$USER" /opt/engineiq
cd /opt/engineiq
git clone https://github.com/Codist-Pty-Ltd/engineiq.git .
# You must be in the repository root (directory that contains docker-compose.yml and deploy.sh).
```

Stay on **`main`** (or your release branch) for production.

---

## 6. Configure `.env`

```bash
cp .env.example .env
nano .env   # or vim
```

**Minimum critical fields:**

- `POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `ENGINEIQ_ADMIN_PASSWORD`
- `CADDY_ACME_EMAIL` (real mailbox for Let’s Encrypt expiry)
- `GITHUB_*`, `ANTHROPIC_API_KEY`
- `ENGINEIQ_REGISTRY`, `ENGINEIQ_TAG` **or** `SKIP_PULL=1` for build-on-server (see §7)
- `NEXT_PUBLIC_ENGINEIQ_API_URL=https://api.engineiq.co.za`
- `NEXT_PUBLIC_PORTAL_URL=https://app.engineiq.co.za`
- `ENGINEIQ_DASHBOARD_BASE_URL=https://app.engineiq.co.za`

**Never commit `.env`.**

### 6.1 PEM in `.env`

Use quoted multiline PEM **or** single line with `\n` escapes. Docker Compose must parse the file cleanly.

---

## 7. Images: registry pull vs build on server

### Option A — Pull from GHCR (recommended)

1. In GitHub: enable **Actions**, ensure workflow **Publish Docker images** has run on `main`.
2. Packages: make container images **public** or configure PAT on server:

   ```bash
   echo "<GITHUB_PAT_WITH_READ_PACKAGES>" | docker login ghcr.io -u <GITHUB_USERNAME> --password-stdin
   ```

   PAT must have **`read:packages`** for **`ghcr.io/codist-pty-ltd/engineiq`**.

3. In `.env`:

   ```env
   ENGINEIQ_REGISTRY=ghcr.io/codist-pty-ltd/engineiq
   ENGINEIQ_TAG=latest
   SKIP_PULL=0
   ```

### Option B — Build on server (no registry)

```env
SKIP_PULL=1
ENGINEIQ_REGISTRY=engineiq   # local image prefix
```

Then `./deploy.sh` runs `docker compose build` before `up`. Requires **more RAM/CPU** on the VM (building Node + .NET).

---

## 8. First deploy

```bash
cd /opt/engineiq   # repository root: docker-compose.yml + deploy.sh live here
chmod +x deploy.sh scripts/verify-deployment.sh scripts/verify-golden-four-api.sh scripts/register-internal-demo-tenants.sh 2>/dev/null || true
./deploy.sh
```

What `deploy.sh` does:

1. `docker compose pull` (unless `SKIP_PULL=1` → `build`)
2. `docker compose --profile migration run --rm engineiq-migrator` → **DB migrations**
3. `docker compose --profile platform up -d`
4. Internal health checks (API + static sites)

Optional:

```env
PUBLIC_API_HEALTH_URL=https://api.engineiq.co.za/health
```

If TLS/DNS are not ready yet:

```env
SKIP_PUBLIC_HEALTH=1
```

---

## 9. Verification (must pass)

From your laptop:

```bash
curl -fsS https://api.engineiq.co.za/health
curl -fsS https://api.engineiq.co.za/security
curl -fsSI https://engineiq.co.za | head -n 5
curl -fsSI https://app.engineiq.co.za | head -n 5
```

Use **`scripts/verify-deployment.sh`** on the server if included in the repo (public health + static portal routes).

**Golden-four tenant API** (after **`demo-tenant-state.local.env`** is filled on your laptop):

```bash
chmod +x scripts/verify-golden-four-api.sh
./scripts/verify-golden-four-api.sh
```

Confirms **`X-Api-Key`** + **`/status`**, **`/account`**, **`/jobs`** for each persona still match production (**§11.2**: only the installation-linked tenant receives live PR webhooks; others may show empty jobs until they have their own install).

---

## 10. Internal admin (operators only)

URL on server: **`http://127.0.0.1:8081`**

Primary UI (React): **`http://127.0.0.1:8081/admin`** — sign in with operator credentials; the session stores the `Authorization` header client-side for **`/api/v1/admin/*`** calls (no browser-native Basic dialog).

From your machine:

```bash
ssh -L 8081:127.0.0.1:8081 root@<HETZNER_IP>
# browser: http://127.0.0.1:8081/admin
```

Credentials: `ENGINEIQ_ADMIN_USERNAME` / `ENGINEIQ_ADMIN_PASSWORD` from `.env`.

JSON smoke (`Authorization: Basic …` on **`/api/v1/admin`** only; **`/admin`** static shell is unauthenticated):

```bash
curl -fsS -u "$ENGINEIQ_ADMIN_USERNAME:$ENGINEIQ_ADMIN_PASSWORD" http://127.0.0.1:8081/api/v1/admin/health
curl -fsS -u "$ENGINEIQ_ADMIN_USERNAME:$ENGINEIQ_ADMIN_PASSWORD" http://127.0.0.1:8081/api/v1/admin/metrics
```

### 10.1 Local parity with production (recommended)

**Goal:** Same **Dockerfiles**, **`docker-compose.yml` env keys**, and **migration step** as deploy — without starting API, worker, Caddy, marketing, or portal.

| Script | Purpose |
|--------|---------|
| **`scripts/local-stack-admin-parity.sh`** | Starts **postgres** + **rabbitmq**, runs **`engineiq-migrator`**, builds/runs **`engineiq-admin`** (`Dockerfile.admin` includes the React build). Uses `.env` (`POSTGRES_PASSWORD`, `RABBITMQ_PASSWORD`, `ENGINEIQ_ADMIN_PASSWORD`, etc.). **Defaults to local `docker compose build`** — it does **not** follow deploy’s **`SKIP_PULL`** from `.env` (which often points at placeholder **`ENGINEIQ_REGISTRY`** and fails with “denied”). To pull prebuilt images instead: **`ADMIN_PARITY_PULL_IMAGES=1`** (and **`docker login`** to your registry if private). |
| **`scripts/sync-admin-ui-wwwroot.sh`** | **`npm ci`** + **`npm run build`** in **`web/admin-ui`**, copies **`dist`** → **`src/EngineIQ.Admin/wwwroot/admin`**. Use before **`dotnet run`** on **`EngineIQ.Admin`** so the SPA matches the Docker image output. |

From repo root (Git Bash / Linux / macOS):

```bash
chmod +x scripts/local-stack-admin-parity.sh scripts/sync-admin-ui-wwwroot.sh

# A — Docker-shaped admin (matches pushed image build path; binds 127.0.0.1:8081)
./scripts/local-stack-admin-parity.sh

# B — dotnet run / IDE: stop the engineiq-admin container first (same port), sync SPA, align Postgres/Rabbit env with .env
./scripts/sync-admin-ui-wwwroot.sh
dotnet run --project src/EngineIQ.Admin/EngineIQ.Admin.csproj
```

---

## 11. Tenant onboarding & GitHub installations

### 11.1 How real clients onboard (you never touch their GitHub)

Production behaviour matches standard GitHub App SaaS:

1. Customer registers (**marketing sign-up** or **`POST /api/v1/onboarding/register`** with **`dpa_accepted: true`**).
2. EngineIQ creates **one tenant row**, emails **install URL** + **API key** (once).
3. Customer clicks **Install GitHub App** and selects **their** organisation when GitHub prompts.
4. GitHub sends **`installation`** events; EngineIQ binds **`git_hub_app_installation_id`** to **that tenant**.
5. PR webhooks on repos covered by that installation resolve to **that tenant** only (`fn_resolve_tenant_by_installation`).

No EngineIQ engineer needs access to the customer’s GitHub — installs are self-serve.

### 11.2 Installation ID uniqueness (internal testing vs production)

The database enforces a **unique index** on **`tenants.git_hub_app_installation_id`**.

Consequences:

| Scenario | Result |
|----------|--------|
| **Production clients** — each org installs the app once | Distinct installation IDs → **one tenant per customer org**. Correct forever. |
| **Codist internal testing** — multiple tenant rows (four personas) but **one** GitHub org or account with **one** installation ID | **Only one** tenant row may hold that **`git_hub_app_installation_id`**. That tenant receives **live webhook reviews** for repos under that installation. The **other** persona tenants remain valid for **portal / admin / support demos** (log in with each **`tenant_id` + `api_key`**) but **will not** receive PR events from that installation until they have **their own** install (e.g. separate GitHub org per product). |

**Practical recommendation**

- **Treat the four registrations below as permanent “demo clients”** for UX and dashboards; store **`tenant_id` / `api_key`** in **`scripts/demo-tenant-state.local.env`** (copy from **`scripts/demo-tenant-state.example.env`**, gitignored).
- **Pick one** persona (or a dedicated **Codist** tenant) as the single webhook-linked row when all repos share **one** installation.
- **Production-shaped split:** move each product under its **own GitHub organisation** so each org gets its **own** installation → four tenants, four installs, full realism.

### 11.3 Codist internal demo tenants (“golden four”)

These four **`contact_email`** values identify **standing Codist product/integration tenants**, not disposable QA fixtures:

| Persona | `company_name` | `email` (unique in DB) | `github_org` (stored slug) |
|---------|----------------|-------------------------|----------------------------|
| Mybillable | Mybillable | `hello@mybillable.co.za` | `mybillable` |
| Therecord | Therecord | `hello@therecord.co.za` | `therecord` |
| Skillbay | Skillbay | `hello@skillbay.co.za` | `skillbay` |
| War Room | War Room | `technical@codist.co.za` | `warroom` |

Repositories under active development receive PRs continuously; portal/admin/support features **must keep behaving for these UUIDs** as the platform grows.

**Operational posture**

- **Preserve rows** across releases (included in normal Postgres backups). Do **not** bulk-delete these tenants when “cleaning” data unless you are deliberately resetting an entire environment and will recreate them immediately after.
- **Prefer suspension over deletion:** internal **`Suspended`** tenant blocks new webhook enqueue without destroying onboarding/API-key continuity — restore when testing resumes.
- **Credentials:** mirror **`tenant_id`** + **`api_key`** into **`scripts/demo-tenant-state.local.env`** (copy from **`demo-tenant-state.example.env`**) so everyone aligns portal demos with the same personas **years later**.
- **Bootstrap-only registration script:** **`scripts/register-internal-demo-tenants.sh`** matches onboarding payloads for **greenfield** DBs; **it fails if those emails already exist**. Long-lived environments rely on **migration / backup**, not re-registration churn.

```bash
chmod +x scripts/register-internal-demo-tenants.sh
ENGINEIQ_API_URL=https://api.engineiq.co.za ./scripts/register-internal-demo-tenants.sh
```

### 11.4 Production-style onboarding (four separate organisations)

Target shape when each brand has **its own GitHub org** (matches §2.1 checklist):

| # | GitHub org slug (`github_org`) | Suggested company display name | Example operator email |
|---|-------------------------------|--------------------------------|-------------------------|
| 1 | `therecord` | The Record | `ops+therecord@yourdomain.co.za` |
| 2 | `billable` | Billable | `ops+billable@yourdomain.co.za` |
| 3 | `war-room` | War Room | `ops+warroom@yourdomain.co.za` |
| 4 | `skillbay` | Skillbay | `ops+skillbay@yourdomain.co.za` |

**Per org — repeat:**

1. Open **`https://engineiq.co.za/sign-up`** (or call **`POST /api/v1/onboarding/register`**).
2. **`dpa_accepted: true`** required.
3. Copy **`tenant_id`** + **`api_key`**; complete **Install GitHub App** for **that org only**.
4. Portal login: **`https://app.engineiq.co.za/login`** with that tenant’s UUID + API key.

**API equivalent (replace email/domain):**

```bash
curl -sS -X POST "https://api.engineiq.co.za/api/v1/onboarding/register" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "ops+billable@yourdomain.co.za",
    "company_name": "Billable",
    "plan": "Growth",
    "github_org": "billable",
    "dpa_accepted": true
  }'
```

Validate each org can open a test PR and receive a review comment (Worker + Anthropic + GitHub permissions).

---

## 12. Operations cheatsheet

| Task | Command / note |
|------|----------------|
| **Tenant lost API key** | **`RUNBOOK.md`** — SSH tunnel → admin **`POST .../rotate-key`** → deliver new key once |
| Logs API | `docker compose logs -f engineiq-api` |
| Logs worker | `docker compose logs -f engineiq-worker` |
| Restart stack | `docker compose --profile platform restart` |
| DB backup | `docker compose exec postgres pg_dump -U engineiq engineiq > backup.sql` (golden-four tenants in **`§11.3`** must survive restores — continuous PR traffic) |
| Update deploy | `git pull && ./deploy.sh` |

---

## 13. Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| Let’s Encrypt fails | DNS not pointing to this server; port 80 blocked; rate limit — check `docker compose logs caddy` |
| Webhook 401 | Wrong `GITHUB_WEBHOOK_SECRET` vs GitHub App settings |
| Reviews never run | Worker down, RabbitMQ creds wrong, Anthropic key invalid |
| Portal CORS errors | API `Cors:AllowedOrigins` must include portal/marketing origins (compose sets prod hosts) |
| Docker build `mcr.microsoft.com` **403** | Dockerfiles default to `public.ecr.aws/dotnet/*:8.0`; re-run workflow or `docker pull public.ecr.aws/dotnet/sdk:8.0` on the host |
| Admin PG errors | Ensure migrator ran; check `postgres` health |

---

## 14. Handover criteria (definition of done)

- [ ] DNS A records for `@`, `www`, `api`, `app` → Hetzner IP
- [ ] `curl https://api.engineiq.co.za/health` returns JSON
- [ ] GitHub App webhook delivered once (check API logs)
- [ ] All compose services healthy: `docker compose ps`
- [ ] Demo tenants registered (**§11.3**) or four production org tenants (**§11.4**) with credentials captured in **`demo-tenant-state.local.env`** (operators); understand **§11.2** if one GitHub installation feeds multiple personas
- [ ] Test PR reviewed on at least one repo (installation-linked tenant per **§11.2**)
- [ ] Operator can SSH tunnel to admin and list tenants
- [ ] `.env` backed up to a **secrets vault** (not git)

---

**Support note:** product copy still mentions legacy Azure SA North in places; production runtime for this path is **Docker on Hetzner** with disclosure strings configurable under **`Trust:`** in configuration.
