/** Short-lived credentials bridge from marketing sign-up → portal login (same browser tab). */

export const SIGNUP_HANDOFF_KEY = "engineiq_signup_handoff";

const MAX_AGE_MS = 15 * 60 * 1000;

export type SignupHandoff = {
  tenant_id: string;
  api_key: string;
  ts: number;
};

export function writeSignupHandoff(tenantId: string, apiKey: string): void {
  if (typeof window === "undefined") return;
  const payload: SignupHandoff = {
    tenant_id: tenantId.trim(),
    api_key: apiKey,
    ts: Date.now(),
  };
  sessionStorage.setItem(SIGNUP_HANDOFF_KEY, JSON.stringify(payload));
}

/** Reads handoff once, then removes it from sessionStorage. */
export function consumeSignupHandoff(): SignupHandoff | null {
  if (typeof window === "undefined") return null;
  const raw = sessionStorage.getItem(SIGNUP_HANDOFF_KEY);
  if (!raw) return null;
  sessionStorage.removeItem(SIGNUP_HANDOFF_KEY);
  try {
    const p = JSON.parse(raw) as SignupHandoff;
    if (!p.tenant_id?.trim() || !p.api_key?.trim() || typeof p.ts !== "number") return null;
    if (Date.now() - p.ts > MAX_AGE_MS) return null;
    return p;
  } catch {
    return null;
  }
}

export function buildPortalLoginUrl(
  portalBase: string,
  tenantId: string,
  opts?: { from?: "signup" | "github" },
): string {
  const base = portalBase.replace(/\/$/, "");
  const u = new URL(`${base}/login`);
  u.searchParams.set("tenant_id", tenantId.trim());
  if (opts?.from) u.searchParams.set("from", opts.from);
  return u.toString();
}

export function navigateToPortalWithHandoff(
  portalBase: string,
  tenantId: string,
  apiKey: string,
): void {
  writeSignupHandoff(tenantId, apiKey);
  window.location.href = buildPortalLoginUrl(portalBase, tenantId, { from: "signup" });
}
