using AgentPrism.Testing;
using AgentPrism.Testing.Contracts.AgentSources;
using Microsoft.Extensions.DependencyInjection;

namespace AgentPrism.Samples.CustomAgentSource.Tests;

/// <summary>
/// Runs AgentPrism's official agent-source contract against the sample source.
/// </summary>
/// <remarks>
/// The base class ships in the AgentPrism.Testing.Contracts.Xunit package; every
/// scenario it declares runs here without being redeclared. This is the whole claim
/// the sample exists to prove: an <see cref="IAgentSource"/> written outside the
/// repository can be verified with published artifacts only.
/// </remarks>
public sealed class JsonFileAgentSourceContractTests : AgentSourceContract
{
    protected override string KnownAgentName => "known-agent";

    protected override async ValueTask<IAgentSource> CreateSourceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("agentprism-json-source-contract-").FullName;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "known-agent.json"),
            """
            {
              "name": "known-agent",
              "instructions": "Reply briefly.",
              "model": { "provider": "fake", "model": "fake-model" }
            }
            """);

        var services = new ServiceCollection();
        var agentPrism = services.AddAgentPrism().AddModelProvider(new FakeModelProvider().EchoesUserMessage());
        agentPrism.Services.AddSingleton(new JsonFileAgentSourceOptions { Directory = directory });
        agentPrism.AddAgentSource<JsonFileAgentSource>();

        var provider = services.BuildServiceProvider();

        return provider.GetServices<IAgentSource>().OfType<JsonFileAgentSource>().Single();
    }
}
