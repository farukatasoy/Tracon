using Tracon.Anthropic.UnitTests.Infrastructure;

namespace Tracon.Anthropic.UnitTests;

/// <summary>
/// What the Anthropic provider states through <see cref="ISamplingSupport.SamplingLimits"/>: nothing.
/// </summary>
/// <remarks>
/// The Messages API sends no seed, and it bounds the TOTAL length of the stop sequences (2,500 characters, measured
/// with real calls on 2026-10-27), not their number. A count limit cannot state that, and a count limit stated
/// anyway would be a number the provider never used.
/// </remarks>
public sealed class AnthropicSamplingLimitsTests
{
    [Fact]
    public void The_provider_states_no_limit()
    {
        var provider = new AnthropicModelProvider(
            AnthropicProviderNames.Anthropic,
            new AnthropicChatClientFactory(TestData.Options()),
            [new ModelDescriptor { Name = TestData.Model }]);

        provider.SamplingLimits.ShouldBe(SamplingLimits.None);
    }
}
