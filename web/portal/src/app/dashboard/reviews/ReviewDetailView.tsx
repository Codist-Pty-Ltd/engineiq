"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useState } from "react";
import { IssueCard } from "@/components/dashboard/IssueCard";
import { tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";
import type { FindingRow, FindingsList, JobRow } from "@/lib/portal-types";
import { formatRelativeTime, githubPullRequestUrl, severityClass } from "@/lib/portal-utils";
import "../../portal-dashboard.css";

function ReviewDetailInner() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const jobId = searchParams.get("job");

  const [job, setJob] = useState<JobRow | null>(null);
  const [findings, setFindings] = useState<FindingRow[]>([]);
  const [err, setErr] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    if (!jobId) {
      router.replace("/dashboard");
      return;
    }

    const s = loadSession();
    if (!s) return;

    (async () => {
      setLoading(true);
      setErr(null);

      const [jobRes, findingsRes] = await Promise.all([
        tenantGet(s.tenantId, s.apiKey, `/jobs/${jobId}`),
        tenantGet(s.tenantId, s.apiKey, `/jobs/${jobId}/findings`),
      ]);

      if (!jobRes.ok) {
        setErr(`Review not found (${jobRes.status})`);
        setLoading(false);
        return;
      }

      setJob((await jobRes.json()) as JobRow);

      if (findingsRes.ok) {
        const list = (await findingsRes.json()) as FindingsList;
        setFindings(list.items);
      }

      setLoading(false);
    })();
  }, [jobId, router]);

  if (!jobId) return null;

  if (loading) {
    return (
      <div className="eq-card">
        <div className="eq-skeleton" style={{ height: 14, width: 220 }} />
        <div className="eq-skeleton" style={{ height: 12, width: 320, marginTop: 12 }} />
      </div>
    );
  }

  if (err || !job) {
    return (
      <div>
        <button type="button" className="eq-det-back" onClick={() => router.push("/dashboard")}>
          ← Back to dashboard
        </button>
        <div className="eq-card" style={{ borderColor: "rgba(239, 68, 68, 0.35)", padding: 14 }}>
          <p className="eq-text-sm" style={{ color: "var(--eq-red)" }}>
            {err ?? "Review not found"}
          </p>
        </div>
      </div>
    );
  }

  const when = job.completed_at ?? job.created_at;
  const ghUrl = githubPullRequestUrl(job.repository_full_name, job.pr_number);
  const topSeverity = findings[0]?.severity;

  return (
    <div>
      <button type="button" className="eq-det-back" onClick={() => router.push("/dashboard")}>
        ← Back to dashboard
      </button>

      <h1 className="eq-h2">PR #{job.pr_number}</h1>
      <p className="eq-text-sm eq-text-dim eq-font-mono" style={{ marginTop: 6 }}>
        {job.repository_full_name} · {formatRelativeTime(when)}
        {job.duration_ms != null ? ` · ${job.duration_ms} ms` : ""}
      </p>

      <div
        className="eq-card"
        style={{
          marginTop: 16,
          marginBottom: 16,
          padding: 14,
          background: "var(--eq-surface)",
        }}
      >
        <p className="eq-text-sm eq-text-muted" style={{ lineHeight: 1.6 }}>
          {findings.length === 0
            ? "Review completed with no persisted findings for this job."
            : `${findings.length} finding${findings.length === 1 ? "" : "s"} from this review (metadata only — no source code stored).`}
        </p>
      </div>

      <div className="eq-row" style={{ gap: 8, marginBottom: 18, flexWrap: "wrap" }}>
        <a href={ghUrl} target="_blank" rel="noopener noreferrer" className="eq-btn eq-btn--secondary eq-text-sm">
          View on GitHub
        </a>
        <Link
          href={`/findings?file=${encodeURIComponent(job.repository_full_name)}`}
          className="eq-btn eq-btn--secondary eq-text-sm"
        >
          All findings
        </Link>
        {topSeverity ? (
          <span className={severityClass(topSeverity)} style={{ marginLeft: "auto" }}>
            Top: {topSeverity}
          </span>
        ) : null}
      </div>

      {findings.length === 0 ? (
        <p className="eq-text-sm eq-text-muted">No issue rows to display.</p>
      ) : (
        findings.map((f) => <IssueCard key={f.id} finding={f} />)
      )}
    </div>
  );
}

export function ReviewDetailView() {
  return (
    <Suspense
      fallback={
        <div className="eq-card">
          <div className="eq-skeleton" style={{ height: 14, width: 220 }} />
        </div>
      }
    >
      <ReviewDetailInner />
    </Suspense>
  );
}
