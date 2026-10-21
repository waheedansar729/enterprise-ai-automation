using System.Net.Http.Json;
using System.Text.Json;
using Automation.Core.Contracts;
using Automation.Core.Services;
using Microsoft.Extensions.Options;

namespace Automation.Infrastructure.Llm;

/// <summary>Groq (OpenAI-compatible chat completions). Works for any OpenAI-style endpoint via Llm__BaseUrl.</summary>
public class GroqLlmClient(HttpClient http, IOptions<LlmOptions> options) : ILlmClient
{
    private readonly LlmOptions _o = options.Value;
    public string ModelName => _o.Model;

    public async Task<ReviewResult> ReviewAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _o.Model,
            ["temperature"] = 0,
            ["max_tokens"] = _o.MaxTokens,
            ["response_format"] = new { type = "json_object" },
            ["messages"] = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/openai/v1/chat/completions") { Content = JsonContent.Create(body) };
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _o.ApiKey);

        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new TransientException("Groq network error: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new TransientException("Groq request timed out."); }

        using (resp)
        {
            var raw = await resp.Content.ReadAsStringAsync(ct);
            var code = (int)resp.StatusCode;
            if (code == 429 || code >= 500) throw new TransientException($"Groq returned {code} (rate limit or server error).");
            if (code == 400 && raw.Contains("json_validate_failed")) throw new InvalidModelOutputException("Groq could not produce valid JSON.");
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Groq rejected the request ({code}): {(raw.Length > 300 ? raw[..300] : raw)}");

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var text = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                return ReviewResultParser.Parse(text);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new InvalidModelOutputException("Unexpected Groq response shape: " + ex.Message);
            }
        }
    }
}
