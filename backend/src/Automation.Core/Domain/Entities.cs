// namespace Automation.Core.Domain;

// public enum JobStatus { Queued, Running, Completed, Failed }
// public enum WriteBackStatus { Pending, Delivered, Failed }

// public class ReviewJob
// {
//     public Guid Id { get; set; } = Guid.NewGuid();
//     public string EventId { get; set; } = "";
//     public string TaskId { get; set; } = "";
//     public string TaskUrl { get; set; } = "";
//     public string Title { get; set; } = "";
//     public string? Description { get; set; }
//     public string? RepositoryUrl { get; set; }
//     public string? PullRequestUrl { get; set; }

//     public JobStatus Status { get; set; } = JobStatus.Queued;
//     public WriteBackStatus WriteBackStatus { get; set; } = WriteBackStatus.Pending;

//     public string? OverallResult { get; set; }
//     public string? Summary { get; set; }
//     public string? LimitationsJson { get; set; }
//     public string? Model { get; set; }
//     public string? PromptVersion { get; set; }
//     public string? Error { get; set; }
//     public string CorrelationId { get; set; } = "";

//     public int Attempts { get; set; }
//     public DateTime? NextAttemptAtUtc { get; set; }
//     public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
//     public DateTime? StartedAtUtc { get; set; }
//     public DateTime? CompletedAtUtc { get; set; }

//     public List<ReviewFinding> Findings { get; set; } = new();
//     public List<DeliveryAttempt> DeliveryAttempts { get; set; } = new();
// }

// public class ReviewFinding
// {
//     public Guid Id { get; set; } = Guid.NewGuid();
//     public Guid ReviewJobId { get; set; }
//     public string Severity { get; set; } = "";
//     public string Category { get; set; } = "";
//     public string Title { get; set; } = "";
//     public string Evidence { get; set; } = "";
//     public string Recommendation { get; set; } = "";
//     public string Confidence { get; set; } = "";
// }

// public class DeliveryAttempt
// {
//     public Guid Id { get; set; } = Guid.NewGuid();
//     public Guid ReviewJobId { get; set; }
//     public string Destination { get; set; } = "";
//     public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
//     public int StatusCode { get; set; }
//     public string? ErrorCode { get; set; }
//     public int AttemptNumber { get; set; }
// }
namespace Automation.Core.Domain;

public enum JobStatus { Queued, Running, Completed, Failed }
public enum WriteBackStatus { Pending, Delivered, Failed }

public class ReviewJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventId { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string TaskUrl { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? RepositoryUrl { get; set; }
    public string? PullRequestUrl { get; set; }

    // 👈 GitHub support ke liye yeh field add ki hai
    public string Source { get; set; } = "ClickUp"; 

    public JobStatus Status { get; set; } = JobStatus.Queued;
    public WriteBackStatus WriteBackStatus { get; set; } = WriteBackStatus.Pending;

    public string? OverallResult { get; set; }
    public string? Summary { get; set; }
    public string? LimitationsJson { get; set; }
    public string? Model { get; set; }
    public string? PromptVersion { get; set; }
    public string? Error { get; set; }
    public string CorrelationId { get; set; } = "";

    public int Attempts { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    // 👈 Yeh lists wapas add karein
    public List<ReviewFinding> Findings { get; set; } = new();
    public List<DeliveryAttempt> DeliveryAttempts { get; set; } = new();
}

// 👈 Yeh class wapas add karein (Inhe hata diya gaya tha)
public class ReviewFinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReviewJobId { get; set; }
    public string Severity { get; set; } = "";
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Evidence { get; set; } = "";
    public string Recommendation { get; set; } = "";
    public string Confidence { get; set; } = "";
}

// 👈 Yeh class wapas add karein
public class DeliveryAttempt
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ReviewJobId { get; set; }
    public string Destination { get; set; } = "";
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;
    public int StatusCode { get; set; }
    public string? ErrorCode { get; set; }
    public int AttemptNumber { get; set; }
}