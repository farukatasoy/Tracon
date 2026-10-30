using System.Text.Json;
using System.Text.RegularExpressions;
using Tracon.Package.Tests.Infrastructure;

namespace Tracon.Package.Tests;

/// <summary>
/// Verifies that the template actually compiles at at least two endpoints: the
/// most minimal (in-memory, no UI) and the most fully loaded (SQL Server +
/// Azure OpenAI + UI) combination, plus the PostgreSQL combination, whose store
/// reference comes from the template alone (the meta package carries no store,
/// An earlier change). The template can break silently when the library changes (37.3);
/// these tests are the only real gate that catches that breakage.
/// </summary>
public sealed class TemplateInstantiationTests(TemplateFixture fixture)
{
    private const string NpgsqlMessage =
        "The generated project's package graph contains Npgsql, but its persistence choice is not PostgreSQL. "
        + "The meta package must carry no store.";

    private static readonly Regex WarningLine = new(
        @"^.*: warning [A-Z]+\d+:",
        RegexOptions.Multiline,
        TimeSpan.FromSeconds(1));

    [Fact]
    public async Task Default_package_version_matches_the_packed_template_version()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Default.Version.Sample", dir.Path);
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var project = await File.ReadAllTextAsync(
            Path.Combine(dir.Path, "Default.Version.Sample.csproj"));

        project.ShouldContain($"Version=\"{fixture.Version}\"");
        project.ShouldNotContain("TRACON_TEMPLATE_PACKAGE_VERSION");
    }

    [Fact]
    public async Task Most_minimal_combination_compiles_with_zero_warnings()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync("Minimal.Sample", dir.Path, "--persistence memory --provider openai --ui false");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
        TakesNpgsql(dir.Path).ShouldBeFalse(NpgsqlMessage);
    }

    [Fact]
    public async Task Most_full_combination_compiles_with_zero_warnings()
    {
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Full.Sample",
            dir.Path,
            "--persistence sqlserver --provider azure --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
        TakesNpgsql(dir.Path).ShouldBeFalse(NpgsqlMessage);
    }

    [Fact]
    public async Task Postgres_combination_compiles_with_zero_warnings()
    {
        // PostgreSQL is not in the meta package, so the Tracon.PostgreSql reference of this output comes
        // from the template. A provider outside the meta package (anthropic) is chosen on purpose: the combination
        // proves the store reference does not depend on which provider the project takes.
        using var dir = new TempDirectory();

        var newResult = await fixture.NewAsync(
            "Postgres.Sample",
            dir.Path,
            "--persistence postgres --provider anthropic --ui true");
        newResult.ExitCode.ShouldBe(0, newResult.Combined);

        var buildResult = await ProcessRunner.RunAsync("dotnet", "build -c Release", dir.Path, TimeSpan.FromMinutes(5));

        buildResult.ExitCode.ShouldBe(0, buildResult.Combined);
        WarningLine.IsMatch(buildResult.Combined).ShouldBeFalse(buildResult.Combined);
        TakesNpgsql(dir.Path).ShouldBeTrue("A project generated with --persistence postgres must take the Npgsql driver.");
    }

    /// <summary>Whether the restore of the generated project resolved the Npgsql package.</summary>
    /// <remarks>
    /// Read from <c>obj/project.assets.json</c>, the restore's own record of the whole graph. The key of a library is
    /// <c>Id/Version</c>, so the id is matched with its separator: <c>Npgsql.EntityFrameworkCore.PostgreSQL</c> would
    /// not match.
    /// </remarks>
    private static bool TakesNpgsql(string projectDirectory)
    {
        var assetsPath = Path.Combine(projectDirectory, "obj", "project.assets.json");

        File.Exists(assetsPath).ShouldBeTrue($"The restore left no '{assetsPath}'.");

        using var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));

        return assets.RootElement.GetProperty("libraries").EnumerateObject()
            .Any(library => library.Name.StartsWith("Npgsql/", StringComparison.Ordinal));
    }
}
