export type AdminHealth = {
  database: string
  rabbitMq: string
}

export type AdminPlatformMetrics = {
  prsReviewedToday: number
  avgReviewLatencyMsToday: number | null
  tokenCostZarToday: number
  revenueMrrZar: number
}

export type AdminTenantRow = {
  id: string
  name: string
  plan: string
  status: string
  prCount: number
  lastActive: string | null
  mrrContributionZar: number
}

export type AdminTenantDetail = AdminTenantRow & {
  contactEmail: string | null
  createdAt: string
  gitHubOrgLogin: string | null
  gitHubAppInstallationId: number | null
  featureFlagsJson: string | null
}

export type AdminFindingRow = {
  id: string
  jobId: string
  severity: string
  category: string
  ruleId: string | null
  source: string
  filePath: string
  lineNumber: number | null
  message: string
  wasActioned: boolean
  prMergeStatus: string
  createdAt: string
}

export type AdminFailedJobRow = {
  tenantId: string
  tenantName: string
  jobId: string
  repositoryFullName: string
  prNumber: number
  when: string
}
