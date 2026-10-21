namespace Automation.Core.Services;

public static class Severity
{
    public static readonly string[] Allowed = { "critical", "high", "medium", "low", "informational" };
    public static readonly string[] AllowedConfidence = { "low", "medium", "high" };
    public static readonly string[] AllowedOverall = { "no_issues_found", "needs_attention", "insufficient_evidence" };

    public static int Rank(string? severity)
    {
        var i = Array.IndexOf(Allowed, severity?.ToLowerInvariant());
        return i < 0 ? Allowed.Length : i;
    }
}
