namespace Consolidation.Persistence;
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}
