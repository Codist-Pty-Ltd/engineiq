"use client";

import type { JobRow } from "@/lib/portal-types";
import { formatRelativeTime } from "@/lib/portal-utils";

type Props = {
  job: JobRow;
  onClick: () => void;
};

export function ReviewCard({ job, onClick }: Props) {
  const when = job.completed_at ?? job.created_at;
  const title = `PR #${job.pr_number}`;

  return (
    <button type="button" className="eq-rv-card" onClick={onClick} aria-label={`Review ${job.repository_full_name} ${title}`}>
      <div className="eq-rv-card__repo eq-font-mono">{job.repository_full_name}</div>
      <div className="eq-rv-card__title">{title}</div>
      <div className="eq-rv-card__foot">
        <span
          className={`eq-badge ${
            job.status === "Completed" ? "eq-badge--green" : job.status === "Failed" ? "eq-badge--red" : "eq-badge--grey"
          }`}
        >
          {job.status}
        </span>
        {job.findings_count > 0 ? (
          <span className="eq-text-xs eq-text-muted">
            {job.findings_count} finding{job.findings_count === 1 ? "" : "s"}
          </span>
        ) : null}
        <span className="eq-rv-card__time">{formatRelativeTime(when)}</span>
      </div>
    </button>
  );
}
