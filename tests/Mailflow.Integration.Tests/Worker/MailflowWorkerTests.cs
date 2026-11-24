using Mailflow.Application.Abstractions;
using Mailflow.Web.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Mailflow.Integration.Tests.Worker;

public class MailflowWorkerTests
{
    private sealed class FakeRunner : IMailboxRunner
    {
        private int _calls;
        public Func<int, CancellationToken, Task>? Behaviour { get; init; }
        public int Calls => Volatile.Read(ref _calls);
        public async Task RunAsync(CancellationToken ct)
        {
            var n = Interlocked.Increment(ref _calls);
            if (Behaviour is not null) await Behaviour(n, ct);
        }
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private static (MailflowWorker Worker, RunNowSignal Signal, WorkerState State) Create(FakeRunner runner, TimeSpan interval)
    {
        var signal = new RunNowSignal();
        var state = new WorkerState();
        var worker = new MailflowWorker(runner, signal, state, new FixedClock(),
            Options.Create(new WorkerOptions { Interval = interval }), NullLogger<MailflowWorker>.Instance);
        return (worker, signal, state);
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not reached in time");
            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Runs_at_startup_then_on_schedule()
    {
        var runner = new FakeRunner();
        var (worker, _, state) = Create(runner, TimeSpan.FromMilliseconds(50));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => runner.Calls >= 3);
        await worker.StopAsync(CancellationToken.None);
        Assert.NotNull(state.LastRunUtc);
        Assert.Null(state.LastError);
    }

    [Fact]
    public async Task Failing_run_does_not_kill_the_loop()
    {
        var runner = new FakeRunner { Behaviour = (n, _) => n == 1 ? throw new InvalidOperationException("boom") : Task.CompletedTask };
        var (worker, _, state) = Create(runner, TimeSpan.FromMilliseconds(50));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => runner.Calls >= 3);
        await worker.StopAsync(CancellationToken.None);
        Assert.True(runner.Calls >= 3);
    }

    [Fact]
    public async Task Failure_is_recorded_in_state()
    {
        var runner = new FakeRunner { Behaviour = (_, _) => throw new InvalidOperationException("boom") };
        var (worker, _, state) = Create(runner, TimeSpan.FromHours(1));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => state.LastRunUtc is not null);
        await worker.StopAsync(CancellationToken.None);
        Assert.Equal("boom", state.LastError);
    }

    [Fact]
    public async Task RunNow_triggers_a_run_before_the_next_tick()
    {
        var runner = new FakeRunner();
        var (worker, signal, _) = Create(runner, TimeSpan.FromHours(1));
        await worker.StartAsync(CancellationToken.None);
        await WaitUntil(() => runner.Calls == 1);
        signal.Request();
        await WaitUntil(() => runner.Calls == 2);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Paused_skips_scheduled_runs_and_resume_restores_them()
    {
        var runner = new FakeRunner();
        var (worker, _, state) = Create(runner, TimeSpan.FromMilliseconds(50));
        state.Pause();
        await worker.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        Assert.Equal(0, runner.Calls);
        state.Resume();
        await WaitUntil(() => runner.Calls >= 1);
        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cancellation_stops_a_running_run_and_the_worker()
    {
        var started = new TaskCompletionSource();
        var runner = new FakeRunner { Behaviour = async (_, ct) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); } };
        var (worker, _, _) = Create(runner, TimeSpan.FromHours(1));
        await worker.StartAsync(CancellationToken.None);
        await started.Task;
        var stop = worker.StopAsync(CancellationToken.None);
        Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(5000)));
    }
}
