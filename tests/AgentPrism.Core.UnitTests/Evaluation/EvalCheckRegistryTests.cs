using System.Text.Json;
using Microsoft.Agents.AI;

namespace AgentPrism.Core.UnitTests.Evaluation;

public sealed class EvalCheckRegistryTests
{
    [Fact]
    public void Bos_veya_tanimsiz_yuk_bos_liste_dondurur()
    {
        var registry = new EvalCheckRegistry([]);

        registry.BuildChecks(default).ShouldBeEmpty();
        registry.BuildChecks(JsonDocument.Parse("null").RootElement).ShouldBeEmpty();
    }

    [Fact]
    public void Dizi_olmayan_yuk_hata_firlatir()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""{"kind":"nonEmpty"}""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void Kind_alani_olmayan_tanim_hata_firlatir()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""[{"minLength":10}]""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void Bilinmeyen_denetim_turu_hata_firlatir()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""[{"kind":"boyleBirSeyYok"}]""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void NonEmpty_dogru_esik_ile_calisir()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"nonEmpty","minLength":5}]"""));

        checks.Count.ShouldBe(1);
        checks[0].Invoke(new EvalItem("soru", "kisa")).Passed.ShouldBeFalse();
        checks[0].Invoke(new EvalItem("soru", "yeterince uzun cevap")).Passed.ShouldBeTrue();
    }

    [Fact]
    public void ContainsExpected_beklenen_ciktiya_gore_calisir()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"containsExpected","caseSensitive":false}]"""));

        var item = new EvalItem("soru", "Cevap IADE sureci hakkinda") { ExpectedOutput = "iade" };

        checks[0].Invoke(item).Passed.ShouldBeTrue();
    }

    [Fact]
    public void ContainsExpected_bos_ExpectedOutput_ile_daima_basarisiz_olur()
    {
        // Faz 45 Acik Soru 2: terfi eden bir vaka (Failed/NegativeScore) bos
        // ExpectedOutput tasir. Olculdu: EvalChecks.ContainsExpected null/bos
        // ExpectedOutput'ta FIRLATMAZ, sessizce PASSED=false doner — bu yuzden
        // boyle bir vaka `containsExpected` iceren bir takimda HER ZAMAN
        // basarisiz gorunur (docs/45-URETIMDEN-EVAL-KUMESI.md, secenek B).
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"containsExpected"}]"""));

        var item = new EvalItem("soru", "herhangi bir cikti") { ExpectedOutput = null };

        checks[0].Invoke(item).Passed.ShouldBeFalse();
    }

    [Fact]
    public void Keywords_verilen_kelimeleri_arar()
    {
        var registry = new EvalCheckRegistry([]);
        var checks = registry.BuildChecks(Parse("""[{"kind":"keywords","values":["iade","kargo"]}]"""));

        checks[0].Invoke(new EvalItem("soru", "iade ve kargo sureci")).Passed.ShouldBeTrue();
        checks[0].Invoke(new EvalItem("soru", "alakasiz cevap")).Passed.ShouldBeFalse();
    }

    [Fact]
    public void ToolCalled_gecersiz_mod_hata_firlatir()
    {
        var registry = new EvalCheckRegistry([]);
        var spec = Parse("""[{"kind":"toolCalled","tools":["get_order_status"],"mode":"herseyi"}]""");

        Should.Throw<AgentPrismException>(() => registry.BuildChecks(spec));
    }

    [Fact]
    public void Ozel_denetim_kaydi_kullanilabilir()
    {
        EvalCheck custom = item =>
            new EvalCheckResult(string.Equals(item.Response, "beklenen", StringComparison.Ordinal), "ozel", "ozelKontrol");
        var registry = new EvalCheckRegistry([new AgentPrismEvalCheckRegistration("ozelKontrol", custom)]);

        var checks = registry.BuildChecks(Parse("""[{"kind":"ozelKontrol"}]"""));

        checks.Count.ShouldBe(1);
        checks[0].Invoke(new EvalItem("soru", "beklenen")).Passed.ShouldBeTrue();
    }

    [Fact]
    public void Ayni_ozel_denetim_iki_kez_kaydedilirse_hata_firlatir()
    {
        EvalCheck custom = _ => new EvalCheckResult(true, "ozel", "ozelKontrol");

        Should.Throw<AgentPrismException>(() => new EvalCheckRegistry(
        [
            new AgentPrismEvalCheckRegistration("ozelKontrol", custom),
            new AgentPrismEvalCheckRegistration("ozelKontrol", custom),
        ]));
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
