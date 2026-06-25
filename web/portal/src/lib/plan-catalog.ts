/** Mirrors `PlanCatalog` in the API — display-only; enforcement lives server-side. */
export type ProductPlan = "Starter" | "Growth" | "Scale" | "Enterprise";

export type PlanTier = {
  id: ProductPlan;
  label: string;
  priceZar: string;
  maxRepos: number;
  blurb: string;
};

export const PLAN_TIERS: PlanTier[] = [
  {
    id: "Starter",
    label: "Starter",
    priceZar: "R1 999",
    maxRepos: 5,
    blurb: "Small teams getting consistent PR reviews.",
  },
  {
    id: "Growth",
    label: "Growth",
    priceZar: "R4 999",
    maxRepos: 25,
    blurb: "Scaling engineering orgs with higher PR volume.",
  },
  {
    id: "Scale",
    label: "Scale",
    priceZar: "R9 999",
    maxRepos: -1,
    blurb: "High-volume teams with unlimited repositories.",
  },
  {
    id: "Enterprise",
    label: "Enterprise",
    priceZar: "R15 000",
    maxRepos: -1,
    blurb: "Dedicated support, SSO, and custom policies.",
  },
];

const PLAN_ORDER: ProductPlan[] = ["Starter", "Growth", "Scale", "Enterprise"];

export function normalizePlan(plan: string): ProductPlan {
  const p = plan.trim();
  if (p.toLowerCase() === "enterprise") return "Enterprise";
  if (p.toLowerCase() === "scale") return "Scale";
  if (p.toLowerCase() === "growth") return "Growth";
  return "Starter";
}

export function maxReposForPlan(plan: string): number {
  const tier = PLAN_TIERS.find((t) => t.id === normalizePlan(plan));
  return tier?.maxRepos ?? 5;
}

export function formatRepoLimit(maxRepos: number): string {
  return maxRepos < 0 ? "Unlimited" : String(maxRepos);
}

export function comparePlans(a: string, b: string): number {
  return PLAN_ORDER.indexOf(normalizePlan(a)) - PLAN_ORDER.indexOf(normalizePlan(b));
}

export function billingStatusLabel(status: string): string {
  switch (status) {
    case "Trialing":
      return "Trial";
    case "Active":
      return "Active";
    case "PastDue":
      return "Past due";
    case "Cancelled":
      return "Cancelled";
    case "Internal":
      return "Internal";
    default:
      return status;
  }
}

export function billingStatusBadgeClass(status: string): string {
  switch (status) {
    case "Active":
      return "eq-badge eq-badge--green";
    case "Trialing":
      return "eq-badge eq-badge--purple";
    case "PastDue":
      return "eq-badge eq-badge--amber";
    case "Cancelled":
      return "eq-badge eq-badge--red";
    case "Internal":
      return "eq-badge eq-badge--grey";
    default:
      return "eq-badge eq-badge--grey";
  }
}

export function trialDaysRemaining(trialEndsAt: string | null): number | null {
  if (!trialEndsAt) return null;
  const end = new Date(trialEndsAt);
  if (Number.isNaN(end.getTime())) return null;
  const ms = end.getTime() - Date.now();
  return Math.max(0, Math.ceil(ms / (1000 * 60 * 60 * 24)));
}
