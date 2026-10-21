namespace Automation.Core.Services;

public static class Backoff
{
    /// <summary>Exponential backoff with +/-20% jitter. attempt starts at 1.</summary>
    public static TimeSpan Delay(int attempt, double baseSeconds)
    {
        if (baseSeconds <= 0) return TimeSpan.Zero;
        var seconds = baseSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        var jitter = 0.8 + Random.Shared.NextDouble() * 0.4;
        return TimeSpan.FromSeconds(Math.Min(seconds * jitter, 300));
    }
}
