using System.Globalization;
using CsCheck;

namespace Tracon.Tests.Differential;

/// <summary>
/// The properties every store implementation must hold, beside the model comparison: paging that neither drops nor
/// repeats a record, and a cost total that is the sum of its parts.
/// </summary>
public abstract partial class StoreDifferentialSuite
{
    private static readonly DateTimeOffset PagingOrigin = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Pages through the sessions of one tenant, most of which share an update time, and checks that the pages put
    /// together are the whole list: nothing missing, nothing twice, the same order on every read.
    /// </summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Session_pages_put_together_are_the_whole_list()
    {
        var stores = await CreateStoresAsync();
        var store = DifferentialStores.Require(stores.Sessions);
        var plans =
            from count in Gen.Int[1, 25]
            from distinct in Gen.Int[1, 3]
            from times in Gen.Int[0, distinct - 1].Array[count]
            from pageSize in Gen.Int[1, 8]
            from skipBack in Gen.Int[-2, 2]
            select (times, pageSize, skipBack);

        await plans.SampleAsync(
            async plan =>
            {
                var scope = DifferentialScope.Next();
                stores.Tenant.TenantId = scope.Wrap("paging");

                for (var index = 0; index < plan.times.Length; index++)
                {
                    await store.SaveAsync(new SessionRecord
                    {
                        Id = "s" + index.ToString(CultureInfo.InvariantCulture),
                        AgentName = "agent",
                        State = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
                        CreatedAt = PagingOrigin,
                        UpdatedAt = PagingOrigin.AddSeconds(plan.times[index]),
                    });
                }

                var whole = await store.QueryAsync(new SessionQuery { Take = int.MaxValue });
                AssertWholeList(plan.times.Length, plan.pageSize, whole.Select(static session => session.Id).ToList(), whole.Select(static session => session.UpdatedAt.UtcTicks).ToList());

                for (var pass = 0; pass < 2; pass++)
                {
                    var seen = new List<string>();

                    for (var skip = 0; skip <= plan.times.Length + plan.pageSize; skip += plan.pageSize)
                    {
                        seen.AddRange((await store.QueryAsync(new SessionQuery { Skip = skip, Take = plan.pageSize })).Select(static session => session.Id));
                    }

                    seen.ShouldBe(whole.Select(static session => session.Id).ToList(), $"pass {pass + 1}: the pages put together are not the whole list, in its order");
                }

                (await store.QueryAsync(new SessionQuery { Take = 0 })).ShouldBeEmpty();
                (await store.QueryAsync(new SessionQuery { Take = -plan.pageSize })).ShouldBeEmpty();
                (await store.QueryAsync(new SessionQuery { Skip = plan.times.Length })).ShouldBeEmpty();
                (await store.QueryAsync(new SessionQuery { Skip = plan.skipBack < 0 ? plan.skipBack : 0, Take = int.MaxValue }))
                    .Select(static session => session.Id).ToList().ShouldBe(whole.Select(static session => session.Id).ToList(), "a negative skip must read as zero");
            },
            seed: DifferentialSettings.Seed,
            iter: DifferentialSettings.Iterations(Iterations) / 4,
            threads: 1,
            print: static plan => $"update-time slot per session: [{string.Join(",", plan.times)}], page size {plan.pageSize}");
    }

    /// <summary>
    /// Pages through the runs of one tenant, most of which started at the same instant, and checks that the pages put
    /// together are the whole list.
    /// </summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Run_pages_put_together_are_the_whole_list()
    {
        var stores = await CreateStoresAsync();
        var store = DifferentialStores.Require(stores.Runs);
        var plans =
            from count in Gen.Int[1, 25]
            from distinct in Gen.Int[1, 3]
            from times in Gen.Int[0, distinct - 1].Array[count]
            from pageSize in Gen.Int[1, 8]
            select (times, pageSize);

        await plans.SampleAsync(
            async plan =>
            {
                var scope = DifferentialScope.Next();
                stores.Tenant.TenantId = scope.Wrap("paging");
                var expected = new List<Guid>();

                for (var index = 0; index < plan.times.Length; index++)
                {
                    var id = Guid.NewGuid();
                    expected.Add(id);
                    await store.StartRunAsync(new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = PagingOrigin.AddSeconds(plan.times[index]) });
                }

                var whole = await store.QueryRunsAsync(new RunQuery { Take = int.MaxValue });
                AssertWholeList(plan.times.Length, plan.pageSize, whole.Select(static run => run.Id.ToString("N", CultureInfo.InvariantCulture)).ToList(), whole.Select(static run => run.StartedAt.UtcTicks).ToList());
                whole.Select(static run => run.Id).ToList().ShouldBe(expected, ignoreOrder: true);

                for (var pass = 0; pass < 2; pass++)
                {
                    var seen = new List<Guid>();

                    for (var skip = 0; skip <= plan.times.Length + plan.pageSize; skip += plan.pageSize)
                    {
                        seen.AddRange((await store.QueryRunsAsync(new RunQuery { Skip = skip, Take = plan.pageSize })).Select(static run => run.Id));
                    }

                    seen.ShouldBe(whole.Select(static run => run.Id).ToList(), $"pass {pass + 1}: the pages put together are not the whole list, in its order");
                }

                (await store.QueryRunsAsync(new RunQuery { Take = 0 })).ShouldBeEmpty();
                (await store.QueryRunsAsync(new RunQuery { Take = -plan.pageSize })).ShouldBeEmpty();
                (await store.QueryRunsAsync(new RunQuery { Skip = plan.times.Length })).ShouldBeEmpty();
            },
            seed: DifferentialSettings.Seed,
            iter: DifferentialSettings.Iterations(Iterations) / 4,
            threads: 1,
            print: static plan => $"start-time slot per run: [{string.Join(",", plan.times)}], page size {plan.pageSize}");
    }

    /// <summary>
    /// Writes runs with generated costs and checks that the total the store reports is the sum of the parts, each part
    /// rounded to the ten decimals the cost columns keep.
    /// </summary>
    /// <returns>The completion task.</returns>
    [Fact]
    public async Task Cost_total_is_the_sum_of_its_parts()
    {
        var stores = await CreateStoresAsync();
        var store = DifferentialStores.Require(stores.Runs);
        var part = Gen.Frequency((1, Gen.Const((decimal?)null)), (3, BoundaryValues.Cost.Select(static value => (decimal?)value)));
        var currency = Gen.OneOf("USD", "usd", "Usd");
        var cost =
            from input in part
            from output in part
            from cached in part
            from write in part
            from label in currency
            select (input, output, cached, write, label);
        var plans = cost.Array[1, 6];

        await plans.SampleAsync(
            async costs =>
            {
                var scope = DifferentialScope.Next();
                stores.Tenant.TenantId = scope.Wrap("cost");
                var expected = 0m;
                var anyTotal = false;

                foreach (var (rawInput, rawOutput, rawCached, rawWrite, label) in costs)
                {
                    var input = stores.Traits.Representable(rawInput);
                    var output = stores.Traits.Representable(rawOutput);
                    var cached = stores.Traits.Representable(rawCached);
                    var write = stores.Traits.Representable(rawWrite);
                    var id = Guid.NewGuid();
                    await store.StartRunAsync(new RunStartInfo { RunId = id, AgentName = "agent", StartedAt = PagingOrigin, ModelId = "model" });
                    await store.CompleteRunAsync(new RunCompletion
                    {
                        RunId = id,
                        Status = RunStatus.Completed,
                        CompletedAt = PagingOrigin.AddSeconds(1),
                        Usage = new RunUsage { InputTokens = 1, OutputTokens = 1, TotalTokens = 2 },
                        Cost = new RunCost { InputCost = input, OutputCost = output, CachedInputCost = cached, CacheWriteCost = write, Currency = label, Source = PricingSource.Catalog },
                    });

                    // The sum is written out by hand, term by term: it is the oracle, not RunCost.Total().
                    var sum = (stores.Traits.Cost(input) ?? 0m) + (stores.Traits.Cost(output) ?? 0m) + (stores.Traits.Cost(cached) ?? 0m) + (stores.Traits.Cost(write) ?? 0m);
                    anyTotal |= input is not null || output is not null || cached is not null || write is not null;
                    expected += sum;

                    var read = (await store.GetRunAsync(id))!.Cost;
                    read.ShouldNotBeNull();
                    (read.Total() ?? 0m).ShouldBe(sum, $"the run's own cost total, parts {input}/{output}/{cached}/{write}");
                }

                var statistics = await store.GetStatisticsAsync(new RunStatisticsQuery());

                statistics.TotalRuns.ShouldBe(costs.Length);
                statistics.CurrencyCount.ShouldBeLessThanOrEqualTo(1);

                if (anyTotal)
                {
                    statistics.TotalCost.ShouldNotBeNull();
                    statistics.TotalCost.Value.ShouldBe(expected, stores.Traits.AggregateCostRelativeTolerance * Math.Max(1m, Math.Abs(expected)), "the total cost of all runs");
                }
            },
            seed: DifferentialSettings.Seed,
            iter: DifferentialSettings.Iterations(Iterations) / 4,
            threads: 1,
            print: static costs => string.Join(" | ", costs.Select(static c => $"{c.input}/{c.output}/{c.cached}/{c.write} {c.label}")));
    }

    private static void AssertWholeList(int count, int pageSize, List<string> ids, List<long> keys)
    {
        ids.Count.ShouldBe(count, $"the whole list holds every record (page size {pageSize})");
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(count, "a record is listed twice");

        for (var index = 1; index < keys.Count; index++)
        {
            keys[index].ShouldBeLessThanOrEqualTo(keys[index - 1], "the list is newest first");
        }
    }
}
