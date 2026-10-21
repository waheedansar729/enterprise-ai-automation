using System.Net.Http.Json;
using System.Text.Json;
using Automation.Core.Contracts;
using Automation.Core.Services;
using Microsoft.Extensions.Options;

namespace Automation.Infrastructure.Llm;

/// <summary>Anthropic Messages API adapter. All provider-specific parsing stays in this class.</summary>
public class AnthropicLlmClient(HttpClient http, IOptions<LlmOptions> options) : ILlmClient
{
    private readonly LlmOptions _o = options.Value;
    public string ModelName => _o.Model;

    public async Task<ReviewResult> ReviewAsync(string systemPrompt, string userMessage, CancellationToken ct)
    {
        var body = new Dictionary<string, object>
        {
            ["model"] = _o.Model,
            ["max_tokens"] = _o.MaxTokens,
            ["temperature"] = 0,
            ["system"] = systemPrompt,
            ["messages"] = new[] { new { role = "user", content = userMessage } }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages") { Content = JsonContent.Create(body) };
        req.Headers.Add("x-api-key", _o.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");

        HttpResponseMessage resp;
        try { resp = await http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new TransientException("LLM network error: " + ex.Message); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new TransientException("LLM request timed out."); }

        using (resp)
        {
            var raw = await resp.Content.ReadAsStringAsync(ct);
            var code = (int)resp.StatusCode;
            if (code == 429 || code >= 500) throw new TransientException($"LLM provider returned {code}.");
            if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"LLM provider rejected the request ({code}): {Trim(raw)}");

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString();
                return ReviewResultParser.Parse(text);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException)
            {
                throw new InvalidModelOutputException("Unexpected LLM response shape: " + ex.Message);
            }
        }
    }

    private static string Trim(string s) => s.Length > 300 ? s[..300] : s;
}
