#!/usr/bin/env python3
"""Tests for the packed extension-sample release gate's deterministic checks."""
from __future__ import annotations

import importlib.util
import pathlib
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


def _write_clean_tree(root: pathlib.Path) -> None:
    samples = root / "samples"
    for project in (*release_extension_samples.SAMPLE_TEST_PROJECTS, release_extension_samples.AOT_PROJECT):
        project_dir = samples / project
        project_dir.mkdir(parents=True)
        (project_dir / f"{project}.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk">'
            f'<ItemGroup><PackageReference Include="AgentPrism" VersionOverride="$(AgentPrismSamplePackageVersion)" /></ItemGroup>'
            f'</Project>',
            encoding="utf-8",
        )


class ReleaseExtensionSamplesTestleri(unittest.TestCase):
    def test_temiz_agac_sifir_hata_uretir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertEqual(errors, [])

    def test_bos_surum_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, "")

        self.assertTrue(any("exact AgentPrism sample package version" in error for error in errors))

    def test_wildcard_surum_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, "1.0.0-preview.*")

        self.assertTrue(any("exact AgentPrism sample package version" in error for error in errors))

    def test_eksik_sample_listesi_ogesi_yakalanir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            # One required project's directory is missing entirely.
            missing = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            import shutil
            shutil.rmtree(root / "samples" / missing)

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any(f"missing release sample project: {missing}" in error for error in errors))

    def test_wildcard_version_override_yakalanir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            project = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            csproj = root / "samples" / project / f"{project}.csproj"
            csproj.write_text(
                '<Project Sdk="Microsoft.NET.Sdk">'
                '<ItemGroup><PackageReference Include="AgentPrism" VersionOverride="*-*" /></ItemGroup>'
                '</Project>',
                encoding="utf-8",
            )

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("wildcard VersionOverride" in error for error in errors))

    def test_agentprism_kaynak_projectreference_yakalanir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            project = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            csproj = root / "samples" / project / f"{project}.csproj"
            csproj.write_text(
                '<Project Sdk="Microsoft.NET.Sdk">'
                '<ItemGroup><ProjectReference Include="../../src/AgentPrism.Core/AgentPrism.Core.csproj" /></ItemGroup>'
                '</Project>',
                encoding="utf-8",
            )

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("references AgentPrism source" in error for error in errors))

    def test_stale_feed_paketi_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            release_dir = root / "release"
            release_dir.mkdir()
            (release_dir / "AgentPrism.Core.1.0.0-preview.0.nupkg").write_bytes(b"")

            result = release_extension_samples.verify(root, release_dir, EXACT_VERSION)

        self.assertEqual(result, 1)

    def test_kuresel_nuget_cache_kullanilmaz(self):
        # The isolation contract itself: `verify` must set NUGET_PACKAGES to a
        # fresh temp directory, never the developer's global cache.
        source = pathlib.Path(release_extension_samples.__file__).read_text(encoding="utf-8")

        self.assertIn('environment["NUGET_PACKAGES"] = str(package_cache)', source)
        self.assertNotIn(".nuget/packages", source)


if __name__ == "__main__":
    unittest.main()
