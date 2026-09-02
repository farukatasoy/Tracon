using AgentPrism.Core.UnitTests.Fakes;

namespace AgentPrism.Core.UnitTests.Recording;

/// <summary>
/// Verifies that <see cref="RunCostRecalculationService"/> only fills in runs
/// whose price is <see cref="PricingSource.Unknown"/> (or unpriced) — a run's
/// cost is a price snapshot and, once known, must never be rewritten
/// (phase 132, F-175).
/// </summary>
public sealed class RunCostRecalculationServiceTests
{
    private const string TenantId = "tenant-a";

    [Fact]
    public async Task A_priced_run_is_skipped_even_when_a_new_price_would_change_it()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext(TenantId));

        var runId = await StartAndCompleteAsync(
            store,
            provider: "openai",
            model: "gpt-x",
            cost: new RunCost { InputCost = 1m, OutputCost = 2m, Currency = "USD", Source = PricingSource.Catalog });

        // A resolver that would compute a DIFFERENT price if it were ever called.
        var resolver = new FakeRunPricingResolver((_, _, _) =>
            new RunCost { InputCost = 999m, OutputCost = 999m, Currency = "USD", Source = PricingSource.Catalog });

        var service = new RunCostRecalculationService(store, resolver);
        var result = await service.RecalculateAsync(TenantId);

        result.RunsSkipped.ShouldBe(1);
        result.RunsConsidered.ShouldBe(0);
        result.RunsUpdated.ShouldBe(0);
        resolver.CallCount.ShouldBe(0);

        var run = await store.GetRunAsync(runId);
        run!.Cost!.InputCost.ShouldBe(1m);
        run.Cost.OutputCost.ShouldBe(2m);
    }

    [Fact]
    public async Task An_unknown_priced_run_is_filled_in_using_its_own_stored_provider()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext(TenantId));

        var runId = await StartAndCompleteAsync(
            store,
            provider: "openai",
            model: "gpt-x",
            cost: new RunCost { Source = PricingSource.Unknown });

        var resolver = new FakeRunPricingResolver((provider, model, _) =>
            string.Equals(provider, "openai", StringComparison.Ordinal) && string.Equals(model, "gpt-x", StringComparison.Ordinal)
                ? new RunCost { InputCost = 5m, OutputCost = 7m, Currency = "USD", Source = PricingSource.Catalog }
                : new RunCost { Source = PricingSource.Unknown });

        var service = new RunCostRecalculationService(store, resolver);
        var result = await service.RecalculateAsync(TenantId);

        result.RunsConsidered.ShouldBe(1);
        result.RunsUpdated.ShouldBe(1);
        result.RunsStillUnknown.ShouldBe(0);
        result.RunsSkipped.ShouldBe(0);

        var run = await store.GetRunAsync(runId);
        run!.Cost!.InputCost.ShouldBe(5m);
        run.Cost.Source.ShouldBe(PricingSource.Catalog);
    }

    [Fact]
    public async Task A_row_with_no_stored_provider_resolves_by_model_name_alone()
    {
        // Legacy rows (written before runs.model_provider existed) carry a null
        // provider; recalculation must not crash and must still resolve by model.
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext(TenantId));

        var runId = await StartAndCompleteAsync(
            store,
            provider: null,
            model: "legacy-model",
            cost: new RunCost { Source = PricingSource.Unknown });

        var resolver = new FakeRunPricingResolver((provider, model, _) =>
        {
            provider.ShouldBeNull();
            return string.Equals(model, "legacy-model", StringComparison.Ordinal)
                ? new RunCost { InputCost = 3m, Currency = "USD", Source = PricingSource.Configuration }
                : new RunCost { Source = PricingSource.Unknown };
        });

        var service = new RunCostRecalculationService(store, resolver);
        var result = await service.RecalculateAsync(TenantId);

        result.RunsUpdated.ShouldBe(1);

        var run = await store.GetRunAsync(runId);
        run!.Cost!.InputCost.ShouldBe(3m);
    }

    [Fact]
    public async Task A_run_that_stays_unpriced_is_counted_as_still_unknown_not_skipped()
    {
        var store = new InMemoryRunStore(tenantContext: new FixedTenantContext(TenantId));

        await StartAndCompleteAsync(
            store,
            provider: "openai",
            model: "no-such-model",
            cost: new RunCost { Source = PricingSource.Unknown });

        var resolver = new FakeRunPricingResolver((_, _, _) => new RunCost { Source = PricingSource.Unknown });

        var service = new RunCostRecalculationService(store, resolver);
        var result = await service.RecalculateAsync(TenantId);

        result.RunsConsidered.ShouldBe(1);
        result.RunsStillUnknown.ShouldBe(1);
        result.RunsUpdated.ShouldBe(0);
        result.RunsSkipped.ShouldBe(0);
    }

    private static async Task<Guid> StartAndCompleteAsync(
        InMemoryRunStore store,
        string? provider,
        string model,
        RunCost cost)
    {
        var runId = AgentPrismId.NewId();

        await store.StartRunAsync(new RunStartInfo
        {
            RunId = runId,
            AgentName = "test-agent",
            StartedAt = DateTimeOffset.UtcNow,
            TenantId = TenantId,
            ModelId = model,
            ModelProvider = provider,
        });

        await store.CompleteRunAsync(new RunCompletion
        {
            RunId = runId,
            Status = RunStatus.Completed,
            CompletedAt = DateTimeOffset.UtcNow,
            Usage = new RunUsage { InputTokens = 1_000, OutputTokens = 1_000, TotalTokens = 2_000 },
            Cost = cost,
        });

        return runId;
    }
}
