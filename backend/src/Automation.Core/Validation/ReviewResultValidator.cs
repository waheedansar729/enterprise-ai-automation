using Automation.Core.Contracts;
using Automation.Core.Services;

namespace Automation.Core.Validation;

public static class ReviewResultValidator
{
    private const int MaxFindings = 20;
    private const int MaxText = 2000;

    /// <summary>Validates against the fixed schema and normalizes casing/length. Throws on bad output.</summary>
    public static void Validate(ReviewResult r)
    {
        r.OverallResult = (r.OverallResult ?? "").Trim().ToLowerInvariant();
        if (!Severity.AllowedOverall.Contains(r.OverallResult))
            throw new InvalidModelOutputException($"Invalid overallResult '{r.OverallResult}'.");

        if (string.IsNullOrWhiteSpace(r.Summary))
            throw new InvalidModelOutputException("Summary is missing.");
        r.Summary = Cut(r.Summary);

        r.Findings ??= new();
        r.Limitations ??= new();
        if (r.Findings.Count > MaxFindings) r.Findings = r.Findings.Take(MaxFindings).ToList();

        foreach (var f in r.Findings)
        {
            f.Severity = (f.Severity ?? "").Trim().ToLowerInvariant();
            f.Confidence = (f.Confidence ?? "").Trim().ToLowerInvariant();
            if (!Severity.Allowed.Contains(f.Severity))
                throw new InvalidModelOutputException($"Invalid severity '{f.Severity}'.");
            if (!Severity.AllowedConfidence.Contains(f.Confidence)) f.Confidence = "low";
            if (string.IsNullOrWhiteSpace(f.Title))
                throw new InvalidModelOutputException("A finding has no title.");
            f.Category = Cut(f.Category ?? "");
            f.Title = Cut(f.Title);
            f.Evidence = Cut(f.Evidence ?? "");
            f.Recommendation = Cut(f.Recommendation ?? "");
        }
        r.Limitations = r.Limitations.Select(Cut).ToList();
        if (r.Limitations.Count == 0)
            r.Limitations.Add("Only the supplied task text was reviewed; linked repositories and pull requests were not read.");
    }

    private static string Cut(string s) => s.Length > MaxText ? s[..MaxText] : s;
}
