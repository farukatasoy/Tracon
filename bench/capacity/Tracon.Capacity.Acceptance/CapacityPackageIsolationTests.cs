using System.Text.Json;

namespace Tracon.Capacity.Acceptance;

/// <summary>Proof that the measured host really consumed the packed feed.</summary>
/// <remarks>
/// 🚨 Without these, a capacity run could silently measure a stale package from
/// the machine-wide cache, or - worse - the working tree via a project
/// reference, and the report would not know which bytes it described.
/// </remarks>
public sealed class CapacityPackageIsolationTests
{
    private static JsonDocument Assets()
        => JsonDocument.Parse(File.ReadAllText(AcceptanceEnvironment.AssetsPath));

    [Fact]
    public void Every_Tracon_library_resolved_at_the_exact_measured_version()
    {
        using var assets = Assets();
        var version = AcceptanceEnvironment.PackageVersion;
        var seen = 0;

        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            var name = library.Name.Split('/')[0];

            if (!name.StartsWith("Tracon", StringComparison.Ordinal))
            {
                continue;
            }

            seen++;
            library.Name.ShouldBe($"{name}/{version}");
            library.Value.GetProperty("type").GetString().ShouldBe("package");
        }

        // A run that resolved no Tracon package at all would pass a
        // per-library loop vacuously.
        seen.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void A_floating_version_never_reaches_the_measurement()
    {
        AcceptanceEnvironment.PackageVersion.ShouldNotContain("*");
    }

    [Fact]
    public void The_restore_used_the_isolated_cache_rather_than_the_machine_wide_one()
    {
        var text = File.ReadAllText(AcceptanceEnvironment.AssetsPath);

        text.ShouldContain(AcceptanceEnvironment.PackageCache);
    }

    [Fact]
    public void No_Tracon_assembly_came_from_a_project_reference()
    {
        using var assets = Assets();

        foreach (var library in assets.RootElement.GetProperty("libraries").EnumerateObject())
        {
            if (!library.Name.StartsWith("Tracon", StringComparison.Ordinal))
            {
                continue;
            }

            // "project" here would mean the working tree was measured instead
            // of the package a consumer gets.
            library.Value.GetProperty("type").GetString().ShouldNotBe("project");
        }
    }

    [Fact]
    public void The_host_output_carries_the_Tracon_assemblies_the_package_shipped()
    {
        var output = new DirectoryInfo(AcceptanceEnvironment.HostOutputDirectory);

        output.Exists.ShouldBeTrue();
        output.GetFiles("Tracon.Core.dll").Length.ShouldBe(1);
        output.GetFiles("Tracon.AspNetCore.dll").Length.ShouldBe(1);
        output.GetFiles("Tracon.PostgreSql.dll").Length.ShouldBe(1);
    }
}
