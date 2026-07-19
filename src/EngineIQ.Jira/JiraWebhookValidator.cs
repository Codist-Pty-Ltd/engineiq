using System.Security.Cryptography;
using System.Text;
using EngineIQ.Domain.Interfaces;

namespace EngineIQ.Jira;

/// <summary>
/// Jira Cloud admin webhooks are unsigned. EngineIQ authenticates deliveries via a per-connection
/// secret embedded in the webhook URL path, compared in constant time.
/// </summary>
public sealed class JiraWebhookValidator
{
    private readonly IJiraConnectionRepository _connections;

    public JiraWebhookValidator(IJiraConnectionRepository connections)
    {
        _connections = connections;
    }

    public async Task<JiraConnectionRow?> TryResolveConnectionAsync(
        string webhookSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(webhookSecret) || webhookSecret.Length < 32)
            return null;

        var row = await _connections.FindByWebhookSecretAsync(webhookSecret.Trim(), cancellationToken);
        if (row is null)
            return null;

        // Constant-time compare against the stored secret (defense in depth if lookup ever soft-matches).
        var provided = Encoding.UTF8.GetBytes(webhookSecret.Trim());
        var stored = Encoding.UTF8.GetBytes(row.WebhookSecret);
        if (provided.Length != stored.Length || !CryptographicOperations.FixedTimeEquals(provided, stored))
            return null;

        return row;
    }

    /// <summary>Constant-time equality for unit tests and callers that already have both secrets.</summary>
    public static bool SecretsEqual(string a, string b)
    {
        var left = Encoding.UTF8.GetBytes(a ?? string.Empty);
        var right = Encoding.UTF8.GetBytes(b ?? string.Empty);
        if (left.Length != right.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(left, right);
    }
}
