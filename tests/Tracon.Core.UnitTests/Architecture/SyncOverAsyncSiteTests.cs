namespace Tracon.Core.UnitTests.Architecture;

/// <summary>
/// The only places a shipped package blocks a thread on a task are named here, each with the reason it cannot
/// starve a request.
/// </summary>
/// <remarks>
/// <para>
/// A blocking wait on a task inside a request path holds a thread pool thread for as long as the awaited work takes;
/// with a few concurrent requests and a store that is slow, the pool runs out of threads and every other request
/// (and every timer that needs one) waits behind them. The scan finds <c>.GetAwaiter().GetResult()</c>,
/// <c>.Wait(</c>, <c>.WaitAll(</c>, <c>.WaitAny(</c> and <c>Thread.Sleep(</c> in <c>src/</c> and fails for one that is not
/// in the list below. <c>.Result</c> is not scanned: it is also the name of a property of half the records in the
/// product, and the one real use (a task that is already completed) is read in the code review that adds it.
/// </para>
/// <para>
/// The test-support package <c>Tracon.Testing.Contracts.Xunit</c> and the generated client are not scanned: a contract
/// that races several callers blocks on purpose, and neither runs inside a consumer's request.
/// </para>
/// </remarks>
public sealed class SyncOverAsyncSiteTests
{
    private static readonly string[] Patterns =
    [
        ".GetAwaiter().GetResult()",
        ".Wait(",
        ".WaitAll(",
        ".WaitAny(",
        "Thread.Sleep(",
    ];

    private static readonly Dictionary<string, string> KnownSites = new(StringComparer.Ordinal)
    {
        ["src/Tracon.AspNetCore/Security/TraconRolePolicies.cs"] =
            "resolves the role policy names once while the endpoints are mapped; the default provider answers from a dictionary",
        ["src/Tracon.OpenAI.Shared/OpenAIBoundedRetryPolicy.cs"] =
            "the synchronous half of the client library's retry loop (Wait), on an injected clock only: only a caller of the library's blocking API reaches it, and the library's own wait blocks the same thread (the system clock keeps that wait); the asynchronous half awaits",
    };

    [Fact]
    public void Every_blocking_wait_on_a_task_in_the_packages_is_a_known_start_up_site()
    {
        var root = RepoRoot.Path;

        var actual = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .Where(static path => !path.Contains("/obj/", StringComparison.Ordinal)
                && !path.StartsWith("src/Tracon.Testing.Contracts.Xunit/", StringComparison.Ordinal)
                && !path.StartsWith("src/Tracon.Client/", StringComparison.Ordinal))
            .Where(path => File.ReadLines(Path.Combine(root, path)).Any(IsBlockingWait))
            .ToHashSet(StringComparer.Ordinal);

        var unknown = actual.Where(path => !KnownSites.ContainsKey(path)).Order(StringComparer.Ordinal).ToList();
        var gone = KnownSites.Keys.Where(path => !actual.Contains(path)).Order(StringComparer.Ordinal).ToList();

        unknown.ShouldBeEmpty(
            "A thread now blocks on a task in a shipped package. Await it instead; if the site runs before the host " +
            "takes a request and cannot be async, add the file here with the reason.");
        gone.ShouldBeEmpty("A known blocking site no longer blocks; remove it from the list.");
    }

    private static bool IsBlockingWait(string line)
    {
        var trimmed = line.TrimStart();

        // Comments and XML documentation name the pattern without running it.
        return !trimmed.StartsWith("//", StringComparison.Ordinal)
            && Patterns.Any(pattern => line.Contains(pattern, StringComparison.Ordinal));
    }
}
