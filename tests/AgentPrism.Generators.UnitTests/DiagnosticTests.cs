using System.Globalization;

namespace AgentPrism.Generators.UnitTests;

/// <summary>APG0001-APG0007 tanilarinin her birini ayri ayri dogrular.</summary>
public sealed class DiagnosticTests
{
    [Fact]
    public void APG0001_ayni_tool_adi_iki_metotta_kullanilirsa_verilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class ToolsA
            {
                [AgentPrismTool("cakisma", "Birinci.")]
                public static string Birinci() => "a";
            }

            internal static class ToolsB
            {
                [AgentPrismTool("cakisma", "Ikinci.")]
                public static string Ikinci() => "b";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0001").Count.ShouldBe(2);
    }

    [Fact]
    public void APG0002_gecersiz_tool_adi_verilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("gecersiz ad!", "Aciklama.")]
                public static string Getir() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0002").Count.ShouldBe(1);
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void APG0003_desteklenmeyen_parametre_tipi_verilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal sealed class KarmasikTip { public string? Ad { get; set; } }

            internal static class Tools
            {
                [AgentPrismTool("olustur", "Olusturur.")]
                public static void Olustur(KarmasikTip veri) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0003");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("KarmasikTip");
    }

    [Fact]
    public void APG0004_generic_metot_reddedilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("donustur", "Donusturur.")]
                public static T Donustur<T>(T deger) => deger;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0004").Count.ShouldBe(1);
    }

    [Fact]
    public void APG0005_AddGeneratedTools_cagrilir_ama_isaretli_metot_yoksa_verilir()
    {
        const string Source = """
            using AgentPrism;
            using Microsoft.Extensions.DependencyInjection;

            namespace MyApp;

            internal static class Program
            {
                public static void Configure(IServiceCollection services)
                {
                    services.AddAgentPrism().AddGeneratedTools();
                }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.DiagnosticsWithId("APG0005").Count.ShouldBe(1);
    }

    [Fact]
    public void APG0005_AddGeneratedTools_cagrilmazsa_isaretli_metot_yokken_sessizdir()
    {
        const string Source = """
            namespace MyApp;

            internal static class Tools
            {
                public static string Ping() => "pong";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();
        result.GeneratedFiles().ShouldBeEmpty();
    }

    [Fact]
    public void APG0006_aciklama_eksikse_uyari_verir_ama_uretimi_engellemez()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("adsiz_aciklama")]
                public static string Getir() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0006");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);

        // Uyari uretimi ENGELLEMEZ - wrapper dosyasi yine olusur.
        result.GeneratedFiles().Count.ShouldBe(2);
    }

    [Fact]
    public void APG0007_ornek_metodu_reddedilir_ve_mesaj_K218ye_isaret_eder()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal sealed class Tools
            {
                [AgentPrismTool("getir", "Getirir.")]
                public string Getir() => "x";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var diagnostics = result.DiagnosticsWithId("APG0007");
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].GetMessage(CultureInfo.InvariantCulture).ShouldContain("K-218");
    }

    [Fact]
    public void Islenemeyen_bir_sinif_sessizce_atlanmaz_her_zaman_bir_tani_uretir()
    {
        // 52.5: sessiz atlama yasak - APG0003/4/7 dogrulayan testler zaten bunu
        // kanitliyor, burada engelleyici HER kategori icin en az bir tani
        // uretildigini tek yerde teyit ediyoruz.
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal sealed class BozukTool
            {
                [AgentPrismTool("kotu")]
                public string Getir(string id) => id;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldNotBeEmpty();
        result.GeneratedFiles().ShouldBeEmpty();
    }
}
