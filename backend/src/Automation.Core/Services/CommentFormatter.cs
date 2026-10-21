using System.Text;
using System.Text.Json;
using Automation.Core.Domain;

namespace Automation.Core.Services;

public static class CommentFormatter
{
    public static List<string> ParseLimitations(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    public static string Format(ReviewJob job)
    {
        var label = job.OverallResult switch
        {
            "needs_attention" => "Needs attention",
            "no_issues_found" => "No issues found in the supplied evidence",
            _ => "Insufficient evidence"
        };

        var counts = job.Findings
            .GroupBy(f => f.Severity)
            .OrderBy(g => Severity.Rank(g.Key))
            .Select(g => $"{g.Count()} {g.Key}")
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"AI review completed — {label}");
        sb.AppendLine();
        sb.AppendLine($"Summary: {job.Summary}");
        sb.AppendLine($"Findings: {(counts.Count == 0 ? "none" : string.Join(", ", counts))}.");

        var top = job.Findings.OrderBy(f => Severity.Rank(f.Severity)).Take(3).ToList();
        foreach (var f in top) sb.AppendLine($"- [{f.Severity}] {f.Title}");

        var limits = ParseLimitations(job.LimitationsJson);
        if (limits.Count > 0) sb.AppendLine($"Limitations: {string.Join(" ", limits)}");
        sb.AppendLine($"Review job: {job.Id}");
        sb.AppendLine("This is an AI-assisted review; an engineer must verify the findings.");
        return sb.ToString().TrimEnd();
    }
}
