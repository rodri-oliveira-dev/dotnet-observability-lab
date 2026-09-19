using System.Diagnostics;
using System.Text;
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
    private static readonly ActivitySource Traces = new("Consolidation.Worker");
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await RabbitMqTopology.DeclareAsync(channel, stoppingToken);
        await channel.BasicQosAsync(0, 1, false, stoppingToken);
        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            // Missing/invalid carrier data must never block the business message.
            var parent = ExtractParent(delivery.BasicProperties.Headers);
            using var activity = Traces.StartActivity("rabbitmq process ValueReceived.v1",
                ActivityKind.Consumer, parent);
            activity?.SetTag("messaging.system", "rabbitmq");
            activity?.SetTag("messaging.destination.name", RabbitMqTopology.ValueReceivedQueue);
            activity?.SetTag("messaging.operation.name", "process");
            activity?.SetTag("messaging.message.id", delivery.BasicProperties.MessageId);
            ValueReceivedV1? message;
            try
            {
                message = DeserializeAndValidate(delivery.Body, delivery.BasicProperties.MessageId);
            }
            catch (JsonException ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "Invalid ValueReceived.v1 payload");
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
                activity?.SetStatus(ActivityStatusCode.Error, "Consolidation persistence failed");
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
    /// <summary>Extracts W3C context from RabbitMQ binary headers; absent/malformed data is ignored.</summary>
    internal static ActivityContext ExtractParent(IDictionary<string, object?>? headers)
    {
        if (headers is null ||
            !headers.TryGetValue("traceparent", out var traceParentField) ||
            !TryReadHeader(traceParentField, 256, out var traceParent))
            return default;

        string? traceState = null;
        if (headers.TryGetValue("tracestate", out var traceStateField) &&
            TryReadHeader(traceStateField, 512, out var state))
            traceState = state;

        return ActivityContext.TryParse(traceParent, traceState, out var parent)
            ? parent : default;
    }

    private static bool TryReadHeader(object? field, int maxLength, out string value)
    {
        value = string.Empty;
        try
        {
            value = field switch
            {
                byte[] bytes when bytes.Length <= maxLength => new UTF8Encoding(false, true).GetString(bytes),
                ReadOnlyMemory<byte> bytes when bytes.Length <= maxLength =>
                    new UTF8Encoding(false, true).GetString(bytes.Span),
                string text when text.Length <= maxLength => text,
                _ => string.Empty
            };
            return value.Length > 0;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>Validates the complete wire event before any Inbox write or acknowledgement.</summary>
    internal static ValueReceivedV1 DeserializeAndValidate(ReadOnlyMemory<byte> body, string? amqpMessageId)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Value", out var valueField) ||
            valueField.ValueKind != JsonValueKind.Number ||
            !valueField.TryGetDecimal(out _) ||
            !root.TryGetProperty("OccurredAt", out var occurredAtField) ||
            occurredAtField.ValueKind != JsonValueKind.String)
            throw new JsonException("ValueReceived.v1 requires a numeric Value and OccurredAt.");

        var message = JsonSerializer.Deserialize<ValueReceivedV1>(body.Span);
        if (message is null || message.EventId == Guid.Empty || message.ValueId == Guid.Empty ||
            message.OccurredAt == default ||
            !string.Equals(amqpMessageId, message.EventId.ToString("D"), StringComparison.OrdinalIgnoreCase))
            throw new JsonException("Invalid ValueReceived.v1 identity or timestamp.");
        return message;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation consumer started.")]
    private static partial void Started(ILogger logger);
    [LoggerMessage(Level = LogLevel.Information, Message = "Consolidation message {MessageId} committed.")]
    private static partial void Accepted(ILogger logger, Guid messageId);
    [LoggerMessage(Level = LogLevel.Information, Message = "Duplicate message {MessageId} ignored.")]
    private static partial void Duplicate(ILogger logger, Guid messageId);
    [LoggerMessage(Level = LogLevel.Error, Message = "Malformed message rejected without requeue.")]
    private static partial void InvalidMessage(ILogger logger, Exception exception);
    [LoggerMessage(Level = LogLevel.Error, Message = "Consolidation failed; delivery requeued.")]
    private static partial void ProcessingFailure(ILogger logger, Exception exception);
}
