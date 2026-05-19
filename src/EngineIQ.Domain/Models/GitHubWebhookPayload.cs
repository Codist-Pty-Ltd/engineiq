using System.Text.Json.Serialization;

namespace EngineIQ.Domain.Models;

/// <summary>
/// Minimal GitHub webhook payload for PR events (opened, synchronize, reopened).
/// </summary>
public class GitHubWebhookPayload
{
    [JsonPropertyName("action")]
    public string? Action { get; set; }

    [JsonPropertyName("installation")]
    public InstallationInfo? Installation { get; set; }

    [JsonPropertyName("pull_request")]
    public PullRequestInfo? PullRequest { get; set; }

    [JsonPropertyName("repository")]
    public RepositoryInfo? Repository { get; set; }
}

public class InstallationInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }
}

public class PullRequestInfo
{
    [JsonPropertyName("number")]
    public int Number { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }
}

public class RepositoryInfo
{
    [JsonPropertyName("full_name")]
    public string? FullName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("owner")]
    public OwnerInfo? Owner { get; set; }
}

public class OwnerInfo
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}
