using System.Text.RegularExpressions;

namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// Every file in <c>src/</c> that names a <see cref="SemaphoreSlim"/> must be a known owner, filed under the one way
/// it ends the semaphore's life.
/// </summary>
/// <remarks>
/// <para>
/// A call that is inside a gate when the object around it is disposed still leaves the gate in its <c>finally</c>. If the
/// dispose has already closed the semaphore, that <c>Release()</c> throws <see cref="ObjectDisposedException"/> from the
/// same <c>finally</c> that closes the call, and the call loses its own result. The class turned up four times in one
/// pass over the job worker, the workflow runner, the skill-script limiter and a speech client, and three more owners
/// were left open at the end of it. Disposing a <see cref="SemaphoreSlim"/> does not wake its waiters either, so a gate
/// closed too early is a second way to hang.
/// </para>
/// <para>
/// There are three safe ways to end a semaphore. The owner is a <c>SlotPool</c>, which closes the semaphore with the last
/// slot given back. Or nobody disposes it: a binary gate or a limiter whose wait handle is never read holds nothing to
/// release. Or the dispose waits for everyone who holds or waits for the gate, and the entry says what it waits for.
/// </para>
/// <para>
/// The gate does not read the code: it makes a NEW owner fail until someone files it under one of the three, and a file
/// that no longer names the type fails too, so the list cannot go stale. The one claim it checks is the cheapest one: an
/// owner filed as never disposed must not call <c>Dispose()</c> on a gate, slots or semaphore field. The type is searched
/// by name because the target-typed <c>new(1, 1)</c> that most owners use does not carry it.
/// </para>
/// </remarks>
public sealed partial class SemaphoreOwnerSiteTests
{
    private enum Ending
    {
        /// <summary>The semaphore belongs to a <c>SlotPool</c>, which closes it with the last slot given back.</summary>
        SlotPool,

        /// <summary>Nothing disposes the semaphore.</summary>
        NeverDisposed,

        /// <summary>The dispose waits for every call that holds or waits for the gate.</summary>
        AfterHolders,
    }

    private static readonly Dictionary<string, (Ending Ending, string Reason)> KnownSites = new(StringComparer.Ordinal)
    {
        ["src/Tracon.Core/Hosting/SlotPool.cs"] = (Ending.SlotPool, "the pool: Dispose only stops handing out slots, the semaphore closes with the last slot returned (SlotPoolTests)"),
        ["src/Tracon.Core/Models/FallbackChatClient.cs"] = (Ending.NeverDisposed, "binary build gate; a build in flight at Dispose still returns (FallbackChatClientTests)"),
        ["src/Tracon.Core/Models/ProviderConcurrencyLimiter.cs"] = (Ending.NeverDisposed, "one semaphore per provider for the life of the limiter, which nothing disposes"),
        ["src/Tracon.Core/Recording/RunEventWriter.cs"] = (Ending.NeverDisposed, "append gate; the writer is not disposable and the CA1001 suppression says why"),
        ["src/Tracon.Core/Skills/Scripts/SkillScriptConcurrencyLimiter.cs"] = (Ending.AfterHolders, "a tenant's semaphore counts its users and the last Forget disposes it; the total is a SlotPool"),
        ["src/Tracon.Core/Voice/LiveVoiceSessionHost.cs"] = (Ending.AfterHolders, "Dispose waits for CloseAsync first, which waits for the pump (read, not probed)"),
        ["src/Tracon.Core/Voice/VoiceConversationDriver.cs"] = (Ending.AfterHolders, "the connection is disposed after RunAsync and the pending turn task end (read, not probed)"),
        ["src/Tracon.AspNetCore/Streaming/SseWriter.cs"] = (Ending.AfterHolders, "await using; writes are awaited at the endpoint and the keep-alive stops first (read, not probed)"),
        ["src/Tracon.Mcp/Internal/InMemoryMcpTokenCache.cs"] = (Ending.AfterHolders, "no await inside the critical section (read, not probed)"),
        ["src/Tracon.Mcp/Internal/McpConnection.cs"] = (Ending.NeverDisposed, "binary resource gate; a read in flight at DisposeAsync still returns a status (McpDisposeDuringCallTests)"),
        ["src/Tracon.Mcp/Internal/McpToolCatalog.cs"] = (Ending.NeverDisposed, "binary refresh gate; a refresh in flight at DisposeAsync still returns its outcome (McpDisposeDuringCallTests)"),
        ["src/Tracon.OpenAI/Live/OpenAILiveSideband.cs"] = (Ending.AfterHolders, "delegations are stopped and awaited at close, then the gate is disposed (read, not probed)"),
        ["src/Tracon.Workflows/Internal/TenantSlotLimiter.cs"] = (Ending.AfterHolders, "a tenant's gate counts its users and the last Leave disposes it"),
        ["src/Tracon.Workflows/Internal/WorkflowNodeConcurrencyLimit.cs"] = (Ending.NeverDisposed, "the slots of a function live as long as the process; nothing closes them"),
    };

    [Fact]
    public void Every_semaphore_owner_is_known_and_filed_under_how_it_ends()
    {
        var actual = SourceFiles()
            .Where(file => MentionsSemaphore(File.ReadAllText(file)))
            .Select(RelativePath)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = actual.Where(path => !KnownSites.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var gone = KnownSites.Keys.Where(path => !actual.Contains(path)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A new SemaphoreSlim owner was added. Decide how it ends: a SlotPool (closes with the last slot returned), " +
            "never disposed (a binary gate or limiter that holds nothing to release), or disposed after everyone who " +
            "holds or waits for it (say what it waits for). Then add the file here under that ending.");
        gone.ShouldBeEmpty("A known owner no longer names a SemaphoreSlim; remove it from the list.");
    }

    [Fact]
    public void An_owner_filed_as_never_disposed_does_not_dispose_a_gate()
    {
        var offenders = KnownSites
            .Where(site => site.Value.Ending == Ending.NeverDisposed)
            .Select(site => site.Key)
            .Where(path => DisposesAGate(File.ReadAllText(Path.Combine(RepoRoot.Path, path))))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            "An owner filed as never disposed calls Dispose() on a gate, slots or semaphore. A call in flight at that " +
            "moment would fail in its finally. Remove the Dispose(), or file the owner under the ending that waits for " +
            "its holders.");
    }

    [Fact]
    public void Every_entry_says_why_it_is_safe()
        => KnownSites.Where(site => string.IsNullOrWhiteSpace(site.Value.Reason)).Select(site => site.Key).ShouldBeEmpty();

    [Theory]
    [InlineData("    private readonly SemaphoreSlim _gate = new(1, 1);")]
    [InlineData("    public SemaphoreSlim Semaphore { get; } = new(slots, slots);")]
    [InlineData("    var gate = new SemaphoreSlim(1, 1);")]
    [InlineData("    private readonly ConcurrentDictionary<string, SemaphoreSlim> _semaphores = new();")]
    [InlineData("    public static SemaphoreSlim CreateSlots(int count) => new(count, count);")]
    public void The_scan_sees_an_owner_however_the_semaphore_is_created(string source)
        => MentionsSemaphore(source).ShouldBeTrue();

    [Theory]
    [InlineData("    /// <summary>Waits on a <see cref=\"SemaphoreSlim\"/>.</summary>")]
    [InlineData("    // a SemaphoreSlim would work here, a Lock is cheaper")]
    [InlineData("    private readonly Lock _gate = new();")]
    public void The_scan_leaves_a_comment_and_other_locks_alone(string source)
        => MentionsSemaphore(source).ShouldBeFalse();

    [Theory]
    [InlineData("        _buildGate.Dispose();")]
    [InlineData("        _resourceGate.Dispose();")]
    [InlineData("        slots.Dispose();")]
    [InlineData("        gate.Semaphore.Dispose();")]
    public void The_dispose_check_sees_a_gate_being_closed(string source)
        => DisposesAGate(source).ShouldBeTrue();

    [Theory]
    [InlineData("        _client.Dispose();")]
    [InlineData("        _subscriptions.Clear();")]
    public void The_dispose_check_leaves_other_disposals_alone(string source)
        => DisposesAGate(source).ShouldBeFalse();

    private static bool MentionsSemaphore(string source)
        => source.Split('\n').Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && SemaphoreName().IsMatch(line));

    private static bool DisposesAGate(string source)
        => source.Split('\n').Any(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && GateDispose().IsMatch(line));

    private static IEnumerable<string> SourceFiles()
        => Directory
            .EnumerateFiles(Path.Combine(RepoRoot.Path, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static string RelativePath(string path) => Path.GetRelativePath(RepoRoot.Path, path).Replace('\\', '/');

    [GeneratedRegex(@"\bSemaphoreSlim\b", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex SemaphoreName();

    // `<something named gate, slots or semaphore>.Dispose(` - the field, or its Semaphore property.
    [GeneratedRegex(@"\b\w*(?:[Gg]ate|[Ss]lots|[Ss]emaphore)\w*\.Dispose\(", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 5000)]
    private static partial Regex GateDispose();
}
