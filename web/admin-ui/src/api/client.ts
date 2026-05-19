const AUTH_KEY = 'engineiq_admin_authorization'

export function getAuthHeader(): string | null {
  return sessionStorage.getItem(AUTH_KEY)
}

export function setAuthHeader(value: string): void {
  sessionStorage.setItem(AUTH_KEY, value)
}

export function clearAuth(): void {
  sessionStorage.removeItem(AUTH_KEY)
}

export class UnauthorizedError extends Error {
  constructor() {
    super('Unauthorized')
    this.name = 'UnauthorizedError'
  }
}

export function redirectToLogin(): void {
  window.location.href = '/admin/login'
}

export async function apiFetch(path: string, init: RequestInit = {}): Promise<Response> {
  const auth = getAuthHeader()
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (auth) headers.set('Authorization', auth)

  const method = init.method ?? 'GET'
  const shouldSetJsonContentType =
    init.body !== undefined &&
    !(init.body instanceof FormData) &&
    method !== 'GET' &&
    method !== 'HEAD'
  if (shouldSetJsonContentType && !headers.has('Content-Type'))
    headers.set('Content-Type', 'application/json')

  const res = await fetch(path, { ...init, headers })
  if (res.status === 401) {
    clearAuth()
    throw new UnauthorizedError()
  }
  return res
}

export async function apiJson<T>(path: string, init?: RequestInit): Promise<T> {
  const res = await apiFetch(path, init)
  if (!res.ok) {
    const text = await res.text()
    throw new Error(text || res.statusText)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}
