namespace Hartonomous.Worker;

public sealed partial class Worker(ILogger<Worker> logger) : BackgroundService
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Worker running at: {Time}")]
    private static partial void LogWorkerRunning(ILogger logger, DateTimeOffset time);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Information))
            {
                LogWorkerRunning(logger, DateTimeOffset.Now);
            }
            await Task.Delay(1000, stoppingToken);
        }
    }
}
