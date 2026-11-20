using Microsoft.Extensions.DependencyInjection;
using Tracon.Testing;

namespace Tracon.Samples.CustomAgentSource.Tests;

/// <summary>
/// Completes a real agent run through the sample source, using only published
/// Tracon packages.
/// </summary>
/// <remarks>
/// The contract suite proves the source satisfies the rules <see cref="IAgentDefinitionSource"/>
/// states. This proves the other half: that a source registered with
/// <c>AddAgentDefinitionSource()</c> is actually reachable end to end — the catalog lists it,
/// carries <see cref="AgentDefinitionOrigin.Custom"/>, resolves it, binds the parameter values a
/// definition declares, and the run produces the model's own answer.
/// </remarks>
public sealed class JsonFileAgentDefinitionSourceRunTests
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
        tracon.Services.AddSingleton(new JsonFileAgentDefinitionSourceOptions { Directory = directory });
        tracon.AddAgentDefinitionSource<JsonFileAgentDefinitionSource>();

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentCatalog>();

        var descriptor = (await catalog.ListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        descriptor.Name.ShouldBe("greeter");
        descriptor.Origin.ShouldBe(AgentDefinitionOrigin.Custom);
        descriptor.SourceName.ShouldBe(JsonFileAgentDefinitionSource.SourceName);

        var agent = await catalog.ResolveAsync("greeter", cancellationToken: TestContext.Current.CancellationToken);
        agent.ShouldNotBeNull();

        var response = await agent.RunAsync("hello from the sample", cancellationToken: TestContext.Current.CancellationToken);

        response.Text.ShouldContain("hello from the sample");
    }

    [Fact]
    public async Task A_definition_that_declares_a_parameter_takes_its_value_when_the_agent_is_resolved()
    {
        // The case a source that builds its own agent cannot serve: the definition names a placeholder, the
        // caller supplies the value, and the model's instructions carry it. The value is also recorded, so a
        // replay of the run repeats it.
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-params-").FullName;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "summarizer.json"),
            """
            {
              "name": "summarizer",
              "instructions": "Summarize this article: {{articleContent}}",
              "model": { "provider": "fake", "model": "fake-model" },
              "parameters": [ { "name": "articleContent", "kind": "Text", "required": true } ]
            }
            """);

        var fake = new FakeModelProvider().EchoesUserMessage();
        var services = new ServiceCollection();
        var tracon = services.AddTracon().AddModelProvider(fake);
        tracon.Services.AddSingleton(new JsonFileAgentDefinitionSourceOptions { Directory = directory });
        tracon.AddAgentDefinitionSource<JsonFileAgentDefinitionSource>();

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentCatalog>();

        var agent = await catalog.ResolveAsync(
            "summarizer",
            new AgentResolveOptions
            {
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["articleContent"] = "Tides follow the moon." },
            },
            TestContext.Current.CancellationToken);

        agent.ShouldNotBeNull();

        await agent.RunAsync("go", cancellationToken: TestContext.Current.CancellationToken);

        fake.Requests[^1].Options!.Instructions.ShouldBe("Summarize this article: Tides follow the moon.");

        var run = (await provider.GetRequiredService<IRunStore>().QueryRunsAsync(new RunQuery(), TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        var input = await provider.GetRequiredService<IRunInputStore>().GetAsync(
            provider.GetRequiredService<ITenantContext>().TenantId,
            run.Id,
            TestContext.Current.CancellationToken);

        input.ShouldNotBeNull().Parameters.ShouldNotBeNull().ShouldContainKeyAndValue("articleContent", "Tides follow the moon.");
    }

    [Fact]
    public async Task A_value_for_a_name_the_definition_does_not_declare_is_refused_before_the_model()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-unknown-").FullName;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "greeter.json"),
            """
            {
              "name": "greeter",
              "instructions": "Reply briefly.",
              "model": { "provider": "fake", "model": "fake-model" }
            }
            """);

        var fake = new FakeModelProvider().EchoesUserMessage();
        var services = new ServiceCollection();
        var tracon = services.AddTracon().AddModelProvider(fake);
        tracon.AddAgentDefinitionSource(new JsonFileAgentDefinitionSource(new JsonFileAgentDefinitionSourceOptions { Directory = directory }));

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentCatalog>();

        var failure = await Should.ThrowAsync<TraconParameterException>(
            async () => await catalog.ResolveAsync(
                "greeter",
                new AgentResolveOptions { Parameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["anything"] = "x" } },
                TestContext.Current.CancellationToken));

        failure.UnknownParameters.ShouldBe(["anything"]);
        fake.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_descriptor_names_the_extensions_the_definition_names()
    {
        // The catalog shows a source agent's extensions from its descriptor. Tracon builds the descriptor from the
        // definition now, so a field the sample once forgot to copy cannot be forgotten again.
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-extensions-").FullName;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "audited.json"),
            """
            {
              "name": "audited",
              "instructions": "Reply briefly.",
              "model": { "provider": "fake", "model": "fake-model" },
              "extensionNames": ["audit-trail"]
            }
            """);

        var services = new ServiceCollection();
        var tracon = services.AddTracon().AddModelProvider(new FakeModelProvider().EchoesUserMessage());
        tracon.Services.AddSingleton(new JsonFileAgentDefinitionSourceOptions { Directory = directory });
        tracon.AddAgentDefinitionSource<JsonFileAgentDefinitionSource>();

        await using var provider = services.BuildServiceProvider();

        var descriptor = (await provider.GetRequiredService<IAgentCatalog>().ListAsync(TestContext.Current.CancellationToken)).ShouldHaveSingleItem();

        descriptor.ExtensionNames.ShouldBe(["audit-trail"]);
    }

    [Fact]
    public async Task An_unknown_agent_name_resolves_to_null_instead_of_throwing()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-run-empty-").FullName;

        var services = new ServiceCollection();
        var tracon = services.AddTracon().AddModelProvider(new FakeModelProvider().EchoesUserMessage());

        // The factory overload: useful when the source needs something (like a directory
        // path) that does not belong in the container as its own singleton.
        tracon.AddAgentDefinitionSource(_ => new JsonFileAgentDefinitionSource(
            new JsonFileAgentDefinitionSourceOptions { Directory = directory }));

        await using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IAgentCatalog>();

        var agent = await catalog.ResolveAsync("no-such-agent", cancellationToken: TestContext.Current.CancellationToken);

        agent.ShouldBeNull();
    }
}
