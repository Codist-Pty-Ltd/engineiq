export function apiBase() {
  return process.env.NEXT_PUBLIC_ENGINEIQ_API_URL ?? "http://localhost:5056";
}

export async function tenantGet(
  tenantId: string,
  apiKey: string,
  path: string,
  init?: RequestInit,
) {
  return fetch(`${apiBase()}/api/v1/tenant/${tenantId}${path}`, {
    ...init,
    headers: {
      "X-Api-Key": apiKey,
      ...init?.headers,
    },
  });
}

export type PortalPreferences = {
  review_all_pull_requests: boolean;
  skip_draft_pull_requests: boolean;
  enforce_cursorrules: boolean;
  monetary_type_safety_checks: boolean;
  email_on_critical_issues: boolean;
  weekly_digest: boolean;
};

export async function tenantPatch(
  tenantId: string,
  apiKey: string,
  path: string,
  body: unknown,
  init?: RequestInit,
) {
  return fetch(`${apiBase()}/api/v1/tenant/${tenantId}${path}`, {
    method: "PATCH",
    ...init,
    headers: {
      "X-Api-Key": apiKey,
      "Content-Type": "application/json",
      ...init?.headers,
    },
    body: JSON.stringify(body),
  });
}

export async function tenantPost(
  tenantId: string,
  apiKey: string,
  path: string,
  body: unknown,
  init?: RequestInit,
) {
  return fetch(`${apiBase()}/api/v1/tenant/${tenantId}${path}`, {
    method: "POST",
    ...init,
    headers: {
      "X-Api-Key": apiKey,
      "Content-Type": "application/json",
      ...init?.headers,
    },
    body: JSON.stringify(body),
  });
}

export type TenantBilling = {
  plan: string;
  billing_status: string;
  trial_ends_at: string | null;
  paystack_customer_code: string | null;
  paystack_subscription_code: string | null;
  paystack_required: boolean;
};

export type BillingSubscribeResult = {
  reference: string;
  authorization_url: string;
};

export type BillingConfirmResult = {
  ok: boolean;
  billing_status: string | null;
  paystack_subscription_code: string | null;
  error: string | null;
};

export type BillingChangePlanResult = {
  ok: boolean;
  plan: string | null;
  error: string | null;
};

export async function fetchTenantBilling(tenantId: string, apiKey: string) {
  return tenantGet(tenantId, apiKey, "/billing");
}

export async function subscribeToPlan(
  tenantId: string,
  apiKey: string,
  plan: string,
  callbackUrl: string,
) {
  return tenantPost(tenantId, apiKey, "/billing/subscribe", {
    plan,
    callback_url: callbackUrl,
  });
}

export async function confirmBillingReference(
  tenantId: string,
  apiKey: string,
  reference: string,
) {
  return tenantPost(tenantId, apiKey, "/billing/confirm", { reference });
}

export async function changeBillingPlan(tenantId: string, apiKey: string, plan: string) {
  return tenantPost(tenantId, apiKey, "/billing/change-plan", { plan });
}

export async function postConfigYaml(tenantId: string, apiKey: string, yaml: string) {
  return fetch(`${apiBase()}/api/v1/tenant/${tenantId}/config`, {
    method: "POST",
    headers: {
      "X-Api-Key": apiKey,
      "Content-Type": "text/yaml; charset=utf-8",
    },
    body: yaml,
  });
}
