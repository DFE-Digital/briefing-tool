using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Dfe.Briefing.Tool.Application.Services.Base;

/// <summary>
/// Provides a base class for implementing daily background jobs that run at a specified UTC time.
/// </summary>
/// <param name="timeProvider">The time provider used to determine the current UTC time.</param>
/// <param name="logger">The logger used for job logging.</param>
internal abstract partial class BaseBackgroundDailyJob(TimeProvider timeProvider, ILogger logger) : BackgroundService
{
    /// <summary>
    /// Name of the job, used in logging and diagnostics.
    /// </summary>
    protected abstract string Name { get; }

    /// <summary>
    /// Gets the time of day when the operation should run.
    /// </summary>
    protected abstract TimeOnly RunAt { get; }

    /// <summary>
    /// Executes the asynchronous operation.
    /// </summary>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    internal abstract Task RunAsync(CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (true)
            {
                var delay = UntilNext(RunAt, timeProvider.GetUtcNow());
                await Task.Delay(delay, timeProvider, stoppingToken);
                await RunAsync(stoppingToken);
            }
        }
        catch (Exception ex) when (stoppingToken.IsCancellationRequested)
        {
            LogRunFailed(logger, ex, Name);
        }
    }

    /// <summary>How long from <paramref name="now"/> until the next <paramref name="at"/> (UTC): today's, or tomorrow's if it's passed.</summary>
    internal static TimeSpan UntilNext(TimeOnly at, DateTimeOffset now)
    {
        var today = new DateTimeOffset(DateOnly.FromDateTime(now.UtcDateTime).ToDateTime(at), TimeSpan.Zero);
        return (today > now ? today : today.AddDays(1)) - now;
    }
    [LoggerMessage(Level = LogLevel.Error, Message = "{Job} failed; it runs again tomorrow")]
    private static partial void LogRunFailed(ILogger logger, Exception exception, string job);
}
