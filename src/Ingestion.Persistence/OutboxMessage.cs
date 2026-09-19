namespace Ingestion.Persistence;

public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public Guid ValueId { get; set; }
    public required string EventType { get; set; }
    public required string Payload { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceParent { get; set; }
    public string? TraceState { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? QuarantinedAt { get; set; }
    public string? QuarantineReason { get; set; }
    public int PublishAttempts { get; set; }
    public DateTimeOffset? NextAttemptAt { get; set; }
}
