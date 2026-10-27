using System.Reflection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Recording;

/// <summary>
/// F-349: a run whose tokens were spent on more than one model prices each
/// share at the model that spent it. One model alone keeps the resolver's
/// answer as it is, rate fields included.
/// </summary>
public sealed class RunCostCombinerTests
{
    private const string AgentProvider = "looping";
    private const string AgentModel = "loop-model";
    private const string JudgeProvider = "judging";
    private const string JudgeModel = "judge-model";

    private static readonly RunUsage AgentUsage = new() { InputTokens = 1_000, OutputTokens = 100, TotalTokens = 1_100 };
    private static readonly RunUsage JudgeUsage = new() { InputTokens = 400, OutputTokens = 40, TotalTokens = 440 };

    [Fact]
    public void One_model_keeps_the_resolvers_answer_and_its_rates()
    {
        var resolver = PricedResolver(judgePriced: true);

        var cost = RunCostCombiner.Resolve(resolver, AgentProvider, AgentModel, AgentUsage, []).ShouldNotBeNull();

        cost.InputCost.ShouldBe(0.002m);
        cost.InputPricePerMillionTokens.ShouldBe(2m);
        cost.OutputPricePerMillionTokens.ShouldBe(4m);
        resolver.CallCount.ShouldBe(1);
    }

    [Fact]
    public void A_share_on_the_agents_own_model_is_priced_with_the_agents_usage()
    {
        // A summarizer with no model of its own runs on the agent's model.
        var resolver = PricedResolver(judgePriced: true);

        var cost = RunCostCombiner.Resolve(
            resolver,
            AgentProvider,
            AgentModel,
            AgentUsage,
            [new SideChannelModelUsage("LOOPING", "Loop-Model", JudgeUsage)]).ShouldNotBeNull();

        cost.InputCost.ShouldBe(1_400 * 2m / 1_000_000m);
        cost.InputPricePerMillionTokens.ShouldBe(2m);
        resolver.CallCount.ShouldBe(1);
    }

    [Fact]
    public void Two_models_are_priced_apart_summed_and_carry_no_rate()
    {
        var cost = RunCostCombiner.Resolve(
            PricedResolver(judgePriced: true),
            AgentProvider,
            AgentModel,
            AgentUsage,
            [new SideChannelModelUsage(JudgeProvider, JudgeModel, JudgeUsage)]).ShouldNotBeNull();

        cost.Source.ShouldBe(PricingSource.Catalog);
        cost.Currency.ShouldBe("USD");
        cost.InputCost.ShouldBe((1_000 * 2m / 1_000_000m) + (400 * 100m / 1_000_000m));
        cost.OutputCost.ShouldBe((100 * 4m / 1_000_000m) + (40 * 300m / 1_000_000m));
        cost.InputPricePerMillionTokens.ShouldBeNull();
        cost.OutputPricePerMillionTokens.ShouldBeNull();
        cost.CachedInputPricePerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public void An_unpriced_model_adds_nothing_and_no_price_is_invented_for_it()
    {
        var cost = RunCostCombiner.Resolve(
            PricedResolver(judgePriced: false),
            AgentProvider,
            AgentModel,
            AgentUsage,
            [new SideChannelModelUsage(JudgeProvider, JudgeModel, JudgeUsage)]).ShouldNotBeNull();

        // The agent's share only — the judge's tokens are NOT priced at the agent's rate.
        cost.Source.ShouldBe(PricingSource.Catalog);
        cost.InputCost.ShouldBe(0.002m);
        cost.OutputCost.ShouldBe(0.0004m);
        cost.InputPricePerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public void An_unpriced_agent_model_takes_the_source_of_the_first_priced_share()
    {
        var resolver = new FakeRunPricingResolver((provider, _, usage) =>
            string.Equals(provider, JudgeProvider, StringComparison.Ordinal)
                ? new RunCost { InputCost = usage!.InputTokens, Currency = "USD", Source = PricingSource.Configuration }
                : new RunCost { Source = PricingSource.Unknown });

        var cost = RunCostCombiner.Resolve(
            resolver,
            AgentProvider,
            AgentModel,
            AgentUsage,
            [new SideChannelModelUsage(JudgeProvider, JudgeModel, JudgeUsage)]).ShouldNotBeNull();

        cost.Source.ShouldBe(PricingSource.Configuration);
        cost.InputCost.ShouldBe(400m);
    }

    [Fact]
    public void No_priced_share_makes_the_whole_cost_unknown()
    {
        var resolver = new FakeRunPricingResolver((_, _, _) => new RunCost { Source = PricingSource.Unknown });

        var cost = RunCostCombiner.Resolve(
            resolver,
            AgentProvider,
            AgentModel,
            AgentUsage,
            [new SideChannelModelUsage(JudgeProvider, JudgeModel, JudgeUsage)]).ShouldNotBeNull();

        cost.Source.ShouldBe(PricingSource.Unknown);
        cost.Total().ShouldBeNull();
    }

    [Fact]
    public void A_side_share_without_agent_usage_still_carries_no_rate()
    {
        // The agent's call failed after the summarizer ran: the only priced share
        // is not the run's own model, so its rate would describe the wrong model.
        var cost = RunCostCombiner.Resolve(
            PricedResolver(judgePriced: true),
            AgentProvider,
            AgentModel,
            agentUsage: null,
            [new SideChannelModelUsage(JudgeProvider, JudgeModel, JudgeUsage)]).ShouldNotBeNull();

        cost.InputCost.ShouldBe(400 * 100m / 1_000_000m);
        cost.InputPricePerMillionTokens.ShouldBeNull();
    }

    [Fact]
    public void Shares_in_different_currencies_are_not_summed()
    {
        var cost = RunCostCombiner.Combine(
        [
            new RunCost { InputCost = 1m, Currency = "USD", Source = PricingSource.Catalog },
            new RunCost { InputCost = 1m, Currency = "EUR", Source = PricingSource.Catalog },
        ]);

        cost.Source.ShouldBe(PricingSource.Unknown);
        cost.Total().ShouldBeNull();
    }

    [Fact]
    public void Two_models_cache_write_charges_are_summed_into_the_total()
    {
        // Phase 253: a judge or summarizer on a caching model writes its own cache. Its charge is a
        // term of the combined cost, or the run under-reports it.
        var resolver = new FakeRunPricingResolver((provider, _, usage) => new RunCost
        {
            InputCost = 1m,
            CacheWriteCost = string.Equals(provider, JudgeProvider, StringComparison.Ordinal) ? 0.5m : 0.25m,
            CacheWritePricePerMillionTokens = 12.5m,
            Currency = "USD",
            Source = PricingSource.Catalog,
        });

        var cost = RunCostCombiner.Resolve(
            resolver,
            AgentProvider,
            AgentModel,
            AgentUsage,
            [new SideChannelModelUsage(JudgeProvider, JudgeModel, JudgeUsage)]).ShouldNotBeNull();

        cost.CacheWriteCost.ShouldBe(0.75m);
        cost.CacheWritePricePerMillionTokens.ShouldBeNull("one rate cannot describe a total several rates produced");
        cost.CacheWriteLongPricePerMillionTokens.ShouldBeNull();
        cost.Total().ShouldBe(2.75m);
    }

    /// <summary>
    /// K-483's class guard: every cost term of <see cref="RunCost"/> — each
    /// <c>decimal?</c> property whose name ends in <c>Cost</c> — is summed. A
    /// fourth term that <see cref="RunCost.Total"/> learns but the combiner does
    /// not would make every run priced at two models under-report.
    /// </summary>
    [Fact]
    public void Every_cost_term_of_RunCost_is_summed()
    {
        var terms = typeof(RunCost)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(static property => property.PropertyType == typeof(decimal?)
                                      && property.Name.EndsWith("Cost", StringComparison.Ordinal))
            .ToList();

        terms.Count.ShouldBeGreaterThanOrEqualTo(3);

        var part = new RunCost { Currency = "USD", Source = PricingSource.Catalog };

        foreach (var term in terms)
        {
            term.SetValue(part, 1m);
        }

        var combined = RunCostCombiner.Combine([part, part with { }]);

        foreach (var term in terms)
        {
            term.GetValue(combined).ShouldBe(2m, $"RunCostCombiner.Combine does not sum {term.Name}.");
        }

        combined.Total().ShouldBe(2m * terms.Count);
    }

    /// <summary>Agent model: 2 / 4 per million. Judge model: 100 / 300 per million, or no price.</summary>
    private static FakeRunPricingResolver PricedResolver(bool judgePriced)
        => new((provider, _, usage) =>
        {
            // The resolver contract: no usage, no cost.
            if (usage is null)
            {
                return null;
            }

            var (input, output) = string.Equals(provider, JudgeProvider, StringComparison.OrdinalIgnoreCase)
                ? (100m, 300m)
                : (2m, 4m);

            if (!judgePriced && input == 100m)
            {
                return new RunCost { Source = PricingSource.Unknown };
            }

            return new RunCost
            {
                InputCost = (usage.InputTokens ?? 0) * input / 1_000_000m,
                InputPricePerMillionTokens = input,
                OutputCost = (usage.OutputTokens ?? 0) * output / 1_000_000m,
                OutputPricePerMillionTokens = output,
                Currency = "USD",
                Source = PricingSource.Catalog,
            };
        });
}
