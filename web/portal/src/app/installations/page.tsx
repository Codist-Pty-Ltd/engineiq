"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";
import { useToasts } from "@/components/Toasts";
import "../portal-onboarding.css";

type Account = {
  company_name: string;
  github_org: string | null;
  github_app_connected: boolean;
  github_app_installation_id: number | null;
};

type RepoRow = {
  id: string;
  full_name: string;
  job_count: number;
};

export default function InstallationsPage() {
  const { pushToast } = useToasts();
  const [account, setAccount] = useState<Account | null>(null);
  const [repos, setRepos] = useState<RepoRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [installLoading, setInstallLoading] = useState(false);

  const refresh = useCallback(async () => {
    const s = loadSession();
    if (!s) return;
    const [aRes, rRes] = await Promise.all([
      tenantGet(s.tenantId, s.apiKey, "/account"),
      tenantGet(s.tenantId, s.apiKey, "/repositories"),
    ]);
    if (aRes.ok) setAccount((await aRes.json()) as Account);
    if (rRes.ok) setRepos((await rRes.json()) as RepoRow[]);
  }, []);

  useEffect(() => {
    (async () => {
      await refresh();
      setLoading(false);
    })();
  }, [refresh]);

  async function openInstall() {
    const s = loadSession();
    if (!s) return;
    setInstallLoading(true);
    try {
      const res = await tenantGet(s.tenantId, s.apiKey, "/onboarding/install-url");
      if (!res.ok) {
        pushToast({ kind: "error", title: "Install link unavailable", message: `API returned ${res.status}.` });
        return;
      }
      const data = (await res.json()) as { install_url: string };
      window.open(data.install_url, "_blank", "noopener,noreferrer");
    } finally {
      setInstallLoading(false);
    }
  }

  if (loading) {
    return <div className="eq-skeleton" style={{ height: 14, width: 200 }} />;
  }

  const connected = account?.github_app_connected ?? false;

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div
            className="eq-text-xs eq-text-muted"
            style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}
          >
            GitHub
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Installations
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            Your EngineIQ GitHub App installation and connected repositories.
          </p>
        </div>
      </div>

      <div className="eq-card" style={{ marginBottom: 16 }}>
        <div className="eq-row" style={{ justifyContent: "space-between", alignItems: "flex-start", flexWrap: "wrap", gap: 12 }}>
          <div>
            <div className="eq-text-sm" style={{ fontWeight: 600 }}>
              {account?.company_name ?? "Workspace"}
            </div>
            <div className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
              Organisation: {account?.github_org ?? "—"}
            </div>
            <div className="eq-text-sm eq-text-muted" style={{ marginTop: 6 }}>
              Status:{" "}
              <span className={`eq-badge ${connected ? "eq-badge--green" : "eq-badge--grey"}`}>
                {connected ? "Connected" : "Not connected"}
              </span>
            </div>
            {account?.github_app_installation_id ? (
              <div className="eq-text-xs eq-text-dim" style={{ marginTop: 8 }}>
                Installation ID {account.github_app_installation_id}
              </div>
            ) : null}
          </div>
          <div style={{ display: "flex", gap: 10, flexWrap: "wrap" }}>
            {!connected ? (
              <button
                type="button"
                className="eq-btn eq-btn--primary"
                disabled={installLoading}
                onClick={() => void openInstall()}
              >
                {installLoading ? "Opening…" : "Connect GitHub"}
              </button>
            ) : null}
            <Link href="/onboarding" className="eq-btn eq-btn--secondary">
              Setup wizard
            </Link>
          </div>
        </div>
      </div>

      <div className="eq-card">
        <h2 className="eq-h3">Repositories</h2>
        {repos.length === 0 ? (
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
            No repositories detected yet. Grant access on GitHub or complete the{" "}
            <Link href="/onboarding">onboarding wizard</Link>.
          </p>
        ) : (
          <ul style={{ marginTop: 14, padding: 0, listStyle: "none" }}>
            {repos.map((r) => (
              <li
                key={r.id}
                style={{
                  display: "flex",
                  justifyContent: "space-between",
                  padding: "10px 0",
                  borderBottom: "1px solid var(--eq-border)",
                }}
              >
                <span className="eq-text-sm" style={{ fontFamily: "var(--font-mono)" }}>
                  {r.full_name}
                </span>
                <span className="eq-text-xs eq-text-dim">{r.job_count} reviews</span>
              </li>
            ))}
          </ul>
        )}
        <button type="button" className="eq-btn eq-btn--secondary" style={{ marginTop: 14 }} onClick={() => void refresh()}>
          Refresh
        </button>
      </div>
    </div>
  );
}
