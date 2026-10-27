// using System.Net.Http.Json;
// using System.Text.Json;
// using Automation.Core.Contracts;
// using Automation.Core.Services;
// using Microsoft.Extensions.Options;

// namespace Automation.Infrastructure.Llm;

// /// <summary>Groq (OpenAI-compatible chat completions). Works for any OpenAI-style endpoint via Llm__BaseUrl.</summary>
// public class GroqLlmClient(HttpClient http, IOptions<LlmOptions> options) : ILlmClient
// {
//     private readonly LlmOptions _o = options.Value;
//     public string ModelName => _o.Model;

//     public async Task<ReviewResult> ReviewAsync(string systemPrompt, string userMessage, CancellationToken ct)
//     {
//         var body = new Dictionary<string, object>
//         {
//             ["model"] = _o.Model,
//             ["temperature"] = 0,
//             ["max_tokens"] = _o.MaxTokens,
//             ["response_format"] = new { type = "json_object" },
//             ["messages"] = new object[]
//             {
//                 new { role = "system", content = systemPrompt },
//                 new { role = "user", content = userMessage }
//             }
//         };

//         using var req = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions") { Content = JsonContent.Create(body) };
//         req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _o.ApiKey);

//         HttpResponseMessage resp;
//         try { resp = await http.SendAsync(req, ct); }
//         catch (HttpRequestException ex) { throw new TransientException("Groq network error: " + ex.Message); }
//         catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new TransientException("Groq request timed out."); }

//         using (resp)
//         {
//             var raw = await resp.Content.ReadAsStringAsync(ct);
//             var code = (int)resp.StatusCode;
//             if (code == 429 || code >= 500) throw new TransientException($"Groq returned {code} (rate limit or server error).");
//             if (code == 400 && raw.Contains("json_validate_failed")) throw new InvalidModelOutputException("Groq could not produce valid JSON.");
//             if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Groq rejected the request ({code}): {(raw.Length > 300 ? raw[..300] : raw)}");

//             try
//             {
//                 using var doc = JsonDocument.Parse(raw);
//                 var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
//                 return ReviewResultParser.Parse(text);
//             }
//             catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
//             {
//                 throw new InvalidModelOutputException("Unexpected Groq response shape: " + ex.Message);
//             }
//         }
//     }
// }
using System.Net.Http.Json;
using System.Text.Json;
using Automation.Core.Contracts;
using Automation.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Automation.Infrastructure.Llm;

/// <summary>Groq (OpenAI-compatible chat completions). gpt-oss models are forced into our exact JSON schema.</summary>
public class GroqLlmClient(HttpClient http, IOptions<LlmOptions> options, ILogger<GroqLlmClient> log) : ILlmClient
{
    private readonly LlmOptions _o = options.Value;
    public string ModelName => _o.Model;

    // Structured outputs (json_schema, strict) are supported on the gpt-oss models. Others use plain JSON mode.
    private bool UseSchema => _o.Model.Contains("gpt-oss", StringComparison.OrdinalIgnoreCase);

    public async Task<ReviewResult> ReviewAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        object format = UseSchema ? SchemaFormat() : new { type = "json_object" };
        var (code, raw) = await PostAsync(systemPrompt, userMessage, format, ct);

        // json_schema not accepted for this model/account: fall back to plain JSON mode once.
        if (code == 400 && UseSchema && !raw.Contains("json_validate_failed")
            && (raw.Contains("response_format", StringComparison.OrdinalIgnoreCase) || raw.Contains("schema", StringComparison.OrdinalIgnoreCase)))
        {
            log.LogWarning("Groq rejected json_schema, falling back to json_object: {Raw}", Cut(raw, 400));
            (code, raw) = await PostAsync(systemPrompt, userMessage, new { type = "json_object" }, ct);
        }

        if (code == 429 || code >= 500) throw new TransientException($"Groq returned {code} (rate limit or server error).");
        if (code == 400 && raw.Contains("json_validate_failed")) throw new InvalidModelOutputException("Groq could not produce valid JSON.");
        if (code < 200 || code >= 300) throw new InvalidOperationException($"Groq rejected the request ({code}): {Cut(raw, 300)}");

        string? text;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
        {
            throw new InvalidModelOutputException("Unexpected Groq response shape: " + ex.Message);
        }

        var result = ReviewResultParser.Parse(text);
        if (string.IsNullOrWhiteSpace(result.OverallResult))
            log.LogWarning("Groq answer has no overallResult. Raw answer (first 800 chars): {Text}", Cut(text ?? "", 800));
        return result;
    }

    private async Task<(int Code, string Raw)> PostAsync(string systemPrompt, string userMessage, object responseFormat, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _o.Model,
            ["temperature"] = 0,
            ["max_tokens"] = _o.MaxTokens,
            ["response_format"] = responseFormat,
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };
        // gpt-oss models are reasoning models: keep reasoning short so the JSON answer is not cut off by max_tokens.
        if (UseSchema) body["reasoning_effort"] = "low";

        using var req = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions") { Content = JsonContent.Create(body) };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _o.ApiKey);

        try
        {
            using var resp = await http.SendAsync(req, ct);
            return ((int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (HttpRequestException ex) { throw new TransientException("Groq network error: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new TransientException("Groq request timed out."); }
    }

    /// <summary>JSON schema matching ReviewResult. Strict mode needs every property required and no extras.</summary>
    public static object SchemaFormat()
    {
        static Dictionary<string, object> Str() => new() { ["type"] = "string" };
        static Dictionary<string, object> OneOf(params string[] values) => new() { ["type"] = "string", ["enum"] = values };

        var finding = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["severity"] = OneOf(Severity.Allowed),
                ["category"] = Str(),
                ["title"] = Str(),
                ["evidence"] = Str(),
                ["recommendation"] = Str(),
                ["confidence"] = OneOf(Severity.AllowedConfidence)
            },
            ["required"] = new[] { "severity", "category", "title", "evidence", "recommendation", "confidence" },
            ["additionalProperties"] = false
        };

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>
            {
                ["overallResult"] = OneOf(Severity.AllowedOverall),
                ["summary"] = Str(),
                ["findings"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = finding },
                ["limitations"] = new Dictionary<string, object> { ["type"] = "array", ["items"] = Str() }
            },
            ["required"] = new[] { "overallResult", "summary", "findings", "limitations" },
            ["additionalProperties"] = false
        };

        return new Dictionary<string, object>
        {
            ["type"] = "json_schema",
            ["json_schema"] = new Dictionary<string, object> { ["name"] = "engineering_review", ["strict"] = true, ["schema"] = schema }
        };
    }

    private static string Cut(string s, int n) => s.Length > n ? s[..n] : s;
}