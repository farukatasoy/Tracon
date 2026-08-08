namespace AgentPrism.Generators.UnitTests;

/// <summary>Basariyla siniflandirilan tool'lar icin uretilen kaynagi dogrular.</summary>
public sealed class GeneratedOutputTests
{
    [Fact]
    public void Isaretli_statik_metot_icin_kayit_uretilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class OrderTools
            {
                [AgentPrismTool("get_order_status", "Bir siparisin durumunu dondurur.")]
                public static string GetOrderStatus(string orderId) => orderId;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var aggregate = result.GeneratedFiles()["AgentPrismGeneratedTools.g.cs"];
        aggregate.ShouldContain("AddGeneratedTools");
        aggregate.ShouldContain("source: \"generated\"");

        var wrapperFile = result.SingleWrapperFile();
        wrapperFile.ShouldContain("public override string Name => \"get_order_status\";");
        wrapperFile.ShouldContain("Bir siparisin durumunu dondurur.");
        wrapperFile.ShouldContain("MyApp.OrderTools.GetOrderStatus(");
    }

    [Fact]
    public void Isaret_adsizsa_metot_adi_kullanilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool]
                public static string Ping() => "pong";
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("public override string Name => \"Ping\";");
    }

    [Fact]
    public void Onay_gereksinimi_uretilen_kayda_gecer()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("sil", "Kalici siler.", RequiresApproval = true)]
                public static void Sil(string id) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var aggregate = result.GeneratedFiles()["AgentPrismGeneratedTools.g.cs"];
        aggregate.ShouldContain("requiresApproval: true");
    }

    [Fact]
    public void Json_semasi_parametreleri_ve_zorunlulugu_icerir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("ara", "Arar.")]
                public static string Ara(string sorgu, int adet = 10) => sorgu;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("\\\"sorgu\\\":{\\\"type\\\":\\\"string\\\"}");
        wrapper.ShouldContain("\\\"adet\\\":{\\\"type\\\":\\\"integer\\\"}");
        wrapper.ShouldContain("\\\"required\\\":[\\\"sorgu\\\"]");
        wrapper.ShouldContain("GetOptional(arguments, \"adet\", static e => e.GetInt32(), 10)");
    }

    [Fact]
    public void Enum_parametresi_string_sema_ve_Enum_Parse_uretir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal enum Durum { Acik, Kapali }

            internal static class Tools
            {
                [AgentPrismTool("durum_ayarla", "Durumu ayarlar.")]
                public static void Ayarla(Durum durum) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("\\\"enum\\\":[\\\"Acik\\\",\\\"Kapali\\\"]");
        wrapper.ShouldContain("global::System.Enum.Parse<global::MyApp.Durum>(e.GetString()!, ignoreCase: true)");
    }

    [Fact]
    public void Dizi_parametresi_array_semasi_ve_GetArray_uretir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("etiketle", "Etiketler.")]
                public static void Etiketle(string[] etiketler) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("\\\"type\\\":\\\"array\\\"");
        wrapper.ShouldContain("GetArray(arguments, \"etiketler\", static e => e.GetString()!, required: true, defaultValue: null)");
    }

    [Fact]
    public void CancellationToken_parametresi_semadan_haric_tutulur_ve_dogrudan_baglanir()
    {
        const string Source = """
            using System.Threading;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("bekle", "Bekler.")]
                public static void Bekle(string id, CancellationToken cancellationToken) { }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldNotContain("cancellationToken\\\"");
        wrapper.ShouldContain("MyApp.Tools.Bekle(@id, cancellationToken)");
    }

    [Fact]
    public void Async_Task_donen_metot_await_ile_uretilir()
    {
        const string Source = """
            using System.Threading.Tasks;
            using AgentPrism;

            namespace MyApp;

            internal static class Tools
            {
                [AgentPrismTool("getir", "Getirir.")]
                public static async Task<string> GetirAsync(string id)
                {
                    await Task.Yield();
                    return id;
                }
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var wrapper = result.SingleWrapperFile();
        wrapper.ShouldContain("async global::System.Threading.Tasks.ValueTask<object?> InvokeCoreAsync");
        wrapper.ShouldContain("await global::MyApp.Tools.GetirAsync(@id).ConfigureAwait(false);");
    }

    [Fact]
    public void Iki_farkli_sinifta_tool_varsa_ikisi_de_uretilir()
    {
        const string Source = """
            using AgentPrism;

            namespace MyApp;

            internal static class OrderTools
            {
                [AgentPrismTool("get_order", "Siparis getirir.")]
                public static string GetOrder(string id) => id;
            }

            internal static class UserTools
            {
                [AgentPrismTool("get_user", "Kullanici getirir.")]
                public static string GetUser(string id) => id;
            }
            """;

        var result = GeneratorTestHelper.Run(Source);

        result.Diagnostics.ShouldBeEmpty();

        var files = result.GeneratedFiles();
        files.Count.ShouldBe(3); // 2 wrapper + 1 aggregator
        files.Values.Count(text => text.Contains("\"get_order\"", StringComparison.Ordinal)).ShouldBe(1);
        files.Values.Count(text => text.Contains("\"get_user\"", StringComparison.Ordinal)).ShouldBe(1);
    }
}
