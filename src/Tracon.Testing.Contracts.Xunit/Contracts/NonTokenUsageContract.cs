namespace Tracon.Testing.Contracts.Storage;

/// <summary>
/// Behavior tests for the <see cref="IRunStore"/> non-token usage contract: the items a
/// workflow function node reports.
/// </summary>
/// <remarks>
/// <para>
/// Every implementation must pass the same scenarios. The items hang off a run, so
/// isolation is established through the run's tenant, the same way a tool invocation's is.
/// </para>
/// <para>
/// The run's <em>total</em> (<see cref="RunRecord.NonTokenCost"/>) is a different
/// surface: it is a snapshot written with <see cref="IRunStore.CompleteRunAsync"/> and
/// <see cref="RunStoreContract"/> covers it.
/// </para>
/// </remarks>
public abstract class NonTokenUsageContract : TenantIsolationContract<IRunStore>
{
    // Every run any test of this instance seeded, whoever owns it: CountAsync reads them ALL
    // through the asking tenant's eyes, which is what makes "tenant B sees none of A's" mean
    // something.
    private readonly List<Guid> _seededRunIds = [];

    // The run the cancellation write records its item against.
    private readonly Guid _cancellationRunId = TraconId.NewId();

    /// <inheritdoc />
    protected override async ValueTask<object> SeedAsync(string tenantId, string name, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;

        var runId = TraconId.NewId();
        _seededRunIds.Add(runId);

        await Store.StartRunAsync(TestData.Run(runId), cancellationToken);
        await Store.RecordNonTokenUsageAsync(Item(runId, name), cancellationToken);

        return runId;
    }

    /// <inheritdoc />
    protected override async ValueTask<bool> ExistsAsync(string tenantId, object key)
    {
        AmbientTenant.TenantId = tenantId;

        return (await Store.ListNonTokenUsageAsync((Guid)key)).Count > 0;
    }

    /// <inheritdoc />
    protected override async ValueTask<int> CountAsync(string tenantId, CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = tenantId;

        var count = 0;

        foreach (var runId in _seededRunIds)
        {
            count += (await Store.ListNonTokenUsageAsync(runId, cancellationToken)).Count;
        }

        return count;
    }

    /// <inheritdoc />
    /// <remarks>
    /// This contract has no listing that needs no key: <see cref="CountAsync"/> reads only the
    /// runs it seeded and so makes no store call on an untouched store. The read is made here.
    /// </remarks>
    protected override async ValueTask CancellableReadAsync(CancellationToken cancellationToken)
        => await Store.ListNonTokenUsageAsync(TraconId.NewId(), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// The run this contract's subject hangs off is opened first, with a live token: the
    /// surface under test is the item, not the run.
    /// </remarks>
    protected override async ValueTask CancellableWriteAsync(CancellationToken cancellationToken)
    {
        AmbientTenant.TenantId = TenantA;
        _seededRunIds.Add(_cancellationRunId);

        await Store.StartRunAsync(TestData.Run(_cancellationRunId), CancellationToken.None);
        await Store.RecordNonTokenUsageAsync(Item(_cancellationRunId, "cancelled"), cancellationToken);
    }

    [Fact]
    public async Task Item_fields_round_trip()
    {
        var runId = TraconId.NewId();
        var createdAt = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordNonTokenUsageAsync(new NonTokenUsageRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            NodeName = "narrate",
            CreatedAt = createdAt,
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Characters,

                // 🚨 The fractional part must NOT be truncated: SQL Server reads a decimal
                // parameter with no explicit precision as decimal(18,0).
                Quantity = 1234.5678901m,
                Cost = 0.0001357m,
                Currency = "USD",
                IsEstimated = true,
                Provider = "elevenlabs",
                Model = "eleven_multilingual_v2",
            },
        });

        var stored = (await Store.ListNonTokenUsageAsync(runId)).ShouldHaveSingleItem();

        string.Equals(stored.NodeName, "narrate", StringComparison.Ordinal).ShouldBeTrue();
        stored.RunId.ShouldBe(runId);
        string.Equals(stored.Usage.Unit, ToolUsageUnits.Characters, StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.Quantity.ShouldBe(1234.5678901m);
        stored.Usage.Cost.ShouldBe(0.0001357m);
        string.Equals(stored.Usage.Currency, "USD", StringComparison.Ordinal).ShouldBeTrue();
        stored.Usage.IsEstimated.ShouldBeTrue();
        string.Equals(stored.Usage.Provider, "elevenlabs", StringComparison.Ordinal).ShouldBeTrue();
        string.Equals(stored.Usage.Model, "eleven_multilingual_v2", StringComparison.Ordinal).ShouldBeTrue();
        stored.CreatedAt.ShouldBe(createdAt, tolerance: TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task An_item_with_no_amount_comes_back_with_no_amount_not_zero()
    {
        var runId = TraconId.NewId();

        await Store.StartRunAsync(TestData.Run(runId));

        await Store.RecordNonTokenUsageAsync(new NonTokenUsageRecord
        {
            Id = TraconId.NewId(),
            RunId = runId,
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new ToolCallUsage { Unit = ToolUsageUnits.Seconds, Quantity = 42m },
        });

        var stored = (await Store.ListNonTokenUsageAsync(runId)).ShouldHaveSingleItem();

        // Unknown, not free: a zero would claim the render cost nothing.
        stored.Usage.Cost.ShouldBeNull();
        stored.Usage.Currency.ShouldBeNull();
        stored.Usage.Provider.ShouldBeNull();
        stored.Usage.Model.ShouldBeNull();
        stored.NodeName.ShouldBeNull();
        stored.Usage.IsEstimated.ShouldBeFalse();
    }

    [Fact]
    public async Task Items_come_back_in_the_order_they_were_reported()
    {
        var runId = TraconId.NewId();
        var start = DateTimeOffset.UtcNow;

        await Store.StartRunAsync(TestData.Run(runId));

        for (var index = 0; index < 5; index++)
        {
            await Store.RecordNonTokenUsageAsync(new NonTokenUsageRecord
            {
                Id = TraconId.NewId(),
                RunId = runId,
                NodeName = $"step-{index}",
                CreatedAt = start.AddMilliseconds(index * 10),
                Usage = new ToolCallUsage { Unit = ToolUsageUnits.Characters, Quantity = index },
            });
        }

        var names = (await Store.ListNonTokenUsageAsync(runId)).Select(static item => item.NodeName).ToArray();

        names.ShouldBe(["step-0", "step-1", "step-2", "step-3", "step-4"]);
    }

    [Fact]
    public async Task A_run_lists_its_own_items_and_not_its_childrens()
    {
        var rootId = TraconId.NewId();
        var childId = TraconId.NewId();

        await Store.StartRunAsync(TestData.Run(rootId));
        await Store.StartRunAsync(TestData.Run(childId) with { ParentRunId = rootId, RootRunId = rootId, Depth = 1 });

        await Store.RecordNonTokenUsageAsync(Item(rootId, "root-node"));
        await Store.RecordNonTokenUsageAsync(Item(childId, "child-node"));

        var items = await Store.ListNonTokenUsageAsync(rootId);

        string.Equals(items.ShouldHaveSingleItem().NodeName, "root-node", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public async Task A_run_that_reported_nothing_or_does_not_exist_lists_nothing()
    {
        var runId = TraconId.NewId();

        await Store.StartRunAsync(TestData.Run(runId));

        (await Store.ListNonTokenUsageAsync(runId)).ShouldBeEmpty();
        (await Store.ListNonTokenUsageAsync(TraconId.NewId())).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_for_a_run_that_was_never_opened_leaves_nothing()
    {
        var runId = TraconId.NewId();

        // A store may refuse it (a persistent store does: the run is the item's owner) or
        // drop it (the in-memory store cannot tell a never-opened run from one it trimmed).
        // Neither may leave the item behind.
        try
        {
            await Store.RecordNonTokenUsageAsync(Item(runId, "orphan"));
        }
        catch (TraconException)
        {
            // Refused: the other allowed outcome.
        }

        (await Store.ListNonTokenUsageAsync(runId)).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_item_stamped_with_another_tenant_is_refused_and_leaves_nothing()
    {
        var runId = TraconId.NewId();

        AmbientTenant.TenantId = TenantA;
        await Store.StartRunAsync(TestData.Run(runId));

        // The EXPECTED tenant is the write's guard: the ambient tenant is not consulted.
        await Should.ThrowAsync<TraconException>(
            async () => await Store.RecordNonTokenUsageAsync(Item(runId, "intruder") with { TenantId = TenantB }));

        (await Store.ListNonTokenUsageAsync(runId)).ShouldBeEmpty();

        // The owning tenant's own stamp is accepted.
        await Store.RecordNonTokenUsageAsync(Item(runId, "owner") with { TenantId = TenantA });

        (await Store.ListNonTokenUsageAsync(runId)).ShouldHaveSingleItem();
    }

    private static NonTokenUsageRecord Item(Guid runId, string nodeName)
        => new()
        {
            Id = TraconId.NewId(),
            RunId = runId,
            NodeName = nodeName,
            CreatedAt = DateTimeOffset.UtcNow,
            Usage = new ToolCallUsage
            {
                Unit = ToolUsageUnits.Characters,
                Quantity = 120m,
                Cost = 0.0132m,
                Currency = "USD",
            },
        };
}
