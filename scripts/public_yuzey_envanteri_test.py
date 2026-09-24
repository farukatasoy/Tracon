#!/usr/bin/env python3
"""Public API yüzey envanterinin testleri (Faz 182).

Envanter bir karar aracıdır: yanlış sayılan bir tip ya gereksiz yere dondurulur
ya da tüketiciyi kıracak biçimde `internal` adayı gösterilir. Testler bilinen
küçük örnek kümeler üzerinde her kanıt türünü, imza kapanışını ve sınıf
önceliğini sabitler.
"""
from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "public_yuzey_envanteri", ROOT / "scripts" / "public-yuzey-envanteri.py")
envanter = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = envanter
spec.loader.exec_module(envanter)


CORE_UNSHIPPED = """#nullable enable
Tracon.RunRecord
Tracon.RunRecord.Status.get -> Tracon.RunStatus
Tracon.RunRecord.Status.init -> void
Tracon.RunStatus
Tracon.RunStatus.Completed = 2 -> Tracon.RunStatus
Tracon.IRunStore
Tracon.IRunStore.GetAsync(string! id, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Tracon.RunRecord?>
Tracon.RunStoreBase
Tracon.RunStoreBase.Probe() -> Tracon.ProbeResult!
Tracon.ProbeResult
Tracon.Cache<TKey>
Tracon.Cache<TKey>.Get(TKey key) -> Tracon.CacheEntry!
Tracon.CacheEntry
Tracon.Outer
Tracon.Outer.Inner
Tracon.Outer.Inner.Value.get -> int
Tracon.TraconBuilderExtensions
static Tracon.TraconBuilderExtensions.AddWidget(this Tracon.ITraconBuilder! builder, System.Action<Tracon.WidgetOptions!>? configure = null) -> Tracon.ITraconBuilder!
Tracon.WidgetOptions
Tracon.WidgetOptions.Mode.get -> Tracon.WidgetMode
Tracon.WidgetMode
Tracon.WidgetMode.Fast = 0 -> Tracon.WidgetMode
Tracon.StringExtensions
static Tracon.StringExtensions.Shorten(this string! value) -> string!
Tracon.SampleToolAttribute
Tracon.Helper
static Tracon.Helper.Compute() -> int
Tracon.HelperResult
Tracon.AgentDescriptor
Tracon.Justified
Tracon.DerivedHandler
Tracon.HandlerBase
Tracon.IThrowingStore
Tracon.StoreFailedException
Tracon.StoreFailedException.StoreFailedException(Tracon.StoreFailure reason) -> void
Tracon.StoreFailure
Tracon.StoreFailure.Gone = 0 -> Tracon.StoreFailure
Tracon.UndocumentedException
"""

CORE_SOURCE = """
namespace Tracon;

public sealed record RunRecord { }
public enum RunStatus { Completed = 2 }
public interface IRunStore { }
public abstract class RunStoreBase { }
public sealed class ProbeResult { }
public sealed class Cache<TKey> where TKey : notnull { }
public sealed class CacheEntry { }
public static class Outer { public sealed class Inner { } }
public static class TraconBuilderExtensions { }
public sealed class WidgetOptions { }
public enum WidgetMode { Fast }
public static class StringExtensions { }
public sealed class SampleToolAttribute : System.Attribute { }
public static class Helper { }
public readonly record struct HelperResult(int Value);
public sealed record AgentDescriptor(string Name);
public sealed class Justified { }
public sealed class DerivedHandler : HandlerBase, IRunStore { }
public class HandlerBase { }
internal sealed class RunRecord2 { }
"""

THROWING_STORE_SOURCE = """
namespace Tracon;

/// <summary>A seam whose contract names the exception it throws.</summary>
public interface IThrowingStore
{
    /// <exception cref="StoreFailedException">The record is gone.</exception>
    void Load();
}
"""

EXCEPTION_SOURCE = """
namespace Tracon;

public sealed class StoreFailedException : System.Exception { }
public enum StoreFailure { Gone }
public sealed class UndocumentedException : System.Exception { }
"""


def _envanter(korpus_girdileri, semalar=frozenset(), gerekceler=None, ek_paketler=None):
    tipler = {}
    for tip in envanter.unshipped_ayristir("Tracon.Core", CORE_UNSHIPPED).values():
        tipler[tip.anahtar] = tip
    kaynaklar = {"Tracon.Core": [
        ("src/Tracon.Core/Types.cs", CORE_SOURCE),
        ("src/Tracon.Core/IThrowingStore.cs", THROWING_STORE_SOURCE),
        ("src/Tracon.Core/Exceptions.cs", EXCEPTION_SOURCE),
    ]}
    for paket, (unshipped, kaynak) in (ek_paketler or {}).items():
        for tip in envanter.unshipped_ayristir(paket, unshipped).values():
            tipler[tip.anahtar] = tip
        kaynaklar[paket] = [(f"src/{paket}/Types.cs", kaynak)]
    envanter.bildirimleri_tara(tipler, kaynaklar)
    envanter.siniflandir(tipler, envanter.Korpus.kur(korpus_girdileri), set(semalar), gerekceler or {})
    return tipler


def _tip(tipler, ad, paket="Tracon.Core"):
    return tipler[(paket, ad)]


class UnshippedAyristirmaTestleri(unittest.TestCase):
    def test_tip_satiri_ile_uye_satiri_ayrilir(self):
        tipler = envanter.unshipped_ayristir("Tracon.Core", CORE_UNSHIPPED)
        self.assertIn("Tracon.RunRecord", tipler)
        self.assertNotIn("Tracon.RunRecord.Status", tipler)
        self.assertEqual(len(tipler["Tracon.RunRecord"].uyeler), 2)

    def test_enum_uyesi_tip_sayilmaz(self):
        tipler = envanter.unshipped_ayristir("Tracon.Core", CORE_UNSHIPPED)
        self.assertNotIn("Tracon.RunStatus.Completed", tipler)
        self.assertEqual(tipler["Tracon.RunStatus"].uyeler,
                         ["Tracon.RunStatus.Completed = 2 -> Tracon.RunStatus"])

    def test_generic_tipin_parametre_listesi_atilir_ve_uyeleri_ona_baglanir(self):
        tipler = envanter.unshipped_ayristir("Tracon.Core", CORE_UNSHIPPED)
        self.assertIn("Tracon.Cache", tipler)
        self.assertEqual(len(tipler["Tracon.Cache"].uyeler), 1)

    def test_degistiricili_uye_satiri_sahibine_baglanir(self):
        tipler = envanter.unshipped_ayristir("Tracon.Core", CORE_UNSHIPPED)
        self.assertEqual(len(tipler["Tracon.TraconBuilderExtensions"].uyeler), 1)

    def test_ic_ice_tipin_uyesi_ic_tipe_baglanir_kapsayiciya_degil(self):
        tipler = envanter.unshipped_ayristir("Tracon.Core", CORE_UNSHIPPED)
        self.assertEqual(len(tipler["Tracon.Outer.Inner"].uyeler), 1)
        self.assertEqual(tipler["Tracon.Outer"].uyeler, [])


class BildirimTaramaTestleri(unittest.TestCase):
    def setUp(self):
        self.tipler = _envanter([])

    def test_tur_bildirimden_okunur(self):
        beklenen = {
            "Tracon.RunRecord": "record",
            "Tracon.RunStatus": "enum",
            "Tracon.IRunStore": "interface",
            "Tracon.RunStoreBase": "abstract",
            "Tracon.Outer": "static",
            "Tracon.HelperResult": "record",
            "Tracon.ProbeResult": "class",
        }
        for ad, tur in beklenen.items():
            self.assertEqual(_tip(self.tipler, ad).tur, tur, ad)

    def test_taban_listesi_kapanisa_girer(self):
        self.assertEqual(_tip(self.tipler, "Tracon.DerivedHandler").baslik_adlari,
                         {"Tracon.HandlerBase", "Tracon.IRunStore"})


class SiniflandirmaTestleri(unittest.TestCase):
    def test_kanitsiz_varsayilandir(self):
        tipler = _envanter([])
        self.assertEqual(_tip(tipler, "Tracon.Helper").sinif, "kanıtsız")

    def test_site_sayfasindaki_ad_tuketici_kanitidir(self):
        tipler = _envanter([("site", "docs-site/src/content/docs/a.md", "Call `Helper.Compute()`.")])
        self.assertEqual(_tip(tipler, "Tracon.Helper").sinif, "tüketici")
        self.assertEqual(_tip(tipler, "Tracon.Helper").gerekce, "site:docs-site/src/content/docs/a.md")

    def test_ad_alt_dize_olarak_eslesmez(self):
        tipler = _envanter([("site", "a.md", "HelperResultSet and MyHelper")])
        self.assertEqual(_tip(tipler, "Tracon.Helper").sinif, "kanıtsız")
        self.assertEqual(_tip(tipler, "Tracon.HelperResult").sinif, "kanıtsız")

    def test_kayit_giris_noktasi_korpus_olmadan_tuketicidir_ve_secenekleri_kapanisla_tutar(self):
        tipler = _envanter([])
        uzanti = _tip(tipler, "Tracon.TraconBuilderExtensions")
        self.assertEqual((uzanti.sinif, uzanti.gerekce), ("tüketici", "giris-noktasi:AddWidget"))
        self.assertEqual(_tip(tipler, "Tracon.WidgetOptions").gerekce, "imza:Tracon.TraconBuilderExtensions")
        self.assertEqual(_tip(tipler, "Tracon.WidgetMode").gerekce, "imza:Tracon.WidgetOptions")

    def test_onek_tasimayan_builder_uzantisi_kayit_giris_noktasidir(self):
        # Faz 189: `Configure` builder uzantısı oldu; önek kuralı onu kanıtsız bırakırdı.
        unshipped = (
            "Tracon.ConfigureOnlyExtensions\n"
            "static Tracon.ConfigureOnlyExtensions.Configure(this Tracon.ITraconBuilder! builder, "
            "System.Action<Tracon.TraconOptions!>! configure) -> Tracon.ITraconBuilder!\n")
        kaynak = "namespace Tracon;\npublic static class ConfigureOnlyExtensions { }\n"
        tipler = _envanter([], ek_paketler={"Tracon.Extra": (unshipped, kaynak)})
        tip = _tip(tipler, "Tracon.ConfigureOnlyExtensions", paket="Tracon.Extra")
        self.assertEqual((tip.sinif, tip.gerekce), ("tüketici", "giris-noktasi:Configure"))

    def test_uzanti_sinifi_metot_adiyla_kanit_bulur(self):
        tipler = _envanter([("sample", "samples/A/Program.cs", 'var s = "x".Shorten();')])
        self.assertEqual(_tip(tipler, "Tracon.StringExtensions").gerekce,
                         "uzanti:Shorten@sample:samples/A/Program.cs")

    def test_oznitelik_kisa_adiyla_kanit_bulur(self):
        tipler = _envanter([("sample", "samples/A/Tools.cs", "[SampleTool(\"x\")] public static int F() => 1;")])
        self.assertEqual(_tip(tipler, "Tracon.SampleToolAttribute").sinif, "tüketici")

    def test_oznitelik_kisa_adi_duz_metinde_kanit_degildir(self):
        tipler = _envanter([("site", "a.md", "The SampleTool mode is fast.")])
        self.assertEqual(_tip(tipler, "Tracon.SampleToolAttribute").sinif, "kanıtsız")

    def test_openapi_semasi_tuketici_kanitidir(self):
        tipler = _envanter([], semalar={"AgentDescriptor"})
        self.assertEqual(_tip(tipler, "Tracon.AgentDescriptor").gerekce, "http-sema:docs/openapi/tracon.json")

    def test_arayuz_ve_abstract_sinif_seamdir_ve_imzalarini_tutar(self):
        tipler = _envanter([])
        self.assertEqual(_tip(tipler, "Tracon.IRunStore").gerekce, "seam:arayuz")
        self.assertEqual(_tip(tipler, "Tracon.RunStoreBase").gerekce, "seam:abstract-sinif")
        self.assertEqual((_tip(tipler, "Tracon.RunRecord").sinif, _tip(tipler, "Tracon.RunRecord").gerekce),
                         ("seam", "imza:Tracon.IRunStore"))
        self.assertEqual(_tip(tipler, "Tracon.RunStatus").gerekce, "imza:Tracon.RunRecord")
        self.assertEqual(_tip(tipler, "Tracon.ProbeResult").gerekce, "imza:Tracon.RunStoreBase")

    def test_tuketici_kaniti_seamden_once_gelir(self):
        tipler = _envanter([("site", "a.md", "Read a `RunRecord` from the store.")])
        self.assertEqual(_tip(tipler, "Tracon.RunRecord").sinif, "tüketici")

    def test_kapanis_taban_sinifi_ve_kapsayiciyi_tutar(self):
        tipler = _envanter([("site", "a.md", "DerivedHandler and Inner")])
        self.assertEqual(_tip(tipler, "Tracon.HandlerBase").gerekce, "imza:Tracon.DerivedHandler")
        self.assertEqual(_tip(tipler, "Tracon.Outer").gerekce, "imza:Tracon.Outer.Inner")

    def test_generic_tipin_uye_imzasi_kapanisa_girer(self):
        tipler = _envanter([("site", "a.md", "Cache")])
        self.assertEqual(_tip(tipler, "Tracon.CacheEntry").gerekce, "imza:Tracon.Cache")

    def test_belgelenmis_istisna_ve_imzasi_kapanisa_girer(self):
        tipler = _envanter([])
        istisna = _tip(tipler, "Tracon.StoreFailedException")
        self.assertEqual((istisna.sinif, istisna.gerekce), ("seam", "istisna:Tracon.IThrowingStore"))
        self.assertEqual(_tip(tipler, "Tracon.StoreFailure").gerekce, "imza:Tracon.StoreFailedException")

    def test_belgelenmemis_istisna_kapanisa_girmez(self):
        tipler = _envanter([])
        self.assertEqual(_tip(tipler, "Tracon.UndocumentedException").sinif, "kanıtsız")

    def test_gerekceli_tip_kanitsizdan_ayrilir(self):
        tipler = _envanter([], gerekceler={"Tracon.Justified": "üretilmiş kod çağırır"})
        self.assertEqual((_tip(tipler, "Tracon.Justified").sinif, _tip(tipler, "Tracon.Justified").gerekce),
                         ("gerekçeli", "gerekçe:üretilmiş kod çağırır"))

    def test_sozlesme_paketi_butunuyle_seamdir(self):
        tipler = _envanter([], ek_paketler={
            "Tracon.Testing.Contracts.Xunit": (
                "Tracon.Testing.Contracts.StoreContract\nTracon.Testing.Contracts.StoreContract.Run() -> void\n",
                "namespace Tracon.Testing.Contracts; public class StoreContract { }"),
        })
        self.assertEqual(_tip(tipler, "Tracon.Testing.Contracts.StoreContract",
                              "Tracon.Testing.Contracts.Xunit").gerekce, "seam:sozlesme-paketi")

    def test_ayni_tam_ad_iki_pakette_iki_ayri_tiptir(self):
        paylasilan = ("Tracon.MigrationRunner\nTracon.MigrationRunner.ProviderName.get -> string!\n",
                      "namespace Tracon; public sealed class MigrationRunner { }")
        tipler = _envanter([("site", "a.md", "MigrationRunner")],
                           ek_paketler={"Tracon.PostgreSql": paylasilan, "Tracon.Sqlite": paylasilan})
        self.assertEqual(_tip(tipler, "Tracon.MigrationRunner", "Tracon.PostgreSql").sinif, "tüketici")
        self.assertEqual(_tip(tipler, "Tracon.MigrationRunner", "Tracon.Sqlite").sinif, "tüketici")


class YardimciTestleri(unittest.TestCase):
    def test_xml_ornekleri_yalniz_uclu_egik_yorumdan_okunur(self):
        metin = (
            "/// <example>\n/// <code>\n/// builder.AddWidget();\n/// </code>\n/// </example>\n"
            "// <code>Hidden()</code>\n")
        ornek = envanter.xml_ornekleri(metin)
        self.assertIn("AddWidget", ornek)
        self.assertNotIn("Hidden", ornek)

    def test_gerekcesiz_satir_hatadir(self):
        with self.assertRaises(ValueError):
            envanter.gerekceleri_oku("Tracon.Foo\t\n")

    def test_gerekce_satirlari_okunur_yorumlar_atlanir(self):
        self.assertEqual(envanter.gerekceleri_oku("# başlık\nTracon.Foo\tneden\n"), {"Tracon.Foo": "neden"})

    def test_bayat_gerekce_raporlanir(self):
        tipler = _envanter([("site", "a.md", "Helper")])
        sorunlar = envanter.bayat_gerekceler(tipler, {"Tracon.Yok": "x", "Tracon.Helper": "y"})
        self.assertEqual(len(sorunlar), 2)
        self.assertTrue(sorunlar[0].startswith("Tracon.Helper: artık 'tüketici'"))
        self.assertTrue(sorunlar[1].startswith("Tracon.Yok: public değil"))

    def test_tablo_toplam_satiri_siniflari_toplar(self):
        tipler = _envanter([("site", "a.md", "Helper")])
        tablo = envanter.tablo_yaz(tipler)
        self.assertIn("| `Tracon.Core` |", tablo)
        self.assertTrue(tablo.splitlines()[-1].startswith(f"| **Toplam** | **{len(tipler)}** |"))


if __name__ == "__main__":
    unittest.main()
