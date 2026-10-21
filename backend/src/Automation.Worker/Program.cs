// using Microsoft.Extensions.DependencyInjection;
// using Microsoft.Extensions.Hosting;
// using Automation.Infrastructure;
// using Automation.Worker;

// var builder = Host.CreateApplicationBuilder(args);
// builder.Services.AddAutomationInfrastructure(builder.Configuration);
// builder.Services.AddHostedService<ReviewWorker>();
// await builder.Build().RunAsync();
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Automation.Infrastructure;
using Automation.Worker;
using Automation.Core.Contracts; // 👈 Interface ke liye

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddAutomationInfrastructure(builder.Configuration);

// 👈 YEH LINE YAHAN ADD KAREIN (RunAsync se pehle)
builder.Services.AddHttpClient<IGitHubClient, Automation.Infrastructure.GitHub.GitHubService>();

builder.Services.AddHostedService<ReviewWorker>();

await builder.Build().RunAsync();