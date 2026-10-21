using Automation.Core;
using Automation.Core.Contracts;
using Automation.Core.Domain;
using Automation.Core.Services;
using Automation.Core.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Automation.UnitTests;

public class SecretRedactorTests
{
    [Theory]
    [InlineData("password=hunter2 ok", "hunter2")]
    [InlineData("use Bearer abc123.def456 here", "abc123.def456")]
    [InlineData("key AKIAABCDEFGHIJKLMNOP end", "AKIAABCDEFGHIJKLMNOP")]
    [InlineData("api_key: sk-1234567890abcdefghij", "sk-1234567890abcdefghij")]
    public void Removes_secrets(string input, string secret)
    {
        var output = SecretRedactor.Redact(input);
        Assert.DoesNotContain(secret, output);
        Assert.Contains("REDACTED", output);
    }

    [Fact]
    public void Leaves_normal_text_alone() =>
        Assert.Equal("Check retry handling", SecretRedactor.Redact("Check retry handling"));
}

public class RequestValidatorTests
{
    private static readonly ReviewOptions Opt = new();

    private static CreateReviewRequest Valid() => new("evt-1", "t1", "https://app.clickup.com/t/t1", "Under Review",
        "Title", "Desc", "https://github.com/acme/svc", "https://github.com/acme/svc/pull/1", null);

    [Fact] public void Valid_request_has_no_errors() => Assert.Empty(RequestValidator.Validate(Valid(), Opt));

    [Fact]
    public void Missing_ids_are_rejected()
    {
        var e = RequestValidator.Validate(Valid() with { EventId = "", TaskId = null }, Opt);
        Assert.Contains("eventId", e.Keys);
        Assert.Contains("taskId", e.Keys);
    }

    [Theory]
    [InlineData("http://github.com/acme/svc")]      // not https
    [InlineData("https://evil.example.com/repo")]   // host not approved
    [InlineData("https://github.com.evil.com/x")]   // look-alike host
    [InlineData("not a url")]
    public void Bad_repository_urls_are_rejected(string url) =>
        Assert.Contains("repositoryUrl", RequestValidator.Validate(Valid() with { RepositoryUrl = url }, Opt).Keys);
}

public class ParserAndValidatorTests
{
    private const string Good = """
        ```json
        {"overallResult":"Needs_Attention","summary":"s","findings":[{"severity":"HIGH","category":"c","title":"t","evidence":"e","recommendation":"r","confidence":"medium"}],"limitations":[]}
        ```
        """;

    [Fact]
    public void Parses_fenced_json_and_normalizes()
    {
        var r = ReviewResultParser.Parse(Good);
        ReviewResultValidator.Validate(r);
        Assert.Equal("needs_attention", r.OverallResult);
        Assert.Equal("high", r.Findings[0].Severity);
        Assert.NotEmpty(r.Limitations); // default limitation is added
    }

    [Theory]
    [InlineData("")]
    [InlineData("no json here")]
    [InlineData("{ broken")]
    public void Garbage_throws(string text) =>
        Assert.Throws<InvalidModelOutputException>(() => ReviewResultParser.Parse(text));

    [Fact]
    public void Unknown_severity_throws()
    {
        var r = new ReviewResult { OverallResult = "needs_attention", Summary = "s",
            Findings = { new FindingDto { Severity = "scary", Title = "t" } } };
        Assert.Throws<InvalidModelOutputException>(() => ReviewResultValidator.Validate(r));
    }
}

public class ClickUpWebhookParserTests
{
    private const string Body = """
        {"event":"taskStatusUpdated","webhook_id":"wh1","task_id":"86abc123",
         "history_items":[{"id":"555","date":"1700000000000","field":"status",
           "before":{"status":"in progress"},"after":{"status":"under review"}}]}
        """;

    [Fact]
    public void Reads_task_id_new_status_and_stable_event_id()
    {
        var e = ClickUpWebhookParser.Parse(System.Text.Encoding.UTF8.GetBytes(Body));
        Assert.NotNull(e);
        Assert.Equal("taskStatusUpdated", e!.Event);
        Assert.Equal("86abc123", e.TaskId);
        Assert.Equal("under review", e.NewStatus);
        Assert.Equal("clickup-555", e.EventId);
    }

    [Fact]
    public void Garbage_body_returns_null() =>
        Assert.Null(ClickUpWebhookParser.Parse(System.Text.Encoding.UTF8.GetBytes("not json")));
}

public class CommentFormatterTests
{
    [Fact]
    public void Comment_contains_summary_counts_and_disclaimer()
    {
        var job = new ReviewJob { OverallResult = "needs_attention", Summary = "Retry may duplicate.",
            LimitationsJson = "[\"Tests were not run.\"]" };
        job.Findings.Add(new ReviewFinding { Severity = "high", Title = "A" });
        job.Findings.Add(new ReviewFinding { Severity = "medium", Title = "B" });
        job.Findings.Add(new ReviewFinding { Severity = "medium", Title = "C" });

        var text = CommentFormatter.Format(job);
        Assert.Contains("Needs attention", text);
        Assert.Contains("1 high, 2 medium", text);
        Assert.Contains("Tests were not run.", text);
        Assert.Contains("engineer must verify", text);
    }
}

public class ReviewProcessorTests
{
    // ---- fakes -------------------------------------------------------------------------
    private class FakeRepo(ReviewJob job) : IReviewRepository
    {
        public List<string> Log = new();
        public Task<ReviewJob?> ClaimNextAsync(CancellationToken ct) => Task.FromResult<ReviewJob?>(job);
        public Task<ReviewJob?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<ReviewJob?>(job);
        public Task SaveAnalysisAsync(Guid id, ReviewResult r, string model, string pv, CancellationToken ct)
        {
            job.OverallResult = r.OverallResult; job.Summary = r.Summary; job.Status = JobStatus.Completed;
            job.Findings = r.Findings.Select(f => new ReviewFinding { Severity = f.Severity, Title = f.Title }).ToList();
            Log.Add("saved"); return Task.CompletedTask;
        }
        public Task SetStatusAsync(Guid id, JobStatus s, CancellationToken ct) { job.Status = s; return Task.CompletedTask; }
        public Task RecordDeliveryAsync(Guid id, DeliveryAttempt a, WriteBackStatus? s, CancellationToken ct)
        {
            job.DeliveryAttempts.Add(a); if (s is not null) job.WriteBackStatus = s.Value; return Task.CompletedTask;
        }
        public Task RetryOrFailAsync(Guid id, string error, bool transient, int max, CancellationToken ct)
        {
            job.Error = error; job.Status = transient && job.Attempts < max ? JobStatus.Queued : JobStatus.Failed;
            Log.Add("retryOrFail:" + job.Status); return Task.CompletedTask;
        }
        public Task<int> RequeueStaleAsync(TimeSpan t, CancellationToken ct) => Task.FromResult(0);
    }

    private class FakeLlm(Func<ReviewResult> produce) : ILlmClient
    {
        public int Calls;
        public string ModelName => "fake";
        public Task<ReviewResult> ReviewAsync(string s, string u, CancellationToken ct) { Calls++; return Task.FromResult(produce()); }
    }

    private class FakeClickUp(int failFirst = 0) : IClickUpClient
    {
        public int Calls;
        public Task<int> PostCommentAsync(string taskId, string text, CancellationToken ct)
        {
            Calls++;
            if (Calls <= failFirst) throw new TransientException("boom");
            return Task.FromResult(200);
        }
        public Task<ClickUpTask?> GetTaskAsync(string taskId, CancellationToken ct) => Task.FromResult<ClickUpTask?>(null);
    }

    private static ReviewJob NewJob() => new() { TaskId = "t1", Title = "x", Description = "retry", Attempts = 1, Status = JobStatus.Running };
    private static ReviewResult Ok() => new() { OverallResult = "needs_attention", Summary = "s",
        Findings = { new FindingDto { Severity = "high", Title = "t", Confidence = "high" } } };

    private static ReviewProcessor Make(FakeRepo repo, ILlmClient llm, IClickUpClient cu) =>
        new(repo, llm, cu, Options.Create(new ReviewOptions { WriteBackRetryBaseSeconds = 0 }), NullLogger<ReviewProcessor>.Instance);

    // ---- tests -------------------------------------------------------------------------
    [Fact]
    public async Task Happy_path_saves_analysis_and_posts_comment_once()
    {
        var job = NewJob(); var llm = new FakeLlm(Ok); var cu = new FakeClickUp();
        await Make(new FakeRepo(job), llm, cu).ProcessAsync(job, default);

        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.Equal(WriteBackStatus.Delivered, job.WriteBackStatus);
        Assert.Equal(1, cu.Calls);
    }

    [Fact]
    public async Task ClickUp_failure_does_not_erase_the_analysis()
    {
        var job = NewJob(); var cu = new FakeClickUp(failFirst: 99);
        await Make(new FakeRepo(job), new FakeLlm(Ok), cu).ProcessAsync(job, default);

        Assert.Equal(JobStatus.Completed, job.Status);          // analysis kept
        Assert.Equal(WriteBackStatus.Failed, job.WriteBackStatus); // delivery tracked separately
        Assert.Equal(3, job.DeliveryAttempts.Count);
    }

    [Fact]
    public async Task ClickUp_recovers_on_second_try()
    {
        var job = NewJob(); var cu = new FakeClickUp(failFirst: 1);
        await Make(new FakeRepo(job), new FakeLlm(Ok), cu).ProcessAsync(job, default);
        Assert.Equal(WriteBackStatus.Delivered, job.WriteBackStatus);
        Assert.Equal(2, cu.Calls);
    }

    [Fact]
    public async Task Transient_llm_error_requeues_the_job()
    {
        var job = NewJob(); var repo = new FakeRepo(job);
        await Make(repo, new FakeLlm(() => throw new TransientException("timeout")), new FakeClickUp()).ProcessAsync(job, default);
        Assert.Equal(JobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task Transient_error_after_max_attempts_fails_the_job()
    {
        var job = NewJob(); job.Attempts = 3;
        await Make(new FakeRepo(job), new FakeLlm(() => throw new TransientException("timeout")), new FakeClickUp()).ProcessAsync(job, default);
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    [Fact]
    public async Task Malformed_model_output_is_not_saved()
    {
        var job = NewJob(); var repo = new FakeRepo(job);
        await Make(repo, new FakeLlm(() => new ReviewResult { OverallResult = "banana", Summary = "s" }), new FakeClickUp()).ProcessAsync(job, default);
        Assert.DoesNotContain("saved", repo.Log);
        Assert.Null(job.Summary);
    }

    [Fact]
    public async Task Replay_after_failed_writeback_skips_the_llm()
    {
        var job = NewJob(); job.Summary = "already analysed"; job.OverallResult = "needs_attention";
        job.WriteBackStatus = WriteBackStatus.Pending;
        var llm = new FakeLlm(Ok);
        await Make(new FakeRepo(job), llm, new FakeClickUp()).ProcessAsync(job, default);

        Assert.Equal(0, llm.Calls);
        Assert.Equal(WriteBackStatus.Delivered, job.WriteBackStatus);
        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public async Task Secrets_are_redacted_before_reaching_the_llm()
    {
        var job = NewJob(); job.Description = "password=hunter2";
        string? seen = null;
        var llm = new CapturingLlm(m => seen = m);
        await Make(new FakeRepo(job), llm, new FakeClickUp()).ProcessAsync(job, default);
        Assert.NotNull(seen);
        Assert.DoesNotContain("hunter2", seen);
    }

    private class CapturingLlm(Action<string> capture) : ILlmClient
    {
        public string ModelName => "cap";
        public Task<ReviewResult> ReviewAsync(string s, string u, CancellationToken ct)
        {
            capture(u);
            return Task.FromResult(new ReviewResult { OverallResult = "no_issues_found", Summary = "ok" });
        }
    }
}
