"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";
import { formatRelativeTime } from "@/lib/portal-utils";
import "../portal-settings.css";

type NotificationRow = {
  kind: string;
  title: string;
  subtitle: string;
  occurred_at: string;
  job_id: string | null;
};

function notifStyle(kind: string): { bg: string; glyph: string } {
  switch (kind) {
    case "critical_issue":
      return { bg: "rgba(250, 173, 58, 0.2)", glyph: "!" };
    case "review_complete":
      return { bg: "rgba(16, 185, 129, 0.15)", glyph: "✓" };
    case "review_failed":
      return { bg: "rgba(239, 68, 68, 0.15)", glyph: "×" };
    case "pr_queued":
      return { bg: "rgba(99, 102, 241, 0.15)", glyph: "↗" };
    case "github_connected":
      return { bg: "var(--eq-card)", glyph: "◎" };
    default:
      return { bg: "var(--eq-card)", glyph: "•" };
  }
}

export default function NotificationsPage() {
  const [items, setItems] = useState<NotificationRow[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const s = loadSession();
    if (!s) return;
    (async () => {
      const res = await tenantGet(s.tenantId, s.apiKey, "/notifications?take=50");
      if (res.ok) {
        const data = (await res.json()) as { items: NotificationRow[] };
        setItems(data.items ?? []);
      }
      setLoading(false);
    })();
  }, []);

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div
            className="eq-text-xs eq-text-muted"
            style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}
          >
            Notifications
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Activity
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            Reviews, critical findings, and GitHub events from the last 30 days. Email preferences are in{" "}
            <Link href="/settings" style={{ color: "var(--eq-accent-light)" }}>
              Settings
            </Link>
            .
          </p>
        </div>
      </div>

      <div className="eq-card">
        {loading ? (
          <div className="eq-skeleton" style={{ height: 14, width: 200 }} />
        ) : items.length === 0 ? (
          <p className="eq-text-sm eq-text-muted">
            No activity yet. Connect GitHub and open a pull request to see reviews here.
          </p>
        ) : (
          <div>
            {items.map((n, i) => {
              const style = notifStyle(n.kind);
              const inner = (
                <>
                  <div className="eq-notif-ico" style={{ background: style.bg }}>
                    <span aria-hidden="true">{style.glyph}</span>
                  </div>
                  <div className="eq-notif-body">
                    <div className="eq-notif-title">{n.title}</div>
                    <div className="eq-notif-sub">{n.subtitle}</div>
                  </div>
                  <div className="eq-notif-time">{formatRelativeTime(n.occurred_at)}</div>
                </>
              );
              return n.job_id ? (
                <Link
                  key={`${n.kind}-${n.occurred_at}-${i}`}
                  href={`/dashboard/reviews?job=${encodeURIComponent(n.job_id)}`}
                  className="eq-notif-row"
                  style={{ display: "flex", textDecoration: "none", color: "inherit" }}
                >
                  {inner}
                </Link>
              ) : (
                <div key={`${n.kind}-${n.occurred_at}-${i}`} className="eq-notif-row">
                  {inner}
                </div>
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}
