using RabbitMQ.Client;

namespace Messaging;

/// <summary>
/// Transport-only names and declaration. Shared by workers; never referenced by HTTP APIs.
/// </summary>
public static class RabbitMqTopology
{
    public const string Exchange = "lab.events.v1";
    public const string ValueReceivedQueue = "consolidation.value-received.v1";
    public const string ValueReceivedRoutingKey = "value.received.v1";
    public const string ValueReceivedEventType = "ValueReceived.v1";

    // Idempotent, durable declarations: the consumer boundary owns its queue and binding.
    public static async Task DeclareAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ValueReceivedQueue,
            durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ValueReceivedQueue, Exchange, ValueReceivedRoutingKey,
            cancellationToken: cancellationToken);
    }
}
