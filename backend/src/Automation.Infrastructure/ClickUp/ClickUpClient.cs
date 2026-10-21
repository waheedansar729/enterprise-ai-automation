using System.Net.Http.Json;
using System.Text.Json;
using Automation.Core.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Automation.Infrastructure.ClickUp;

public class ClickUpOptions
{
    public string BaseUrl { get; set; } = "https://api.clickup.com/api/v2";
    public string ApiToken { get; set; } = "";
    /// <summary>When true (or when no token is set) comments are only logged, not sent.</summary>
    public bool DryRun { get; set; }
    /// <summary>Secret returned by ClickUp when the webhook is created; used to verify X-Signature.</summary>
    public string WebhookSecret { get; set; } = "";
}

public class ClickUpClient(HttpClient http, IOptions<ClickUpOptions> options, ILogger<ClickUpClient> log) : IClickUpClient
{
    private readonly ClickUpOptions _o = options.Value;

    public async Task<int> PostCommentAsync(string taskId, string text, CancellationToken ct)
    {
        if (_o.DryRun || string.IsNullOrWhiteSpace(_o.ApiToken))
        {
            log.LogWarning("ClickUp DRY RUN - comment for task {TaskId} not sent:\n{Text}", taskId, text);
            return 200;
        }

        var url = $"{_o.BaseUrl.TrimEnd('/')}/task/{Uri.EscapeDataString(taskId)}/comment";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(new { comment_text = text, notify_all = false })
        };
        req.Headers.TryAddWithoutValidation("Authorization", _o.ApiToken); // ClickUp personal token: no "Bearer"

        using var resp = await http.SendAsync(req, ct);
        var code = (int)resp.StatusCode;
        if (code == 429 || code >= 500) throw new TransientException($"ClickUp returned {code}.");
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"ClickUp rejected the comment ({code}).");
        return code;
    }

    public async Task<ClickUpTask?> GetTaskAsync(string taskId, CancellationToken ct)
    {
        if (_o.DryRun || string.IsNullOrWhiteSpace(_o.ApiToken)) return null;

        var url = $"{_o.BaseUrl.TrimEnd('/')}/task/{Uri.EscapeDataString(taskId)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Authorization", _o.ApiToken);
        using var resp = await http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            log.LogWarning("ClickUp GET task {TaskId} returned {Code}", taskId, (int)resp.StatusCode);
            return null;
        }

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = doc.RootElement;
        string? Get(string n) => root.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        var description = Get("text_content");
        if (string.IsNullOrWhiteSpace(description)) description = Get("description");
        return new ClickUpTask(Get("name") ?? $"ClickUp task {taskId}", description, Get("url"));
    }
}
