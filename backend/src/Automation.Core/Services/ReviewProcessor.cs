
// using Automation.Core.Contracts;
// using Automation.Core.Domain;
// using Automation.Core.Validation;
// using Microsoft.Extensions.Logging;
// using Microsoft.Extensions.Options;

// namespace Automation.Core.Services;

// public class ReviewProcessor(
//     IReviewRepository repo,
//     ILlmClient llm,
//     IClickUpClient clickUp,
//     IGitHubClient gitHub, // 👈 GitHub Client yahan inject kiya gaya hai
//     IOptions<ReviewOptions> options,
//     ILogger<ReviewProcessor> log) : IReviewProcessor
// {
//     private const int WriteBackAttempts = 3;
//     private readonly ReviewOptions _opt = options.Value;

//     public async Task ProcessAsync(ReviewJob job, CancellationToken ct)
//     {
//         using var _ = log.BeginScope(new Dictionary<string, object>
//         {
//             ["CorrelationId"] = job.CorrelationId, ["JobId"] = job.Id
//         });
//         log.LogInformation("Processing job {JobId} (attempt {Attempt})", job.Id, job.Attempts);

//         try
//         {
//             var analysisAlreadySaved = job.Summary is not null;
//             if (!analysisAlreadySaved)
//             {
//                 string userMessage;

//                 // 👈 1. Check karein ke job GitHub ki hai ya ClickUp ki
//                 if (job.Source == "GitHub" && !string.IsNullOrEmpty(job.PullRequestUrl))
//                 {
//                     // PR URL se repo aur PR number nikaalein (e.g., https://github.com/owner/repo/pull/12)
//                     var uri = new Uri(job.PullRequestUrl);
//                     var segments = uri.AbsolutePath.Trim('/').Split('/');
//                     var repoFullName = $"{segments[0]}/{segments[1]}";
//                     var prNumber = int.Parse(segments[3]);

//                     log.LogInformation("Fetching GitHub diff for {Repo} PR #{PrNumber}", repoFullName, prNumber);
                    
//                     var diff = await gitHub.GetPullRequestDiffAsync(repoFullName, prNumber, ct);

//                     // Size limit (Groq ki limit ke liye)
//                     if (diff.Length > _opt.MaxContextCharacters)
//                         diff = diff.Substring(0, _opt.MaxContextCharacters) + "\n...[Diff truncated]";

//                     userMessage = $"Review this Pull Request:\n\nTitle: {job.Title}\n\nCode Changes:\n{diff}";
//                 }
//                 else
//                 {
//                     // ClickUp wala purana flow
//                     var ctx = ContextBuilder.Build(job, _opt.MaxContextCharacters);
//                     userMessage = PromptBuilder.BuildUserMessage(ctx);
//                 }

//                 // 👈 2. LLM (Groq) ko bhejein
//                 var result = await llm.ReviewAsync(PromptBuilder.SystemPrompt, userMessage, ct);
//                 ReviewResultValidator.Validate(result);
//                 await repo.SaveAnalysisAsync(job.Id, result, llm.ModelName, PromptBuilder.Version, ct);
//                 log.LogInformation("Analysis saved: {Result}, {Count} findings", result.OverallResult, result.Findings.Count);
//             }

//             var full = await repo.GetAsync(job.Id, ct) ?? throw new InvalidOperationException("Job disappeared.");
//             await WriteBackAsync(full, ct);

//             if (analysisAlreadySaved) await repo.SetStatusAsync(job.Id, JobStatus.Completed, ct);
//         }
//         catch (OperationCanceledException) when (ct.IsCancellationRequested)
//         {
//             throw;
//         }
//         catch (Exception ex) when (ex is TransientException or InvalidModelOutputException)
//         {
//             log.LogWarning("Transient failure: {Message}", ex.Message);
//             await repo.RetryOrFailAsync(job.Id, ex.Message, transient: true, _opt.MaxAttempts, CancellationToken.None);
//         }
//         catch (Exception ex)
//         {
//             log.LogError(ex, "Job failed permanently");
//             await repo.RetryOrFailAsync(job.Id, ex.Message, transient: false, _opt.MaxAttempts, CancellationToken.None);
//         }
//     }

//     /// <summary>ClickUp / GitHub write-back is tracked separately; it never turns a saved analysis into a failure.</summary>
//     private async Task WriteBackAsync(ReviewJob job, CancellationToken ct)
//     {
//         if (job.WriteBackStatus == WriteBackStatus.Delivered) return;
//         var text = CommentFormatter.Format(job);

//         for (var attempt = 1; attempt <= WriteBackAttempts; attempt++)
//         {
//             try
//             {
//                 int code;
//                 string destination;

//                 // 👈 3. Comment post karein (GitHub ya ClickUp par)
//                 if (job.Source == "GitHub" && !string.IsNullOrEmpty(job.PullRequestUrl))
//                 {
//                     var uri = new Uri(job.PullRequestUrl);
//                     var segments = uri.AbsolutePath.Trim('/').Split('/');
//                     var repoFullName = $"{segments[0]}/{segments[1]}";
//                     var prNumber = int.Parse(segments[3]);

//                     log.LogInformation("Posting comment to GitHub {Repo} PR #{PrNumber}", repoFullName, prNumber);
//                     code = await gitHub.PostCommentAsync(repoFullName, prNumber, text, ct);
//                     destination = "github-comment";
//                 }
//                 else
//                 {
//                     code = await clickUp.PostCommentAsync(job.TaskId, text, ct);
//                     destination = "clickup-comment";
//                 }

//                 await repo.RecordDeliveryAsync(job.Id, new DeliveryAttempt
//                 {
//                     Destination = destination, StatusCode = code, AttemptNumber = attempt
//                 }, WriteBackStatus.Delivered, ct);
//                 return;
//             }
//             catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
//             catch (Exception ex)
//             {
//                 var last = attempt == WriteBackAttempts;
//                 log.LogWarning("Write-back attempt {Attempt} failed: {Message}", attempt, ex.Message);
                
//                 var dest = job.Source == "GitHub" ? "github-comment" : "clickup-comment";
//                 await repo.RecordDeliveryAsync(job.Id, new DeliveryAttempt
//                 {
//                     Destination = dest, StatusCode = 0, ErrorCode = ex.GetType().Name, AttemptNumber = attempt
//                 }, last ? WriteBackStatus.Failed : null, ct);
                
//                 if (!last) await Task.Delay(Backoff.Delay(attempt, _opt.WriteBackRetryBaseSeconds), ct);
//             }
//         }
//     }
// }
using Automation.Core.Contracts;
using Automation.Core.Domain;
using Automation.Core.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Automation.Core.Services;

public class ReviewProcessor(
    IReviewRepository repo,
    ILlmClient llm,
    IClickUpClient clickUp,
    IGitHubClient gitHub,
    IOptions<ReviewOptions> options,
    ILogger<ReviewProcessor> log) : IReviewProcessor
{
    private const int WriteBackAttempts = 3;
    private readonly ReviewOptions _opt = options.Value;

    public async Task ProcessAsync(ReviewJob job, CancellationToken ct)
    {
        using var _ = log.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = job.CorrelationId, ["JobId"] = job.Id
        });
        log.LogInformation("Processing job {JobId} (attempt {Attempt})", job.Id, job.Attempts);

        try
        {
            var analysisAlreadySaved = job.Summary is not null;
            if (!analysisAlreadySaved)
            {
                // 1. Job process karein
                var (userMessage, extraLimitations) = await BuildUserMessageAsync(job, ct);
                
                // 2. LLM (Groq) ko call karein
                var result = await llm.ReviewAsync(PromptBuilder.SystemPrompt, userMessage, ct);
                ReviewResultValidator.Validate(result);
                result.Limitations.AddRange(extraLimitations);
                
                // 3. Result Database mein save karein
                await repo.SaveAnalysisAsync(job.Id, result, llm.ModelName, PromptBuilder.Version, ct);
                log.LogInformation("Analysis saved: {Result}, {Count} findings", result.OverallResult, result.Findings.Count);
            }

            var full = await repo.GetAsync(job.Id, ct) ?? throw new InvalidOperationException("Job disappeared.");
            await WriteBackAsync(full, ct);

            if (analysisAlreadySaved) await repo.SetStatusAsync(job.Id, JobStatus.Completed, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // shutting down; stale-job recovery will requeue
        }
        catch (Exception ex) when (ex is TransientException or InvalidModelOutputException)
        {
            log.LogWarning("Transient failure: {Message}", ex.Message);
            await repo.RetryOrFailAsync(job.Id, ex.Message, transient: true, _opt.MaxAttempts, CancellationToken.None);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Job failed permanently");
            await repo.RetryOrFailAsync(job.Id, ex.Message, transient: false, _opt.MaxAttempts, CancellationToken.None);
        }
    }

    /// <summary>Builds the text sent to the LLM. Secrets are redacted and large diffs are cut (and reported as a limitation).</summary>
    private async Task<(string Message, List<string> Limitations)> BuildUserMessageAsync(ReviewJob job, CancellationToken ct)
    {
        var limitations = new List<string>();
        
         // 1. Check karein ke source GitHub hai
        if (job.Source != "GitHub")
        {
            var ctx = ContextBuilder.Build(job, _opt.MaxContextCharacters);
            return (PromptBuilder.BuildUserMessage(ctx), limitations);
        }
        
        // 2. PR URL parse karein
        if (!PullRequestRef.TryParse(job.PullRequestUrl, out var pr) || pr is null)
            throw new InvalidOperationException(
                "This is a GitHub job but its pull request URL is not a valid https://github.com/<owner>/<repo>/pull/<number> link.");

        log.LogInformation("Fetching GitHub diff for {Repo} PR #{PrNumber}", pr.FullName, pr.Number);

        // 3. GitHub se diff fetch karein
        var diff = await gitHub.GetPullRequestDiffAsync(pr.FullName, pr.Number, ct);
        
        // 4. Secret redact karein
        diff = SecretRedactor.Redact(diff);            // redact first, so a secret cut in half cannot slip through

        // 5. Limit lagayein
        var truncated = diff.Length > _opt.MaxContextCharacters;
        if (truncated) diff = diff[.._opt.MaxContextCharacters];
        
        // 6. Limitations add karein
        limitations.Add("Only the pull request diff was reviewed; surrounding code, tests and runtime behaviour were not inspected.");
        if (truncated)
            limitations.Add($"The diff was cut to the first {_opt.MaxContextCharacters} characters; later changes were not reviewed.");
        
        // 7. Message banayein
        var title = SecretRedactor.Redact(job.Title);
        if (title.Length > 500) title = title[..500];
        return (PromptBuilder.BuildPullRequestMessage(title, pr.FullName, pr.Number, diff, truncated), limitations);
    }

    /// <summary>Write-back is tracked separately; it never turns a saved analysis into a failure.</summary>
    private async Task WriteBackAsync(ReviewJob job, CancellationToken ct)
    {
        if (job.WriteBackStatus == WriteBackStatus.Delivered) return;
        var text = CommentFormatter.Format(job);
        var toGitHub = job.Source == "GitHub";
        var destination = toGitHub ? "github-comment" : "clickup-comment";

        PullRequestRef? pr = null;
        if (toGitHub && !PullRequestRef.TryParse(job.PullRequestUrl, out pr))
        {
            log.LogError("Cannot post to GitHub: invalid pull request URL for job {JobId}", job.Id);
            await repo.RecordDeliveryAsync(job.Id, new DeliveryAttempt
            {
                Destination = destination, StatusCode = 0, ErrorCode = "InvalidPullRequestUrl", AttemptNumber = 1
            }, WriteBackStatus.Failed, ct);
            return;
        }

        for (var attempt = 1; attempt <= WriteBackAttempts; attempt++)
        {
            try
            {
                var code = toGitHub
                    ? await gitHub.PostCommentAsync(pr!.FullName, pr.Number, text, ct)
                    : await clickUp.PostCommentAsync(job.TaskId, text, ct);

                await repo.RecordDeliveryAsync(job.Id, new DeliveryAttempt
                {
                    Destination = destination, StatusCode = code, AttemptNumber = attempt
                }, WriteBackStatus.Delivered, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var last = attempt == WriteBackAttempts;
                log.LogWarning("Write-back attempt {Attempt} failed: {Message}", attempt, ex.Message);
                await repo.RecordDeliveryAsync(job.Id, new DeliveryAttempt
                {
                    Destination = destination, StatusCode = 0, ErrorCode = ex.GetType().Name, AttemptNumber = attempt
                }, last ? WriteBackStatus.Failed : null, ct);
                if (!last) await Task.Delay(Backoff.Delay(attempt, _opt.WriteBackRetryBaseSeconds), ct);
            }
        }
    }
}