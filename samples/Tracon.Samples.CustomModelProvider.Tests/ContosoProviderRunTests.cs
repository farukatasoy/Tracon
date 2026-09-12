using Tracon.Samples.CustomModelProvider;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.CustomModelProvider.Tests;

/// <summary>
/// Completes a real agent run through the sample provider, using only
/// published Tracon packages.
/// </summary>
/// <remarks>
/// <para>
/// The contract suite proves the provider satisfies the rules
/// <see cref="IModelProvider"/> states. This proves the other half: that a
/// provider registered with <c>AddModelProvider()</c> is actually reachable
/// end to end — the registry finds it by name, the compiler builds an agent on
/// it, and the run produces the provider's own answer.
/// </para>
/// <para>
/// Nothing here touches a network. The sample provider answers from the last
/// user message, so the assertion can be exact.
/// </para>
/// </remarks>
public sealed class ContosoProviderRunTests
{
    private const string AgentName = "contoso-echo";

    private static ServiceProvider BuildHost(Action<ITraconBuilder>? extra = null)
    {
        var services = new ServiceCollection();

        var tracon = services
            .AddTracon()
            .AddModelProvider(new ContosoModelProvider("setup-time-key"))
            .AddAgent(new AgentDefinition
            {
                Name = AgentName,
                Instructions = "Repeat what the user said.",
                Model = new ModelBinding
                {
                    Provider = ContosoModelProvider.ProviderName,
                    Model = "contoso-large",
                },
            });

        extra?.Invoke(tracon);

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task An_agent_bound_to_the_sample_provider_completes_a_run()
    {
        await using var host = BuildHost();

        var catalog = host.GetRequiredService<IAgentCatalog>();
        var agent = await catalog.ResolveAsync(AgentName, culture: null, TestContext.Current.CancellationToken);

        agent.ShouldNotBeNull();

        var response = await agent.RunAsync("hello from the sample");

        response.Text.ShouldContain("hello from the sample");
        response.Text.ShouldContain("contoso");
    }

    /// <remarks>
    /// The provider is registered under <c>contoso</c>, and the binding here
    /// names it in a different casing. The registry matches provider names
    /// case-insensitively, so this must resolve to the same provider.
    /// </remarks>
    [Fact]
    public async Task A_binding_whose_provider_name_differs_in_case_still_resolves()
    {
        await using var host = BuildHost(static builder => builder.AddAgent(new AgentDefinition
        {
            Name = "contoso-shouted-binding",
            Instructions = "Repeat what the user said.",
            Model = new ModelBinding
            {
                Provider = ContosoModelProvider.ProviderName.ToUpperInvariant(),
                Model = "contoso-large",
            },
        }));

        var catalog = host.GetRequiredService<IAgentCatalog>();
        var agent = await catalog.ResolveAsync(
            "contoso-shouted-binding", culture: null, TestContext.Current.CancellationToken);

        agent.ShouldNotBeNull();
        (await agent.RunAsync("still routed")).Text.ShouldContain("still routed");
    }

    /// <remarks>
    /// The registry names every registered provider when a binding points at
    /// one that does not exist. A third-party provider must appear in that
    /// list, or the error would send the host author looking in the wrong
    /// place.
    /// </remarks>
    [Fact]
    public async Task An_unknown_provider_name_fails_with_a_message_naming_the_registered_ones()
    {
        await using var host = BuildHost(static builder => builder.AddAgent(new AgentDefinition
        {
            Name = "bound-to-nothing",
            Instructions = "Repeat what the user said.",
            Model = new ModelBinding { Provider = "no-such-provider", Model = "whatever" },
        }));

        var catalog = host.GetRequiredService<IAgentCatalog>();

        var exception = await Should.ThrowAsync<TraconCompilationException>(
            async () => await catalog.ResolveAsync(
                "bound-to-nothing", culture: null, TestContext.Current.CancellationToken));

        exception.Message.ShouldContain(ContosoModelProvider.ProviderName);
    }
}
