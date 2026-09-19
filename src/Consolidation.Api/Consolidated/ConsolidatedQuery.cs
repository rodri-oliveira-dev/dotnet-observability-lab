using Consolidation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Consolidation.Api.Consolidated;

/// <summary>HTTP read contract; null timestamp denotes a model with no processed events.</summary>
public sealed record ConsolidatedResponse(
    long Count,
    decimal Sum,
    decimal Average,
    DateTimeOffset? LastUpdatedAt);

/// <summary>Reads only the boundary-owned consolidated database, without broker or ingestion dependencies.</summary>
public sealed class ConsolidatedQuery(ConsolidationDbContext database)
{
    /// <summary>Returns the last durable snapshot, or a stable zero-valued response before the first event.</summary>
    public async Task<ConsolidatedResponse> GetAsync(CancellationToken cancellationToken)
    {
        var total = await database.ConsolidatedTotals.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);

        return total is null
            ? new ConsolidatedResponse(0, 0m, 0m, null)
            : new ConsolidatedResponse(total.Count, total.Sum, total.Average, total.LastUpdatedAt);
    }
}
