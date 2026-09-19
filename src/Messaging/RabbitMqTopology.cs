using RabbitMQ.Client;

namespace Messaging;

/// <summary>Transport names and idempotent broker declaration shared by workers, never APIs.</summary>
public static class RabbitMqTopology
{
    public const string Exchange = "lab.events.v1";
    public const string ValueReceivedQueue = "consolidation.value-received.v1";
    public const string ValueReceivedRoutingKey = "value.received.v1";
    public const string ValueReceivedEventType = "ValueReceived.v1";

    public static async Task DeclareAsync(IConnection connection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await DeclareAsync(channel, cancellationToken);
    }

    // Both workers can safely declare the same durable topology even if the other is offline.
    public static async Task DeclareAsync(IChannel channel, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(channel);
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Direct,
            durable: true, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueDeclareAsync(ValueReceivedQueue,
            durable: true, exclusive: false, autoDelete: false, cancellationToken: cancellationToken);
        await channel.QueueBindAsync(ValueReceivedQueue, Exchange, ValueReceivedRoutingKey,
            cancellationToken: cancellationToken);
    }
}
