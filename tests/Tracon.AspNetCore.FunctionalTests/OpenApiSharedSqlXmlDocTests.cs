using System.Xml.Linq;

namespace Tracon.AspNetCore.FunctionalTests;

/// <summary>
/// 🚨 F-76 guard (K-352): <c>Tracon.Sql.Shared</c> is not a package; the same
/// source is linked into three assemblies via <c>LinkBase</c> (K-185). The XML
/// documentation file of all three assemblies therefore carries the same
/// <c>&lt;member&gt;</c> ids.
/// </summary>
/// <remarks>
/// <para>
/// <c>Microsoft.AspNetCore.OpenApi</c>'s <c>GenerateAdditionalXmlFilesForOpenApi</c>
/// target adds each <c>ProjectReference</c>'s <c>.xml</c> file to <c>AdditionalFiles</c>;
/// <c>XmlCommentGenerator</c> collects all of them into a single dictionary and
/// throws <c>ArgumentException</c> on the second occurrence of the same id. The
/// result: <c>/openapi/v1.json</c> returns 500.
/// </para>
/// <para>
/// Scope was measured (2026-08-08): because the target's condition is
/// <c>ReferenceSourceTarget == 'ProjectReference'</c>, the XML of assemblies
/// that come from a NuGet package is never added at all. A real
/// <c>PackageReference</c> consumer was tried and returned HTTP 200; the bug
/// only affects a consumer that builds from source. That is why the fix lives
/// in the sample application's build, not in the library.
/// </para>
/// <para>
/// This test guards that fix. If it fails, either the target was removed or
/// K-185 changed; in the latter case the target is unnecessary and this test
/// should also be removed.
/// </para>
/// </remarks>
public sealed class OpenApiSharedSqlXmlDocTests
{
    private const string TargetName = "TraconRemoveDuplicateSqlXmlDocs";

    private static readonly string[] StrippedDocs = ["Tracon.SqlServer", "Tracon.Sqlite"];

    [Fact]
    public void Sample_application_excludes_conflicting_SQL_XML_docs_from_AdditionalFiles()
    {
        var projectPath = Path.Combine(
            RepositoryRoot, "samples", "Tracon.Api", "Tracon.Api.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Project file not found: {projectPath}");

        var project = XDocument.Load(projectPath);

        var target = project.Descendants("Target")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Name")?.Value, TargetName, StringComparison.Ordinal));

        target.ShouldNotBeNull(
            $"The '{TargetName}' target was removed. F-76 regresses: /openapi/v1.json returns 500 " +
            "because Tracon.SqlServer and Tracon.Sqlite's XML docs carry the same " +
            "<member> ids (K-185's linked-source pattern). Rationale: K-352.");

        target.Attribute("AfterTargets")?.Value
            .ShouldBe(
                "GenerateAdditionalXmlFilesForOpenApi",
                customMessage: $"'{TargetName}' runs after the wrong target; " +
                               "AdditionalFiles may not have been populated yet.");

        var removeCondition = target.Descendants("AdditionalFiles")
            .FirstOrDefault(element => element.Attribute("Remove") is not null)
            ?.Attribute("Condition")?.Value;

        removeCondition.ShouldNotBeNullOrWhiteSpace(
            $"The '{TargetName}' target has no <AdditionalFiles Remove=... Condition=... /> element.");

        foreach (var stripped in StrippedDocs)
        {
            removeCondition.ShouldContain(
                stripped,
                customMessage: $"The '{stripped}' XML doc is not excluded from AdditionalFiles; " +
                               "the conflict regresses.");
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
