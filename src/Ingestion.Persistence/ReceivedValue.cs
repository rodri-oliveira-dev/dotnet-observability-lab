namespace Ingestion.Persistence;

public sealed class ReceivedValue
{
    public Guid Id { get; set; }
    public required string IdempotencyKey { get; set; }
    public required string RequestFingerprint { get; set; }
    public decimal Value { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
}
