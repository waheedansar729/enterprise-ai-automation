using System.Security.Cryptography;
using System.Text;

namespace Automation.Core.Services;

/// <summary>Verifies GitHub's X-Hub-Signature-256 header (HMAC-SHA256 of the raw body with the webhook secret).</summary>
public static class GitHubSignature
{
    private const string Prefix = "sha256=";

    public static string Sign(string secret, byte[] body) =>
        Prefix + Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();

    public static bool Verify(string? secret, byte[] body, string? header)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(header)) return false;
        if (!header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var expected = Sign(secret, body);
        var given = Prefix + header[Prefix.Length..].Trim().ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected));
    }
}