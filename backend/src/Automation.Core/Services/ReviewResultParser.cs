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

    /// <summary>Extracts and deserializes the JSON object from the model text (tolerates ``` fences).</summary>
    public static ReviewResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidModelOutputException("Model returned an empty response.");

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new InvalidModelOutputException("Model response did not contain a JSON object.");

        try
        {
            var result = JsonSerializer.Deserialize<ReviewResult>(text[start..(end + 1)], Options);
            return result ?? throw new InvalidModelOutputException("Model JSON was null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidModelOutputException("Model JSON could not be parsed: " + ex.Message);
        }
    }
}
