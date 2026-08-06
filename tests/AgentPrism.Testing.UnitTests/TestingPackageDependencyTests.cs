using System.Xml.Linq;

namespace AgentPrism.Testing.UnitTests;

/// <summary>
/// 🚨 <c>AgentPrism.Testing</c>'in K-007/39.1 gerekcelerini korur: hicbir test
/// cercevesi getirmez ve meta paket ona baglanmaz.
/// </summary>
public sealed class TestingPackageDependencyTests
{
    private static readonly string[] BannedFrameworkPrefixes =
    [
        "xunit", "NUnit", "MSTest", "Shouldly", "FluentAssertions", "Microsoft.NET.Test.Sdk",
    ];

    [Fact]
    public void AgentPrism_Testing_hicbir_test_cercevesine_referans_vermez()
    {
        var references = ReadPackageReferences("AgentPrism.Testing");

        foreach (var banned in BannedFrameworkPrefixes)
        {
            references.ShouldNotContain(
                reference => reference.StartsWith(banned, StringComparison.OrdinalIgnoreCase),
                customMessage: $"'AgentPrism.Testing' bir test cercevesine ({banned}) referans veriyor; " +
                               "paket hicbir cerceveye baglanmamalidir (bolum 39.2).");
        }
    }

    [Fact]
    public void Meta_paket_AgentPrism_Testing_e_referans_vermez()
    {
        var references = ReadProjectReferences("AgentPrism");

        references.ShouldNotContain(
            reference => string.Equals(reference, "AgentPrism.Testing", StringComparison.Ordinal),
            customMessage: "Meta paket 'AgentPrism.Testing'e baglanmamalidir; test kodu " +
                           "meta paketin vaat ettigi 'tek referansla her sey'in icinde degildir (bolum 39.1).");
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

        File.Exists(projectPath).ShouldBeTrue($"Proje dosyasi bulunamadi: {projectPath}");

        return XDocument.Load(projectPath);
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AgentPrism.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("AgentPrism.slnx bulunamadi.");
    }
}
