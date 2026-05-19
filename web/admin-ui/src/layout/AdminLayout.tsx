import { NavLink, Outlet } from 'react-router-dom'

const navCls = ({ isActive }: { isActive: boolean }) =>
  'eq-navitem' + (isActive ? ' eq-navitem--active' : '')

export function AdminLayout() {
  return (
    <div className="eq-app">
      <aside className="eq-sidebar" aria-label="Admin navigation">
        <div className="eq-sidebar__logo eq-brand">
          <span className="eq-brand__mark" aria-hidden />
          <span className="eq-text-md">EngineIQ Admin</span>
        </div>
        <nav className="eq-sidebar__nav">
          <NavLink className={navCls} to="/" end>
            Overview
          </NavLink>
          <NavLink className={navCls} to="/tenants">
            Tenants
          </NavLink>
          <NavLink className={navCls} to="/jobs">
            Jobs & DLQ
          </NavLink>
          <NavLink className={navCls} to="/health">
            Health
          </NavLink>
          <NavLink className={navCls} to="/support">
            Support
          </NavLink>
          <NavLink className={navCls} to="/settings">
            Settings
          </NavLink>
        </nav>
        <div className="eq-sidebar__bottom eq-text-xs eq-text-dim">
          Bound to <span className="eq-font-mono">127.0.0.1:8081</span>
        </div>
      </aside>
      <main className="eq-main">
        <div className="eq-page">
          <Outlet />
        </div>
      </main>
    </div>
  )
}
