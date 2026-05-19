import type { Metadata } from "next";
import Link from "next/link";
import { portalLoginUrl } from "@/lib/site-urls";

export const metadata: Metadata = {
  title: "Docs — EngineIQ",
};

const API_BASE = process.env.NEXT_PUBLIC_ENGINEIQ_API_URL ?? "https://api.engineiq.co.za";

export default function DocsPage() {
  return (
    <div className="eq-section">
      <div className="eq-container" style={{ maxWidth: 720 }}>
        <h1 className="eq-h1" style={{ fontSize: "clamp(28px, 4vw, 40px)" }}>
          Documentation
        </h1>
        <p className="eq-text-md eq-text-muted" style={{ marginTop: 16 }}>
          Quick links for CTOs and platform owners. Product flows are self-serve; these endpoints and pages are the source
          of truth for trust and integration.
        </p>

        <div className="eq-grid-2" style={{ marginTop: 32 }}>
          <div className="eq-card">
            <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
              Trust
            </div>
            <h2 className="eq-h3" style={{ marginTop: 10 }}>
              Security disclosure (JSON)
            </h2>
            <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
              Programmatic verification of processing claims — suitable for vendor registers and questionnaires.
            </p>
            <a className="eq-btn eq-btn--secondary" style={{ marginTop: 16 }} href={`${API_BASE}/security`}>
              GET /security →
            </a>
          </div>

          <div className="eq-card">
            <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
              Onboarding
            </div>
            <h2 className="eq-h3" style={{ marginTop: 10 }}>
              Register a tenant
            </h2>
            <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
              Creates tenant credentials and triggers the GitHub App install flow via email. Requires DPA acceptance.
            </p>
            <p className="eq-text-xs eq-font-mono eq-text-dim" style={{ marginTop: 12, wordBreak: "break-all" }}>
              POST {API_BASE}/api/v1/onboarding/register
            </p>
            <Link href="/sign-up" className="eq-btn eq-btn--secondary" style={{ marginTop: 16 }}>
              Use sign-up UI →
            </Link>
          </div>

          <div className="eq-card">
            <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
              Portal
            </div>
            <h2 className="eq-h3" style={{ marginTop: 10 }}>
              Client dashboard
            </h2>
            <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
              Authenticate with tenant ID and API key from sign-up or your welcome email. After sign-up, use
              &quot;Continue to portal setup&quot; to pre-fill credentials in the same browser.
            </p>
            <a className="eq-btn eq-btn--secondary" style={{ marginTop: 16 }} href={portalLoginUrl()}>
              Open portal →
            </a>
          </div>

          <div className="eq-card">
            <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
              Compliance
            </div>
            <h2 className="eq-h3" style={{ marginTop: 10 }}>
              POPIA &amp; security model
            </h2>
            <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
              Human-readable summary of data handling, tenancy, and AI subprocessors.
            </p>
            <Link href="/security" className="eq-btn eq-btn--secondary" style={{ marginTop: 16 }}>
              Read security model →
            </Link>
          </div>
        </div>
      </div>
    </div>
  );
}
