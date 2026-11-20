using BenchmarkDotNet.Attributes;
using Microsoft.Agents.AI;

namespace Tracon.Benchmarks;

/// <summary>
/// Measures what the parameter schema lookup adds to a run for an agent that an opaque agent source holds: the
/// source's listing is read once to learn whether it holds the name.
/// </summary>
/// <remarks>
/// A source that builds its own <c>AIAgent</c> has no definition to read, so the lookup asks its listing and scans
/// it for the name. The cost has two parts: the scan, which is Tracon's, and the listing, which is the source's.
/// The two benchmarks separate them. The first source hands back a list it built once, so it shows the scan alone
/// over a thousand names with the wanted one last. The second builds the thousand descriptors on every call, the way
/// a source that reads a database would, so it shows what such a source adds to every run.
/// </remarks>
[MemoryDiagnoser]
public class AgentDefinitionLookupBenchmarks
{
    private const int Agents = 1_000;
    private const string Wanted = "agent-999";

    private AgentDefinitionLookup _prebuilt = null!;
    private AgentDefinitionLookup _rebuilding = null!;

    [GlobalSetup]
    public void Setup()
    {
        _prebuilt = new AgentDefinitionLookup([new ListingSource(prebuilt: true)]);
        _rebuilding = new AgentDefinitionLookup([new ListingSource(prebuilt: false)]);
    }

    /// <summary>The scan over a listing the source built once.</summary>
    [Benchmark]
    public async Task<bool> ScanOfAListingBuiltOnce()
        => (await _prebuilt.FindAsync(Wanted, version: null, CancellationToken.None).ConfigureAwait(false)).Held;

    /// <summary>The scan, and the listing a source that builds it on every call allocates.</summary>
    [Benchmark]
    public async Task<bool> ScanOfAListingBuiltEachCall()
        => (await _rebuilding.FindAsync(Wanted, version: null, CancellationToken.None).ConfigureAwait(false)).Held;

    private static AgentDescriptor[] Build()
    {
        var descriptors = new AgentDescriptor[Agents];

        for (var index = 0; index < Agents; index++)
        {
            descriptors[index] = new AgentDescriptor
            {
                Name = $"agent-{index}",
                Origin = AgentDefinitionOrigin.Custom,
                SourceName = "listing",
            };
        }

        return descriptors;
    }

    private sealed class ListingSource(bool prebuilt) : IAgentSource
    {
        private readonly AgentDescriptor[] _built = prebuilt ? Build() : [];

        public string Name => "listing";

        public int Priority => 10;

        public ValueTask<IReadOnlyList<AgentDescriptor>> ListAsync(CancellationToken cancellationToken = default)
            => new(prebuilt ? _built : Build());

        public ValueTask<AIAgent?> ResolveAsync(string agentName, string? culture = null, CancellationToken cancellationToken = default)
            => new((AIAgent?)null);
    }
}
