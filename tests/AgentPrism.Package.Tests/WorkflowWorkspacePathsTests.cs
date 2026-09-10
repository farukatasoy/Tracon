using System.Text.RegularExpressions;
using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// Every path the CI workflow places INSIDE the checkout must be ignored by git.
/// </summary>
/// <remarks>
/// The pack gate (<c>AgentPrismValidateCleanWorkingTree</c>) counts an untracked
/// file as dirty, so a workflow-created directory stops every package with
/// <c>AGENTPRISM0004</c>. That is exactly what
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
                "git-ignored. It makes the working tree dirty and AGENTPRISM0004 stops every pack.");
        }
    }
}
