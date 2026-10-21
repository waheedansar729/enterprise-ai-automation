using System.Text.RegularExpressions;

namespace Automation.Core.Services;

/// <summary>Removes likely credentials before text is sent to the LLM provider.</summary>
public static class SecretRedactor
{
    private static readonly (Regex Pattern, string Replacement)[] Rules =
    {
        (new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----[\s\S]*?-----END [A-Z ]*PRIVATE KEY-----", RegexOptions.Compiled), "[REDACTED PRIVATE KEY]"),
        (new Regex(@"\bAKIA[0-9A-Z]{16}\b", RegexOptions.Compiled), "[REDACTED AWS KEY]"),
        (new Regex(@"\bgh[pousr]_[A-Za-z0-9]{20,}\b", RegexOptions.Compiled), "[REDACTED GITHUB TOKEN]"),
        (new Regex(@"\bsk-[A-Za-z0-9_\-]{16,}", RegexOptions.Compiled), "[REDACTED API KEY]"),
        (new Regex(@"\bBearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Bearer [REDACTED]"),
        (new Regex(@"(?<k>api[_-]?key|secret|token|password|passwd|pwd)\s*[:=]\s*\S+", RegexOptions.Compiled | RegexOptions.IgnoreCase), "${k}=[REDACTED]"),
    };

    public static string Redact(string? input)
    {
        if (string.IsNullOrEmpty(input)) return "";
        var text = input;
        foreach (var (pattern, replacement) in Rules)
            text = pattern.Replace(text, replacement);
        return text;
    }
}
