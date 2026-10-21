namespace Automation.Core.Contracts;

/// <summary>Normalized payload sent by Power Automate.</summary>
public record CreateReviewRequest(
    string? EventId, string? TaskId, string? TaskUrl, string? Status,
    string? Title, string? Description, string? RepositoryUrl,
    string? PullRequestUrl, DateTime? TriggeredAtUtc);

/// <summary>Structured review returned by the LLM adapter.</summary>
public class ReviewResult
{
    public string OverallResult { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<FindingDto> Findings { get; set; } = new();
    public List<string> Limitations { get; set; } = new();
}

public class FindingDto
{
    public string Severity { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string Confidence { get; set; } = "";
}

public record ReviewContext(string TaskId, string Title, string Description, string? RepositoryUrl, string? PullRequestUrl);

/// <summary>Task details fetched from ClickUp when only a task ID arrives (native webhook).</summary>
public record ClickUpTask(string Name, string? Description, string? Url);
