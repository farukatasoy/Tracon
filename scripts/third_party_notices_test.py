#!/usr/bin/env python3
"""Tests for the third-party notice writer of the `tracon` tool package."""
from __future__ import annotations

import json
import pathlib
import sys
import tempfile
import unittest
import unittest.mock
import zipfile

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import third_party_notices as notices  # noqa: E402

MIT_BODY = "Permission is hereby granted, free of charge.\n"


def _nuspec(license_xml: str, copyright_text: str | None = "(c) Example Corp") -> str:
    copyright_xml = f"<copyright>{copyright_text}</copyright>" if copyright_text is not None else ""
    return (
        '<?xml version="1.0" encoding="utf-8"?>'
        '<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"><metadata>'
        f"<id>x</id>{license_xml}{copyright_xml}</metadata></package>")


class ThirdPartyNoticesTests(unittest.TestCase):
    def setUp(self):
        self._directory = tempfile.TemporaryDirectory()
        self.addCleanup(self._directory.cleanup)
        self.root = pathlib.Path(self._directory.name)
        self.packages = self.root / "nuget"
        self.licenses = self.root / "licenses"
        (self.licenses / "spdx").mkdir(parents=True)
        (self.licenses / "packages").mkdir()
        (self.licenses / "spdx" / "MIT.txt").write_text(MIT_BODY, encoding="utf-8")

    def _package(self, package_id: str, version: str, license_xml: str, files: dict[str, str] | None = None,
                 copyright_text: str | None = "(c) Example Corp") -> None:
        directory = self.packages / package_id.lower() / version
        directory.mkdir(parents=True)
        (directory / f"{package_id.lower()}.nuspec").write_text(
            _nuspec(license_xml, copyright_text), encoding="utf-8")
        for name, text in (files or {}).items():
            (directory / name).write_text(text, encoding="utf-8")

    def _render(self, *packages: tuple[str, str]) -> str:
        return notices.render(packages, self.packages, self.licenses)

    def test_a_package_without_a_licence_file_gets_the_standard_text_under_its_copyright_line(self):
        self._package("Plain.Package", "1.0.0", '<license type="expression">MIT</license>')

        text = self._render(("Plain.Package", "1.0.0"))

        self.assertIn("Plain.Package (MIT)", text)
        self.assertIn("(c) Example Corp\n\n" + MIT_BODY, text)

    def test_a_licence_file_the_package_carries_wins_over_the_standard_text(self):
        self._package("Own.Package", "2.0.0", '<license type="expression">MIT</license>',
                      {"LICENSE.TXT": "Own licence text\n"})

        text = self._render(("Own.Package", "2.0.0"))

        self.assertIn("Own licence text", text)
        self.assertNotIn(MIT_BODY, text)

    def test_a_package_override_serves_a_package_that_carries_no_file(self):
        self._package("Odd.Package", "1.0.0", '<license type="expression">BSD-3-Clause</license>')
        (self.licenses / "packages" / "Odd.Package.txt").write_text("Odd text\n", encoding="utf-8")

        text = self._render(("Odd.Package", "1.0.0"))

        self.assertIn("Odd.Package (BSD-3-Clause)", text)
        self.assertIn("Odd text", text)

    def test_a_licence_nobody_can_resolve_stops_the_script_and_says_what_to_add(self):
        self._package("Odd.Package", "1.0.0", '<license type="expression">BSD-3-Clause</license>')

        with self.assertRaises(notices.NoticeError) as raised:
            self._render(("Odd.Package", "1.0.0"))

        self.assertIn("spdx", str(raised.exception))
        self.assertIn("BSD-3-Clause.txt", str(raised.exception))

    def test_a_package_that_is_not_restored_stops_the_script(self):
        with self.assertRaises(notices.NoticeError) as raised:
            self._render(("Missing.Package", "1.0.0"))

        self.assertIn("restore", str(raised.exception))

    def test_a_package_with_no_copyright_and_no_authors_stops_the_script(self):
        self._package("Bare.Package", "1.0.0", '<license type="expression">MIT</license>', copyright_text=None)

        with self.assertRaises(notices.NoticeError):
            self._render(("Bare.Package", "1.0.0"))

    def test_packages_that_share_a_text_share_one_block_and_each_keeps_its_own_line(self):
        for name in ("A.Package", "B.Package"):
            self._package(name, "1.0.0", '<license type="expression">MIT</license>')

        text = self._render(("A.Package", "1.0.0"), ("B.Package", "1.0.0"))

        self.assertEqual(text.count(MIT_BODY), 1)
        self.assertIn("\nA.Package (MIT)\nB.Package (MIT)\n", text)

    def test_the_text_a_package_bundles_for_the_code_inside_it_is_printed_once(self):
        for name in ("A.Package", "B.Package"):
            self._package(name, "1.0.0", '<license type="expression">MIT</license>',
                          {"THIRD-PARTY-NOTICES.TXT": "Bundled zlib notice\n"})

        text = self._render(("A.Package", "1.0.0"), ("B.Package", "1.0.0"))

        self.assertEqual(text.count("Bundled zlib notice"), 1)
        self.assertIn("A.Package, B.Package", text)

    def test_the_notice_names_no_version_so_a_bump_that_keeps_the_texts_leaves_it_unchanged(self):
        self._package("Plain.Package", "1.0.0", '<license type="expression">MIT</license>')
        self._package("Plain.Package", "1.0.1", '<license type="expression">MIT</license>')

        self.assertEqual(self._render(("Plain.Package", "1.0.0")), self._render(("Plain.Package", "1.0.1")))
        self.assertNotIn("1.0.0", self._render(("Plain.Package", "1.0.0")))

    def test_windows_line_endings_in_a_licence_file_do_not_reach_the_notice(self):
        self._package("Crlf.Package", "1.0.0", '<license type="expression">MIT</license>')
        (self.packages / "crlf.package" / "1.0.0" / "LICENSE").write_bytes(b"Line one  \r\nLine two\r\n")

        text = self._render(("Crlf.Package", "1.0.0"))

        self.assertNotIn("\r", text)
        self.assertIn("Line one\nLine two\n", text)

    def test_only_nuget_packages_of_the_dependency_manifest_are_listed(self):
        deps = json.dumps({"libraries": {
            "Zeta/1.0.0": {"type": "package"},
            "Tracon.Core/1.0.0": {"type": "project"},
            "alpha/2.0.0": {"type": "package"}}})

        self.assertEqual(notices.packages_from_deps(deps), [("alpha", "2.0.0"), ("Zeta", "1.0.0")])

    def _tool_package(self, deps: dict) -> pathlib.Path:
        path = self.root / "Tracon.Cli.1.0.0.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr(notices.DEPS_ENTRY, json.dumps(deps))
        return path

    def test_drift_is_reported_for_a_missing_a_stale_and_a_matching_notice(self):
        self._package("Plain.Package", "1.0.0", '<license type="expression">MIT</license>')
        nupkg = self._tool_package({"libraries": {"Plain.Package/1.0.0": {"type": "package"}}})
        source = self.root / notices.NOTICE_SOURCE
        (self.root / "scripts").mkdir()
        (self.root / "scripts" / "third_party_licenses").symlink_to(self.licenses, target_is_directory=True)

        with unittest.mock.patch.dict("os.environ", {"NUGET_PACKAGES": str(self.packages)}):
            self.assertIn("is missing", notices.drift_error(self.root, nupkg))

            source.parent.mkdir(parents=True)
            source.write_text("stale\n", encoding="utf-8")
            self.assertIn("does not match", notices.drift_error(self.root, nupkg))

            source.write_bytes(notices.expected_notice(nupkg, self.root).encode("utf-8"))
            self.assertIsNone(notices.drift_error(self.root, nupkg))

    def test_a_tool_package_without_a_dependency_manifest_is_an_error(self):
        path = self.root / "Empty.nupkg"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("readme.txt", "x")

        with self.assertRaises(notices.NoticeError):
            notices.deps_from_package(path)


if __name__ == "__main__":
    unittest.main()
