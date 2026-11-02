using System.Collections.Immutable;

namespace Tracon.Generators.UnitTests.Corpus;

/// <summary>
/// The C# files one project would compile, found by LOCATION alone.
/// </summary>
/// <param name="Name">The project directory's name; the compilation takes it as its assembly name.</param>
/// <param name="Directory">The project directory.</param>
/// <param name="Files">Every <c>*.cs</c> file under the directory that belongs to this project and to no nested one.</param>
internal sealed record CorpusUnit(string Name, string Directory, ImmutableArray<string> Files);

/// <summary>
/// Finds the units of a source tree without evaluating anything in it.
/// </summary>
/// <remarks>
/// <para>
/// A directory that directly holds a <c>*.csproj</c> is a project. The project file is NEVER opened, restored or built:
/// MSBuild can run code while it evaluates a project, and a corpus is somebody else's tree. Everything downstream is
/// text, parsed by Roslyn and analysed without being run. That is also why the project's own settings
/// (<c>ImplicitUsings</c>, <c>Nullable</c>) are not read: the runner assumes the ASP.NET Core defaults instead.
/// </para>
/// <para>
/// A root without any project file (a flat folder of cached sources) is one unit.
/// </para>
/// </remarks>
internal static class CorpusDiscovery
{
    private static readonly string[] SkippedDirectoryNames = ["obj", "bin", "node_modules", ".git", "artifacts", "wwwroot"];

    /// <summary>The units of the repository's own trees: <c>src</c>, <c>tests</c>, <c>samples</c> and <c>bench</c>.</summary>
    /// <param name="repositoryRoot">The repository root.</param>
    /// <param name="trees">The top-level directories to read.</param>
    /// <returns>The units, ordered by path.</returns>
    public static IReadOnlyList<CorpusUnit> FromRepository(string repositoryRoot, params string[] trees)
        => [.. trees
            .Select(tree => Path.Combine(repositoryRoot, tree))
            .Where(Directory.Exists)
            .SelectMany(FromRoot)
            .OrderBy(static unit => unit.Directory, StringComparer.Ordinal)];

    /// <summary>The units under one root.</summary>
    /// <param name="root">A directory.</param>
    /// <returns>One unit per project directory, or the whole root as one unit when it holds no project file.</returns>
    public static IReadOnlyList<CorpusUnit> FromRoot(string root)
    {
        var projectDirectories = new List<string>();
        Walk(root, directory =>
        {
            if (Directory.EnumerateFiles(directory, "*.csproj").Any())
            {
                projectDirectories.Add(directory);
            }
        });

        if (projectDirectories.Count == 0)
        {
            return [new CorpusUnit(Path.GetFileName(root), root, [.. CollectFiles(root, [])])];
        }

        var units = new List<CorpusUnit>();

        foreach (var directory in projectDirectories)
        {
            var nested = projectDirectories.Where(other => other.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal)).ToList();
            var files = CollectFiles(directory, nested);

            if (files.Count > 0)
            {
                units.Add(new CorpusUnit(Path.GetFileName(directory), directory, [.. files]));
            }
        }

        return units;
    }

    private static void Walk(string directory, Action<string> visit)
    {
        visit(directory);

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (!SkippedDirectoryNames.Contains(Path.GetFileName(child), StringComparer.Ordinal))
            {
                Walk(child, visit);
            }
        }
    }

    private static List<string> CollectFiles(string directory, List<string> excludedDirectories)
    {
        var files = new List<string>();

        Walk(directory, current =>
        {
            if (excludedDirectories.Any(excluded => string.Equals(excluded, current, StringComparison.Ordinal)
                || current.StartsWith(excluded + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
            {
                return;
            }

            files.AddRange(Directory.EnumerateFiles(current, "*.cs"));
        });

        files.Sort(StringComparer.Ordinal);

        return files;
    }
}
