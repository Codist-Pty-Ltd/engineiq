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

    /// <summary>Push event only: full ref pushed, e.g. <c>refs/heads/main</c>.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>Push event only: head commit sha after the push.</summary>
    [JsonPropertyName("after")]
    public string? After { get; set; }

    /// <summary>Push event only: head commit sha before the push (all-zero sha on branch creation).</summary>
    [JsonPropertyName("before")]
    public string? Before { get; set; }

    /// <summary>Push event only: true when the ref was deleted rather than pushed to.</summary>
    [JsonPropertyName("deleted")]
    public bool Deleted { get; set; }
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

    /// <summary>Push event only: used to ignore pushes to non-default branches for code indexing.</summary>
    [JsonPropertyName("default_branch")]
    public string? DefaultBranch { get; set; }
}

public class OwnerInfo
{
    [JsonPropertyName("login")]
    public string? Login { get; set; }
}
