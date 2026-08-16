namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>Resolves important paths relative to the repository root.</summary>
internal static class RepoPaths
{
    /// <summary>
    /// The repository root. Walked upward from <c>AppContext.BaseDirectory</c>
    /// until <c>AgentPrism.slnx</c> is found; because of <c>UseArtifactsOutput</c>,
    /// the test assembly's output lands under an <c>artifacts/bin/...</c> path
    /// far from the repository root.
    /// </summary>
    public static string Root { get; } = FindRoot();

    public static string TemplatesProjectDirectory => Path.Combine(Root, "src", "AgentPrism.Templates");

    public static string SolutionFile => Path.Combine(Root, "AgentPrism.slnx");

    /// <summary>
    /// A solution filter covering the packable projects under <c>/src/</c>.
    /// Template tests need only these packages; packing this filter instead of
    /// the full solution avoids unnecessarily building 13 test projects
    /// (including the Postgres/SqlServer/Sqlite containers and Playwright E2E).
    /// </summary>
    public static string PackableSolutionFilter => Path.Combine(Root, "AgentPrism.src.slnf");

    public static string PackageReleaseDirectory => Path.Combine(Root, "artifacts", "package", "release");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentPrism.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"AgentPrism.slnx was not found. The search walked upward from '{AppContext.BaseDirectory}'.");
        }

        return dir.FullName;
    }
}
