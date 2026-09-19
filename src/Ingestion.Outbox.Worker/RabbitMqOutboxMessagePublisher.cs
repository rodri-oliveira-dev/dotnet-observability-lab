using System.Text;
using Contracts;
using Messaging;
using RabbitMQ.Client;

namespace Ingestion.Outbox.Worker;

internal sealed class RabbitMqOutboxMessagePublisher(IConnection connection) : IOutboxMessagePublisher
{
    public async Task PublishAsync(ValueReceivedV1 message, string json, CancellationToken cancellationToken)
    {
        // With confirmation tracking, BasicPublishAsync completes only after a broker confirm.
        // mandatory=true also surfaces unroutable messages as publication failures.
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);
        await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);

        // Safe, idempotent declarations also allow publication on a fresh broker while
        // the consolidation worker is stopped; no API or consumer process is required.
        await RabbitMqTopology.DeclareAsync(channel, cancellationToken);

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            Type = RabbitMqTopology.ValueReceivedEventType,
            MessageId = message.EventId.ToString("D"),
            CorrelationId = message.CorrelationId
        };
        await channel.BasicPublishAsync(RabbitMqTopology.Exchange,
            RabbitMqTopology.ValueReceivedRoutingKey, mandatory: true,
            basicProperties: properties, body: Encoding.UTF8.GetBytes(json),
            cancellationToken: cancellationToken);
    }
}
