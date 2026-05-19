import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import type { AdminPlatformMetrics } from '../api/types'
import { UnauthorizedError, apiJson, redirectToLogin } from '../api/client'

export function DashboardPage() {
  const [metrics, setMetrics] = useState<AdminPlatformMetrics | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    ;(async () => {
      try {
        const m = await apiJson<AdminPlatformMetrics>('/api/v1/admin/metrics')
        if (!cancelled) setMetrics(m)
      } catch (e) {
        if (e instanceof UnauthorizedError) {
          redirectToLogin()
          return
        }
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load metrics.')
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
          <h1 className="eq-h1">Overview</h1>
          <p className="eq-text-sm eq-text-muted">Platform metrics from PostgreSQL (tenant-scoped queries).</p>
        </div>
        <Link className="eq-btn eq-btn--secondary" to="/health">
          Health check
        </Link>
      </header>

      {error ? <p className="admin-error">{error}</p> : null}

      {!metrics && !error ? (
        <p className="eq-text-muted eq-text-sm">Loading…</p>
      ) : metrics ? (
        <div className="eq-grid-3">
          <MetricCard
            label="PRs completed today"
            value={String(metrics.prsReviewedToday)}
          />
          <MetricCard
            label="Avg latency today (ms)"
            value={metrics.avgReviewLatencyMsToday?.toFixed(0) ?? '—'}
          />
          <MetricCard
            label="Token cost (ZAR) today"
            value={metrics.tokenCostZarToday.toFixed(2)}
          />
          <MetricCard label="MRR (ZAR)" value={metrics.revenueMrrZar.toFixed(2)} />
        </div>
      ) : null}

      <section style={{ marginTop: 28 }}>
        <h2 className="eq-h3">Shortcuts</h2>
        <div className="eq-row" style={{ marginTop: 12, flexWrap: 'wrap', gap: 12 }}>
          <Link className="eq-btn eq-btn--secondary" to="/tenants">
            Tenants
          </Link>
          <Link className="eq-btn eq-btn--secondary" to="/jobs">
            Failed jobs & DLQ
          </Link>
        </div>
      </section>
    </>
  )
}

function MetricCard({ label, value }: { label: string; value: string }) {
  return (
    <div className="eq-card" style={{ padding: 16 }}>
      <div className="eq-text-xs eq-text-dim">{label}</div>
      <div className="eq-text-2xl" style={{ marginTop: 6 }}>
        {value}
      </div>
    </div>
  )
}
