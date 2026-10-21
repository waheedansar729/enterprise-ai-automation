using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Automation.Core.Domain;
using Automation.Infrastructure.Persistence;

namespace Automation.Api;

[ApiController]
[Route("api/v1/webhooks/github")]
public class GitHubWebhookController(AppDbContext db) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post()
    {
        var eventType = Request.Headers["X-GitHub-Event"].FirstOrDefault();
        if (eventType != "pull_request") return Ok();

        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        var payload = JsonSerializer.Deserialize<JsonElement>(body);

        var action = payload.GetProperty("action").GetString();
        if (action == "opened" || action == "synchronize")
        {
            var repo = payload.GetProperty("repository").GetProperty("full_name").GetString(); // e.g., owner/repo
            var prNum = payload.GetProperty("number").GetInt32();
            var title = payload.GetProperty("pull_request").GetProperty("title").GetString();
            var prUrl = payload.GetProperty("pull_request").GetProperty("html_url").GetString();
            var repoUrl = payload.GetProperty("repository").GetProperty("html_url").GetString();

            var job = new ReviewJob
            {
                Id = Guid.NewGuid(),
                Source = "GitHub", // 👈 Yahan Source set kiya
                RepositoryUrl = repoUrl,
                PullRequestUrl = prUrl, // e.g., https://github.com/owner/repo/pull/12
                Title = title,
                TaskId = $"{repo}#{prNum}", // e.g., owner/repo#12
                TaskUrl = prUrl,
                Status = JobStatus.Queued,
                CreatedAtUtc = DateTime.UtcNow,
                CorrelationId = Guid.NewGuid().ToString("N")
            };

            db.ReviewJobs.Add(job);
            await db.SaveChangesAsync();
        }
        return Ok();
    }
}