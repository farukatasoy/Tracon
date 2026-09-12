namespace Tracon.Package.Tests.Infrastructure;

/// <summary>Resolves important paths relative to the repository root.</summary>
internal static class RepoPaths
{
    /// <summary>
    /// The repository root. The upward walk lives in <see cref="RepoRoot"/>
    /// (tests/Shared/Infrastructure), shared with the other test projects that
    /// need a repository-relative path.
    /// </summary>
    public static string Root => RepoRoot.Path;

    public static string TemplatesProjectDirectory => Path.Combine(Root, "src", "Tracon.Templates");

    public static string SolutionFile => Path.Combine(Root, "Tracon.slnx");

    /// <summary>
    /// A solution filter covering the packable projects under <c>/src/</c>.
    /// Template tests need only these packages; packing this filter instead of
    /// the full solution avoids unnecessarily building 13 test projects
    /// (including the Postgres/SqlServer/Sqlite containers and Playwright E2E).
    /// </summary>
    public static string PackableSolutionFilter => Path.Combine(Root, "Tracon.src.slnf");

    public static string PackageReleaseDirectory => Path.Combine(Root, "artifacts", "package", "release");
}
