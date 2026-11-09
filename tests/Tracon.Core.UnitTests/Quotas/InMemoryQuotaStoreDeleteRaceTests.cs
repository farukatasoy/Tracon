using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Quotas;

/// <summary>
/// A rule that a delete removed stays removed when a save of the same scope runs at the same moment.
/// </summary>
/// <remarks>
/// A save of a scope that already holds a rule replaces that rule under its identifier. It finds the rule first and
/// writes it second. A delete that lands between the two removed the rule and reported <see langword="true"/>, and the
/// save then wrote the rule back under the identifier the delete had just freed, with the creation time of a rule that no
/// longer existed. <see cref="StoreDeleteRace"/> runs the two calls against each other and says why no serial order gives
/// that result.
/// </remarks>
public sealed class InMemoryQuotaStoreDeleteRaceTests
{
    private const string Tenant = "acme";
    private const string Agent = "support";

    private static readonly DateTimeOffset Moment = new(2026, 11, 9, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_rule_whose_delete_returned_true_is_not_listed_again_by_a_save_of_its_scope_running_at_the_same_time()
    {
        var store = new InMemoryQuotaStore();
        var cancellationToken = TestContext.Current.CancellationToken;

        var reappeared = await StoreDeleteRace.RunAsync(
            write: async () => await store.SaveAsync(Rule(Guid.NewGuid()), cancellationToken),
            list: async () => [.. (await store.ListAsync(Tenant, cancellationToken)).Select(static rule => rule.Id)],
            delete: async id => await store.DeleteAsync(Tenant, id, cancellationToken));

        reappeared.ShouldBeEmpty(StoreDeleteRace.Describe(reappeared));
    }

    private static QuotaDefinition Rule(Guid id)
        => new()
        {
            Id = id,
            TenantId = Tenant,
            AgentName = Agent,
            Period = QuotaPeriod.Daily,
            MaxRuns = 10,
            CreatedAt = Moment,
            UpdatedAt = Moment,
        };
}
