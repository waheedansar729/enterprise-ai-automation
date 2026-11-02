using System.Text.RegularExpressions;

namespace Automation.Core.Services;

/// <summary>A validated https://github.com/{owner}/{repo}/pull/{number} reference.</summary>
public sealed record PullRequestRef(string Owner, string Repo, int Number)
{
    public string FullName => $"{Owner}/{Repo}";

    private static readonly Regex Name = new(@"^[A-Za-z0-9_.\-]+$", RegexOptions.Compiled);

    public static bool TryParse(string? url, out PullRequestRef? pr)
    {
        pr = null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;

        var host = uri.Host.ToLowerInvariant();
        if (host != "github.com" && host != "www.github.com") return false;

        var seg = uri.AbsolutePath.Trim('/').Split('/');
        if (seg.Length < 4 || seg[2] != "pull") return false;
        if (!Name.IsMatch(seg[0]) || !Name.IsMatch(seg[1])) return false;
        if (seg[0].Trim('.').Length == 0 || seg[1].Trim('.').Length == 0) return false; // "." / ".."
        if (!int.TryParse(seg[3], out var number) || number <= 0) return false;

        pr = new PullRequestRef(seg[0], seg[1], number);
        return true;
    }
}