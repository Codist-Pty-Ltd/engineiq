# EngineIQ — Engineering intelligence platform

Architecture-aware **AI pull request review** for teams standardising on **Anthropic Claude**, tenant isolation, and a **public trust surface** (`GET /security`, audit metadata).

**Source repository:** [github.com/Codist-Pty-Ltd/engineiq](https://github.com/Codist-Pty-Ltd/engineiq)

---

## Documentation map

| Document | Purpose |
|----------|---------|
| **[DEPLOYMENT.md](./DEPLOYMENT.md)** | **Production handover:** Hetzner, Docker, DNS (cPanel), TLS, secrets, first `./deploy.sh`, onboarding **four** GitHub orgs (therecord, billable, war-room, skillbay), verification, ops. |
| **[RUNBOOK.md](./RUNBOOK.md)** | **Operator procedures:** tenant API key loss / rotation via internal admin. |
| [.env.example](./.env.example) | Every production env var (copy → `.env`, never commit `.env`). |
| [.cursor/rules/engineiq.mdc](./.cursor/rules/engineiq.mdc) | Engineering constraints (no source persistence, tenant isolation, model choice). |

---

## What ships in this repo

| Layer | Stack |
|-------|--------|
| API | .NET 8 — webhooks, onboarding, tenant API, `GET /security` |
| Worker | .NET 8 — RabbitMQ consumer → in-memory diff → Claude → GitHub comment |
| Admin | .NET 8 + React (`/admin`) — internal ops UI (`127.0.0.1:8081` in Docker) |
| Marketing | Next.js 14 (static export) — `engineiq.co.za` |
| Portal | Next.js 14 (static export) — `app.engineiq.co.za` |
| Edge | Caddy 2 — Let’s Encrypt, routes to marketing / portal / API |
| Data | PostgreSQL 16, RabbitMQ 3, Redis 7 |
| Reporting | Python 3.12 + supercronic (scheduled jobs) |

**Public URLs (production):**

- `https://engineiq.co.za` — marketing  
- `https://app.engineiq.co.za` — client portal (tenant UUID + API key)  
- `https://api.engineiq.co.za` — API & GitHub webhook `POST /webhooks/github`

---

## Quick start — production (empty server → live)

1. Follow **[DEPLOYMENT.md](./DEPLOYMENT.md)** end-to-end (DNS → Hetzner → `.env` → `./deploy.sh`).
2. Configure **one GitHub App** with webhook `https://api.engineiq.co.za/webhooks/github`.
3. Register tenants: **production-shaped** — one per GitHub org (**therecord**, **billable**, **war-room**, **skillbay**); **Codist golden four** — bootstrap via **`scripts/register-internal-demo-tenants.sh`** only on greenfield DBs; treat those tenant rows as **permanent** (ongoing PRs); capture keys in **`scripts/demo-tenant-state.local.env`** (see **DEPLOYMENT.md §11** — unique **`installation_id`** per tenant).
4. Run **`scripts/verify-deployment.sh`** from any machine once DNS/TLS are green.

**Images:** push to **`ghcr.io/codist-pty-ltd/engineiq`** via GitHub Actions ([`.github/workflows/publish-images.yml`](./.github/workflows/publish-images.yml)); set `ENGINEIQ_REGISTRY` in `.env` to match. Or set `SKIP_PULL=1` and build on the server (see DEPLOYMENT).

---

## Quick start — local development

1. **Dependencies:** .NET 8 SDK, Docker, Node 20 (for `web/*`).
2. Copy **`.env.example` → `.env`** at repo root (compose **requires** `POSTGRES_PASSWORD` and `RABBITMQ_PASSWORD` for Postgres/RabbitMQ).
3. `docker compose up -d` — starts **postgres**, **redis**, **rabbitmq** only (no `platform` profile).
4. Apply migrations:

   ```bash
   dotnet ef database update --project src/EngineIQ.Infrastructure/EngineIQ.Infrastructure.csproj --startup-project src/EngineIQ.API/EngineIQ.API.csproj
   ```

5. Configure **user secrets** for `src/EngineIQ.API` and `src/EngineIQ.Worker` (`GitHub:*`, `Anthropic:*`, `Postgres:ConnectionString`, `RabbitMq:ConnectionString` — Rabbit URL must match `.env`, usually `amqp://engineiq:<password>@localhost:5672/`).

6. Run:

   ```bash
   dotnet run --project src/EngineIQ.API          # http://localhost:5056
   dotnet run --project src/EngineIQ.Worker
   ```

7. Optional UI: `web/marketing` (3000), `web/portal` (3001). Use **`POST /api/v1/onboarding/register`** with `dpa_accepted: true` to obtain **tenant_id** + **api_key** (seed tenant `f1111111-…` has **no** API key — do not use it for portal login).

**Internal admin locally:**

```bash
dotnet run --project src/EngineIQ.Admin   # http://127.0.0.1:8081 — see appsettings.Development.json for Basic Auth defaults
```

---

## Security principles

- Customer **source / full diffs are not persisted**; processing is **in-memory** with explicit disclosure on PR comments and **`GET /security`**.
- **Tenant isolation** in PostgreSQL (RLS + `set_config` session tenant).
- **Secrets** only via environment / secret managers — never committed.

---

## Licence / product

Proprietary — EngineIQ. Configure branding and legal URLs under **`Trust:`** and marketing pages as needed.
# Tue May 19 07:07:52 PM UTC 2026
