// using Automation.Core.Contracts;
// using Automation.Core.Services;

// namespace Automation.Core.Validation;

// public static class ReviewResultValidator
// {
//     private const int MaxFindings = 20;
//     private const int MaxText = 2000;

//     /// <summary>Validates against the fixed schema and normalizes casing/length. Throws on bad output.</summary>
//     public static void Validate(ReviewResult r)
//     {
//         r.OverallResult = (r.OverallResult ?? "").Trim().ToLowerInvariant();
//         if (!Severity.AllowedOverall.Contains(r.OverallResult))
//             throw new InvalidModelOutputException($"Invalid overallResult '{r.OverallResult}'.");

//         if (string.IsNullOrWhiteSpace(r.Summary))
//             throw new InvalidModelOutputException("Summary is missing.");
//         r.Summary = Cut(r.Summary);

//         r.Findings ??= new();
//         r.Limitations ??= new();
//         if (r.Findings.Count > MaxFindings) r.Findings = r.Findings.Take(MaxFindings).ToList();

//         foreach (var f in r.Findings)
//         {
//             f.Severity = (f.Severity ?? "").Trim().ToLowerInvariant();
//             f.Confidence = (f.Confidence ?? "").Trim().ToLowerInvariant();
//             if (!Severity.Allowed.Contains(f.Severity))
//                 throw new InvalidModelOutputException($"Invalid severity '{f.Severity}'.");
//             if (!Severity.AllowedConfidence.Contains(f.Confidence)) f.Confidence = "low";
//             if (string.IsNullOrWhiteSpace(f.Title))
//                 throw new InvalidModelOutputException("A finding has no title.");
//             f.Category = Cut(f.Category ?? "");
//             f.Title = Cut(f.Title);
//             f.Evidence = Cut(f.Evidence ?? "");
//             f.Recommendation = Cut(f.Recommendation ?? "");
//         }
//         r.Limitations = r.Limitations.Select(Cut).ToList();
//         if (r.Limitations.Count == 0)
//             r.Limitations.Add("Only the supplied task text was reviewed; linked repositories and pull requests were not read.");
//     }

//     private static string Cut(string s) => s.Length > MaxText ? s[..MaxText] : s;
// }
using Automation.Core.Contracts;
using Automation.Core.Services;

namespace Automation.Core.Validation;

public static class ReviewResultValidator
{
    private const int MaxFindings = 20;
    private const int MaxText = 2000;

    /// <summary>Validates against the fixed schema and normalizes values. Unknown labels are mapped, not rejected.</summary>
    public static void Validate(ReviewResult r)
    {
        r.Findings ??= new();
        r.Limitations ??= new();
        var notes = new List<string>();

        if (r.Findings.Count > MaxFindings) r.Findings = r.Findings.Take(MaxFindings).ToList();

        var severityMapped = false;
        foreach (var f in r.Findings)
        {
            var mapped = MapSeverity(f.Severity, out var known);
            if (!known) severityMapped = true;
            f.Severity = mapped;
            f.Confidence = Normalize(f.Confidence);
            if (!Severity.AllowedConfidence.Contains(f.Confidence)) f.Confidence = "low";
            if (string.IsNullOrWhiteSpace(f.Title))
                throw new InvalidModelOutputException("A finding has no title.");
            f.Category = Cut(f.Category ?? "");
            f.Title = Cut(f.Title);
            f.Evidence = Cut(f.Evidence ?? "");
            f.Recommendation = Cut(f.Recommendation ?? "");
        }
        if (severityMapped)
            notes.Add("Some finding severities were not recognised and were mapped to the closest level.");

        // Overall result: "Needs Attention" -> needs_attention; unknown labels are mapped from the findings.
        var original = r.OverallResult ?? "";
        r.OverallResult = Normalize(original);
        if (!Severity.AllowedOverall.Contains(r.OverallResult))
        {
            if (r.Findings.Count > 0) r.OverallResult = "needs_attention";
            else if (!string.IsNullOrWhiteSpace(r.Summary)) r.OverallResult = "insufficient_evidence";
            else throw new InvalidModelOutputException(
                $"Invalid overallResult '{Cut(original)}' and the model gave no summary or findings.");

            notes.Add(Normalize(original) == ""
                ? "The model did not state an overall result; it was inferred from the findings."
                : $"The model returned an unrecognised overall result ('{Trim(original, 60)}'); it was mapped from the findings.");
        }

        if (string.IsNullOrWhiteSpace(r.Summary))
        {
            if (r.Findings.Count > 0)
                r.Summary = $"The model reported {r.Findings.Count} finding(s) but did not provide a summary.";
            else
                throw new InvalidModelOutputException("Summary is missing.");
        }
        r.Summary = Cut(r.Summary);

        r.Limitations = r.Limitations.Select(Cut).ToList();
        r.Limitations.AddRange(notes);
        if (r.Limitations.Count == 0)
            r.Limitations.Add("Only the supplied task text was reviewed; linked repositories and pull requests were not read.");
    }

    private static string MapSeverity(string? raw, out bool known)
    {
        var v = Normalize(raw);
        known = true;
        if (Severity.Allowed.Contains(v)) return v;
        known = false;
        return v switch
        {
            "info" or "note" or "nit" or "suggestion" => "informational",
            "minor" or "trivial" => "low",
            "moderate" or "warning" or "warn" => "medium",
            "major" or "error" or "severe" or "important" => "high",
            "blocker" or "fatal" => "critical",
            _ => "medium"
        };
    }

    private static string Normalize(string? s) =>
        (s ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');

    private static string Cut(string s) => s.Length > MaxText ? s[..MaxText] : s;
    private static string Trim(string s, int n) => s.Length > n ? s[..n] : s;
}