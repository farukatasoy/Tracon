#!/usr/bin/env python3
"""Tests for the CHANGELOG.md Keep a Changelog section parser."""
from __future__ import annotations

import importlib.util
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("changelog", ROOT / "scripts" / "changelog.py")
changelog = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = changelog
spec.loader.exec_module(changelog)


class ChangelogTestleri(unittest.TestCase):
    def test_bolum_govdesi_okunur(self):
        text = (
            "# Changelog\n\n"
            "## [Unreleased]\n\n"
            "## [1.0.0-preview.1] - 2026-08-28\n\n"
            "### Added\n- ilk ozellik\n\n"
            "## [0.9.0] - 2026-08-01\n\n"
            "### Added\n- eski ozellik\n"
        )

        body = changelog.section_body(text, "1.0.0-preview.1")

        self.assertIn("ilk ozellik", body)
        self.assertNotIn("eski ozellik", body)

    def test_dosyanin_sonundaki_bolum_de_okunur(self):
        text = "# Changelog\n\n## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- tek ozellik\n"

        body = changelog.section_body(text, "1.0.0-preview.1")

        self.assertIn("tek ozellik", body)

    def test_olmayan_surum_none_doner(self):
        text = "# Changelog\n\n## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- ozellik\n"

        self.assertIsNone(changelog.section_body(text, "1.0.0-preview.2"))

    def test_bos_dosya_bolumsuz_sayilir(self):
        self.assertIsNone(changelog.section_body("", "1.0.0-preview.1"))
        self.assertFalse(changelog.has_section("", "1.0.0-preview.1"))

    def test_baslik_var_govde_bos_ise_has_section_false(self):
        text = "# Changelog\n\n## [1.0.0-preview.1] - 2026-08-28\n\n## [0.9.0] - 2026-08-01\n\n### Added\n- x\n"

        self.assertFalse(changelog.has_section(text, "1.0.0-preview.1"))

    def test_ayni_surum_iki_kez_yazilirsa_ilki_kullanilir(self):
        text = (
            "## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- birinci\n\n"
            "## [1.0.0-preview.1] - 2026-08-29\n\n### Added\n- ikinci\n"
        )

        body = changelog.section_body(text, "1.0.0-preview.1")

        self.assertIn("birinci", body)
        self.assertNotIn("ikinci", body)

    def test_tarihsiz_baslik_da_eslesir(self):
        text = "## [Unreleased]\n\n### Added\n- devam eden\n"

        self.assertTrue(changelog.has_section(text, "Unreleased"))

    # latest_released_version: yayın, tarihli ilk bölümdür; `site-deploy.sh` aynı kuralı okur.

    def test_son_yayin_ilk_tarihli_bolumdur(self):
        text = (
            "# Changelog\n\n"
            "## [Unreleased]\n\n### Added\n- devam eden\n\n"
            "## [1.0.0-preview.2] - 2026-09-01\n\n"
            "## [1.0.0-preview.1] - 2026-08-28\n"
        )

        self.assertEqual(changelog.latest_released_version(text), "1.0.0-preview.2")

    def test_tarihsiz_surum_bolumu_yayin_sayilmaz(self):
        text = "## [Unreleased]\n\n## [1.0.0-preview.3]\n\n## [1.0.0-preview.2] - 2026-09-01\n"

        self.assertEqual(changelog.latest_released_version(text), "1.0.0-preview.2")

    def test_tarihli_bolum_yoksa_son_yayin_yoktur(self):
        self.assertIsNone(changelog.latest_released_version("# Changelog\n\n## [Unreleased]\n"))

    # released_versions: kırıcı değişiklik tabanı yalnız bu kümedeki etiketi seçer; yanmış numaranın başlığı yoktur.

    def test_yayinlanmis_surumler_yalniz_tarihli_basliklardir(self):
        text = (
            "## [Unreleased]\n\n"
            "## [1.0.0-preview.4]\n\n"
            "## [1.0.0-preview.3] - 2026-09-24\n\n"
            "### Added\n- `## [1.0.0-preview.9] - 2026-01-01` bir başlık değil\n\n"
            "## [1.0.0-preview.1] - 2026-09-20\n"
        )

        self.assertEqual(changelog.released_versions(text), {"1.0.0-preview.3", "1.0.0-preview.1"})

    # release_notes: sürüm bölümü ETİKET anında yazılır, prova ondan önce koşar.

    def test_surum_bolumu_varsa_o_kazanir(self):
        text = (
            "## [Unreleased]\n\n### Added\n- devam eden\n\n"
            "## [1.0.0-preview.1] - 2026-08-28\n\n### Added\n- sevk edilen\n"
        )

        body, heading = changelog.release_notes(text, "1.0.0-preview.1")

        self.assertIn("sevk edilen", body)
        self.assertNotIn("devam eden", body)
        self.assertEqual(heading, "1.0.0-preview.1")

    def test_surum_bolumu_yoksa_unreleased_okunur(self):
        text = "# Changelog\n\n## [Unreleased]\n\n### Added\n- devam eden\n"

        body, heading = changelog.release_notes(text, "1.0.0-preview.1")

        self.assertIn("devam eden", body)
        self.assertEqual(heading, "Unreleased")

    def test_bos_unreleased_notsuz_surumu_engeller(self):
        text = "# Changelog\n\n## [Unreleased]\n\n## [0.9.0] - 2026-08-01\n\n### Added\n- eski\n"

        body, heading = changelog.release_notes(text, "1.0.0-preview.1")

        self.assertIsNone(body)
        self.assertIsNone(heading)

    def test_hicbir_bolum_yoksa_notsuz_sayilir(self):
        body, heading = changelog.release_notes("# Changelog\n", "1.0.0-preview.1")

        self.assertIsNone(body)
        self.assertIsNone(heading)

    def test_sigan_notlar_github_govdesine_degismeden_girer(self):
        notes = "Summary.\n\n### Fixed\n- one fix"

        self.assertEqual(changelog.github_release_body(notes, "1.0.0", limit=1_000), notes)

    def test_uzun_notlar_butun_gruplarla_sigar_ve_kalanlari_adlandirir(self):
        notes = (
            "Summary.\n\n"
            "### Security\n" + "- s\n" * 20 +
            "### Fixed\n" + "- f\n" * 20 +
            "### Added\n" + "- a\n" * 400 +
            "### Changed\n- c\n"
        )

        body = changelog.github_release_body(notes, "1.0.0-preview.9", limit=600)

        self.assertLessEqual(len(body), 600)
        self.assertIn("### Security", body)
        self.assertIn("### Fixed", body)
        self.assertNotIn("### Added", body)
        # Order is kept: the small group after the one that did not fit is
        # left out too, and both are named.
        self.assertNotIn("### Changed", body)
        self.assertIn("Not shown here: Added, Changed.", body)
        self.assertIn("https://tracon.dev/reference/changelog/#v1.0.0-preview.9", body)

    def test_ilk_gruptan_once_gelen_metin_tek_basina_sigmazsa_satirda_kesilir(self):
        notes = "".join(f"line {i}\n" for i in range(500)) + "### Fixed\n- f\n"

        body = changelog.github_release_body(notes, "1.0.0", limit=400)

        self.assertLessEqual(len(body), 400)
        head = body.split("\n\n---", 1)[0]
        self.assertTrue(notes.startswith(head + "\n"))
        self.assertIn("Not shown here: the rest of the notes.", body)

    def test_deponun_her_surum_notu_github_govdesine_sigar(self):
        """Every section the repository has cut fits once it is cut down."""
        path = pathlib.Path(__file__).resolve().parent.parent / "CHANGELOG.md"
        text = path.read_text(encoding="utf-8")
        versions = [
            match.group("version")
            for line in text.splitlines()
            if (match := changelog.SECTION_HEADER_PATTERN.match(line))
            and match.group("version") != changelog.UNRELEASED
        ]

        for version in versions:
            body = changelog.github_release_body(changelog.section_body(text, version) or "", version)
            self.assertLessEqual(len(body), changelog.GITHUB_RELEASE_BODY_LIMIT, version)

    def test_deponun_kendi_changelogu_provayi_gecirir(self):
        """Whatever state the repository's own file is in, the rehearsal finds notes.

        The assertion follows the file instead of freezing one moment of it.
        An earlier version demanded the heading be `Unreleased`, which was true
        only until the first version was cut -- it went red on cut day and said
        nothing about what had broken. Both states are legitimate: before a cut
        the notes come from Unreleased, after one they come from the version
        section, and in both the body has to be there.
        """
        path = pathlib.Path(__file__).resolve().parent.parent / "CHANGELOG.md"
        cut = [
            match.group("version")
            for line in path.read_text(encoding="utf-8").splitlines()
            if (match := changelog.SECTION_HEADER_PATTERN.match(line))
            and match.group("version") != changelog.UNRELEASED
        ]

        target = cut[0] if cut else "1.0.0-preview.1"
        body, heading = changelog.read_release_notes(path, target)

        self.assertIsNotNone(body)
        self.assertEqual(heading, target if cut else changelog.UNRELEASED)


if __name__ == "__main__":
    unittest.main()
