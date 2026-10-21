using Automation.Core.Domain;

namespace Automation.Core.Contracts;

public interface ILlmClient
{
    string ModelName { get; }
    Task<ReviewResult> ReviewAsync(string systemPrompt, string userMessage, CancellationToken ct);
}

public interface IClickUpClient
{
    /// <summary>Posts a comment and returns the HTTP status code. Throws on failure.</summary>
    Task<int> PostCommentAsync(string taskId, string text, CancellationToken ct);

    /// <summary>Reads task name/description/url. Returns null when unavailable (dry-run, no token, API error).</summary>
    Task<ClickUpTask?> GetTaskAsync(string taskId, CancellationToken ct);
}

public interface IReviewProcessor
{
    Task ProcessAsync(ReviewJob job, CancellationToken ct);
}

public interface IReviewRepository
{
    Task<ReviewJob?> ClaimNextAsync(CancellationToken ct);
    Task<ReviewJob?> GetAsync(Guid id, CancellationToken ct);
    Task SaveAnalysisAsync(Guid jobId, ReviewResult result, string model, string promptVersion, CancellationToken ct);
    Task SetStatusAsync(Guid jobId, JobStatus status, CancellationToken ct);
    Task RecordDeliveryAsync(Guid jobId, DeliveryAttempt attempt, WriteBackStatus? newStatus, CancellationToken ct);
    Task RetryOrFailAsync(Guid jobId, string error, bool transient, int maxAttempts, CancellationToken ct);
    Task<int> RequeueStaleAsync(TimeSpan olderThan, CancellationToken ct);
}

/// <summary>Temporary provider problem (timeout, 429, 5xx). The job is retried.</summary>
public class TransientException(string message) : Exception(message);

/// <summary>The model returned something that is not the agreed JSON schema.</summary>
public class InvalidModelOutputException(string message) : Exception(message);
public interface IGitHubClient
{
    Task<string> GetPullRequestDiffAsync(string repoFullName, int prNumber, CancellationToken ct);
    Task<int> PostCommentAsync(string repoFullName, int prNumber, string text, CancellationToken ct);
}