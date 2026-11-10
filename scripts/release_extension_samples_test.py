#!/usr/bin/env python3
"""Tests for the packed extension-sample release gate's deterministic checks."""
from __future__ import annotations

import contextlib
import importlib.util
import io
import xml.etree.ElementTree as ElementTree
import pathlib
import re
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "release_extension_samples", ROOT / "scripts" / "release_extension_samples.py")
release_extension_samples = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = release_extension_samples
spec.loader.exec_module(release_extension_samples)

EXACT_VERSION = "1.0.0-preview.1"


COMPATIBILITY_TABLE = """\
| Package | Meta | Frameworks | Native AOT | Purpose or limit |
|---|---:|---|---:|---|
| `Tracon.OpenAI` | Yes | net10 | Yes | OpenAI |
| `Tracon.Anthropic` | No | net10 | No | Claude provider |
| `Tracon.Google` | No | net10 | No | Gemini provider |
| `Tracon.Azure` | No | net10 | Yes | Azure OpenAI provider |
| `Tracon.Voice` | No | net10 | Yes | ElevenLabs |
"""

SMOKE_PACKAGES = ("Tracon.Core", "Tracon.OpenAI", "Tracon.Azure", "Tracon.Voice")


def _write_compatibility_page(root: pathlib.Path, table: str = COMPATIBILITY_TABLE) -> None:
    page = root / release_extension_samples.COMPATIBILITY_PAGE
    page.parent.mkdir(parents=True, exist_ok=True)
    page.write_text(f"# Compatibility\n\nSome prose.\n\n{table}\nAfter the table.\n", encoding="utf-8")


def _write_clean_tree(root: pathlib.Path) -> None:
    samples = root / "samples"
    for project in (*release_extension_samples.SAMPLE_TEST_PROJECTS, *release_extension_samples.AOT_PROJECTS):
        project_dir = samples / project
        project_dir.mkdir(parents=True)
        packages = SMOKE_PACKAGES if project == release_extension_samples.PROVIDER_AOT_PROJECT else ("Tracon",)
        references = "".join(
            f'<PackageReference Include="{package}" VersionOverride="$(TraconSamplePackageVersion)" />'
            for package in packages)
        (project_dir / f"{project}.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk"><ItemGroup>{references}</ItemGroup></Project>',
            encoding="utf-8",
        )
    _write_compatibility_page(root)


class ReleaseExtensionSamplesTests(unittest.TestCase):
    def test_a_clean_tree_produces_no_errors(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertEqual(errors, [])

    def test_a_provider_aot_promise_without_a_smoke_is_rejected(self):
        # "Native AOT: Yes" is given only to a package the smoke proves. Flipping
        # Anthropic to "Yes" must not pass without adding it to the smoke.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            _write_compatibility_page(root, COMPATIBILITY_TABLE.replace(
                "| `Tracon.Anthropic` | No | net10 | No |", "| `Tracon.Anthropic` | No | net10 | Yes |"))

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("Tracon.Anthropic" in error and "no smoke behind it" in error for error in errors), errors)

    def test_a_withdrawn_provider_aot_promise_needs_no_smoke(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertEqual(errors, [])

    def test_a_yes_row_in_neither_list_is_rejected(self):
        # A new package cannot claim "Yes" and stay out of both lists.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            _write_compatibility_page(root, COMPATIBILITY_TABLE + "| `Tracon.NewProvider` | No | net10 | Yes | New |\n")

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertTrue(any("Tracon.NewProvider" in error and "neither" in error for error in errors), errors)

    def test_a_provider_row_removed_from_the_table_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            _write_compatibility_page(root, "\n".join(
                line for line in COMPATIBILITY_TABLE.splitlines() if "Tracon.Google" not in line) + "\n")

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertTrue(any("no Native AOT row for Tracon.Google" in error for error in errors), errors)

    def test_a_missing_compatibility_page_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            (root / release_extension_samples.COMPATIBILITY_PAGE).unlink()

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertTrue(any("cannot be checked" in error for error in errors), errors)

    def test_the_native_aot_column_is_read_by_its_header(self):
        promises = release_extension_samples.native_aot_promises(
            "intro\n\n" + COMPATIBILITY_TABLE + "\n| Other | Native |\n|---|---|\n| `Tracon.X` | Yes |\n")

        self.assertEqual(promises["Tracon.OpenAI"], "Yes")
        self.assertEqual(promises["Tracon.Anthropic"], "No")
        self.assertNotIn("Tracon.X", promises)

    def test_the_real_tree_keeps_provider_aot_promises_in_step_with_the_smoke(self):
        # The real site table against the real smoke project. Red here means a
        # package was promised AOT (or the smoke lost a package) without the
        # other half being updated.
        self.assertEqual(release_extension_samples.validate_provider_aot_promises(ROOT), [])

    def test_an_empty_version_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, "")

        self.assertTrue(any("exact Tracon sample package version" in error for error in errors))

    def test_a_wildcard_version_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, "1.0.0-preview.*")

        self.assertTrue(any("exact Tracon sample package version" in error for error in errors))

    def test_a_missing_sample_project_is_caught(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            # One required project's directory is missing entirely.
            missing = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            import shutil
            shutil.rmtree(root / "samples" / missing)

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any(f"missing release sample project: {missing}" in error for error in errors))

    def test_a_wildcard_version_override_is_caught(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            project = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            csproj = root / "samples" / project / f"{project}.csproj"
            csproj.write_text(
                '<Project Sdk="Microsoft.NET.Sdk">'
                '<ItemGroup><PackageReference Include="Tracon" VersionOverride="*-*" /></ItemGroup>'
                '</Project>',
                encoding="utf-8",
            )

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("wildcard VersionOverride" in error for error in errors))

    def test_a_project_reference_to_tracon_source_is_caught(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            project = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            csproj = root / "samples" / project / f"{project}.csproj"
            csproj.write_text(
                '<Project Sdk="Microsoft.NET.Sdk">'
                '<ItemGroup><ProjectReference Include="../../src/Tracon.Core/Tracon.Core.csproj" /></ItemGroup>'
                '</Project>',
                encoding="utf-8",
            )

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("references Tracon source" in error for error in errors))

    def test_a_stale_feed_package_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            release_dir = root / "release"
            release_dir.mkdir()
            (release_dir / "Tracon.Core.1.0.0-preview.0.nupkg").write_bytes(b"")

            result = release_extension_samples.verify(root, release_dir, EXACT_VERSION)

        self.assertEqual(result, 1)

    def test_a_new_sample_missing_from_the_inventory_is_caught(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            # A new sample test project directory exists but was never added
            # to SAMPLE_TEST_PROJECTS or SAMPLE_TEST_EXCLUSIONS — the exact
            # drift that once let CustomJobHandler.Tests go unrun.
            stray = root / "samples" / "Tracon.Samples.Stray.Tests"
            stray.mkdir(parents=True)
            (stray / "Tracon.Samples.Stray.Tests.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk" />', encoding="utf-8")

            errors = release_extension_samples.validate_sample_inventory(root)

        self.assertTrue(any("Tracon.Samples.Stray.Tests" in error for error in errors))

    def test_a_listed_sample_that_does_not_exist_is_caught(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            missing = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            import shutil
            shutil.rmtree(root / "samples" / missing)

            errors = release_extension_samples.validate_sample_inventory(root)

        self.assertTrue(any(missing in error for error in errors))

    def test_an_exclusion_without_a_reason_is_caught(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            extra = root / "samples" / "Tracon.Samples.Exempt.Tests"
            extra.mkdir(parents=True)
            (extra / "Tracon.Samples.Exempt.Tests.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk" />', encoding="utf-8")

            original = dict(release_extension_samples.SAMPLE_TEST_EXCLUSIONS)
            release_extension_samples.SAMPLE_TEST_EXCLUSIONS["Tracon.Samples.Exempt.Tests"] = "   "
            try:
                errors = release_extension_samples.validate_sample_inventory(root)
            finally:
                release_extension_samples.SAMPLE_TEST_EXCLUSIONS.clear()
                release_extension_samples.SAMPLE_TEST_EXCLUSIONS.update(original)

        self.assertTrue(any("has no reason" in error for error in errors))

    def test_the_global_nuget_cache_is_not_used(self):
        # The isolation contract itself: `verify` must set NUGET_PACKAGES to a
        # fresh temp directory, never the developer's global cache.
        source = pathlib.Path(release_extension_samples.__file__).read_text(encoding="utf-8")

        self.assertIn('environment["NUGET_PACKAGES"] = str(package_cache)', source)
        self.assertNotIn(".nuget/packages", source)

    def test_the_main_solution_excludes_the_packed_consumer_samples(self):
        solution = ElementTree.parse(ROOT / "Tracon.slnx")
        projects = {node.attrib["Path"] for node in solution.iter("Project")}

        packed_consumers = {
            str(path.relative_to(ROOT)).replace("\\", "/")
            for path in (ROOT / "samples").glob("Tracon.Samples.*/*.csproj")
        }

        self.assertTrue(packed_consumers)
        self.assertEqual(projects & packed_consumers, set())


if __name__ == "__main__":
    unittest.main()


class TraconDependencyClosureTests(unittest.TestCase):
    """`release-dryrun` does not restore; the dependency graph of the contracts package is read from the packages."""

    VERSION = "1.0.0-preview.3"

    def _write_package(self, directory: pathlib.Path, package_id: str, *dependencies: str) -> None:
        import zipfile
        deps = "".join(
            f'<dependency id="{dependency}" version="[{self.VERSION}]" exclude="Build,Analyzers" />'
            for dependency in dependencies)
        nuspec = (f'<package><metadata><id>{package_id}</id><dependencies>'
                  f'<group targetFramework="net10.0">{deps}<dependency id="xunit.v3.assert" version="3.2.2" /></group>'
                  f'</dependencies></metadata></package>')
        with zipfile.ZipFile(directory / f"{package_id}.{self.VERSION}.nupkg", "w") as archive:
            archive.writestr(f"{package_id}.nuspec", nuspec)

    def test_a_transitive_tracon_dependency_is_found(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_package(root, "Tracon.Testing.Contracts.Xunit", "Tracon.Abstractions")
            self._write_package(root, "Tracon.Abstractions", "Tracon.Core")
            self._write_package(root, "Tracon.Core")

            closure = release_extension_samples.tracon_dependency_closure(
                root, "Tracon.Testing.Contracts.Xunit", self.VERSION)

        self.assertEqual(closure, {"Tracon.Abstractions", "Tracon.Core"})

    def test_a_third_party_dependency_stays_out_of_the_closure(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_package(root, "Tracon.Testing.Contracts.Xunit", "Tracon.Abstractions")
            self._write_package(root, "Tracon.Abstractions")

            closure = release_extension_samples.tracon_dependency_closure(
                root, "Tracon.Testing.Contracts.Xunit", self.VERSION)

        self.assertEqual(closure, {"Tracon.Abstractions"})

    def test_the_source_projects_obj_directory_is_not_read(self):
        """Regression guard: `verify` used to read artifacts/obj for the contracts package."""
        source = pathlib.Path(release_extension_samples.__file__).read_text(encoding="utf-8")
        self.assertNotIn('_project_assets_json(root, "Tracon.Testing.Contracts.Xunit")', source)


class OutputLanguageTests(unittest.TestCase):
    """Everything the gate prints is English: a contributor of the public repository reads it."""

    def test_a_violated_contract_is_reported_in_english(self):
        with tempfile.TemporaryDirectory() as root_directory, tempfile.TemporaryDirectory() as feed_directory:
            feed = pathlib.Path(feed_directory)
            (feed / "Tracon.Core.0.0.1.nupkg").write_bytes(b"")
            output = io.StringIO()

            with contextlib.redirect_stdout(output):
                code = release_extension_samples.verify(pathlib.Path(root_directory), feed, EXACT_VERSION)

        self.assertEqual(1, code)
        self.assertIn("The extension sample contract is violated", output.getvalue())
        self.assertIn("release feed contains stale Tracon packages", output.getvalue())
        # Written as code points so that this source file holds no Turkish letter itself.
        self.assertIsNone(
            re.search("[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]", output.getvalue()),
            output.getvalue())
