using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Storage;

/// <summary>
/// An entry that a delete removed stays removed when a write that finds the entry runs at the same moment. One case for
/// each in-memory store whose write finds the entry that is there and writes it back under the same identifier.
/// </summary>
/// <remarks>
/// <see cref="StoreDeleteRace"/> runs the write against the delete and says why no serial order gives an entry back
/// after a delete of it returned <see langword="true"/>.
/// </remarks>
public sealed class InMemoryStoreDeleteRaceTests
{
    private const string Tenant = "acme";

    [Fact]
    public async Task A_score_whose_delete_returned_true_is_not_listed_again_by_a_score_of_its_target_running_at_the_same_time()
    {
        var store = new InMemoryRunScoreStore();
        var runId = Guid.NewGuid();
        var cancellationToken = TestContext.Current.CancellationToken;

        var reappeared = await StoreDeleteRace.RunAsync(
            write: async () => await store.UpsertAsync(Score(runId, Guid.NewGuid()), cancellationToken),
            list: async () => [.. (await store.ListAsync(Tenant, runId, cancellationToken)).Select(static score => score.Id)],
            delete: async id => await store.DeleteAsync(Tenant, id, cancellationToken));

        reappeared.ShouldBeEmpty(StoreDeleteRace.Describe(reappeared));
    }

    [Fact]
    public async Task An_mcp_server_whose_delete_returned_true_is_not_listed_again_by_a_save_of_its_name_running_at_the_same_time()
    {
        var store = new InMemoryMcpServerStore();
        var cancellationToken = TestContext.Current.CancellationToken;

        var reappeared = await StoreDeleteRace.RunAsync(
            write: async () => await store.SaveAsync(Server(Guid.NewGuid()), cancellationToken),
            list: async () => [.. (await store.ListAsync(Tenant, cancellationToken)).Select(static server => server.Id)],
            delete: async _ => await store.DeleteAsync(Tenant, "search", cancellationToken));

        reappeared.ShouldBeEmpty(StoreDeleteRace.Describe(reappeared));
    }

    [Fact]
    public async Task A_tenant_whose_delete_returned_true_is_not_listed_again_by_a_save_of_its_slug_running_at_the_same_time()
    {
        var store = new InMemoryTenantStore();
        var cancellationToken = TestContext.Current.CancellationToken;

        var reappeared = await StoreDeleteRace.RunAsync(
            write: async () => await store.SaveAsync(Descriptor(Guid.NewGuid()), cancellationToken),
            list: async () => [.. (await store.ListAsync(cancellationToken)).Select(static tenant => tenant.Id)],
            delete: async _ => await store.DeleteAsync(Tenant, cancellationToken));

        reappeared.ShouldBeEmpty(StoreDeleteRace.Describe(reappeared));
    }

    /// <summary>
    /// A draft goes through save, start and stop on one thread while the other deletes whatever is listed. A start that
    /// has read the draft and not yet written the running experiment must not have the draft deleted from under it:
    /// delete then start fails for a missing experiment, and start then delete is refused for a running one.
    /// </summary>
    [Fact]
    public async Task An_experiment_whose_delete_returned_true_is_not_listed_again_by_a_start_running_at_the_same_time()
    {
        var store = new InMemoryExperimentStore();
        var cancellationToken = TestContext.Current.CancellationToken;

        var reappeared = await StoreDeleteRace.RunAsync(
            write: async () =>
            {
                await RefusedAsync(async () => await store.SaveAsync(Draft(Guid.NewGuid()), cancellationToken));
                await RefusedAsync(async () => await store.StartAsync(Tenant, "checkout", cancellationToken));
                await RefusedAsync(async () => await store.StopAsync(Tenant, "checkout", cancellationToken));
            },
            list: async () => [.. (await store.ListAsync(Tenant, cancellationToken)).Select(static experiment => experiment.Id)],
            delete: async _ => await DeletedAsync(store, cancellationToken));

        reappeared.ShouldBeEmpty(StoreDeleteRace.Describe(reappeared));
    }

    // The steps of one experiment are refused in every order but the right one, as they must be.
    private static async Task RefusedAsync(Func<Task> step)
    {
        try
        {
            await step();
        }
        catch (TraconException)
        {
            // Not in the status this step needs, or no longer there: the other thread's call came first.
        }
    }

    // A running experiment cannot be deleted; the delete says so by throwing, and nothing was removed.
    private static async Task<bool> DeletedAsync(InMemoryExperimentStore store, CancellationToken cancellationToken)
    {
        try
        {
            return await store.DeleteAsync(Tenant, "checkout", cancellationToken);
        }
        catch (TraconException)
        {
            return false;
        }
    }

    private static RunScore Score(Guid runId, Guid id)
        => new()
        {
            Id = id,
            TenantId = Tenant,
            RunId = runId,
            Name = "overall",
            Kind = RunScoreKind.Binary,
            Value = 1,
            Source = "human",
            Author = "reviewer",
        };

    private static McpServerDefinition Server(Guid id)
        => new()
        {
            Id = id,
            TenantId = Tenant,
            Name = "search",
            Endpoint = new Uri("https://mcp.example.com/search"),
        };

    private static TenantDescriptor Descriptor(Guid id)
        => new()
        {
            Id = id,
            Slug = Tenant,
            DisplayName = "Acme",
        };

    private static Experiment Draft(Guid id)
        => new()
        {
            Id = id,
            TenantId = Tenant,
            Name = "checkout",
            AgentName = "support",
            Variants =
            [
                new ExperimentVariant { Name = "control", Version = 1, Weight = 50 },
                new ExperimentVariant { Name = "candidate", Version = 2, Weight = 50 },
            ],
        };
}
