using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// Every path the CI workflow places INSIDE the checkout must be ignored by git.
/// </summary>
/// <remarks>
/// The pack gate (<c>TraconValidateCleanWorkingTree</c>) counts an untracked
/// file as dirty, so a workflow-created directory stops every package with
/// <c>TRACON0004</c>. That is exactly what
/// <c>NUGET_PACKAGES: ${{ github.workspace }}/.nuget/packages</c> did: the
/// restore populated <c>.nuget/</c> in the repository root, the directory was
/// not in <c>.gitignore</c>, and both the `pack` and the release-rehearsal jobs
/// failed on a tree the developer's machine reported as clean. The failure is
/// invisible locally - <c>NUGET_PACKAGES</c> points outside the repository
/// there - so only a gate that reads the workflow itself can catch it.
/// </remarks>
public sealed partial class WorkflowWorkspacePathsTests
{
    // The timeout is not optional: MA0009 flags every regex whose match time is
    // unbounded (docs/hafiza/analyzer-tanilari.md).
    [GeneratedRegex(@"\$\{\{\s*github\.workspace\s*\}\}/(?<path>[^\s'""]+)", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex WorkspacePath();

    [Fact]
    public async Task EveryWorkspaceRelativeWorkflowPathIsGitIgnored()
    {
        var workflow = await File.ReadAllTextAsync(
            Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));

        var paths = WorkspacePath().Matches(workflow)
            .Select(match => match.Groups["path"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // A silent zero would turn this gate into decoration the day the
        // expression syntax changes.
        paths.ShouldNotBeEmpty("No '${{ github.workspace }}/...' path was found; the pattern is stale.");

        foreach (var path in paths)
        {
            var result = await ProcessRunner.RunAsync(
                "git",
                $"check-ignore -q \"{path}\"",
                workingDirectory: RepoPaths.Root);

            result.ExitCode.ShouldBe(
                0,
                $"'{path}' is written into the checkout by .github/workflows/ci.yml but is not " +
                "git-ignored. It makes the working tree dirty and TRACON0004 stops every pack.");
        }
    }

    /// <summary>
    /// The relative <c>path:</c> of every artifact upload and download step is
    /// git-ignored too (Phase 191). A download lands inside the checkout; the
    /// release rehearsal that follows refuses a dirty tree, and the tracked
    /// <c>packages/tracon-client/</c> directory once shared its name with the
    /// publish job's download path. Like <c>.nuget/</c>, this class only fails in CI.
    /// </summary>
    [Fact]
    public async Task EveryArtifactStepPathIsGitIgnored()
    {
        var workflow = await File.ReadAllLinesAsync(
            Path.Combine(RepoPaths.Root, ".github", "workflows", "ci.yml"));

        var paths = ArtifactStepPaths(workflow).Distinct(StringComparer.Ordinal).ToArray();

        paths.ShouldNotBeEmpty("No upload/download-artifact 'path:' was found; the parser is stale.");
        paths.ShouldContain("artifacts/ci-paket", StringComparer.Ordinal);

        foreach (var path in paths)
        {
            var result = await ProcessRunner.RunAsync(
                "git",
                $"check-ignore -q \"{path}\"",
                workingDirectory: RepoPaths.Root);

            result.ExitCode.ShouldBe(
                0,
                $"'{path}' is an artifact upload/download path in .github/workflows/ci.yml but is " +
                "not git-ignored. A download there makes the working tree dirty.");
        }
    }

    /// <summary>
    /// Reads the <c>path:</c> values (inline or a <c>|</c> block) of the steps
    /// that use <c>actions/upload-artifact</c> or <c>actions/download-artifact</c>.
    /// Absolute, home and expression paths are skipped: they are not inside
    /// the checkout or are covered by the workspace test above.
    /// </summary>
    private static IEnumerable<string> ArtifactStepPaths(string[] lines)
    {
        var inArtifactStep = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.StartsWith("- ", StringComparison.Ordinal))
            {
                inArtifactStep = false;
            }

            if (trimmed.Contains("uses: actions/upload-artifact@", StringComparison.Ordinal)
                || trimmed.Contains("uses: actions/download-artifact@", StringComparison.Ordinal))
            {
                inArtifactStep = true;
            }

            if (!inArtifactStep || !trimmed.StartsWith("path:", StringComparison.Ordinal))
            {
                continue;
            }

            var value = trimmed["path:".Length..].Trim();
            var values = new List<string>();
            if (string.Equals(value, "|", StringComparison.Ordinal))
            {
                var indent = lines[index].Length - lines[index].TrimStart().Length;
                while (index + 1 < lines.Length
                       && (lines[index + 1].Trim().Length == 0
                           || lines[index + 1].Length - lines[index + 1].TrimStart().Length > indent))
                {
                    index++;
                    if (lines[index].Trim().Length > 0)
                    {
                        values.Add(lines[index].Trim());
                    }
                }
            }
            else
            {
                values.Add(value);
            }

            foreach (var path in values)
            {
                if (!path.StartsWith('/') && !path.StartsWith('~') && !path.StartsWith("${{", StringComparison.Ordinal))
                {
                    yield return path;
                }
            }
        }
    }
}
