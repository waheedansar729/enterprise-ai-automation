namespace Automation.Infrastructure.Llm;

public class LlmOptions
{
    /// <summary>"Mock" (no key needed), "Groq" (free tier) or "Anthropic".</summary>
    public string Provider { get; set; } = "Mock";
    public string ApiKey { get; set; } = "";
    /// <summary>Leave empty to use the provider default (set in DependencyInjection).</summary>
    public string Model { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public int MaxTokens { get; set; } = 1500;
    public int TimeoutSeconds { get; set; } = 60;
}
