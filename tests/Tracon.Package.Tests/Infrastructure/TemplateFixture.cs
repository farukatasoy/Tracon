namespace Tracon.Package.Tests.Infrastructure;

/// <summary>
/// The one-time setup shared by all template tests: packs the solution
/// (populating <c>artifacts/package/release</c> as a local NuGet feed),
/// resolves the package version, and installs the packed template with
/// <c>dotnet new install</c>.
/// </summary>
/// <remarks>
/// The generated projects resolve Tracon from the local feed. Installing the
/// packed template is intentional: it verifies the version stamped into the
/// package instead of bypassing that behavior through a source-directory
/// install or an explicit <c>--TraconVersion</c> argument.
/// </remarks>
public sealed class TemplateFixture : IAsyncLifetime
{
    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The version shared by all Tracon packages in the solution.</summary>
    public string Version { get; private set; } = string.Empty;

    private string TemplatePackagePath { get; set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        // TraconSkipCleanWorkingTreeCheck (Phase 136): same reasoning as
        // ReleaseArtifactFixture - this pack backs a local test feed, it is
        // never what a `v*` tag actually publishes.
        var packResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{RepoPaths.PackableSolutionFilter}\" -c Release -p:TraconSkipCleanWorkingTreeCheck=true",
            timeout: PackTimeout);

        if (packResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet pack' failed:{Environment.NewLine}{packResult.Combined}");
        }

        Version = await ResolvePackedVersionAsync();
        TemplatePackagePath = Path.Combine(
            RepoPaths.PackageReleaseDirectory,
            $"Tracon.Templates.{Version}.nupkg");

        if (!File.Exists(TemplatePackagePath))
        {
            throw new InvalidOperationException($"Packed template not found: '{TemplatePackagePath}'.");
        }

        ClearGlobalPackageCache();

        // Remove any registration left over from a previous run first - an
        // explicit error is preferred over silently running with a stale version.
        await ProcessRunner.RunAsync("dotnet", "new uninstall Tracon.Templates", timeout: InstallTimeout);

        var installResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"new install \"{TemplatePackagePath}\"",
            timeout: InstallTimeout);

        if (installResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet new install' failed:{Environment.NewLine}{installResult.Combined}");
        }
    }

    /// <summary>
    /// Removes the just-packed version from the global package folder.
    /// </summary>
    /// <remarks>
    /// 🚨 <strong>Without this, these tests silently run against a STALE
    /// package.</strong> MinVer derives the version from the git height, so
    /// every pack between two commits produces the SAME version string. NuGet
    /// extracts a version into the global packages folder ONCE and reuses it
    /// afterwards, so a rebuilt <c>.nupkg</c> with an unchanged version is
    /// never unpacked again - the consumer keeps compiling against the
    /// assemblies and analyzers of the first pack of the day. Measured in Phase
    /// 73: an analyzer change was invisible to every consumer test until this
    /// directory was removed.
    /// </remarks>
    private void ClearGlobalPackageCache()
    {
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");

        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var package in Directory.EnumerateDirectories(root, "tracon*"))
        {
            var extracted = Path.Combine(package, Version);

            if (Directory.Exists(extracted))
            {
                Directory.Delete(extracted, recursive: true);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await ProcessRunner.RunAsync("dotnet", "new uninstall Tracon.Templates", timeout: InstallTimeout);
    }

    /// <summary>
    /// Writes a <c>NuGet.config</c> to the given directory that declares the
    /// local package feed (<c>artifacts/package/release</c>) alongside nuget.org.
    /// </summary>
    public static async Task WriteLocalNuGetConfigAsync(string directory)
    {
        var content = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
                <add key="tracon-local" value="{RepoPaths.PackageReleaseDirectory}" />
              </packageSources>
            </configuration>
            """;

        await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.config"), content);
    }

    /// <summary>
    /// Runs <c>dotnet new tracon-api</c>; a <c>NuGet.config</c> pointing at the
    /// packed version is written to the target directory beforehand.
    /// </summary>
    public async Task<ProcessResult> NewAsync(string name, string outputDirectory, string extraArgs = "")
    {
        if (string.IsNullOrEmpty(Version))
        {
            throw new InvalidOperationException("The packed template fixture has not been initialized.");
        }

        Directory.CreateDirectory(outputDirectory);
        await WriteLocalNuGetConfigAsync(outputDirectory);

        return await ProcessRunner.RunAsync(
            "dotnet",
            $"new tracon-api -n {name} -o \"{outputDirectory}\" --skip-restore {extraArgs}",
            timeout: InstallTimeout);
    }

    private static string MetaProjectPath => Path.Combine(RepoPaths.Root, "src", "Tracon", "Tracon.csproj");

    /// <summary>The version the pack above stamped: MinVer's answer for this commit.</summary>
    /// <remarks>
    /// 🚨 <strong>Asked of MinVer, never read off the newest file in the feed.</strong>
    /// An incremental pack SKIPS the meta package when nothing it packs changed
    /// (it has no build output, and the version is not a file input), so its
    /// <c>.nupkg</c> keeps the timestamp of an older pack. Measured 2026-09-24:
    /// on the second run in a row the <c>Tracon.1.0.0-preview.1.nupkg</c> left
    /// by the previous run's <see cref="ReleaseArtifactFixture"/> was the newest
    /// file, this fixture resolved <c>1.0.0-preview.1</c>, installed that
    /// template, and every template test restored the LOCAL preview.1 into the
    /// global package folder - where nuget.org's different preview.1 of the same
    /// ids belongs. <c>TemplateFixtureVersionTests</c> keeps this closed.
    /// The query runs in the same environment as the pack, so an ambient
    /// <c>MinVerVersionOverride</c> reaches both.
    /// </remarks>
    private static async Task<string> ResolvePackedVersionAsync()
    {
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"msbuild \"{MetaProjectPath}\" -t:MinVer -getProperty:PackageVersion -p:Configuration=Release -nologo",
            timeout: InstallTimeout);

        var version = result.StandardOutput.Trim();

        if (result.ExitCode != 0 || version.Length == 0 || version.Contains(' ', StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"MinVer did not report the package version of '{MetaProjectPath}':{Environment.NewLine}{result.Combined}");
        }

        if (!File.Exists(Path.Combine(RepoPaths.PackageReleaseDirectory, $"Tracon.{version}.nupkg")))
        {
            throw new InvalidOperationException(
                $"The pack reported version {version}, but 'Tracon.{version}.nupkg' is not under '{RepoPaths.PackageReleaseDirectory}'.");
        }

        return version;
    }
}
