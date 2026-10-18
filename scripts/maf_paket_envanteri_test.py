#!/usr/bin/env python3
"""`maf-paket-envanteri.py` testleri.

Betik canlı NuGet aramasına karşı ağdan koşar ve CI'da koşmaz. Burada ağ gerekmeyen her şey
sınanır: olgunluk ayrıştırması, fark kuralları, tabanın biçimi ve `Directory.Packages.props`
ile tutarlılığı. Son ikisi CI'da koşar: biri MAF paketi referansı ekleyip tabanı
güncellemezse bu dosya kırmızı olur.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import pathlib
import sys
import tempfile
import unittest
import urllib.error
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("maf_paket_envanteri", ROOT / "scripts" / "maf-paket-envanteri.py")
envanter = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = envanter
spec.loader.exec_module(envanter)

Row = envanter.Row


def satir(paket: str, olgunluk: str = "preview", durum: str = "degerlendirilmedi", not_: str = "") -> Row:
    return Row(paket, olgunluk, durum, not_)


def belge(**surumler: str) -> dict:
    """A NuGet search response; keyword names use `_` for `.`."""
    return {"totalHits": len(surumler),
            "data": [{"id": ad.replace("_", "."), "version": surum} for ad, surum in surumler.items()]}


class OlgunlukTestleri(unittest.TestCase):
    def test_etiketsiz_surum_ga_dir(self):
        self.assertEqual(envanter.maturity_of("1.25.0"), "ga")

    def test_etiket_noktali_ve_bitisik_yazilabilir(self):
        # `rc5` ve `rc1` gerçek NuGet sürümleridir (AzureAI 1.0.0-rc5, Purview 1.25.0-rc1).
        self.assertEqual(envanter.maturity_of("1.0.0-rc5"), "rc")
        self.assertEqual(envanter.maturity_of("1.25.0-rc1"), "rc")
        self.assertEqual(envanter.maturity_of("1.25.0-preview.261014.1"), "preview")
        self.assertEqual(envanter.maturity_of("1.13.0-preview.1.26513.7"), "preview")
        self.assertEqual(envanter.maturity_of("1.25.0-alpha.261014.1"), "alpha")

    def test_taninmayan_etiket_preview_sayilir_ve_raporda_kaybolmaz(self):
        self.assertEqual(envanter.maturity_of("2.0.0-beta.3"), "preview")


class TabanTestleri(unittest.TestCase):
    def yaz(self, icerik: str) -> pathlib.Path:
        klasor = pathlib.Path(tempfile.mkdtemp())
        self.addCleanup(lambda: [dosya.unlink() for dosya in klasor.iterdir()] and klasor.rmdir())
        yol = klasor / "taban.tsv"
        yol.write_text(icerik, encoding="utf-8")
        return yol

    def test_yazilan_taban_ayni_satirlarla_okunur_ve_siralidir(self):
        yol = self.yaz("")
        envanter.write_baseline([satir("B.Paket", "ga", "kullaniliyor"), satir("a.paket", "alpha", "karar", "K-1: gerekçe")], yol)

        okunan = envanter.read_baseline(yol)

        self.assertEqual([r.package for r in okunan], ["a.paket", "B.Paket"])
        self.assertEqual(okunan[0].note, "K-1: gerekçe")

    def test_dort_sutun_olmayan_satir_reddedilir(self):
        with self.assertRaisesRegex(ValueError, "4 sütun"):
            envanter.read_baseline(self.yaz("Paket\tga\tkarar\n"))

    def test_gecersiz_durum_ve_olgunluk_reddedilir(self):
        with self.assertRaisesRegex(ValueError, "durum"):
            envanter.read_baseline(self.yaz("Paket\tga\tbilinmiyor\t\n"))
        with self.assertRaisesRegex(ValueError, "olgunluk"):
            envanter.read_baseline(self.yaz("Paket\tfinal\tkarar\tK-1\n"))

    def test_gerekcesiz_karar_reddedilir(self):
        with self.assertRaisesRegex(ValueError, "gerekçesiz"):
            envanter.read_baseline(self.yaz("Paket\tga\tkarar\t\n"))

    def test_buyuk_kucuk_harfle_tekrar_reddedilir(self):
        with self.assertRaisesRegex(ValueError, "iki kez"):
            envanter.read_baseline(self.yaz("Paket\tga\tkullaniliyor\t\npaket\tga\tkullaniliyor\t\n"))

    def test_yorum_ve_bos_satirlar_atlanir(self):
        self.assertEqual(envanter.read_baseline(self.yaz("# yorum\n\nPaket\tga\tkullaniliyor\t\n")), [satir("Paket", "ga", "kullaniliyor")])


class FarkTestleri(unittest.TestCase):
    def turler(self, canli, satirlar, referans=()):
        return [(f.kind, f.package) for f in envanter.compare(canli, satirlar, set(referans))]

    def test_ayni_durumda_fark_yoktur(self):
        self.assertEqual(self.turler({"A.P": "1.0.0-preview.1"}, [satir("A.P", "preview")]), [])

    def test_tabanda_olmayan_paket_yenidir(self):
        self.assertEqual(self.turler({"A.Yeni": "1.0.0-alpha.1"}, []), [("YENİ", "A.Yeni")])

    def test_olgunluk_yukselirse_olgunlasti_der(self):
        self.assertEqual(self.turler({"A.P": "1.25.0"}, [satir("A.P", "preview")]), [("OLGUNLAŞTI", "A.P")])
        self.assertEqual(self.turler({"A.P": "1.0.0-rc2"}, [satir("A.P", "alpha")]), [("OLGUNLAŞTI", "A.P")])

    def test_olgunluk_duserse_geriledi_der(self):
        self.assertEqual(self.turler({"A.P": "1.0.0-alpha.1"}, [satir("A.P", "ga")]), [("GERİLEDİ", "A.P")])

    def test_aramada_olmayan_taban_satiri_gorunmuyordur(self):
        self.assertEqual(self.turler({}, [satir("A.Kayip")]), [("GÖRÜNMÜYOR", "A.Kayip")])

    def test_paket_kimligi_buyuk_kucuk_harf_duyarsizdir(self):
        self.assertEqual(self.turler({"A.Paket": "1.0.0"}, [satir("a.paket", "ga")]), [])

    def test_referansli_ama_tabanda_olmayan_paket_tutarsizdir(self):
        bulgular = self.turler({"A.P": "1.0.0"}, [satir("A.P", "ga", "degerlendirilmedi")], ["A.Baska"])

        self.assertEqual(bulgular, [("TUTARSIZ", "A.Baska")])

    def test_referansli_ama_durumu_referans_olmayan_paket_tutarsizdir(self):
        self.assertEqual(self.turler({"A.P": "1.0.0"}, [satir("A.P", "ga", "degerlendirilmedi")], ["A.P"]), [("TUTARSIZ", "A.P")])

    def test_referans_durumlu_ama_props_te_olmayan_paket_tutarsizdir(self):
        self.assertEqual(self.turler({"A.P": "1.0.0"}, [satir("A.P", "ga", "kullaniliyor")]), [("TUTARSIZ", "A.P")])

    def test_karar_ve_degerlendirilmedi_satirlari_referans_istemez(self):
        taban = [satir("A.K", "ga", "karar", "K-1"), satir("A.D", "ga", "degerlendirilmedi")]
        self.assertEqual(self.turler({"A.K": "1.0.0", "A.D": "1.0.0"}, taban), [])


class GuncelleTestleri(unittest.TestCase):
    def test_olgunluk_yenilenir_durum_ve_not_korunur(self):
        yenilenen = envanter.refresh({"A.P": "1.25.0"}, [satir("A.P", "preview", "karar", "K-9: gerekçe")])

        self.assertEqual(yenilenen, [Row("A.P", "ga", "karar", "K-9: gerekçe")])

    def test_yeni_paket_degerlendirilmedi_olarak_eklenir(self):
        yenilenen = envanter.refresh({"A.Yeni": "1.0.0-rc2"}, [])

        self.assertEqual(yenilenen, [Row("A.Yeni", "rc", "degerlendirilmedi", "")])

    def test_aramada_gorunmeyen_satir_silinmez(self):
        self.assertEqual(envanter.refresh({}, [satir("A.Kayip", "ga", "karar", "K-1")]), [satir("A.Kayip", "ga", "karar", "K-1")])

    def test_girdi_satirlari_yerinde_degistirilmez(self):
        girdi = [satir("A.P", "preview")]

        envanter.refresh({"A.P": "1.25.0"}, girdi)

        self.assertEqual(girdi[0].maturity, "preview")


class CanliOkumaTestleri(unittest.TestCase):
    def test_yalniz_aile_oneki_alinir_buyuk_kucuk_harf_duyarsiz(self):
        canli = envanter.live_family(belge(Microsoft_Agents_AI="1.25.0", microsoft_agents_ai_mcp="1.25.0-alpha.1", Baska_Paket="9.9.9"))

        self.assertEqual(sorted(canli), ["Microsoft.Agents.AI", "microsoft.agents.ai.mcp"])

    def test_sayfalar_toplam_sonuca_kadar_gezilir_ve_on_surum_istenir(self):
        sayfalar = [{"totalHits": 3, "data": [{"id": "A", "version": "1"}, {"id": "B", "version": "1"}]},
                    {"totalHits": 3, "data": [{"id": "C", "version": "1"}]}]
        istekler = []

        @contextlib.contextmanager
        def ac(adres, timeout):
            istekler.append(adres)
            yield io.BytesIO(json.dumps(sayfalar[len(istekler) - 1]).encode())

        with mock.patch.object(envanter, "PAGE_SIZE", 2):
            sonuc = envanter.fetch_live(("A",), open_url=ac)

        self.assertEqual([e["id"] for e in sonuc["data"]], ["A", "B", "C"])
        self.assertEqual(len(istekler), 2)
        self.assertIn("prerelease=true", istekler[0])
        self.assertIn("semVerLevel=2.0.0", istekler[0])
        self.assertIn("skip=2", istekler[1])

    def test_bos_sayfa_donguyu_bitirir(self):
        @contextlib.contextmanager
        def ac(adres, timeout):
            yield io.BytesIO(json.dumps({"totalHits": 50, "data": []}).encode())

        self.assertEqual(envanter.fetch_live(("A",), open_url=ac)["data"], [])


class GercekTabanTestleri(unittest.TestCase):
    """Ağ gerekmez. CI'da koşar."""

    def test_depodaki_taban_gecerlidir(self):
        satirlar = envanter.read_baseline(envanter.BASELINE)

        self.assertTrue(satirlar)
        for r in satirlar:
            self.assertTrue(r.package.startswith(envanter.PREFIXES[0]), r.package)

    def test_depodaki_taban_props_dosyasiyla_tutarlidir(self):
        # Birisi MAF paketi referansı ekler ya da kaldırır da tabanı güncellemezse bu test kırmızı olur.
        satirlar = envanter.read_baseline(envanter.BASELINE)
        referansli = envanter.referenced_packages(envanter.PROPS)

        sorunlar = [f for f in envanter.compare({}, satirlar, referansli) if f.kind == "TUTARSIZ"]

        self.assertEqual(sorunlar, [])

    def test_props_dosyasindaki_mafpaketleri_okunur(self):
        referansli = envanter.referenced_packages(envanter.PROPS)

        self.assertIn("Microsoft.Agents.AI", referansli)
        self.assertIn("Microsoft.Agents.AI.Workflows", referansli)


class UcTanUcaTestleri(unittest.TestCase):
    def kur(self, taban: str, props: str, canli: dict) -> list[str]:
        klasor = pathlib.Path(tempfile.mkdtemp())
        self.addCleanup(lambda: [dosya.unlink() for dosya in klasor.iterdir()] and klasor.rmdir())
        (klasor / "taban.tsv").write_text(taban, encoding="utf-8")
        (klasor / "props.xml").write_text(props, encoding="utf-8")
        (klasor / "arama.json").write_text(json.dumps(canli), encoding="utf-8")
        self.klasor = klasor
        return ["maf-paket-envanteri.py", "--dosya", str(klasor / "arama.json")]

    def calistir(self, argv: list[str]) -> tuple[int, str]:
        cikti = io.StringIO()
        with mock.patch.object(envanter, "BASELINE", self.klasor / "taban.tsv"), \
                mock.patch.object(envanter, "PROPS", self.klasor / "props.xml"), \
                mock.patch.object(sys, "argv", argv), contextlib.redirect_stdout(cikti), \
                contextlib.redirect_stderr(io.StringIO()):
            return envanter.main(), cikti.getvalue()

    PROPS = '<Project><ItemGroup><PackageVersion Include="Microsoft.Agents.AI" Version="1.0.0" /></ItemGroup></Project>'
    TABAN = "Microsoft.Agents.AI\tga\tkullaniliyor\t\n"

    def test_fark_yoksa_cikis_sifirdir(self):
        argv = self.kur(self.TABAN, self.PROPS, belge(Microsoft_Agents_AI="1.0.0"))

        kod, cikti = self.calistir(argv)

        self.assertEqual(kod, 0, cikti)
        self.assertIn("✅", cikti)

    def test_yeni_paket_cikis_birdir_ve_adi_basilir(self):
        argv = self.kur(self.TABAN, self.PROPS, belge(Microsoft_Agents_AI="1.0.0", Microsoft_Agents_AI_Yeni="0.1.0-alpha.1"))

        kod, cikti = self.calistir(argv)

        self.assertEqual(kod, 1)
        self.assertIn("YENİ", cikti)
        self.assertIn("Microsoft.Agents.AI.Yeni", cikti)

    def test_guncelle_yeni_paketi_basar_yazar_ve_kayitsiz_birakir(self):
        argv = self.kur(self.TABAN, self.PROPS, belge(Microsoft_Agents_AI="1.0.0", Microsoft_Agents_AI_Yeni="0.1.0-alpha.1"))

        kod, cikti = self.calistir(argv + ["--guncelle"])

        # Güncelleme `YENİ` sinyalini silmeden ÖNCE basar; sonra taban tutarlıdır.
        self.assertEqual(kod, 0, cikti)
        self.assertIn("YENİ", cikti)
        self.assertIn("ELLE", cikti)
        self.assertIn("Microsoft.Agents.AI.Yeni\talpha\tdegerlendirilmedi\t", (self.klasor / "taban.tsv").read_text(encoding="utf-8"))

    def test_indirme_hatasi_cikis_ikidir(self):
        self.kur(self.TABAN, self.PROPS, belge())
        with mock.patch.object(envanter, "fetch_live", side_effect=urllib.error.URLError("sertifika")):
            kod, _ = self.calistir(["maf-paket-envanteri.py"])

        self.assertEqual(kod, 2)


if __name__ == "__main__":
    unittest.main()
