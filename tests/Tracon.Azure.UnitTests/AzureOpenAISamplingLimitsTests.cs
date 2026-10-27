using Tracon.Azure.UnitTests.Infrastructure;

namespace Tracon.Azure.UnitTests;

/// <summary>
/// What the Azure OpenAI provider states through <see cref="ISamplingSupport.SamplingLimits"/> (phase 252): the
/// four stop sequences Azure documents for the Chat Completions request it sends. The seed is a 64-bit integer on
/// the wire, so no seed range is stated.
/// </summary>
public sealed class AzureOpenAISamplingLimitsTests
{
    [Fact]
    public void The_provider_accepts_four_stop_sequences_and_any_seed()
    {
        var provider = new AzureOpenAIModelProvider(
            AzureOpenAIProviderNames.AzureOpenAI,
            new AzureOpenAIChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Deployment }]);

        provider.SamplingLimits.MaxStopSequences.ShouldBe(4);
        provider.SamplingLimits.MinSeed.ShouldBeNull();
        provider.SamplingLimits.MaxSeed.ShouldBeNull();
    }
}
