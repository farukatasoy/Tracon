using System.Xml.Linq;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// 🚨 K-039/L35 gerekcesini korur: <c>AgentPrism.AspNetCore</c> tuketiciye OpenAPI
/// uretimi dayatmaz, yalniz paylasilan cerceveden gelen uc ustverisini tasir.
/// </summary>
/// <remarks>
/// <c>Microsoft.AspNetCore.OpenApi</c> 10.0.10, CVE'li <c>Microsoft.OpenApi</c>
/// 2.0.0 ceker (NU1903, GHSA-v5pm-xwqc-g5wc). Bu paket kutuphaneye eklenirse
/// CVE her tuketiciye — OpenAPI kullanmayanlar dahil — dayatilmis olur.
/// </remarks>
public sealed class OpenApiDependencyTests
{
    private static readonly string[] BannedPackages = ["Microsoft.AspNetCore.OpenApi", "Microsoft.OpenApi"];

    [Fact]
    public void AgentPrism_AspNetCore_OpenApi_paketine_bagimli_degil()
    {
        var projectPath = Path.Combine(
            RepositoryRoot, "src", "AgentPrism.AspNetCore", "AgentPrism.AspNetCore.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Proje dosyasi bulunamadi: {projectPath}");

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
                customMessage: $"'AgentPrism.AspNetCore', '{banned}' paketine referans veriyor; " +
                               "K-039/L35 bunu kütüphane sınırının dışında tutar (bölüm 40.1).");
        }
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
