"use client";

import type { RepoRow } from "@/lib/portal-types";
import { repoShortName } from "@/lib/portal-utils";

type Props = {
  repos: RepoRow[];
  activeRepo: string | null;
  onSelect: (fullName: string | null) => void;
};

export function RepoSidebar({ repos, activeRepo, onSelect }: Props) {
  return (
    <aside className="eq-dash-repos eq-card" style={{ padding: "12px 0" }} aria-label="Repositories">
      <div className="eq-text-xs eq-text-dim" style={{ padding: "0 14px 8px", letterSpacing: "0.06em", textTransform: "uppercase" }}>
        Repositories
      </div>
      <button
        type="button"
        className={`eq-repo-item ${activeRepo === null ? "eq-repo-item--active" : ""}`}
        onClick={() => onSelect(null)}
      >
        <span className="eq-repo-dot" style={{ background: "var(--eq-accent-light)" }} aria-hidden />
        All repos
      </button>
      {repos.map((r) => (
        <button
          key={r.id}
          type="button"
          className={`eq-repo-item ${activeRepo === r.full_name ? "eq-repo-item--active" : ""}`}
          onClick={() => onSelect(r.full_name)}
        >
          <span className="eq-repo-dot" aria-hidden />
          <span className="eq-font-mono" style={{ overflow: "hidden", textOverflow: "ellipsis", whiteSpace: "nowrap" }}>
            {repoShortName(r.full_name)}
          </span>
          <span className="eq-repo-count">{r.job_count}</span>
        </button>
      ))}
      {repos.length === 0 ? (
        <p className="eq-text-xs eq-text-dim" style={{ padding: "8px 14px" }}>
          No repos yet — install the GitHub App and open a PR.
        </p>
      ) : null}
    </aside>
  );
}
