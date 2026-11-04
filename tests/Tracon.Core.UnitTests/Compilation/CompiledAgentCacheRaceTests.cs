using System.Collections.Concurrent;
using CsCheck;
using Microsoft.Agents.AI;
using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Compilation;

/// <summary>
/// Resolving, dropping and clearing at the same time never hands out the agent of another key, never lets the
/// count pass the limit, and never disposes an agent.
/// </summary>
/// <remarks>
/// A tenant's key is the only thing that keeps its agent from another tenant's run (the same agent name and the
/// same content exist in every tenant), so the property is stated on the key: an agent that comes back always
/// carries the tenant, name, definition and dependency fingerprint that was asked for. An entry a concurrent
/// resolve drops, replaces or clears is the case that would break it.
/// </remarks>
public sealed class CompiledAgentCacheRaceTests
{
    private const int Limit = 16;

    private sealed record Outcome(ConcurrentBag<string> Mismatches, ConcurrentBag<TaggedAgent> Agents, int PeakCount);

    private static string Key(int tenant, int name, int definition, int dependencies)
        => $"tenant-{tenant}/agent-{name}/fp-{definition}/deps-{dependencies}";

    private static TaggedAgent Resolve(CompiledAgentCache cache, int tenant, int name, int definition, int dependencies, ConcurrentBag<TaggedAgent> created)
    {
        var expected = Key(tenant, name, definition, dependencies);

        var agent = (TaggedAgent)cache.GetOrAdd(
            $"tenant-{tenant}",
            $"agent-{name}",
            $"fp-{definition}",
            $"deps-{dependencies}",
            string.Empty,
            () =>
            {
                var compiled = new TaggedAgent(expected);
                created.Add(compiled);
                return compiled;
            });

        return agent;
    }

    private static async Task<Outcome> RunAsync(CompiledAgentCache cache, IReadOnlyList<int[]> plans)
    {
        var mismatches = new ConcurrentBag<string>();
        var created = new ConcurrentBag<TaggedAgent>();
        var peak = 0;
        using var stop = new CancellationTokenSource();

        // Reads the count all the time: it must never be above the limit, not even for a moment between
        // adding one entry and dropping another.
        var observer = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                var count = cache.Count;
                var seen = Volatile.Read(ref peak);

                while (count > seen && Interlocked.CompareExchange(ref peak, count, seen) != seen)
                {
                    seen = Volatile.Read(ref peak);
                }

                Thread.Yield();
            }
        });

        var workers = plans.Select(plan => Task.Run(() =>
        {
            foreach (var value in plan)
            {
                var tenant = value % 40;
                var name = value / 40 % 3;
                var definition = value / 120 % 2;
                var dependencies = value / 240 % 3;

                var agent = Resolve(cache, tenant, name, definition, dependencies, created);

                if (!string.Equals(agent.Tag, Key(tenant, name, definition, dependencies), StringComparison.Ordinal))
                {
                    mismatches.Add($"asked {Key(tenant, name, definition, dependencies)}, got {agent.Tag}");
                }
            }
        })).ToArray();

        await Task.WhenAll(workers);
        await stop.CancelAsync();
        await observer;

        return new Outcome(mismatches, created, Volatile.Read(ref peak));
    }

    private static int[] Plan(int seed, int length)
    {
        var random = new Random(seed);

        return [.. Enumerable.Range(0, length).Select(_ => random.Next(720))];
    }

    [Fact]
    public async Task Concurrent_resolves_that_drop_entries_never_hand_out_the_agent_of_another_key()
    {
        var cache = new CompiledAgentCache(
            new TraconAgentCacheOptions { MaxEntries = Limit },
            timeProvider: null);

        var outcome = await RunAsync(cache, [.. Enumerable.Range(0, 8).Select(worker => Plan(worker, 25_000))]);

        outcome.Mismatches.ShouldBeEmpty();
        outcome.PeakCount.ShouldBeLessThanOrEqualTo(Limit);
        cache.Count.ShouldBeLessThanOrEqualTo(Limit);
        outcome.Agents.Count.ShouldBeGreaterThan(Limit, "the limit was small enough to make every worker drop entries");
        outcome.Agents.ShouldAllBe(static agent => agent.DisposeCount == 0);
    }

    [Fact]
    public async Task A_clear_in_the_middle_of_the_resolves_changes_nothing_but_the_work_to_compile_again()
    {
        var cache = new CompiledAgentCache(
            new TraconAgentCacheOptions { MaxEntries = Limit },
            timeProvider: null);

        using var stop = new CancellationTokenSource();
        var clearing = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                cache.Clear();
                Thread.Yield();
            }
        });

        var outcome = await RunAsync(cache, [.. Enumerable.Range(0, 6).Select(worker => Plan(200 + worker, 20_000))]);

        await stop.CancelAsync();
        await clearing;

        outcome.Mismatches.ShouldBeEmpty();
        outcome.PeakCount.ShouldBeLessThanOrEqualTo(Limit);
        outcome.Agents.ShouldAllBe(static agent => agent.DisposeCount == 0);
    }

    [Fact]
    public async Task Sweeps_by_idle_time_that_run_while_the_cache_is_in_use_change_nothing_a_run_can_see()
    {
        var clock = new ManualTimeProvider();
        var cache = new CompiledAgentCache(
            new TraconAgentCacheOptions { MaxEntries = 10_000, IdleLifetime = TimeSpan.FromSeconds(30) },
            clock);

        using var stop = new CancellationTokenSource();

        // One thread moves the clock a minute at a time: every entry is idle for the whole lifetime by the
        // time anyone looks, so each use finds a sweep due.
        var ticking = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
            {
                clock.Advance(TimeSpan.FromSeconds(7));
                Thread.Yield();
            }
        });

        var outcome = await RunAsync(cache, [.. Enumerable.Range(0, 8).Select(worker => Plan(300 + worker, 25_000))]);

        await stop.CancelAsync();
        await ticking;

        outcome.Mismatches.ShouldBeEmpty();
        outcome.Agents.ShouldAllBe(static agent => agent.DisposeCount == 0);
    }

    [Fact]
    public void Generated_interleavings_keep_every_agent_with_its_own_key_and_the_count_within_the_limit()
    {
        var plans = Gen.Int[0, 719].Array[10, 80].Array[2, 4];

        plans.Sample(
            static generated =>
            {
                var cache = new CompiledAgentCache(
                    new TraconAgentCacheOptions { MaxEntries = 8 },
                    timeProvider: null);

                var outcome = RunAsync(cache, generated).GetAwaiter().GetResult();

                outcome.Mismatches.ShouldBeEmpty();
                outcome.PeakCount.ShouldBeLessThanOrEqualTo(8);
                outcome.Agents.ShouldAllBe(static agent => agent.DisposeCount == 0);
            },
            iter: 150);
    }

    [Fact]
    public async Task Concurrent_asynchronous_resolves_of_one_key_share_the_agent_that_won_and_leave_one_entry()
    {
        var cache = new CompiledAgentCache(
            new TraconAgentCacheOptions { MaxEntries = Limit },
            timeProvider: null);

        var agents = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => cache.GetOrAddAsync(
            "tenant-1",
            "agent",
            "fp",
            "deps",
            () => ValueTask.FromResult<AIAgent>(new TaggedAgent("tenant-1"))).AsTask())));

        agents.Distinct().Count().ShouldBe(1);
        cache.Count.ShouldBe(1);
    }
}
