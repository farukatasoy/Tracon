#!/usr/bin/env python3
"""`dokuman-bakim.py` testleri — stdlib `unittest`, yeni bağımlılık yok (K-007).

Dosya adı alt çizgi taşır (`dokuman-bakim_test.py` DEĞİL): `unittest discover`
`VALID_MODULE_NAME` deseni (`[_a-z]\\w*\\.py$`) tire taşıyan dosyaları SESSİZCE
atlar — ölçüldü, "Ran 0 tests" hiçbir hata vermeden döner (Faz 80, plandan
sapma). Kaynak dosya yine de tire taşır (CLI script konvansiyonu); bu yüzden
aşağıda `importlib` ile yüklenir, düz `import` ile değil.

Koşum: python3 -m unittest discover -s scripts -p "*_test.py"
"""
from __future__ import annotations

import importlib.util
import pathlib
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
_spec = importlib.util.spec_from_file_location(
    "dokuman_bakim", ROOT / "scripts" / "dokuman-bakim.py"
)
dokuman_bakim = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(dokuman_bakim)


class KuralEslesmesiTestleri(unittest.TestCase):
    """`_kural_eslesmesi` saf fonksiyonu — git veya dosya sistemi istemez."""

    def test_karsilanmayan_kural_kirmizidir(self):
        degisen = ["src/Tracon.Workflows/Foo.cs", "docs-site/src/content/docs/packages.md"]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        workflow = next(e for e in eslesme if e[0] == "workflow")
        self.assertFalse(workflow[3])

    def test_karsilanan_kural_yanlis_pozitif_uretmez(self):
        degisen = [
            "src/Tracon.Workflows/Foo.cs",
            "docs-site/src/content/docs/concepts/workflows.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        workflow = next(e for e in eslesme if e[0] == "workflow")
        self.assertTrue(workflow[3])

    def test_dizin_hedefi_alt_sayfayla_karsilanir(self):
        degisen = [
            "src/Tracon.Abstractions/IFoo.cs",
            "docs-site/src/content/docs/concepts/agents.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        kavram = next(e for e in eslesme if e[0] == "cekirdek-kavram")
        self.assertTrue(kavram[3])

    def test_dizin_hedefi_baska_sayfayla_karsilanmaz(self):
        degisen = [
            "src/Tracon.Abstractions/IFoo.cs",
            "docs-site/src/content/docs/packages.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        kavram = next(e for e in eslesme if e[0] == "cekirdek-kavram")
        self.assertFalse(kavram[3])

    def test_izlenmeyen_yol_da_girdi_olarak_kabul_edilir(self):
        # `_degisen_dosyalar` izlenmeyen (commit edilmemiş) dosyaları da dahil
        # eder; `_kural_eslesmesi` girdiyi olduğu gibi kabul eder, kaynağını
        # sormaz -- yeni bir site sayfası henüz commit edilmemiş olabilir.
        degisen = [
            "src/Tracon.UI/frontend/src/screens/Yeni.tsx",
            "docs-site/src/content/docs/ui.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        arayuz = next(e for e in eslesme if e[0] == "arayuz")
        self.assertTrue(arayuz[3])

    def test_buildtransitive_capabilities_hedefler(self):
        degisen = ["src/Tracon.Core/buildTransitive/Tracon.Core.targets"]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        adlar = {e[0] for e in eslesme}
        self.assertIn("buildtransitive", adlar)
        bt = next(e for e in eslesme if e[0] == "buildtransitive")
        self.assertEqual(bt[1], ("capabilities.md",))
        self.assertFalse(bt[3])
        # Aynı yol genel cekirdek-kavram kuralını da tetikler -- ikisi ayrı satır.
        self.assertIn("cekirdek-kavram", adlar)

    def test_http_api_kurali_uretilen_openapi_belgesiyle_karsilanir(self):
        # F-203: HTTP yuzeyi degistiginde bayatlayabilen IZLENEN cikti,
        # elle yazilmis sekil sayfasi degil, uretilip commit edilen OpenAPI
        # belgesidir. Hedef depo koku'ne gore cozulur.
        degisen = [
            "src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs",
            "docs/openapi/tracon.json",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        http = next(e for e in eslesme if e[0] == "http-api")
        self.assertTrue(http[3])

    def test_http_api_kurali_sekil_sayfasiyla_da_karsilanir(self):
        # Iki alternatif hedef: belge yeniden uretilmediyse SEKIL sayfasinin
        # elle guncellenmesi de kurali karsilar.
        degisen = [
            "src/Tracon.AspNetCore/OpenAICompat/Foo.cs",
            "docs-site/src/content/docs/http-api.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        http = next(e for e in eslesme if e[0] == "http-api")
        self.assertTrue(http[3])

    def test_http_api_kurali_hicbir_hedef_degismezse_kirmizidir(self):
        eslesme = dokuman_bakim._kural_eslesmesi(
            ["src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs"])
        http = next(e for e in eslesme if e[0] == "http-api")
        self.assertFalse(http[3])

    def test_depo_koku_hedefi_site_koku_altinda_ARANMAZ(self):
        # `docs/openapi/tracon.json` site icerik koku ile ONEKLENMEMELIDIR;
        # oneklenirse hicbir zaman eslesmez ve kural kalici kirmizi kalir --
        # F-203'un tam olarak duzelttigi kusur.
        degisen = [
            "src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs",
            "docs-site/src/content/docs/docs/openapi/tracon.json",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        http = next(e for e in eslesme if e[0] == "http-api")
        self.assertFalse(http[3])

    def test_tetiklenmeyen_kural_sonuca_girmez(self):
        self.assertEqual(dokuman_bakim._kural_eslesmesi(["README.md"]), [])

    def test_bos_kume_tuzagi(self):
        # SITE_KURALLARI boşalırsa hiçbir kural tetiklenmez ve kapı SESSİZCE
        # yeşil kalır. Gerçek sabiti kullanır (mock değil) -- boşalırsa düşer.
        self.assertGreater(len(dokuman_bakim.SITE_KURALLARI), 0)
        eslesme = dokuman_bakim._kural_eslesmesi(["src/Tracon.AspNetCore/Endpoints/Foo.cs"])
        self.assertGreater(len(eslesme), 0)


class SlugHesaplaTestleri(unittest.TestCase):
    """`_slug_hesapla` saf fonksiyonu."""

    def test_frontmatter_slug_oncelikli(self):
        self.assertEqual(dokuman_bakim._slug_hesapla("http-api.md", "http-api"), "http-api")

    def test_frontmatsiz_dosya_yolundan_turer(self):
        self.assertEqual(dokuman_bakim._slug_hesapla("concepts/agents.md", None), "concepts/agents")

    def test_kok_index_bos_sluga_duser(self):
        self.assertEqual(dokuman_bakim._slug_hesapla("index.mdx", None), "")

    def test_alt_dizin_index_dizin_slugu_alir(self):
        self.assertEqual(dokuman_bakim._slug_hesapla("getting-started/index.md", None), "getting-started")


class KirikBaglantilarTestleri(unittest.TestCase):
    """`kirik_baglantilar(kok=...)` — geçici bir dizin ağacında koşar."""

    def _kok_kur(self, tmp: pathlib.Path) -> None:
        docs = tmp / "docs-site" / "src" / "content" / "docs"
        (docs / "concepts").mkdir(parents=True)
        (docs / "http-api").mkdir(parents=True)
        (tmp / "docs-site" / "public").mkdir(parents=True)
        (docs / "capabilities.md").write_text("---\nslug: capabilities\n---\nX\n")
        (docs / "concepts" / "agents.md").write_text("Agents\n")

    def test_mdx_sayfasi_taranir(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[kırık](/yok-boyle-sayfa/)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertTrue(any("yok-boyle-sayfa" in k for k in kirik))

    def test_uretilen_sayfa_hedef_olarak_denetim_disi(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[ok](/http-api/schemas/) [ok2](/http-api/) [ok3](/api/)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertEqual(kirik, [])

    def test_uretilmeyen_openapi_dosyasi_hedef_olarak_denetim_disi(self):
        # `docs-site/public/openapi/tracon.json` `.gitignore`'dadir ve
        # yalniz `site` isinin `npm run build` -> `prebuild` zincirinde uretilir;
        # `build` isinde HENUZ yoktur. Bagimsiz denetimin buldugu kalici yanlis
        # pozitif: dosya gercekten yokken bile bu baglanti kirik SAYILMAMALI.
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            self.assertFalse((tmp / "docs-site" / "public" / "openapi").exists())
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[openapi](/openapi/tracon.json)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertEqual(kirik, [])

    def test_uretilen_sayfa_kaynak_olarak_taranmaz(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            (tmp / "docs-site" / "src" / "content" / "docs" / "http-api" / "sayfa.md").write_text(
                "[kırık](/yok-boyle-sayfa/)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertEqual(kirik, [])

    def test_slug_haritasi_frontmatli_ve_frontmatsiz_sayfayi_cozer(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[a](/capabilities/) [b](/concepts/agents/)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertEqual(kirik, [])

    def test_dosya_uzantili_hedef_public_ile_cozulur(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            (tmp / "docs-site" / "public" / "llms.txt").write_text("x")
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[llms](/llms.txt) [yok](/eksik.txt)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertEqual(len(kirik), 1)
            self.assertIn("eksik.txt", kirik[0])

    def test_dis_adres_ve_capa_denetim_disi(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[dış](https://example.com/x) [mail](mailto:a@b.com) [çapa](#giris)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertEqual(kirik, [])

    def test_repo_icindeki_nuget_cache_dokumanlari_taranmaz(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            paket = tmp / ".nuget" / "packages" / "dependency" / "1.0.0"
            paket.mkdir(parents=True)
            (paket / "README.md").write_text("[paketlenmemis](SECURITY.md)\n")

            kirik = dokuman_bakim.kirik_baglantilar(tmp)

            self.assertEqual(kirik, [])


class DenetleKirikBaglantiTestleri(unittest.TestCase):
    """`denetle()` kırık bağlantı sayısını çıkış koduna katmalı — bağımsız
    denetimin bulduğu kusur: eskiden `kirik_baglantilar()` yalnız YAZDIRILIYOR,
    `hata`'ya hiç katılmıyordu; `--denetle` kırık bağlantı sayısından BAĞIMSIZ
    olarak çıkış kodu 0 veriyordu."""

    def test_kirik_baglanti_varsa_cikis_kodu_1(self):
        with mock.patch.object(dokuman_bakim, "kirik_baglantilar", return_value=["a.md -> /yok/"]):
            self.assertEqual(dokuman_bakim.denetle(), 1)


class FazYasamDongusuTestleri(unittest.TestCase):
    """Kapanmış plan kökte kalırsa, arşivleme kapısı kırmızı olmalıdır."""

    def test_kokteki_tamamlanmis_faz_bulgudur(self):
        with tempfile.TemporaryDirectory() as t:
            kok = pathlib.Path(t)
            docs = kok / "docs"
            docs.mkdir()
            (docs / "90-ORNEK.md").write_text(
                "# Faz 90 — Örnek\n\n> **Durum:** ✅ Tamamlandı\n", encoding="utf-8")

            bulgular = dokuman_bakim.kapanmis_faz_bulgulari(kok)

            self.assertEqual(len(bulgular), 1)
            self.assertIn("90-ORNEK.md", bulgular[0])

    def test_planlanan_faz_kokte_kalabilir(self):
        with tempfile.TemporaryDirectory() as t:
            kok = pathlib.Path(t)
            docs = kok / "docs"
            docs.mkdir()
            (docs / "90-ORNEK.md").write_text(
                "# Faz 90 — Örnek\n\n> **Durum:** 📋 Planlandı\n", encoding="utf-8")

            self.assertEqual(dokuman_bakim.kapanmis_faz_bulgulari(kok), [])


class GitHatasiTestleri(unittest.TestCase):
    """`git` çağrısı düşerse kapı sessizce geçmez — çıkış kodu 1 (kullanıcı kararı)."""

    def test_git_basarisiz_none_doner(self):
        with mock.patch.object(dokuman_bakim.subprocess, "run") as m:
            m.return_value = mock.Mock(returncode=1, stdout="")
            self.assertIsNone(dokuman_bakim._git("status", "--porcelain"))

    def test_degisen_dosyalar_git_hatasinda_none_doner(self):
        with mock.patch.object(dokuman_bakim, "_git", return_value=None):
            self.assertIsNone(dokuman_bakim._degisen_dosyalar(None))

    def test_site_denetle_git_hatasinda_cikis_1_verir(self):
        with mock.patch.object(dokuman_bakim, "_degisen_dosyalar", return_value=None):
            self.assertEqual(dokuman_bakim.site_denetle(None, False), 1)


class KodBloguSoymaTestleri(unittest.TestCase):
    """`_kod_bloklarini_soy` saf fonksiyonu — dosya sistemi veya git istemez."""

    def test_fence_ici_bosaltilir_satir_sayisi_korunur(self):
        metin = "a\n```\ngizli\n```\nb"
        cikti = dokuman_bakim._kod_bloklarini_soy(metin)
        self.assertNotIn("gizli", cikti)
        self.assertEqual(len(metin.split("\n")), len(cikti.split("\n")))
        self.assertEqual(cikti.split("\n")[0], "a")
        self.assertEqual(cikti.split("\n")[-1], "b")

    def test_fence_disindaki_metin_dokunulmaz(self):
        metin = "[x](../yok.md)\n```\ny\n```"
        self.assertIn("[x](../yok.md)", dokuman_bakim._kod_bloklarini_soy(metin))

    def test_bilgi_dizeli_fence_acilir(self):
        cikti = dokuman_bakim._kod_bloklarini_soy("```markdown\n[a](../../b.md)\n```")
        self.assertNotIn("b.md", cikti)

    def test_alintili_ic_fence_dis_fencei_kapatmaz(self):
        # Damitilmis faz kaydinin sablonu tam bu sekli tasir: ```markdown
        # blogunun icinde `> ```bash` satirlari vardir. Kapanis fence'i satir
        # basinda olmadigi icin dis blok orada KAPANMAMALI.
        metin = "```markdown\n> ```bash\n> git show x\n> ```\n[a](../../b.md)\n```\nson"
        cikti = dokuman_bakim._kod_bloklarini_soy(metin)
        self.assertNotIn("b.md", cikti)
        self.assertEqual(cikti.split("\n")[-1], "son")

    def test_daha_uzun_fence_kisa_olanla_kapanmaz(self):
        metin = "````\n```\n[a](../../b.md)\n````\nson"
        cikti = dokuman_bakim._kod_bloklarini_soy(metin)
        self.assertNotIn("b.md", cikti)
        self.assertEqual(cikti.split("\n")[-1], "son")

    def test_tilde_fence_de_soyulur(self):
        self.assertNotIn("b.md", dokuman_bakim._kod_bloklarini_soy("~~~\n[a](b.md)\n~~~"))

    def test_tilde_fence_backtickle_kapanmaz(self):
        cikti = dokuman_bakim._kod_bloklarini_soy("~~~\n```\n[a](b.md)\n~~~\nson")
        self.assertNotIn("b.md", cikti)
        self.assertEqual(cikti.split("\n")[-1], "son")

    def test_liste_ogesindeki_girintili_fence_blok_acmaz(self):
        # `docs/manuel-test/30-YEREL-REFERANS.md:451` gercegi: numarali liste
        # icindeki kod blogunun KAPANIS fence'i girintilidir. Girintiliyi fence
        # sayarsak dosyanin geri kalani sessizce denetim disi kalir.
        metin = "3. ```bash\n   x\n   ```\n[a](yok.md)\nson"
        cikti = dokuman_bakim._kod_bloklarini_soy(metin)
        self.assertIn("[a](yok.md)", cikti)
        self.assertEqual(cikti.split("\n")[-1], "son")

    def test_kendi_satirinda_girintili_fence_blok_ACAR(self):
        # Faz 157: bu deponun faz sablonu numarali liste ICINDE, kendi satirinda,
        # uc bosluk girintili fence kullanir. Sutun 0 isteyen bir regex onu hic
        # gormuyordu: blok soyulmuyor, icindeki `## ...` gercek bir bolum
        # basligi saniliyordu. `faz-damit` bu yuzden Faz 157'nin kaydini IKIYE
        # KATLADI. Kardes test (`3. ```bash`) fence'in liste isaretcisiyle ayni
        # satirda oldugu bicimi korur; ikisi de ACMALI.
        metin = "2. Kararlar:\n   ```bash\n   awk '/## Sonraki/,0' x.md\n   ```\n[a](yok.md)\nson"
        cikti = dokuman_bakim._kod_bloklarini_soy(metin)
        self.assertNotIn("## Sonraki", cikti)
        self.assertIn("[a](yok.md)", cikti)
        self.assertEqual(cikti.split("\n")[-1], "son")

    def test_alintili_fence_girintili_desenle_de_kapatmaz(self):
        # Acilis girintiye izin verince kapanisin da izin vermesi CEKICIDIR ve
        # YANLISTIR: ```markdown blogu icindeki `> ```bash` ornegi dis blogu
        # kapatirdi. Kapanis deseni liste isaretcisi TANIMAZ ve `>` ile
        # baslayan satiri hic eslemez.
        metin = "```markdown\n> ```bash\n> git show x\n> ```\n[a](../../b.md)\n```\nson"
        cikti = dokuman_bakim._kod_bloklarini_soy(metin)
        self.assertNotIn("b.md", cikti)
        self.assertEqual(cikti.split("\n")[-1], "son")

    def test_kapanmamis_fence_dosya_sonuna_kadar_yutar(self):
        cikti = dokuman_bakim._kod_bloklarini_soy("a\n```\n[x](b.md)\nc")
        self.assertNotIn("b.md", cikti)
        self.assertEqual(cikti.split("\n")[0], "a")


class KirikBaglantiKodBloguTestleri(unittest.TestCase):
    """Fence soyma `kirik_baglantilar` icinde gercekten devrede mi — ve
    GERCEK kirik baglantilari maskeliyor mu (asil risk budur)."""

    def test_kod_blogundaki_ornek_baglanti_bulgu_uretmez(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            (tmp / "docs").mkdir()
            (tmp / "docs" / "a.md").write_text(
                "Sablon:\n\n```markdown\n[ADAYLAR.md](../../ADAYLAR.md)\n```\n"
            )
            self.assertEqual(dokuman_bakim.kirik_baglantilar(tmp), [])

    def test_kod_blogu_disindaki_kirik_baglanti_HALA_yakalanir(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            (tmp / "docs").mkdir()
            (tmp / "docs" / "a.md").write_text(
                "```\n[gizli](yok1.md)\n```\n[gercek](yok2.md)\n"
            )
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertTrue(any("yok2.md" in k for k in kirik), kirik)
            self.assertFalse(any("yok1.md" in k for k in kirik), kirik)

    def test_fence_sonrasi_baglanti_yeniden_taranir(self):
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            (tmp / "docs").mkdir()
            (tmp / "docs" / "a.md").write_text("```\nx\n```\n[a](yok.md)\n")
            self.assertTrue(any("yok.md" in k for k in dokuman_bakim.kirik_baglantilar(tmp)))


class TazelikDenetleTestleri(unittest.TestCase):
    """`tazelik_denetle` — üretilen dosya kaynağıyla aynı mı."""

    def test_bayat_dosya_bulgu_uretir(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "docs").mkdir()
            (tmp / "docs" / "X.md").write_text("BAYAT")
            sahte = (("docs/X.md", lambda: "TAZE"),)
            with mock.patch.object(dokuman_bakim, "_URETILEN", sahte):
                self.assertTrue(dokuman_bakim.tazelik_denetle(tmp))

    def test_taze_dosya_bulgu_uretmez(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "docs").mkdir()
            (tmp / "docs" / "X.md").write_text("TAZE")
            sahte = (("docs/X.md", lambda: "TAZE"),)
            with mock.patch.object(dokuman_bakim, "_URETILEN", sahte):
                self.assertEqual(dokuman_bakim.tazelik_denetle(tmp), [])

    def test_hicbir_sey_YAZMAZ(self):
        # `--denetle`nin "yazmaz" sözü: kapı diski değiştirirse denetim
        # kendi ölçtüğü şeyi bozar.
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "docs").mkdir()
            hedef = tmp / "docs" / "X.md"
            hedef.write_text("BAYAT")
            sahte = (("docs/X.md", lambda: "TAZE"),)
            with mock.patch.object(dokuman_bakim, "_URETILEN", sahte):
                dokuman_bakim.tazelik_denetle(tmp)
            self.assertEqual(hedef.read_text(), "BAYAT")


class GecmisIsaretciTestleri(unittest.TestCase):
    """`gecmis_isaretci_denetle` — GECMISI'ye yollayan kararın başlığı var mı."""

    def _kur(self, tmp: pathlib.Path, kararlar: str, gecmis: str) -> None:
        (tmp / "docs" / "arsiv").mkdir(parents=True)
        (tmp / "docs" / "KARARLAR.md").write_text(kararlar, encoding="utf-8")
        (tmp / "docs" / "arsiv" / "KARARLAR-GECMISI.md").write_text(gecmis, encoding="utf-8")

    def test_sarkan_isaretci_yakalanir(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._kur(tmp,
                      "| **K-9 — x** | 2026 | Ayrıntı: [`arsiv/KARARLAR-GECMISI.md`]"
                      "(arsiv/KARARLAR-GECMISI.md). | — |\n", "### K-8\n\nmetin\n")
            self.assertTrue(any("K-9" in b for b in dokuman_bakim.gecmis_isaretci_denetle(tmp)))

    def test_baslikli_karar_bulgu_uretmez(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._kur(tmp,
                      "| **K-9 — x** | 2026 | Ayrıntı: [`arsiv/KARARLAR-GECMISI.md`]"
                      "(arsiv/KARARLAR-GECMISI.md). | — |\n", "### K-9\n\nmetin\n")
            self.assertEqual(dokuman_bakim.gecmis_isaretci_denetle(tmp), [])

    def test_ciplak_atif_isaretci_SAYILMAZ(self):
        # "Kural `arsiv/KARARLAR-GECMISI.md` satır 1622'de yazılıydı" olgusal bir
        # ifadedir, "tam gerekçe orada" sözü değil (gerçek vaka: K-526).
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._kur(tmp,
                      "| **K-9 — x** | 2026 | Kural `arsiv/KARARLAR-GECMISI.md` "
                      "satır 1622'de yazılıydı. | — |\n", "### K-8\n\nmetin\n")
            self.assertEqual(dokuman_bakim.gecmis_isaretci_denetle(tmp), [])

    def test_egik_cizgili_baslik_iki_karari_da_cozer(self):
        # Gerçek biçim: `## K-512 / K-513 — ...`
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._kur(tmp,
                      "| **K-512 — x** | 2026 | [`arsiv/KARARLAR-GECMISI.md`]"
                      "(arsiv/KARARLAR-GECMISI.md) | — |\n"
                      "| **K-513 — y** | 2026 | [`arsiv/KARARLAR-GECMISI.md`]"
                      "(arsiv/KARARLAR-GECMISI.md) | — |\n",
                      "## K-512 / K-513 — birlikte\n\nmetin\n")
            self.assertEqual(dokuman_bakim.gecmis_isaretci_denetle(tmp), [])

    def test_dosya_yoksa_sessizce_bos_doner(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(dokuman_bakim.gecmis_isaretci_denetle(pathlib.Path(d)), [])


class TamMetinDenetleTestleri(unittest.TestCase):
    """`tam_metin_denetle` — damıtılmış kayıttaki SHA git'te çözülüyor mu."""

    def _faz(self, tmp: pathlib.Path, govde: str) -> None:
        (tmp / "docs" / "arsiv" / "fazlar").mkdir(parents=True)
        (tmp / "docs" / "arsiv" / "fazlar" / "01-X.md").write_text(govde, encoding="utf-8")

    def test_cozulmeyen_sha_yakalanir(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._faz(tmp, "> git show deadbee:docs/arsiv/fazlar/01-X.md\n")
            with mock.patch.object(dokuman_bakim, "_git", return_value=None):
                self.assertTrue(dokuman_bakim.tam_metin_denetle(tmp))

    def test_cozulen_sha_bulgu_uretmez(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._faz(tmp, "> git show deadbee:docs/arsiv/fazlar/01-X.md\n")
            with mock.patch.object(dokuman_bakim, "_git", return_value=[]):
                self.assertEqual(dokuman_bakim.tam_metin_denetle(tmp), [])

    def test_sha_tasimayan_kayit_git_cagirmaz(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            self._faz(tmp, "# Faz 1\n\nSHA yok.\n")
            with mock.patch.object(dokuman_bakim, "_git") as g:
                self.assertEqual(dokuman_bakim.tam_metin_denetle(tmp), [])
                g.assert_not_called()

    def test_arsiv_dizini_yoksa_bos_doner(self):
        with tempfile.TemporaryDirectory() as d:
            self.assertEqual(dokuman_bakim.tam_metin_denetle(pathlib.Path(d)), [])


class Faz91DokumanKapilariTestleri(unittest.TestCase):
    def test_tamamlanmis_fazdaki_isaretsiz_kutulari_bulur(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            fazlar = tmp / "docs" / "arsiv" / "fazlar"
            fazlar.mkdir(parents=True)
            (fazlar / "71-ORNEK.md").write_text(
                "> **Durum:** ✅ Tamamlandı\n\n- [ ] Bir\n- [ ] İki\n", encoding="utf-8")
            (fazlar / "72-PLAN.md").write_text(
                "> **Durum:** 📋 Planlandı\n\n- [ ] Açık\n", encoding="utf-8")

            bulgular = dokuman_bakim.tamamlanmis_faz_isaretsiz_kutular(tmp)

            self.assertEqual(len(bulgular), 2)
            self.assertTrue(all("71-ORNEK.md" in bulgu for bulgu in bulgular))

    def test_tamamlandi_esanlamlisi_yalin_yazimda_da_yakalanir(self):
        """Faz 91 denetimi: 24-SQLITE.md '✅ Kod tamam' yazıyordu, literal
        `Tamamlandı` deseni bunu kaçırıyordu — iki gerçek işaretsiz kutu
        denetimden görünmez kalmıştı."""
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            fazlar = tmp / "docs" / "arsiv" / "fazlar"
            fazlar.mkdir(parents=True)
            (fazlar / "24-ORNEK.md").write_text(
                "> **Durum:** ✅ Kod tamam · 205/205 test yeşil\n\n- [ ] AOT ölçülmedi\n",
                encoding="utf-8")
            (fazlar / "23-ORNEK.md").write_text(
                "> **Durum:** ✅ Tamam — 204/204 test yeşil\n\n- [ ] Açık kalem\n",
                encoding="utf-8")

            bulgular = dokuman_bakim.tamamlanmis_faz_isaretsiz_kutular(tmp)

            self.assertEqual(len(bulgular), 2)

    def test_dokuman_iddiasi_gercek_props_degeriyle_karsilastirilir(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "Directory.Build.props").write_text(
                "<EnablePublicApiTracking>true</EnablePublicApiTracking>", encoding="utf-8")
            skill = tmp / ".agents" / "skills" / "ornek"
            skill.mkdir(parents=True)
            (skill / "SKILL.md").write_text(
                "`EnablePublicApiTracking` bugün `false`.", encoding="utf-8")

            bulgular = dokuman_bakim.dokuman_iddia_cakismalari(tmp)

            self.assertEqual(len(bulgular), 1)
            self.assertIn("gerçek değer true", bulgular[0])

    def test_kapi_tanimlari_merkezi_delegasyonu_zorlar(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "scripts").mkdir()
            (tmp / "scripts" / "kapi.py").write_text("SECRET_PATTERN SYNC_ROOTS", encoding="utf-8")
            (tmp / ".github" / "workflows").mkdir(parents=True)
            (tmp / ".github" / "workflows" / "ci.yml").write_text(
                "python3 scripts/kapi.py tarama", encoding="utf-8")
            (tmp / "AGENTS.md").write_text(
                "python3 scripts/kapi.py kapanis --taban HEAD", encoding="utf-8")
            completion = tmp / ".agents" / "skills" / "faz-tamamlama"
            completion.mkdir(parents=True)
            (completion / "SKILL.md").write_text(
                "python3 scripts/kapi.py tarama", encoding="utf-8")
            ortak = tmp / ".agents" / "ortak"
            ortak.mkdir(parents=True)
            (ortak / "kurtarma.md").write_text(
                "`KR-12` → [kapilar.md](kapilar.md)", encoding="utf-8")

            self.assertEqual(dokuman_bakim.tekrarlanan_kapi_tanimlari(tmp), [])

    def test_kapi_tanimlari_eksik_dosya_ihlali_yakalanir(self):
        with tempfile.TemporaryDirectory() as d:
            bulgular = dokuman_bakim.tekrarlanan_kapi_tanimlari(pathlib.Path(d))

            self.assertEqual(bulgular, ["kapi.py veya delegasyon hedefi eksik"])

    def test_kapi_tanimlari_yalniz_kurtarma_eksikse_de_yakalanir(self):
        """Faz 168 denetimi (🟡 5): dort hedef varken YALNIZ `kurtarma.md`
        eksikse varlik dali hic tetiklenmiyordu -- dal tuple'dan dusurulse
        testler yesil kalirdi. Bu vaka o dali tek basina kirmizi yapar."""
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "scripts").mkdir()
            (tmp / "scripts" / "kapi.py").write_text("SECRET_PATTERN SYNC_ROOTS", encoding="utf-8")
            (tmp / ".github" / "workflows").mkdir(parents=True)
            (tmp / ".github" / "workflows" / "ci.yml").write_text(
                "python3 scripts/kapi.py tarama", encoding="utf-8")
            (tmp / "AGENTS.md").write_text(
                "python3 scripts/kapi.py kapanis --taban HEAD", encoding="utf-8")
            completion = tmp / ".agents" / "skills" / "faz-tamamlama"
            completion.mkdir(parents=True)
            (completion / "SKILL.md").write_text(
                "python3 scripts/kapi.py tarama", encoding="utf-8")
            # `.agents/ortak/kurtarma.md` BILEREK yazilmadi.

            bulgular = dokuman_bakim.tekrarlanan_kapi_tanimlari(tmp)

            self.assertEqual(bulgular, ["kapi.py veya delegasyon hedefi eksik"])

    def test_kapi_tanimlari_ihlalleri_tek_tek_yakalanir(self):
        """Faz 91 denetimi: mutlu yol boş liste döndüğünü doğruluyordu ama
        hiçbir ihlal dalı tetiklenmiyordu — biri bozulsa hiçbir test kırılmazdı."""
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "scripts").mkdir()
            (tmp / "scripts" / "kapi.py").write_text("eski script, desen yok", encoding="utf-8")
            (tmp / ".github" / "workflows").mkdir(parents=True)
            (tmp / ".github" / "workflows" / "ci.yml").write_text(
                "find src tests samples docs .agents -name '* 2.*'", encoding="utf-8")
            (tmp / "AGENTS.md").write_text(
                "dotnet build  Tracon.slnx\n"
                "dotnet test   Tracon.slnx\n"
                "dotnet pack   Tracon.slnx\n"
                "dotnet format Tracon.slnx\n",
                encoding="utf-8")
            completion = tmp / ".agents" / "skills" / "faz-tamamlama"
            completion.mkdir(parents=True)
            (completion / "SKILL.md").write_text(
                "Password|pwd deseniyle secret taranır.", encoding="utf-8")
            ortak = tmp / ".agents" / "ortak"
            ortak.mkdir(parents=True)
            (ortak / "kurtarma.md").write_text(
                "`KR-04` → python3 scripts/kapi.py kapanis --taban HEAD", encoding="utf-8")

            bulgular = dokuman_bakim.tekrarlanan_kapi_tanimlari(tmp)

            self.assertEqual(len(bulgular), 9)
            self.assertIn("scripts/kapi.py sync/secret desenlerinin kaynağı değil", bulgular)
            self.assertIn(".github/workflows/ci.yml kapi.py taramasını çağırmıyor", bulgular)
            self.assertIn(".github/workflows/ci.yml eski sync/secret desenini taşıyor", bulgular)
            self.assertIn("AGENTS.md kapanış kapısını kapi.py'ye devretmiyor", bulgular)
            self.assertIn("AGENTS.md dört ham kapanış komutunu kopyalıyor", bulgular)
            self.assertIn("faz-tamamlama sync/secret taramasını kapi.py'ye devretmiyor", bulgular)
            self.assertIn("faz-tamamlama eski sync/secret desenini taşıyor", bulgular)
            self.assertIn(
                "kurtarma.md ham kapanış komutunu kopyalıyor; kapilar.md'ye bağlanmalı", bulgular)
            self.assertIn("kurtarma.md kapı sözleşmesine bağlanmıyor", bulgular)


FAZ_ORNEK = """# Faz 42 — Örnek

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Paketler:** `Tracon.Core`

## Bu Faza Başlarken

Bunu oku, şunu oku.

## Amaç

Birinci cümle. İkinci cümle. Üçüncü cümle.

## 42.1 — İlk tasarım

Plan gövdesi.

## Planlanan Public API

```csharp
public interface IX { }
```

## Gerçekleşen Public API

```csharp
public interface IX { void Y(); }
```

## Bitiş Ölçütleri (DoD)

- [x] Bir ✅
- [ ] AOT ölçülmedi

## Plandan Sapmalar

Plan A dedi, gerçek B çıktı.

## Açık Kalan

Gerçek sunucu koşturulamadı.

## Sonraki Faza Devir Notu

Şu sözleşmeyi devral.
"""


class FazBolumleriTestleri(unittest.TestCase):
    def test_baslik_blogu_ve_bolumler_ayrilir(self):
        bas, bol = dokuman_bakim._faz_bolumleri(FAZ_ORNEK)
        self.assertIn("**Durum:**", bas)
        self.assertNotIn("## ", bas)
        self.assertEqual(bol[0][0], "Bu Faza Başlarken")
        self.assertTrue(bol[0][1].startswith("## Bu Faza Başlarken"))

    def test_bolumsuz_metin_bozulmaz(self):
        bas, bol = dokuman_bakim._faz_bolumleri("# X\n\nyalnız gövde\n")
        self.assertEqual(bol, [])
        self.assertIn("yalnız gövde", bas)


class FazDamitmaTestleri(unittest.TestCase):
    def _damit(self, metin=FAZ_ORNEK):
        return dokuman_bakim._faz_damit_metni(
            metin, tam_sha="abc1234", yol="docs/arsiv/fazlar/42-X.md", bugun="2026-08-23")

    def test_durum_satiri_damitmadan_sonra_AYNI_yerde(self):
        # `yol_haritasi_uret()` bu iki satiri okur; kayarsa faz YOL-HARITASI'ndan
        # sessizce duser ve hicbir hata verilmez.
        yeni, _ = self._damit()
        self.assertTrue(yeni.startswith("# Faz 42 — Örnek"))
        self.assertRegex(yeni, r"(?m)^>\s*\*\*Durum:\*\*\s*✅ Tamamlandı")

    def test_kalici_bolumler_BIRE_BIR_korunur(self):
        yeni, _ = self._damit()
        for b in ("## Plandan Sapmalar\n\nPlan A dedi, gerçek B çıktı.",
                  "## Sonraki Faza Devir Notu\n\nŞu sözleşmeyi devral."):
            self.assertIn(b, yeni)

    def test_plan_bolumleri_duser(self):
        yeni, _ = self._damit()
        for d in ("## Bu Faza Başlarken", "## 42.1", "## Planlanan Public API",
                  "## Gerçekleşen Public API"):
            self.assertNotIn(d, yeni)

    def test_tek_dodun_isaretsiz_kutusu_KORUNUR(self):
        # `24-SQLITE.md` gercegi: "AOT olculmedi" kapanmamis bir isi kaydeder.
        yeni, _ = self._damit()
        self.assertIn("- [ ] AOT ölçülmedi", yeni)

    def test_iki_dod_varsa_planin_isaretsiz_kopyasi_duser(self):
        metin = FAZ_ORNEK.replace(
            "## Bitiş Ölçütleri (DoD)\n\n- [x] Bir ✅\n- [ ] AOT ölçülmedi",
            "## Bitiş Ölçütleri (DoD)\n\n- [ ] Plan kutusu\n\n"
            "## Bitiş Ölçütleri (DoD) — sonuç\n\n- [x] Gerçekleşen ✅")
        yeni, _ = dokuman_bakim._faz_damit_metni(
            metin, tam_sha="a", yol="x.md", bugun="2026-08-23")
        self.assertNotIn("Plan kutusu", yeni)
        self.assertIn("Gerçekleşen ✅", yeni)

    def test_taninmayan_bolum_DUSURULMEZ_uyari_uretir(self):
        yeni, uyari = self._damit()
        self.assertIn("## Açık Kalan", yeni)
        self.assertTrue(any("Açık Kalan" in u for u in uyari))

    def test_amac_cumle_sinirinda_kirpilir(self):
        uzun = FAZ_ORNEK.replace("Birinci cümle. İkinci cümle. Üçüncü cümle.",
                                 ("Kısa cümle. " + "Çok uzun bir cümle daha. " * 40).strip())
        yeni, _ = dokuman_bakim._faz_damit_metni(
            uzun, tam_sha="a", yol="x.md", bugun="2026-08-23")
        _, bol = dokuman_bakim._faz_bolumleri(yeni)
        amac = next(g for a, g in bol if a == "Amaç")
        self.assertLessEqual(len(amac.encode()), dokuman_bakim.AMAC_SINIRI + 120)
        self.assertTrue(amac.rstrip().endswith("."), amac[-40:])

    def test_tam_metin_komutu_yazilir(self):
        yeni, _ = self._damit()
        self.assertIn("git show abc1234:docs/arsiv/fazlar/42-X.md", yeni)

    def test_uretilen_metinde_markdown_link_deseni_YOK(self):
        # Damitma blogu `](` tasirsa `kirik_baglantilar()` yanlis pozitif uretir.
        yeni, _ = self._damit()
        blok = yeni[yeni.index(dokuman_bakim.DAMITMA_ISARETI):yeni.index("\n---\n")]
        self.assertNotIn("](", blok)

    def test_IDEMPOTENT(self):
        bir, _ = self._damit()
        iki, uyari = self._damit(bir)
        self.assertEqual(bir, iki)
        self.assertEqual(uyari, [])

    def test_imza_anlik_goruntusu_ve_dosya_listesi_duser(self):
        for baslik in ("## Doğrulanmış MAF API'si", "## Kullanılan MAF API'si (reflection)",
                       "## Gerçekleşen Dosya Listesi", "## Üretilen Dosyalar",
                       "## Yeni HTTP Uçları"):
            metin = FAZ_ORNEK.replace("## Açık Kalan", f"{baslik}\n\nGÖVDE\n\n## Açık Kalan")
            yeni, _ = dokuman_bakim._faz_damit_metni(
                metin, tam_sha="a", yol="x.md", bugun="2026-08-23")
            self.assertNotIn("GÖVDE", yeni, baslik)


KOSUM_ORNEK = """# 23 — Saklama (`RET`) — Koşum Kaydı (2026-08-13)

> Bu dosya bir koşum kaydıdır, spesifikasyon değildir.

---

## MT-RET-001 — Temiz geçen

**Gerçek sonuç**
- Her şey beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-RET-002 — Geçti ama düzeltme gerekiyor

**Gerçek sonuç**
- Çalıştı.

⚠️ **Doküman düzeltmesi gerekiyor:** `INSERT` güncel şemayla uyuşmuyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

## MT-RET-003 — Kaldı

**Gerçek sonuç**
- `500` döndü, beklenen `200`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı
"""


class KosumDamitmaTestleri(unittest.TestCase):
    def test_temiz_gecen_case_tek_satira_iner(self):
        yeni, sayac = dokuman_bakim._kosum_damit_metni(KOSUM_ORNEK)
        self.assertIn("| MT-RET-001 | ☑ | Temiz geçen |", yeni)
        self.assertNotIn("Her şey beklendiği gibi", yeni)
        self.assertEqual(sayac["daraltilan"], 1)

    def test_gecti_ama_ISARETLI_case_BIRE_BIR_korunur(self):
        # Gerçek vaka: MT-RET-001 "Geçti" olduğu hâlde iki doküman düzeltmesi
        # kaydediyordu. Ölçüldü: geçen 1061 case'in 254'ü işaret taşıyor.
        yeni, sayac = dokuman_bakim._kosum_damit_metni(KOSUM_ORNEK)
        self.assertIn("⚠️ **Doküman düzeltmesi gerekiyor:**", yeni)
        self.assertIn("`INSERT` güncel şemayla uyuşmuyor", yeni)

    def test_gecmeyen_case_blogu_BIRE_BIR_korunur(self):
        yeni, _ = dokuman_bakim._kosum_damit_metni(KOSUM_ORNEK)
        self.assertIn("`500` döndü, beklenen `200`.", yeni)

    def test_korunan_case_sayisi_dogru(self):
        _, sayac = dokuman_bakim._kosum_damit_metni(KOSUM_ORNEK)
        self.assertEqual(sayac["korunan"], 2)

    def test_spec_baglantisi_ve_baslik_korunur(self):
        yeni, _ = dokuman_bakim._kosum_damit_metni(KOSUM_ORNEK)
        self.assertTrue(yeni.startswith("# 23 — Saklama"))
        self.assertIn("spesifikasyon değildir", yeni)

    def test_durum_satiri_olmayan_case_korunur(self):
        # Ölçüldü: 20 case durum satırı taşımıyor. "Geçti" varsayılamaz.
        metin = KOSUM_ORNEK.replace(
            "**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı\n\n## MT-RET-002",
            "## MT-RET-002")
        yeni, _ = dokuman_bakim._kosum_damit_metni(metin)
        self.assertIn("Her şey beklendiği gibi", yeni)

    def test_IDEMPOTENT(self):
        bir, _ = dokuman_bakim._kosum_damit_metni(KOSUM_ORNEK)
        iki, sayac = dokuman_bakim._kosum_damit_metni(bir)
        self.assertEqual(bir, iki)
        self.assertEqual(sayac["daraltilan"], 0)

    def test_case_tasimayan_dosya_dokunulmaz(self):
        metin = "# Kapanış Planı\n\nDüz metin.\n"
        yeni, _ = dokuman_bakim._kosum_damit_metni(metin)
        self.assertEqual(yeni, metin)


class KararSatiriDamitmaTestleri(unittest.TestCase):
    """`_karar_satiri_damit` saf fonksiyonu."""

    def _satir(self, gerekce: str, baslik="K-100 — kısa başlık", kosul="Yeniden ölçülürse"):
        return f"| **{baslik}** | 2026-08-01 | {gerekce} | {kosul} |"

    def test_sinirin_altindaki_satir_dokunulmaz(self):
        s = self._satir("Kısa gerekçe.")
        self.assertEqual(dokuman_bakim._karar_satiri_damit(s), (s, None, None))

    def test_baslik_tarih_ve_kosul_ASLA_kesilmez(self):
        s = self._satir("Birinci cümle. " + "Uzun bir gerekçe cümlesi. " * 40)
        yeni, tasinan, sebep = dokuman_bakim._karar_satiri_damit(s)
        self.assertIsNone(sebep)
        self.assertTrue(yeni.startswith("| **K-100 — kısa başlık** | 2026-08-01 |"))
        self.assertTrue(yeni.rstrip().endswith("| Yeniden ölçülürse |"))
        self.assertIn("Birinci cümle.", yeni)

    def test_kesilen_kuyruk_dondurulur(self):
        s = self._satir("Bir. " + "İki üç dört beş altı. " * 40)
        _, tasinan, _ = dokuman_bakim._karar_satiri_damit(s)
        self.assertTrue(tasinan)
        self.assertIn("İki üç dört beş altı.", tasinan)

    def test_isaretci_eklenir(self):
        s = self._satir("Bir. " + "Uzun cümle burada. " * 40)
        yeni, _, _ = dokuman_bakim._karar_satiri_damit(s)
        self.assertIn("KARARLAR-GECMISI.md", yeni)
        self.assertIn("— K-100.", yeni)

    def test_yeniden_acildi_kuyrukta_kalirsa_ATLANIR(self):
        # `_kararlar_kalemleri()` 🔁'yi satırın TAMAMINDA arar; kesilirse
        # indeks sessizce yanlış olur.
        s = self._satir("Bir. " + "Dolgu cümlesi. " * 40 + "Bu karar yeniden açıldı.")
        yeni, tasinan, sebep = dokuman_bakim._karar_satiri_damit(s)
        self.assertEqual(yeni, s)
        self.assertIsNone(tasinan)
        self.assertIn("👤/🔁", sebep)

    def test_kullanici_karari_kuyrukta_kalirsa_ATLANIR(self):
        s = self._satir("Bir. " + "Dolgu cümlesi. " * 40 + "Bu bir kullanıcı kararı.")
        yeni, _, sebep = dokuman_bakim._karar_satiri_damit(s)
        self.assertEqual(yeni, s)
        self.assertIn("👤/🔁", sebep)

    def test_gerekce_icindeki_boru_satiri_bozmaz(self):
        # Ölçüldü: 8 satırın gerekçesinde fazladan `|` var (tablo/kod).
        s = self._satir("Bir. " + "A | B tablosu var. " * 40)
        yeni, _, sebep = dokuman_bakim._karar_satiri_damit(s)
        self.assertIsNone(sebep)
        self.assertTrue(yeni.rstrip().endswith("| Yeniden ölçülürse |"))

    def test_IDEMPOTENT(self):
        s = self._satir("Bir. " + "Uzun cümle burada. " * 40)
        bir, _, _ = dokuman_bakim._karar_satiri_damit(s)
        iki, tasinan, _ = dokuman_bakim._karar_satiri_damit(bir)
        self.assertEqual(bir, iki)
        self.assertIsNone(tasinan)

    def test_ISARETCILI_ama_UZUN_satir_yine_damitilir(self):
        # Faz 153: isaretci formati satirlarin cogunda ELLE, kirpma olmadan
        # uygulanmisti; "isaretcisi var, atla" kurali 374 KB'lik bir gerekce
        # yiginini damitma disinda birakiyordu.
        uzun = "Bir cümle. " + "Dolgu cümlesi burada. " * 40
        isaretci = ("**Tam gerekçe:** [`arsiv/KARARLAR-GECMISI.md`]"
                    "(arsiv/KARARLAR-GECMISI.md) — K-100.")
        s = self._satir(f"{uzun} {isaretci}")

        yeni, tasinan, sebep = dokuman_bakim._karar_satiri_damit(s)

        self.assertIsNone(sebep)
        self.assertTrue(tasinan)
        self.assertLess(len(yeni.encode()), len(s.encode()))
        self.assertIn("Bir cümle.", yeni)
        self.assertEqual(yeni.count("KARARLAR-GECMISI.md"), 2)  # tek isaretci: metin + link
        self.assertNotIn("Tam gerekçe", tasinan)

        # Ikinci gecis bir sey yapmaz.
        iki, tasinan_iki, _ = dokuman_bakim._karar_satiri_damit(yeni)
        self.assertEqual(yeni, iki)
        self.assertIsNone(tasinan_iki)

    def test_uzun_iskelette_bile_ilk_cumle_durur(self):
        s = self._satir("Bir cümle. " + "Dolgu. " * 40,
                        baslik="K-100 — " + "çok uzun bir başlık " * 12,
                        kosul="çok uzun bir yeniden açılma koşulu " * 8)
        yeni, tasinan, sebep = dokuman_bakim._karar_satiri_damit(s)
        self.assertIsNone(sebep)
        self.assertIn("Bir cümle.", yeni)
        self.assertLess(len(yeni.encode()), len(s.encode()))


class GecmiseTasimaTestleri(unittest.TestCase):
    """`_gecmise_tasinan_metin` — hedef `docs/arsiv/` altında; göreli
    bağlantılar oraya göre yeniden yazılmalı. Ölçüldü: bu düzeltme olmadan
    damıtma 292 kırık bağlantı üretti."""

    def test_kendi_kendine_isaretci_silinir(self):
        m = ("Ölçüm şu. **Tam gerekçe:** [`arsiv/KARARLAR-GECMISI.md`]"
             "(arsiv/KARARLAR-GECMISI.md) — K-9.")
        self.assertEqual(dokuman_bakim._gecmise_tasinan_metin(m), "Ölçüm şu.")

    def test_arsiv_oneki_dusurulur(self):
        m = "Bkz. [`28-SES`](arsiv/fazlar/28-SES-TOOLLARI.md) dosyası."
        self.assertIn("](fazlar/28-SES-TOOLLARI.md)", dokuman_bakim._gecmise_tasinan_metin(m))

    def test_arsiv_disi_baglanti_dokunulmaz(self):
        m = "Bkz. [`MIMARI`](../MIMARI.md)."
        self.assertIn("](../MIMARI.md)", dokuman_bakim._gecmise_tasinan_metin(m))

    def test_baglantisiz_metin_bozulmaz(self):
        self.assertEqual(dokuman_bakim._gecmise_tasinan_metin("  Düz metin.  "), "Düz metin.")


class DizinButcesiTestleri(unittest.TestCase):
    """`HARIC` muafiyeti ve üçlü anahtar."""

    def test_arsiv_butcesi_SIFIR_olcmez(self):
        # 🚨 `_dizin_boyutu("docs/arsiv", True)` varsayılan `haric_uygula=True`
        # ile HARIC'i uygular ve `docs/arsiv` KENDİNİ düşürüp 0 döner. Yeni
        # bütçeler `haric_uygula=False` ile ölçülmezse sessizce anlamsız olur.
        self.assertEqual(dokuman_bakim._dizin_boyutu("docs/arsiv", True), 0)
        self.assertGreater(dokuman_bakim._dizin_boyutu("docs/arsiv", True, haric_uygula=False), 0)

    def test_butce_anahtarlari_UCLU(self):
        for anahtar in dokuman_bakim.DIZIN_BUTCESI:
            self.assertEqual(len(anahtar), 3, anahtar)
            self.assertIsInstance(anahtar[2], bool)

    def test_muaf_agaclarin_hepsinin_butcesi_var(self):
        # Fazın tezi: muaf tutulan her ağaç KENDİ bütçesini alır.
        butceli = {a[0] for a in dokuman_bakim.DIZIN_BUTCESI}
        for h in dokuman_bakim.HARIC:
            self.assertIn(h, butceli, f"{h} muaf ama bütçesiz")

    def test_damitilmis_faz_kayitlari_butcede(self):
        import pathlib as _p
        for f in (_p.Path("docs/arsiv/fazlar")).glob("[0-9][0-9]*-*.md"):
            self.assertLessEqual(len(f.read_bytes()),
                                 dokuman_bakim.DAMITILMIS_FAZ_BUTCESI, f.name)


class YenidenKonumlandirmaTestleri(unittest.TestCase):
    """`_yeniden_konumlandir` — `docs/` -> `docs/arsiv/fazlar/` taşımasında
    dosyanın İÇİNDEKİ göreli bağlantılar. Faz 58'de bu iş elle yapıldı ve önce
    17, sonra 3 bağlantı kırdı."""

    def _tasi(self, metin):
        return dokuman_bakim._yeniden_konumlandir(metin, "docs", "docs/arsiv/fazlar")

    def test_kardes_dosya_iki_seviye_yukari_cikar(self):
        self.assertIn("](../../MIMARI.md)", self._tasi("[x](MIMARI.md)"))

    def test_ayni_dizine_gelen_hedef_sadelesir(self):
        self.assertIn("](78-X.md)", self._tasi("[x](arsiv/fazlar/78-X.md)"))

    def test_arsiv_kokundeki_hedef_bir_seviye_yukari(self):
        self.assertIn("](../KARARLAR-GECMISI.md)", self._tasi("[x](arsiv/KARARLAR-GECMISI.md)"))

    def test_repo_koku_disina_cikan_hedef(self):
        self.assertIn("](../../../src/A/B.cs)", self._tasi("[x](../src/A/B.cs)"))

    def test_capa_korunur(self):
        self.assertIn("](../../MIMARI.md#bolum-7)", self._tasi("[x](MIMARI.md#bolum-7)"))

    def test_dis_adres_ve_site_mutlak_dokunulmaz(self):
        for h in ("https://x.dev/a", "/reference/compatibility/"):
            self.assertIn(f"]({h})", self._tasi(f"[x]({h})"))

    def test_alt_dizin_hedefi(self):
        self.assertIn("](../../hafiza/maf-api.md)", self._tasi("[x](hafiza/maf-api.md)"))


class YenidenAcildiIsaretiTestleri(unittest.TestCase):
    """🔁 işareti şablonun biçimini (iki nokta) ister — çıplak ifadeyi değil."""

    def _isaret(self, satir):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d); (tmp / "docs").mkdir()
            (tmp / "docs" / "KARARLAR.md").write_text(
                "## 2. Kalıcı\n\n" + satir + "\n", encoding="utf-8")
            with mock.patch.object(dokuman_bakim, "ROOT", tmp):
                # dönüş sırası: (reddedilen, kalıcı)
                _, kalici = dokuman_bakim._kararlar_kalemleri()
        return kalici[0][3]

    def test_sablon_bicimi_isaret_uretir(self):
        self.assertIn("🔁", self._isaret(
            "| **K-9 — x (yeniden açıldı: 2026-08-02, ölçüm)** | 2026-08-01 | g | — |"))

    def test_ifadeyi_ALINTILAYAN_satir_isaret_ALMAZ(self):
        # Gerçek vaka: K-600'ün gerekçesi "asla kesilmez" listesinde ifadeyi
        # anıyor; çıplak arama onu "yeniden açılmış" sanıyordu.
        self.assertNotIn("🔁", self._isaret(
            "| **K-9 — x** | 2026-08-01 | ASLA kesilmez: `yeniden açıldı` ifadesi. | — |"))

    def test_kullanici_karari_isareti_calisir(self):
        self.assertIn("👤", self._isaret(
            "| **K-9 — x (kullanıcı kararı)** | 2026-08-01 | g | — |"))


class DenetimBulgulariTestleri(unittest.TestCase):
    """Bağımsız denetimin (Faz 90) bulduğu üç kusur için regresyon kapıları."""

    def test_NNx_onekli_kalici_bolum_DUSMEZ(self):
        # 🔴 Bulgu 2: `NN.x` bir NUMARALANDIRMA konvansiyonudur. Faz 29
        # sapmalarını `29.0 — Plandan Sapmalar` diye numaralandırmıştı ve
        # önekle sınıflandırma onu SESSİZCE düşürdü.
        metin = FAZ_ORNEK.replace("## Plandan Sapmalar", "## 42.0 — Plandan Sapmalar")
        yeni, _ = dokuman_bakim._faz_damit_metni(
            metin, tam_sha="a", yol="x.md", bugun="2026-08-23")
        self.assertIn("Plan A dedi, gerçek B çıktı.", yeni)

    def test_NNx_onekli_saglanamayanlar_DUSMEZ(self):
        metin = FAZ_ORNEK.replace(
            "## Açık Kalan", "## 42.7 — Sağlanamayan Şeyler (dürüstlük bölümü)")
        yeni, _ = dokuman_bakim._faz_damit_metni(
            metin, tam_sha="a", yol="x.md", bugun="2026-08-23")
        self.assertIn("Gerçek sunucu koşturulamadı.", yeni)

    def test_NNx_onekli_GERCEK_is_kalemi_duser(self):
        metin = FAZ_ORNEK.replace("## Açık Kalan", "## 42.3 — Protokol tasarımı")
        yeni, _ = dokuman_bakim._faz_damit_metni(
            metin, tam_sha="a", yol="x.md", bugun="2026-08-23")
        self.assertNotIn("Gerçek sunucu koşturulamadı.", yeni)

    def test_is_kalemi_kalani_ayristirir(self):
        self.assertEqual(dokuman_bakim._is_kalemi_kalani("29.0 — Plandan Sapmalar"),
                         "Plandan Sapmalar")
        self.assertIsNone(dokuman_bakim._is_kalemi_kalani("Plandan Sapmalar"))

    def test_kirli_agac_reset_hard_koşan_komutu_ENGELLER(self):
        # 🔴 Bulgu 1: ön denetim dar bir yol listesine bakıyordu ama geri alma
        # `git reset --hard` idi — `src/` altındaki düzenleme yok olurdu.
        with mock.patch.object(dokuman_bakim, "_git",
                               return_value=[" M src/Tracon.Core/X.cs"]):
            self.assertIsNotNone(dokuman_bakim._izlenen_degisiklik_var_mi())

    def test_temiz_agac_gecer(self):
        with mock.patch.object(dokuman_bakim, "_git", return_value=[]):
            self.assertIsNone(dokuman_bakim._izlenen_degisiklik_var_mi())

    def test_git_hatasi_temiz_SAYILMAZ(self):
        with mock.patch.object(dokuman_bakim, "_git", return_value=None):
            self.assertIsNotNone(dokuman_bakim._izlenen_degisiklik_var_mi())

    def test_cozulen_icerik_damitilmissa_bulgudur(self):
        # 🟡 Bulgu 6: `cat-file -e` başarılı olsa bile içerik damıtılmış olabilir.
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d)
            (tmp / "docs" / "arsiv" / "fazlar").mkdir(parents=True)
            (tmp / "docs" / "arsiv" / "fazlar" / "01-X.md").write_text(
                "> git show deadbee:docs/arsiv/fazlar/01-X.md\n", encoding="utf-8")
            with mock.patch.object(dokuman_bakim, "_git",
                                   return_value=[dokuman_bakim.DAMITMA_ISARETI]):
                bulgu = dokuman_bakim.tam_metin_denetle(tmp)
            self.assertTrue(any("ZATEN damıtılmış" in b for b in bulgu), bulgu)


class SatirIciKodTestleri(unittest.TestCase):
    """Satır içi kod da GÖSTERİMDİR — fence ile aynı sınıf."""

    def test_satir_ici_koddaki_baglanti_sayilmaz(self):
        self.assertNotIn("YOK.md", dokuman_bakim._kod_bloklarini_soy("`[x](../../YOK.md)` yaz"))

    def test_kod_ETIKETLI_baglantinin_HEDEFI_taranmaya_devam_eder(self):
        # `[`dosya.md`](dosya.md)` yaygın biçimdir; yalnız ETİKET kod parçasıdır.
        # Hedefi de gizleseydik gerçek kırık bağlantılar görünmez olurdu.
        c = dokuman_bakim._kod_bloklarini_soy("[`dosya.md`](hedef.md)")
        self.assertIn("](hedef.md)", c)

    def test_uzunluk_korunur(self):
        m = "a `kod` b"
        self.assertEqual(len(dokuman_bakim._kod_bloklarini_soy(m)), len(m))

    def test_kod_ici_baglanti_gercek_kirigi_MASKELEMEZ(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d); (tmp / "docs").mkdir()
            (tmp / "docs" / "a.md").write_text(
                "`[gizli](yok1.md)` ve [gerçek](yok2.md)\n", encoding="utf-8")
            kirik = dokuman_bakim.kirik_baglantilar(tmp)
            self.assertTrue(any("yok2.md" in k for k in kirik), kirik)
            self.assertFalse(any("yok1.md" in k for k in kirik), kirik)


class KodBlogundakiBaslikTestleri(unittest.TestCase):
    """Kod bloğundaki `## ` bir BAŞLIK değildir — damıtma şablonunu GÖSTEREN
    bir doküman kendi örneğini gerçek bölüm sanıp parçalanıyordu."""

    ORNEK = ("# Faz 1\n\n> **Durum:** ✅ Tamamlandı\n\n"
             "## Amaç\n\nGerçek amaç.\n\n"
             "## 1.2 — Şablon\n\n```markdown\n## Amaç\n\nÖRNEK\n\n"
             "## Plandan Sapmalar\n\nÖRNEK SAPMA\n```\n\n"
             "## Plandan Sapmalar\n\nGerçek sapma.\n")

    def test_kod_blogundaki_baslik_bolum_saymaz(self):
        _, bol = dokuman_bakim._faz_bolumleri(self.ORNEK)
        self.assertEqual([a for a, _ in bol], ["Amaç", "1.2 — Şablon", "Plandan Sapmalar"])

    def test_ornek_govde_gercek_bolume_karismaz(self):
        yeni, _ = dokuman_bakim._faz_damit_metni(
            self.ORNEK, tam_sha="a", yol="x.md", bugun="2026-08-23")
        self.assertIn("Gerçek sapma.", yeni)
        self.assertNotIn("ÖRNEK SAPMA", yeni)   # `1.2` iş kalemiyle birlikte düşer

    def test_isareti_ORNEK_olarak_gosteren_dokuman_damitilabilir(self):
        m = self.ORNEK.replace("```markdown\n## Amaç",
                               f"```markdown\n> {dokuman_bakim.DAMITMA_ISARETI}\n## Amaç")
        yeni, _ = dokuman_bakim._faz_damit_metni(
            m, tam_sha="a", yol="x.md", bugun="2026-08-23")
        self.assertNotEqual(yeni, m, "örnek işaret 'zaten damıtılmış' sanıldı")


class TamMetinKodBloguTestleri(unittest.TestCase):
    """Kapı, işareti ÖRNEK olarak gösteren bir tam metni damıtılmış sanmamalı —
    aynı sınıfın dördüncü vakası (LINK regex'i, `_faz_bolumleri`, idempotans
    denetimi ve bu kapı)."""

    def _kur(self, tmp):
        (tmp / "docs" / "arsiv" / "fazlar").mkdir(parents=True)
        (tmp / "docs" / "arsiv" / "fazlar" / "01-X.md").write_text(
            "> git show deadbee:docs/arsiv/fazlar/01-X.md\n", encoding="utf-8")

    def test_ORNEK_olarak_gosterilen_isaret_bulgu_uretmez(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d); self._kur(tmp)
            tam = ["# Faz 1", "", "```markdown", f"> {dokuman_bakim.DAMITMA_ISARETI}",
                   "```", "", "## Plandan Sapmalar"]
            with mock.patch.object(dokuman_bakim, "_git", return_value=tam):
                self.assertEqual(dokuman_bakim.tam_metin_denetle(tmp), [])

    def test_GERCEKTEN_damitilmis_icerik_bulgudur(self):
        with tempfile.TemporaryDirectory() as d:
            tmp = pathlib.Path(d); self._kur(tmp)
            with mock.patch.object(dokuman_bakim, "_git",
                                   return_value=[f"> {dokuman_bakim.DAMITMA_ISARETI}"]):
                self.assertTrue(dokuman_bakim.tam_metin_denetle(tmp))




class SatirIciKodSatirSonuTestleri(unittest.TestCase):
    """Bir satir sonuna sarilan kod parcasi da GOSTERIMDIR."""

    def test_iki_satira_bolunmus_kod_parcasi_baglanti_sayilmaz(self):
        # 2026-09-16 kosumu: bir kayit `[kapilar.md](kapilar.md)` alintisini
        # 80 sutunda sardi, kod parcasi satir sonunu asamadigi icin kod
        # sayilmadi ve kapi onu gercek bir kirik baglanti ilan etti.
        metin = "Kural: `[kapilar.md](\n../ortak/kapilar.md)` yazilmaz.\n"

        soyulmus = dokuman_bakim._kod_bloklarini_soy(metin)

        self.assertNotIn("](", soyulmus)

    def test_soyma_satir_sayisini_korur(self):
        metin = "bir\niki `a\nb` uc\ndort\n"

        self.assertEqual(
            dokuman_bakim._kod_bloklarini_soy(metin).count("\n"),
            metin.count("\n"))

    def test_iki_satir_sonu_asan_desen_kod_sayilmaz(self):
        # Sinir TEK satir sonudur: tek basina kalmis bir backtick dokumanin
        # yarisini yutmamalidir.
        metin = "`a\nb\nc`"

        self.assertEqual(dokuman_bakim._kod_bloklarini_soy(metin), metin)


class TamMetinKarsiOrnekTestleri(unittest.TestCase):
    """Kod gosterimi icindeki `git show` bir referans degil, ornektir."""

    def test_kod_blogundaki_karsi_ornek_referans_sayilmaz(self):
        metin = (
            "Karsi ornek:\n\n```\n"
            "git show 0000000:docs/yok.md\n"
            "```\n"
        )

        soyulmus = dokuman_bakim._kod_bloklarini_soy(metin)

        self.assertEqual(dokuman_bakim._TAM_METIN.findall(soyulmus), [])

    def test_blok_alintisindaki_mesru_referans_gorulur(self):
        metin = "> git show 7f1833e:docs/arsiv/fazlar/05-X.md\n"

        soyulmus = dokuman_bakim._kod_bloklarini_soy(metin)

        self.assertEqual(
            dokuman_bakim._TAM_METIN.findall(soyulmus),
            [("7f1833e", "docs/arsiv/fazlar/05-X.md")])

if __name__ == "__main__":
    unittest.main()


class SevkEdilenGenislemeNoktasiTestleri(unittest.TestCase):
    """Genişleme noktası sayısı kod ↔ sevk edilen metin (2026-09-04, KUSUR-A1).

    Sınıf ÜÇ kez ölçüldü: `a377106e` (Faz 139, altıncı nokta) · Faz 142'nin
    denetim bulgusu #2 (yedinci nokta) · 2026-09-04 süreç denetimi. Faz 142'nin
    devir notu "hiçbir kapı bu boşluğu otomatik yakalamaz" diyordu; kapı budur.
    """

    KOD = """
    private IReadOnlyList<ExtensionPointDiagnostic> CollectExtensionPoints()
    {
        return
        [
            new ExtensionPointDiagnostic { Contract = nameof(ITenantContext) },
            new ExtensionPointDiagnostic { Contract = nameof(IRunEventSink) },
        ];
    }
}
"""

    def _kok(self, tablo: str, sevk: dict[str, str] | None = None) -> pathlib.Path:
        kok = pathlib.Path(tempfile.mkdtemp())
        kaynak = kok / "src" / "Tracon.Core" / "Diagnostics"
        kaynak.mkdir(parents=True)
        (kaynak / "TraconDiagnosticsCollector.cs").write_text(self.KOD, encoding="utf-8")
        icerik = kok / "docs-site" / "src" / "content" / "docs"
        icerik.mkdir(parents=True)
        (icerik / "capabilities.md").write_text(
            "# X\n\n## Embedding points\n\n" + tablo + "\n## Sonraki\n", encoding="utf-8")
        (kok / "samples").mkdir()
        for ad, metin in (sevk or {}).items():
            yol = (kok / ad) if ad.startswith("samples/") else (icerik / ad)
            yol.parent.mkdir(parents=True, exist_ok=True)
            yol.write_text(metin, encoding="utf-8")
        return kok

    IKI_SATIR = ("| Capability | Bind it |\n|---|---|\n"
                 "| Tenant | `ITenantContext` |\n| Events | `IRunEventSink` |\n")

    def test_eksik_tablo_satiri_kirmizidir(self):
        kok = self._kok("| Capability | Bind it |\n|---|---|\n| Tenant | `ITenantContext` |\n")
        bulgular = dokuman_bakim.sevk_edilen_genisleme_noktasi(kok)
        self.assertTrue(any("1 satır" in b for b in bulgular))
        self.assertTrue(any("IRunEventSink" in b for b in bulgular))

    def test_sevk_edilen_metindeki_bayat_sayi_kirmizidir(self):
        # KUSUR-A1'in birebir hali: kod 2 nokta, örnek "three embedding points".
        kok = self._kok(self.IKI_SATIR, {
            "samples/Program.cs": "// The three embedding points, all bound first.\n"})
        bulgular = dokuman_bakim.sevk_edilen_genisleme_noktasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("3 genişleme noktası diyor, kod 2", bulgular[0])

    def test_dogru_sayi_yanlis_pozitif_uretmez(self):
        kok = self._kok(self.IKI_SATIR, {
            "samples/Program.cs": "// The two embedding points.\n"})
        self.assertEqual([], dokuman_bakim.sevk_edilen_genisleme_noktasi(kok))

    def test_EN_YAKIN_sayi_yonetir(self):
        """"One of the two embedding points" DOĞRUDUR; soldaki sayı örneğin kendi payıdır."""
        kok = self._kok(self.IKI_SATIR, {
            "samples/README.md": "One of the two embedding points is bound here.\n"})
        self.assertEqual([], dokuman_bakim.sevk_edilen_genisleme_noktasi(kok))

    def test_genel_extension_point_ifadesi_SAYILMAZ(self):
        """"extension point" genel terimdir (IModelProvider, IAgentSource, ...).

        Onu saymak yanlış pozitif üretir ve yanlış pozitif veren kapı kapatılır.
        """
        kok = self._kok(self.IKI_SATIR, {
            "guides/write-your-own-error-classifier.md": "Two questions, two extension points\n"})
        self.assertEqual([], dokuman_bakim.sevk_edilen_genisleme_noktasi(kok))

    def test_uretilen_api_referansi_SAYILMAZ(self):
        kok = self._kok(self.IKI_SATIR, {
            "api/Tracon.Report.md": "one entry per embedding point\n",
            "http-api/schema-report.md": "nine embedding points\n"})
        self.assertEqual([], dokuman_bakim.sevk_edilen_genisleme_noktasi(kok))


class SevkEdilenOlayAnlatisiTestleri(unittest.TestCase):
    """Sevk edilen webhook olayının anlatı kapsamı (2026-09-03 kusuru)."""

    def _kok(self, olaylar: str, anlati: dict[str, str]) -> pathlib.Path:
        kok = pathlib.Path(tempfile.mkdtemp())
        sabit = kok / "src" / "Tracon.Abstractions" / "Webhooks"
        sabit.mkdir(parents=True)
        (sabit / "WebhookTypes.cs").write_text(olaylar, encoding="utf-8")
        icerik = kok / "docs-site" / "src" / "content" / "docs"
        icerik.mkdir(parents=True)
        for ad, metin in anlati.items():
            yol = icerik / ad
            yol.parent.mkdir(parents=True, exist_ok=True)
            yol.write_text(metin, encoding="utf-8")
        return kok

    SABITLER = (
        'public const string RunCompleted = "run.completed";\n'
        'public const string QuotaThreshold = "quota.threshold";\n'
    )

    def test_anlatida_gecmeyen_olay_kirmizidir(self):
        kok = self._kok(self.SABITLER, {"concepts/governance.md": "run.completed anlatısı"})
        bulgular = dokuman_bakim.sevk_edilen_olay_anlatisi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("quota.threshold", bulgular[0])

    def test_anlatida_gecen_olay_yanlis_pozitif_uretmez(self):
        kok = self._kok(
            self.SABITLER,
            {"concepts/governance.md": "run.completed ve quota.threshold anlatısı"})
        self.assertEqual([], dokuman_bakim.sevk_edilen_olay_anlatisi(kok))

    def test_uretilen_api_referansi_anlati_SAYILMAZ(self):
        """Kusurun çekirdeği: `/api/` altındaki üretilen sayfa kapsama girmez."""
        kok = self._kok(
            self.SABITLER,
            {
                "concepts/governance.md": "run.completed anlatısı",
                "api/Tracon.WebhookEvents.md": "quota.threshold burada üretildi",
            })
        bulgular = dokuman_bakim.sevk_edilen_olay_anlatisi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("quota.threshold", bulgular[0])

    def test_uretilen_sema_sayfasi_anlati_SAYILMAZ(self):
        kok = self._kok(
            self.SABITLER,
            {
                "concepts/governance.md": "run.completed anlatısı",
                "http-api/schema-webhook.md": "quota.threshold şema sayfası",
            })
        self.assertEqual(1, len(dokuman_bakim.sevk_edilen_olay_anlatisi(kok)))


class ManuelTestSayimKaymasiTestleri(unittest.TestCase):
    """Indeksin yazdigi case sayisi ile aile dosyasindaki gercek (2026-09-08)."""

    def _kok(self, yazan: int, case_sayisi: int, kod: str = "JOB") -> pathlib.Path:
        kok = pathlib.Path(tempfile.mkdtemp())
        mt = kok / "docs" / "manuel-test"
        mt.mkdir(parents=True)
        (mt / "00-INDEKS.md").write_text(
            "| # | Dosya | Kod | Faz | Kaynak | Case | Spec | Koşum |\n"
            "|---|---|---|---|---|---|---|---|\n"
            f"| 16 | [`16-IS.md`](16-IS.md) | `{kod}` | 17 | `src/x` | **{yazan}** | ✅ | ✅ |\n",
            encoding="utf-8")
        govde = "".join(f"### MT-{kod}-{i:03d} — case {i}\n\nmetin\n\n" for i in range(1, case_sayisi + 1))
        (mt / "16-IS.md").write_text("# 16 — Is\n\n" + govde, encoding="utf-8")
        return kok

    def test_bayat_sayim_kirmizidir(self):
        bulgular = dokuman_bakim.manuel_test_sayim_kaymasi(self._kok(yazan=63, case_sayisi=98))
        self.assertEqual(1, len(bulgular))
        self.assertIn("+35", bulgular[0])

    def test_dogru_sayim_yanlis_pozitif_uretmez(self):
        self.assertEqual([], dokuman_bakim.manuel_test_sayim_kaymasi(self._kok(yazan=98, case_sayisi=98)))

    def test_case_silinmesi_de_yakalanir(self):
        # Ters yondeki sapma da bir bulgudur: eval ailesi 77 yaziyordu, 69 vardi.
        bulgular = dokuman_bakim.manuel_test_sayim_kaymasi(self._kok(yazan=77, case_sayisi=69))
        self.assertEqual(1, len(bulgular))
        self.assertIn("-8", bulgular[0])

    def _kok_tablo(self, yazan: int, case_sayisi: int, kod: str = "GDK") -> pathlib.Path:
        """Tablo bicimli aile: case'ler `### MT-` basligi degil, TABLO SATIRI.

        31-36 aileleri bu bicimdedir (Faz 167'de olculdu).
        """
        kok = pathlib.Path(tempfile.mkdtemp())
        mt = kok / "docs" / "manuel-test"
        mt.mkdir(parents=True)
        (mt / "00-INDEKS.md").write_text(
            "| # | Dosya | Kod | Faz | Kaynak | Case | Spec | Koşum |\n"
            "|---|---|---|---|---|---|---|---|\n"
            f"| 36 | [`36-IS.md`](36-IS.md) | `{kod}` | 91 | `src/x` | **{yazan}** | ✅ | ✅ |\n",
            encoding="utf-8")
        satirlar = "".join(
            f"| {i} | `MT-{kod}-{i:03d}` | on kosul | adim | beklenen |\n"
            for i in range(1, case_sayisi + 1))
        (mt / "36-IS.md").write_text(
            "# 36 — Is\n\n| # | Kod | Ön koşul | Adımlar | Beklenen sonuç |\n"
            "|---|---|---|---|---|\n" + satirlar, encoding="utf-8")
        return kok

    def test_tablo_bicimli_ailede_bayat_sayim_KIRMIZIDIR(self):
        # Faz 167: kapi bu aileleri SESSIZCE atliyordu. Olculdu: alti aile
        # kapsam disiydi ve IKISINDE gercek sapma vardi (31-*: +8, 36-*: +6).
        bulgular = dokuman_bakim.manuel_test_sayim_kaymasi(self._kok_tablo(yazan=25, case_sayisi=31))
        self.assertEqual(1, len(bulgular))
        self.assertIn("+6", bulgular[0])

    def test_tablo_bicimli_aile_dogru_sayimda_yanlis_pozitif_uretmez(self):
        self.assertEqual([], dokuman_bakim.manuel_test_sayim_kaymasi(self._kok_tablo(yazan=31, case_sayisi=31)))

    def test_hicbir_bicimde_case_tasimayan_aile_KAPSAM_DISIDIR(self):
        # Kalan tek kacis: ne `### MT-` basligi ne de `| n | \`MT-KOD-nnn\` |`
        # satiri olan dosya. Bu bir bicim farki degil, case'siz bir dosyadir.
        self.assertEqual([], dokuman_bakim.manuel_test_sayim_kaymasi(self._kok(yazan=24, case_sayisi=0)))


class BagimlilikSurumDamgasiTestleri(unittest.TestCase):
    """Sevk edilen XML'deki surum damgasi ile pin (F-171'in ikinci yarisi)."""

    def _kok(self, xml_satiri: str, dosya: str) -> pathlib.Path:
        kok = pathlib.Path(tempfile.mkdtemp())
        (kok / "Directory.Packages.props").write_text(
            "<Project>\n"
            "  <PropertyGroup><MicrosoftAgentsAIVersion>1.20.0</MicrosoftAgentsAIVersion></PropertyGroup>\n"
            '  <ItemGroup><PackageVersion Include="OpenAI" Version="2.12.0" />\n'
            '  <PackageVersion Include="Microsoft.Agents.AI.Hosting.OpenAI" Version="1.20.0-alpha.260831.1" />\n'
            '  <PackageVersion Include="Microsoft.Agents.AI" Version="$(MicrosoftAgentsAIVersion)" /></ItemGroup>\n'
            "</Project>\n", encoding="utf-8")
        yol = kok / dosya
        yol.parent.mkdir(parents=True, exist_ok=True)
        yol.write_text(f"/// <summary>\n{xml_satiri}\n/// </summary>\npublic class X {{ }}\n", encoding="utf-8")
        return kok

    DOSYA = "src/Tracon.Core/Models/FallbackChatClient.cs"

    def test_sapan_damga_kirmizidir(self):
        kok = self._kok("/// Measured against the real OpenAI 2.11.0 client.", self.DOSYA)
        bulgular = dokuman_bakim.bagimlilik_surum_damgasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("2.12.0", bulgular[0])

    def test_pinle_ayni_damga_yanlis_pozitif_uretmez(self):
        kok = self._kok("/// Measured against the real OpenAI 2.12.0 client.", self.DOSYA)
        self.assertEqual([], dokuman_bakim.bagimlilik_surum_damgasi(kok))

    def test_msbuild_degiskeni_cozulur(self):
        kok = self._kok("/// Measured against MAF 1.19.0 (2026-09-07).",
                        "src/Tracon.Core/Compilation/RecordingLoopEvaluator.cs")
        bulgular = dokuman_bakim.bagimlilik_surum_damgasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("1.20.0", bulgular[0])

    def test_KAYITSIZ_damga_kirmizidir(self):
        # Sinifi kapatan parca: yeni bir damga sessizce kapinin disinda kalamaz.
        kok = self._kok("/// Measured against Fake 9.9.9.", "src/Tracon.Core/Yeni.cs")
        bulgular = dokuman_bakim.bagimlilik_surum_damgasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("KAYITSIZ", bulgular[0])

    def test_pinlenmeyen_paket_bilerek_atlanir(self):
        # `None` kaydi: damga repo'nun ALMADIGI bir paket hakkinda.
        kok = self._kok("/// Measured (10.8.0): that type requires an expression.",
                        "src/Tracon.Abstractions/Knowledge/IVectorSearchStore.cs")
        self.assertEqual([], dokuman_bakim.bagimlilik_surum_damgasi(kok))

    def test_measured_demeyen_damga_da_gorulur(self):
        # Genisletmenin sinifi: kapiyi atlatmanin yolu damgayi baska turlu
        # yazmak olamaz. Olculdu 2026-09-14 — dort gercek vaka boyle kacmisti.
        kok = self._kok("/// Applies only to OpenAI 2.11.0 and later.", self.DOSYA)
        bulgular = dokuman_bakim.bagimlilik_surum_damgasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("2.12.0", bulgular[0])

    def test_tarihsel_damga_YAZILI_olarak_muaftir(self):
        # Bilerek eski surum ("1.18.0'da su hâlâ basarisizdi") pine esit
        # olmamalidir. Muafiyet `<dosya>:<surum>` anahtariyla yazilir.
        dosya = "src/Tracon.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs"
        kok = self._kok("/// A function_call item still fails on 1.18.0-alpha.", dosya)
        self.assertEqual([], dokuman_bakim.bagimlilik_surum_damgasi(kok))

    def test_tarihsel_OLMAYAN_damga_ayni_dosyada_yine_karsilastirilir(self):
        # Muafiyet dosyanin tamamini degil, TEK satiri kapsar.
        dosya = "src/Tracon.AspNetCore/OpenAICompat/OpenAIResponsesEndpoints.cs"
        kok = self._kok("/// Measured against the host package 1.19.0-alpha.", dosya)
        bulgular = dokuman_bakim.bagimlilik_surum_damgasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("1.19.0-alpha", bulgular[0])

    def test_IP_literali_surum_damgasi_SAYILMAZ(self):
        # 🚨 Bir dotted quad da uc noktalidir. Egress ve webhook guard'larindaki
        # adresler kapiyi kirmamalidir.
        kok = self._kok(
            "/// Refuses <c>169.254.169.254</c>, <c>127.0.0.0/8</c> and <c>1.2.3.4</c>.",
            "src/Tracon.Core/Egress/EgressAddressValidator.cs")
        self.assertEqual([], dokuman_bakim.bagimlilik_surum_damgasi(kok))

    def test_cumle_sonundaki_nokta_damgaya_KATILMAZ(self):
        # `-[\w.]+` cumlenin noktasini yutup hicbir pakete uymayan bir dize
        # uretiyordu; ek SemVer dilbilgisiyle yazilir.
        kok = self._kok("/// Measured against MAF 1.19.0.", "src/Tracon.Core/Compilation/RecordingLoopEvaluator.cs")
        bulgular = dokuman_bakim.bagimlilik_surum_damgasi(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("damga 1.19.0 diyor", bulgular[0])


class SurecOlcumuTestleri(unittest.TestCase):
    """Faz 169: kapanmış fazın `## Süreç Ölçümü` tablosu dolu mu (eşik 167)."""

    TAM = (
        "## Süreç Ölçümü\n\n"
        "| Metrik | Değer |\n"
        "|---|---|\n"
        "| Plan revizyonu sayısı | 0 |\n"
        "| Düzeltme turu sayısı | 2 |\n"
        "| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |\n"
        "| Fazın ürettiği regresyon | 0 |\n"
        "| Faz kapandıktan sonra bulunan kusur | ölçülmedi |\n"
    )

    def _kok(self, dosyalar: dict[str, str]) -> pathlib.Path:
        """`{"arsiv/fazlar/167-X.md": gövde}` -> geçici repo kökü."""
        kok = pathlib.Path(tempfile.mkdtemp())
        for rel, govde in dosyalar.items():
            p = kok / "docs" / rel
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(govde, encoding="utf-8")
        (kok / "docs" / "arsiv" / "fazlar").mkdir(parents=True, exist_ok=True)
        return kok

    def _faz(self, durum: str, govde: str = "") -> str:
        return f"# Faz X\n\n> **Durum:** {durum}\n\n## Amaç\n\nmetin\n\n{govde}"

    def test_esik_ALTI_faz_atlanir(self):
        # 166 faz geriye dönük doldurulmaz (kullanici karari). Esik alti bir
        # kayit bolum TASIMASA da bulgu uretmez.
        kok = self._kok({"arsiv/fazlar/166-ESKI.md": self._faz("✅ Tamamlandı (2026-09-01)")})
        self.assertEqual([], dokuman_bakim.surec_olcumu_bulgulari(kok))

    def test_esik_USTU_tamamlanmis_fazda_bolum_YOKSA_bulgudur(self):
        kok = self._kok({"arsiv/fazlar/167-YENI.md": self._faz("✅ Tamamlandı (2026-09-12)")})
        bulgular = dokuman_bakim.surec_olcumu_bulgulari(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("Süreç Ölçümü", bulgular[0])
        self.assertIn("167-YENI.md", bulgular[0])

    def test_TAMAMLANMAMIS_faz_bulgu_uretmez(self):
        # Plan durumundaki faz henuz kapanmadi; bolum bos olmali.
        kok = self._kok({"169-NN.md": self._faz("📋 Planlandı (2026-09-13)")})
        self.assertEqual([], dokuman_bakim.surec_olcumu_bulgulari(kok))

    def test_BOS_deger_hucresi_bulgudur(self):
        eksik = self.TAM.replace("| Fazın ürettiği regresyon | 0 |",
                                 "| Fazın ürettiği regresyon | |")
        kok = self._kok({"arsiv/fazlar/168-X.md": self._faz("✅ Tamamlandı (2026-09-13)", eksik)})
        bulgular = dokuman_bakim.surec_olcumu_bulgulari(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("Fazın ürettiği regresyon", bulgular[0])
        # Satir numarasi raporlanir -- manuel case 2 bunu iddia ediyor.
        self.assertRegex(bulgular[0], r":\d+:")

    def test_EKSIK_metrik_satiri_bulgudur(self):
        eksik = self.TAM.replace("| Düzeltme turu sayısı | 2 |\n", "")
        kok = self._kok({"arsiv/fazlar/168-X.md": self._faz("✅ Tamamlandı (2026-09-13)", eksik)})
        bulgular = dokuman_bakim.surec_olcumu_bulgulari(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("Düzeltme turu sayısı", bulgular[0])

    def test_DOLU_tablo_yanlis_pozitif_uretmez(self):
        # `ölçülmedi` GECERLI bir degerdir: kapi bir sayi degil, bir KARAR arar.
        kok = self._kok({"arsiv/fazlar/168-X.md": self._faz("✅ Tamamlandı (2026-09-13)", self.TAM)})
        self.assertEqual([], dokuman_bakim.surec_olcumu_bulgulari(kok))

    def test_FAZLADAN_satir_ve_bicim_varyanti_gecer(self):
        # Faz 167 tablosu YEDI satir tasiyor ve degerler kalin yazim iceriyor.
        genis = self.TAM.replace(
            "| Fazın ürettiği regresyon | 0 |",
            "| Denetim sonrası düzeltme turu | 1 |\n"
            "| **Fazın ürettiği regresyon** | **0** |\n"
            "| Faz dışı bulunan ve kapatılan kusur | 1 |")
        kok = self._kok({"arsiv/fazlar/167-X.md": self._faz("✅ Tamamlandı (2026-09-12)", genis)})
        self.assertEqual([], dokuman_bakim.surec_olcumu_bulgulari(kok))

    def test_KOD_BLOGU_icindeki_baslik_bolum_saymaz(self):
        # 🚨 Duz `split("## ")` burada belgeyi parcalar. Sablonu GOSTEREN bir
        # faz kaydi (Faz 169'un kendisi) basligi kod blogu icinde tasir ve o
        # ornek GERCEK bolumun yerine gecmemelidir.
        ornek = "## Şablon\n\n```markdown\n## Süreç Ölçümü\n\n| Metrik | Değer |\n|---|---|\n| Plan revizyonu sayısı | |\n```\n\n"
        kok = self._kok({"arsiv/fazlar/169-X.md":
                         self._faz("✅ Tamamlandı (2026-09-13)", ornek + self.TAM)})
        self.assertEqual([], dokuman_bakim.surec_olcumu_bulgulari(kok))

    def test_KOK_docs_altindaki_tamamlanmis_faz_da_taranir(self):
        # Acik Soru 1 -> A: eksik olcum ARSIVLEMEDEN once gorunur.
        kok = self._kok({"169-Y.md": self._faz("✅ Tamamlandı (2026-09-13)")})
        bulgular = dokuman_bakim.surec_olcumu_bulgulari(kok)
        self.assertEqual(1, len(bulgular))
        self.assertIn("docs/169-Y.md", bulgular[0])

    def test_kapi_HICBIR_SEY_YAZMAZ(self):
        # `tazelik_denetle()` emsali: denetim yan etkisizdir.
        kok = self._kok({"arsiv/fazlar/167-X.md": self._faz("✅ Tamamlandı (2026-09-12)")})
        once = {p: p.read_bytes() for p in sorted((kok / "docs").rglob("*.md"))}
        dokuman_bakim.surec_olcumu_bulgulari(kok)
        sonra = {p: p.read_bytes() for p in sorted((kok / "docs").rglob("*.md"))}
        self.assertEqual(once, sonra)

    def test_SATIR_ICI_KOD_degeri_GECERLIDIR(self):
        # 🔴 denetim bulgusu (Faz 169, triyaj: gerçek). `_kod_bloklarini_soy`
        # satir ici kodu BOSLUKLA doldurur; gövdeye uygulanirsa `` `ölçülmedi` ``
        # yazan bir hucre kapiya BOS gorunur. Oysa `faz-tamamlama` Adim 5 ve
        # sablonun kendisi tam olarak o yazimi OGRETIYOR.
        kod = self.TAM.replace("| Faz kapandıktan sonra bulunan kusur | ölçülmedi |",
                               "| `Faz kapandıktan sonra bulunan kusur` | `ölçülmedi` |")
        kok = self._kok({"arsiv/fazlar/168-X.md": self._faz("✅ Tamamlandı (2026-09-13)", kod)})
        self.assertEqual([], dokuman_bakim.surec_olcumu_bulgulari(kok))

    def test_FAZ_KAL_bolumu_damitmada_KORUR(self):
        self.assertIn("Süreç Ölçümü", dokuman_bakim._FAZ_KAL)
        self.assertFalse(dokuman_bakim._duser_mu("Süreç Ölçümü", "Süreç Ölçümü"))


class KesikKararBasligiTestleri(unittest.TestCase):
    """İÇ İÇE `**` karar başlığını SESSİZCE keser (faz dışı kusur, Faz 169).

    `_kararlar_kalemleri()` başlığı `\\*\\*(.+?)\\*\\*` ile okur ve desen tembeldir:
    başlığın İÇİNDEKİ ilk `**` başlığı orada bitirir. Üretilen indeks satırı
    cümlenin yarısında kesilir ve kalem aranamaz hâle gelir — ölçüldü: K-413
    indekste `... kaynak her fazın kendi \\`>` diye bitiyordu, K-523 `... \\`docs/`
    diye. Hiçbir kapı görmedi; `--denetle` yeşildi.
    """

    def test_ic_ice_bold_KESIK_baslik_bulgusudur(self):
        govde = [(826, "| **K-767 — eşik sabit sayı **167**'dir (kullanıcı kararı)** | 2026-09-13 | gerekçe | — |")]
        bulgular = dokuman_bakim._kesik_karar_basliklari(govde)
        self.assertEqual(1, len(bulgular))
        self.assertIn("826", bulgular[0])
        self.assertIn("K-767", bulgular[0])

    def test_KOD_PARCASI_icindeki_bold_bulgu_DEGILDIR(self):
        # 🚨 `` `> **Durum:**` `` bir VURGU degil, gosterilen METINDIR. K-413 ve
        # K-523 tam olarak boyleydi; kacis eklemek yerine URETEC kod parcasini
        # taniyacak hale getirildi (`_karar_basligi`), cunku kacis kod
        # parcasinin ICINDE birebir goruntulenir.
        govde = [(460, "| **K-413 — kaynak her fazın kendi `> **Durum:**` satırıdır** | 2026-08-16 | g | — |"),
                 (570, "| **K-523 — `docs/**.md` bütçesi büyütülmez (kullanıcı kararı)** | 2026-08-20 | g | — |")]
        self.assertEqual([], dokuman_bakim._kesik_karar_basliklari(govde))

    def test_kod_parcasi_icindeki_bold_INDEKSTE_de_TAM_kalir(self):
        satir = "| **K-413 — kaynak her fazın kendi `> **Durum:**` satırıdır** | 2026-08-16 | g | — |"
        baslik, kuyruk = dokuman_bakim._karar_basligi(satir)
        self.assertTrue(baslik.endswith("`> **Durum:**` satırıdır"), baslik)
        self.assertTrue(kuyruk.startswith(" |"), repr(kuyruk))

    def test_MESRU_kuyruk_isaretcileri_yanlis_pozitif_uretmez(self):
        # `**(Faz 168)**`, `*(kullanıcı kararı)*` ve 🚨 mesru kuyruklardir:
        # hepsi basligi KAPATAN `**`den SONRA gelir ve bosluk ile baslar.
        govde = [
            (1, "| **K-765 — bir başlık** **(Faz 168)** | 2026-09-13 | gerekçe | — |"),
            (2, "| **K-001 — başka başlık** *(kullanıcı kararı)* | 2026-01-01 | gerekçe | — |"),
            (3, "| **K-166 — üçüncü başlık** 🚨 | 2026-01-01 | gerekçe | — |"),
            (4, "| **K-002 — sade başlık** | 2026-01-01 | gerekçe | — |"),
        ]
        self.assertEqual([], dokuman_bakim._kesik_karar_basliklari(govde))

    def test_KARAR_OLMAYAN_satir_atlanir(self):
        self.assertEqual([], dokuman_bakim._kesik_karar_basliklari(
            [(1, "| Karar | Tarih | Gerekçe | Yeniden açılma |"), (2, "|---|---|---|---|")]))

    def test_GERCEK_defterde_kesik_baslik_YOK(self):
        # Sinif taramasi: iki vaka bulundu ve ikisi de duzeltildi. Tarama
        # URETECIN gordugu satir kumesinin TAMAMINI kapsar (§2 + reddedilen
        # kararlar tablosu), yalniz §2'yi degil.
        satirlar = list(enumerate(
            (ROOT / "docs" / "KARARLAR.md").read_text(encoding="utf-8").split("\n"), 1))
        self.assertEqual([], dokuman_bakim._kesik_karar_basliklari(satirlar))

    def test_kapi_URETECIN_gordugu_HER_satiri_tarar(self):
        # 🟡 3 (Faz 169 denetimi): kapi eskiden yalniz §2'yi okuyordu; uretec
        # dosyanin tamamini okur. Reddedilen kararlar tablosundaki kesik bir
        # baslik indekse cikar ama denetlenmezdi.
        _reddedilen, kalici = dokuman_bakim._kararlar_kalemleri()
        satirlar = list(enumerate(
            (ROOT / "docs" / "KARARLAR.md").read_text(encoding="utf-8").split("\n"), 1))
        karar_satiri = sum(1 for _, s in satirlar if s.startswith("| **"))
        self.assertGreater(karar_satiri, len(kalici),
                           "uretec §2 disinda da kalem uretiyor olmali")
