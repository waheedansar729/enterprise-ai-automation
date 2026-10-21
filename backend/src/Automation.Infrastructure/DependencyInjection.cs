using Automation.Core;
using Automation.Core.Contracts;
using Automation.Core.Services;
using Automation.Infrastructure.ClickUp;
using Automation.Infrastructure.Llm;
using Automation.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Automation.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAutomationInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<ReviewOptions>(config.GetSection("Review"));
        services.Configure<LlmOptions>(config.GetSection("Llm"));
        services.Configure<ClickUpOptions>(config.GetSection("ClickUp"));

        var cs = config.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs))
            throw new InvalidOperationException("ConnectionStrings__Default is not configured.");
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(cs));

        services.AddScoped<IReviewRepository, EfReviewRepository>();
        services.AddScoped<IReviewProcessor, ReviewProcessor>();
        services.AddHttpClient<IClickUpClient, ClickUpClient>(c => c.Timeout = TimeSpan.FromSeconds(30));

        // Provider defaults (base URL + model) so .env only needs Provider + ApiKey.
        services.PostConfigure<LlmOptions>(o =>
        {
            var groq = o.Provider.Equals("Groq", StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(o.BaseUrl)) o.BaseUrl = groq ? "https://api.groq.com" : "https://api.anthropic.com";
            if (string.IsNullOrWhiteSpace(o.Model)) o.Model = groq ? "llama-3.3-70b-versatile" : "claude-sonnet-5-5";
        });

        var llm = config.GetSection("Llm").Get<LlmOptions>() ?? new LlmOptions();
        var isGroq = llm.Provider.Equals("Groq", StringComparison.OrdinalIgnoreCase);
        var isAnthropic = llm.Provider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase);
        if ((isGroq || isAnthropic) && string.IsNullOrWhiteSpace(llm.ApiKey))
            throw new InvalidOperationException($"Llm__Provider={llm.Provider} requires Llm__ApiKey.");

        void ConfigureHttp(IServiceProvider sp, HttpClient c)
        {
            var o = sp.GetRequiredService<IOptions<LlmOptions>>().Value;
            c.BaseAddress = new Uri(o.BaseUrl);
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds);
        }

        if (isGroq) services.AddHttpClient<ILlmClient, GroqLlmClient>(ConfigureHttp);
        else if (isAnthropic) services.AddHttpClient<ILlmClient, AnthropicLlmClient>(ConfigureHttp);
        else services.AddSingleton<ILlmClient, MockLlmClient>();
        return services;
    }
}
