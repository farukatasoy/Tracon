using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// The compiled agent cache holds at most <see cref="TraconAgentCacheOptions.MaxEntries"/> entries and drops one
/// that is not used for <see cref="TraconAgentCacheOptions.IdleLifetime"/>.
/// </summary>
/// <remarks>
/// <para>
/// An installation that accepts any tenant identifier lets the caller choose how many entries the cache holds,
/// and a compiled agent weighs between 8 KiB and 42 KiB: before the bound, the cache grew with the number of
/// tenants and never gave anything back.
/// </para>
/// <para>
/// An entry that leaves the cache is only unlinked. A run that holds the agent keeps using it, so a test that
/// drops an entry also proves the agent was not disposed under that run.
/// </para>
/// </remarks>
public sealed class CompiledAgentCacheBoundTests
{
    private const string Fingerprint = "FINGERPRINT-1";

    private static CompiledAgentCache Create(int maxEntries, TimeSpan? idleLifetime = null, ManualClock? clock = null)
        => new(
            new TraconAgentCacheOptions { MaxEntries = maxEntries, IdleLifetime = idleLifetime ?? TimeSpan.FromHours(1) },
            clock ?? new ManualClock());

    private static AIAgent Resolve(CompiledAgentCache cache, string tenant, string name = "support", string dependencies = "")
        => cache.GetOrAdd(tenant, name, Fingerprint, dependencies, string.Empty, () => new TaggedAgent($"{tenant}/{name}/{dependencies}"));

    private static bool IsCached(CompiledAgentCache cache, string tenant, string name = "support", string dependencies = "")
    {
        var compiled = false;

        cache.GetOrAdd(tenant, name, Fingerprint, dependencies, string.Empty, () =>
        {
            compiled = true;
            return new TaggedAgent($"{tenant}/{name}/{dependencies}");
        });

        return !compiled;
    }

    [Fact]
    public void The_count_never_passes_the_limit_however_many_tenants_ask()
    {
        var cache = Create(maxEntries: 100);

        for (var tenant = 0; tenant < 5_000; tenant++)
        {
            Resolve(cache, $"tenant-{tenant}");

            cache.Count.ShouldBeLessThanOrEqualTo(100, $"after tenant {tenant}");
        }

        cache.Count.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task The_asynchronous_path_is_bounded_too()
    {
        var cache = Create(maxEntries: 20);

        for (var tenant = 0; tenant < 500; tenant++)
        {
            var name = $"tenant-{tenant}";
            await cache.GetOrAddAsync(name, "support", Fingerprint, string.Empty, () => ValueTask.FromResult<AIAgent>(new TaggedAgent(name)));
        }

        cache.Count.ShouldBeLessThanOrEqualTo(20);
    }

    [Fact]
    public void A_full_cache_drops_to_a_tenth_below_the_limit_so_one_scan_serves_many_stores()
    {
        var cache = Create(maxEntries: 100);

        for (var tenant = 0; tenant < 100; tenant++)
        {
            Resolve(cache, $"tenant-{tenant}");
        }

        cache.Count.ShouldBe(100);

        Resolve(cache, "tenant-100");

        cache.Count.ShouldBe(91, "ten entries make room, then the new one is stored");
    }

    [Fact]
    public void The_entries_used_least_recently_are_the_ones_dropped()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 10, clock: clock);

        for (var tenant = 0; tenant < 10; tenant++)
        {
            Resolve(cache, $"tenant-{tenant}");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // The oldest entry is used again, so it is no longer the one used least recently.
        Resolve(cache, "tenant-0");
        clock.Advance(TimeSpan.FromSeconds(1));

        Resolve(cache, "tenant-10");

        // A full cache of ten makes room for the new entry by keeping nine: the one dropped is the oldest
        // entry that nobody used again.
        IsCached(cache, "tenant-0").ShouldBeTrue("a use moves an entry to the front");
        IsCached(cache, "tenant-2").ShouldBeTrue();
        IsCached(cache, "tenant-9").ShouldBeTrue();
        IsCached(cache, "tenant-10").ShouldBeTrue();
        IsCached(cache, "tenant-1").ShouldBeFalse("the oldest entry that nobody used again is dropped first");
    }

    [Fact]
    public void Several_entries_are_dropped_in_the_order_they_were_last_used()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 20, clock: clock);

        for (var tenant = 0; tenant < 20; tenant++)
        {
            Resolve(cache, $"tenant-{tenant}");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // The first four entries are used again, newest last: tenant-4 is now the oldest.
        for (var tenant = 0; tenant < 4; tenant++)
        {
            Resolve(cache, $"tenant-{tenant}");
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        Resolve(cache, "tenant-20");

        // Keeping eighteen of twenty drops tenant-4 and tenant-5, the two used least recently.
        cache.Count.ShouldBe(19);

        foreach (var tenant in new[] { 0, 1, 2, 3, 6, 19, 20 })
        {
            IsCached(cache, $"tenant-{tenant}").ShouldBeTrue($"tenant-{tenant}");
        }

        IsCached(cache, "tenant-4").ShouldBeFalse();
    }

    [Fact]
    public void The_entries_dropped_are_the_ones_a_full_sort_by_use_time_and_then_store_order_would_drop()
    {
        // Many entries share a use time (the clock often does not move between two uses), so the order of storing
        // decides among them. The expectation is a plain sort over the same bookkeeping the cache is meant to keep.
        for (var seed = 0; seed < 20; seed++)
        {
            var random = new Random(seed);
            var clock = new ManualClock();
            var cache = Create(maxEntries: 60, clock: clock);
            var lastUsed = new Dictionary<string, (long Use, int Stored)>(StringComparer.Ordinal);

            for (var tenant = 0; tenant < 60; tenant++)
            {
                var name = $"tenant-{tenant}";

                Resolve(cache, name);
                lastUsed[name] = (clock.GetTimestamp(), tenant);
                clock.Advance(TimeSpan.FromSeconds(random.Next(0, 3)));
            }

            for (var use = 0; use < 40; use++)
            {
                var name = $"tenant-{random.Next(0, 60)}";

                Resolve(cache, name);
                lastUsed[name] = (clock.GetTimestamp(), lastUsed[name].Stored);
                clock.Advance(TimeSpan.FromSeconds(random.Next(0, 3)));
            }

            // Keeping fifty-four of sixty drops six: the six with the oldest use, the earliest store among equals.
            var dropped = lastUsed
                .OrderBy(static pair => pair.Value.Use)
                .ThenBy(static pair => pair.Value.Stored)
                .Take(6)
                .Select(static pair => pair.Key)
                .ToList();

            Resolve(cache, "newcomer");

            cache.Count.ShouldBe(55, $"seed {seed}");

            // A miss stores the entry it asks for, which can drop others: the entries that stayed are asked first.
            foreach (var name in lastUsed.Keys.Except(dropped, StringComparer.Ordinal))
            {
                IsCached(cache, name).ShouldBeTrue($"seed {seed}, {name} stayed");
            }

            foreach (var name in dropped)
            {
                IsCached(cache, name).ShouldBeFalse($"seed {seed}, {name} was dropped");
            }
        }
    }

    [Fact]
    public void The_entry_just_stored_is_never_the_one_dropped_even_when_every_entry_is_equally_old()
    {
        // A clock that does not move gives every entry the same age: nothing but the order of storing
        // distinguishes them, and the entry the caller is about to use must not be among the ones dropped.
        var cache = Create(maxEntries: 1);

        var first = Resolve(cache, "tenant-a");
        var second = Resolve(cache, "tenant-b");

        cache.Count.ShouldBe(1);
        IsCached(cache, "tenant-b").ShouldBeTrue();
        second.ShouldNotBeSameAs(first);
    }

    [Fact]
    public void An_entry_that_stays_idle_for_the_whole_lifetime_is_dropped_when_the_cache_is_next_used()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 1_000, idleLifetime: TimeSpan.FromMinutes(10), clock: clock);

        Resolve(cache, "tenant-a");
        Resolve(cache, "tenant-b");
        Resolve(cache, "tenant-c");

        clock.Advance(TimeSpan.FromMinutes(11));

        // A hit on one entry is enough: the cache sweeps while it is in use, with no timer of its own.
        Resolve(cache, "tenant-c");

        cache.Count.ShouldBe(1);
        IsCached(cache, "tenant-c").ShouldBeTrue();
        IsCached(cache, "tenant-a").ShouldBeFalse();
    }

    [Fact]
    public void A_miss_sweeps_the_idle_entries_too()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 1_000, idleLifetime: TimeSpan.FromMinutes(10), clock: clock);

        Resolve(cache, "tenant-a");
        Resolve(cache, "tenant-b");

        clock.Advance(TimeSpan.FromMinutes(11));
        Resolve(cache, "tenant-new");

        cache.Count.ShouldBe(1);
    }

    [Fact]
    public void A_use_starts_the_idle_time_again()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 1_000, idleLifetime: TimeSpan.FromMinutes(10), clock: clock);

        Resolve(cache, "tenant-a");
        clock.Advance(TimeSpan.FromMinutes(6));
        Resolve(cache, "tenant-a");
        clock.Advance(TimeSpan.FromMinutes(6));

        // Twelve minutes since the first store but six since the last use: the entry stays.
        Resolve(cache, "tenant-b");

        IsCached(cache, "tenant-a").ShouldBeTrue();
        cache.Count.ShouldBe(2);
    }

    [Fact]
    public void An_entry_is_not_dropped_before_its_lifetime_has_passed()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 1_000, idleLifetime: TimeSpan.FromMinutes(10), clock: clock);

        Resolve(cache, "tenant-a");
        clock.Advance(TimeSpan.FromMinutes(9));
        Resolve(cache, "tenant-b");
        clock.Advance(TimeSpan.FromMinutes(9));
        Resolve(cache, "tenant-c");

        // tenant-a: 18 minutes idle, dropped. tenant-b: 9 minutes idle, kept.
        IsCached(cache, "tenant-b").ShouldBeTrue();
        IsCached(cache, "tenant-a").ShouldBeFalse();
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("idle")]
    [InlineData("clear")]
    [InlineData("replaced")]
    public void An_entry_that_leaves_the_cache_is_not_disposed_and_the_run_that_holds_it_keeps_a_working_agent(string how)
    {
        var clock = new ManualClock();

        // A cache with room, so the idle case leaves by its lifetime and by nothing else.
        var cache = Create(maxEntries: string.Equals(how, "idle", StringComparison.Ordinal) ? 100 : 1, idleLifetime: TimeSpan.FromMinutes(10), clock: clock);

        var held = (TaggedAgent)Resolve(cache, "tenant-a");

        switch (how)
        {
            case "limit":
                Resolve(cache, "tenant-b");
                break;
            case "idle":
                clock.Advance(TimeSpan.FromMinutes(11));
                Resolve(cache, "tenant-b");
                break;
            case "clear":
                cache.Clear();
                break;
            case "replaced":
                Resolve(cache, "tenant-a", dependencies: "other-dependencies");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(how), how, null);
        }

        // The entry is gone: the next resolve compiles another agent. The run that holds the first one is
        // untouched, so a dispose here would have been a dispose under a running request.
        IsCached(cache, "tenant-a").ShouldBeFalse();
        held.DisposeCount.ShouldBe(0);
        held.Tag.ShouldBe("tenant-a/support/");
    }

    [Fact]
    public void A_key_never_serves_another_tenants_agent_after_eviction_and_compiling_again()
    {
        var clock = new ManualClock();
        var cache = Create(maxEntries: 10, clock: clock);
        var random = new Random(20261104);

        for (var step = 0; step < 4_000; step++)
        {
            var tenant = $"tenant-{random.Next(60)}";
            var name = $"agent-{random.Next(3)}";

            // Every tenant has the same agent names and the same content: only the tenant tells them apart.
            var agent = (TaggedAgent)Resolve(cache, tenant, name);

            agent.Tag.ShouldBe($"{tenant}/{name}/");
            clock.Advance(TimeSpan.FromSeconds(1));
        }
    }

    [Fact]
    public void A_changed_dependency_fingerprint_wins_over_an_entry_that_was_dropped_and_over_one_that_was_not()
    {
        var cache = Create(maxEntries: 1);

        var original = Resolve(cache, "tenant-a", dependencies: "before");
        Resolve(cache, "tenant-b");

        // The slot of tenant-a was dropped. The same definition now carries a new dependency fingerprint, and
        // the only agent that may answer is one compiled for it.
        var after = (TaggedAgent)Resolve(cache, "tenant-a", dependencies: "after");

        after.Tag.ShouldBe("tenant-a/support/after");
        after.ShouldNotBeSameAs(original);

        // The old fingerprint was replaced, not kept beside the new one.
        IsCached(cache, "tenant-a", dependencies: "before").ShouldBeFalse();
    }

    [Fact]
    public void A_factory_that_throws_leaves_nothing_behind()
    {
        var cache = Create(maxEntries: 10);

        Should.Throw<InvalidOperationException>(() => cache.GetOrAdd(
            "tenant-a",
            "support",
            Fingerprint,
            () => throw new InvalidOperationException("compile failed")));

        cache.Count.ShouldBe(0);
    }

    [Fact]
    public void The_defaults_hold_a_single_tenant_installation_without_dropping_anything()
    {
        var clock = new ManualClock();
        var cache = new CompiledAgentCache(new TraconAgentCacheOptions(), clock);

        // 100 agents, three cultures, three live versions of each: far more than a single tenant keeps.
        for (var agent = 0; agent < 100; agent++)
        {
            foreach (var culture in new[] { "en", "tr", "de" })
            {
                for (var version = 0; version < 3; version++)
                {
                    cache.GetOrAdd("default", $"agent-{agent}", $"fp-{version}", string.Empty, culture, () => new TaggedAgent("x"));
                }
            }
        }

        cache.Count.ShouldBe(900);

        // And an agent used at least once an hour is never compiled again.
        clock.Advance(TimeSpan.FromMinutes(50));
        cache.GetOrAdd("default", "agent-0", "fp-0", string.Empty, "en", () => throw new InvalidOperationException("dropped"));
    }
}
