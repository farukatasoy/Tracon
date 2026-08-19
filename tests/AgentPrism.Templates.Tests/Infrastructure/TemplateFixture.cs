using System.Text.RegularExpressions;

namespace AgentPrism.Templates.Tests.Infrastructure;

/// <summary>
/// The one-time setup shared by all template tests: packs the solution
/// (populating <c>artifacts/package/release</c> as a local NuGet feed),
/// resolves the package version, and installs the template with
/// <c>dotnet new install</c>.
/// </summary>
/// <remarks>
/// Because AgentPrism is not published on nuget.org (Phase 7 pending, K-068),
/// the generated projects' <c>AgentPrism</c> package reference can only resolve
/// from this local feed. The template's own default version value (<c>*-*</c>,
/// a floating pre-release) assumes a full feed like nuget.org; for test
/// isolation, the PACKED version is resolved explicitly here and passed to
/// every `dotnet new` call.
/// </remarks>
public sealed class TemplateFixture : IAsyncLifetime
{
    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The version shared by all AgentPrism packages in the solution.</summary>
    public string Version { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var packResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{RepoPaths.PackableSolutionFilter}\" -c Release",
            timeout: PackTimeout);

        if (packResult.ExitCode != 0)
        {
            throw new InvalidOperationException($"'dotnet pack' failed:{Environment.NewLine}{packResult.Combined}");
        }

        Version = ResolveMetaPackageVersion();

        ClearGlobalPackageCache();

        // Remove any registration left over from a previous run first - an
        // explicit error is preferred over silently running with a stale version.
        await ProcessRunner.RunAsync("dotnet", $"new uninstall \"{RepoPaths.TemplatesProjectDirectory}\"", timeout: InstallTimeout);

        var installResult = await ProcessRunner.RunAsync(
            "dotnet",
            $"new install \"{RepoPaths.TemplatesProjectDirectory}\"",
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

        foreach (var package in Directory.EnumerateDirectories(root, "agentprism*"))
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
        await ProcessRunner.RunAsync("dotnet", $"new uninstall \"{RepoPaths.TemplatesProjectDirectory}\"", timeout: InstallTimeout);
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
                <add key="agentprism-local" value="{RepoPaths.PackageReleaseDirectory}" />
              </packageSources>
            </configuration>
            """;

        await File.WriteAllTextAsync(Path.Combine(directory, "NuGet.config"), content);
    }

    /// <summary>
    /// Runs <c>dotnet new agentprism-api</c>; a <c>NuGet.config</c> pointing at the
    /// packed version is written to the target directory beforehand.
    /// </summary>
    public async Task<ProcessResult> NewAsync(string name, string outputDirectory, string extraArgs = "")
    {
        Directory.CreateDirectory(outputDirectory);
        await WriteLocalNuGetConfigAsync(outputDirectory);

        return await ProcessRunner.RunAsync(
            "dotnet",
            $"new agentprism-api -n {name} -o \"{outputDirectory}\" --AgentPrismVersion {Version} --skip-restore {extraArgs}",
            timeout: InstallTimeout);
    }

    private static readonly Regex MetaPackageFileName = new(
        @"^AgentPrism\.(?<version>\d[^.]*(?:\.[^.]*)*)\.nupkg$",
        RegexOptions.None,
        TimeSpan.FromSeconds(1));

    /// <remarks>
    /// 🚨 The order of <c>Directory.EnumerateFiles</c> is filesystem-specific and
    /// <strong>may not be SORTED by version</strong> (measured). If a stale
    /// <c>AgentPrism.&lt;old-version&gt;.nupkg</c> left over from a previous
    /// phase's closeout sits in the folder, the first matching file could
    /// randomly pick an OLD version (possibly missing an analyzer, see the
    /// '--no-build' note in AgentPrism.Core.csproj). The MOST RECENTLY WRITTEN
    /// file is picked among the candidates: that is the pack output of the
    /// <c>InitializeAsync</c> that just completed.
    /// </remarks>
    private static string ResolveMetaPackageVersion()
    {
        var match = Directory.EnumerateFiles(RepoPaths.PackageReleaseDirectory, "AgentPrism.*.nupkg")
                .Select(path => (Path: path, Match: MetaPackageFileName.Match(Path.GetFileName(path))))
                .Where(candidate => candidate.Match.Success)
                .OrderByDescending(candidate => File.GetLastWriteTimeUtc(candidate.Path))
                .Select(candidate => candidate.Match)
                .FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"No 'AgentPrism.<version>.nupkg' was found under '{RepoPaths.PackageReleaseDirectory}'.");

        return match.Groups["version"].Value;
    }
}
