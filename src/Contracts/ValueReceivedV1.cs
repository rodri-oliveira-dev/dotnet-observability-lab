using System.Text.Json.Serialization;

namespace Contracts;

// Versioned integration payload. EventId is the persisted Outbox identity in v1.
// The future RabbitMQ adapter maps it to AMQP MessageId without changing the v1 JSON.
public sealed record ValueReceivedV1(
    Guid EventId,
    Guid ValueId,
    decimal Value,
    DateTimeOffset OccurredAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CorrelationId = null)
{
    [JsonIgnore]
    public Guid MessageId => EventId;
}
