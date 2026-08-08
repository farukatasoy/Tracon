using System.Xml.Linq;

namespace AgentPrism.AspNetCore.FunctionalTests;

/// <summary>
/// 🚨 F-76 korumasi (K-352): <c>AgentPrism.Sql.Shared</c> bir paket degildir; ayni kaynak
/// uc derlemeye <c>LinkBase</c> ile baglanir (K-185). Uc derlemenin XML dokuman dosyasi
/// bu yuzden ayni <c>&lt;member&gt;</c> kimliklerini tasir.
/// </summary>
/// <remarks>
/// <para>
/// <c>Microsoft.AspNetCore.OpenApi</c>'nin <c>GenerateAdditionalXmlFilesForOpenApi</c> hedefi
/// her <c>ProjectReference</c>'in <c>.xml</c> dosyasini <c>AdditionalFiles</c>'a ekler;
/// <c>XmlCommentGenerator</c> hepsini tek bir sozlukte toplar ve ikinci ayni kimlikte
/// <c>ArgumentException</c> firlatir. Sonuc: <c>/openapi/v1.json</c> 500 doner.
/// </para>
/// <para>
/// Kapsam olculdu (2026-08-08): hedefin kosulu <c>ReferenceSourceTarget == 'ProjectReference'</c>
/// oldugu icin NuGet paketiyle gelen derlemelerin XML'i hic eklenmez. Gercek bir
/// <c>PackageReference</c> tuketicisi denendi ve HTTP 200 dondu; hata yalniz kaynaktan
/// derleyen tuketiciyi etkiler. Bu yuzden duzeltme kutuphanede degil, ornek uygulamanin
/// derlemesindedir.
/// </para>
/// <para>
/// Bu test o duzeltmeyi korur. Dusuyorsa ya hedef silinmistir ya K-185 degismistir;
/// ikincisi ise hedef gereksizdir ve bu test de kaldirilir.
/// </para>
/// </remarks>
public sealed class OpenApiSharedSqlXmlDocTests
{
    private const string TargetName = "AgentPrismRemoveDuplicateSqlXmlDocs";

    private static readonly string[] StrippedDocs = ["AgentPrism.SqlServer", "AgentPrism.Sqlite"];

    [Fact]
    public void Ornek_uygulama_cakisan_SQL_XML_dokumanlarini_AdditionalFiles_disinda_birakir()
    {
        var projectPath = Path.Combine(
            RepositoryRoot, "samples", "AgentPrism.Api", "AgentPrism.Api.csproj");

        File.Exists(projectPath).ShouldBeTrue($"Proje dosyasi bulunamadi: {projectPath}");

        var project = XDocument.Load(projectPath);

        var target = project.Descendants("Target")
            .FirstOrDefault(element => string.Equals(
                element.Attribute("Name")?.Value, TargetName, StringComparison.Ordinal));

        target.ShouldNotBeNull(
            $"'{TargetName}' hedefi kaldirilmis. F-76 geri doner: /openapi/v1.json 500 verir " +
            "cunku AgentPrism.SqlServer ve AgentPrism.Sqlite XML dokumanlari ayni " +
            "<member> kimliklerini tasir (K-185 linked-source deseni). Gerekce: K-352.");

        target.Attribute("AfterTargets")?.Value
            .ShouldBe(
                "GenerateAdditionalXmlFilesForOpenApi",
                customMessage: $"'{TargetName}' yanlis hedeften sonra calisiyor; " +
                               "AdditionalFiles henuz doldurulmamis olabilir.");

        var removeCondition = target.Descendants("AdditionalFiles")
            .FirstOrDefault(element => element.Attribute("Remove") is not null)
            ?.Attribute("Condition")?.Value;

        removeCondition.ShouldNotBeNullOrWhiteSpace(
            $"'{TargetName}' hedefi bir <AdditionalFiles Remove=... Condition=... /> ogesi tasimiyor.");

        foreach (var stripped in StrippedDocs)
        {
            removeCondition.ShouldContain(
                stripped,
                customMessage: $"'{stripped}' XML dokumani AdditionalFiles disinda birakilmiyor; " +
                               "cakisma geri doner.");
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
