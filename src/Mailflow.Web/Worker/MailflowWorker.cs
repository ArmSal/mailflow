using Mailflow.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace Mailflow.Web.Worker;

public sealed class MailflowWorker(
    IMailboxRunner runner,
    RunNowSignal signal,
    WorkerState state,
    IClock clock,
    IOptions<WorkerOptions> options,
    ILogger<MailflowWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.Interval;
        using var timer = new PeriodicTimer(interval);

        // First scheduled run happens immediately at startup.
        Task<bool> tick = Task.FromResult(true);
        Task runNow = signal.WaitAsync(stoppingToken);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                state.SetNextRun(clock.UtcNow.Add(interval));
                var winner = await Task.WhenAny(tick, runNow);
                stoppingToken.ThrowIfCancellationRequested();

                if (winner == runNow)
                {
                    runNow = signal.WaitAsync(stoppingToken);
                    await RunOnceAsync(stoppingToken, onlyDue: false); // explicit request: all mailboxes, allowed while paused
                    continue;
                }

                tick = timer.WaitForNextTickAsync(stoppingToken).AsTask();
                if (state.IsPaused)
                {
                    logger.LogDebug("Worker paused, scheduled run skipped");
                    continue;
                }
                await RunOnceAsync(stoppingToken, onlyDue: true);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // normal shutdown
        }
    }

    private async Task RunOnceAsync(CancellationToken ct, bool onlyDue)
    {
        string? error = null;
        state.SetRunning(true);
        try
        {
            await runner.RunAsync(ct, onlyDue);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            logger.LogError(ex, "Mailbox run failed; worker keeps running");
        }
        finally
        {
            state.SetRunning(false);
            state.SetLastRun(clock.UtcNow, error);
        }
    }
}
