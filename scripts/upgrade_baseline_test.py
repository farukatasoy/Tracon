#!/usr/bin/env python3
"""Yükseltme kapısının taban betiğinin testleri: özet algoritması ve manifestin yeniden yazımı."""
from __future__ import annotations

import hashlib
import importlib.util
import json
import pathlib
import sys
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("upgrade_baseline", ROOT / "scripts" / "upgrade_baseline.py")
ub = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = ub
spec.loader.exec_module(ub)


class DeyimOzetiTestleri(unittest.TestCase):
    def test_yorum_ve_bosluk_ozeti_degistirmez(self):
        a = "-- eski yorum\nCREATE TABLE {schema}t (id TEXT);\n/* blok */\n"
        b = "-- yeni yorum, baska satir\n\nCREATE  TABLE {schema}t\n  (id TEXT);\n"

        self.assertEqual(ub.statement_digest(a), ub.statement_digest(b))

    def test_deyim_degisince_ozet_degisir(self):
        a = "CREATE TABLE {schema}t (id TEXT);"
        b = "CREATE TABLE {schema}t (id TEXT, extra TEXT);"

        self.assertNotEqual(ub.statement_digest(a), ub.statement_digest(b))

    def test_satir_sonu_farki_ozeti_degistirmez(self):
        self.assertEqual(ub.statement_digest("SELECT 1;\r\nSELECT 2;"), ub.statement_digest("SELECT 1;\nSELECT 2;"))

    def test_ham_toplam_yorumu_da_kapsar_ve_buyuk_harf_yazilir(self):
        text = "-- yorum\nSELECT 1;\n"
        expected = hashlib.sha256(text.encode("utf-8")).hexdigest().upper()

        self.assertEqual(ub.raw_checksum(text), expected)
        self.assertEqual(ub.raw_checksum(text.replace("\n", "\r\n")), expected)
        self.assertNotEqual(ub.raw_checksum(text), ub.raw_checksum("SELECT 1;\n"))

    def test_bom_ozeti_degistirmez(self):
        raw = b"\xef\xbb\xbfSELECT 1;\n"

        self.assertEqual(ub.describe("0001_initial.sql", raw)["checksum"], ub.describe("0001_initial.sql", b"SELECT 1;\n")["checksum"])

    def test_dosya_adi_biciminden_sira_numarasi_okunur(self):
        entry = ub.describe("0042_header_configuration_keys.sql", b"SELECT 1;")

        self.assertEqual(entry["id"], 42)
        self.assertEqual(entry["name"], "0042_header_configuration_keys")

    def test_bicime_uymayan_dosya_adi_reddedilir(self):
        with self.assertRaises(ValueError):
            ub.describe("initial.sql", b"SELECT 1;")


class ManifestTestleri(unittest.TestCase):
    def _manifest(self, version: str, exemptions: list[dict]) -> dict:
        return {"version": version, "tag": f"v{version}", "providers": {}, "exemptions": exemptions}

    def test_ayni_surumde_muafiyetler_korunur(self):
        previous = self._manifest("1.0.0-preview.6", [{"provider": "sqlite", "name": "0001_initial"}])

        with mock.patch.object(ub, "read_tag", return_value={}):
            manifest = ub.build_manifest(ROOT, "v1.0.0-preview.6", previous)

        self.assertEqual(manifest["exemptions"], previous["exemptions"])

    def test_yeni_surumde_muafiyetler_bosalir(self):
        previous = self._manifest("1.0.0-preview.6", [{"provider": "sqlite", "name": "0001_initial"}])

        with mock.patch.object(ub, "read_tag", return_value={}):
            manifest = ub.build_manifest(ROOT, "v1.0.0-rc.1", previous)

        self.assertEqual(manifest["version"], "1.0.0-rc.1")
        self.assertEqual(manifest["exemptions"], [])

    def test_depodaki_manifest_uc_saglayiciyi_ve_ayni_etiketi_tasir(self):
        manifest = json.loads((ROOT / ub.MANIFEST).read_text(encoding="utf-8"))

        self.assertEqual(sorted(manifest["providers"]), ["postgresql", "sqlite", "sqlserver"])
        self.assertEqual(manifest["tag"], "v" + manifest["version"])


if __name__ == "__main__":
    unittest.main()
