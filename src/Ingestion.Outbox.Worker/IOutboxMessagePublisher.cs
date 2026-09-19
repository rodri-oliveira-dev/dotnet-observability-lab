using Contracts;

namespace Ingestion.Outbox.Worker;

// The worker owns the durable state; the adapter owns RabbitMQ details.
public interface IOutboxMessagePublisher
{
    Task PublishAsync(ValueReceivedV1 message, string json, CancellationToken cancellationToken);
}
