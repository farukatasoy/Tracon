using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// The version every template and consumer test restores is the one the shared
/// <see cref="TemplateFixture"/> pack produced from the git height - never the
/// <see cref="ReleaseArtifactFixture.Version"/> stamp another fixture forced
/// onto an earlier pack.
/// </summary>
/// <remarks>
/// 🚨 Measured 2026-09-24: the fixture used to take the NEWEST
/// <c>Tracon.&lt;version&gt;.nupkg</c> in the feed. An incremental pack skips the
/// meta package when nothing it packs changed (it has no build output, and the
/// version is not a file input), so on the second run in a row the
/// <c>1.0.0-preview.1</c> left by the previous run's release pack was newer and
/// the fixture resolved it: every template test installed the preview.1 template
/// and restored the LOCAL preview.1 into the global package folder, where
/// nuget.org's different preview.1 of the same ids belongs. The poisoned cache
/// directories found while planning phase 185 were exactly the default
/// template's package closure.
/// </remarks>
public sealed class TemplateFixtureVersionTests(TemplateFixture fixture)
{
    [Fact]
    public void The_fixture_resolves_the_height_version_not_a_forced_release_stamp()
    {
        string.Equals(fixture.Version, ReleaseArtifactFixture.Version, StringComparison.Ordinal).ShouldBeFalse(
            $"The template fixture resolved {fixture.Version}, the version ReleaseArtifactFixture forces, not the one its own pack produced.");

        File.Exists(Path.Combine(RepoPaths.PackageReleaseDirectory, $"Tracon.Templates.{fixture.Version}.nupkg"))
            .ShouldBeTrue($"Tracon.Templates.{fixture.Version}.nupkg is not in the local feed.");
    }
}
