using System.Text.Json;

namespace AgentPrism.Core.UnitTests.Models;

/// <summary>
/// <see cref="ModelBinding.ProviderSettings"/> sozlugunun okunmasi ve dogrulanmasi.
/// </summary>
/// <remarks>
/// Bu sozlesme iki saglayici paketi tarafindan paylasilir; kurallarin burada
/// dogrulanmasi her pakette tekrar edilmesini onler.
/// </remarks>
public sealed class ModelProviderSettingsTests
{
    private static readonly string[] Supported = ["demo.acik", "demo.butce", "demo.esik"];

    [Fact]
    public void Bos_sozluk_dogrulamayi_gecer()
        => Should.NotThrow(() => ModelProviderSettings.Validate(Binding(), "demo", Supported));

    [Fact]
    public void Desteklenen_anahtarlar_gecer()
        => Should.NotThrow(() => ModelProviderSettings.Validate(
            Binding(("demo.acik", true), ("demo.butce", 8)),
            "demo",
            Supported));

    [Fact]
    public void Taninmayan_anahtar_gecerli_anahtarlari_listeler()
    {
        var exception = Should.Throw<AgentPrismException>(() => ModelProviderSettings.Validate(
            Binding(("demo.yokBoyle", true)),
            "demo",
            Supported));

        exception.Message.ShouldContain("demo.yokBoyle");
        exception.Message.ShouldContain("demo.acik");
        exception.Message.ShouldContain("demo.butce");
        exception.Message.ShouldContain("demo.esik");
    }

    [Fact]
    public void Baska_onekli_anahtar_ayri_bir_hata_mesaji_verir()
    {
        var exception = Should.Throw<AgentPrismException>(() => ModelProviderSettings.Validate(
            Binding(("baska.ayar", true)),
            "demo",
            Supported));

        // Yanlis onek "ayari baska bir saglayiciya yazdin" demektir; taninmayan
        // anahtardan farkli bir duzeltme ister.
        exception.Message.ShouldContain("baska.ayar");
        exception.Message.ShouldContain("do not belong");
    }

    [Fact]
    public void Anahtar_karsilastirmasi_harfe_duyarsizdir()
        => Should.NotThrow(() => ModelProviderSettings.Validate(
            Binding(("DEMO.Acik", true)),
            "demo",
            Supported));

    [Fact]
    public void Hicbir_ayar_desteklemeyen_saglayici_anlasilir_hata_verir()
        => Should.Throw<AgentPrismException>(() => ModelProviderSettings.Validate(
                Binding(("demo.acik", true)),
                "demo",
                []))
            .Message.ShouldContain("supports no extra settings");

    [Fact]
    public void Tanimsiz_ayar_null_dondurur()
    {
        ModelProviderSettings.ReadBoolean(Binding(), "demo.acik").ShouldBeNull();
        ModelProviderSettings.ReadInt32(Binding(), "demo.butce").ShouldBeNull();
        ModelProviderSettings.ReadString(Binding(), "demo.esik").ShouldBeNull();
    }

    [Fact]
    public void Mantiksal_deger_okunur()
    {
        ModelProviderSettings.ReadBoolean(Binding(("demo.acik", true)), "demo.acik").ShouldBe(true);
        ModelProviderSettings.ReadBoolean(Binding(("demo.acik", false)), "demo.acik").ShouldBe(false);
    }

    [Fact]
    public void Metin_olarak_gelen_mantiksal_deger_de_okunur()
    {
        // Ortam degiskeni ve komut satiri saglayicilari her degeri metin tasir.
        ModelProviderSettings.ReadBoolean(Binding(("demo.acik", "true")), "demo.acik").ShouldBe(true);
    }

    [Fact]
    public void Tam_sayi_okunur_ve_metin_bicimi_kabul_edilir()
    {
        ModelProviderSettings.ReadInt32(Binding(("demo.butce", 2048)), "demo.butce").ShouldBe(2048);
        ModelProviderSettings.ReadInt32(Binding(("demo.butce", "2048")), "demo.butce").ShouldBe(2048);
    }

    [Fact]
    public void Metin_okunur_ve_bos_metin_null_sayilir()
    {
        ModelProviderSettings.ReadString(Binding(("demo.esik", "BLOCK_NONE")), "demo.esik").ShouldBe("BLOCK_NONE");
        ModelProviderSettings.ReadString(Binding(("demo.esik", "  ")), "demo.esik").ShouldBeNull();
    }

    [Fact]
    public void Yanlis_tip_beklenen_tipi_yazan_bir_hata_verir()
    {
        Should.Throw<AgentPrismException>(() => ModelProviderSettings.ReadBoolean(Binding(("demo.acik", 3)), "demo.acik"))
            .Message.ShouldContain("boolean");

        Should.Throw<AgentPrismException>(() => ModelProviderSettings.ReadInt32(Binding(("demo.butce", true)), "demo.butce"))
            .Message.ShouldContain("integer");

        Should.Throw<AgentPrismException>(() => ModelProviderSettings.ReadString(Binding(("demo.esik", 3)), "demo.esik"))
            .Message.ShouldContain("text");
    }

    [Fact]
    public void Atanmamis_JsonElement_yok_sayilir()
    {
        // 🚨 Atanmamis bir JsonElement'in ValueKind degeri Undefined'dir. Okuma yolu
        // bunu "yok" saymalidir; istisna atmak tek bir eksik alan yuzunden butun
        // calistirmayi kirardi.
        var binding = new ModelBinding
        {
            Provider = "demo",
            Model = "demo-model",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo.acik"] = default,
            },
        };

        ModelProviderSettings.ReadBoolean(binding, "demo.acik").ShouldBeNull();
    }

    [Fact]
    public void Null_deger_yok_sayilir()
    {
        var binding = new ModelBinding
        {
            Provider = "demo",
            Model = "demo-model",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["demo.butce"] = JsonSerializer.SerializeToElement<int?>(null),
            },
        };

        ModelProviderSettings.ReadInt32(binding, "demo.butce").ShouldBeNull();
    }

    [Fact]
    public void Sirali_karsilastiricili_sozlukte_de_harfe_duyarsiz_okur()
    {
        // jsonb'den cozulen sozluk varsayilan (sirali) karsilastirici ile gelir;
        // arama sozlugun kendi karsilastiricisina birakilamaz.
        var binding = new ModelBinding
        {
            Provider = "demo",
            Model = "demo-model",
            ProviderSettings = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            {
                ["Demo.Butce"] = JsonSerializer.SerializeToElement(64),
            },
        };

        ModelProviderSettings.ReadInt32(binding, "demo.butce").ShouldBe(64);
    }

    [Fact]
    public void Varsayilan_baglantida_sozluk_bos_ve_atanmis_gelir()
    {
        var binding = new ModelBinding { Provider = "demo", Model = "demo-model" };

        binding.ProviderSettings.ShouldNotBeNull();
        binding.ProviderSettings.ShouldBeEmpty();
    }

    private static ModelBinding Binding(params (string Key, object Value)[] entries)
    {
        var settings = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in entries)
        {
            settings[key] = value switch
            {
                bool boolean => JsonSerializer.SerializeToElement(boolean),
                int number => JsonSerializer.SerializeToElement(number),
                _ => JsonSerializer.SerializeToElement(value.ToString()),
            };
        }

        return new ModelBinding { Provider = "demo", Model = "demo-model", ProviderSettings = settings };
    }
}
