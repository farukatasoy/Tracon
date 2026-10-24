#!/usr/bin/env python3
"""Tests for the ApiCompat probe (Faz 237). Pure logic: the real `dotnet pack` runs are the manual case MT-PKG-170."""
from __future__ import annotations

import importlib.util
import pathlib
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("apicompat_yoklama", ROOT / "scripts" / "apicompat_yoklama.py")
probe = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = probe
spec.loader.exec_module(probe)


class YoklamaTestleri(unittest.TestCase):
    def test_her_varyantin_aciklamasi_ve_kaynakta_yeri_var(self):
        self.assertEqual(set(probe.EXPECTED), set(probe.DESCRIPTIONS))
        for variant in probe.EXPECTED:
            if variant != "CONTROL_NONE":
                self.assertIn(variant, probe.SOURCE, variant)

    def test_rapor_yoksa_kayit_yok(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(probe.diagnostics(pathlib.Path(directory) / "yok.xml"), frozenset())

    def test_rapor_tanilari_okunur(self):
        with tempfile.TemporaryDirectory() as directory:
            report = pathlib.Path(directory) / "r.xml"
            report.write_text(
                "<Suppressions><Suppression><DiagnosticId>CP0002</DiagnosticId></Suppression>"
                "<Suppression><DiagnosticId>CP0011</DiagnosticId></Suppression></Suppressions>", encoding="utf-8")

            self.assertEqual(probe.diagnostics(report), frozenset({"CP0002", "CP0011"}))

    def test_beklenenden_farkli_varyant_isaretlenir(self):
        measured = dict(probe.EXPECTED)
        measured["V_REQUIRED_NEW"] = frozenset({"CP0002"})

        lines, different = probe.render(measured)

        self.assertEqual(different, 1)
        self.assertTrue(any("V_REQUIRED_NEW" in line and "FARKLI" in line for line in lines))

    def test_beklenen_tablo_required_uyeyi_gormez(self):
        """The gap the snapshot closes: if this row changes, `required-uyeler.txt` may be redundant."""
        self.assertEqual(probe.EXPECTED["V_REQUIRED_NEW"], frozenset())
        self.assertEqual(probe.EXPECTED["V_OPTIONAL_TO_REQUIRED"], frozenset())


if __name__ == "__main__":
    unittest.main()
