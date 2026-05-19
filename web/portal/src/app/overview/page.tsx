"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import {
  CartesianGrid,
  Legend,
  Line,
  LineChart,
  ResponsiveContainer,
  Tooltip,
  XAxis,
  YAxis,
} from "recharts";
import { tenantGet } from "@/lib/api";
import { loadSession } from "@/lib/auth";

type Analytics = {
  days: number;
  prs_reviewed_in_period: number;
  violations_in_period: number;
  prs_reviewed_per_day: { date: string; count: number }[];
  violations_per_day: { date: string; count: number }[];
  architecture_drift_score: number;
  architecture_drift_note: string;
};

export default function OverviewPage() {
  const [data, setData] = useState<Analytics | null>(null);
  const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    const s = loadSession();
    if (!s) return;
    (async () => {
      const res = await tenantGet(s.tenantId, s.apiKey, "/analytics?days=30");
      if (!res.ok) {
        setErr(`Could not load analytics (${res.status})`);
        return;
      }
      setData((await res.json()) as Analytics);
    })();
  }, []);

  const chartData =
    data?.prs_reviewed_per_day.map((p, i) => ({
      date: p.date.slice(5),
      prs: p.count,
      violations: data.violations_per_day[i]?.count ?? 0,
    })) ?? [];

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
            Analytics
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Overview
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            Last 30 days · UTC
          </p>
        </div>
        <Link href="/dashboard" className="eq-btn eq-btn--secondary eq-text-sm">
          ← Dashboard
        </Link>
      </div>

      {err && (
        <div className="eq-card" style={{ borderColor: "rgba(239, 68, 68, 0.35)", padding: 14 }}>
          <div className="eq-text-sm" style={{ color: "var(--eq-red)" }}>
            {err}
          </div>
        </div>
      )}

      {!data && !err && (
        <div className="eq-grid-3">
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="eq-card">
              <div className="eq-skeleton" style={{ height: 12, width: 120 }} />
              <div className="eq-skeleton" style={{ height: 28, width: 80, marginTop: 12 }} />
              <div className="eq-skeleton" style={{ height: 10, width: 220, marginTop: 12 }} />
            </div>
          ))}
        </div>
      )}

      {data && (
        <>
          <div className="eq-grid-3">
            <div className="eq-card">
              <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                PRs reviewed
              </div>
              <div className="eq-text-2xl" style={{ marginTop: 12, fontWeight: 600, color: "var(--eq-accent-light)" }}>
                {data.prs_reviewed_in_period}
              </div>
            </div>
            <div className="eq-card">
              <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                Findings logged
              </div>
              <div className="eq-text-2xl" style={{ marginTop: 12, fontWeight: 600, color: "var(--eq-amber)" }}>
                {data.violations_in_period}
              </div>
            </div>
            <div className="eq-card">
              <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                Architecture drift
              </div>
              <div className="eq-row" style={{ marginTop: 12, alignItems: "baseline" }}>
                <div className="eq-text-2xl" style={{ fontWeight: 600 }}>
                  {data.architecture_drift_score}
                </div>
                <span className="eq-badge eq-badge--grey">0–100</span>
              </div>
              <p className="eq-text-xs eq-text-dim" style={{ margin: "10px 0 0" }}>
                {data.architecture_drift_note}
              </p>
            </div>
          </div>

          <div className="eq-card" style={{ marginTop: 16 }}>
            <div className="eq-row" style={{ alignItems: "center" }}>
              <div>
                <div className="eq-text-xs eq-text-muted" style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}>
                  Trend
                </div>
                <div className="eq-text-sm eq-text-muted" style={{ marginTop: 8 }}>
                  PRs reviewed vs findings per day
                </div>
              </div>
              <span className="eq-badge eq-badge--purple">30d</span>
            </div>

            <div style={{ height: 320, marginTop: 16 }}>
              <ResponsiveContainer width="100%" height="100%">
                <LineChart data={chartData}>
                  <CartesianGrid strokeDasharray="3 3" stroke="rgba(63,63,70,0.9)" />
                  <XAxis dataKey="date" stroke="#A1A1AA" fontSize={12} />
                  <YAxis stroke="#A1A1AA" fontSize={12} />
                  <Tooltip
                    contentStyle={{ background: "#111113", border: "1px solid #27272A" }}
                    labelStyle={{ color: "#FAFAFA" }}
                  />
                  <Legend />
                  <Line type="monotone" dataKey="prs" name="PRs" stroke="#8B5CF6" dot={false} />
                  <Line type="monotone" dataKey="violations" name="Findings" stroke="#F59E0B" dot={false} />
                </LineChart>
              </ResponsiveContainer>
            </div>
          </div>
        </>
      )}
    </div>
  );
}
