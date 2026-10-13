using Tracon.Samples.FileRunStore;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Samples.FileRunStore.Tests;

/// <summary>
/// Runs every <see cref="NonTokenUsageContract"/> scenario against
/// <see cref="JsonFileRunStore"/>: the items a workflow function node reports,
/// written and read back through the public <c>IRunStore</c> members alone.
/// </summary>
public sealed class JsonFileNonTokenUsageContractTests : NonTokenUsageContract, IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("tracon-file-run-store-").FullName;

    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
        => ValueTask.FromResult<IRunStore>(new JsonFileRunStore(
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
