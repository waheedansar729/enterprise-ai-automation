// using System.Text.Json;
// using Automation.Core.Contracts;

// namespace Automation.Core.Services;

// public static class ReviewResultParser
// {
//     private static readonly JsonSerializerOptions Options = new()
//     {
//         PropertyNameCaseInsensitive = true,
//         PropertyNamingPolicy = JsonNamingPolicy.CamelCase
//     };

//     /// <summary>Extracts and deserializes the JSON object from the model text (tolerates ``` fences).</summary>
//     public static ReviewResult Parse(string? text)
//     {
//         if (string.IsNullOrWhiteSpace(text))
//             throw new InvalidModelOutputException("Model returned an empty response.");

//         var start = text.IndexOf('{');
//         var end = text.LastIndexOf('}');
//         if (start < 0 || end <= start)
//             throw new InvalidModelOutputException("Model response did not contain a JSON object.");

//         try
//         {
//             var result = JsonSerializer.Deserialize<ReviewResult>(text[start..(end + 1)], Options);
//             return result ?? throw new InvalidModelOutputException("Model JSON was null.");
//         }
//         catch (JsonException ex)
//         {
//             throw new InvalidModelOutputException("Model JSON could not be parsed: " + ex.Message);
//         }
//     }
// }
using System.Text.Json;
using Automation.Core.Contracts;

namespace Automation.Core.Services;

public static class ReviewResultParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Names some models use instead of "overallResult".
    private static readonly string[] OverallAliases = { "overall_result", "overall", "result", "verdict", "status" };

    /// <summary>Extracts and deserializes the JSON object from the model text (tolerates ``` fences).</summary>
    public static ReviewResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidModelOutputException("Model returned an empty response.");

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidModelOutputException("Model response did not contain a JSON object. Raw (first 600 chars): " + Snippet(text));

        var json = text[start..(end + 1)];
        ReviewResult result;
        try
        {
            result = JsonSerializer.Deserialize<ReviewResult>(json, Options)
                     ?? throw new InvalidModelOutputException("Model JSON was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidModelOutputException($"Model JSON could not be parsed: {ex.Message} Raw (first 600 chars): {Snippet(json)}");
        }

        if (string.IsNullOrWhiteSpace(result.OverallResult)) TryReadAlias(json, result);

        // Nothing we expect is present at all: the model ignored the schema. Show what it sent.
        if (string.IsNullOrWhiteSpace(result.OverallResult) && string.IsNullOrWhiteSpace(result.Summary) && (result.Findings?.Count ?? 0) == 0)
            throw new InvalidModelOutputException("Model JSON has none of the expected fields. Raw (first 600 chars): " + Snippet(json));

        return result;
    }

    private static void TryReadAlias(string json, ReviewResult result)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return;
            foreach (var key in OverallAliases)
            {
                if (doc.RootElement.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String)
                {
                    result.OverallResult = v.GetString() ?? "";
                    return;
                }
            }
        }
        catch (JsonException) { /* already parsed once; ignore */ }
    }

    private static string Snippet(string s) => s.Length > 600 ? s[..600] : s;
}