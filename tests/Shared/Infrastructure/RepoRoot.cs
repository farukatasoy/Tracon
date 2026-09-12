namespace Tracon.Tests.Common;

/// <summary>Finds this checkout's repository root from a test assembly's output directory.</summary>
/// <remarks>
/// Because of <c>UseArtifactsOutput</c>, a test assembly's output lands under
/// <c>artifacts/bin/...</c>, far from the repository root. Every test project
/// that has to reach a repository-relative path walks upward the same way, so
/// the walk lives here once rather than in each project's own path helper.
/// </remarks>
internal static class RepoRoot
{
    /// <summary>The repository root directory.</summary>
    public static string Path { get; } = Find();

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(System.IO.Path.Combine(dir.FullName, "Tracon.slnx")))
        {
            dir = dir.Parent;
        }

        if (dir is null)
        {
            throw new InvalidOperationException(
                $"Tracon.slnx was not found. The search walked upward from '{AppContext.BaseDirectory}'.");
        }

        return dir.FullName;
    }
}
