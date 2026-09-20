using Tracon.Benchmarks;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The benchmark harness hands BenchmarkDotNet an artifacts path derived from
/// the repository root. Resolving that root is a BUILD-boundary problem, not an
/// arithmetic one: the repository builds with
/// <c>ContinuousIntegrationBuild=true</c> whenever <c>CI</c> is set
/// (Directory.Build.props), the SDK then turns on
/// <c>DeterministicSourcePaths</c>, and every compile-time source path is
/// rewritten to start with <c>/_</c>. A root derived from
/// <c>[CallerFilePath]</c> therefore resolves to <c>/_</c> on CI, and
/// BenchmarkDotNet dies trying to create a directory at the filesystem root.
/// </summary>
/// <remarks>
/// Measured 2026-09-20: this is what the first `v*` tag push hit
/// (<c>exit 134</c>, <c>Access to the path '/_' is denied</c>). It had never
/// appeared before because the allocation gate is skipped unless a hot path
/// changed - 21 consecutive CI runs skipped it, and the tag run was its first
/// real execution. A gate that never runs cannot be trusted to be green.
/// </remarks>
public sealed class BenchmarkRepositoryLayoutTests
{
    /// <summary>The path shape a deterministic CI build bakes in.</summary>
    private const string DeterministicSourcePath = "/_/bench/Tracon.Benchmarks/Program.cs";

    [Fact]
    public void A_deterministic_CI_source_path_still_resolves_to_this_checkout()
    {
        var root = RepositoryLayout.ResolveRoot(DeterministicSourcePath, AppContext.BaseDirectory);

        // The point of the assertion is not the exact string: it is that the
        // resolved root is a directory that EXISTS and is writable. '/_' is
        // neither, which is precisely how the tag run failed.
        Directory.Exists(root).ShouldBeTrue(
            $"the benchmark artifacts root resolved to '{root}', which does not exist");

        File.Exists(Path.Combine(root, "Tracon.slnx")).ShouldBeTrue(
            $"'{root}' does not look like this checkout's repository root");
    }

    [Fact]
    public void A_real_source_path_resolves_to_the_same_checkout()
    {
        var real = Path.Combine(RepoPaths.Root, "bench", "Tracon.Benchmarks", "Program.cs");

        var root = RepositoryLayout.ResolveRoot(real, AppContext.BaseDirectory);

        Path.TrimEndingDirectorySeparator(root)
            .ShouldBe(Path.TrimEndingDirectorySeparator(RepoPaths.Root));
    }
}
