#!/usr/bin/env python3
"""Manuel kabul turu tazelik ölçümünün testleri."""
from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "manuel_test_tazelik", ROOT / "scripts" / "manuel-test-tazelik.py")
tazelik = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = tazelik
spec.loader.exec_module(tazelik)


CASE_TABAN = """### MT-CORE-001 — Geçerli tanım hatasız doğrulanır

**Ön koşul:** Örnek uygulama çalışır.

**Adımlar:** `POST /api/agents/validate`

**Beklenen sonuç:** `valid:true`, `messages:[]`

**Gerçek sonuç**
`valid:true`, `messages:[]`. Kayıt yok — beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı
"""

CASE_BUGUN = """### MT-CORE-001 — Geçerli tanım hatasız doğrulanır

**Ön koşul:** Örnek uygulama çalışır.

**Adımlar:** `POST /api/agents/validate`

**Beklenen sonuç:** `valid:true`, `messages:[]`
"""


class ImzaTestleri(unittest.TestCase):
    def test_kosum_kaydinin_silinmesi_case_i_DEGISMIS_saymaz(self):
        # K-414 spec ile koşum kaydını ayırdı ve `Gerçek sonuç`/`Durum`
        # bloklarını spec'ten sildi. Bu tek değişiklik normalize edilmezse
        # taban turdaki HER case "değişti" görünür: ölçüldü 1096 sahte, gerçek
        # 267.
        self.assertEqual(tazelik.case_imzalari(CASE_TABAN),
                         tazelik.case_imzalari(CASE_BUGUN))

    def test_yeniden_adlandirma_case_i_DEGISMIS_saymaz(self):
        # Faz 162 ürünü AgentPrism'den Tracon'a çevirdi ve her case gövdesine
        # dokundu.
        eski = CASE_BUGUN.replace("api/agents", "api/agentprism-agents")
        yeni = CASE_BUGUN.replace("api/agents", "api/tracon-agents")
        self.assertEqual(tazelik.case_imzalari(eski),
                         tazelik.case_imzalari(yeni))

    def test_fixture_adinin_yeniden_adlandirilmasi_DEGISMIS_saymaz(self):
        # Faz 162 ornek uygulamanin agent/workflow adlarini da cevirdi
        # (`ozetleyici`->`summarizer`). 2026-09-16 tazeleme turu sette 294
        # gecisi duzeltti; normalize edilmezse o duzeltme tek basina 98 case'i
        # "degisti" kovasina atardi.
        eski = CASE_BUGUN.replace("`valid:true`", "`ozetleyici` ve `cevirmen`")
        yeni = CASE_BUGUN.replace("`valid:true`", "`summarizer` ve `translator`")
        self.assertEqual(tazelik.case_imzalari(eski),
                         tazelik.case_imzalari(yeni))

    def test_GERCEK_metin_degisikligi_DEGISTI_sayilir(self):
        # 🚨 Negatif yön. Yukarıdaki iki test yalnız "sahte değişikliği ele"
        # yönünü kanıtlar; normalize her şeyi eleyecek kadar geniş olsaydı
        # ikisi de yeşil kalır ve ölçüm sessizce her case'i "sessiz" sayardı.
        degismis = CASE_BUGUN.replace("`valid:true`, `messages:[]`",
                                      "`valid:false` ve `unknown_tool` hatası")
        self.assertNotEqual(tazelik.case_imzalari(CASE_BUGUN),
                            tazelik.case_imzalari(degismis))

    def test_baslik_bicimli_case_bulunur(self):
        self.assertEqual(list(tazelik.case_imzalari(CASE_BUGUN)), ["MT-CORE-001"])

    def test_tablo_bicimli_case_bulunur(self):
        # Aile 31–36 case'i başlık değil TABLO SATIRI yazar. Bu biçim
        # `manuel_test_sayim_kaymasi` kapısına Faz 167'ye kadar görünmezdi.
        metin = ("| # | Kod | Ön koşul |\n"
                 "|---|---|---|\n"
                 "| 1 | `MT-GDK-001` | Temiz ağaç |\n"
                 "| 2 | `MT-GDK-002` | Temiz ağaç |\n")
        self.assertEqual(list(tazelik.case_imzalari(metin)),
                         ["MT-GDK-001", "MT-GDK-002"])


CASE_DEVREDILDI = """### MT-API-001 — `POST /api/agents` yeni bir tanım oluşturur

| | |
|---|---|
| **Önem** | Yüksek |
| **İlgili karar** | — |
| **Devir** | ➜ CI: `AgentCrudTests.Definition_is_created_and_appears_in_the_catalog` |

**Beklenen sonuç**
- `HTTP: 201`.
"""

CASE_MANUEL = """### MT-API-040 — Gerçek bir sağlayıcı hatası gruplanarak görünür

| | |
|---|---|
| **Önem** | Yüksek |
| **İlgili karar** | K-296 |
| **Devir** | 👤 insan gerekir — gerçek sağlayıcı hesabı ister |

**Beklenen sonuç**
- Dizide en az bir giriş vardır.
"""


class DevirIsaretiTestleri(unittest.TestCase):
    @staticmethod
    def _sinif(metin: str):
        blok = next(iter(tazelik.case_bloklari(metin).values()))
        return tazelik.devir_sinifi(blok)

    def test_ci_isareti_DEVREDILDI_sayilir_ve_hedefi_okunur(self):
        sinif, hedefler = self._sinif(CASE_DEVREDILDI)
        self.assertEqual(sinif, tazelik.DEVREDILDI)
        self.assertEqual(
            hedefler,
            ["AgentCrudTests.Definition_is_created_and_appears_in_the_catalog"])

    def test_insan_isareti_MANUEL_sayilir(self):
        self.assertEqual(self._sinif(CASE_MANUEL), (tazelik.MANUEL, []))

    def test_isaretsiz_case_ISARETSIZ_sayilir(self):
        # 🚨 Negatif yön. Sınıflandırıcı fazla geniş olsaydı her case bir
        # kovaya düşer ve "işaretsiz" sütunu sessizce sıfırlanırdı — devir
        # turunun ölçmek istediği tek sayı odur.
        self.assertEqual(self._sinif(CASE_BUGUN), (tazelik.ISARETSIZ, []))

    def test_ikisi_birden_yazilirsa_CI_kazanir(self):
        ikisi = CASE_DEVREDILDI.replace(
            "**Beklenen sonuç**",
            "👤 insan gerekir — eski not\n\n**Beklenen sonuç**")
        sinif, hedefler = self._sinif(ikisi)
        self.assertEqual(sinif, tazelik.DEVREDILDI)
        self.assertEqual(len(hedefler), 1)

    def test_hedefsiz_CI_isareti_DEVREDILDI_SAYILMAZ(self):
        # 🚨 Hedefi backtick'e alınmamış bir işaret doğrulanamaz. Devredilmiş
        # sayılsaydı bayat işaret kapısının göremediği bir kaçış yolu açılırdı:
        # case kanıtlıymış görünür, hiçbir test adı denetlenmez.
        hedefsiz = CASE_DEVREDILDI.replace(
            "➜ CI: `AgentCrudTests.Definition_is_created_and_appears_in_the_catalog`",
            "➜ CI: AgentCrudTests.Definition_is_created_and_appears_in_the_catalog")
        self.assertEqual(self._sinif(hedefsiz), (tazelik.ISARETSIZ, []))

    def test_tablo_bicimli_case_de_isaret_tasiyabilir(self):
        # Aile 31–36 case'i başlık değil TABLO SATIRI yazar; işaret bir `Devir`
        # SÜTUNUNDA yaşar. Sayaç iki biçimi de görmelidir.
        metin = ("| # | Kod | Ön koşul | Devir |\n"
                 "|---|---|---|---|\n"
                 "| 1 | `MT-GDK-001` | Temiz ağaç | ➜ CI: `KapiTestleri.test_tarama` |\n")
        blok = tazelik.case_bloklari(metin)["MT-GDK-001"]
        self.assertEqual(tazelik.devir_sinifi(blok),
                         (tazelik.DEVREDILDI, ["KapiTestleri.test_tarama"]))

    def test_tablo_bicimli_case_de_DEVIR_SUTUNU_imzadan_duser(self):
        # 🚨 Başlık biçimi için kurulan koruma tablo biçiminde de geçerlidir:
        # bir aileye `Devir` sütunu eklemek o ailenin HER case'ini "değişti"
        # kovasına atamamalıdır. Hücre boşaltılmaz, düşürülür — boş bırakmak
        # satıra fazladan bir ayırıcı ekler ve imza yine ayrışırdı.
        oncesi = ("| # | Kod | Ön koşul |\n"
                  "|---|---|---|\n"
                  "| 1 | `MT-GDK-001` | Temiz ağaç |\n")
        sonrasi = ("| # | Kod | Ön koşul | Devir |\n"
                   "|---|---|---|---|\n"
                   "| 1 | `MT-GDK-001` | Temiz ağaç | ➜ CI: `KapiTestleri.test_tarama` |\n")
        self.assertEqual(tazelik.case_imzalari(oncesi),
                         tazelik.case_imzalari(sonrasi))

    def test_govdedeki_insan_cumlesi_ISARET_SAYILMAZ(self):
        # 🚨 İşaretin TEK evi `Devir` satırı/sütunudur. Ön koşul metninde geçen
        # bir `👤` cümlesi sayılsaydı, devredilmemiş dört aile sahte `manuel`
        # üretirdi (ölçüldü: yedi case).
        metin = ("| # | Kod | Ön koşul |\n"
                 "|---|---|---|\n"
                 "| 1 | `MT-GDK-011` | 👤 insan gerekir — skill listesi açık |\n")
        blok = tazelik.case_bloklari(metin)["MT-GDK-011"]
        self.assertEqual(tazelik.devir_sinifi(blok), (tazelik.ISARETSIZ, []))

    def test_devir_satiri_case_i_DEGISMIS_saymaz(self):
        # 🚨 Bir case'i işaretlemek onun DAVRANIŞINI değiştirmez. İmzada
        # kalsaydı bu fazın 43 işareti bir sonraki tazelik ölçümünde 43 sahte
        # "değişti" üretir ve turun risk sırasını bozardı (ölçüldü: 0).
        isaretsiz = "\n".join(
            s for s in CASE_DEVREDILDI.split("\n")
            if not s.startswith("| **Devir**"))

        self.assertEqual(tazelik.case_imzalari(CASE_DEVREDILDI),
                         tazelik.case_imzalari(isaretsiz))


class TestEnvanteriTestleri(unittest.TestCase):
    def test_var_olan_test_envanterde_bulunur(self):
        envanter = tazelik.test_envanteri()
        self.assertIn(
            "ImzaTestleri.test_baslik_bicimli_case_bulunur", envanter)

    def test_sevk_edilen_sozlesme_testi_envanterde_bulunur(self):
        # Sözleşme testleri `src/Tracon.Testing.Contracts.Xunit` altındadır;
        # yalnız `tests/` taranınca bunlara giden işaret bayat görünüyordu.
        self.assertIn(
            "ToolApprovalRuleStoreContract."
            "A_path_carrying_separator_characters_does_not_merge_two_condition_sets",
            tazelik.test_envanteri())

    def test_var_OLMAYAN_test_envanterde_bulunmaz(self):
        # İki yönlü: yalnız ilk iddia yazılsaydı envanter her şeyi içeren bir
        # küme olabilir ve bayat işaret kapısı hiçbir zaman kırmızı olmazdı.
        self.assertNotIn(
            "ImzaTestleri.test_hic_boyle_bir_test_yok", tazelik.test_envanteri())


class BayatIsaretTestleri(unittest.TestCase):
    @staticmethod
    def _aile(bayat: list[tuple[str, str]]) -> tazelik.Aile:
        return tazelik.Aile(
            no="07", dosya="07-AILE.md", kod="API", fazlar="", yeni=[],
            degisti=[], sessiz=[], kod_kaymasi=0, yol_sayisi=1, cozulemeyen=[],
            yeni_dosya=False, devredildi=["MT-API-013"], manuel=[],
            isaretsiz=[], bayat_isaretler=bayat)

    def test_bayat_isaret_KIRMIZI_doner(self):
        bayat = tazelik.bayat_isaretler(
            [self._aile([("MT-API-013", "AgentCrudTests.Yok")])])
        self.assertEqual(bayat, [("07", "MT-API-013", "AgentCrudTests.Yok")])
        self.assertEqual(tazelik._bayat_bildir(bayat), 1)

    def test_temiz_set_YESIL_doner(self):
        self.assertEqual(tazelik.bayat_isaretler([self._aile([])]), [])
        self.assertEqual(tazelik._bayat_bildir([]), 0)


class TabansizOlcumTestleri(unittest.TestCase):
    def test_taban_yokken_tazelik_kovalari_BOS_kalir(self):
        # Tabansız modda karşılaştırılacak bir tur yoktur. Her case'i "yeni"
        # saymak devir raporunu bir tazelik ölçümü gibi gösterirdi.
        aileler = tazelik.aileleri_olc(None)
        self.assertTrue(aileler)
        self.assertEqual(sum(len(a.yeni) for a in aileler), 0)
        self.assertEqual(sum(len(a.degisti) for a in aileler), 0)

    def test_taban_yokken_devir_sayilari_DOLU_gelir(self):
        aileler = tazelik.aileleri_olc(None)
        yedi = next(a for a in aileler if a.no == "07")
        self.assertEqual(yedi.case_sayisi, len(yedi.devredildi)
                         + len(yedi.manuel) + len(yedi.isaretsiz))
        self.assertGreater(len(yedi.devredildi), 0)


class KisaYolTestleri(unittest.TestCase):
    def test_repo_ici_yol_KISALIR(self):
        self.assertEqual(
            tazelik._kisa_yol(ROOT / "docs" / "manuel-test" / "00-INDEKS.md"),
            "docs/manuel-test/00-INDEKS.md")

    def test_repo_DISI_yol_traceback_yerine_mutlak_yol_verir(self):
        # 🚨 `--kosum` repo dışında bir dizin alabilir. `relative_to` orada
        # `ValueError` atıyordu ve komut dosyayı YAZDIKTAN sonra ham bir
        # traceback ile düşüyordu — betiğin kendi kabul kuralı (`MT-GDK-022`)
        # ham traceback'i yasaklar.
        disarisi = pathlib.Path("/tmp/kosum-denemesi/00-KOSUM-PLANI.md")
        self.assertEqual(tazelik._kisa_yol(disarisi), str(disarisi))


class YolCozumuTestleri(unittest.TestCase):
    def test_brace_acilir(self):
        self.assertEqual(
            tazelik._brace_ac("src/A/{Bir,Iki}.cs"),
            ["src/A/Bir.cs", "src/A/Iki.cs"])

    def test_paket_kisaltmasi_cozulur(self):
        # Sütun `Core/Audit/AuditRecorder.cs` yazar, `src/Tracon.Core/...` değil.
        self.assertEqual(
            tazelik._tek_yol_coz("Core/Audit/AuditRecorder.cs", None, None),
            "src/Tracon.Core/Audit/AuditRecorder.cs")

    def test_frontend_goreli_yolu_cozulur(self):
        self.assertEqual(
            tazelik._tek_yol_coz("screens/dashboard.tsx", None, None),
            "src/Tracon.UI/frontend/src/screens/dashboard.tsx")

    def test_kardes_dosya_son_dizine_baglanir(self):
        yol = tazelik._tek_yol_coz(
            "AuditEndpoints.cs", "src/Tracon.AspNetCore",
            "src/Tracon.AspNetCore/Endpoints")
        self.assertEqual(yol, "src/Tracon.AspNetCore/Endpoints/AuditEndpoints.cs")

    def test_cokli_eslesen_ciplak_ad_TAHMIN_EDILMEZ(self):
        # 🚨 Yanlış dosyayı seçmek kod kaymasını sessizce yanlış ölçer.
        # Çözülememek en azından raporun §5 bölümünde görünür.
        self.assertIsNone(tazelik._tekil_temel_ad("README.md"))

    def test_tekil_eslesen_ciplak_ad_cozulur(self):
        self.assertEqual(tazelik._tekil_temel_ad("AuditRecorder.cs"),
                         "src/Tracon.Core/Audit/AuditRecorder.cs")

    def test_ayar_anahtari_yol_sayilmaz(self):
        # `AgentGraph.MaxDuration` bir yapılandırma anahtarıdır. Nokta taşıdığı
        # için yol sanılıyordu ve her ailede sahte bir "çözülemedi" üretiyordu.
        cozulen, cozulemeyen = tazelik.kaynak_yollari("`AgentGraph.MaxDuration`")
        self.assertEqual((cozulen, cozulemeyen), ([], []))

    def test_http_rotasi_sessizce_elenir(self):
        cozulen, cozulemeyen = tazelik.kaynak_yollari("`/api/mcp-servers/*`")
        self.assertEqual((cozulen, cozulemeyen), ([], []))

    def test_parantez_ici_aciklama_yoldan_ayiklanir(self):
        cozulen, _ = tazelik.kaynak_yollari(
            "`src/Tracon.Core/Audit/AuditRecorder.cs (yalnız yazma yolu)`")
        self.assertEqual(cozulen, ["src/Tracon.Core/Audit/AuditRecorder.cs"])

    def test_cozulemeyen_yol_RAPORLANIR(self):
        # Sessiz geçmek ölçümü sessizce yanlış yapar: aile kod kayması
        # olduğundan küçük görünür ve risk sırasında hak ettiği yerin altına
        # düşer.
        _, cozulemeyen = tazelik.kaynak_yollari("`src/Yok.Boyle.Bir/Paket.cs`")
        self.assertEqual(cozulemeyen, ["src/Yok.Boyle.Bir/Paket.cs"])


class SiralamaTestleri(unittest.TestCase):
    @staticmethod
    def _aile(no: str, yeni: int, kayma: int) -> tazelik.Aile:
        return tazelik.Aile(
            no=no, dosya=f"{no}-AILE.md", kod="X", fazlar="",
            yeni=[f"MT-X-{i}" for i in range(yeni)], degisti=[], sessiz=[],
            kod_kaymasi=kayma, yol_sayisi=1, cozulemeyen=[], yeni_dosya=False)

    def test_zincir_risk_sirasini_EZER(self):
        # `01 → 02 → 03 → 05 → 07` kırılırsa sonraki hiçbir aile anlamlı sonuç
        # vermez; riski düşük olsa da önce koşulur.
        aileler = [self._aile("13", 90, 5), self._aile("07", 0, 1)]
        self.assertEqual([a.no for a in tazelik.kosum_sirasi(aileler)],
                         ["07", "13"])

    def test_zincir_disi_aileler_risk_sirasinda(self):
        aileler = [self._aile("20", 1, 0), self._aile("13", 90, 5),
                   self._aile("36", 48, 85)]
        self.assertEqual([a.no for a in tazelik.kosum_sirasi(aileler)],
                         ["13", "36", "20"])

    def test_kod_kaymasi_tavanla_sinirlanir(self):
        # 85 commit ile 200 commit aynı şeyi söyler: "baştan oku". Tavan
        # olmazsa tek bir gürültülü aile sıralamanın tamamını ezer.
        aile = self._aile("36", 0, 500)
        self.assertEqual(aile.risk, tazelik.KAYMA_TAVANI)


class SeritDagilimiTestleri(unittest.TestCase):
    @staticmethod
    def _aile(no: str, case: int) -> tazelik.Aile:
        return tazelik.Aile(
            no=no, dosya=f"{no}-AILE.md", kod="X", fazlar="",
            yeni=[f"MT-X-{i}" for i in range(case)], degisti=[], sessiz=[],
            kod_kaymasi=0, yol_sayisi=1, cozulemeyen=[], yeni_dosya=False)

    def test_arayuz_ailesi_yarim_oturum_butcesi_kullanir(self):
        # Playwright case'i anlik goruntu + konsol kontrolu ister; ayni case
        # sayisi arayuzde daha cok oturum tutar (SKILL.md §3).
        self.assertEqual(self._aile("09", 36).oturum, 2)   # 36 / 18
        self.assertEqual(self._aile("13", 36).oturum, 2)   # 36 / 30 -> 2
        self.assertEqual(self._aile("09", 54).oturum, 3)
        self.assertEqual(self._aile("13", 54).oturum, 2)

    def test_tek_case_lik_aile_bir_oturum_tutar(self):
        self.assertEqual(self._aile("20", 1).oturum, 1)

    def test_zincir_aileleri_seritlere_DAGITILMAZ(self):
        # Zincir tek serit ve sirayla kosar; paralel dagitima girerse kapi
        # anlamini kaybeder.
        aileler = [self._aile(no, 30) for no in ("01", "02", "13", "20")]
        dagitilan = {a.no for s in tazelik.serit_dagilimi(aileler) for a in s}
        self.assertEqual(dagitilan, {"13", "20"})

    def test_aile_BOLUNMEZ_ve_tam_bir_seride_dusher(self):
        aileler = [self._aile(no, 30) for no in ("13", "20", "22", "23", "24")]
        seritler = tazelik.serit_dagilimi(aileler)
        dusen = [a.no for s in seritler for a in s]
        self.assertEqual(sorted(dusen), ["13", "20", "22", "23", "24"])
        self.assertEqual(len(dusen), len(set(dusen)))   # hicbiri iki seritte degil

    def test_yuk_seritler_arasinda_dengelenir(self):
        aileler = [self._aile(no, 30) for no in
                   ("13", "20", "22", "23", "24", "25", "26", "27")]
        yukler = [sum(a.oturum for a in s) for s in tazelik.serit_dagilimi(aileler)]
        self.assertLessEqual(max(yukler) - min(yukler), 1)


class CommitSayisiTestleri(unittest.TestCase):
    def test_bos_yol_listesi_SIFIR_doner(self):
        # 🚨 Tüm commit'leri döndürseydi, yolu çözülemeyen bir aile turun
        # tamamını kendi kaymasıymış gibi gösterir ve sıralamanın başına
        # yerleşirdi.
        self.assertEqual(tazelik.commit_sayisi("HEAD~1", []), 0)

    def test_gercek_yol_icin_commit_sayar(self):
        self.assertGreater(tazelik.toplam_commit("HEAD~3"), 0)


if __name__ == "__main__":
    unittest.main()
