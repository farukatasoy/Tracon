using Microsoft.Extensions.DependencyInjection;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Runs;

/// <summary>
/// Verifies that <see cref="IRunErrorClassifier"/>'s DI registration follows
/// the K4 pattern (the consumer's registration wins).
/// </summary>
public sealed class RunErrorClassifierRegistrationTests
{
    [Fact]
    public void The_built_in_classifier_is_registered_by_default()
    {
        var services = new ServiceCollection();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRunErrorClassifier>().ShouldBeOfType<DefaultRunErrorClassifier>();
    }

    [Fact]
    public void The_consumers_own_classifier_wins_via_TryAdd()
    {
        var services = new ServiceCollection();

        // The consumer registers BEFORE AddTracon; TryAddSingleton silently
        // skips the second registration (K4).
        services.AddSingleton<IRunErrorClassifier, SpyRunErrorClassifier>();
        services.AddTracon();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRunErrorClassifier>().ShouldBeOfType<SpyRunErrorClassifier>();
    }
}
