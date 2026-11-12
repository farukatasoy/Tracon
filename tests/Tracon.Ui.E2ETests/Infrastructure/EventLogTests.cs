namespace Tracon.Ui.E2ETests.Infrastructure;

/// <summary>What the page's evidence says about the events it did not keep.</summary>
public sealed class EventLogTests
{
    [Fact]
    public void Events_up_to_the_limit_are_kept_in_order_and_nothing_is_said_about_dropping()
    {
        var log = new EventLog(limit: 3);

        log.Record("one");
        log.Record("two");
        log.Record("three");

        log.Lines().ShouldBe(["one", "two", "three"]);
    }

    [Fact]
    public void Events_past_the_limit_are_counted_in_a_last_line_instead_of_stopping_without_a_word()
    {
        var log = new EventLog(limit: 2);

        foreach (var number in Enumerable.Range(1, 5))
        {
            log.Record($"event {number}");
        }

        log.Lines().ShouldBe(["event 1", "event 2", "3 more events were dropped after the first 2"]);
    }

    [Fact]
    public async Task Events_recorded_from_many_threads_are_all_either_kept_or_counted()
    {
        var log = new EventLog(limit: 10);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(thread => Task.Run(() =>
        {
            for (var index = 0; index < 100; index++)
            {
                log.Record($"{thread}:{index}");
            }
        }, TestContext.Current.CancellationToken)));

        var lines = log.Lines();

        lines.Count.ShouldBe(11);
        lines[^1].ShouldBe("790 more events were dropped after the first 10");
    }
}
