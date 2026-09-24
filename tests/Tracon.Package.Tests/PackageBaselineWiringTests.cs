using System.Text.Json;
using Tracon.Package.Tests.Infrastructure;
using Tracon.Tests.Common;

namespace Tracon.Package.Tests;

/// <summary>
/// The MSBuild side of the release rehearsal's breaking-change gate (Phase 187):
/// strict mode on every library package, and the three properties
/// <c>scripts/kapi.py yayin</c> passes (<c>TraconPackageBaselineRoot</c>,
/// <c>TraconPackageBaselineVersion</c>, <c>TraconApiCompatReportDir</c>) turned
/// into the SDK's baseline path and report path by <c>Directory.Build.targets</c>.
/// </summary>
/// <remarks>
/// MSBuild evaluation and a real <c>dotnet pack</c> are invisible to the Python
/// unit tests (<c>scripts/breaking_changes_test.py</c>), which own the report
/// parsing and the release-note matching. Every fact here evaluates the real
/// <c>src/*/*.csproj</c>; the list of 20 packages and their profiles comes from
/// <see cref="PackableProjects"/>, the same rule <c>kapi.py</c> applies.
/// </remarks>
[Collection(RepositoryTreeGate.Name)]
public sealed class PackageBaselineWiringTests(PackageBaselineWiringTests.Evaluations evaluations)
    : IClassFixture<PackageBaselineWiringTests.Evaluations>
{
    private const string BaselineVersion = "1.0.0-preview.2";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    private static readonly string BaselineRoot = Path.Combine(Path.GetTempPath(), "tracon-baseline-wiring", "packages");

    private static readonly string ReportDir = Path.Combine(Path.GetTempPath(), "tracon-baseline-wiring", "reports");

    private static readonly string[] EvaluatedProperties =
    [
        "PackageValidationBaselinePath",
        "ApiCompatGenerateSuppressionFile",
        "ApiCompatSuppressionOutputFile",
        "EnablePackageValidation",
        "EnableStrictModeForCompatibleTfms",
        "EnableStrictModeForCompatibleFrameworksInPackage",
    ];

    private static readonly string BaselineArguments =
        $"-p:TraconPackageBaselineRoot=\"{BaselineRoot}\" -p:TraconPackageBaselineVersion={BaselineVersion} -p:TraconApiCompatReportDir=\"{ReportDir}\"";

    private static List<string> LibraryIds
        => PackableProjects.Ids().Where(id => PackableProjects.ProfileOf(id) == PackageProfile.Library).ToList();

    [Fact]
    public void EveryPackableProjectIsEvaluated()
    {
        // 17 library packages, the meta package, the template package and the tool.
        evaluations.WithBaseline.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(PackableProjects.Ids().Order(StringComparer.Ordinal));
        LibraryIds.Count.ShouldBe(17);
    }

    [Fact]
    public void StrictModeIsOnForEveryLibraryPackage()
    {
        foreach (var id in LibraryIds)
        {
            var properties = evaluations.WithoutBaseline[id];
            properties["EnablePackageValidation"].ShouldBe("true", id);
            properties["EnableStrictModeForCompatibleTfms"].ShouldBe("true", id);
            properties["EnableStrictModeForCompatibleFrameworksInPackage"].ShouldBe("true", id);
        }
    }

    [Fact]
    public void BaselinePathIsDerivedOnlyForLibraryPackages()
    {
        foreach (var (id, properties) in evaluations.WithBaseline)
        {
            if (PackableProjects.ProfileOf(id) == PackageProfile.Library && !HasFirstReleaseFlag(id))
            {
                var lower = id.ToLowerInvariant();
                properties["PackageValidationBaselinePath"].ShouldBe(
                    Path.Combine(BaselineRoot, lower, BaselineVersion, $"{lower}.{BaselineVersion}.nupkg"), id);
                properties["ApiCompatGenerateSuppressionFile"].ShouldBe("true", id);
            }
            else
            {
                // Meta and template packages have no lib/; the tool is never validated
                // (the SDK's PackTool targets set EnablePackageValidation=false); a
                // library flagged TraconPackageFirstRelease has no published baseline.
                properties["PackageValidationBaselinePath"].ShouldBeEmpty(id);
                properties["ApiCompatGenerateSuppressionFile"].ShouldBeEmpty(id);
                properties["ApiCompatSuppressionOutputFile"].ShouldBeEmpty(id);
            }
        }
    }

    [Fact]
    public async Task FirstReleaseFlagStopsTheDerivation()
    {
        var properties = await EvaluateAsync("Tracon.Core", $"{BaselineArguments} -p:TraconPackageFirstRelease=true");

        properties["PackageValidationBaselinePath"].ShouldBeEmpty();
        properties["ApiCompatGenerateSuppressionFile"].ShouldBeEmpty();
        properties["EnableStrictModeForCompatibleTfms"].ShouldBe("true");
    }

    [Fact]
    public void ReportPathIsUnderReportDirPerProject()
    {
        // An empty report path makes the SDK write CompatibilitySuppressions.xml
        // into the project directory, and every later pack reads it back as a
        // suppression input - the break would be hidden for good.
        foreach (var id in LibraryIds.Where(id => !HasFirstReleaseFlag(id)))
        {
            evaluations.WithBaseline[id]["ApiCompatSuppressionOutputFile"]
                .ShouldBe(Path.Combine(ReportDir, $"{id}.xml"), id);
        }
    }

    [Fact]
    public void ReportPathsAreDistinct()
    {
        var paths = LibraryIds.Where(id => !HasFirstReleaseFlag(id)).Select(id => evaluations.WithBaseline[id]["ApiCompatSuppressionOutputFile"]).ToList();

        paths.Distinct(StringComparer.Ordinal).Count().ShouldBe(paths.Count);
    }

    [Fact]
    public void NothingIsDerivedWithoutBaselineRoot()
    {
        // The daily loop (ic-dongu, kapanis) never compares with a published
        // version and never reaches the network for one. Only the release packs
        // (`kapi.py yayin`, and `kapi.py paketle` in the CI build job) pass a root.
        foreach (var (id, properties) in evaluations.WithoutBaseline)
        {
            properties["PackageValidationBaselinePath"].ShouldBeEmpty(id);
            properties["ApiCompatGenerateSuppressionFile"].ShouldBeEmpty(id);
            properties["ApiCompatSuppressionOutputFile"].ShouldBeEmpty(id);
        }
    }

    [Fact]
    public async Task BaselineRootWithoutReportDirStopsWithTracon0007()
    {
        var project = CsprojPath("Tracon.Abstractions");

        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"msbuild \"{project}\" -t:TraconValidatePackageBaselineInputs -p:Configuration=Release -nologo " +
            $"-p:TraconPackageBaselineRoot=\"{BaselineRoot}\" -p:TraconPackageBaselineVersion={BaselineVersion}",
            timeout: Timeout);

        result.ExitCode.ShouldNotBe(0);
        result.Combined.ShouldContain("TRACON0007");
    }

    /// <summary>
    /// <c>RunPackageValidation</c> is incremental: its inputs are the pack inputs
    /// and the suppression files, its output a semaphore. A second pack of an
    /// unchanged package skips it and reports nothing - a silent green. The
    /// rehearsal forces the target with a report path that does not exist yet
    /// (measured, 187.0 step 5). This fact packs the same package twice against
    /// its own local baseline, no network, and requires both packs to validate.
    /// </summary>
    [Fact]
    public async Task SecondPackStillValidates()
    {
        const string ProjectId = "Tracon.Abstractions";
        const string Version = "0.0.0-dirty.baseline-wiring";

        using var work = new TempDirectory();

        // The escape hatch the clean-tree gate already has for local experiments
        // (a dirty version that sorts below every release). CI is cleared: on a
        // CI runner the tree is clean and the hatch is inert anyway.
        var dirtyVersion = $"-p:TraconAllowDirtyPack=true -p:MinVerVersionOverride={Version}";
        var environment = new Dictionary<string, string>(StringComparer.Ordinal) { ["CI"] = "" };
        var project = CsprojPath(ProjectId);

        var first = await ProcessRunner.RunAsync(
            "dotnet",
            $"pack \"{project}\" -c Release -o \"{Path.Combine(work.Path, "baseline-pack")}\" {dirtyVersion}",
            environment: environment,
            timeout: Timeout);
        first.ExitCode.ShouldBe(0, first.Combined);

        var lower = ProjectId.ToLowerInvariant();
        var baselineRoot = Path.Combine(work.Path, "packages");
        var baselineDirectory = Path.Combine(baselineRoot, lower, Version);
        Directory.CreateDirectory(baselineDirectory);
        File.Copy(
            Path.Combine(work.Path, "baseline-pack", $"{ProjectId}.{Version}.nupkg"),
            Path.Combine(baselineDirectory, $"{lower}.{Version}.nupkg"));

        for (var run = 1; run <= 2; run++)
        {
            var reportDir = Path.Combine(work.Path, $"reports-{run}");

            // The previous pack touched the semaphore as its LAST step. Compare with
            // that touch, not with a clock reading: a skipped validation leaves it
            // unchanged. The pause keeps a one-second file system from folding the
            // two touches into one timestamp.
            var before = File.GetLastWriteTimeUtc(SemaphorePath(ProjectId));
            await Task.Delay(TimeSpan.FromMilliseconds(1100)); // delay: product

            var result = await ProcessRunner.RunAsync(
                "dotnet",
                $"pack \"{project}\" -c Release -o \"{Path.Combine(work.Path, $"pack-{run}")}\" {dirtyVersion} " +
                $"-p:TraconPackageBaselineRoot=\"{baselineRoot}\" -p:TraconPackageBaselineVersion={Version} " +
                $"-p:TraconApiCompatReportDir=\"{reportDir}\"",
                environment: environment,
                timeout: Timeout);

            result.ExitCode.ShouldBe(0, result.Combined);
            File.GetLastWriteTimeUtc(SemaphorePath(ProjectId)).ShouldBeGreaterThan(before, $"pack {run} did not validate");

            // Measured (187.0 step 6): with no difference the SDK writes no report.
            // The semaphore, not the report, is the proof that validation ran.
            File.Exists(Path.Combine(reportDir, $"{ProjectId}.xml")).ShouldBeFalse($"pack {run} reported a difference against itself");
        }
    }

    private static string CsprojPath(string id) => Path.Combine(RepoPaths.Root, "src", id, $"{id}.csproj");

    /// <summary>Same text rule as <c>scripts/breaking_changes.py</c> (<c>FIRST_RELEASE_FLAG</c>).</summary>
    private static bool HasFirstReleaseFlag(string id)
        => File.ReadAllText(CsprojPath(id)).Contains(
            "<TraconPackageFirstRelease>true</TraconPackageFirstRelease>", StringComparison.Ordinal);

    /// <summary>The configuration directory is matched case-insensitively: macOS <c>Release</c>, Linux <c>release</c>.</summary>
    private static string SemaphorePath(string id)
    {
        var matches = Directory.EnumerateDirectories(Path.Combine(RepoPaths.Root, "artifacts", "obj", id))
            .Where(directory => string.Equals(Path.GetFileName(directory), "release", StringComparison.OrdinalIgnoreCase))
            .Select(directory => Path.Combine(directory, "Microsoft.NET.ApiCompat.ValidatePackage.semaphore"))
            .Where(File.Exists)
            .ToList();

        matches.Count.ShouldBe(1, $"expected one package-validation semaphore for {id}");
        return matches[0];
    }

    private static async Task<IReadOnlyDictionary<string, string>> EvaluateAsync(string id, string arguments)
    {
        var properties = string.Join(' ', EvaluatedProperties.Select(property => $"-getProperty:{property}"));
        var result = await ProcessRunner.RunAsync(
            "dotnet",
            $"msbuild \"{CsprojPath(id)}\" {properties} -p:Configuration=Release -nologo {arguments}",
            timeout: Timeout);

        result.ExitCode.ShouldBe(0, result.Combined);
        using var document = JsonDocument.Parse(result.StandardOutput);
        return document.RootElement.GetProperty("Properties").EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.Ordinal);
    }

    /// <summary>Evaluates every packable project once with and once without the baseline properties.</summary>
    public sealed class Evaluations : IAsyncLifetime
    {
        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WithBaseline { get; private set; } =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WithoutBaseline { get; private set; } =
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);

        public async ValueTask InitializeAsync()
        {
            var ids = PackableProjects.Ids();
            WithBaseline = await EvaluateAllAsync(ids, BaselineArguments);
            WithoutBaseline = await EvaluateAllAsync(ids, string.Empty);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static async Task<IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>> EvaluateAllAsync(
            IReadOnlyList<string> ids, string arguments)
        {
            // Four evaluations at a time: each is a separate `dotnet msbuild`
            // process; twenty in parallel would contend for the machine.
            using var gate = new SemaphoreSlim(4);
            var tasks = ids.Select(async id =>
            {
                await gate.WaitAsync();
                try
                {
                    return (id, properties: await EvaluateAsync(id, arguments));
                }
                finally
                {
                    gate.Release();
                }
            });

            return (await Task.WhenAll(tasks)).ToDictionary(pair => pair.id, pair => pair.properties, StringComparer.Ordinal);
        }
    }
}
