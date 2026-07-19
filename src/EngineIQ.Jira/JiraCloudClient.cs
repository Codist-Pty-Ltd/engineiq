using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EngineIQ.Domain.Interfaces;
using EngineIQ.Domain.Jira;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EngineIQ.Jira;

/// <summary>Jira Cloud REST API v2 client (wiki-markup comments; no ADF).</summary>
public sealed class JiraCloudClient : IJiraClient
{
    public const string HttpClientName = "JiraCloud";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JiraClientOptions _options;
    private readonly ILogger<JiraCloudClient> _logger;

    public JiraCloudClient(
        IHttpClientFactory httpClientFactory,
        IOptions<JiraClientOptions> options,
        ILogger<JiraCloudClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JiraIssueDetails?> GetIssueAsync(
        JiraConnectionInfo connection,
        string issueKey,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(connection);
        var url =
            $"{TrimSite(connection.SiteBaseUrl)}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}" +
            "?fields=summary,description,issuetype,priority,reporter,project,updated";

        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Jira GetIssue failed Status={Status} IssueKey={IssueKey}",
                (int)response.StatusCode,
                issueKey);
            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return MapIssue(doc.RootElement);
    }

    public async Task PostCommentAsync(
        JiraConnectionInfo connection,
        string issueKey,
        string body,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(connection);
        var url = $"{TrimSite(connection.SiteBaseUrl)}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}/comment";
        var payload = JsonSerializer.Serialize(new { body });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var response = await client.PostAsync(url, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Jira PostComment failed Status={Status} IssueKey={IssueKey}",
                (int)response.StatusCode,
                issueKey);
            response.EnsureSuccessStatusCode();
        }
    }

    private HttpClient CreateClient(JiraConnectionInfo connection)
    {
        var client = _httpClientFactory.CreateClient(HttpClientName);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.TimeoutSeconds, 5, 60));
        client.Timeout = timeout;
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", _options.UserAgent);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"{connection.Email}:{connection.ApiToken}")));
        return client;
    }

    private static string TrimSite(string siteBaseUrl) => siteBaseUrl.Trim().TrimEnd('/');

    private static JiraIssueDetails MapIssue(JsonElement root)
    {
        var key = root.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? "" : "";
        long id = 0;
        if (root.TryGetProperty("id", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number)
                id = idEl.GetInt64();
            else if (idEl.ValueKind == JsonValueKind.String && long.TryParse(idEl.GetString(), out var parsed))
                id = parsed;
        }

        var fields = root.TryGetProperty("fields", out var f) ? f : default;
        var summary = ReadString(fields, "summary") ?? "";
        var description = ReadString(fields, "description");
        var issueType = ReadNestedName(fields, "issuetype") ?? "Unknown";
        var priority = ReadNestedName(fields, "priority");
        var reporter = ReadNestedDisplayName(fields, "reporter");
        var projectKey = ReadNestedKey(fields, "project") ?? "";
        DateTimeOffset? updated = null;
        if (fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("updated", out var updatedEl)
            && updatedEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedEl.GetString(), out var u))
        {
            updated = u;
        }

        return new JiraIssueDetails(key, id, issueType, summary, description, priority, reporter, projectKey, updated);
    }

    private static string? ReadString(JsonElement fields, string name)
    {
        if (fields.ValueKind != JsonValueKind.Object || !fields.TryGetProperty(name, out var el))
            return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            _ => el.GetRawText(),
        };
    }

    private static string? ReadNestedName(JsonElement fields, string objectName)
    {
        if (fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty(objectName, out var obj)
            || obj.ValueKind != JsonValueKind.Object)
            return null;
        return obj.TryGetProperty("name", out var name) ? name.GetString() : null;
    }

    private static string? ReadNestedKey(JsonElement fields, string objectName)
    {
        if (fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty(objectName, out var obj)
            || obj.ValueKind != JsonValueKind.Object)
            return null;
        return obj.TryGetProperty("key", out var key) ? key.GetString() : null;
    }

    private static string? ReadNestedDisplayName(JsonElement fields, string objectName)
    {
        if (fields.ValueKind != JsonValueKind.Object
            || !fields.TryGetProperty(objectName, out var obj)
            || obj.ValueKind != JsonValueKind.Object)
            return null;
        if (obj.TryGetProperty("displayName", out var dn))
            return dn.GetString();
        return obj.TryGetProperty("name", out var name) ? name.GetString() : null;
    }
}
