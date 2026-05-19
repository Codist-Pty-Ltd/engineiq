"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { useCallback, useEffect, useMemo, useState } from "react";
import { RepoSidebar } from "@/components/dashboard/RepoSidebar";
import { ReviewCard } from "@/components/dashboard/ReviewCard";
import { tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";
import type { Analytics, JobRow, JobsPage, RepoRow } from "@/lib/portal-types";
import { matchesReviewSearch } from "@/lib/portal-utils";
import "../portal-dashboard.css";

export function DashboardView() {
  const router = useRouter();
  const [repos, setRepos] = useState<RepoRow[]>([]);
  const [jobs, setJobs] = useState<JobRow[]>([]);
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [criticalCount, setCriticalCount] = useState<number | null>(null);
  const [activeRepo, setActiveRepo] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    const s = loadSession();
    if (!s) return;
    setLoading(true);
    setErr(null);

    const [reposRes, jobsRes, analyticsRes, criticalRes] = await Promise.all([
      tenantGet(s.tenantId, s.apiKey, "/repositories"),
      tenantGet(s.tenantId, s.apiKey, "/jobs?status=Completed&take=50&skip=0"),
      tenantGet(s.tenantId, s.apiKey, "/analytics?days=30"),
      tenantGet(s.tenantId, s.apiKey, "/findings?severity=critical&take=1&skip=0"),
    ]);

    if (!reposRes.ok || !jobsRes.ok) {
      setErr(`Could not load dashboard (${reposRes.status}/${jobsRes.status})`);
      setLoading(false);
      return;
    }

    setRepos((await reposRes.json()) as RepoRow[]);
    const jobsPage = (await jobsRes.json()) as JobsPage;
    setJobs(jobsPage.items);

    if (analyticsRes.ok) setAnalytics((await analyticsRes.json()) as Analytics);
    if (criticalRes.ok) {
      const c = (await criticalRes.json()) as { total_count: number };
      setCriticalCount(c.total_count);
    }

    setLoading(false);
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const filteredJobs = useMemo(() => {
    let list = jobs;
    if (activeRepo) list = list.filter((j) => j.repository_full_name === activeRepo);
    if (search.trim()) list = list.filter((j) => matchesReviewSearch(j, search));
    return list;
  }, [jobs, activeRepo, search]);

  function openReview(jobId: string) {
    router.push(`/dashboard/reviews?job=${encodeURIComponent(jobId)}`);
  }

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
            Dashboard
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Code reviews
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            Recent PR reviews from your connected repositories — metadata only, no source stored.
          </p>
        </div>
        <Link href="/overview" className="eq-btn eq-btn--secondary eq-text-sm">
          Analytics chart
        </Link>
      </div>

      {err ? (
        <div className="eq-card" style={{ borderColor: "rgba(239, 68, 68, 0.35)", padding: 14, marginBottom: 16 }}>
          <p className="eq-text-sm" style={{ color: "var(--eq-red)" }}>
            {err}
          </p>
        </div>
      ) : null}

      <div className="eq-dash-layout">
        <RepoSidebar repos={repos} activeRepo={activeRepo} onSelect={setActiveRepo} />

        <div className="eq-dash-main">
          <div className="eq-stat-row">
            <div className="eq-stat-tile">
              <div className="eq-stat-tile__label">Reviews (30d)</div>
              <div className="eq-stat-tile__value" style={{ color: "var(--eq-accent-light)" }}>
                {loading ? "—" : (analytics?.prs_reviewed_in_period ?? 0)}
              </div>
            </div>
            <div className="eq-stat-tile">
              <div className="eq-stat-tile__label">Issues found (30d)</div>
              <div className="eq-stat-tile__value" style={{ color: "var(--eq-amber)" }}>
                {loading ? "—" : (analytics?.violations_in_period ?? 0)}
              </div>
            </div>
            <div className="eq-stat-tile">
              <div className="eq-stat-tile__label">Critical (all time)</div>
              <div className="eq-stat-tile__value" style={{ color: "var(--eq-red)" }}>
                {loading ? "—" : (criticalCount ?? 0)}
              </div>
            </div>
          </div>

          <div className="eq-search-bar">
            <span className="eq-text-dim eq-text-sm" aria-hidden>
              ⌕
            </span>
            <input
              type="search"
              placeholder="Search by repo, PR number, or status…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              aria-label="Search reviews"
            />
          </div>

          <div className="eq-text-sm eq-text-muted" style={{ marginBottom: 10, fontWeight: 500 }}>
            Recent reviews
          </div>

          {loading ? (
            <div className="eq-card">
              <div className="eq-skeleton" style={{ height: 14, width: "100%" }} />
              <div className="eq-skeleton" style={{ height: 14, width: "80%", marginTop: 10 }} />
            </div>
          ) : filteredJobs.length === 0 ? (
            <div className="eq-card" style={{ padding: 32, textAlign: "center" }}>
              <p className="eq-text-sm eq-text-muted">
                {jobs.length === 0
                  ? "No completed reviews yet — open a PR after installing the GitHub App."
                  : "No reviews match this filter."}
              </p>
              <Link href="/settings" className="eq-btn eq-btn--secondary" style={{ marginTop: 16 }}>
                GitHub App settings
              </Link>
            </div>
          ) : (
            filteredJobs.map((j) => <ReviewCard key={j.job_id} job={j} onClick={() => openReview(j.job_id)} />)
          )}
        </div>
      </div>
    </div>
  );
}
