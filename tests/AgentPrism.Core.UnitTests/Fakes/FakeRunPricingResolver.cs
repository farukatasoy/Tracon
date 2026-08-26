namespace AgentPrism.Core.UnitTests.Fakes;

/// <summary>A fake <see cref="IRunPricingResolver"/> that also counts calls.</summary>
/// <remarks>
/// Tests that must prove the resolver was <strong>never</strong> called (no
/// cost cap configured) assert against <see cref="CallCount"/>; a real
/// <see cref="RunPricingResolver"/> would need a full <see cref="ModelProviderRegistry"/>
/// catalog scan just to answer that question.
/// </remarks>
internal sealed class FakeRunPricingResolver(Func<string?, string?, RunUsage?, RunCost?> resolve) : IRunPricingResolver
{
    public int CallCount { get; private set; }

    public RunCost? Resolve(string? provider, string? model, RunUsage? usage)
    {
        CallCount++;
        return resolve(provider, model, usage);
    }
}
