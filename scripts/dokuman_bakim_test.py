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
        degisen = ["src/AgentPrism.Workflows/Foo.cs", "docs-site/src/content/docs/packages.md"]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        workflow = next(e for e in eslesme if e[0] == "workflow")
        self.assertFalse(workflow[3])

    def test_karsilanan_kural_yanlis_pozitif_uretmez(self):
        degisen = [
            "src/AgentPrism.Workflows/Foo.cs",
            "docs-site/src/content/docs/concepts/workflows.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        workflow = next(e for e in eslesme if e[0] == "workflow")
        self.assertTrue(workflow[3])

    def test_dizin_hedefi_alt_sayfayla_karsilanir(self):
        degisen = [
            "src/AgentPrism.Abstractions/IFoo.cs",
            "docs-site/src/content/docs/concepts/agents.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        kavram = next(e for e in eslesme if e[0] == "cekirdek-kavram")
        self.assertTrue(kavram[3])

    def test_dizin_hedefi_baska_sayfayla_karsilanmaz(self):
        degisen = [
            "src/AgentPrism.Abstractions/IFoo.cs",
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
            "src/AgentPrism.UI/frontend/src/screens/Yeni.tsx",
            "docs-site/src/content/docs/ui.md",
        ]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        arayuz = next(e for e in eslesme if e[0] == "arayuz")
        self.assertTrue(arayuz[3])

    def test_buildtransitive_capabilities_hedefler(self):
        degisen = ["src/AgentPrism.Core/buildTransitive/AgentPrism.Core.targets"]
        eslesme = dokuman_bakim._kural_eslesmesi(degisen)
        adlar = {e[0] for e in eslesme}
        self.assertIn("buildtransitive", adlar)
        bt = next(e for e in eslesme if e[0] == "buildtransitive")
        self.assertEqual(bt[1], ("capabilities.md",))
        self.assertFalse(bt[3])
        # Aynı yol genel cekirdek-kavram kuralını da tetikler -- ikisi ayrı satır.
        self.assertIn("cekirdek-kavram", adlar)

    def test_tetiklenmeyen_kural_sonuca_girmez(self):
        self.assertEqual(dokuman_bakim._kural_eslesmesi(["README.md"]), [])

    def test_bos_kume_tuzagi(self):
        # SITE_KURALLARI boşalırsa hiçbir kural tetiklenmez ve kapı SESSİZCE
        # yeşil kalır. Gerçek sabiti kullanır (mock değil) -- boşalırsa düşer.
        self.assertGreater(len(dokuman_bakim.SITE_KURALLARI), 0)
        eslesme = dokuman_bakim._kural_eslesmesi(["src/AgentPrism.AspNetCore/Endpoints/Foo.cs"])
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
        # `docs-site/public/openapi/agentprism.json` `.gitignore`'dadir ve
        # yalniz `site` isinin `npm run build` -> `prebuild` zincirinde uretilir;
        # `build` isinde HENUZ yoktur. Bagimsiz denetimin buldugu kalici yanlis
        # pozitif: dosya gercekten yokken bile bu baglanti kirik SAYILMAMALI.
        with tempfile.TemporaryDirectory() as t:
            tmp = pathlib.Path(t)
            self._kok_kur(tmp)
            self.assertFalse((tmp / "docs-site" / "public" / "openapi").exists())
            (tmp / "docs-site" / "src" / "content" / "docs" / "index.mdx").write_text(
                "[openapi](/openapi/agentprism.json)\n"
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


FAZ_ORNEK = """# Faz 42 — Örnek

> **Durum:** ✅ Tamamlandı (2026-08-07)
> **Paketler:** `AgentPrism.Core`

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


if __name__ == "__main__":
    unittest.main()
