using System.Globalization;
using Dfe.Briefing.Tool.Application.Services.Base;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Dfe.Briefing.Tool.Application.Tests.Services;

public sealed class BaseBackgroundDailyJobTests
{
    private static readonly TimeSpan WaitLimit = TimeSpan.FromSeconds(10);

    private readonly CancellationToken cancellationToken = TestContext.Current.CancellationToken;
    private readonly RecordingLogger _logger = new();

    [Theory]
    [InlineData("2026-10-26T00:30:00Z", "01:00", 0.5)]   // later today
    [InlineData("2026-10-26T02:00:00Z", "02:00", 24)]    // exactly now: tomorrow's
    [InlineData("2026-10-26T03:30:00Z", "02:00", 22.5)]  // passed today: tomorrow's
    public void UntilNext_GivesTheTimeToTheNextRun(string now, string at, double hours)
        => Assert.Equal(TimeSpan.FromHours(hours),
            BaseBackgroundDailyJob.UntilNext(TimeOnly.Parse(at, CultureInfo.InvariantCulture), DateTimeOffset.Parse(now, CultureInfo.InvariantCulture)));

    [Fact]
    public async Task Stopping_while_waiting_for_the_next_run_logs_no_failure()
    {
        var job = new TestJob(new FixedClock(DateTimeOffset.UtcNow), RunAt(TimeSpan.FromHours(12)), _logger, _ => Task.CompletedTask);

        await job.StartAsync(cancellationToken);
        await job.StopAsync(cancellationToken);

        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task Stopping_during_a_run_logs_no_failure()
    {
        var started = new TaskCompletionSource();
        var job = new TestJob(SoonClock(), RunAtSoon, _logger, async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
        });

        await job.StartAsync(cancellationToken);
        await started.Task.WaitAsync(WaitLimit, cancellationToken);
        await job.StopAsync(cancellationToken);

        Assert.Empty(_logger.Errors);
    }

    [Fact]
    public async Task A_failed_run_is_logged_and_the_job_keeps_running()
    {
        var runs = 0;
        var ranAgain = new TaskCompletionSource();
        var job = new TestJob(SoonClock(), RunAtSoon, _logger, _ =>
        {
            if (Interlocked.Increment(ref runs) == 1)
                throw new InvalidOperationException("Foundry is down");

            ranAgain.TrySetResult();
            return Task.CompletedTask;
        });

        await job.StartAsync(cancellationToken);
        await ranAgain.Task.WaitAsync(WaitLimit, cancellationToken);
        await job.StopAsync(cancellationToken);

        var error = Assert.Single(_logger.Errors);
        Assert.IsType<InvalidOperationException>(error);
    }

    // The clock always reads a millisecond before RunAtSoon, so each run is due a millisecond after the last.
    private static readonly TimeOnly RunAtSoon = new(2, 0);
    private static FixedClock SoonClock() => new(new DateTimeOffset(2026, 11, 4, 1, 59, 59, 999, TimeSpan.Zero));
    private static TimeOnly RunAt(TimeSpan fromNow) => TimeOnly.FromDateTime(DateTime.UtcNow.Add(fromNow));

    private sealed class TestJob(TimeProvider clock, TimeOnly runAt, ILogger logger, Func<CancellationToken, Task> run)
        : BaseBackgroundDailyJob(clock, logger)
    {
        protected override string Name => "Test job";
        protected override TimeOnly RunAt => runAt;
        internal override Task RunAsync(CancellationToken cancellationToken) => run(cancellationToken);
    }

    // Reads a fixed time, but its timers are real ones.
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class RecordingLogger : ILogger
    {
        private readonly List<Exception?> _errors = [];

        public IReadOnlyList<Exception?> Errors
        {
            get { lock (_errors) return [.. _errors]; }
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel >= LogLevel.Error)
                lock (_errors) _errors.Add(exception);
        }
    }
}
