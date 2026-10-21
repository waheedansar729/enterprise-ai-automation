using System.Text.Json;
using Automation.Core.Contracts;
using Automation.Core.Domain;
using Automation.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace Automation.Infrastructure.Persistence;

public class EfReviewRepository(AppDbContext db) : IReviewRepository
{
    /// <summary>Atomically claims one queued job. SKIP LOCKED lets several workers run safely in parallel.</summary>
    public async Task<ReviewJob?> ClaimNextAsync(CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var job = await db.ReviewJobs.FromSqlRaw("""
            SELECT * FROM "ReviewJobs"
            WHERE "Status" = 'Queued' AND ("NextAttemptAtUtc" IS NULL OR "NextAttemptAtUtc" <= now())
            ORDER BY "CreatedAtUtc"
            LIMIT 1
            FOR UPDATE SKIP LOCKED
            """).FirstOrDefaultAsync(ct);

        if (job is null)
        {
            await tx.RollbackAsync(ct);
            return null;
        }

        job.Status = JobStatus.Running;
        job.StartedAtUtc = DateTime.UtcNow;
        job.NextAttemptAtUtc = null;
        job.Attempts += 1;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return job;
    }

    public Task<ReviewJob?> GetAsync(Guid id, CancellationToken ct) =>
        db.ReviewJobs.AsNoTracking().Include(j => j.Findings).FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task SaveAnalysisAsync(Guid jobId, ReviewResult result, string model, string promptVersion, CancellationToken ct)
    {
        var job = await db.ReviewJobs.FirstAsync(j => j.Id == jobId, ct);
        job.OverallResult = result.OverallResult;
        job.Summary = result.Summary;
        job.LimitationsJson = JsonSerializer.Serialize(result.Limitations);
        job.Model = model;
        job.PromptVersion = promptVersion;
        job.Error = null;
        job.Status = JobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;

        db.ReviewFindings.AddRange(result.Findings.Select(f => new ReviewFinding
        {
            ReviewJobId = jobId,
            Severity = f.Severity, Category = f.Category, Title = f.Title,
            Evidence = f.Evidence, Recommendation = f.Recommendation, Confidence = f.Confidence
        }));
        await db.SaveChangesAsync(ct);
    }

    public async Task SetStatusAsync(Guid jobId, JobStatus status, CancellationToken ct)
    {
        var job = await db.ReviewJobs.FirstAsync(j => j.Id == jobId, ct);
        job.Status = status;
        job.CompletedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task RecordDeliveryAsync(Guid jobId, DeliveryAttempt attempt, WriteBackStatus? newStatus, CancellationToken ct)
    {
        attempt.ReviewJobId = jobId;
        attempt.AttemptedAtUtc = DateTime.UtcNow;
        db.DeliveryAttempts.Add(attempt);
        if (newStatus is not null)
        {
            var job = await db.ReviewJobs.FirstAsync(j => j.Id == jobId, ct);
            job.WriteBackStatus = newStatus.Value;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task RetryOrFailAsync(Guid jobId, string error, bool transient, int maxAttempts, CancellationToken ct)
    {
        var job = await db.ReviewJobs.FirstAsync(j => j.Id == jobId, ct);
        job.Error = error.Length > 1000 ? error[..1000] : error;

        if (transient && job.Attempts < maxAttempts)
        {
            job.Status = JobStatus.Queued;
            job.NextAttemptAtUtc = DateTime.UtcNow + Backoff.Delay(job.Attempts, 10);
        }
        else
        {
            job.Status = JobStatus.Failed;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Jobs stuck in Running (worker crashed) are put back in the queue.</summary>
    public async Task<int> RequeueStaleAsync(TimeSpan olderThan, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - olderThan;
        return await db.ReviewJobs
            .Where(j => j.Status == JobStatus.Running && j.StartedAtUtc < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, JobStatus.Queued), ct);
    }
}
