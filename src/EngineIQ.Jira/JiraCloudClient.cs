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
            "?fields=summary,description,issuetype,priority,reporter,project,updated,parent";

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

    public async Task<string> PostCommentAsync(
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
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Jira PostComment failed Status={Status} IssueKey={IssueKey}",
                (int)response.StatusCode,
                issueKey);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(responseText);
        if (doc.RootElement.TryGetProperty("id", out var idEl))
        {
            return idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString() ?? throw new InvalidOperationException("jira_comment_id_missing")
                : idEl.GetRawText();
        }

        throw new InvalidOperationException("jira_comment_id_missing");
    }

    public async Task<string?> UpdateCommentAsync(
        JiraConnectionInfo connection,
        string issueKey,
        string commentId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(connection);
        var url =
            $"{TrimSite(connection.SiteBaseUrl)}/rest/api/2/issue/{Uri.EscapeDataString(issueKey)}" +
            $"/comment/{Uri.EscapeDataString(commentId)}";
        var payload = JsonSerializer.Serialize(new { body });
        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = content };
        using var response = await client.SendAsync(request, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Jira UpdateComment failed Status={Status} IssueKey={IssueKey}",
                (int)response.StatusCode,
                issueKey);
            response.EnsureSuccessStatusCode();
        }

        return commentId;
    }

    public async Task<JiraSearchPage> SearchIssuesAsync(
        JiraConnectionInfo connection,
        string jql,
        int startAt,
        int maxResults,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(connection);
        var qs =
            $"jql={Uri.EscapeDataString(jql)}" +
            $"&startAt={Math.Max(0, startAt)}" +
            $"&maxResults={Math.Clamp(maxResults, 1, 100)}" +
            "&fields=issuetype,updated";
        var url = $"{TrimSite(connection.SiteBaseUrl)}/rest/api/2/search?{qs}";

        using var response = await client.GetAsync(url, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
            throw new InvalidJqlException(ExtractJiraError(responseText) ?? "invalid_jql");

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Jira Search failed Status={Status}", (int)response.StatusCode);
            response.EnsureSuccessStatusCode();
        }

        using var doc = JsonDocument.Parse(responseText);
        var root = doc.RootElement;
        var total = root.TryGetProperty("total", out var totalEl) && totalEl.TryGetInt32(out var t) ? t : 0;
        var start = root.TryGetProperty("startAt", out var startEl) && startEl.TryGetInt32(out var s) ? s : startAt;
        var issues = new List<JiraSearchIssue>();
        if (root.TryGetProperty("issues", out var issuesEl) && issuesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var issue in issuesEl.EnumerateArray())
                issues.Add(MapSearchIssue(issue));
        }

        return new JiraSearchPage(total, start, issues);
    }

    public async Task<JiraParentSummary?> GetParentAsync(
        JiraConnectionInfo connection,
        string parentKey,
        CancellationToken cancellationToken = default)
    {
        var client = CreateClient(connection);
        var url =
            $"{TrimSite(connection.SiteBaseUrl)}/rest/api/2/issue/{Uri.EscapeDataString(parentKey)}" +
            "?fields=summary,description";

        using var response = await client.GetAsync(url, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Jira GetParent failed Status={Status} ParentKey={ParentKey}",
                (int)response.StatusCode,
                parentKey);
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = doc.RootElement;
        var key = root.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? parentKey : parentKey;
        var fields = root.TryGetProperty("fields", out var f) ? f : default;
        var summary = ReadString(fields, "summary") ?? string.Empty;
        var description = ReadString(fields, "description");
        return new JiraParentSummary(key, summary, description);
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

    private static string? ExtractJiraError(string responseText)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseText);
            if (doc.RootElement.TryGetProperty("errorMessages", out var msgs)
                && msgs.ValueKind == JsonValueKind.Array)
            {
                var parts = msgs.EnumerateArray()
                    .Select(e => e.GetString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .ToList();
                if (parts.Count > 0)
                    return string.Join("; ", parts);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        return string.IsNullOrWhiteSpace(responseText) ? null : responseText.Trim()[..Math.Min(500, responseText.Trim().Length)];
    }

    private static JiraSearchIssue MapSearchIssue(JsonElement issue)
    {
        long id = 0;
        if (issue.TryGetProperty("id", out var idEl))
        {
            if (idEl.ValueKind == JsonValueKind.Number)
                id = idEl.GetInt64();
            else if (idEl.ValueKind == JsonValueKind.String && long.TryParse(idEl.GetString(), out var parsed))
                id = parsed;
        }

        var key = issue.TryGetProperty("key", out var keyEl) ? keyEl.GetString() ?? "" : "";
        var fields = issue.TryGetProperty("fields", out var f) ? f : default;
        var issueType = ReadNestedName(fields, "issuetype") ?? "Unknown";
        var updated = DateTimeOffset.UtcNow;
        if (fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("updated", out var updatedEl)
            && updatedEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedEl.GetString(), out var u))
        {
            updated = u;
        }

        return new JiraSearchIssue(id, key, issueType, updated);
    }

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
        var parentKey = ReadNestedKey(fields, "parent");
        DateTimeOffset? updated = null;
        if (fields.ValueKind == JsonValueKind.Object
            && fields.TryGetProperty("updated", out var updatedEl)
            && updatedEl.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(updatedEl.GetString(), out var u))
        {
            updated = u;
        }

        return new JiraIssueDetails(
            key, id, issueType, summary, description, priority, reporter, projectKey, updated, parentKey);
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
