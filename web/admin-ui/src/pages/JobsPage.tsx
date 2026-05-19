import { useEffect, useState } from 'react'
import type { AdminFailedJobRow } from '../api/types'
import { UnauthorizedError, apiFetch, apiJson, redirectToLogin } from '../api/client'

export function JobsPage() {
  const [failed, setFailed] = useState<AdminFailedJobRow[] | null>(null)
  const [dlq, setDlq] = useState<string[] | null>(null)
  const [dlqIndex, setDlqIndex] = useState('0')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function refreshFailed() {
    setBusy(true)
    setError(null)
    try {
      const rows = await apiJson<AdminFailedJobRow[]>('/api/v1/admin/jobs/failed')
      setFailed(rows)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load jobs.')
    } finally {
      setBusy(false)
    }
  }

  async function refreshDlq() {
    setBusy(true)
    setError(null)
    try {
      const previews = await apiJson<string[]>('/api/v1/admin/jobs/dlq')
      setDlq(previews)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to peek DLQ.')
    } finally {
      setBusy(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const rows = await apiJson<AdminFailedJobRow[]>('/api/v1/admin/jobs/failed')
        if (!cancelled) setFailed(rows)
      } catch (e) {
        if (e instanceof UnauthorizedError) {
          redirectToLogin()
          return
        }
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load jobs.')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  async function retryDbJob(tenantId: string, jobId: string) {
    setBusy(true)
    setError(null)
    try {
      const res = await apiFetch(`/api/v1/admin/tenants/${tenantId}/jobs/${jobId}/retry`, {
        method: 'POST',
      })
      if (!res.ok) throw new Error(await res.text())
      await refreshFailed()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Retry failed.')
    } finally {
      setBusy(false)
    }
  }

  async function retryDlq() {
    const index = Number(dlqIndex)
    if (!Number.isFinite(index) || index < 0) {
      setError('DLQ index must be a non-negative number.')
      return
    }
    setBusy(true)
    setError(null)
    try {
      const res = await apiFetch('/api/v1/admin/jobs/dlq/retry', {
        method: 'POST',
        body: JSON.stringify({ index }),
      })
      if (!res.ok) throw new Error(await res.text())
      await refreshDlq()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'DLQ retry failed.')
    } finally {
      setBusy(false)
    }
  }

  return (
    <>
      <header className="eq-pagehead">
        <div>
          <h1 className="eq-h1">Jobs & DLQ</h1>
          <p className="eq-text-sm eq-text-muted">
            Failed rows from PostgreSQL and RabbitMQ DLQ peek/retry (internal tooling).
          </p>
        </div>
        <button type="button" className="eq-btn eq-btn--secondary" disabled={busy} onClick={() => void refreshFailed()}>
          Refresh failed
        </button>
      </header>

      {error ? <p className="admin-error">{error}</p> : null}

      <section style={{ marginBottom: 28 }}>
        <h2 className="eq-h3">Failed DB jobs</h2>
        {!failed ? (
          <p className="eq-text-muted eq-text-sm">Loading…</p>
        ) : failed.length === 0 ? (
          <p className="eq-text-muted eq-text-sm">No failed jobs.</p>
        ) : (
          <div className="eq-table-wrap" style={{ marginTop: 12 }}>
            <table className="eq-table eq-text-xs">
              <thead>
                <tr>
                  <th>Tenant</th>
                  <th>Repo</th>
                  <th>PR</th>
                  <th>When</th>
                  <th />
                </tr>
              </thead>
              <tbody>
                {failed.map((j) => (
                  <tr key={`${j.tenantId}:${j.jobId}`}>
                    <td>{j.tenantName}</td>
                    <td className="eq-font-mono">{j.repositoryFullName}</td>
                    <td>{j.prNumber}</td>
                    <td>{new Date(j.when).toLocaleString()}</td>
                    <td>
                      <button
                        type="button"
                        className="eq-btn eq-btn--secondary eq-text-xs"
                        disabled={busy}
                        onClick={() => void retryDbJob(j.tenantId, j.jobId)}
                      >
                        Republish
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="eq-card" style={{ padding: 16 }}>
        <div className="eq-pagehead" style={{ marginBottom: 0 }}>
          <h2 className="eq-h3">RabbitMQ DLQ</h2>
          <button type="button" className="eq-btn eq-btn--secondary" disabled={busy} onClick={() => void refreshDlq()}>
            Peek
          </button>
        </div>
        <p className="eq-text-xs eq-text-muted" style={{ marginTop: 8 }}>
          Previews are truncated. Retry drains the DLQ and republishes one message by index (same backend semantics as{' '}
          <span className="eq-font-mono">DlqRetryService</span>).
        </p>
        <div className="eq-row" style={{ marginTop: 12, gap: 12, flexWrap: 'wrap', alignItems: 'center' }}>
          <div className="eq-input-wrap">
            <label className="eq-text-xs eq-text-dim" htmlFor="dlqIdx">
              Index
            </label>
            <input
              id="dlqIdx"
              className="eq-input eq-font-mono"
              value={dlqIndex}
              onChange={(e) => setDlqIndex(e.target.value)}
            />
          </div>
          <button type="button" className="eq-btn eq-btn--primary" disabled={busy} onClick={() => void retryDlq()}>
            Retry at index
          </button>
        </div>
        {dlq !== null && dlq.length > 0 ? (
          <ul className="admin-stack" style={{ marginTop: 16 }}>
            {dlq.map((line, i) => (
              <li key={i} className="admin-code eq-card" style={{ padding: 10 }}>
                <span className="eq-text-dim">{i}: </span>
                {line}
              </li>
            ))}
          </ul>
        ) : dlq !== null && dlq.length === 0 ? (
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 16 }}>
            DLQ is empty.
          </p>
        ) : null}
      </section>
    </>
  )
}
