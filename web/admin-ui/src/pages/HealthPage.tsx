import { useEffect, useState } from 'react'
import type { AdminHealth } from '../api/types'
import { UnauthorizedError, apiJson, redirectToLogin } from '../api/client'

export function HealthPage() {
  const [health, setHealth] = useState<AdminHealth | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const h = await apiJson<AdminHealth>('/api/v1/admin/health')
        if (!cancelled) setHealth(h)
      } catch (e) {
        if (e instanceof UnauthorizedError) {
          redirectToLogin()
          return
        }
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load health.')
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
          <h1 className="eq-h1">Health</h1>
          <p className="eq-text-sm eq-text-muted">Light connectivity checks only.</p>
        </div>
      </header>

      {error ? <p className="admin-error">{error}</p> : null}

      {!health && !error ? <p className="eq-text-muted">Loading…</p> : null}

      {health ? (
        <div className="eq-grid-2">
          <div className="eq-card" style={{ padding: 16 }}>
            <div className="eq-text-sm eq-text-muted">PostgreSQL</div>
            <div className="eq-text-lg" style={{ marginTop: 8 }}>
              {health.database === 'ok' ? (
                <span style={{ color: 'var(--eq-green)' }}>ok</span>
              ) : (
                <span style={{ color: 'var(--eq-red)' }}>error</span>
              )}
            </div>
          </div>
          <div className="eq-card" style={{ padding: 16 }}>
            <div className="eq-text-sm eq-text-muted">RabbitMQ</div>
            <div className="eq-text-lg" style={{ marginTop: 8 }}>
              {health.rabbitMq === 'ok' ? (
                <span style={{ color: 'var(--eq-green)' }}>ok</span>
              ) : (
                <span style={{ color: 'var(--eq-red)' }}>error</span>
              )}
            </div>
          </div>
        </div>
      ) : null}
    </>
  )
}
