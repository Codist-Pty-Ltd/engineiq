namespace EngineIQ.Domain.Jira;

/// <summary>Decrypted Jira Cloud credentials — in-memory only; never persist or log.</summary>
public sealed record JiraConnectionInfo(
    string SiteBaseUrl,
    string Email,
    string ApiToken);
