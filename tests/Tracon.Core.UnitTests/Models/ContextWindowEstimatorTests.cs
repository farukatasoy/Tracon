using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>Verifies the pre-flight context-window estimator (phase 62, F-59).</summary>
public sealed class ContextWindowEstimatorTests
{
    [Fact]
    public void Unknown_model_never_reports_a_rejection()
    {
        var estimator = Estimator(reserveRatio: 0.2, models: []);

        var estimate = estimator.Estimate(Binding, new string('x', 10_000));

        estimate.ContextWindowTokens.ShouldBeNull();
        estimate.AllowedPromptTokens.ShouldBeNull();
        estimate.WouldBeRejected.ShouldBeFalse();
        estimate.PromptTokens.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_short_prompt_under_a_tiny_window_is_not_rejected()
    {
        var estimator = Estimator(
            reserveRatio: 0,
            models: [new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 100 }]);

        var estimate = estimator.Estimate(Binding, "hi");

        estimate.ContextWindowTokens.ShouldBe(100);
        estimate.AllowedPromptTokens.ShouldBe(100);
        estimate.WouldBeRejected.ShouldBeFalse();
    }

    [Fact]
    public void A_long_prompt_over_a_tiny_window_is_rejected()
    {
        var estimator = Estimator(
            reserveRatio: 0,
            models: [new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 5 }]);

        var estimate = estimator.Estimate(Binding, string.Join(' ', Enumerable.Repeat("banana", 200)));

        estimate.WouldBeRejected.ShouldBeTrue();
        estimate.PromptTokens.ShouldBeGreaterThan(estimate.AllowedPromptTokens!.Value);
    }

    [Fact]
    public void The_reserve_ratio_shrinks_the_allowed_budget()
    {
        var estimator = Estimator(
            reserveRatio: 0.5,
            models: [new ModelDescriptor { Name = "fake-model", ContextWindowTokens = 1_000 }]);

        var estimate = estimator.Estimate(Binding, "hi");

        estimate.AllowedPromptTokens.ShouldBe(500);
    }

    [Fact]
    public void Null_prompt_counts_as_empty()
    {
        var estimator = Estimator(reserveRatio: 0.2, models: []);

        var estimate = estimator.Estimate(Binding, null);

        estimate.PromptTokens.ShouldBe(0);
    }

    private static ModelBinding Binding => new() { Provider = "fake", Model = "fake-model" };

    private static ContextWindowEstimator Estimator(double reserveRatio, IReadOnlyList<ModelDescriptor> models)
        => new(
            TestData.Providers(new FakeModelProvider(models: models)),
            new StaticOptionsMonitor<TraconOptions>(
                new TraconOptions { Preflight = new TraconPreflightOptions { ReserveRatio = reserveRatio } }));
}
