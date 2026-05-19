import { type FormEvent, useEffect, useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import type { AdminFindingRow, AdminTenantDetail } from '../api/types'
import { UnauthorizedError, apiFetch, apiJson, redirectToLogin } from '../api/client'

export function TenantDetailPage() {
  const { tenantId } = useParams<{ tenantId: string }>()
  const [detail, setDetail] = useState<AdminTenantDetail | null>(null)
  const [findings, setFindings] = useState<AdminFindingRow[] | null>(null)
  const [plan, setPlan] = useState('')
  const [flags, setFlags] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    if (!tenantId) return
    let cancelled = false
    ;(async () => {
      try {
        const d = await apiJson<AdminTenantDetail>(`/api/v1/admin/tenants/${tenantId}`)
        if (!cancelled) {
          setDetail(d)
          setPlan(d.plan)
          setFlags(d.featureFlagsJson ?? '')
        }
      } catch (e) {
        if (e instanceof UnauthorizedError) {
          redirectToLogin()
          return
        }
        if (!cancelled) setError(e instanceof Error ? e.message : 'Tenant not found.')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [tenantId])

  async function loadFindings() {
    if (!tenantId) return
    setBusy(true)
    setError(null)
    try {
      const list = await apiJson<AdminFindingRow[]>(
        `/api/v1/admin/tenants/${tenantId}/findings?take=100`,
      )
      setFindings(list)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load findings.')
    } finally {
      setBusy(false)
    }
  }

  async function setSuspended(next: boolean) {
    if (!tenantId) return
    setBusy(true)
    setError(null)
    try {
      const res = await apiFetch(`/api/v1/admin/tenants/${tenantId}/suspend`, {
        method: 'POST',
        body: JSON.stringify({ suspended: next }),
      })
      if (!res.ok) throw new Error(await res.text())
      const d = await apiJson<AdminTenantDetail>(`/api/v1/admin/tenants/${tenantId}`)
      setDetail(d)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Update failed.')
    } finally {
      setBusy(false)
    }
  }

  async function savePlan(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    if (!tenantId) return
    setBusy(true)
    setError(null)
    try {
      const res = await apiFetch(`/api/v1/admin/tenants/${tenantId}/plan`, {
        method: 'POST',
        body: JSON.stringify({
          plan,
          featureFlagsJson: flags.trim() === '' ? null : flags,
        }),
      })
      if (!res.ok) throw new Error(await res.text())
      const d = await apiJson<AdminTenantDetail>(`/api/v1/admin/tenants/${tenantId}`)
      setDetail(d)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
    } finally {
      setBusy(false)
    }
  }

  if (!tenantId) return <p className="admin-error">Missing tenant id.</p>

  return (
    <>
      <header className="eq-pagehead">
        <div>
          <Link className="eq-text-sm eq-text-muted" to="/tenants">
            ← Tenants
          </Link>
          <h1 className="eq-h1" style={{ marginTop: 8 }}>
            {detail?.name ?? 'Tenant'}
          </h1>
          <p className="eq-text-xs eq-font-mono eq-text-dim">{tenantId}</p>
        </div>
        <div className="eq-row" style={{ gap: 8 }}>
          <button
            type="button"
            className="eq-btn eq-btn--secondary"
            disabled={busy || !detail}
            onClick={() => void setSuspended(true)}
          >
            Suspend
          </button>
          <button
            type="button"
            className="eq-btn eq-btn--secondary"
            disabled={busy || !detail}
            onClick={() => void setSuspended(false)}
          >
            Resume
          </button>
        </div>
      </header>

      {error ? <p className="admin-error">{error}</p> : null}

      {!detail ? (
        <p className="eq-text-muted">Loading…</p>
      ) : (
        <div className="admin-stack">
          <div className="eq-card" style={{ padding: 16 }}>
            <div className="eq-grid-2">
              <div>
                <div className="eq-text-xs eq-text-dim">Status</div>
                <div className="eq-text-md">{detail.status}</div>
              </div>
              <div>
                <div className="eq-text-xs eq-text-dim">Plan</div>
                <div className="eq-text-md eq-font-mono">{detail.plan}</div>
              </div>
              <div>
                <div className="eq-text-xs eq-text-dim">GitHub org</div>
                <div className="eq-text-md">{detail.gitHubOrgLogin ?? '—'}</div>
              </div>
              <div>
                <div className="eq-text-xs eq-text-dim">Installation ID</div>
                <div className="eq-text-md eq-font-mono">
                  {detail.gitHubAppInstallationId ?? '—'}
                </div>
              </div>
              <div>
                <div className="eq-text-xs eq-text-dim">Contact</div>
                <div className="eq-text-md">{detail.contactEmail ?? '—'}</div>
              </div>
              <div>
                <div className="eq-text-xs eq-text-dim">Created</div>
                <div className="eq-text-md">{new Date(detail.createdAt).toLocaleString()}</div>
              </div>
            </div>
          </div>

          <form className="eq-card" style={{ padding: 16 }} onSubmit={(e) => void savePlan(e)}>
            <h2 className="eq-h3">Plan & flags</h2>
            <div className="eq-input-wrap" style={{ marginTop: 12 }}>
              <label className="eq-text-xs eq-text-dim" htmlFor="plan">
                Plan
              </label>
              <input id="plan" className="eq-input eq-font-mono" value={plan} onChange={(e) => setPlan(e.target.value)} />
            </div>
            <div className="eq-input-wrap">
              <label className="eq-text-xs eq-text-dim" htmlFor="flags">
                Feature flags JSON
              </label>
              <textarea
                id="flags"
                className="eq-input eq-font-mono"
                rows={5}
                value={flags}
                onChange={(e) => setFlags(e.target.value)}
              />
            </div>
            <button type="submit" className="eq-btn eq-btn--primary" disabled={busy}>
              Save
            </button>
          </form>

          <div className="eq-card" style={{ padding: 16 }}>
            <div className="eq-pagehead" style={{ marginBottom: 0 }}>
              <h2 className="eq-h3">Findings</h2>
              <button type="button" className="eq-btn eq-btn--secondary" disabled={busy} onClick={() => void loadFindings()}>
                Load recent
              </button>
            </div>
            {findings ? (
              <div className="eq-table-wrap" style={{ marginTop: 12 }}>
                <table className="eq-table eq-text-xs">
                  <thead>
                    <tr>
                      <th>Severity</th>
                      <th>File</th>
                      <th>Message</th>
                      <th>When</th>
                    </tr>
                  </thead>
                  <tbody>
                    {findings.map((f) => (
                      <tr key={f.id}>
                        <td>{f.severity}</td>
                        <td className="eq-font-mono">
                          {f.filePath}:{f.lineNumber ?? '—'}
                        </td>
                        <td>{f.message}</td>
                        <td>{new Date(f.createdAt).toLocaleString()}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            ) : (
              <p className="eq-text-sm eq-text-muted" style={{ marginTop: 12 }}>
                Load tenant-scoped findings (metadata only — no source persistence).
              </p>
            )}
          </div>
        </div>
      )}
    </>
  )
}
