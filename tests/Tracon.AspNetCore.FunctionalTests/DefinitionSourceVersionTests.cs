using Microsoft.Extensions.DependencyInjection;
using Tracon.AspNetCore.FunctionalTests.Infrastructure;
using FakeModelProvider = Tracon.Testing.FakeModelProvider;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// Version pinning for an agent whose definition a custom source returns.
/// </summary>
/// <remarks>
/// A pinned run measures one version of an agent. A source that keeps no history cannot answer for a version, and
/// serving its current definition under a pin would measure the wrong thing without saying so.
/// </remarks>
public sealed class DefinitionSourceVersionTests
{
    private const string Agent = "versioned-agent";
    private const string ProviderName = "version-provider";
    private const string ModelName = "version-model";

    [Fact]
    public async Task A_versioned_source_serves_the_pinned_version_while_the_current_one_moves_on()
    {
        var provider = Provider();
        var source = new VersionedInMemoryDefinitionSource()
            .Add(Definition("Version one speaks."))
            .Add(Definition("Version two speaks."));
        await using var host = await StartAsync(provider, source);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();

        var pinned = (await catalog.ResolveAsync(Agent, new AgentResolveOptions { Version = 1 }, TestContext.Current.CancellationToken)).ShouldNotBeNull();
        var current = (await catalog.ResolveAsync(Agent, cancellationToken: TestContext.Current.CancellationToken)).ShouldNotBeNull();

        await pinned.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);
        await current.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        provider.Requests.Select(static request => request.Options!.Instructions).ShouldBe(["Version one speaks.", "Version two speaks."]);
    }

    [Fact]
    public async Task A_version_the_source_does_not_hold_is_reported_not_replaced_by_the_current_one()
    {
        var provider = Provider();
        var source = new VersionedInMemoryDefinitionSource().Add(Definition("Version one speaks."));
        await using var host = await StartAsync(provider, source);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(
                Agent,
                new AgentResolveOptions { Version = 9 },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("Version 9", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_source_with_no_history_refuses_a_pinned_resolve_instead_of_serving_its_current_definition()
    {
        var provider = Provider();
        var source = new InMemoryDefinitionSource().Add(Definition("The only version speaks."));
        await using var host = await StartAsync(provider, source);

        var failure = await Should.ThrowAsync<TraconException>(
            async () => await host.Services.GetRequiredService<IAgentCatalog>().ResolveAsync(
                Agent,
                new AgentResolveOptions { Version = 1 },
                TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("does not keep version history", Case.Sensitive);
        provider.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pinned_resolve_with_values_checks_them_against_the_pinned_versions_schema()
    {
        var provider = Provider();
        var source = new VersionedInMemoryDefinitionSource()
            .Add(Definition("One on {{topic}}.") with { Parameters = [Required("topic")] })
            .Add(Definition("Two on {{topic}} in {{tone}}.") with { Parameters = [Required("topic"), Required("tone")] });
        await using var host = await StartAsync(provider, source);

        var catalog = host.Services.GetRequiredService<IAgentCatalog>();
        var topicOnly = new Dictionary<string, string>(StringComparer.Ordinal) { ["topic"] = "tides" };

        var one = (await catalog.ResolveAsync(
            Agent,
            new AgentResolveOptions { Version = 1, Parameters = topicOnly },
            TestContext.Current.CancellationToken)).ShouldNotBeNull();

        await one.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        provider.Requests[^1].Options!.Instructions.ShouldBe("One on tides.");

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await catalog.ResolveAsync(
                Agent,
                new AgentResolveOptions { Version = 2, Parameters = topicOnly },
                TestContext.Current.CancellationToken));

        failure.MissingParameters.ShouldBe(["tone"]);
    }

    private static AgentParameter Required(string name) => new() { Name = name, Kind = AgentParameterKind.Text, Required = true };

    private static AgentDefinition Definition(string instructions) => new()
    {
        Name = Agent,
        Instructions = instructions,
        Model = new ModelBinding { Provider = ProviderName, Model = ModelName },
    };

    private static FakeModelProvider Provider()
        => new FakeModelProvider(ProviderName)
            .WithModel(new ModelDescriptor { Name = ModelName })
            .EchoesUserMessage();

    private static Task<TraconTestHost> StartAsync(FakeModelProvider provider, InMemoryDefinitionSource source)
        => TraconTestHost.StartAsync(
            builder =>
            {
                builder.AddModelProvider(provider);
                builder.AddAgentDefinitionSource(source);
            });
}
