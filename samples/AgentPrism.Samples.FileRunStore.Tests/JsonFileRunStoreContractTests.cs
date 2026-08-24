using AgentPrism.Samples.FileRunStore;
using AgentPrism.Testing.Contracts.Storage;

namespace AgentPrism.Samples.FileRunStore.Tests;

/// <summary>
/// Runs every <see cref="RunStoreContract"/> scenario against
/// <see cref="JsonFileRunStore"/> -- the hardest seam <c>IRunStore</c> offers
/// (15 methods, statistics computed inside the store, tenant isolation,
/// event ordering) -- proving a third party can implement it correctly using
/// nothing but the published <c>AgentPrism.Abstractions</c> package.
/// </summary>
public sealed class JsonFileRunStoreContractTests : RunStoreContract, IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("agentprism-file-run-store-").FullName;

    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => new(new JsonFileRunStore(
            Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.json"),
            tenantContext: AmbientTenant));

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: a lingering handle on a slow CI filesystem must
            // not fail the test that already passed or failed on its own merits.
        }
    }
}
