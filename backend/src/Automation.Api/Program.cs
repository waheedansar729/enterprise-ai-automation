
// using System.Security.Cryptography;
// using System.Text;
// using System.Text.Json.Serialization;
// using Automation.Core;
// using Automation.Core.Contracts;
// using Automation.Core.Domain;
// using Automation.Core.Services;
// using Automation.Core.Validation;
// using Automation.Infrastructure;
// using Automation.Infrastructure.Persistence;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Options;

// var builder = WebApplication.CreateBuilder(args);

// builder.Services.AddAutomationInfrastructure(builder.Configuration);
// builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 256 * 1024); // request size limit
// builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// // Fail fast if the webhook key is missing (never run with an open webhook).
// var apiKey = builder.Configuration["WebhookAuth:ApiKey"];
// if (string.IsNullOrWhiteSpace(apiKey))
//     throw new InvalidOperationException("WebhookAuth__ApiKey is not configured.");

// // 👈 YEH LINE YAHAN HONI CHAHIYE (builder.Build() se PEHLE)
// builder.Services.AddHttpClient<IGitHubClient, Automation.Infrastructure.GitHub.GitHubService>();

// var app = builder.Build();

// // NGROK MIDDLEWARE
// app.Use(async (ctx, next) =>
// {
//     if (ctx.Request.Headers.ContainsKey("X-Forwarded-Host") && 
//         ctx.Request.Headers["X-Forwarded-Host"].ToString().Contains("ngrok-free.dev"))
//     {
//         ctx.Request.Headers["ngrok-skip-browser-warning"] = "true";
//     }
//     await next();
// });

// // Create tables on startup
// using (var scope = app.Services.CreateScope())
// {
//     await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
// }

// // Correlation ID on every request/response.
// app.Use(async (ctx, next) =>
// {
//     var cid = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault();
//     if (string.IsNullOrWhiteSpace(cid) || cid.Length > 100) cid = Guid.NewGuid().ToString("N");
//     ctx.Items["cid"] = cid;
//     ctx.Response.Headers["X-Correlation-Id"] = cid;
//     using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = cid }))
//         await next();
// });

// bool Authorized(HttpContext ctx)
// {
//     var provided = ctx.Request.Headers["X-Api-Key"].FirstOrDefault() ?? "";
//     return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(apiKey!));
// }

// // Shared by both entry points: dedupe on EventId, save, answer 202.
// async Task<IResult> Enqueue(AppDbContext db, ReviewJob job, string cid, CancellationToken ct)
// {
//     var existing = await db.ReviewJobs.AsNoTracking().FirstOrDefaultAsync(j => j.EventId == job.EventId, ct);
//     if (existing is not null)
//         return Results.Accepted($"/api/v1/reviews/{existing.Id}", new { jobId = existing.Id, duplicate = true, correlationId = cid });

//     db.ReviewJobs.Add(job);
//     try
//     {
//         await db.SaveChangesAsync(ct);
//     }
//     catch (DbUpdateException)
//     {
//         db.ChangeTracker.Clear();
//         var dup = await db.ReviewJobs.AsNoTracking().FirstOrDefaultAsync(j => j.EventId == job.EventId, ct);
//         if (dup is null) throw;
//         return Results.Accepted($"/api/v1/reviews/{dup.Id}", new { jobId = dup.Id, duplicate = true, correlationId = cid });
//     }
//     return Results.Accepted($"/api/v1/reviews/{job.Id}", new { jobId = job.Id, duplicate = false, correlationId = cid });
// }

// var v1 = app.MapGroup("/api/v1");

// // ---- POST /reviews ----
// v1.MapPost("/reviews", async (CreateReviewRequest req, HttpContext http, AppDbContext db,
//     IOptions<ReviewOptions> opt, CancellationToken ct) =>
// {
//     if (!Authorized(http)) return Results.Unauthorized();

//     var errors = RequestValidator.Validate(req, opt.Value);
//     if (errors.Count > 0) return Results.ValidationProblem(errors);

//     var cid = (string)http.Items["cid"]!;

//     if (!string.Equals(req.Status, opt.Value.TargetStatus, StringComparison.OrdinalIgnoreCase))
//         return Results.Ok(new { ignored = true, reason = $"Status '{req.Status}' is not the review status.", correlationId = cid });

//     var job = new ReviewJob
//     {
//         EventId = req.EventId!, TaskId = req.TaskId!, TaskUrl = req.TaskUrl ?? "",
//         Title = req.Title ?? "", Description = req.Description,
//         RepositoryUrl = req.RepositoryUrl, PullRequestUrl = req.PullRequestUrl,
//         CorrelationId = cid
//     };
//     return await Enqueue(db, job, cid, ct);
// });

// // ---- POST /webhooks/github ----
// // v1.MapPost("/webhooks/github", async (HttpRequest request, HttpContext http, AppDbContext db, CancellationToken ct) =>
// // {
// //     var eventType = request.Headers["X-GitHub-Event"].FirstOrDefault();
// //     if (eventType != "pull_request") return Results.Ok(new { ignored = true });

// //     using var reader = new StreamReader(request.Body);
// //     var body = await reader.ReadToEndAsync(ct);
    
// //     try
// //     {
// //         var payload = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(body);
// //         var action = payload.GetProperty("action").GetString();
        
// //         if (action == "opened" || action == "synchronize")
// //         {
// //             var repo = payload.GetProperty("repository").GetProperty("full_name").GetString();
// //             var prNum = payload.GetProperty("number").GetInt32();
// //             var title = payload.GetProperty("pull_request").GetProperty("title").GetString();
// //             var prUrl = payload.GetProperty("pull_request").GetProperty("html_url").GetString();
// //             var repoUrl = payload.GetProperty("repository").GetProperty("html_url").GetString();
// //             var cid = (string)http.Items["cid"]!;

// //             var job = new ReviewJob
// //             {
// //                 EventId = $"github-{repo}-{prNum}-{Guid.NewGuid():N}",
// //                 Source = "GitHub",
// //                 RepositoryUrl = repoUrl,
// //                 PullRequestUrl = prUrl,
// //                 Title = title ?? "",
// //                 TaskId = $"{repo}#{prNum}",
// //                 TaskUrl = prUrl ?? "",
// //                 Status = JobStatus.Queued,
// //                 CreatedAtUtc = DateTime.UtcNow,
// //                 CorrelationId = cid
// //             };

// //             return await Enqueue(db, job, cid, ct);
// //         }
// //     }
// //     catch (Exception ex)
// //     {
// //         return Results.BadRequest(new { error = "Invalid GitHub payload", details = ex.Message });
// //     }

// //     return Results.Ok(new { ignored = true });
// // });
// // ---- POST /webhooks/github ----
// v1.MapPost("/webhooks/github", async (HttpRequest request, HttpContext http, AppDbContext db, IConfiguration cfg, CancellationToken ct) =>
// {
//     var secret = cfg["GitHub:WebhookSecret"];
//     if (string.IsNullOrWhiteSpace(secret))
//         return Results.Json(new { error = "GitHub__WebhookSecret is not configured." }, statusCode: 503);

//     // GitHub payloads can be bigger than our default 256 KB limit.
//     var sizeFeature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
//     if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = 1024 * 1024;

//     using var ms = new MemoryStream();
//     await request.Body.CopyToAsync(ms, ct);
//     var raw = ms.ToArray();

//     // Only GitHub (who knows the secret) may create review jobs.
//     if (!GitHubSignature.Verify(secret, raw, request.Headers["X-Hub-Signature-256"].FirstOrDefault()))
//         return Results.Unauthorized();

//     var eventType = request.Headers["X-GitHub-Event"].FirstOrDefault();
//     if (eventType == "ping") return Results.Ok(new { pong = true });
//     if (eventType != "pull_request") return Results.Ok(new { ignored = true, reason = "Not a pull_request event." });

//     try
//     {
//         using var doc = System.Text.Json.JsonDocument.Parse(raw);
//         var root = doc.RootElement;

//         var action = root.GetProperty("action").GetString();
//         if (action is not ("opened" or "synchronize" or "reopened"))
//             return Results.Ok(new { ignored = true, reason = $"Action '{action}' is not reviewed." });

//         var pr = root.GetProperty("pull_request");
//         var title = pr.GetProperty("title").GetString() ?? "";
//         var prUrl = pr.GetProperty("html_url").GetString() ?? "";
//         var repoUrl = root.GetProperty("repository").GetProperty("html_url").GetString();
//         var headSha = pr.GetProperty("head").GetProperty("sha").GetString();
//         if (string.IsNullOrWhiteSpace(headSha)) headSha = request.Headers["X-GitHub-Delivery"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");

//         if (!PullRequestRef.TryParse(prUrl, out var parsed) || parsed is null)
//             return Results.BadRequest(new { error = "pull_request.html_url is not a valid github.com pull request URL." });

//         var cid = (string)http.Items["cid"]!;
//         var job = new ReviewJob
//         {
//             // Same PR + same commit = same event, so a GitHub redelivery does not create a second review.
//             EventId = $"github-{parsed.FullName}-{parsed.Number}-{headSha}",
//             Source = "GitHub",
//             RepositoryUrl = repoUrl,
//             PullRequestUrl = prUrl,
//             Title = title,
//             TaskId = $"{parsed.FullName}#{parsed.Number}",
//             TaskUrl = prUrl,
//             Status = JobStatus.Queued,
//             CreatedAtUtc = DateTime.UtcNow,
//             CorrelationId = cid
//         };
//         return await Enqueue(db, job, cid, ct);
//     }
//     catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
//     {
//         return Results.BadRequest(new { error = "Invalid GitHub payload." });
//     }
// });

// // ---- POST /webhooks/clickup ----
// v1.MapPost("/webhooks/clickup", async (HttpRequest request, HttpContext http, AppDbContext db, IClickUpClient clickUp,
//     IOptions<ReviewOptions> opt, IConfiguration cfg, CancellationToken ct) =>
// {
//     var secret = cfg["ClickUp:WebhookSecret"];
//     if (string.IsNullOrWhiteSpace(secret))
//         return Results.Json(new { error = "ClickUp__WebhookSecret is not configured." }, statusCode: 503);

//     using var ms = new MemoryStream();
//     await request.Body.CopyToAsync(ms, ct);
//     var raw = ms.ToArray();

//     var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), raw)).ToLowerInvariant();
//     var given = (request.Headers["X-Signature"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();
//     if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
//         return Results.Unauthorized();

//     var cid = (string)http.Items["cid"]!;
//     var evt = ClickUpWebhookParser.Parse(raw);
//     if (evt is null) return Results.BadRequest(new { error = "Unreadable webhook body." });
//     if (evt.Event != "taskStatusUpdated" || string.IsNullOrWhiteSpace(evt.TaskId))
//         return Results.Ok(new { ignored = true, reason = "Not a taskStatusUpdated event.", correlationId = cid });
//     if (!string.Equals(evt.NewStatus, opt.Value.TargetStatus, StringComparison.OrdinalIgnoreCase))
//         return Results.Ok(new { ignored = true, reason = $"Status '{evt.NewStatus}' is not the review status.", correlationId = cid });

//     ClickUpTask? task = null;
//     try
//     {
//         using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
//         cts.CancelAfter(TimeSpan.FromSeconds(4));
//         task = await clickUp.GetTaskAsync(evt.TaskId, cts.Token);
//     }
//     catch (Exception) { }

//     var job = new ReviewJob
//     {
//         EventId = evt.EventId, TaskId = evt.TaskId,
//         TaskUrl = task?.Url ?? $"https://app.clickup.com/t/{evt.TaskId}",
//         Title = task?.Name ?? $"ClickUp task {evt.TaskId}",
//         Description = task?.Description,
//         CorrelationId = cid
//     };
//     return await Enqueue(db, job, cid, ct);
// });

// // ---- GET /reviews ----
// v1.MapGet("/reviews", async (AppDbContext db, int? page, int? pageSize, string? status, string? taskId, CancellationToken ct) =>
// {
//     var p = Math.Max(1, page ?? 1);
//     var ps = Math.Clamp(pageSize ?? 20, 1, 100);
//     IQueryable<ReviewJob> q = db.ReviewJobs.AsNoTracking();
//     if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var st)) q = q.Where(j => j.Status == st);
//     if (!string.IsNullOrWhiteSpace(taskId)) q = q.Where(j => j.TaskId == taskId);

//     var total = await q.CountAsync(ct);
//     var items = await q.OrderByDescending(j => j.CreatedAtUtc).Skip((p - 1) * ps).Take(ps)
//         .Select(j => new ReviewListItem(j.Id, j.TaskId, j.TaskUrl, j.Title, j.Status, j.WriteBackStatus,
//             j.OverallResult, j.Summary, j.CreatedAtUtc, j.StartedAtUtc, j.CompletedAtUtc, j.Findings.Count))
//         .ToListAsync(ct);
//     return Results.Ok(new { items, page = p, pageSize = ps, total });
// });

// // ---- GET /reviews/{id} ----
// v1.MapGet("/reviews/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
// {
//     var j = await db.ReviewJobs.AsNoTracking().Include(x => x.Findings).Include(x => x.DeliveryAttempts)
//         .FirstOrDefaultAsync(x => x.Id == id, ct);
//     if (j is null) return Results.NotFound();
//     return Results.Ok(new
//     {
//         j.Id, j.EventId, j.TaskId, j.TaskUrl, j.Title, j.Status, j.WriteBackStatus, j.OverallResult, j.Summary,
//         j.Model, j.PromptVersion, j.Error, j.Attempts, j.CorrelationId, j.CreatedAtUtc, j.StartedAtUtc, j.CompletedAtUtc,
//         limitations = CommentFormatter.ParseLimitations(j.LimitationsJson),
//         findings = j.Findings.OrderBy(f => Severity.Rank(f.Severity)),
//         deliveryAttempts = j.DeliveryAttempts.OrderBy(a => a.AttemptedAtUtc)
//     });
// });

// // ---- POST /reviews/{id}/retry ----
// v1.MapPost("/reviews/{id:guid}/retry", async (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
// {
//     if (!Authorized(http)) return Results.Unauthorized();
//     var job = await db.ReviewJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
//     if (job is null) return Results.NotFound();

//     var failedAnalysis = job.Status == JobStatus.Failed;
//     var failedWriteBack = job.Status == JobStatus.Completed && job.WriteBackStatus == WriteBackStatus.Failed;
//     if (!failedAnalysis && !failedWriteBack)
//         return Results.Conflict(new { error = "Only failed jobs or failed write-backs can be retried." });

//     job.Status = JobStatus.Queued;
//     job.Attempts = 0;
//     job.Error = null;
//     job.NextAttemptAtUtc = null;
//     if (failedWriteBack) job.WriteBackStatus = WriteBackStatus.Pending;
//     await db.SaveChangesAsync(ct);
//     return Results.Accepted($"/api/v1/reviews/{job.Id}", new { jobId = job.Id });
// });

// // ---- health ----
// v1.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
// v1.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
//     await db.Database.CanConnectAsync(ct)
//         ? Results.Ok(new { status = "ready", database = "ok" })
//         : Results.Json(new { status = "unavailable", database = "down" }, statusCode: 503));

// // ---- metrics ----
// v1.MapGet("/metrics/summary", async (AppDbContext db, int? hours, CancellationToken ct) =>
// {
//     var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours ?? 24, 1, 24 * 90));
//     var recent = db.ReviewJobs.AsNoTracking().Where(j => j.CreatedAtUtc >= since);

//     var byStatus = await recent.GroupBy(j => j.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
//     int Count(JobStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

//     var times = await recent.Where(j => j.Status == JobStatus.Completed && j.StartedAtUtc != null && j.CompletedAtUtc != null)
//         .Select(j => new { S = j.StartedAtUtc, C = j.CompletedAtUtc }).ToListAsync(ct);
//     var secs = times.Select(t => (t.C!.Value - t.S!.Value).TotalSeconds).OrderBy(x => x).ToList();

//     var outcomes = await recent.Where(j => j.OverallResult != null)
//         .GroupBy(j => j.OverallResult!).Select(g => new { Result = g.Key, Count = g.Count() }).ToListAsync(ct);
//     var writeBackFailed = await recent.CountAsync(j => j.WriteBackStatus == WriteBackStatus.Failed, ct);

//     var queueDepth = await db.ReviewJobs.CountAsync(j => j.Status == JobStatus.Queued, ct);
//     var oldest = await db.ReviewJobs.Where(j => j.Status == JobStatus.Queued).MinAsync(j => (DateTime?)j.CreatedAtUtc, ct);

//     return Results.Ok(new
//     {
//         windowHours = (int)(DateTime.UtcNow - since).TotalHours,
//         total = byStatus.Sum(x => x.Count),
//         queued = Count(JobStatus.Queued), running = Count(JobStatus.Running),
//         completed = Count(JobStatus.Completed), failed = Count(JobStatus.Failed),
//         writeBackFailed,
//         medianSeconds = Percentile(secs, 0.5), p95Seconds = Percentile(secs, 0.95),
//         outcomes,
//         queueDepth,
//         oldestQueuedAgeSeconds = oldest is null ? (double?)null : (DateTime.UtcNow - oldest.Value).TotalSeconds
//     });
// });

// app.Run();

// static double? Percentile(List<double> sorted, double p)
// {
//     if (sorted.Count == 0) return null;
//     var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
//     return Math.Round(sorted[Math.Clamp(idx, 0, sorted.Count - 1)], 2);
// }

// public record ReviewListItem(Guid Id, string TaskId, string TaskUrl, string Title, JobStatus Status,
//     WriteBackStatus WriteBackStatus, string? OverallResult, string? Summary, DateTime CreatedAtUtc,
//     DateTime? StartedAtUtc, DateTime? CompletedAtUtc, int FindingCount)
// {
//     public double? DurationSeconds =>
//         StartedAtUtc.HasValue && CompletedAtUtc.HasValue ? Math.Round((CompletedAtUtc.Value - StartedAtUtc.Value).TotalSeconds, 2) : null;
// }
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Automation.Core;
using Automation.Core.Contracts;
using Automation.Core.Domain;
using Automation.Core.Services;
using Automation.Core.Validation;
using Automation.Infrastructure;
using Automation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddAutomationInfrastructure(builder.Configuration);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 256 * 1024); // request size limit
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// 👈 CORS ADD KIYA GAYA HAI
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Fail fast if the webhook key is missing (never run with an open webhook).
var apiKey = builder.Configuration["WebhookAuth:ApiKey"];
if (string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("WebhookAuth__ApiKey is not configured.");

// 👈 YEH LINE YAHAN HONI CHAHIYE (builder.Build() se PEHLE)
builder.Services.AddHttpClient<IGitHubClient, Automation.Infrastructure.GitHub.GitHubService>();

var app = builder.Build();

// 👈 CORS USE KIYA GAYA HAI
app.UseCors("AllowAll");

// NGROK MIDDLEWARE
app.Use(async (ctx, next) =>
{
    if (ctx.Request.Headers.ContainsKey("X-Forwarded-Host") && 
        ctx.Request.Headers["X-Forwarded-Host"].ToString().Contains("ngrok-free.dev"))
    {
        ctx.Request.Headers["ngrok-skip-browser-warning"] = "true";
    }
    await next();
});

// Create tables on startup
using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
}

// Correlation ID on every request/response.
app.Use(async (ctx, next) =>
{
    var cid = ctx.Request.Headers["X-Correlation-Id"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(cid) || cid.Length > 100) cid = Guid.NewGuid().ToString("N");
    ctx.Items["cid"] = cid;
    ctx.Response.Headers["X-Correlation-Id"] = cid;
    using (app.Logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = cid }))
        await next();
});

bool Authorized(HttpContext ctx)
{
    var provided = ctx.Request.Headers["X-Api-Key"].FirstOrDefault() ?? "";
    return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(apiKey!));
}

// Shared by both entry points: dedupe on EventId, save, answer 202.
async Task<IResult> Enqueue(AppDbContext db, ReviewJob job, string cid, CancellationToken ct)
{
    var existing = await db.ReviewJobs.AsNoTracking().FirstOrDefaultAsync(j => j.EventId == job.EventId, ct);
    if (existing is not null)
        return Results.Accepted($"/api/v1/reviews/{existing.Id}", new { jobId = existing.Id, duplicate = true, correlationId = cid });

    db.ReviewJobs.Add(job);
    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
        db.ChangeTracker.Clear();
        var dup = await db.ReviewJobs.AsNoTracking().FirstOrDefaultAsync(j => j.EventId == job.EventId, ct);
        if (dup is null) throw;
        return Results.Accepted($"/api/v1/reviews/{dup.Id}", new { jobId = dup.Id, duplicate = true, correlationId = cid });
    }
    return Results.Accepted($"/api/v1/reviews/{job.Id}", new { jobId = job.Id, duplicate = false, correlationId = cid });
}

var v1 = app.MapGroup("/api/v1");

// ---- POST /reviews ----
v1.MapPost("/reviews", async (CreateReviewRequest req, HttpContext http, AppDbContext db,
    IOptions<ReviewOptions> opt, CancellationToken ct) =>
{
    if (!Authorized(http)) return Results.Unauthorized();

    var errors = RequestValidator.Validate(req, opt.Value);
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var cid = (string)http.Items["cid"]!;

    if (!string.Equals(req.Status, opt.Value.TargetStatus, StringComparison.OrdinalIgnoreCase))
        return Results.Ok(new { ignored = true, reason = $"Status '{req.Status}' is not the review status.", correlationId = cid });

    var job = new ReviewJob
    {
        EventId = req.EventId!, TaskId = req.TaskId!, TaskUrl = req.TaskUrl ?? "",
        Title = req.Title ?? "", Description = req.Description,
        RepositoryUrl = req.RepositoryUrl, PullRequestUrl = req.PullRequestUrl,
        CorrelationId = cid
    };
    return await Enqueue(db, job, cid, ct);
});

// ---- POST /webhooks/github ----
v1.MapPost("/webhooks/github", async (HttpRequest request, HttpContext http, AppDbContext db, IConfiguration cfg, CancellationToken ct) =>
{
    // 1. Secret check
    var secret = cfg["GitHub:WebhookSecret"];
    if (string.IsNullOrWhiteSpace(secret))
        return Results.Json(new { error = "GitHub__WebhookSecret is not configured." }, statusCode: 503);

    // GitHub payloads can be bigger than our default 256 KB limit.
    var sizeFeature = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
    if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = 1024 * 1024;
    
    // 2. Raw body read karein
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms, ct);
    var raw = ms.ToArray();

    // Only GitHub (who knows the secret) may create review jobs.
    // 3. HMAC Signature verify karein
    if (!GitHubSignature.Verify(secret, raw, request.Headers["X-Hub-Signature-256"].FirstOrDefault()))
        return Results.Unauthorized();
    
    // 4. Event type check karein
    var eventType = request.Headers["X-GitHub-Event"].FirstOrDefault();
    if (eventType == "ping") return Results.Ok(new { pong = true });
    if (eventType != "pull_request") return Results.Ok(new { ignored = true, reason = "Not a pull_request event." });

    try
    {
        // 5. JSON parse karein
        using var doc = System.Text.Json.JsonDocument.Parse(raw);
        var root = doc.RootElement;

        var action = root.GetProperty("action").GetString();
        if (action is not ("opened" or "synchronize" or "reopened"))
            return Results.Ok(new { ignored = true, reason = $"Action '{action}' is not reviewed." });
        
        // 6. PR details fetch karein
        var pr = root.GetProperty("pull_request");
        var title = pr.GetProperty("title").GetString() ?? "";
        var prUrl = pr.GetProperty("html_url").GetString() ?? "";
        var repoUrl = root.GetProperty("repository").GetProperty("html_url").GetString();
        var headSha = pr.GetProperty("head").GetProperty("sha").GetString();
        if (string.IsNullOrWhiteSpace(headSha)) headSha = request.Headers["X-GitHub-Delivery"].FirstOrDefault() ?? Guid.NewGuid().ToString("N");
        
        // 7. PR URL validate karein
        if (!PullRequestRef.TryParse(prUrl, out var parsed) || parsed is null)
            return Results.BadRequest(new { error = "pull_request.html_url is not a valid github.com pull request URL." });

        var cid = (string)http.Items["cid"]!;
        // 8. Database mein job save karein
        var job = new ReviewJob
        {
            // Same PR + same commit = same event, so a GitHub redelivery does not create a second review.
            EventId = $"github-{parsed.FullName}-{parsed.Number}-{headSha}",
            Source = "GitHub",
            RepositoryUrl = repoUrl,
            PullRequestUrl = prUrl,
            Title = title,
            TaskId = $"{parsed.FullName}#{parsed.Number}",
            TaskUrl = prUrl,
            Status = JobStatus.Queued,
            CreatedAtUtc = DateTime.UtcNow,
            CorrelationId = cid
        };
        return await Enqueue(db, job, cid, ct);
    }
    catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = "Invalid GitHub payload." });
    }
});

// ---- POST /webhooks/clickup ----
v1.MapPost("/webhooks/clickup", async (HttpRequest request, HttpContext http, AppDbContext db, IClickUpClient clickUp,
    IOptions<ReviewOptions> opt, IConfiguration cfg, CancellationToken ct) =>
{
    // 1. Secret check (HMAC verify)
    var secret = cfg["ClickUp:WebhookSecret"];
    if (string.IsNullOrWhiteSpace(secret))
        return Results.Json(new { error = "ClickUp__WebhookSecret is not configured." }, statusCode: 503);

    // 2. Raw body read karein
    using var ms = new MemoryStream();
    await request.Body.CopyToAsync(ms, ct);
    var raw = ms.ToArray();

     // 3. HMAC Signature verify karein
    var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), raw)).ToLowerInvariant();
    var given = (request.Headers["X-Signature"].FirstOrDefault() ?? "").Trim().ToLowerInvariant();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(given), Encoding.UTF8.GetBytes(expected)))
        return Results.Unauthorized();

    // 4. ClickUp ka JSON parse karein
    var cid = (string)http.Items["cid"]!;
    var evt = ClickUpWebhookParser.Parse(raw);
    if (evt is null) return Results.BadRequest(new { error = "Unreadable webhook body." });

    // 5. Event type check karein
    if (evt.Event != "taskStatusUpdated" || string.IsNullOrWhiteSpace(evt.TaskId))
        return Results.Ok(new { ignored = true, reason = "Not a taskStatusUpdated event.", correlationId = cid });

        // 6. Status check karein (sirf "Under Review")
    if (!string.Equals(evt.NewStatus, opt.Value.TargetStatus, StringComparison.OrdinalIgnoreCase))
        return Results.Ok(new { ignored = true, reason = $"Status '{evt.NewStatus}' is not the review status.", correlationId = cid });

    // 7. ClickUp se task ki details fetch karein
    ClickUpTask? task = null;
    try
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(4));
        task = await clickUp.GetTaskAsync(evt.TaskId, cts.Token);
    }
    catch (Exception) { }
    
    // 8. Database mein job save karein
    var job = new ReviewJob
    {
        EventId = evt.EventId, TaskId = evt.TaskId,
        TaskUrl = task?.Url ?? $"https://app.clickup.com/t/{evt.TaskId}",
        Title = task?.Name ?? $"ClickUp task {evt.TaskId}",
        Description = task?.Description,
        CorrelationId = cid
    };
    return await Enqueue(db, job, cid, ct);
});

// ---- GET /reviews ----
v1.MapGet("/reviews", async (AppDbContext db, int? page, int? pageSize, string? status, string? taskId, CancellationToken ct) =>
{
    var p = Math.Max(1, page ?? 1);
    var ps = Math.Clamp(pageSize ?? 20, 1, 100);
    IQueryable<ReviewJob> q = db.ReviewJobs.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<JobStatus>(status, true, out var st)) q = q.Where(j => j.Status == st);
    if (!string.IsNullOrWhiteSpace(taskId)) q = q.Where(j => j.TaskId == taskId);

    var total = await q.CountAsync(ct);
    var items = await q.OrderByDescending(j => j.CreatedAtUtc).Skip((p - 1) * ps).Take(ps)
        .Select(j => new ReviewListItem(j.Id, j.TaskId, j.TaskUrl, j.Title, j.Status, j.WriteBackStatus,
            j.OverallResult, j.Summary, j.CreatedAtUtc, j.StartedAtUtc, j.CompletedAtUtc, j.Findings.Count))
        .ToListAsync(ct);
    return Results.Ok(new { items, page = p, pageSize = ps, total });
});

// ---- GET /reviews/{id} ----
v1.MapGet("/reviews/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
{
    var j = await db.ReviewJobs.AsNoTracking().Include(x => x.Findings).Include(x => x.DeliveryAttempts)
        .FirstOrDefaultAsync(x => x.Id == id, ct);
    if (j is null) return Results.NotFound();
    return Results.Ok(new
    {
        j.Id, j.EventId, j.TaskId, j.TaskUrl, j.Title, j.Status, j.WriteBackStatus, j.OverallResult, j.Summary,
        j.Model, j.PromptVersion, j.Error, j.Attempts, j.CorrelationId, j.CreatedAtUtc, j.StartedAtUtc, j.CompletedAtUtc,
        limitations = CommentFormatter.ParseLimitations(j.LimitationsJson),
        findings = j.Findings.OrderBy(f => Severity.Rank(f.Severity)),
        deliveryAttempts = j.DeliveryAttempts.OrderBy(a => a.AttemptedAtUtc)
    });
});

// ---- POST /reviews/{id}/retry ----
v1.MapPost("/reviews/{id:guid}/retry", async (Guid id, HttpContext http, AppDbContext db, CancellationToken ct) =>
{
    if (!Authorized(http)) return Results.Unauthorized();
    var job = await db.ReviewJobs.FirstOrDefaultAsync(j => j.Id == id, ct);
    if (job is null) return Results.NotFound();

    var failedAnalysis = job.Status == JobStatus.Failed;
    var failedWriteBack = job.Status == JobStatus.Completed && job.WriteBackStatus == WriteBackStatus.Failed;
    if (!failedAnalysis && !failedWriteBack)
        return Results.Conflict(new { error = "Only failed jobs or failed write-backs can be retried." });

    job.Status = JobStatus.Queued;
    job.Attempts = 0;
    job.Error = null;
    job.NextAttemptAtUtc = null;
    if (failedWriteBack) job.WriteBackStatus = WriteBackStatus.Pending;
    await db.SaveChangesAsync(ct);
    return Results.Accepted($"/api/v1/reviews/{job.Id}", new { jobId = job.Id });
});

// ---- health ----
v1.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
v1.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct)
        ? Results.Ok(new { status = "ready", database = "ok" })
        : Results.Json(new { status = "unavailable", database = "down" }, statusCode: 503));

// ---- metrics ----
v1.MapGet("/metrics/summary", async (AppDbContext db, int? hours, CancellationToken ct) =>
{
    var since = DateTime.UtcNow.AddHours(-Math.Clamp(hours ?? 24, 1, 24 * 90));
    var recent = db.ReviewJobs.AsNoTracking().Where(j => j.CreatedAtUtc >= since);

    var byStatus = await recent.GroupBy(j => j.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync(ct);
    int Count(JobStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

    var times = await recent.Where(j => j.Status == JobStatus.Completed && j.StartedAtUtc != null && j.CompletedAtUtc != null)
        .Select(j => new { S = j.StartedAtUtc, C = j.CompletedAtUtc }).ToListAsync(ct);
    var secs = times.Select(t => (t.C!.Value - t.S!.Value).TotalSeconds).OrderBy(x => x).ToList();

    var outcomes = await recent.Where(j => j.OverallResult != null)
        .GroupBy(j => j.OverallResult!).Select(g => new { Result = g.Key, Count = g.Count() }).ToListAsync(ct);
    var writeBackFailed = await recent.CountAsync(j => j.WriteBackStatus == WriteBackStatus.Failed, ct);

    var queueDepth = await db.ReviewJobs.CountAsync(j => j.Status == JobStatus.Queued, ct);
    var oldest = await db.ReviewJobs.Where(j => j.Status == JobStatus.Queued).MinAsync(j => (DateTime?)j.CreatedAtUtc, ct);

    return Results.Ok(new
    {
        windowHours = (int)(DateTime.UtcNow - since).TotalHours,
        total = byStatus.Sum(x => x.Count),
        queued = Count(JobStatus.Queued), running = Count(JobStatus.Running),
        completed = Count(JobStatus.Completed), failed = Count(JobStatus.Failed),
        writeBackFailed,
        medianSeconds = Percentile(secs, 0.5), p95Seconds = Percentile(secs, 0.95),
        outcomes,
        queueDepth,
        oldestQueuedAgeSeconds = oldest is null ? (double?)null : (DateTime.UtcNow - oldest.Value).TotalSeconds
    });
});

app.Run();

static double? Percentile(List<double> sorted, double p)
{
    if (sorted.Count == 0) return null;
    var idx = (int)Math.Ceiling(p * sorted.Count) - 1;
    return Math.Round(sorted[Math.Clamp(idx, 0, sorted.Count - 1)], 2);
}

public record ReviewListItem(Guid Id, string TaskId, string TaskUrl, string Title, JobStatus Status,
    WriteBackStatus WriteBackStatus, string? OverallResult, string? Summary, DateTime CreatedAtUtc,
    DateTime? StartedAtUtc, DateTime? CompletedAtUtc, int FindingCount)
{
    public double? DurationSeconds =>
        StartedAtUtc.HasValue && CompletedAtUtc.HasValue ? Math.Round((CompletedAtUtc.Value - StartedAtUtc.Value).TotalSeconds, 2) : null;
}