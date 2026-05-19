import { type FormEvent, useEffect, useMemo, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'

const LS_API_BASE = 'engineiq_admin_support_api_base'

const GOLDEN_FOUR = [
  { persona: 'Mybillable', email: 'hello@mybillable.co.za', githubOrg: 'mybillable' },
  { persona: 'Therecord', email: 'hello@therecord.co.za', githubOrg: 'therecord' },
  { persona: 'Skillbay', email: 'hello@skillbay.co.za', githubOrg: 'skillbay' },
  { persona: 'War Room', email: 'technical@codist.co.za', githubOrg: 'warroom' },
] as const

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i

function normalizeApiBase(raw: string): string {
  const t = raw.trim().replace(/\/+$/, '')
  return t
}

export function SupportPage() {
  const navigate = useNavigate()
  const envDefault = (import.meta.env.VITE_ENGINEIQ_API_BASE_URL as string | undefined)?.trim()
  const [apiBase, setApiBase] = useState(() => {
    try {
      const saved = localStorage.getItem(LS_API_BASE)
      if (saved) return normalizeApiBase(saved)
    } catch {
      /* ignore */
    }
    return normalizeApiBase(envDefault || 'https://api.engineiq.co.za')
  })
  const [tenantJump, setTenantJump] = useState('')
  const [toast, setToast] = useState<string | null>(null)

  useEffect(() => {
    try {
      localStorage.setItem(LS_API_BASE, apiBase)
    } catch {
      /* ignore */
    }
  }, [apiBase])

  const snippets = useMemo(() => {
    const base = apiBase || 'https://api.engineiq.co.za'
    return {
      health: `curl -fsS "${base}/health"`,
      security: `curl -fsS "${base}/security"`,
    }
  }, [apiBase])

  async function copyLine(text: string, label: string) {
    try {
      await navigator.clipboard.writeText(text)
      setToast(`Copied ${label}`)
      setTimeout(() => setToast(null), 2000)
    } catch {
      setToast('Copy failed — select text manually')
      setTimeout(() => setToast(null), 2500)
    }
  }

  function onJumpTenant(e: FormEvent) {
    e.preventDefault()
    const id = tenantJump.trim()
    if (!UUID_RE.test(id)) {
      setToast('Enter a valid tenant UUID')
      setTimeout(() => setToast(null), 2500)
      return
    }
    navigate(`/tenants/${id}`)
  }

  return (
    <>
      <header className="eq-pagehead">
        <div>
          <h1 className="eq-h1">Support</h1>
          <p className="eq-text-sm eq-text-muted">
            Operator runbooks, API snippets, and Codist demo personas. Nothing here persists customer secrets — paste keys only
            into your terminal.
          </p>
        </div>
      </header>

      {toast ? (
        <div className="eq-toast eq-toast--info eq-text-sm" style={{ marginBottom: 16 }}>
          {toast}
        </div>
      ) : null}

      <div className="admin-stack" style={{ gap: 20 }}>
        <section className="eq-card" style={{ padding: 16 }}>
          <h2 className="eq-h3">Public API base URL</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
            Used only for curl snippets in this browser (stored locally). Local API example:{' '}
            <span className="eq-font-mono">http://127.0.0.1:5000</span>. Optional build-time default:{' '}
            <span className="eq-font-mono">VITE_ENGINEIQ_API_BASE_URL</span>.
          </p>
          <div className="eq-input-wrap" style={{ marginTop: 12 }}>
            <label className="eq-text-xs eq-text-dim" htmlFor="apiBase">
              Base URL (no trailing slash)
            </label>
            <input
              id="apiBase"
              className="eq-input eq-font-mono"
              value={apiBase}
              onChange={(e) => setApiBase(normalizeApiBase(e.target.value))}
              spellCheck={false}
            />
          </div>
        </section>

        <section className="eq-card" style={{ padding: 16 }}>
          <h2 className="eq-h3">Jump to tenant</h2>
          <form className="eq-row" style={{ marginTop: 12, gap: 10, flexWrap: 'wrap' }} onSubmit={onJumpTenant}>
            <input
              className="eq-input eq-font-mono"
              style={{ flex: '1 1 260px', minWidth: 200 }}
              placeholder="Tenant UUID"
              value={tenantJump}
              onChange={(e) => setTenantJump(e.target.value)}
              spellCheck={false}
              aria-label="Tenant UUID"
            />
            <button type="submit" className="eq-btn eq-btn--primary">
              Open in Admin
            </button>
          </form>
        </section>

        <section className="eq-card" style={{ padding: 16 }}>
          <h2 className="eq-h3">Public API smoke</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
            Unauthenticated checks against the marketing-facing API host.
          </p>
          <ul className="admin-stack" style={{ marginTop: 12, listStyle: 'none', padding: 0 }}>
            <SnippetRow
              label="GET /health"
              text={snippets.health}
              onCopy={() => void copyLine(snippets.health, 'health')}
            />
            <SnippetRow
              label="GET /security"
              text={snippets.security}
              onCopy={() => void copyLine(snippets.security, 'security')}
            />
          </ul>
        </section>

        <section className="eq-card" style={{ padding: 16 }}>
          <h2 className="eq-h3">Portal tenant API</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
            Replace <span className="eq-font-mono">YOUR_API_KEY</span> and{' '}
            <span className="eq-font-mono">TENANT_UUID</span>. Keys belong in{' '}
            <span className="eq-font-mono">scripts/demo-tenant-state.local.env</span> (gitignored), never committed.
          </p>
          <ul className="admin-stack" style={{ marginTop: 12, listStyle: 'none', padding: 0 }}>
            <SnippetRow
              label="GET …/tenant/{id}/status"
              text={`curl -fsS -H "X-Api-Key: YOUR_API_KEY" \\
  "${normalizeApiBase(apiBase || 'https://api.engineiq.co.za')}/api/v1/tenant/TENANT_UUID/status"`}
              onCopy={() =>
                void copyLine(
                  `curl -fsS -H "X-Api-Key: YOUR_API_KEY" "${normalizeApiBase(apiBase || 'https://api.engineiq.co.za')}/api/v1/tenant/TENANT_UUID/status"`,
                  'portal status',
                )
              }
            />
          </ul>
          <p className="eq-text-xs eq-text-dim" style={{ marginTop: 12 }}>
            Full golden-four check:{' '}
            <span className="eq-font-mono">scripts/verify-golden-four-api.sh</span> (loads{' '}
            <span className="eq-font-mono">demo-tenant-state.local.env</span>).
          </p>
        </section>

        <section className="eq-card" style={{ padding: 16 }}>
          <h2 className="eq-h3">Codist demo personas (“golden four”)</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
            Rows are long-lived integration tenants — align portal demos with{' '}
            <span className="eq-font-mono">DEPLOYMENT.md §11.3</span>. Only one tenant may hold a given GitHub{' '}
            <span className="eq-font-mono">installation_id</span> — see §11.2.
          </p>
          <div className="eq-table-wrap" style={{ marginTop: 12 }}>
            <table className="eq-table eq-text-sm">
              <thead>
                <tr>
                  <th>Persona</th>
                  <th>Contact email</th>
                  <th>GitHub org slug</th>
                </tr>
              </thead>
              <tbody>
                {GOLDEN_FOUR.map((row) => (
                  <tr key={row.persona}>
                    <td>{row.persona}</td>
                    <td className="eq-font-mono eq-text-xs">{row.email}</td>
                    <td className="eq-font-mono">{row.githubOrg}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>

        <section className="eq-card" style={{ padding: 16 }}>
          <h2 className="eq-h3">Incident response</h2>
          <ul className="eq-text-sm admin-stack" style={{ marginTop: 12, paddingLeft: 18 }}>
            <li>
              Prefer <Link to="/tenants">tenant suspension</Link> over deleting rows — webhooks stop enqueueing while
              onboarding and API keys remain intact.
            </li>
            <li>
              Failed queue work: <Link to="/jobs">Jobs & DLQ</Link> for PostgreSQL failed jobs and RabbitMQ DLQ peek/retry.
            </li>
            <li>
              Trust disclosures on GitHub PR comments link to <span className="eq-font-mono">GET /security</span> on the
              public API — keep that endpoint accurate.
            </li>
          </ul>
        </section>
      </div>
    </>
  )
}

function SnippetRow({
  label,
  text,
  onCopy,
}: {
  label: string
  text: string
  onCopy: () => void
}) {
  return (
    <li className="eq-card" style={{ padding: 12, background: 'var(--eq-surface)' }}>
      <div className="eq-pagehead" style={{ marginBottom: 8 }}>
        <span className="eq-text-sm">{label}</span>
        <button type="button" className="eq-btn eq-btn--secondary eq-text-xs" onClick={onCopy}>
          Copy
        </button>
      </div>
      <pre
        className="admin-code eq-text-xs"
        style={{
          margin: 0,
          whiteSpace: 'pre-wrap',
          wordBreak: 'break-word',
          color: 'var(--eq-text-muted)',
        }}
      >
        {text}
      </pre>
    </li>
  )
}
