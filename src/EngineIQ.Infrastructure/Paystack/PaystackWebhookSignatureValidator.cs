using System.Security.Cryptography;
using System.Text;

namespace EngineIQ.Infrastructure.Paystack;

/// <summary>
/// Paystack signs webhook bodies with HMAC-SHA512 using the secret key (see Paystack webhook docs).
/// </summary>
public static class PaystackWebhookSignatureValidator
{
    public const string SignatureHeaderName = "x-paystack-signature";

    public static bool Validate(string rawPayloadBody, string? signatureHeader, string secretKey)
    {
        if (string.IsNullOrWhiteSpace(secretKey)
            || string.IsNullOrWhiteSpace(signatureHeader)
            || string.IsNullOrWhiteSpace(rawPayloadBody))
        {
            return false;
        }

        var payloadBytes = Encoding.UTF8.GetBytes(rawPayloadBody);
        var secretBytes = Encoding.UTF8.GetBytes(secretKey);
        var hash = HMACSHA512.HashData(secretBytes, payloadBytes);
        var expectedHex = Convert.ToHexString(hash).ToLowerInvariant();
        var actualHex = signatureHeader.Trim().ToLowerInvariant();

        if (expectedHex.Length != actualHex.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(expectedHex),
            Convert.FromHexString(actualHex));
    }
}
