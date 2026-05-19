"use client";

import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { saveSession } from "@/lib/auth";
import { tenantGet } from "@/lib/api";
import { useToasts } from "@/components/Toasts";

export function LoginForm() {
  const router = useRouter();
  const params = useSearchParams();
  const { pushToast } = useToasts();
  const [tenantId, setTenantId] = useState("");
  const [apiKey, setApiKey] = useState("");
  const [showKey, setShowKey] = useState(false);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    const t = params.get("tenant_id") ?? params.get("tenant");
    if (t) setTenantId(t);
    if (params.get("onboarding") === "complete") {
      pushToast({
        kind: "success",
        title: "GitHub connected",
        message: "Sign in with your API key to finish setup in the portal.",
      });
    }
  }, [params, pushToast]);

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setErr(null);

    const t = tenantId.trim();
    const k = apiKey.trim();

    // Basic client-side validation before we hit the API.
    const uuidOk = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/.test(t);
    if (!uuidOk) {
      setErr("Tenant ID must be a UUID (e.g. 00000000-0000-0000-0000-000000000000).");
      return;
    }
    if (k.length < 20) {
      setErr("API key looks too short. Paste the full key from onboarding.");
      return;
    }

    setLoading(true);
    try {
      // Validate credentials with the API before saving locally.
      const res = await tenantGet(t, k, "/status");
      if (res.status === 401) {
        setErr("Invalid tenant ID or API key.");
        pushToast({ kind: "error", title: "Sign-in failed", message: "Invalid tenant ID or API key." });
        return;
      }
      if (res.status === 403) {
        setErr("API key does not belong to this tenant.");
        pushToast({ kind: "error", title: "Sign-in failed", message: "API key does not belong to this tenant." });
        return;
      }
      if (!res.ok) {
        setErr(`Sign-in failed (${res.status}). Please try again.`);
        pushToast({ kind: "error", title: "Sign-in failed", message: `Unexpected status ${res.status}.` });
        return;
      }

      const status = (await res.json()) as { onboarding_status?: string };
      saveSession(t, k);
      pushToast({ kind: "success", title: "Signed in", message: "Welcome to your workspace." });
      if (status.onboarding_status === "pending_github_install") {
        router.push("/onboarding");
      } else {
        router.push("/dashboard");
      }
    } catch {
      setErr("Network error — cannot reach the EngineIQ API.");
      pushToast({ kind: "error", title: "Network error", message: "Cannot reach the EngineIQ API." });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="eq-section">
      <div className="eq-container" style={{ maxWidth: 520 }}>
        <div style={{ display: "flex", justifyContent: "center", marginBottom: 18 }}>
          <div className="eq-brand" style={{ gap: 10 }}>
            <span className="eq-brand__mark" aria-hidden="true" />
            <span>EngineIQ</span>
          </div>
        </div>

        <div className="eq-card">
          <h1 className="eq-h2">Sign in to your workspace</h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
          Use the tenant ID and API key from your onboarding email.
          </p>

          <form onSubmit={onSubmit} style={{ marginTop: 18, display: "grid", gap: 14 }}>
            <div>
              <label className="eq-text-sm eq-text-muted">Tenant ID</label>
              <input
                required
                value={tenantId}
                onChange={(e) => setTenantId(e.target.value)}
                className="eq-input"
                placeholder="00000000-0000-0000-0000-000000000000"
                inputMode="text"
                autoComplete="off"
              />
            </div>

            <div>
              <label className="eq-text-sm eq-text-muted">API key</label>
              <div className="eq-row" style={{ alignItems: "center" }}>
                <input
                  required
                  type={showKey ? "text" : "password"}
                  value={apiKey}
                  onChange={(e) => setApiKey(e.target.value)}
                  className="eq-input"
                  autoComplete="off"
                />
                <button
                  type="button"
                  className="eq-btn eq-btn--secondary"
                  onClick={() => setShowKey((v) => !v)}
                  aria-pressed={showKey}
                >
                  {showKey ? "Hide" : "Show"}
                </button>
              </div>
            </div>

            {err && (
              <div className="eq-card" style={{ padding: 14, borderColor: "rgba(239, 68, 68, 0.35)" }}>
                <div className="eq-text-sm" style={{ color: "var(--eq-red)" }}>
                  {err}
                </div>
              </div>
            )}

            <button
              type="submit"
              className="eq-btn eq-btn--primary"
              disabled={loading}
              aria-disabled={loading}
              style={{ width: "100%", opacity: loading ? 0.7 : 1 }}
            >
              {loading ? "Signing in…" : "Sign in"}
            </button>
          </form>

          <div className="eq-text-xs eq-text-dim" style={{ marginTop: 14 }}>
            Don&apos;t have an account?{" "}
            <a href="https://engineiq.co.za/sign-up" style={{ color: "var(--eq-accent-light)" }}>
              Create one
            </a>
            .
          </div>
        </div>
      </div>
    </div>
  );
}
