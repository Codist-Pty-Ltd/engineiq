"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { clearSession, loadSession } from "@/lib/auth";
import { tenantGet } from "@/lib/api";

const links = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/installations", label: "Installations" },
  { href: "/overview", label: "Analytics" },
  { href: "/findings", label: "Findings" },
  { href: "/notifications", label: "Notifications" },
  { href: "/repositories", label: "Repositories" },
  { href: "/usage", label: "Usage" },
  { href: "/settings", label: "Settings" },
  { href: "/reports", label: "Reports" },
];

export function ClientPortalLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const router = useRouter();
  const [ready, setReady] = useState(false);
  const [tenantName, setTenantName] = useState<string | null>(null);
  const [tenantPlan, setTenantPlan] = useState<string | null>(null);
  const [onboardingStatus, setOnboardingStatus] = useState<string | null>(null);

  useEffect(() => {
    if (pathname === "/login") return;
    const s = loadSession();
    if (!s) {
      router.replace("/login");
      return;
    }

    // Soft-validate session (and hydrate sidebar label) without blocking page load.
    (async () => {
      try {
        const [accountRes, statusRes] = await Promise.all([
          tenantGet(s.tenantId, s.apiKey, "/account"),
          tenantGet(s.tenantId, s.apiKey, "/status"),
        ]);
        if (accountRes.status === 401 || accountRes.status === 403) {
          clearSession();
          router.replace("/login");
          return;
        }
        if (accountRes.ok) {
          const data = (await accountRes.json()) as { company_name?: string; plan?: string };
          setTenantName(data.company_name ?? null);
          setTenantPlan(data.plan ?? null);
        }
        if (statusRes.ok) {
          const st = (await statusRes.json()) as { onboarding_status?: string };
          setOnboardingStatus(st.onboarding_status ?? null);
        }
      } finally {
        setReady(true);
      }
    })();
  }, [pathname, router]);

  useEffect(() => {
    if (!ready || pathname === "/login") return;
    if (onboardingStatus !== "pending_github_install") return;
    if (pathname.startsWith("/onboarding") || pathname === "/installations" || pathname === "/settings") return;
    router.replace("/onboarding");
  }, [ready, onboardingStatus, pathname, router]);

  if (pathname === "/login") return <>{children}</>;

  if (!ready) {
    return (
      <div className="eq-section">
        <div className="eq-container" style={{ maxWidth: 520 }}>
          <div className="eq-card">
            <div className="eq-skeleton" style={{ height: 14, width: 180 }} />
            <div className="eq-skeleton" style={{ height: 12, width: 260, marginTop: 10 }} />
          </div>
        </div>
      </div>
    );
  }

  if (!loadSession()) return null;

  return (
    <div className="eq-app">
      <aside className="eq-sidebar" aria-label="Sidebar navigation">
        <div className="eq-sidebar__logo">
          <Link href="/dashboard" className="eq-brand" aria-label="EngineIQ dashboard">
            <span className="eq-brand__mark" aria-hidden="true" />
            <span>EngineIQ</span>
          </Link>
        </div>

        <nav className="eq-sidebar__nav">
          {links.map((l) => (
            <Link
              key={l.href}
              href={l.href}
              className={`eq-navitem ${
                pathname === l.href ||
                (l.href === "/dashboard" && (pathname === "/dashboard" || pathname.startsWith("/dashboard/")))
                  ? "eq-navitem--active"
                  : ""
              }`}
            >
              {l.label}
            </Link>
          ))}
        </nav>

        <div className="eq-sidebar__bottom">
          <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
            Workspace
          </div>
          <div className="eq-text-sm" style={{ marginTop: 8, fontWeight: 600 }}>
            {tenantName ?? "Your tenant"}
          </div>
          <div className="eq-text-xs eq-text-dim" style={{ marginTop: 6 }}>
            {tenantPlan ? <span className="eq-badge eq-badge--grey">{tenantPlan}</span> : null}
          </div>

          <div style={{ marginTop: 12 }}>
            <button
              type="button"
              onClick={() => {
                clearSession();
                router.push("/login");
              }}
              className="eq-btn eq-btn--secondary"
              style={{ width: "100%", justifyContent: "space-between" }}
            >
              Sign out
              <span aria-hidden="true">→</span>
            </button>
          </div>
        </div>
      </aside>

      <div className="eq-main">
        <main className="eq-container" style={{ paddingTop: 24, paddingBottom: 32 }}>
          {children}
        </main>
      </div>
    </div>
  );
}
