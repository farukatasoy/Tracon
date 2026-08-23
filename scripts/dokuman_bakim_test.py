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


if __name__ == "__main__":
    unittest.main()
