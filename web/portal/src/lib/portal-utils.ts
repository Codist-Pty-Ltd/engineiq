export function severityClass(severity: string): string {
  const s = severity.toLowerCase();
  if (s === "critical") return "eq-sev eq-sev--critical";
  if (s === "high") return "eq-sev eq-sev--high";
  if (s === "medium") return "eq-sev eq-sev--medium";
  return "eq-sev eq-sev--low";
}

export function severityLabel(severity: string, category?: string): string {
  const s = severity.charAt(0).toUpperCase() + severity.slice(1).toLowerCase();
  if (category?.trim()) return `${s} — ${category}`;
  return s;
}

export function formatRelativeTime(iso: string): string {
  const then = new Date(iso).getTime();
  const diff = Date.now() - then;
  const mins = Math.floor(diff / 60_000);
  if (mins < 1) return "just now";
  if (mins < 60) return `${mins} min ago`;
  const hours = Math.floor(mins / 60);
  if (hours < 48) return `${hours} hour${hours === 1 ? "" : "s"} ago`;
  const days = Math.floor(hours / 24);
  return `${days} day${days === 1 ? "" : "s"} ago`;
}

export function githubPullRequestUrl(repositoryFullName: string, prNumber: number): string {
  return `https://github.com/${repositoryFullName}/pull/${prNumber}`;
}

export function repoShortName(fullName: string): string {
  const i = fullName.indexOf("/");
  return i >= 0 ? fullName.slice(i + 1) : fullName;
}

export function matchesReviewSearch(job: { repository_full_name: string; pr_number: number; status: string }, q: string): boolean {
  const needle = q.trim().toLowerCase();
  if (!needle) return true;
  return (
    job.repository_full_name.toLowerCase().includes(needle) ||
    String(job.pr_number).includes(needle) ||
    job.status.toLowerCase().includes(needle)
  );
}
