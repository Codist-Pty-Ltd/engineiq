namespace EngineIQ.Jira;

public class JiraClientOptions
{
    public const string SectionName = "Jira";

    public int TimeoutSeconds { get; set; } = 15;

    public string UserAgent { get; set; } = "EngineIQ/1.0";

    /// <summary>Label that triggers on-demand re-analysis when newly added to an issue.</summary>
    public string TriggerLabel { get; set; } = "engineiq";

    public int MaxBackfillIssues { get; set; } = 100;

    public int BackfillDelayMs { get; set; } = 2000;

    public int BackfillTimeoutMinutes { get; set; } = 30;
}
