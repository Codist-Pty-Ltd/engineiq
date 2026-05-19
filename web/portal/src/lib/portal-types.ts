export type JobRow = {
  job_id: string;
  repository_full_name: string;
  pr_number: number;
  status: string;
  created_at: string;
  completed_at: string | null;
  duration_ms: number | null;
  findings_count: number;
  input_tokens: number;
  output_tokens: number;
  estimated_cost_zar: number | null;
};

export type JobsPage = {
  total_count: number;
  items: JobRow[];
};

export type FindingRow = {
  id: string;
  severity: string;
  category: string;
  rule_id: string | null;
  source: string;
  file_path: string;
  line_number: number | null;
  message: string;
  was_actioned: boolean;
  pr_merge_status: string;
  created_at: string;
};

export type FindingsList = {
  items: FindingRow[];
};

export type RepoRow = {
  id: string;
  full_name: string;
  job_count: number;
};

export type Analytics = {
  days: number;
  prs_reviewed_in_period: number;
  violations_in_period: number;
  prs_reviewed_per_day: { date: string; count: number }[];
  violations_per_day: { date: string; count: number }[];
  architecture_drift_score: number;
  architecture_drift_note: string;
};
