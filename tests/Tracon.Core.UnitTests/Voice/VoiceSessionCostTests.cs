using Microsoft.Extensions.Options;

namespace Tracon.Core.UnitTests.Voice;

/// <summary>
/// Verifies that a voice session's cost sums in exactly one place, and that an
/// unpriced session is reported as unpriced rather than free.
/// </summary>
/// <remarks>
/// 🚨 K-483. A total spelled out by hand in several call sites loses a term the
/// day a third addend arrives; that defect once let a tenant with a cost ceiling
/// exceed it, and 4241 tests did not see it. The sum lives in
/// <see cref="VoiceSessionCost.Total"/> and nowhere else.
/// </remarks>
public sealed class VoiceSessionCostTests
{
    [Fact]
    public void Both_addends_are_summed()
        => new VoiceSessionCost { DurationCost = 0.25m, CharacterCost = 0.10m }
            .Total()
            .ShouldBe(0.35m);

    [Fact]
    public void A_single_addend_is_the_total()
    {
        new VoiceSessionCost { DurationCost = 0.25m }.Total().ShouldBe(0.25m);
        new VoiceSessionCost { CharacterCost = 0.10m }.Total().ShouldBe(0.10m);
    }

    [Fact]
    public void Nothing_priced_is_null_NOT_zero()
    {
        // A zero would claim the conversation was free. It was not measured.
        new VoiceSessionCost().Total().ShouldBeNull();
        new VoiceSessionCost { Currency = "USD" }.Total().ShouldBeNull();
    }

    [Fact]
    public void A_zero_addend_is_still_a_measurement()
        => new VoiceSessionCost { DurationCost = 0m }.Total().ShouldBe(0m);

    [Fact]
    public void An_unpriced_model_produces_no_cost_record()
    {
        var pricing = new VoiceDurationPricing(Options(new TraconOptions()));

        pricing.Price("openai", "gpt-live-1", 28.0m).ShouldBeNull();
    }

    [Fact]
    public void An_unknown_duration_produces_no_cost_record()
    {
        // Tracon does not see a live session's media. When the provider reports
        // no duration, nothing is priced — a duration is never invented.
        var pricing = new VoiceDurationPricing(Options(WithLivePrice(0.01m)));

        pricing.Price("openai", "gpt-live-1", null).ShouldBeNull();
    }

    [Fact]
    public void A_priced_model_produces_a_duration_cost_and_a_currency()
    {
        var pricing = new VoiceDurationPricing(Options(WithLivePrice(0.60m)));

        var cost = pricing.Price("openai", "gpt-live-1", 30m).ShouldNotBeNull();

        cost.DurationCost.ShouldBe(0.30m);
        cost.CharacterCost.ShouldBeNull();
        cost.Currency.ShouldBe("USD");
        cost.Total().ShouldBe(0.30m);
    }

    [Fact]
    public void The_duration_formula_is_pinned_to_independently_computed_values()
    {
        // 🚨 Every expected value is worked out by hand, not re-derived from the
        // implementation: `ShouldBe(28.5m / 60m * 0.10m)` would assert that the code
        // equals itself and would survive any change to the formula.
        VoiceDurationPricing.ComputeDurationCost(60m, 0.10m).ShouldBe(0.10m);
        VoiceDurationPricing.ComputeDurationCost(30m, 0.10m).ShouldBe(0.05m);
        VoiceDurationPricing.ComputeDurationCost(90m, 0.60m).ShouldBe(0.90m);
        VoiceDurationPricing.ComputeDurationCost(0m, 0.10m).ShouldBe(0m);

        // The number the real provider reported in the phase's own live run, priced
        // at the rate that run used.
        VoiceDurationPricing.ComputeDurationCost(57m, 0.60m).ShouldBe(0.57m);
    }

    private static TraconOptions WithLivePrice(decimal perMinute)
    {
        var options = new TraconOptions();
        options.Pricing.Currency = "USD";
        options.Pricing.Voice["openai"] = new Dictionary<string, VoicePriceOverride>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-live-1"] = new() { PerMinute = perMinute },
        };

        return options;
    }

    private static StaticOptionsMonitor Options(TraconOptions options) => new(options);

    private sealed class StaticOptionsMonitor(TraconOptions value) : IOptionsMonitor<TraconOptions>
    {
        public TraconOptions CurrentValue => value;

        public TraconOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<TraconOptions, string?> listener) => null;
    }
}
