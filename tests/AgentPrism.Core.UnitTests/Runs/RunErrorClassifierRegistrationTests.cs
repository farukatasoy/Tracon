using AgentPrism.Core.UnitTests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Core.UnitTests.Runs;

/// <summary>
/// <see cref="IRunErrorClassifier"/>'in DI kaydinin K4 desenini (tuketicinin
/// kaydi kazanir) izledigini dogrular.
/// </summary>
public sealed class RunErrorClassifierRegistrationTests
{
    [Fact]
    public void Varsayilan_kurulumda_yerlesik_siniflandirici_kayitlidir()
    {
        var services = new ServiceCollection();
        services.AddAgentPrism();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRunErrorClassifier>().ShouldBeOfType<DefaultRunErrorClassifier>();
    }

    [Fact]
    public void Tuketicinin_kendi_siniflandiricisi_TryAdd_sayesinde_kazanir()
    {
        var services = new ServiceCollection();

        // Tuketici kaydi AddAgentPrism'den ONCE yapilir; TryAddSingleton
        // ikinci kaydi sessizce atlar (K4).
        services.AddSingleton<IRunErrorClassifier, SpyRunErrorClassifier>();
        services.AddAgentPrism();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRunErrorClassifier>().ShouldBeOfType<SpyRunErrorClassifier>();
    }
}
