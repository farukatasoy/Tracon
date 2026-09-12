using Tracon.Samples.FileRunStore;
using Tracon.Testing.Contracts.Storage;

namespace Tracon.Samples.FileRunStore.Tests;

/// <summary>
/// Runs every <see cref="RunStoreContract"/> scenario against
/// <see cref="JsonFileRunStore"/> -- the hardest seam <c>IRunStore</c> offers
/// (15 methods, statistics computed inside the store, tenant isolation,
/// event ordering) -- proving a third party can implement it correctly using
/// nothing but the published <c>Tracon.Abstractions</c> package.
/// </summary>
public sealed class JsonFileRunStoreContractTests : RunStoreContract, IDisposable
{
    private readonly string _tempDirectory = Directory.CreateTempSubdirectory("tracon-file-run-store-").FullName;

    private JsonFileRunStore? _store;

    /// <inheritdoc />
    protected override ValueTask<IRunStore> CreateStoreAsync()
    {
        _store = new JsonFileRunStore(
            Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.json"),
            tenantContext: AmbientTenant);

        return new(_store);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The store's own default score store, so the contract writes into the
    /// same backend the run store reads from.
    /// </remarks>
    protected override ValueTask<IRunScoreStore?> CreateScoreStoreAsync()
        => ValueTask.FromResult<IRunScoreStore?>(_store!.Scores);

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
