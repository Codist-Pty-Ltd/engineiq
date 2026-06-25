"use client";

import Link from "next/link";
import { useCallback, useEffect, useState } from "react";
import { useSearchParams } from "next/navigation";
import {
  changeBillingPlan,
  confirmBillingReference,
  fetchTenantBilling,
  subscribeToPlan,
  tenantGet,
  type TenantBilling,
} from "@/lib/api";
import { loadSession } from "@/lib/auth";
import {
  PLAN_TIERS,
  billingStatusBadgeClass,
  billingStatusLabel,
  comparePlans,
  formatRepoLimit,
  maxReposForPlan,
  normalizePlan,
  trialDaysRemaining,
  type ProductPlan,
} from "@/lib/plan-catalog";
import { useToasts } from "@/components/Toasts";
import "../portal-billing.css";

function usageBarClass(used: number, limit: number): string {
  if (limit < 0) return "eq-usage-bar__fill";
  if (used > limit) return "eq-usage-bar__fill eq-usage-bar__fill--over";
  if (used >= limit * 0.85) return "eq-usage-bar__fill eq-usage-bar__fill--warn";
  return "eq-usage-bar__fill";
}

function usagePercent(used: number, limit: number): number {
  if (limit < 0) return used > 0 ? 35 : 0;
  if (limit === 0) return 100;
  return Math.min(100, Math.round((used / limit) * 100));
}

export function BillingView() {
  const searchParams = useSearchParams();
  const { pushToast } = useToasts();
  const [billing, setBilling] = useState<TenantBilling | null>(null);
  const [repoCount, setRepoCount] = useState(0);
  const [loading, setLoading] = useState(true);
  const [actionPlan, setActionPlan] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  const load = useCallback(async () => {
    const s = loadSession();
    if (!s) return;
    const [billingRes, statusRes] = await Promise.all([
      fetchTenantBilling(s.tenantId, s.apiKey),
      tenantGet(s.tenantId, s.apiKey, "/status"),
    ]);
    if (billingRes.ok) setBilling((await billingRes.json()) as TenantBilling);
    if (statusRes.ok) {
      const st = (await statusRes.json()) as { repositories_detected?: number };
      setRepoCount(st.repositories_detected ?? 0);
    }
  }, []);

  useEffect(() => {
    (async () => {
      await load();
      setLoading(false);
    })();
  }, [load]);

  useEffect(() => {
    const reference = searchParams.get("reference") ?? searchParams.get("trxref");
    if (!reference || confirming) return;
    const s = loadSession();
    if (!s) return;

    setConfirming(true);
    (async () => {
      try {
        const res = await confirmBillingReference(s.tenantId, s.apiKey, reference);
        const body = (await res.json()) as { ok?: boolean; error?: string };
        if (res.ok && body.ok) {
          pushToast({
            kind: "success",
            title: "Payment confirmed",
            message: "Your subscription is active. Reviews will resume shortly.",
          });
          await load();
          window.history.replaceState({}, "", "/billing");
        } else {
          pushToast({
            kind: "error",
            title: "Payment not confirmed",
            message: body.error ?? `API returned ${res.status}.`,
          });
        }
      } catch {
        pushToast({ kind: "error", title: "Network error", message: "Could not confirm payment." });
      } finally {
        setConfirming(false);
      }
    })();
  }, [searchParams, confirming, load, pushToast]);

  async function startCheckout(plan: ProductPlan) {
    const s = loadSession();
    if (!s || !billing) return;
    setActionPlan(plan);
    try {
      const callbackUrl = `${window.location.origin}/billing`;
      const res = await subscribeToPlan(s.tenantId, s.apiKey, plan, callbackUrl);
      const body = (await res.json()) as { authorization_url?: string; error?: string };
      if (!res.ok || !body.authorization_url) {
        pushToast({
          kind: "error",
          title: "Checkout unavailable",
          message: body.error ?? `API returned ${res.status}.`,
        });
        return;
      }
      window.location.href = body.authorization_url;
    } catch {
      pushToast({ kind: "error", title: "Network error", message: "Could not start Paystack checkout." });
    } finally {
      setActionPlan(null);
    }
  }

  async function applyPlanChange(plan: ProductPlan) {
    const s = loadSession();
    if (!s || !billing) return;
    setActionPlan(plan);
    try {
      const hasSubscription =
        Boolean(billing.paystack_subscription_code) && billing.billing_status === "Active";

      if (hasSubscription) {
        const res = await changeBillingPlan(s.tenantId, s.apiKey, plan);
        const body = (await res.json()) as { ok?: boolean; error?: string };
        if (!res.ok || !body.ok) {
          pushToast({
            kind: "error",
            title: "Plan change failed",
            message: body.error ?? `API returned ${res.status}.`,
          });
          return;
        }
        pushToast({ kind: "success", title: "Plan updated", message: `You are now on ${plan}.` });
        await load();
        return;
      }

      await startCheckout(plan);
    } finally {
      setActionPlan(null);
    }
  }

  if (loading) {
    return <div className="eq-skeleton" style={{ height: 14, width: 200 }} />;
  }

  if (!billing) {
    return (
      <div className="eq-card" style={{ padding: 16 }}>
        <p className="eq-text-sm" style={{ color: "var(--eq-red)" }}>
          Could not load billing details.
        </p>
      </div>
    );
  }

  const currentPlan = normalizePlan(billing.plan);
  const maxRepos = maxReposForPlan(billing.plan);
  const trialDays = trialDaysRemaining(billing.trial_ends_at);
  const isInternal = billing.billing_status === "Internal" || !billing.paystack_required;
  const isPastDue = billing.billing_status === "PastDue";
  const isCancelled = billing.billing_status === "Cancelled";
  const needsPayment = isPastDue || isCancelled || billing.billing_status === "Trialing";

  return (
    <div>
      <div className="eq-pagehead">
        <div>
          <div
            className="eq-text-xs eq-text-muted"
            style={{ letterSpacing: "0.08em", textTransform: "uppercase" }}
          >
            Billing
          </div>
          <h1 className="eq-h2" style={{ marginTop: 10 }}>
            Plan &amp; subscription
          </h1>
          <p className="eq-text-sm eq-text-muted" style={{ margin: "10px 0 0" }}>
            Manage your EngineIQ plan. Card details are collected securely by Paystack — we never store them.
          </p>
        </div>
        <Link href="/settings" className="eq-btn eq-btn--secondary">
          Settings →
        </Link>
      </div>

      {isPastDue ? (
        <div className="eq-billing-banner" role="alert">
          <div>
            <div className="eq-billing-banner__title">Reviews paused — update payment</div>
            <p className="eq-text-sm eq-text-muted" style={{ margin: "6px 0 0" }}>
              Your account is past due. New PR reviews are suspended until payment succeeds.
            </p>
          </div>
          {!isInternal ? (
            <button
              type="button"
              className="eq-btn eq-btn--primary"
              disabled={actionPlan !== null}
              onClick={() => void startCheckout(currentPlan)}
            >
              {actionPlan ? "Redirecting…" : "Update payment"}
            </button>
          ) : null}
        </div>
      ) : null}

      {isCancelled && !isPastDue ? (
        <div className="eq-billing-banner eq-billing-banner--danger" role="alert">
          <div>
            <div className="eq-billing-banner__title">Subscription cancelled</div>
            <p className="eq-text-sm eq-text-muted" style={{ margin: "6px 0 0" }}>
              Reactivate to resume automated PR reviews.
            </p>
          </div>
          {!isInternal ? (
            <button
              type="button"
              className="eq-btn eq-btn--primary"
              disabled={actionPlan !== null}
              onClick={() => void startCheckout(currentPlan)}
            >
              {actionPlan ? "Redirecting…" : "Reactivate"}
            </button>
          ) : null}
        </div>
      ) : null}

      <div className="eq-grid-3" style={{ marginBottom: 20 }}>
        <div className="eq-card eq-billing-metric">
          <div className="eq-billing-metric__label">Current plan</div>
          <div className="eq-billing-metric__value">{currentPlan}</div>
          <span className={billingStatusBadgeClass(billing.billing_status)}>
            {billingStatusLabel(billing.billing_status)}
          </span>
        </div>

        <div className="eq-card eq-billing-metric">
          <div className="eq-billing-metric__label">Repository usage</div>
          <div className="eq-billing-metric__value">
            {repoCount}
            <span className="eq-text-sm eq-text-dim" style={{ fontWeight: 400 }}>
              {" "}
              / {formatRepoLimit(maxRepos)}
            </span>
          </div>
          <div className="eq-usage-bar" aria-hidden="true">
            <div
              className={usageBarClass(repoCount, maxRepos)}
              style={{ width: `${usagePercent(repoCount, maxRepos)}%` }}
            />
          </div>
        </div>

        <div className="eq-card eq-billing-metric">
          <div className="eq-billing-metric__label">
            {billing.billing_status === "Trialing" ? "Trial remaining" : "Billing"}
          </div>
          {billing.billing_status === "Trialing" && trialDays !== null ? (
            <>
              <div className="eq-billing-metric__value">
                {trialDays} <span className="eq-text-sm eq-text-dim" style={{ fontWeight: 400 }}>days</span>
              </div>
              <p className="eq-text-xs eq-text-dim" style={{ margin: 0 }}>
                Ends {billing.trial_ends_at ? new Date(billing.trial_ends_at).toLocaleDateString("en-ZA") : "—"}
              </p>
            </>
          ) : isInternal ? (
            <div className="eq-text-sm eq-text-muted">Managed internally — no Paystack billing.</div>
          ) : (
            <div className="eq-text-sm eq-text-muted">
              {billing.paystack_subscription_code ? "Subscription active on Paystack." : "No active subscription yet."}
            </div>
          )}
        </div>
      </div>

      {isInternal ? (
        <section className="eq-card">
          <h2 className="eq-h3">Internal account</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
            This workspace is on an internal Codist plan. Billing changes are managed by your account team.
          </p>
        </section>
      ) : (
        <section className="eq-card">
          <h2 className="eq-h3">Change plan</h2>
          <p className="eq-text-sm eq-text-muted" style={{ marginTop: 10 }}>
            Upgrades with an active subscription apply immediately. New subscriptions and payment recovery redirect to
            Paystack checkout.
          </p>

          <div className="eq-plan-grid">
            {PLAN_TIERS.map((tier) => {
              const isCurrent = tier.id === currentPlan;
              const cmp = comparePlans(tier.id, currentPlan);
              const actionLabel = isCurrent
                ? "Current plan"
                : cmp > 0
                  ? needsPayment && billing.billing_status !== "Active"
                    ? "Subscribe"
                    : "Upgrade"
                  : "Downgrade";
              const disabled = isCurrent || actionPlan !== null;

              return (
                <div
                  key={tier.id}
                  className={`eq-plan-card${isCurrent ? " eq-plan-card--current" : ""}`}
                >
                  <div className="eq-row" style={{ justifyContent: "space-between", gap: 8 }}>
                    <span className="eq-text-sm" style={{ fontWeight: 600 }}>
                      {tier.label}
                    </span>
                    {isCurrent ? <span className="eq-badge eq-badge--purple">Current</span> : null}
                  </div>
                  <div className="eq-plan-card__price">
                    {tier.priceZar}
                    <span className="eq-text-xs eq-text-dim" style={{ fontWeight: 400 }}>
                      /mo
                    </span>
                  </div>
                  <p className="eq-text-xs eq-text-muted" style={{ margin: 0, flex: 1 }}>
                    {tier.blurb}
                  </p>
                  <p className="eq-text-xs eq-text-dim" style={{ margin: 0 }}>
                    {formatRepoLimit(tier.maxRepos)} repositories
                  </p>
                  <button
                    type="button"
                    className={cmp > 0 ? "eq-btn eq-btn--primary" : "eq-btn eq-btn--secondary"}
                    style={{ width: "100%", justifyContent: "center", marginTop: 4 }}
                    disabled={disabled}
                    onClick={() => void applyPlanChange(tier.id)}
                  >
                    {actionPlan === tier.id ? "Working…" : actionLabel}
                  </button>
                </div>
              );
            })}
          </div>
        </section>
      )}

      {confirming ? (
        <p className="eq-text-sm eq-text-muted" style={{ marginTop: 16 }}>
          Confirming your Paystack payment…
        </p>
      ) : null}
    </div>
  );
}
