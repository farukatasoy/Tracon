using System.Xml.Linq;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// 🚨 Protects the K-039/L35 rationale: <c>Tracon.AspNetCore</c> does not
/// force OpenAPI generation on the consumer, it only carries endpoint
/// metadata coming from the shared framework.
/// </summary>
/// <remarks>
/// <c>Microsoft.AspNetCore.OpenApi</c> 10.0.10 pulls in the CVE-affected
/// <c>Microsoft.OpenApi</c> 2.0.0 (NU1903, GHSA-v5pm-xwqc-g5wc). If this
/// package were added to the library, the CVE would be forced onto every
/// consumer — including those who do not use OpenAPI.
/// </remarks>
public sealed class OpenApiDependencyTests
{
    private static readonly string[] BannedPackages = ["Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi"];

    [Fact]
    public void Tracon_AspNetCore_does_not_depend_on_the_OpenApi_package()
    {
        var projectPath = Path.Combine(
            RepositoryRoot, "src", "Tracon.AspNetCore", "Tracon.AspNetCore.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Project file not found: {projectPath}");

        var references = XDocument.Load(projectPath)
            .Descendants("PackageReference")
            .Select(static element => element.Attribute("Include")?.Value)
            .Where(static include => !string.IsNullOrWhiteSpace(include))
            .Select(static include => include!)
            .ToList();

        foreach (var banned in BannedPackages)
        {
            references.ShouldNotContain(
                reference => string.Equals(reference, banned, StringComparison.Ordinal),
                customMessage: $"'Tracon.AspNetCore' references '{banned}'; " +
                               "K-039/L35 keeps it outside the library boundary (section 40.1).");
        }
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tracon.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Tracon.slnx not found.");
    }
}
