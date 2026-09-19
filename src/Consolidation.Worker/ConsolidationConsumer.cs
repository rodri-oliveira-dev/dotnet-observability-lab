using System.Text.Json;
using Contracts;
using Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
namespace Consolidation.Worker;
/// <summary>Delivery metadata, retry and broker acknowledgement stay at the transport boundary.</summary>
internal sealed partial class ConsolidationConsumer(IConnection connection, IServiceScopeFactory scopes,
    ILogger<ConsolidationConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            ValueReceivedV1? message;
            try
            {
                message = JsonSerializer.Deserialize<ValueReceivedV1>(delivery.Body.Span);
                if (message is null || message.EventId == Guid.Empty || message.ValueId == Guid.Empty ||
                    !string.Equals(delivery.BasicProperties.MessageId, message.EventId.ToString("D"), StringComparison.OrdinalIgnoreCase))
                    throw new JsonException("Invalid ValueReceived.v1 identity.");
            }
            catch (JsonException ex)
            {
                InvalidMessage(logger, ex);
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false, cancellationToken: stoppingToken);
                return;
            }
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var processor = scope.ServiceProvider.GetRequiredService<ConsolidationProcessor>();
                var result = await processor.ProcessAsync(message, stoppingToken);
                if (result == ConsolidationResult.Duplicate) Duplicate(logger, message.MessageId);
                else Accepted(logger, message.MessageId);
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                ProcessingFailure(logger, ex);
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
        };
        var tag = await channel.BasicConsumeAsync(RabbitMqTopology.ValueReceivedQueue,
            autoAck: false, consumer: consumer, cancellationToken: stoppingToken);
        Started(logger);
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            if (channel.IsOpen)
                await channel.BasicCancelAsync(tag, noWait: false, cancellationToken: CancellationToken.None);
        }
    }
    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation consumer started.")]
    private static partial void Started(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation message {MessageId} committed.")]
    private static partial void Accepted(ILogger logger, Guid messageId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate message {MessageId} ignored.")]
    private static partial void Duplicate(ILogger logger, Guid messageId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Malformed message dead-lettered.")]
    private static partial void InvalidMessage(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Error, Message = "Consolidation failed; delivery requeued.")]
    private static partial void ProcessingFailure(ILogger logger, Exception exception);
}
