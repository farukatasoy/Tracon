namespace Tracon.Core.UnitTests.Fakes;

/// <summary>
/// The race harness fails a run that removed nothing, and reports what it removed and what came back.
/// </summary>
public sealed class StoreDeleteRaceTests
{
    [Fact]
    public async Task A_run_in_which_no_delete_returns_true_removed_nothing_and_is_refused_as_proving_nothing()
    {
        var outcome = await StoreDeleteRace.RunAsync(
            write: () => Task.CompletedTask,
            list: () => Task.FromResult<IReadOnlyList<Guid>>([Guid.NewGuid()]),
            delete: _ => Task.FromResult(false));

        outcome.Removed.ShouldBe(0);
        outcome.Reappeared.ShouldBeEmpty();
        Should.Throw<ShouldAssertException>(() => StoreDeleteRace.Assert(outcome))
            .Message.ShouldContain("proved nothing");
    }

    [Fact]
    public async Task A_store_that_hands_back_a_removed_entry_is_reported_with_its_identifier()
    {
        var entry = Guid.NewGuid();

        // The entry is always listed and a delete always says it removed it: after the first delete it is "back".
        var outcome = await StoreDeleteRace.RunAsync(
            write: () => Task.CompletedTask,
            list: () => Task.FromResult<IReadOnlyList<Guid>>([entry]),
            delete: _ => Task.FromResult(true));

        outcome.Removed.ShouldBe(1);
        outcome.Reappeared.ShouldBe([entry]);
        Should.Throw<ShouldAssertException>(() => StoreDeleteRace.Assert(outcome))
            .Message.ShouldContain(entry.ToString());
    }

    [Fact]
    public async Task A_store_that_keeps_a_deleted_entry_deleted_passes()
    {
        var present = new System.Collections.Concurrent.ConcurrentDictionary<Guid, bool>();

        var outcome = await StoreDeleteRace.RunAsync(
            write: () =>
            {
                present[Guid.NewGuid()] = true;

                return Task.CompletedTask;
            },
            list: () => Task.FromResult<IReadOnlyList<Guid>>([.. present.Keys]),
            delete: id => Task.FromResult(present.TryRemove(id, out _)));

        outcome.Removed.ShouldBeGreaterThan(0);
        StoreDeleteRace.Assert(outcome);
    }
}
