namespace Contracts;

// Versioned integration payload. The API stores it in the Outbox; a later worker publishes it.
public sealed record ValueReceivedV1(Guid EventId, Guid ValueId, decimal Value, DateTimeOffset OccurredAt);
