namespace Tracon.Tests.Common;

/// <summary>
/// What a SQL store answers to writers that save the same new record, with the same identifier and the same name, at the
/// same time, written once for every SQL provider.
/// </summary>
/// <remarks>
/// <para>
/// The upsert of a record that has a name and an identifier arbitrates on the name. PostgreSQL's <c>ON CONFLICT</c> names
/// one index: two statements that both insert a record no row holds yet can pass the check of that index before either has
/// written to it, and the second then fails on the primary key (<c>23505</c>) when the first commits. The caller saved a
/// record whose name was free and was told it conflicted with itself. Sent again, the statement finds the committed row through
/// the name and updates it, which is the answer every serial order of the two writers gives.
/// </para>
/// <para>
/// The HTTP endpoints create a new identifier for every request, so only code that saves with a fixed identifier meets this: a
/// host that seeds the same subscription on two nodes at start-up.
/// </para>
/// </remarks>
public abstract class SqlUpsertSameNewKeyRaceContract
{
    private const string Tenant = "same-new-key";
    private const int Rounds = 150;
    private const int Writers = 8;

    private static readonly DateTimeOffset Created = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Opens the webhook store of one migrated, empty schema.</summary>
    /// <returns>The store.</returns>
    protected abstract ValueTask<IWebhookStore> OpenAsync();

    /// <summary>Writers that save one new subscription at the same time all succeed and leave one row.</summary>
    /// <returns>A task that completes when the rounds are over.</returns>
    [Fact]
    public async Task Writers_that_save_one_new_subscription_with_one_identifier_at_the_same_time_all_succeed()
    {
        var store = await OpenAsync();
        var failures = new List<string>();

        for (var round = 0; round < Rounds; round++)
        {
            var subscription = Subscription(Guid.NewGuid(), "seeded-" + round);
            var gate = new TaskCompletionSource();
            var writers = Enumerable.Range(0, Writers)
                .Select(index => Task.Run(
                    async () =>
                    {
                        await gate.Task;

                        try
                        {
                            return await store.SaveSubscriptionAsync(subscription with { Url = "https://example.com/" + index }, CancellationToken.None);
                        }
                        catch (Exception exception)
                        {
                            lock (failures)
                            {
                                failures.Add($"round {round}, writer {index}: {exception.GetType().Name}: {exception.Message.ReplaceLineEndings(" ")}");
                            }

                            return null;
                        }
                    }))
                .ToList();

            gate.SetResult();

            await Task.WhenAll(writers);

            var rows = (await store.ListSubscriptionsAsync(Tenant, TestContext.Current.CancellationToken))
                .Count(row => string.Equals(row.Name, subscription.Name, StringComparison.Ordinal));

            rows.ShouldBe(1, $"round {round}: {rows} subscriptions hold one name");
        }

        failures.ShouldBeEmpty(
            "a writer that saved a name no row held was told the record conflicted with itself (the primary key lost the race the name index won)");
    }

    private static WebhookSubscription Subscription(Guid id, string name)
        => new()
        {
            Id = id,
            TenantId = Tenant,
            Name = name,
            Url = "https://example.com/hook",
            Events = ["run.completed"],
            Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            Enabled = true,
            CreatedAt = Created,
            UpdatedAt = Created,
        };
}
