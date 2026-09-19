using Consolidation.Persistence;
using Consolidation.Worker;
using Contracts;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
namespace Consolidation.Worker.Tests;
public sealed class ConsolidationProcessorTests
{
    [Fact]
    public async Task First_duplicate_concurrent_distinct_and_cacheless_deliveries_are_correct()
    {
        await using var postgres = new PostgreSqlBuilder().Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new ConsolidationDbContext(options))
            await setup.Database.MigrateAsync();
        var id = Guid.NewGuid();
        var first = new ValueReceivedV1(id, Guid.NewGuid(), 12.5m, DateTimeOffset.UtcNow);
        async Task<ConsolidationResult> Handle(ValueReceivedV1 value)
        {
            await using var db = new ConsolidationDbContext(options);
            return await new ConsolidationProcessor(db, TimeProvider.System).ProcessAsync(value, CancellationToken.None);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Handle(first)));
        Assert.Single(results, x => x == ConsolidationResult.Applied);
        Assert.Equal(7, results.Count(x => x == ConsolidationResult.Duplicate));
        Assert.Equal(ConsolidationResult.Applied,
            await Handle(new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 7.5m, DateTimeOffset.UtcNow)));
        await using var verify = new ConsolidationDbContext(options);
        var total = await verify.ConsolidatedTotals.SingleAsync();
        Assert.Equal(2, total.Count);
        Assert.Equal(20m, total.Sum);
        Assert.Equal(10m, total.Average);
        Assert.Equal(2, await verify.InboxMessages.CountAsync());
    }

    [Fact]
    public async Task Rolled_back_aggregate_never_leaves_a_processed_inbox_entry()
    {
        await using var postgres = new PostgreSqlBuilder().Build();
        await postgres.StartAsync();
        var options = new DbContextOptionsBuilder<ConsolidationDbContext>()
            .UseNpgsql(postgres.GetConnectionString()).Options;
        await using (var setup = new ConsolidationDbContext(options))
        {
            await setup.Database.MigrateAsync();
            // Force the second statement to fail after the Inbox insertion.
            await setup.Database.ExecuteSqlRawAsync("DROP TABLE consolidated_totals");
        }
        var message = new ValueReceivedV1(Guid.NewGuid(), Guid.NewGuid(), 25m, DateTimeOffset.UtcNow);
        await using (var failing = new ConsolidationDbContext(options))
            await Assert.ThrowsAnyAsync<Exception>(() =>
                new ConsolidationProcessor(failing, TimeProvider.System).ProcessAsync(message, CancellationToken.None));
        await using var verify = new ConsolidationDbContext(options);
        Assert.False(await verify.InboxMessages.AnyAsync(x => x.MessageId == message.MessageId));
    }
}
