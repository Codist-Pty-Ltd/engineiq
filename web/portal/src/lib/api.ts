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
