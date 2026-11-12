using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Evaluation;

/// <summary>
/// A model judge whose model the catalog says rejects sampling stops the host at start, not the first sampled run.
/// </summary>
public sealed class ModelRunJudgeOptionsValidatorTests
{
    [Fact]
    public void A_judge_that_sets_the_temperature_on_a_model_that_rejects_sampling_is_refused()
    {
        using var provider = Provider(supportsSampling: false, temperature: 0.2f);

        var exception = Should.Throw<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ModelRunJudgeOptions>>().Value);

        exception.Message.ShouldContain("fake/fake-model", Case.Sensitive);
        exception.Message.ShouldContain(nameof(ModelBinding.Temperature), Case.Sensitive);
    }

    [Fact]
    public void A_judge_that_sets_the_top_p_on_a_model_that_rejects_sampling_is_refused()
    {
        using var provider = Provider(supportsSampling: false, topP: 0.9f);

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ModelRunJudgeOptions>>().Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public void A_judge_on_a_model_that_accepts_sampling_or_that_the_catalog_does_not_judge_is_accepted(bool? supportsSampling)
    {
        using var provider = Provider(supportsSampling, temperature: 0.2f);

        provider.GetRequiredService<IOptions<ModelRunJudgeOptions>>().Value.Model.Temperature.ShouldBe(0.2f);
    }

    [Fact]
    public void A_judge_that_sets_no_sampling_field_is_accepted_on_a_model_that_rejects_sampling()
    {
        using var provider = Provider(supportsSampling: false);

        provider.GetRequiredService<IOptions<ModelRunJudgeOptions>>().Value.Model.Model.ShouldBe("fake-model");
    }

    [Fact]
    public void The_evaluator_judge_that_takes_the_same_options_is_checked_the_same_way()
    {
        var services = new ServiceCollection();
        services.AddTracon()
            .AddEvaluatorJudge(
                "quality",
                new NoMetricsEvaluator(),
                options => options.Model = new ModelBinding { Provider = "fake", Model = "fake-model", Temperature = 0.2f });
        services.AddSingleton<IModelProviderRegistry>(Registry(supportsSampling: false));
        using var provider = services.BuildServiceProvider();

        Should.Throw<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ModelRunJudgeOptions>>().Value);
    }

    private static ServiceProvider Provider(bool? supportsSampling, float? temperature = null, float? topP = null)
    {
        var services = new ServiceCollection();
        services.AddTracon().AddModelRunJudge(options => options.Model = new ModelBinding
        {
            Provider = "fake",
            Model = "fake-model",
            Temperature = temperature,
            TopP = topP,
        });

        // Registered after AddTracon, so the registry the validator receives is the one with the catalog this test states.
        services.AddSingleton<IModelProviderRegistry>(Registry(supportsSampling));

        return services.BuildServiceProvider();
    }

    private static ModelProviderRegistry Registry(bool? supportsSampling)
        => TestData.Providers(new FakeModelProvider(models:
        [
            new ModelDescriptor { Name = "fake-model", SupportsSampling = supportsSampling },
        ]));

    private sealed class NoMetricsEvaluator : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => [];

        public ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages,
            ChatResponse modelResponse,
            ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(new EvaluationResult());
    }
}
