namespace Consolidation.Persistence;
public sealed class ConsolidatedTotal
{
    public int Id { get; set; } = 1;
    public long Count { get; set; }
    public decimal Sum { get; set; }
    public decimal Average => Count == 0 ? 0 : Sum / Count;
    public DateTimeOffset LastUpdatedAt { get; set; }
}
