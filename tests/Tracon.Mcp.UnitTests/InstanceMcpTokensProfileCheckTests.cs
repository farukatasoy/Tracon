using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tracon.Mcp.UnitTests;

/// <summary>
/// Phase 265 (F-768): the production decision that MCP OAuth tokens live where every declared instance reads
/// them.
/// </summary>
/// <remarks>
/// The mode's own startup validator already refuses a missing cache and content protection that is off. What this
/// check adds is the decision about a deployment that said it runs more than one instance, so it reads the declared
/// count, the mode, and what kind of cache is registered.
/// </remarks>
public sealed class InstanceMcpTokensProfileCheckTests
{
    [Fact]
    public void One_declared_instance_is_never_asked_anything()
    {
        using var provider = Provider(instances: 1, McpOAuthTokenStore.Memory);

        var result = new InstanceMcpTokensProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.NotApplicable);
    }

    [Fact]
    public void The_memory_mode_is_refused_for_two_instances_and_the_remedy_names_the_way_out()
    {
        using var provider = Provider(instances: 2, McpOAuthTokenStore.Memory);

        var result = new InstanceMcpTokensProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.Permissive);
        result.Setting.ShouldBe("Tracon:Mcp:OAuthTokenStore");
        result.Observed.ShouldContain("2 instances", Case.Sensitive);
        result.Remedy.ShouldContain(nameof(McpOAuthTokenStore.DistributedCache), Case.Sensitive);
        result.Remedy.ShouldContain("accept the risk", Case.Sensitive);
    }

    [Fact]
    public void The_distributed_mode_on_a_cache_that_instances_share_satisfies_the_decision()
    {
        using var provider = Provider(
            instances: 2,
            McpOAuthTokenStore.DistributedCache,
            static services => services.AddSingleton<IDistributedCache>(new RecordingDistributedCache()));

        new InstanceMcpTokensProfileCheck().Evaluate(provider).State.ShouldBe(ProductionProfileState.Satisfied);
    }

    [Fact]
    public void The_distributed_mode_on_a_cache_that_lives_in_one_process_is_refused()
    {
        using var provider = Provider(
            instances: 2,
            McpOAuthTokenStore.DistributedCache,
            static services => services.AddSingleton<IDistributedCache>(new MemoryDistributedCache()));

        var result = new InstanceMcpTokensProfileCheck().Evaluate(provider);

        result.State.ShouldBe(ProductionProfileState.Permissive);
        result.Observed.ShouldContain("one process", Case.Sensitive);
    }

    private static ServiceProvider Provider(
        int instances,
        McpOAuthTokenStore mode,
        Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Options.Create(new TraconOptions { Deployment = { Instances = instances } }));
        services.AddSingleton(Options.Create(new TraconMcpOptions { OAuthTokenStore = mode }));
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}
