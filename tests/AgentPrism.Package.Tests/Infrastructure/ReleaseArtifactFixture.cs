namespace AgentPrism.Package.Tests.Infrastructure;

/// <summary>
/// Packs the solution ONCE with a forced version, mirroring what the release
/// rehearsal command does before a real <c>v*</c> tag is pushed (Phase 97,
/// 97.2). Shared by every fact in <c>ReleaseArtifactTests</c> so the pack
/// only runs once per test session.
/// </summary>
/// <remarks>
/// The override is an environment variable (<c>MinVerVersionOverride</c>),
/// not a git tag: a real or temporary tag would mutate repository state that
/// every other test (and a developer's working tree) shares. MinVer reads
/// the variable directly - measured against <c>MinVer.targets</c> in the
/// restored NuGet package.
/// </remarks>
public sealed class ReleaseArtifactFixture : IAsyncLifetime
{
    /// <summary>The version this fixture forces onto every package (97.1, decision 1's target line).</summary>
    public const string Version = "1.0.0-preview.1";

    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(20);

    public string PackOutput { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        // AgentPrismSkipCleanWorkingTreeCheck (Phase 136): this fixture validates
        // the packaging CONTENT CONTRACT during test runs, not a release
        // candidate - it must not be blocked by AgentPrismValidateCleanWorkingTree
        // the way `kapi.py yayin` (the real release rehearsal) is.
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{RepoPaths.PackableSolutionFilter}\" -c Release -p:AgentPrismSkipCleanWorkingTreeCheck=true",
            environment: new Dictionary<string, string>(StringComparer.Ordinal) { ["MinVerVersionOverride"] = Version },
            timeout: PackTimeout);

        PackOutput = result.Combined;

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"'dotnet pack' with MinVerVersionOverride={Version} failed:{Environment.NewLine}{result.Combined}");
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
