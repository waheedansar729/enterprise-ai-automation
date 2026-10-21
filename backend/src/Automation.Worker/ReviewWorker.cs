using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Automation.Core;
using Automation.Core.Contracts;
using Microsoft.Extensions.Options;

namespace Automation.Worker;

/// <summary>Runs N parallel loops that claim queued jobs from the database and process them.</summary>
public class ReviewWorker(IServiceScopeFactory scopes, IOptions<ReviewOptions> options, ILogger<ReviewWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var n = Math.Max(1, options.Value.MaxConcurrentJobs);
        log.LogInformation("Worker started with {Count} parallel loop(s)", n);
        var loops = Enumerable.Range(0, n).Select(_ => LoopAsync(stop)).Append(RecoveryLoopAsync(stop));
        await Task.WhenAll(loops);
    }

    private async Task LoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<IReviewRepository>();
                var job = await repo.ClaimNextAsync(stop);
                if (job is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stop);
                    continue;
                }
                await scope.ServiceProvider.GetRequiredService<IReviewProcessor>().ProcessAsync(job, stop);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                log.LogError(ex, "Worker loop error; retrying in 5s");
                try { await Task.Delay(TimeSpan.FromSeconds(5), stop); } catch (OperationCanceledException) { break; }
            }
        }
    }

    /// <summary>Jobs stuck in "Running" (e.g. worker crashed) go back to the queue after 10 minutes.</summary>
    private async Task RecoveryLoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(60), stop);
                using var scope = scopes.CreateScope();
                var n = await scope.ServiceProvider.GetRequiredService<IReviewRepository>()
                    .RequeueStaleAsync(TimeSpan.FromMinutes(10), stop);
                if (n > 0) log.LogWarning("Requeued {Count} stale job(s)", n);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception ex) { log.LogError(ex, "Recovery loop error"); }
        }
    }
}
