namespace AgentPrism.Package.Tests.Infrastructure;

/// <summary>
/// Dirties this repository's real working tree for the span of one test, by
/// dropping a single UNTRACKED file inside a packable project's directory.
/// </summary>
/// <remarks>
/// Deliberately does not touch any TRACKED file (e.g. appending a newline to
/// <c>Directory.Build.props</c> and running <c>git checkout --</c> afterwards):
/// an untracked file can never corrupt real content if a test crashes before
/// cleanup runs, and it exercises the untracked-file half of the gate's
/// contract on its own (Phase 136, 136.1 - an untracked file blocks the gate too).
/// Spinning up a second, isolated git repository with its own restored
/// dependency graph was considered and rejected - it would cost minutes per
/// fact for no coverage this approach does not already give.
/// </remarks>
internal sealed class DirtMarker : IDisposable
{
    private readonly string _path;

    private DirtMarker(string path) => _path = path;

    public static DirtMarker Create()
    {
        var path = Path.Combine(RepoPaths.Root, "src", "AgentPrism.Abstractions", ".agentprism-pack-cleanliness-test-marker");
        File.WriteAllText(path, "Created by Phase 136 PackCleanlinessGateTests; Dispose() deletes it.");
        return new DirtMarker(path);
    }

    public void Dispose() => File.Delete(_path);
}
