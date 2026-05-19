export function portalBaseUrl() {
  return process.env.NEXT_PUBLIC_PORTAL_URL ?? "http://localhost:3001";
}

export function portalLoginUrl(tenantId?: string) {
  const base = portalBaseUrl().replace(/\/$/, "");
  if (!tenantId) return `${base}/login`;
  return `${base}/login?tenant_id=${encodeURIComponent(tenantId)}`;
}
