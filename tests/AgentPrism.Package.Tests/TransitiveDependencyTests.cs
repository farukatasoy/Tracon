using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// Pins the dependency closure of three consumer shapes to a checked-in
/// baseline (<c>Baselines/transitive-dependencies.txt</c>) so a new
/// transitive package cannot enter silently (Phase 95, item 22).
/// </summary>
/// <remarks>
/// Only package IDENTITIES are compared, never versions - a
/// <c>Directory.Packages.props</c> bump would otherwise make this test
/// flaky noise instead of a real signal (95.3). Growing the baseline on
/// purpose is a deliberate edit to the checked-in file, the same pattern as
/// <c>SourceLanguageTests</c>.
/// </remarks>
public sealed class TransitiveDependencyTests(TemplateFixture fixture)
{
    [Theory]
    [InlineData("AgentPrism")]
    [InlineData("AgentPrism.Core")]
    [InlineData("AgentPrism.Google")]
    public async Task Package_graph_matches_the_checked_in_baseline(string packageId)
    {
        using var dir = new TempDirectory();

        var actual = new HashSet<string>(
            await DependencyProbe.ResolveGraphAsync(packageId, fixture.Version, dir.Path),
            StringComparer.Ordinal);
        var expected = TransitiveDependencyBaseline.Read(packageId);

        var added = actual.Where(id => !expected.Contains(id)).ToList();
        var removed = expected.Where(id => !actual.Contains(id)).ToList();

        var message = $"'{packageId}' dependency graph drifted from the baseline. "
            + $"Added: [{string.Join(", ", added)}]. Removed: [{string.Join(", ", removed)}]. "
            + "If this is intentional, update Baselines/transitive-dependencies.txt.";

        added.ShouldBeEmpty(message);
        removed.ShouldBeEmpty(message);
    }
}
