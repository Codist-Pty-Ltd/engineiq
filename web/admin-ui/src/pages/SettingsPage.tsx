import { useNavigate } from 'react-router-dom'
import { clearAuth } from '../api/client'

export function SettingsPage() {
  const navigate = useNavigate()

  function logout() {
    clearAuth()
    navigate('/login', { replace: true })
  }

  return (
    <>
      <header className="eq-pagehead">
        <div>
          <h1 className="eq-h1">Settings</h1>
          <p className="eq-text-sm eq-text-muted">
            Session uses <span className="eq-font-mono">sessionStorage</span> only (Authorization header).
          </p>
        </div>
      </header>
      <div className="eq-card" style={{ padding: 16 }}>
        <button type="button" className="eq-btn eq-btn--destructive" onClick={logout}>
          Sign out
        </button>
      </div>
    </>
  )
}
