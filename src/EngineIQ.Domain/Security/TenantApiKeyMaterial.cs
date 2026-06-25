using System.Security.Cryptography;
using System.Text;

namespace EngineIQ.Domain.Security;

/// <summary>Generation and hashing for tenant portal API keys ({tenantId:N}.{secret}).</summary>
public static class TenantApiKeyMaterial
{
    public static string Generate(Guid tenantId) =>
        $"{tenantId:N}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    public static byte[] Hash(string apiKey) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.Trim()));

    public static bool TryParseTenantId(string apiKey, out Guid tenantId)
    {
        tenantId = default;
        if (string.IsNullOrWhiteSpace(apiKey))
            return false;

        var trimmed = apiKey.Trim();
        var dot = trimmed.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot >= trimmed.Length - 1)
            return false;

        return Guid.TryParse(trimmed.AsSpan(0, dot), out tenantId);
    }

    public static bool FixedTimeEqualsHash(byte[] storedHash, string apiKey)
    {
        var hash = Hash(apiKey);
        if (storedHash.Length != hash.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(storedHash, hash);
    }
}
