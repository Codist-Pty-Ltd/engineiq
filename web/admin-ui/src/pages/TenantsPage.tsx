import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { AdminTenantRow } from '../api/types'
import { UnauthorizedError, apiJson, redirectToLogin } from '../api/client'

export function TenantsPage() {
  const [rows, setRows] = useState<AdminTenantRow[] | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const list = await apiJson<AdminTenantRow[]>('/api/v1/admin/tenants')
        if (!cancelled) setRows(list)
      } catch (e) {
        if (e instanceof UnauthorizedError) {
          redirectToLogin()
          return
        }
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load tenants.')
      }
    })()
    return () => {
      cancelled = true
    }
  }, [])

  return (
    <>
      <header className="eq-pagehead">
        <div>
          <h1 className="eq-h1">Tenants</h1>
          <p className="eq-text-sm eq-text-muted">All tenant rows (cross-tenant listing uses bootstrap context).</p>
        </div>
      </header>

      {error ? <p className="admin-error">{error}</p> : null}

      {!rows && !error ? <p className="eq-text-muted">Loading…</p> : null}

      {rows ? (
        <div className="eq-table-wrap">
          <table className="eq-table">
            <thead>
              <tr>
                <th>Name</th>
                <th>Plan</th>
                <th>Status</th>
                <th>PR jobs</th>
                <th>MRR ZAR</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {rows.map((t) => (
                <tr key={t.id}>
                  <td>{t.name}</td>
                  <td className="eq-font-mono">{t.plan}</td>
                  <td>{t.status}</td>
                  <td>{t.prCount}</td>
                  <td>{t.mrrContributionZar.toFixed(2)}</td>
                  <td>
                    <Link className="eq-btn eq-btn--secondary eq-text-xs" to={`/tenants/${t.id}`}>
                      Open
                    </Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}
    </>
  )
}
