using AgentPrism.Package.Tests.Infrastructure;

namespace AgentPrism.Package.Tests;

/// <summary>
/// The MSBuild gate (<c>AgentPrismValidateCleanWorkingTree</c>,
/// <c>Directory.Build.targets</c>, Phase 136, 136.1/136.2) that refuses to
/// pack a dirty working tree. Runs against THIS repository's own working
/// tree - see <see cref="DirtMarker"/> for why, and <see cref="RepositoryTreeGate"/>
/// for how it stays isolated from every other pack/build in this project.
/// </summary>
/// <remarks>
/// <c>scripts/kapi.py yayin</c>'s own early-reject step (136.3) and the
/// staging/promote/manifest behavior it adds are pure-Python logic, covered
/// by <c>scripts/kapi_test.py</c>'s <c>YayinTestleri</c> - not here.
/// </remarks>
[Collection(RepositoryTreeGate.Name)]
public sealed class PackCleanlinessGateTests
{
    private static readonly string ProjectPath =
        Path.Combine(RepoPaths.Root, "src", "AgentPrism.Abstractions", "AgentPrism.Abstractions.csproj");

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    [Fact]
    public async Task DirtyWorkingTreeStopsPackWithAgentPrism0004()
    {
        using var marker = DirtMarker.Create();

        var result = await ProcessRunner.RunAsync("dotnet", $"pack \"{ProjectPath}\" -c Release", timeout: Timeout);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldContain("AGENTPRISM0004");
    }

    [Fact]
    public async Task DirtyWorkingTreeDoesNotStopBuild()
    {
        using var marker = DirtMarker.Create();

        var result = await ProcessRunner.RunAsync("dotnet", $"build \"{ProjectPath}\" -c Release", timeout: Timeout);

        result.ExitCode.ShouldBe(0, result.Combined);
    }

    /// <summary>
    /// AgentPrismSkipCleanWorkingTreeCheck is what lets `ReleaseArtifactFixture`
    /// and `TemplateFixture` - and `kapi.py kapanis`'s own pack step - keep
    /// working on an uncommitted tree (Phase 136 independent-audit finding
    /// #1: without an escape hatch, the closing gate could never pass before
    /// a commit). This fact locks the escape hatch itself so a future change
    /// cannot silently widen or remove it.
    /// </summary>
    [Fact]
    public async Task SkipCleanWorkingTreeCheckBypassesTheGateOnADirtyTree()
    {
        using var marker = DirtMarker.Create();

        try
        {
            var result = await ProcessRunner.RunAsync(
                "dotnet",
                $"pack \"{ProjectPath}\" -c Release -p:AgentPrismSkipCleanWorkingTreeCheck=true",
                timeout: Timeout);

            result.ExitCode.ShouldBe(0, result.Combined);
        }
        finally
        {
            foreach (var nupkg in Directory.EnumerateFiles(RepoPaths.PackageReleaseDirectory, "AgentPrism.Abstractions.0.0.0-preview.0.*.nupkg"))
            {
                File.Delete(nupkg);
            }

            foreach (var snupkg in Directory.EnumerateFiles(RepoPaths.PackageReleaseDirectory, "AgentPrism.Abstractions.0.0.0-preview.0.*.snupkg"))
            {
                File.Delete(snupkg);
            }
        }
    }

    [Fact]
    public async Task OverrideWithoutDirtyVersionStopsPackWithAgentPrism0006()
    {
        using var marker = DirtMarker.Create();

        // CI is cleared explicitly: this fact asserts the NON-CI branch
        // (AGENTPRISM0006), and this very test suite may itself be running
        // inside a CI job that already sets CI=true ambiently - which would
        // otherwise make AGENTPRISM0005 fire first instead.
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{ProjectPath}\" -c Release -p:AgentPrismAllowDirtyPack=true",
            environment: new Dictionary<string, string>(StringComparer.Ordinal) { ["CI"] = "" },
            timeout: Timeout);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldContain("AGENTPRISM0006");
    }

    [Fact]
    public async Task OverrideInCiStopsPackWithAgentPrism0005()
    {
        using var marker = DirtMarker.Create();

        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{ProjectPath}\" -c Release -p:AgentPrismAllowDirtyPack=true -p:MinVerVersionOverride=0.0.0-dirty.gate-test",
            environment: new Dictionary<string, string>(StringComparer.Ordinal) { ["CI"] = "true" },
            timeout: Timeout);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldContain("AGENTPRISM0005");
    }

    [Fact]
    public async Task OverrideWithDirtyVersionPacksSuccessfully()
    {
        using var marker = DirtMarker.Create();

        try
        {
            // CI cleared for the same reason as OverrideWithoutDirtyVersionStopsPackWithAgentPrism0006.
            var result = await ProcessRunner.RunAsync(
                "dotnet",
                $"pack \"{ProjectPath}\" -c Release -p:AgentPrismAllowDirtyPack=true -p:MinVerVersionOverride=0.0.0-dirty.gate-test",
                environment: new Dictionary<string, string>(StringComparer.Ordinal) { ["CI"] = "" },
                timeout: Timeout);

            result.ExitCode.ShouldBe(0, result.Combined);
        }
        finally
        {
            File.Delete(Path.Combine(RepoPaths.PackageReleaseDirectory, "AgentPrism.Abstractions.0.0.0-dirty.gate-test.nupkg"));
            File.Delete(Path.Combine(RepoPaths.PackageReleaseDirectory, "AgentPrism.Abstractions.0.0.0-dirty.gate-test.snupkg"));
        }
    }
}
