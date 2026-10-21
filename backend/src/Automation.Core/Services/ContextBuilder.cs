using Automation.Core.Contracts;
using Automation.Core.Domain;

namespace Automation.Core.Services;

public static class ContextBuilder
{
    public static ReviewContext Build(ReviewJob job, int maxChars)
    {
        var description = SecretRedactor.Redact(job.Description);
        if (description.Length > maxChars) description = description[..maxChars];
        var title = SecretRedactor.Redact(job.Title);
        if (title.Length > 500) title = title[..500];
        return new ReviewContext(job.TaskId, title, description, job.RepositoryUrl, job.PullRequestUrl);
    }
}
