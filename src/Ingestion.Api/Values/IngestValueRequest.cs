namespace Ingestion.Api.Values;

public sealed record IngestValueRequest(decimal? Value);

public sealed record ValueReceipt(Guid Id, decimal Value);

public enum IngestValueState
{
    Created,
    Replayed,
    Conflict
}

public sealed record IngestValueResult(IngestValueState State, ValueReceipt? Receipt);
