namespace Tracon.Core.UnitTests.Diagnostics;

/// <summary>
/// The set behind a "warn once per tenant" log line stops growing at its capacity and says so once.
/// </summary>
/// <remarks>
/// The tenant identifier comes from a request. A set that only grows lets a caller grow it by one entry for
/// every identifier it made up, so the audit log's key-off warning (one line per tenant, for the life of the
/// process) takes a bounded number of tenants and then reports that names were left out.
/// </remarks>
public sealed class WarnedKeySetTests
{
    [Fact]
    public void The_set_stops_taking_keys_at_its_capacity_whatever_the_number_of_tenants()
    {
        var set = new WarnedKeySet(capacity: 100);

        for (var tenant = 0; tenant < 5_000; tenant++)
        {
            set.Admit($"tenant-{tenant}");
        }

        set.Count.ShouldBe(100);
    }

    [Fact]
    public void A_key_is_named_once_and_a_known_key_is_never_named_again()
    {
        var set = new WarnedKeySet(capacity: 10);

        set.Admit("tenant-a").ShouldBe(WarnedKeyAdmission.New);
        set.Admit("tenant-a").ShouldBe(WarnedKeyAdmission.Known);
        set.Admit("tenant-b").ShouldBe(WarnedKeyAdmission.New);
        set.Admit("tenant-a").ShouldBe(WarnedKeyAdmission.Known);
    }

    [Fact]
    public void A_full_set_says_so_once_and_then_stays_quiet_for_keys_it_turns_away()
    {
        var set = new WarnedKeySet(capacity: 2);

        set.Admit("tenant-a").ShouldBe(WarnedKeyAdmission.New);
        set.Admit("tenant-b").ShouldBe(WarnedKeyAdmission.New);

        set.Admit("tenant-c").ShouldBe(WarnedKeyAdmission.FullFirstTime);
        set.Admit("tenant-d").ShouldBe(WarnedKeyAdmission.Full);
        set.Admit("tenant-c").ShouldBe(WarnedKeyAdmission.Full);

        // A key the set holds is still known when the set is full.
        set.Admit("tenant-a").ShouldBe(WarnedKeyAdmission.Known);
    }

    [Fact]
    public async Task Callers_that_pass_the_check_together_take_the_set_only_a_few_entries_over_the_capacity()
    {
        const int capacity = 64;
        const int callers = 8;
        var set = new WarnedKeySet(capacity);
        var named = 0;
        var fullFirstTime = 0;

        var workers = Enumerable.Range(0, callers).Select(worker => Task.Run(() =>
        {
            for (var tenant = 0; tenant < 2_000; tenant++)
            {
                switch (set.Admit($"tenant-{worker}-{tenant}"))
                {
                    case WarnedKeyAdmission.New:
                        Interlocked.Increment(ref named);
                        break;
                    case WarnedKeyAdmission.FullFirstTime:
                        Interlocked.Increment(ref fullFirstTime);
                        break;
                    case WarnedKeyAdmission.Known:
                    case WarnedKeyAdmission.Full:
                    default:
                        break;
                }
            }
        })).ToArray();

        await Task.WhenAll(workers);

        named.ShouldBeGreaterThanOrEqualTo(capacity);
        set.Count.ShouldBeLessThanOrEqualTo(capacity + callers);
        fullFirstTime.ShouldBe(1, "the line that says names were left out is written once");
    }
}
