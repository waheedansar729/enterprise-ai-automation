// using System.Net.Http.Headers;
// using System.Text;
// using System.Text.Json;
// using Automation.Core.Contracts;
// using Microsoft.Extensions.Configuration;

// namespace Automation.Infrastructure.GitHub
// {
//     // 👈 IGitHubClient add kiya taake ReviewProcessor isay use kar sake
//     public class GitHubService : IGitHubClient 
//     {
//         private readonly HttpClient _httpClient;
//         private readonly string _token;

//         public GitHubService(HttpClient httpClient, IConfiguration config)
//         {
//             _httpClient = httpClient;
//             _token = config["GitHub:Token"] ?? ""; 
//             _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Reviewer");
            
//             if (!string.IsNullOrEmpty(_token))
//                 _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
//         }

//         // 👈 CancellationToken add kiya
//         public async Task<string> GetPullRequestDiffAsync(string repoFullName, int prNumber, CancellationToken ct)
//         {
//             var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repoFullName}/pulls/{prNumber}");
//             request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
            
//             var response = await _httpClient.SendAsync(request, ct); // ct pass kiya
//             response.EnsureSuccessStatusCode();
//             return await response.Content.ReadAsStringAsync(ct); // ct pass kiya
//         }

//         // 👈 CancellationToken add kiya aur return type int kar diya
//         public async Task<int> PostCommentAsync(string repoFullName, int prNumber, string text, CancellationToken ct)
//         {
//             var payload = new { body = text };
//             var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            
//             var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repoFullName}/issues/{prNumber}/comments")
//             {
//                 Content = content
//             };
            
//             var response = await _httpClient.SendAsync(request, ct); // ct pass kiya
//             response.EnsureSuccessStatusCode();
            
//             return (int)response.StatusCode; // Status code return kiya
//         }
//     }
// }
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Automation.Core.Contracts;
using Microsoft.Extensions.Configuration;

namespace Automation.Infrastructure.GitHub;

public class GitHubService : IGitHubClient
{
    private readonly HttpClient _http;

    public GitHubService(HttpClient http, IConfiguration config)
    {
        _http = http;
        var token = config["GitHub:Token"] ?? "";
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Reviewer");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<string> GetPullRequestDiffAsync(string repoFullName, int prNumber, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repoFullName}/pulls/{prNumber}");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
        using var resp = await SendAsync(req, "read the pull request diff", ct);
        return await resp.Content.ReadAsStringAsync(ct);
    }

    public async Task<int> PostCommentAsync(string repoFullName, int prNumber, string text, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(new { body = text });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repoFullName}/issues/{prNumber}/comments")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        using var resp = await SendAsync(req, "post the comment", ct);
        return (int)resp.StatusCode;
    }

    /// <summary>Sends the request. Rate limits, 5xx and network errors become TransientException (retried);
    /// other failures (404, 401, 403 without rate limit) are permanent and say what to check.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, string action, CancellationToken ct)
    {
        HttpResponseMessage resp;
        try { resp = await _http.SendAsync(req, ct); }
        catch (HttpRequestException ex) { throw new TransientException($"GitHub: could not {action} (network error: {ex.Message})."); }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new TransientException($"GitHub: timed out while trying to {action}."); }

        if (resp.IsSuccessStatusCode) return resp;

        var code = (int)resp.StatusCode;
        var remaining = resp.Headers.TryGetValues("X-RateLimit-Remaining", out var v) ? v.FirstOrDefault() : null;
        var body = await resp.Content.ReadAsStringAsync(ct);
        resp.Dispose();

        if (code == 429 || code >= 500 || (code == 403 && remaining == "0"))
            throw new TransientException($"GitHub: could not {action} ({code}, rate limit or server error).");

        var detail = body.Length > 200 ? body[..200] : body;
        throw new InvalidOperationException(
            $"GitHub: could not {action} ({code}). Check that GitHub__Token can access this repository " +
            $"(Pull requests: read/write, Contents: read). {detail}");
    }
}