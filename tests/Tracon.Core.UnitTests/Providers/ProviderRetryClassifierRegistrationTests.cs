using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Core.UnitTests.Providers;

/// <summary>
/// Verifies that <see cref="IProviderRetryClassifier"/>'s DI registration
/// follows the K4 pattern (the consumer's registration wins) - the same
/// contract <c>RunErrorClassifierRegistrationTests</c> verifies for
/// <see cref="IRunErrorClassifier"/>.
/// </summary>
public sealed class ProviderRetryClassifierRegistrationTests
{
    [Fact]
    public void The_built_in_classifier_is_registered_by_default()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProviderRetryClassifier>().ShouldBeOfType<DefaultProviderRetryClassifier>();
    }

    [Fact]
    public void The_consumers_own_classifier_wins_via_TryAdd()
    {
        var services = new ServiceCollection();

        // The consumer registers BEFORE AddTracon; TryAddSingleton silently
        // skips the second registration (K4).
        services.AddSingleton<IProviderRetryClassifier, SpyProviderRetryClassifier>();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IProviderRetryClassifier>().ShouldBeOfType<SpyProviderRetryClassifier>();
    }

    private sealed class SpyProviderRetryClassifier : IProviderRetryClassifier
    {
        public ProviderRetryDecision Classify(Exception exception) => ProviderRetryDecision.Unknown;
    }
}
