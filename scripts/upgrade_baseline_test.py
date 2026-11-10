#!/usr/bin/env python3
"""Tests of the baseline script of the upgrade gate: the digest algorithm and the rewrite of the manifest."""
from __future__ import annotations

import contextlib
import hashlib
import importlib.util
import io
import json
import pathlib
import re
import sys
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("upgrade_baseline", ROOT / "scripts" / "upgrade_baseline.py")
ub = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = ub
spec.loader.exec_module(ub)


class StatementDigestTests(unittest.TestCase):
    def test_comments_and_whitespace_do_not_change_the_digest(self):
        a = "-- old comment\nCREATE TABLE {schema}t (id TEXT);\n/* block */\n"
        b = "-- new comment, another line\n\nCREATE  TABLE {schema}t\n  (id TEXT);\n"

        self.assertEqual(ub.statement_digest(a), ub.statement_digest(b))

    def test_a_changed_statement_changes_the_digest(self):
        a = "CREATE TABLE {schema}t (id TEXT);"
        b = "CREATE TABLE {schema}t (id TEXT, extra TEXT);"

        self.assertNotEqual(ub.statement_digest(a), ub.statement_digest(b))

    def test_line_ending_differences_do_not_change_the_digest(self):
        self.assertEqual(ub.statement_digest("SELECT 1;\r\nSELECT 2;"), ub.statement_digest("SELECT 1;\nSELECT 2;"))

    def test_raw_checksum_covers_comments_and_is_upper_case(self):
        text = "-- comment\nSELECT 1;\n"
        expected = hashlib.sha256(text.encode("utf-8")).hexdigest().upper()

        self.assertEqual(ub.raw_checksum(text), expected)
        self.assertEqual(ub.raw_checksum(text.replace("\n", "\r\n")), expected)
        self.assertNotEqual(ub.raw_checksum(text), ub.raw_checksum("SELECT 1;\n"))

    def test_a_bom_does_not_change_the_checksum(self):
        raw = b"\xef\xbb\xbfSELECT 1;\n"

        self.assertEqual(ub.describe("0001_initial.sql", raw)["checksum"], ub.describe("0001_initial.sql", b"SELECT 1;\n")["checksum"])

    def test_the_sequence_number_is_read_from_the_file_name(self):
        entry = ub.describe("0042_header_configuration_keys.sql", b"SELECT 1;")

        self.assertEqual(entry["id"], 42)
        self.assertEqual(entry["name"], "0042_header_configuration_keys")

    def test_a_file_name_that_does_not_match_the_pattern_is_rejected(self):
        with self.assertRaises(ValueError):
            ub.describe("initial.sql", b"SELECT 1;")


class ManifestTests(unittest.TestCase):
    def _manifest(self, version: str, exemptions: list[dict]) -> dict:
        return {"version": version, "tag": f"v{version}", "providers": {}, "exemptions": exemptions}

    def test_exemptions_are_kept_for_the_same_version(self):
        previous = self._manifest("1.0.0-preview.6", [{"provider": "sqlite", "name": "0001_initial"}])

        with mock.patch.object(ub, "read_tag", return_value={}), mock.patch.object(ub, "read_optional_tag", return_value={}):
            manifest = ub.build_manifest(ROOT, "1.0.0-preview.6", None, previous)

        self.assertEqual(manifest["exemptions"], previous["exemptions"])

    def test_exemptions_are_emptied_for_a_new_version(self):
        previous = self._manifest("1.0.0-preview.6", [{"provider": "sqlite", "name": "0001_initial"}])

        with mock.patch.object(ub, "read_tag", return_value={}), mock.patch.object(ub, "read_optional_tag", return_value={}):
            manifest = ub.build_manifest(ROOT, "1.0.0-rc.1", None, previous)

        self.assertEqual(manifest["version"], "1.0.0-rc.1")
        self.assertEqual(manifest["exemptions"], [])

    def test_the_tag_of_the_version_is_read_without_a_source_and_the_source_otherwise(self):
        with mock.patch.object(ub, "read_tag", return_value={}) as read, mock.patch.object(ub, "read_optional_tag", return_value={}):
            ub.build_manifest(ROOT, "1.0.0-rc.1", None, None)
            ub.build_manifest(ROOT, "1.0.0-rc.1", "HEAD", None)

        self.assertEqual([call.args[1] for call in read.call_args_list], ["v1.0.0-rc.1", "HEAD"])

    def test_the_manifest_in_the_repository_holds_three_providers_and_a_matching_tag(self):
        manifest = json.loads((ROOT / ub.MANIFEST).read_text(encoding="utf-8"))

        self.assertEqual(sorted(manifest["providers"]), ["postgresql", "sqlite", "sqlserver"])
        self.assertEqual(manifest["tag"], "v" + manifest["version"])

    def test_optional_sets_hold_only_the_files_the_release_has(self):
        manifest = json.loads((ROOT / ub.MANIFEST).read_text(encoding="utf-8"))
        optional = manifest["optionalSets"]

        self.assertEqual(sorted(optional), ["postgresql", "sqlite", "sqlserver"])
        # `views` exists for all three providers, `knowledge` only for PostgreSQL; the files of every set are ordered and unique.
        self.assertEqual(sorted(optional["postgresql"]), ["knowledge", "views"])
        self.assertEqual(sorted(optional["sqlite"]), ["views"])
        self.assertEqual(sorted(optional["sqlserver"]), ["views"])
        for sets in optional.values():
            for entries in sets.values():
                ids = [entry["id"] for entry in entries]
                self.assertEqual(ids, sorted(set(ids)))

    def test_a_changed_optional_set_file_is_reported(self):
        manifest = {
            "providers": {},
            "optionalSets": {"sqlite": {"views": [{"name": "0001_read_views", "statements": "0" * 64}]}},
        }

        changed = ub.differing_from_head(ROOT, manifest)

        self.assertEqual(changed, ["sqlite/views/0001_read_views"])


class OutputLanguageTests(unittest.TestCase):
    """Everything the script prints is English: a contributor of the public repository reads it."""

    # Written as code points so that this source file holds no Turkish letter itself.
    TURKISH_LETTERS = re.compile("[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]")

    def test_the_help_text_names_the_english_flags_only(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output), self.assertRaises(SystemExit):
            ub.main(["--help"])

        for flag in ("--write", "--package-version", "--source"):
            self.assertIn(flag, output.getvalue())
        self.assertIsNone(self.TURKISH_LETTERS.search(output.getvalue()), output.getvalue())

    def test_the_old_flag_names_are_gone(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
            ub.main(["--yaz"])

        self.assertEqual(2, raised.exception.code)

    def test_a_call_without_a_task_says_what_to_choose_in_english(self):
        error = io.StringIO()
        with contextlib.redirect_stderr(error), self.assertRaises(SystemExit) as raised:
            ub.main([])

        self.assertEqual(2, raised.exception.code)
        self.assertIn("choose what to do: --write", error.getvalue())

    def test_a_deleted_migration_file_is_marked_in_english(self):
        manifest = {
            "providers": {"sqlite": [{"name": "9999_removed_for_the_test", "statements": "0" * 64}]},
            "optionalSets": {},
        }

        changed = ub.differing_from_head(ROOT, manifest)

        self.assertEqual(["sqlite/9999_removed_for_the_test (deleted)"], changed)

    def test_a_file_name_that_does_not_match_is_reported_in_english(self):
        with self.assertRaises(ValueError) as raised:
            ub.describe("initial.sql", b"SELECT 1;")

        self.assertIn("does not match the pattern", str(raised.exception))
        self.assertIsNone(self.TURKISH_LETTERS.search(str(raised.exception)))


if __name__ == "__main__":
    unittest.main()
