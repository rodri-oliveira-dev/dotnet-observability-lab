namespace Ingestion.Outbox.Worker;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public int BatchSize { get; set; } = 10;
}
