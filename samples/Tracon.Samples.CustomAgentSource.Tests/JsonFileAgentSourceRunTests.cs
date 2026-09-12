using Tracon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tracon.Samples.CustomAgentSource.Tests;

/// <summary>
/// Completes a real agent run through the sample source, using only published
/// Tracon packages.
/// </summary>
/// <remarks>
/// The contract suite proves the source satisfies the rules <see cref="IAgentSource"/>
/// states. This proves the other half: that a source registered with
/// <c>AddAgentSource()</c> is actually reachable end to end — the catalog lists it,
/// carries <see cref="AgentDefinitionOrigin.Custom"/>, resolves it, and the run
/// produces the model's own answer.
/// </remarks>
public sealed class JsonFileAgentSourceRunTests
{
    [Fact]
    public async Task An_agent_from_the_json_file_source_appears_in_the_catalog_as_Custom_origin_and_completes_a_run()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-run-").FullName;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "greeter.json"),
            """
            {
              "name": "greeter",
              "instructions": "Reply briefly.",
              "model": { "provider": "fake", "model": "fake-model" }
            }
            """);

        var services = new ServiceCollection();
        var tracon = services.AddTracon().AddModelProvider(new FakeModelProvider().EchoesUserMessage());

        // The generic overload: the source's own settings are registered like any other
        // Tracon-managed service, and the container builds the source itself.
        tracon.Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = directory });
        tracon.AddAgentSource<JsonFileAgentSource>();

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentCatalog>();

        var descriptor = (await catalog.ListAsync()).ShouldHaveSingleItem();
        descriptor.Name.ShouldBe("greeter");
        descriptor.Origin.ShouldBe(AgentDefinitionOrigin.Custom);
        descriptor.SourceName.ShouldBe(JsonFileAgentSource.SourceName);

        var agent = await catalog.ResolveAsync("greeter", culture: null, TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        var response = await agent.RunAsync("hello from the sample");

        response.Text.ShouldContain("hello from the sample");
    }

    [Fact]
    public async Task An_unknown_agent_name_resolves_to_null_instead_of_throwing()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-run-empty-").FullName;

        var services = new ServiceCollection();
        var tracon = services.AddTracon().AddModelProvider(new FakeModelProvider().EchoesUserMessage());

        // The factory overload: useful when the source needs something (like a directory
        // path) that does not belong in the container as its own singleton.
        tracon.AddAgentSource(sp => new JsonFileAgentSource(
            new JsonFileAgentSourceOptions { Directory = directory },
            sp.GetRequiredService<AgentDefinitionCompiler>(),
            sp.GetRequiredService<CompiledAgentCache>(),
            sp.GetRequiredService<ITenantContext>()));

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentCatalog>();

        var agent = await catalog.ResolveAsync("no-such-agent", culture: null, TestContext.Current.CancellationToken);

        agent.ShouldBeNull();
    }
}
