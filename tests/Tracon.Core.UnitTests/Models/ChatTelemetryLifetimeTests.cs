using Tracon.Core.UnitTests.Fakes;

namespace Tracon.Core.UnitTests.Models;

/// <summary>
/// Compiling an agent, and dropping one from the compiled-agent cache, must not leave a telemetry meter or activity
/// source behind.
/// </summary>
/// <remarks>
/// <para>
/// The chat client the Microsoft.Extensions.AI library builds for model-call telemetry makes a
/// <see cref="System.Diagnostics.Metrics.Meter"/>, an <see cref="System.Diagnostics.ActivitySource"/> and four
/// histograms when it is constructed, and frees them only when it is disposed. The runtime keeps both in process-wide
/// lists, so a client that nobody disposes is never collected. A compiled agent is dropped, never disposed (a run may
/// still hold it), so a client built for every compile is a leak that a bound on the cache does not close: each
/// tenant's compile adds one set and a compile after an eviction adds another.
/// </para>
/// <para>
/// The census counts by the name of the instrument and of the activity source, never by the name of the meter (a
/// listener sees every meter in the process, and other tests build agents at the same time). A leak adds one set per
/// compile, so a thousand compiles add a thousand; the room left for the other tests is a tenth of that.
/// </para>
/// </remarks>
public sealed class ChatTelemetryLifetimeTests
{
    private const int Compiles = 1_000;
    private const int Limit = 8;
    private const int Room = Compiles / 10;

    [Fact]
    public async Task Compiling_for_a_thousand_tenants_through_a_small_cache_leaves_no_telemetry_instrument_behind()
    {
        var compiler = new AgentDefinitionCompiler(TestData.Providers(new FakeModelProvider()), TestData.Registry());
        var cache = new CompiledAgentCache(new TraconAgentCacheOptions { MaxEntries = Limit }, TimeProvider.System);
        var definition = TestData.Definition();
        var compiled = 0;

        async ValueTask<Microsoft.Agents.AI.AIAgent> CompileAsync(string tenant)
        {
            return await cache.GetOrAddAsync(
                tenant,
                definition.Name,
                "fingerprint",
                string.Empty,
                string.Empty,
                async () =>
                {
                    compiled++;

                    return await compiler.CompileAsync(definition, TestContext.Current.CancellationToken);
                });
        }

        // The first compile may make what the later ones share.
        await CompileAsync("tenant-warm-up");

        using var census = InstrumentCensus.Open(TraconDiagnostics.ActivitySourceName);

        for (var tenant = 0; tenant < Compiles; tenant++)
        {
            await CompileAsync($"tenant-{tenant:D4}");
        }

        cache.Count.ShouldBeLessThanOrEqualTo(Limit);
        compiled.ShouldBeGreaterThanOrEqualTo(Compiles, "every tenant compiles its own agent, and most of them leave the small cache again");

        census.LiveInstruments.ShouldBeLessThan(
            Room,
            $"{Compiles} compiles left {census.LiveInstruments} live telemetry instruments: a chat client with its own meter is built for every compile");
        census.CreatedSources.ShouldBeLessThan(
            Room,
            $"{Compiles} compiles made {census.CreatedSources} activity sources: a chat client with its own source is built for every compile");
    }

    [Fact]
    public async Task Every_way_the_registry_builds_a_client_shares_the_telemetry_of_the_ones_before()
    {
        var registry = TestData.Providers(new FakeModelProvider());
        var binding = TestData.Binding();

        // The first client of each way may make what the later ones share.
        _ = registry.CreateChatClient(binding);

        using var census = InstrumentCensus.Open(TraconDiagnostics.ActivitySourceName);

        for (var client = 0; client < Compiles / 3; client++)
        {
            _ = registry.CreateChatClient(binding);
            _ = await registry.CreateChatClientAsync(binding, TestContext.Current.CancellationToken);
            _ = await registry.CreateSetupChatClientAsync(binding, TestContext.Current.CancellationToken);
        }

        census.LiveInstruments.ShouldBeLessThan(
            Room,
            $"{Compiles} clients left {census.LiveInstruments} live telemetry instruments: every client has a meter of its own");
        census.CreatedSources.ShouldBeLessThan(
            Room,
            $"{Compiles} clients made {census.CreatedSources} activity sources: every client has a source of its own");
    }
}
