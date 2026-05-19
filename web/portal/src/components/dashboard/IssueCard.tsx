"use client";

import type { FindingRow } from "@/lib/portal-types";
import { severityClass, severityLabel } from "@/lib/portal-utils";

type Props = {
  finding: FindingRow;
};

export function IssueCard({ finding }: Props) {
  const fileLine =
    finding.line_number != null ? `${finding.file_path} · Line ${finding.line_number}` : finding.file_path;

  return (
    <article className="eq-iss-card">
      <div className={`eq-iss-card__head ${severityClass(finding.severity)}`} style={{ display: "inline-block", marginBottom: 8 }}>
        {severityLabel(finding.severity, finding.category)}
      </div>
      <div className="eq-iss-card__file">{fileLine}</div>
      <p className="eq-iss-card__body">{finding.message}</p>
      {finding.rule_id ? (
        <p className="eq-text-xs eq-text-dim eq-font-mono" style={{ marginTop: 8 }}>
          Rule: {finding.rule_id}
        </p>
      ) : null}
    </article>
  );
}
