using Tracon.Testing.Contracts.AgentSources;

namespace Tracon.Samples.CustomAgentSource.Tests;

/// <summary>
/// Runs Tracon's official definition-source contract against the sample source.
/// </summary>
/// <remarks>
/// The base class ships in the Tracon.Testing.Contracts.Xunit package; every
/// scenario it declares runs here without being redeclared. This is the whole claim
/// the sample exists to prove: an <see cref="IAgentDefinitionSource"/> written outside the
/// repository can be verified with published artifacts only.
/// </remarks>
public sealed class JsonFileAgentDefinitionSourceContractTests : AgentDefinitionSourceContract
{
    protected override string KnownAgentName => "known-agent";

    protected override async ValueTask<IAgentDefinitionSource> CreateSourceAsync()
    {
        var directory = Directory.CreateTempSubdirectory("tracon-json-source-contract-").FullName;

        await File.WriteAllTextAsync(
            Path.Combine(directory, "known-agent.json"),
            """
            {
              "name": "known-agent",
              "instructions": "Reply briefly.",
              "model": { "provider": "fake", "model": "fake-model" }
            }
            """);

        return new JsonFileAgentDefinitionSource(new JsonFileAgentDefinitionSourceOptions { Directory = directory });
    }
}
