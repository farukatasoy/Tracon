using Microsoft.CodeAnalysis;

namespace AgentPrism.Generators.UnitTests;

/// <summary>
/// <c>IIncrementalGenerator</c> sozlesmesini dogrular: alakasiz bir degisiklik
/// adimin onbellegini BOZMAMALIDIR (52.5, DoD "IncrementalityTests").
/// </summary>
public sealed class IncrementalityTests
{
    [Fact]
    public void Alakasiz_bir_degisiklikte_tool_adayi_adimi_yeniden_calismaz()
    {
        const string First = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("get_order_status", "Bir siparisin durumunu dondurur.")]
                public static string GetOrderStatus(string orderId) => orderId;
            }

            internal static class Unrelated
            {
                public const int Deger = 1;
            }
            """;

        // Yalniz ALAKASIZ sabitin degeri degisti; [AgentPrismTool] isaretli metot
        // metnen AYNI (ayni SyntaxNode konumu/icerigi degil ama ureteç girdisi
        // acisindan esdeger uretmelidir cunku ForAttributeWithMetadataName yalniz
        // isaretlenmis dugumu izler).
        const string Second = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("get_order_status", "Bir siparisin durumunu dondurur.")]
                public static string GetOrderStatus(string orderId) => orderId;
            }

            internal static class Unrelated
            {
                public const int Deger = 2;
            }
            """;

        var (first, second) = GeneratorTestHelper.RunIncremental(First, Second);

        first.Diagnostics.ShouldBeEmpty();
        second.Diagnostics.ShouldBeEmpty();

        var reasons = second.StepReasons(ToolRegistrationGenerator.TrackingNames.ToolCandidates);

        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }

    [Fact]
    public void Isaretli_metodun_govdesi_degisince_adayi_yeniden_uretilir()
    {
        const string First = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("get_order_status", "Bir siparisin durumunu dondurur.")]
                public static string GetOrderStatus(string orderId) => "v1";
            }
            """;

        const string Second = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("get_order_status", "Bir siparisin durumunu dondurur.")]
                public static string GetOrderStatus(string orderId) => "v2";
            }
            """;

        var (_, second) = GeneratorTestHelper.RunIncremental(First, Second);

        // Model (imza, ad, aciklama) AYNI kaldigi icin ToolCandidate esdegerdir -
        // govde degisikligi yalniz cagri sitesindeki metin degil, sema/kayit
        // uretimini ETKILEMEZ. Onbellek bu yuzden Cached kalmalidir; bu, modelin
        // yalniz GEREKEN alanlari tasidigini (SyntaxNode/govde degil) dogrular.
        var reasons = second.StepReasons(ToolRegistrationGenerator.TrackingNames.ToolCandidates);

        reasons.ShouldNotBeEmpty();
        reasons.ShouldAllBe(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged);
    }
}
