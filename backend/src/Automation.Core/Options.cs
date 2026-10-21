namespace Automation.Core;

public class ReviewOptions
{
    public string TargetStatus { get; set; } = "Under Review";
    public int MaxContextCharacters { get; set; } = 30000;
    public int MaxConcurrentJobs { get; set; } = 4;
    public int MaxAttempts { get; set; } = 3;
    public double WriteBackRetryBaseSeconds { get; set; } = 2;
    public string[] AllowedRepoHosts { get; set; } = { "github.com", "gitlab.com", "bitbucket.org", "dev.azure.com" };
}
