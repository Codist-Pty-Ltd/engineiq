"use client";

import Link from "next/link";
import { useState } from "react";
import { useToasts } from "@/components/Toasts";
import { portalBaseUrl, portalLoginUrl } from "@/lib/site-urls";
import { navigateToPortalWithHandoff } from "../../../../shared/signup-handoff";

const API_BASE =
  process.env.NEXT_PUBLIC_ENGINEIQ_API_URL ?? "http://localhost:5056";
const DPA_PDF_URL =
  process.env.NEXT_PUBLIC_DPA_PDF_URL ?? "https://engineiq.co.za/legal/dpa.pdf";

type RegisterResponse = {
  tenant_id: string;
  install_url: string;
  api_key: string;
};

type PlanId = "Starter" | "Growth" | "Enterprise";

export default function SignUpPage() {
  const { pushToast } = useToasts();
  const [step, setStep] = useState<1 | 2>(1);
  const [email, setEmail] = useState("");
  const [companyName, setCompanyName] = useState("");
  const [plan, setPlan] = useState<PlanId>("Growth");
  const [githubOrg, setGithubOrg] = useState("");
  const [dpaAccepted, setDpaAccepted] = useState(false);
  const [loading, setLoading] = useState(false);
  const [err, setErr] = useState<string | null>(null);
  const [done, setDone] = useState<RegisterResponse | null>(null);

  async function copy(label: string, value: string) {
    try {
      await navigator.clipboard.writeText(value);
      pushToast({ kind: "success", title: "Copied", message: `${label} copied to clipboard.` });
    } catch {
      pushToast({ kind: "error", title: "Copy failed", message: "Your browser blocked clipboard access." });
    }
  }

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setErr(null);
    try {
      const res = await fetch(`${API_BASE}/api/v1/onboarding/register`, {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          email,
          company_name: companyName,
          plan,
          github_org: githubOrg,
          dpa_accepted: dpaAccepted,
        }),
      });
      const data = await res.json().catch(() => ({}));
      if (!res.ok) {
        setErr(
          typeof data?.errors === "object"
            ? JSON.stringify(data.errors)
            : data?.error ?? `Request failed (${res.status})`,
        );
        pushToast({ kind: "error", title: "Sign-up failed", message: "Please review the form and try again." });
        return;
      }
      setDone(data as RegisterResponse);
      setStep(2);
      pushToast({ kind: "success", title: "Account created", message: "Step 2: install the GitHub App to go live." });
    } catch {
      setErr("Network error — is the EngineIQ API running?");
      pushToast({ kind: "error", title: "Network error", message: "Cannot reach the EngineIQ API." });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="eq-section">
      <div className="eq-container" style={{ maxWidth: 980 }}>
        <div className="eq-row" style={{ alignItems: "flex-end", gap: 24, flexWrap: "wrap" }}>
          <div>
            <div
              className="eq-text-xs eq-text-muted"
              style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}
            >
              Onboarding
            </div>
            <h1 className="eq-h2" style={{ marginTop: 10 }}>
              Start reviewing in 60 seconds.
            </h1>
            <p className="eq-text-md eq-text-muted" style={{ margin: "12px 0 0", maxWidth: 620 }}>
              Self-serve onboarding — no sales call required. You’ll get a tenant ID + API key, then
              install the GitHub App for your organisation.
            </p>
          </div>

          <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
            <span className={`eq-badge ${step === 1 ? "eq-badge--purple" : "eq-badge--grey"}`}>
              Step 1 · Create account
            </span>
            <span className={`eq-badge ${step === 2 ? "eq-badge--purple" : "eq-badge--grey"}`}>
              Step 2 · Install GitHub App
            </span>
            <span className="eq-badge eq-badge--grey">Step 3 · Portal setup</span>
          </div>
        </div>

        <div style={{ marginTop: 18 }} className="eq-grid-2">
          {/* Left: Steps */}
          <div className="eq-card" style={{ minHeight: 520 }}>
            {step === 1 && (
              <form onSubmit={onSubmit} style={{ display: "grid", gap: 14 }}>
                <div>
                  <label className="eq-text-sm eq-text-muted">Company name *</label>
                  <input
                    className="eq-input"
                    required
                    value={companyName}
                    onChange={(e) => setCompanyName(e.target.value)}
                    placeholder="Codist"
                    aria-label="Company name"
                  />
                </div>

                <div>
                  <label className="eq-text-sm eq-text-muted">Work email *</label>
                  <div className="eq-input-wrap">
                    <input
                      className="eq-input"
                      type="email"
                      required
                      value={email}
                      onChange={(e) => setEmail(e.target.value)}
                      placeholder="cto@company.co.za"
                      aria-label="Work email"
                      aria-busy={loading}
                      style={{ paddingRight: loading ? 40 : undefined }}
                    />
                    {loading ? (
                      <span className="eq-spinner" role="status" aria-label="Submitting" />
                    ) : null}
                  </div>
                </div>

                <div>
                  <label className="eq-text-sm eq-text-muted">GitHub organisation slug *</label>
                  <input
                    className="eq-input"
                    required
                    value={githubOrg}
                    onChange={(e) => setGithubOrg(e.target.value)}
                    placeholder="acme-corp"
                    aria-describedby="github-org-help"
                  />
                  <div id="github-org-help" className="eq-text-xs eq-text-dim" style={{ marginTop: 8 }}>
                    Your GitHub org login, e.g. <span className="eq-font-mono">acme-corp</span>
                  </div>
                </div>

                <div>
                  <div className="eq-text-sm eq-text-muted">Plan *</div>
                  <div style={{ display: "grid", gap: 10, marginTop: 10 }}>
                    {(
                      [
                        {
                          id: "Starter",
                          title: "Starter",
                          price: "Free",
                          meta: "50 PRs/month · 1 repo",
                        },
                        {
                          id: "Growth",
                          title: "Growth",
                          price: "R499/month",
                          meta: "Unlimited PRs · 5 repos · Priority",
                        },
                        {
                          id: "Enterprise",
                          title: "Enterprise",
                          price: "Custom",
                          meta: "Unlimited · SLA · Dedicated support",
                        },
                      ] as const
                    ).map((p) => (
                      <label
                        key={p.id}
                        className="eq-card"
                        style={{
                          padding: 16,
                          cursor: "pointer",
                          borderColor:
                            plan === p.id ? "rgba(124, 58, 237, 0.55)" : "var(--eq-border)",
                          boxShadow: plan === p.id ? "0 0 0 1px var(--eq-accent-glow)" : undefined,
                        }}
                      >
                        <input
                          type="radio"
                          name="plan"
                          value={p.id}
                          checked={plan === p.id}
                          onChange={() => setPlan(p.id)}
                          style={{ position: "absolute", opacity: 0, pointerEvents: "none" }}
                        />
                        <div className="eq-row" style={{ alignItems: "baseline" }}>
                          <div>
                            <div className="eq-h3">{p.title}</div>
                            <div className="eq-text-xs eq-text-dim" style={{ marginTop: 4 }}>
                              {p.meta}
                            </div>
                          </div>
                          <div className="eq-font-mono" style={{ color: "var(--eq-text-muted)" }}>
                            {p.price}
                          </div>
                        </div>
                      </label>
                    ))}
                  </div>
                </div>

                <label
                  className="eq-card"
                  style={{ padding: 16, display: "flex", gap: 12, alignItems: "flex-start", cursor: "pointer" }}
                >
                  <input
                    type="checkbox"
                    checked={dpaAccepted}
                    onChange={(e) => setDpaAccepted(e.target.checked)}
                    aria-label="Accept DPA"
                    style={{ marginTop: 3 }}
                  />
                  <span className="eq-text-sm eq-text-muted">
                    I agree to the{" "}
                    <a href={DPA_PDF_URL} target="_blank" rel="noopener noreferrer" style={{ color: "var(--eq-accent-light)" }}>
                      Data Processing Agreement (DPA)
                    </a>
                    . Registration requires this acknowledgement.
                  </span>
                </label>

                {err && (
                  <div className="eq-card" style={{ padding: 14, borderColor: "rgba(239, 68, 68, 0.35)" }}>
                    <div className="eq-text-sm" style={{ color: "var(--eq-red)" }}>
                      {err}
                    </div>
                  </div>
                )}

                <div className="eq-row" style={{ justifyContent: "flex-end", marginTop: 6 }}>
                  <button
                    type="submit"
                    className="eq-btn eq-btn--primary"
                    disabled={loading || !dpaAccepted}
                    aria-disabled={loading || !dpaAccepted}
                    style={{ opacity: loading || !dpaAccepted ? 0.6 : 1 }}
                  >
                    {loading ? "Creating account…" : "Create account →"}
                  </button>
                </div>
              </form>
            )}

            {step === 2 && done && (
              <div style={{ display: "grid", gap: 14 }}>
                <div>
                  <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                    Credentials
                  </div>
                  <h2 className="eq-h2" style={{ marginTop: 10 }}>
                    Install the GitHub App
                  </h2>
                  <p className="eq-text-md eq-text-muted" style={{ margin: "12px 0 0" }}>
                    Copy these now. Your API key won’t be shown again. The tenant webhook verification secret and DPA
                    PDF link are emailed once only.
                  </p>
                </div>

                <div className="eq-card" style={{ padding: 16 }}>
                  <div className="eq-row" style={{ alignItems: "flex-start" }}>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                        Tenant ID
                      </div>
                      <div className="eq-font-mono" style={{ marginTop: 8, wordBreak: "break-all", color: "var(--eq-text)" }}>
                        {done.tenant_id}
                      </div>
                    </div>
                    <button type="button" className="eq-btn eq-btn--secondary" onClick={() => copy("Tenant ID", done.tenant_id)}>
                      Copy
                    </button>
                  </div>
                </div>

                <div className="eq-card" style={{ padding: 16 }}>
                  <div className="eq-row" style={{ alignItems: "flex-start" }}>
                    <div style={{ flex: 1, minWidth: 0 }}>
                      <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                        API key
                      </div>
                      <div className="eq-font-mono" style={{ marginTop: 8, wordBreak: "break-all", color: "var(--eq-text)" }}>
                        {done.api_key}
                      </div>
                    </div>
                    <button type="button" className="eq-btn eq-btn--secondary" onClick={() => copy("API key", done.api_key)}>
                      Copy
                    </button>
                  </div>
                </div>

                <div className="eq-row" style={{ justifyContent: "space-between", flexWrap: "wrap", gap: 10 }}>
                  <a href={done.install_url} className="eq-btn eq-btn--primary" target="_blank" rel="noopener noreferrer">
                    Install GitHub App →
                  </a>
                  <button
                    type="button"
                    className="eq-btn eq-btn--secondary"
                    onClick={() => navigateToPortalWithHandoff(portalBaseUrl(), done.tenant_id, done.api_key)}
                  >
                    Continue to portal setup →
                  </button>
                </div>
                <p className="eq-text-xs eq-text-dim">
                  Portal setup pre-fills your tenant ID and API key in this browser (15-minute handoff). Install GitHub
                  first if you prefer — you can return via the link in your welcome email.
                </p>

                <div className="eq-card" style={{ padding: 16 }}>
                  <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                    Next steps
                  </div>
                  <ol className="eq-text-sm eq-text-muted" style={{ margin: "12px 0 0", paddingLeft: 18, display: "grid", gap: 8 }}>
                    <li>
                      Click <strong>Install GitHub App</strong> and select repositories (or do this from the portal
                      wizard).
                    </li>
                    <li>
                      Click <strong>Continue to portal setup</strong> to sign in and finish onboarding (repos, optional
                      standards).
                    </li>
                    <li>Open a pull request on a connected repo to trigger your first review.</li>
                  </ol>
                  <div className="eq-text-xs eq-text-dim" style={{ marginTop: 12 }}>
                    Questions? Email <a href="mailto:hello@engineiq.co.za">hello@engineiq.co.za</a>
                  </div>
                </div>
              </div>
            )}
          </div>

          {/* Right: Context / trust */}
          <div className="eq-card" style={{ minHeight: 520 }}>
            <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
              What you’re getting
            </div>
            <h2 className="eq-h2" style={{ marginTop: 10 }}>
              CTO-trust onboarding by design.
            </h2>
            <p className="eq-text-md eq-text-muted" style={{ margin: "12px 0 0" }}>
              EngineIQ processes diffs in memory only. No source code is stored. Findings metadata is retained for dashboards
              and audit.
            </p>

            <div style={{ marginTop: 18 }} className="eq-card">
              <div className="eq-row">
                <span className="eq-badge eq-badge--green">Ephemeral processing</span>
                <span className="eq-font-mono eq-text-sm">true</span>
              </div>
              <div className="eq-row" style={{ marginTop: 10 }}>
                <span className="eq-badge eq-badge--green">No code stored</span>
                <span className="eq-font-mono eq-text-sm">true</span>
              </div>
              <div className="eq-row" style={{ marginTop: 10 }}>
                <span className="eq-badge eq-badge--purple">Audit metadata</span>
                <span className="eq-font-mono eq-text-sm">available</span>
              </div>
            </div>

            <div style={{ marginTop: 18, display: "grid", gap: 10 }}>
              <a className="eq-btn eq-btn--secondary" href="https://api.engineiq.co.za/security">
                Verify trust JSON (GET /security) →
              </a>
              <Link className="eq-btn eq-btn--secondary" href="/security">
                Read the security model →
              </Link>
              <Link className="eq-btn eq-btn--secondary" href="/pricing">
                View pricing →
              </Link>
            </div>

            <div className="eq-text-xs eq-text-dim" style={{ marginTop: 18 }}>
              Already have a tenant?{" "}
              <a href={portalLoginUrl()} style={{ color: "var(--eq-accent-light)" }}>
                Sign in to the portal
              </a>
              .
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
