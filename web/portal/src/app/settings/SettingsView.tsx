"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import {
  apiBase,
  postConfigYaml,
  tenantGet,
  tenantPatch,
  type PortalPreferences,
} from "@/lib/api";
import { loadSession } from "@/lib/auth";
import { useToasts } from "@/components/Toasts";
import { ToggleRow } from "@/components/settings/ToggleRow";
import "../portal-settings.css";

type Account = {
  tenant_id: string;
  company_name: string;
  plan: string;
  status: string;
  contact_email: string | null;
  github_org: string | null;
  github_app_connected: boolean;
  github_app_installation_id: number | null;
  has_config_yaml: boolean;
};

function maskApiKey(key: string): string {
  if (key.length <= 12) return "••••••••";
  return `${key.slice(0, 8)}…${key.slice(-4)}`;
}

export function SettingsView() {
  const { pushToast } = useToasts();
  const [account, setAccount] = useState<Account | null>(null);
  const [prefs, setPrefs] = useState<PortalPreferences | null>(null);
  const [yaml, setYaml] = useState("");
  const [sessionKeyMasked, setSessionKeyMasked] = useState<string | null>(null);
  const [showSessionKey, setShowSessionKey] = useState(false);
  const [loading, setLoading] = useState(true);
  const [savingPrefs, setSavingPrefs] = useState(false);
  const [savingYaml, setSavingYaml] = useState(false);
  const [yamlMsg, setYamlMsg] = useState<string | null>(null);
  const [yamlErr, setYamlErr] = useState<string | null>(null);

  const webhookUrl = `${apiBase().replace(/\/$/, "")}/webhooks/github`;

  useEffect(() => {
    const s = loadSession();
    if (s) setSessionKeyMasked(maskApiKey(s.apiKey));
  }, []);

  const load = useCallback(async () => {
    const s = loadSession();
    if (!s) return;
    const [aRes, pRes, cRes] = await Promise.all([
      tenantGet(s.tenantId, s.apiKey, "/account"),
      tenantGet(s.tenantId, s.apiKey, "/preferences"),
      tenantGet(s.tenantId, s.apiKey, "/config"),
    ]);
    if (aRes.ok) setAccount((await aRes.json()) as Account);
    if (pRes.ok) setPrefs((await pRes.json()) as PortalPreferences);
    if (cRes.ok) {
      const j = (await cRes.json()) as { config_yaml: string };
      setYaml(j.config_yaml ?? "");
    }
  }, []);

  useEffect(() => {
    (async () => {
      await load();
      setLoading(false);
    })();
  }, [load]);

  async function patchPref(patch: Partial<PortalPreferences>) {
    const s = loadSession();
    if (!s || !prefs) return;
    setSavingPrefs(true);
    const next = { ...prefs, ...patch };
    setPrefs(next);
    try {
      const res = await tenantPatch(s.tenantId, s.apiKey, "/preferences", patch);
      if (!res.ok) {
        await load();
        pushToast({ kind: "error", title: "Settings not saved", message: `API returned ${res.status}.` });
        return;
      }
      setPrefs((await res.json()) as PortalPreferences);
      pushToast({ kind: "success", title: "Saved", message: "Your preferences were updated." });
    } catch {
      await load();
      pushToast({ kind: "error", title: "Network error", message: "Could not save preferences." });
    } finally {
      setSavingPrefs(false);
    }
  }

  async function saveYaml() {
    const s = loadSession();
    if (!s) return;
    setYamlMsg(null);
    setYamlErr(null);
    setSavingYaml(true);
    try {
      const res = await postConfigYaml(s.tenantId, s.apiKey, yaml);
      const body = await res.json().catch(() => ({}));
      if (!res.ok) {
        setYamlErr(JSON.stringify(body));
        pushToast({ kind: "error", title: "Save failed", message: "YAML validation failed." });
        return;
      }
      setYamlMsg("Config saved.");
      pushToast({ kind: "success", title: "Config saved", message: "Standards YAML validated and saved." });
    } finally {
      setSavingYaml(false);
    }
  }

  function copyText(label: string, value: string) {
    void navigator.clipboard.writeText(value).then(
      () => pushToast({ kind: "success", title: "Copied", message: `${label} copied.` }),
      () => pushToast({ kind: "error", title: "Copy failed", message: "Clipboard blocked." }),
    );
  }

  if (loading) {
    return <div className="eq-skeleton" style={{ height: 14, width: 200 }} />;
  }

  const session = loadSession();

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div
            className="eq-text-xs eq-text-muted"
            style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}
          >
            Settings
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Workspace settings
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            API access, review behaviour, notifications, and standards YAML.
          </p>
        </div>
        <Link href="/notifications" className="eq-btn eq-btn--secondary">
          View activity →
        </Link>
      </div>

      <div style={{ display: "grid", gap: 16, maxWidth: 720 }}>
        <section className="eq-card">
          <h2 className="eq-h3">Account</h2>
          {account ? (
            <table className="eq-table" style={{ marginTop: 14 }}>
              <tbody>
                {[
                  ["Company", account.company_name],
                  ["Plan", account.plan],
                  ["Status", account.status],
                  ["Contact email", account.contact_email ?? "—"],
                  ["GitHub org", account.github_org ?? "—"],
                  ["Tenant ID", account.tenant_id],
                ].map(([k, v]) => (
                  <tr key={k}>
                    <td style={{ paddingLeft: 16, width: "40%" }} className="eq-text-sm eq-text-muted">
                      {k}
                    </td>
                    <td style={{ paddingRight: 16 }} className="eq-text-sm eq-font-mono">
                      {v}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
          <p className="eq-text-xs eq-text-dim" style={{ marginTop: 12 }}>
            GitHub App:{" "}
            {account?.github_app_connected ? (
              <Link href="/installations">Connected</Link>
            ) : (
              <Link href="/onboarding">Complete setup</Link>
            )}
          </p>
        </section>

        <section className="eq-card">
          <h2 className="eq-h3">API access</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
            Your API key is shown once at registration. Use the key stored in this browser session for portal sign-in.
          </p>
          <div className="eq-tok-box" style={{ marginTop: 12 }}>
            {showSessionKey && session ? session.apiKey : sessionKeyMasked ?? "Sign in to view"}
          </div>
          <div className="eq-row" style={{ marginTop: 12, gap: 10, flexWrap: "wrap" }}>
            {session ? (
              <>
                <button
                  type="button"
                  className="eq-btn eq-btn--secondary"
                  onClick={() => setShowSessionKey((v) => !v)}
                >
                  {showSessionKey ? "Hide key" : "Show key from session"}
                </button>
                <button
                  type="button"
                  className="eq-btn eq-btn--secondary"
                  onClick={() => copyText("API key", session.apiKey)}
                >
                  Copy API key
                </button>
                <button
                  type="button"
                  className="eq-btn eq-btn--secondary"
                  onClick={() => copyText("Tenant ID", session.tenantId)}
                >
                  Copy tenant ID
                </button>
              </>
            ) : null}
          </div>
        </section>

        {prefs ? (
          <>
            <section className="eq-card">
              <h2 className="eq-h3">Default review settings</h2>
              <p className="eq-text-xs eq-text-dim" style={{ marginTop: 8 }}>
                Stored on your tenant profile (full enforcement rules belong in YAML below).
              </p>
              <div style={{ marginTop: 8 }}>
                <ToggleRow
                  label="Review all pull requests"
                  checked={prefs.review_all_pull_requests}
                  disabled={savingPrefs}
                  onChange={(v) => void patchPref({ review_all_pull_requests: v })}
                />
                <ToggleRow
                  label="Skip draft PRs"
                  checked={prefs.skip_draft_pull_requests}
                  disabled={savingPrefs}
                  onChange={(v) => void patchPref({ skip_draft_pull_requests: v })}
                />
                <ToggleRow
                  label="Enforce .cursorrules on all repos"
                  description="When enabled, reviews reference repository cursor rules where present."
                  checked={prefs.enforce_cursorrules}
                  disabled={savingPrefs}
                  onChange={(v) => void patchPref({ enforce_cursorrules: v })}
                />
                <ToggleRow
                  label="Monetary type safety checks"
                  description="Flag decimal types used for money — prefer integer cents."
                  checked={prefs.monetary_type_safety_checks}
                  disabled={savingPrefs}
                  onChange={(v) => void patchPref({ monetary_type_safety_checks: v })}
                />
              </div>
            </section>

            <section className="eq-card">
              <div className="eq-row">
                <h2 className="eq-h3">Notifications</h2>
                <Link href="/notifications" className="eq-text-xs" style={{ color: "var(--eq-accent-light)" }}>
                  Activity feed
                </Link>
              </div>
              <p className="eq-text-xs eq-text-dim" style={{ marginTop: 8 }}>
                Email delivery uses your contact address ({account?.contact_email ?? "on file"}). Activity history is in
                the portal feed.
              </p>
              <div style={{ marginTop: 8 }}>
                <ToggleRow
                  label="Email on critical issues"
                  checked={prefs.email_on_critical_issues}
                  disabled={savingPrefs}
                  onChange={(v) => void patchPref({ email_on_critical_issues: v })}
                />
                <ToggleRow
                  label="Weekly digest"
                  description="Summary of reviews and findings (when enabled for your plan)."
                  checked={prefs.weekly_digest}
                  disabled={savingPrefs}
                  onChange={(v) => void patchPref({ weekly_digest: v })}
                />
              </div>
            </section>
          </>
        ) : null}

        <section className="eq-card">
          <h2 className="eq-h3">GitHub webhook</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
            EngineIQ receives GitHub App events at this endpoint. It is configured automatically when you install the
            GitHub App — no manual setup required.
          </p>
          <div className="eq-tok-box" style={{ marginTop: 12 }}>
            {webhookUrl}
          </div>
          <button
            type="button"
            className="eq-btn eq-btn--secondary"
            style={{ marginTop: 12 }}
            onClick={() => copyText("Webhook URL", webhookUrl)}
          >
            Copy URL
          </button>
        </section>
      </div>

      <section className="eq-card" style={{ marginTop: 16 }}>
        <div className="eq-row">
          <h2 className="eq-h3">Standards config (YAML)</h2>
          {account?.has_config_yaml ? <span className="eq-badge eq-badge--grey">Saved</span> : null}
        </div>
        <p className="eq-text-sm eq-text-muted" style={{ margin: "12px 0 0" }}>
          Architecture rules and custom standards. Validated before save (<code>version: 1</code> required).
        </p>
        <textarea
          value={yaml}
          onChange={(e) => setYaml(e.target.value)}
          rows={14}
          className="eq-input eq-font-mono"
          style={{ marginTop: 14, height: "auto", padding: 12, minHeight: 280, resize: "vertical", width: "100%" }}
          placeholder={`version: 1\nrules:\n  - id: example\n    severity: warn\n    message: Example rule\n`}
        />
        {yamlMsg ? (
          <p className="eq-text-sm" style={{ marginTop: 12, color: "var(--eq-green)" }}>
            {yamlMsg}
          </p>
        ) : null}
        {yamlErr ? (
          <p className="eq-text-sm" style={{ marginTop: 12, color: "var(--eq-red)" }}>
            {yamlErr}
          </p>
        ) : null}
        <div className="eq-row" style={{ justifyContent: "flex-end", marginTop: 12 }}>
          <button
            type="button"
            onClick={() => void saveYaml()}
            className="eq-btn eq-btn--primary"
            disabled={savingYaml}
          >
            {savingYaml ? "Saving…" : "Validate & save"}
          </button>
        </div>
      </section>
    </div>
  );
}
