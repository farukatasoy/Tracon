namespace Tracon.Benchmarks;

/// <summary>
/// Resolves the repository root that the benchmark harness writes its artifacts
/// under.
/// </summary>
/// <remarks>
/// <para>
/// A fixed, repo-relative artifacts path is deliberate: BenchmarkDotNet's own
/// default is derived from the executing assembly's location and lands
/// somewhere different depending on HOW the project is launched
/// (<c>dotnet run</c> vs the built exe directly), which would make
/// <c>scripts/kapi.py performans</c>'s report glob unreliable.
/// </para>
/// <para>
/// 🚨 The obvious way to get that path - <c>[CallerFilePath]</c> - is WRONG
/// here, and the reason is two files away. <c>Directory.Build.props</c> sets
/// <c>ContinuousIntegrationBuild=true</c> whenever <c>CI</c> is set; the SDK
/// responds by turning on <c>DeterministicSourcePaths</c>, which rewrites every
/// compile-time source path to start with <c>/_</c>. The root then resolves to
/// <c>/_</c> and BenchmarkDotNet dies creating a directory at the filesystem
/// root (<c>exit 134</c>). Measured 2026-09-20 on the first <c>v*</c> tag push.
/// </para>
/// <para>
/// The lesson is not "avoid <c>[CallerFilePath]</c>": it is that a compile-time
/// path is a BUILD ARTIFACT, and this repository's own build rewrites it. So
/// the compiled path is treated as a HINT that must prove itself, and the
/// authority is a runtime upward walk from the output directory - the same walk
/// <c>tests/Shared/Infrastructure/RepoRoot.cs</c> already uses for exactly this
/// reason.
/// </para>
/// </remarks>
public static class RepositoryLayout
{
    /// <summary>The file that marks this repository's root.</summary>
    private const string RootMarker = "Tracon.slnx";

    /// <summary>
    /// Resolves the repository root, preferring the compiled source path when
    /// it still points at a real checkout and falling back to an upward walk
    /// from the output directory when the build has rewritten it.
    /// </summary>
    /// <param name="compiledSourcePath">
    /// The <c>[CallerFilePath]</c> value baked in at compile time. A hint only:
    /// a deterministic build rewrites it to <c>/_/...</c>.
    /// </param>
    /// <param name="baseDirectory">The running assembly's output directory.</param>
    /// <exception cref="InvalidOperationException">
    /// Neither route found the repository root. Throwing is the point: handing
    /// BenchmarkDotNet a path that does not exist turns a clear failure here
    /// into an opaque one inside its runner.
    /// </exception>
    public static string ResolveRoot(string compiledSourcePath, string baseDirectory)
        => FromCompiledSourcePath(compiledSourcePath)
           ?? WalkUpFrom(baseDirectory)
           ?? throw new InvalidOperationException(
               $"The repository root was not found. '{RootMarker}' is not next to the compiled "
               + $"source path ('{compiledSourcePath}') and no directory above the output "
               + $"directory ('{baseDirectory}') carries it.");

    private static string? FromCompiledSourcePath(string compiledSourcePath)
    {
        if (string.IsNullOrEmpty(compiledSourcePath))
        {
            return null;
        }

        var directory = Path.GetDirectoryName(compiledSourcePath);

        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        // bench/Tracon.Benchmarks/Program.cs -> the repository root is two up.
        var candidate = Path.GetFullPath(Path.Combine(directory, "..", ".."));

        return CarriesMarker(candidate) ? candidate : null;
    }

    private static string? WalkUpFrom(string baseDirectory)
    {
        var directory = new DirectoryInfo(baseDirectory);

        while (directory is not null && !CarriesMarker(directory.FullName))
        {
            directory = directory.Parent;
        }

        return directory?.FullName;
    }

    private static bool CarriesMarker(string directory)
        => File.Exists(Path.Combine(directory, RootMarker));
}
