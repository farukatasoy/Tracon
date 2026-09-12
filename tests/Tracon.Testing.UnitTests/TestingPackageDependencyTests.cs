using System.Xml.Linq;

namespace Tracon.Testing.UnitTests;

/// <summary>
/// 🚨 Guards <c>Tracon.Testing</c>'s K-007/39.1 rationale: it pulls in no
/// test framework, and the meta package does not depend on it.
/// </summary>
public sealed class TestingPackageDependencyTests
{
    private static readonly string[] BannedFrameworkPrefixes =
    [
        "xunit", "NUnit", "MSTest", "Shouldly", "FluentAssertions", "Microsoft.NET.Test.Sdk",
    ];

    [Fact]
    public void Tracon_Testing_references_no_test_framework()
    {
        var references = ReadPackageReferences("Tracon.Testing");

        foreach (var banned in BannedFrameworkPrefixes)
        {
            references.ShouldNotContain(
                reference => reference.StartsWith(banned, StringComparison.OrdinalIgnoreCase),
                customMessage: $"'Tracon.Testing' references a test framework ({banned}); " +
                               "the package must bind to no framework (section 39.2).");
        }
    }

    [Fact]
    public void Meta_package_does_not_reference_Tracon_Testing()
    {
        var references = ReadProjectReferences("Tracon");

        references.ShouldNotContain(
            reference => string.Equals(reference, "Tracon.Testing", StringComparison.Ordinal),
            customMessage: "The meta package must not depend on 'Tracon.Testing'; test code " +
                           "is not part of the meta package's promised 'everything with one reference' (section 39.1).");
    }

    private static List<string> ReadPackageReferences(string package)
    {
        var document = LoadProject(package);

        return document
            .Descendants("PackageReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToList();
    }

    private static List<string> ReadProjectReferences(string package)
    {
        var document = LoadProject(package);

        return document
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToList();
    }

    private static XDocument LoadProject(string package)
    {
        var projectPath = Path.Combine(RepositoryRoot, "src", package, $"{package}.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Project file not found: {projectPath}");

        return XDocument.Load(projectPath);
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
