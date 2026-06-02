"use client";

import Link from "next/link";
import { useState } from "react";
import { tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";
import { useToasts } from "@/components/Toasts";

type Props = {
  githubOrg: string | null;
  connected: boolean;
  installationId: number | null;
  onRefresh?: () => void;
};

export function GitHubConnectPanel({ githubOrg, connected, installationId, onRefresh }: Props) {
  const { pushToast } = useToasts();
  const [installLoading, setInstallLoading] = useState(false);
  const [checking, setChecking] = useState(false);

  async function openGitHubInstall() {
    const s = loadSession();
    if (!s) return;
    setInstallLoading(true);
    try {
      const res = await tenantGet(s.tenantId, s.apiKey, "/onboarding/install-url");
      if (res.status === 409) {
        pushToast({
          kind: "info",
          title: "Already connected",
          message: "Refresh this page — GitHub may already be linked.",
        });
        onRefresh?.();
        return;
      }
      if (!res.ok) {
        pushToast({ kind: "error", title: "Install link unavailable", message: `API returned ${res.status}.` });
        return;
      }
      const data = (await res.json()) as { install_url: string };
      window.open(data.install_url, "_blank", "noopener,noreferrer");
      pushToast({
        kind: "info",
        title: "GitHub opened",
        message: "Install the app on your organisation, then return here and click Check connection.",
      });
    } finally {
      setInstallLoading(false);
    }
  }

  async function checkConnection() {
    setChecking(true);
    try {
      onRefresh?.();
      pushToast({ kind: "success", title: "Refreshed", message: "Account status updated from the API." });
    } finally {
      setChecking(false);
    }
  }

  if (connected) {
    return (
      <p className="eq-text-sm eq-text-muted" style={{ marginTop: 12 }}>
        GitHub App is connected
        {installationId != null ? (
          <>
            {" "}
            (<span className="eq-font-mono">installation {installationId}</span>)
          </>
        ) : null}
        . Manage repos on <Link href="/installations">Installations</Link>.
      </p>
    );
  }

  return (
    <div className="eq-card" style={{ marginTop: 14, padding: 16, borderColor: "rgba(245, 158, 11, 0.35)" }}>
      <div className="eq-row" style={{ alignItems: "flex-start", justifyContent: "space-between", gap: 12 }}>
        <div>
          <div className="eq-text-sm" style={{ fontWeight: 600, color: "var(--eq-amber, #f59e0b)" }}>
            GitHub App not connected
          </div>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
            Install EngineIQ on GitHub organisation{" "}
            <strong>{githubOrg ?? "from sign-up"}</strong> so we can review pull requests. Choose the org during install
            (not only a personal account unless that is intended).
          </p>
        </div>
        <span className="eq-badge eq-badge--amber">Pending</span>
      </div>
      <div className="eq-row" style={{ marginTop: 14, gap: 10, flexWrap: "wrap" }}>
        <button
          type="button"
          className="eq-btn eq-btn--primary"
          disabled={installLoading}
          onClick={() => void openGitHubInstall()}
        >
          {installLoading ? "Opening GitHub…" : "Install on GitHub"}
        </button>
        <button type="button" className="eq-btn eq-btn--secondary" disabled={checking} onClick={() => void checkConnection()}>
          {checking ? "Checking…" : "Check connection"}
        </button>
        <Link href="/onboarding" className="eq-btn eq-btn--secondary">
          Setup wizard
        </Link>
      </div>
      <p className="eq-text-xs eq-text-dim" style={{ marginTop: 12 }}>
        After install, GitHub redirects to the portal. If the tab closed early, sign in again and click Check connection.
      </p>
    </div>
  );
}
