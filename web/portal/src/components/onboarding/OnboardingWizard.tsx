"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { postConfigYaml, tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";
import { useToasts } from "@/components/Toasts";
import "../../app/portal-onboarding.css";

type Status = {
  onboarding_status: string;
  repositories_detected: number;
  first_pr_reviewed: boolean;
};

type Account = {
  github_org: string | null;
  github_app_connected: boolean;
  has_config_yaml: boolean;
};

type RepoRow = {
  id: string;
  full_name: string;
  job_count: number;
};

const FOCUS_RULES: { id: string; label: string; description: string }[] = [
  { id: "security-baseline", label: "Security", description: "Auth, secrets, injection, and unsafe patterns." },
  { id: "performance-baseline", label: "Performance", description: "Hot paths, N+1 queries, and heavy allocations." },
  { id: "maintainability-baseline", label: "Maintainability", description: "Complexity, naming, and structure." },
];

function buildStarterYaml(selected: string[]): string {
  const rules = selected.map((id) => {
    const label = FOCUS_RULES.find((r) => r.id === id)?.label ?? id;
    return `  - id: ${id}\n    severity: warn\n    message: Focus reviews on ${label.toLowerCase()} concerns.`;
  });
  return `version: 1\nrules:\n${rules.length > 0 ? rules.join("\n") : "  []"}\n`;
}

export function OnboardingWizard() {
  const { pushToast } = useToasts();
  const [step, setStep] = useState(1);
  const [status, setStatus] = useState<Status | null>(null);
  const [account, setAccount] = useState<Account | null>(null);
  const [installUrl, setInstallUrl] = useState<string | null>(null);
  const [repos, setRepos] = useState<RepoRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [installLoading, setInstallLoading] = useState(false);
  const [focus, setFocus] = useState<string[]>(["security-baseline", "maintainability-baseline"]);
  const [savingConfig, setSavingConfig] = useState(false);

  const refresh = useCallback(async () => {
    const s = loadSession();
    if (!s) return;

    const [stRes, acRes, repoRes] = await Promise.all([
      tenantGet(s.tenantId, s.apiKey, "/status"),
      tenantGet(s.tenantId, s.apiKey, "/account"),
      tenantGet(s.tenantId, s.apiKey, "/repositories"),
    ]);

    if (stRes.ok) setStatus((await stRes.json()) as Status);
    if (acRes.ok) setAccount((await acRes.json()) as Account);
    if (repoRes.ok) setRepos((await repoRes.json()) as RepoRow[]);
  }, []);

  useEffect(() => {
    (async () => {
      await refresh();
      setLoading(false);
    })();
  }, [refresh]);

  useEffect(() => {
    if (status?.onboarding_status === "live" && step === 1) {
      setStep(2);
    }
  }, [status, step]);

  useEffect(() => {
    if (step !== 2 || status?.onboarding_status !== "pending_github_install") return;
    const id = window.setInterval(() => {
      void refresh();
    }, 4000);
    return () => window.clearInterval(id);
  }, [step, status?.onboarding_status, refresh]);

  async function loadInstallUrl() {
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
      setInstallUrl(data.install_url);
      window.open(data.install_url, "_blank", "noopener,noreferrer");
    } finally {
      setInstallLoading(false);
    }
  }

  async function saveFocusConfig() {
    const s = loadSession();
    if (!s) return;
    setSavingConfig(true);
    try {
      const yaml = buildStarterYaml(focus);
      const res = await postConfigYaml(s.tenantId, s.apiKey, yaml);
      if (!res.ok) {
        pushToast({ kind: "error", title: "Config not saved", message: "YAML validation failed." });
        return;
      }
      pushToast({ kind: "success", title: "Standards saved", message: "Your review focus rules are active." });
      setStep(4);
      await refresh();
    } finally {
      setSavingConfig(false);
    }
  }

  if (loading) {
    return <div className="eq-skeleton" style={{ height: 14, width: 200 }} />;
  }

  const connected = status?.onboarding_status === "live" || account?.github_app_connected;
  const stepLabels = ["Connect GitHub", "Repositories", "Review focus", "Done"];

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div
            className="eq-text-xs eq-text-muted"
            style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}
          >
            Onboarding
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Set up your workspace
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            Install the EngineIQ GitHub App, confirm repositories, and optional review standards.
          </p>
        </div>
      </div>

      <div className="eq-onboard-steps" role="tablist" aria-label="Onboarding steps">
        {stepLabels.map((label, i) => {
          const n = i + 1;
          const done = n < step || (n === 2 && connected && step > 2);
          const active = n === step;
          return (
            <div
              key={label}
              className={`eq-onboard-step ${active ? "eq-onboard-step--active" : ""} ${done ? "eq-onboard-step--done" : ""}`}
              role="tab"
              aria-selected={active}
            >
              {done ? "✓ " : `${n}. `}
              {label}
            </div>
          );
        })}
      </div>

      <div className="eq-onboard-panel">
        {step === 1 && (
          <div className="eq-card">
            <h2 className="eq-h3">Connect GitHub</h2>
            <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
              Install the EngineIQ app on your organisation
              {account?.github_org ? (
                <>
                  {" "}
                  <strong>{account.github_org}</strong>
                </>
              ) : null}
              . Repository access is granted on GitHub — we do not store your source code.
            </p>
            <div style={{ marginTop: 18, display: "flex", flexWrap: "wrap", gap: 10 }}>
              <button
                type="button"
                className="eq-btn eq-btn--primary"
                disabled={installLoading}
                onClick={() => void loadInstallUrl()}
              >
                {installLoading ? "Opening GitHub…" : "Install on GitHub"}
              </button>
              {installUrl ? (
                <a href={installUrl} className="eq-btn eq-btn--secondary" target="_blank" rel="noopener noreferrer">
                  Open link again
                </a>
              ) : null}
            </div>
            {connected ? (
              <p className="eq-text-sm" style={{ marginTop: 14, color: "var(--eq-green)" }}>
                GitHub App connected — continue to repositories.
              </p>
            ) : (
              <p className="eq-text-xs eq-text-dim" style={{ marginTop: 14 }}>
                After installing, return here — we detect the connection automatically.
              </p>
            )}
            <div style={{ marginTop: 16 }}>
              <button
                type="button"
                className="eq-btn eq-btn--secondary"
                disabled={!connected}
                onClick={() => setStep(2)}
              >
                Continue
              </button>
            </div>
          </div>
        )}

        {step === 2 && (
          <div className="eq-card">
            <h2 className="eq-h3">Repositories</h2>
            <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
              {repos.length > 0
                ? `${repos.length} repositor${repos.length === 1 ? "y" : "ies"} detected from your installation.`
                : "No repositories yet — open a PR on a connected repo or refresh after granting access on GitHub."}
            </p>
            {repos.length > 0 ? (
              <div className="eq-repo-pill-list">
                {repos.map((r) => (
                  <span key={r.id} className="eq-repo-pill">
                    {r.full_name}
                  </span>
                ))}
              </div>
            ) : null}
            <div style={{ marginTop: 18, display: "flex", gap: 10, flexWrap: "wrap" }}>
              <button type="button" className="eq-btn eq-btn--secondary" onClick={() => void refresh()}>
                Refresh
              </button>
              <button type="button" className="eq-btn eq-btn--primary" onClick={() => setStep(3)}>
                Continue
              </button>
            </div>
          </div>
        )}

        {step === 3 && (
          <div className="eq-card">
            <h2 className="eq-h3">Review focus</h2>
            <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
              Optional starter rules — edit full YAML anytime in <Link href="/settings">Settings</Link>.
            </p>
            <div className="eq-focus-grid">
              {FOCUS_RULES.map((r) => (
                <label key={r.id} className="eq-focus-option">
                  <input
                    type="checkbox"
                    checked={focus.includes(r.id)}
                    onChange={(e) => {
                      setFocus((prev) =>
                        e.target.checked ? [...prev, r.id] : prev.filter((x) => x !== r.id),
                      );
                    }}
                  />
                  <span>
                    <span className="eq-text-sm" style={{ fontWeight: 600 }}>
                      {r.label}
                    </span>
                    <span className="eq-text-xs eq-text-dim" style={{ display: "block", marginTop: 4 }}>
                      {r.description}
                    </span>
                  </span>
                </label>
              ))}
            </div>
            <div style={{ marginTop: 18, display: "flex", gap: 10, flexWrap: "wrap" }}>
              <button
                type="button"
                className="eq-btn eq-btn--primary"
                disabled={savingConfig || focus.length === 0}
                onClick={() => void saveFocusConfig()}
              >
                {savingConfig ? "Saving…" : "Save & continue"}
              </button>
              <button type="button" className="eq-btn eq-btn--secondary" onClick={() => setStep(4)}>
                Skip for now
              </button>
            </div>
          </div>
        )}

        {step === 4 && (
          <div className="eq-card">
            <h2 className="eq-h3">You&apos;re ready</h2>
            <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
              Open a pull request on a connected repository to trigger your first EngineIQ review.
            </p>
            <ul className="eq-text-sm eq-text-muted" style={{ marginTop: 12, paddingLeft: 18 }}>
              <li>{status?.repositories_detected ?? 0} repositories connected</li>
              <li>
                {account?.has_config_yaml
                  ? "Standards YAML configured"
                  : "Using platform defaults until you add YAML"}
              </li>
            </ul>
            <div style={{ marginTop: 18 }}>
              <Link href="/dashboard" className="eq-btn eq-btn--primary">
                Go to dashboard
              </Link>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
