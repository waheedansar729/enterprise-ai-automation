using Automation.Core.Contracts;

namespace Automation.Infrastructure.Llm;

/// <summary>
/// Deterministic fake reviewer for local testing (no API key, no cost).
/// Put FORCE_TIMEOUT or FORCE_BAD_JSON in the task description to test error handling.
/// </summary>
public class MockLlmClient : ILlmClient
{
    public string ModelName => "mock-reviewer-v1";

    public Task<ReviewResult> ReviewAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var t = userMessage.ToLowerInvariant();
        if (t.Contains("force_timeout")) throw new TransientException("Mock provider timeout.");
        if (t.Contains("force_bad_json")) throw new InvalidModelOutputException("Mock provider returned malformed JSON.");

        var r = new ReviewResult
        {
            Limitations = new()
            {
                "Mock reviewer: no real analysis was performed.",
                "Linked repositories and pull requests were not read."
            }
        };

        if (t.Contains("retry") || t.Contains("idempot"))
            r.Findings.Add(new FindingDto
            {
                Severity = "high", Category = "correctness", Title = "Retry path may repeat side effects",
                Evidence = "Task text mentions retry/idempotency handling.",
                Recommendation = "Check the idempotency key before starting the side effect.", Confidence = "medium"
            });
        if (t.Contains("sql") || t.Contains("password"))
            r.Findings.Add(new FindingDto
            {
                Severity = "medium", Category = "security", Title = "Possible sensitive data or SQL handling",
                Evidence = "Task text mentions SQL or passwords.",
                Recommendation = "Use parameterized queries and keep secrets out of code.", Confidence = "low"
            });

        r.OverallResult = r.Findings.Count > 0 ? "needs_attention" : "no_issues_found";
        r.Summary = r.Findings.Count > 0
            ? $"Mock review found {r.Findings.Count} possible issue(s) in the supplied task text."
            : "Mock review found nothing notable in the supplied task text.";
        return Task.FromResult(r);
    }
}
