namespace Ingestion.Outbox.Worker;

internal sealed class OutboxBackgroundService(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    Microsoft.Extensions.Options.IOptions<OutboxOptions> options,
    ILogger<OutboxBackgroundService> logger) : BackgroundService
{
    private static readonly Action<ILogger, Exception?> PollFailed =
        LoggerMessage.Define(LogLevel.Error, new EventId(6103, "OutboxPollingFailed"),
            "Outbox polling failed; retrying after the configured interval.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // The process has no dependency on Ingestion.Api availability.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                PollFailed(logger, exception);
            }

            try
            {
                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
