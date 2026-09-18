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

    def test_deponun_kendi_changelogu_provayi_gecirir(self):
        root = pathlib.Path(__file__).resolve().parent.parent
        body, heading = changelog.read_release_notes(root / "CHANGELOG.md", "1.0.0-preview.1")

        self.assertIsNotNone(body)
        self.assertEqual(heading, "Unreleased")


if __name__ == "__main__":
    unittest.main()
