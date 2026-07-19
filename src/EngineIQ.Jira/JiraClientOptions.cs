namespace EngineIQ.Jira;

public class JiraClientOptions
{
    public const string SectionName = "Jira";

    public int TimeoutSeconds { get; set; } = 15;

    public string UserAgent { get; set; } = "EngineIQ/1.0";
}
