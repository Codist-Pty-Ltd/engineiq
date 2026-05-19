import { Navigate, Outlet, useLocation } from 'react-router-dom'
import { getAuthHeader } from '../api/client'

export function RequireAuth() {
  const location = useLocation()
  if (!getAuthHeader())
    return <Navigate to="/login" replace state={{ from: location.pathname }} />
  return <Outlet />
}
