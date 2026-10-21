using Automation.Core.Contracts;

namespace Automation.Core.Validation;

public static class RequestValidator
{
    public static Dictionary<string, string[]> Validate(CreateReviewRequest? req, ReviewOptions opt)
    {
        var errors = new Dictionary<string, string[]>();
        if (req is null) { errors["body"] = new[] { "Request body is required." }; return errors; }

        Required(errors, "eventId", req.EventId, 200);
        Required(errors, "taskId", req.TaskId, 200);
        Required(errors, "status", req.Status, 100);
        if (req.Title is { Length: > 500 }) errors["title"] = new[] { "Title is too long (max 500)." };

        if (!string.IsNullOrWhiteSpace(req.TaskUrl) && !IsHttps(req.TaskUrl, out _))
            errors["taskUrl"] = new[] { "taskUrl must be an absolute https URL." };

        CheckRepoUrl(errors, "repositoryUrl", req.RepositoryUrl, opt);
        CheckRepoUrl(errors, "pullRequestUrl", req.PullRequestUrl, opt);
        return errors;
    }

    private static void Required(Dictionary<string, string[]> e, string name, string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) e[name] = new[] { $"{name} is required." };
        else if (value.Length > max) e[name] = new[] { $"{name} is too long (max {max})." };
    }

    private static void CheckRepoUrl(Dictionary<string, string[]> e, string name, string? url, ReviewOptions opt)
    {
        if (string.IsNullOrWhiteSpace(url)) return;
        if (!IsHttps(url, out var uri)) { e[name] = new[] { $"{name} must be an absolute https URL." }; return; }
        var host = uri!.Host.ToLowerInvariant();
        var ok = opt.AllowedRepoHosts.Any(h => host == h || host.EndsWith("." + h));
        if (!ok) e[name] = new[] { $"Host '{host}' is not in the approved repository host list." };
    }

    private static bool IsHttps(string value, out Uri? uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps;
}
