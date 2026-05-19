export function marketingBaseUrl() {
  return process.env.NEXT_PUBLIC_MARKETING_URL ?? "https://engineiq.co.za";
}

export function signUpUrl() {
  return `${marketingBaseUrl().replace(/\/$/, "")}/sign-up`;
}
