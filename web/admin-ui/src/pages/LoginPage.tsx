import { type FormEvent, useState } from 'react'
import { useLocation, useNavigate } from 'react-router-dom'
import { setAuthHeader } from '../api/client'

export function LoginPage() {
  const navigate = useNavigate()
  const location = useLocation()
  const from =
    typeof (location.state as { from?: string } | null)?.from === 'string'
      ? (location.state as { from: string }).from
      : '/'

  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function onSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault()
    setError(null)
    const fd = new FormData(e.currentTarget)
    const username = String(fd.get('username') ?? '')
    const password = String(fd.get('password') ?? '')
    if (!username || !password) {
      setError('Username and password are required.')
      return
    }

    const header = `Basic ${btoa(`${username}:${password}`)}`
    setBusy(true)
    try {
      const res = await fetch('/api/v1/admin/health', {
        headers: { Authorization: header, Accept: 'application/json' },
      })
      if (!res.ok) {
        setError(res.status === 401 ? 'Invalid credentials.' : `Login failed (${res.status}).`)
        return
      }
      setAuthHeader(header)
      navigate(from.startsWith('/login') ? '/' : from, { replace: true })
    } catch {
      setError('Could not reach the admin API (is the worker listening on 8081?).')
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="eq-app">
      <div className="eq-page" style={{ maxWidth: 420, margin: '0 auto', paddingTop: 48 }}>
        <div className="eq-card" style={{ padding: 24 }}>
          <h1 className="eq-h2">EngineIQ Admin</h1>
          <p className="eq-text-sm eq-text-muted">
            Sign in with the credentials from <span className="eq-font-mono">ENGINEIQ_ADMIN_USERNAME</span>{' '}
            / <span className="eq-font-mono">ENGINEIQ_ADMIN_PASSWORD</span>.
          </p>
          <form className="admin-stack" style={{ marginTop: 20 }} onSubmit={onSubmit}>
            <div className="eq-input-wrap">
              <label className="eq-text-xs eq-text-dim" htmlFor="username">
                Username
              </label>
              <input id="username" name="username" className="eq-input" autoComplete="username" />
            </div>
            <div className="eq-input-wrap">
              <label className="eq-text-xs eq-text-dim" htmlFor="password">
                Password
              </label>
              <input
                id="password"
                name="password"
                type="password"
                className="eq-input"
                autoComplete="current-password"
              />
            </div>
            {error ? <p className="admin-error">{error}</p> : null}
            <button type="submit" className="eq-btn eq-btn--primary" disabled={busy}>
              {busy ? 'Signing in…' : 'Sign in'}
            </button>
          </form>
        </div>
      </div>
    </div>
  )
}
