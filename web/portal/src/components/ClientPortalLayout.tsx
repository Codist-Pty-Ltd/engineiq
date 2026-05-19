"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect, useState } from "react";
import { clearSession, loadSession } from "@/lib/auth";
import { tenantGet } from "@/lib/api";

const links = [
  { href: "/dashboard", label: "Dashboard" },
  { href: "/overview", label: "Analytics" },
  { href: "/findings", label: "Findings" },
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
        const res = await tenantGet(s.tenantId, s.apiKey, "/account");
        if (res.status === 401 || res.status === 403) {
          clearSession();
          router.replace("/login");
          return;
        }
        if (res.ok) {
          const data = (await res.json()) as { company_name?: string; plan?: string };
          setTenantName(data.company_name ?? null);
          setTenantPlan(data.plan ?? null);
        }
      } finally {
        setReady(true);
      }
    })();
  }, [pathname, router]);

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
