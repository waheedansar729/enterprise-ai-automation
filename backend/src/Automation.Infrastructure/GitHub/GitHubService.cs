using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Automation.Core.Contracts;
using Microsoft.Extensions.Configuration;

namespace Automation.Infrastructure.GitHub
{
    // 👈 IGitHubClient add kiya taake ReviewProcessor isay use kar sake
    public class GitHubService : IGitHubClient 
    {
        private readonly HttpClient _httpClient;
        private readonly string _token;

        public GitHubService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _token = config["GitHub:Token"] ?? ""; 
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AI-Reviewer");
            
            if (!string.IsNullOrEmpty(_token))
                _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        }

        // 👈 CancellationToken add kiya
        public async Task<string> GetPullRequestDiffAsync(string repoFullName, int prNumber, CancellationToken ct)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repoFullName}/pulls/{prNumber}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
            
            var response = await _httpClient.SendAsync(request, ct); // ct pass kiya
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(ct); // ct pass kiya
        }

        // 👈 CancellationToken add kiya aur return type int kar diya
        public async Task<int> PostCommentAsync(string repoFullName, int prNumber, string text, CancellationToken ct)
        {
            var payload = new { body = text };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            
            var request = new HttpRequestMessage(HttpMethod.Post, $"https://api.github.com/repos/{repoFullName}/issues/{prNumber}/comments")
            {
                Content = content
            };
            
            var response = await _httpClient.SendAsync(request, ct); // ct pass kiya
            response.EnsureSuccessStatusCode();
            
            return (int)response.StatusCode; // Status code return kiya
        }
    }
}