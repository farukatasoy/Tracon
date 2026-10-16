#!/usr/bin/env python3
"""Tests for the packed extension-sample release gate's deterministic checks."""
from __future__ import annotations

import importlib.util
import xml.etree.ElementTree as ElementTree
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


COMPATIBILITY_TABLE = """\
| Package | Meta | Frameworks | Native AOT | Purpose or limit |
|---|---:|---|---:|---|
| `Tracon.OpenAI` | Yes | net8/9/10 | Yes | OpenAI |
| `Tracon.Anthropic` | No | net8/9/10 | No | Claude provider |
| `Tracon.Google` | No | net8/9/10 | No | Gemini provider |
| `Tracon.Azure` | No | net8/9/10 | Yes | Azure OpenAI provider |
| `Tracon.Voice` | No | net8/9/10 | Yes | ElevenLabs |
"""

SMOKE_PACKAGES = ("Tracon.Core", "Tracon.OpenAI", "Tracon.Azure", "Tracon.Voice")


def _write_compatibility_page(root: pathlib.Path, table: str = COMPATIBILITY_TABLE) -> None:
    page = root / release_extension_samples.COMPATIBILITY_PAGE
    page.parent.mkdir(parents=True, exist_ok=True)
    page.write_text(f"# Compatibility\n\nSome prose.\n\n{table}\nAfter the table.\n", encoding="utf-8")


def _write_clean_tree(root: pathlib.Path) -> None:
    samples = root / "samples"
    for project in (
        *release_extension_samples.SAMPLE_TEST_PROJECTS,
        *release_extension_samples.AOT_PROJECTS,
        release_extension_samples.NET8_CONSUMER_PROJECT,
    ):
        project_dir = samples / project
        project_dir.mkdir(parents=True)
        framework = (
            "<PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>"
            if project == release_extension_samples.NET8_CONSUMER_PROJECT else "")
        packages = SMOKE_PACKAGES if project == release_extension_samples.PROVIDER_AOT_PROJECT else ("Tracon",)
        references = "".join(
            f'<PackageReference Include="{package}" VersionOverride="$(TraconSamplePackageVersion)" />'
            for package in packages)
        (project_dir / f"{project}.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk">{framework}<ItemGroup>{references}</ItemGroup></Project>',
            encoding="utf-8",
        )
    _write_compatibility_page(root)


class ReleaseExtensionSamplesTestleri(unittest.TestCase):
    def test_temiz_agac_sifir_hata_uretir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertEqual(errors, [])

    def test_net8_tuketicisi_net8_hedeflemezse_reddedilir(self):
        # Faz 183: <TargetFramework> satiri silinirse ornek net10.0'i
        # samples/Directory.Build.props'tan miras alir ve yine GECER.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            project = release_extension_samples.NET8_CONSUMER_PROJECT
            csproj = root / "samples" / project / f"{project}.csproj"
            csproj.write_text(
                csproj.read_text(encoding="utf-8").replace("net8.0", "net10.0"), encoding="utf-8")

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("must target net8.0" in error for error in errors), errors)

    def test_saglayici_aot_vaadi_smoke_olmadan_reddedilir(self):
        # Faz 217 (MT-PKG-168): "Native AOT: Yes" yalnız smoke'un kanıtladığı
        # paket için verilir. Anthropic'i "Yes" yapmak smoke'a eklemeden geçmemeli.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            _write_compatibility_page(root, COMPATIBILITY_TABLE.replace(
                "| `Tracon.Anthropic` | No | net8/9/10 | No |", "| `Tracon.Anthropic` | No | net8/9/10 | Yes |"))

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("Tracon.Anthropic" in error and "no smoke behind it" in error for error in errors), errors)

    def test_saglayici_aot_vaadi_geri_cekilince_smoke_zorunlu_olmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertEqual(errors, [])

    def test_listede_olmayan_yes_satiri_reddedilir(self):
        # Yeni bir paket "Yes" yazıp iki listeden de dışarıda kalamaz.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            _write_compatibility_page(root, COMPATIBILITY_TABLE + "| `Tracon.NewProvider` | No | net10 | Yes | New |\n")

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertTrue(any("Tracon.NewProvider" in error and "neither" in error for error in errors), errors)

    def test_saglayici_satiri_tablodan_silinirse_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            _write_compatibility_page(root, "\n".join(
                line for line in COMPATIBILITY_TABLE.splitlines() if "Tracon.Google" not in line) + "\n")

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertTrue(any("no Native AOT row for Tracon.Google" in error for error in errors), errors)

    def test_uyumluluk_sayfasi_yoksa_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            (root / release_extension_samples.COMPATIBILITY_PAGE).unlink()

            errors = release_extension_samples.validate_provider_aot_promises(root)

        self.assertTrue(any("cannot be checked" in error for error in errors), errors)

    def test_native_aot_sutunu_basliga_gore_okunur(self):
        promises = release_extension_samples.native_aot_promises(
            "intro\n\n" + COMPATIBILITY_TABLE + "\n| Other | Native |\n|---|---|\n| `Tracon.X` | Yes |\n")

        self.assertEqual(promises["Tracon.OpenAI"], "Yes")
        self.assertEqual(promises["Tracon.Anthropic"], "No")
        self.assertNotIn("Tracon.X", promises)

    def test_gercek_agac_saglayici_aot_vaatlerini_smoke_ile_tutar(self):
        # The real site table against the real smoke project. Red here means a
        # package was promised AOT (or the smoke lost a package) without the
        # other half being updated.
        self.assertEqual(release_extension_samples.validate_provider_aot_promises(ROOT), [])

    def test_gercek_net8_tuketicisi_net8_hedefler(self):
        errors = release_extension_samples.validate_sample_contract(ROOT, EXACT_VERSION)

        self.assertFalse(any("net8" in error for error in errors), errors)

    def test_bos_surum_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, "")

        self.assertTrue(any("exact Tracon sample package version" in error for error in errors))

    def test_wildcard_surum_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)

            errors = release_extension_samples.validate_sample_contract(root, "1.0.0-preview.*")

        self.assertTrue(any("exact Tracon sample package version" in error for error in errors))

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
                '<ItemGroup><PackageReference Include="Tracon" VersionOverride="*-*" /></ItemGroup>'
                '</Project>',
                encoding="utf-8",
            )

            errors = release_extension_samples.validate_sample_contract(root, EXACT_VERSION)

        self.assertTrue(any("wildcard VersionOverride" in error for error in errors))

    def test_tracon_kaynak_projectreference_yakalanir(self):
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

    def test_stale_feed_paketi_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            release_dir = root / "release"
            release_dir.mkdir()
            (release_dir / "Tracon.Core.1.0.0-preview.0.nupkg").write_bytes(b"")

            result = release_extension_samples.verify(root, release_dir, EXACT_VERSION)

        self.assertEqual(result, 1)

    def test_envanterden_sarkan_yeni_sample_yakalanir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            # A new sample test project directory exists but was never added
            # to SAMPLE_TEST_PROJECTS or SAMPLE_TEST_EXCLUSIONS — the exact
            # drift that let CustomJobHandler.Tests go unrun for three phases.
            stray = root / "samples" / "Tracon.Samples.Deneme.Tests"
            stray.mkdir(parents=True)
            (stray / "Tracon.Samples.Deneme.Tests.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk" />', encoding="utf-8")

            errors = release_extension_samples.validate_sample_inventory(root)

        self.assertTrue(any("Tracon.Samples.Deneme.Tests" in error for error in errors))

    def test_var_olmayan_sample_listede_kalirsa_yakalanir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            missing = release_extension_samples.SAMPLE_TEST_PROJECTS[0]
            import shutil
            shutil.rmtree(root / "samples" / missing)

            errors = release_extension_samples.validate_sample_inventory(root)

        self.assertTrue(any(missing in error for error in errors))

    def test_gerekcesiz_disleme_yakalanir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_clean_tree(root)
            extra = root / "samples" / "Tracon.Samples.Muaf.Tests"
            extra.mkdir(parents=True)
            (extra / "Tracon.Samples.Muaf.Tests.csproj").write_text(
                '<Project Sdk="Microsoft.NET.Sdk" />', encoding="utf-8")

            original = dict(release_extension_samples.SAMPLE_TEST_EXCLUSIONS)
            release_extension_samples.SAMPLE_TEST_EXCLUSIONS["Tracon.Samples.Muaf.Tests"] = "   "
            try:
                errors = release_extension_samples.validate_sample_inventory(root)
            finally:
                release_extension_samples.SAMPLE_TEST_EXCLUSIONS.clear()
                release_extension_samples.SAMPLE_TEST_EXCLUSIONS.update(original)

        self.assertTrue(any("has no reason" in error for error in errors))

    def test_kuresel_nuget_cache_kullanilmaz(self):
        # The isolation contract itself: `verify` must set NUGET_PACKAGES to a
        # fresh temp directory, never the developer's global cache.
        source = pathlib.Path(release_extension_samples.__file__).read_text(encoding="utf-8")

        self.assertIn('environment["NUGET_PACKAGES"] = str(package_cache)', source)
        self.assertNotIn(".nuget/packages", source)

    def test_ana_solution_packed_consumer_samplelarini_icermez(self):
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


class TraconBagimlilikKapanisiTestleri(unittest.TestCase):
    """`release-dryrun` restore etmez; sözleşme paketinin grafiği paketlerden okunur (2026-09-24)."""

    VERSION = "1.0.0-preview.3"

    def _paket(self, directory: pathlib.Path, package_id: str, *dependencies: str) -> None:
        import zipfile
        deps = "".join(
            f'<dependency id="{dependency}" version="[{self.VERSION}]" exclude="Build,Analyzers" />'
            for dependency in dependencies)
        nuspec = (f'<package><metadata><id>{package_id}</id><dependencies>'
                  f'<group targetFramework="net8.0">{deps}<dependency id="xunit.v3.assert" version="3.2.2" /></group>'
                  f'</dependencies></metadata></package>')
        with zipfile.ZipFile(directory / f"{package_id}.{self.VERSION}.nupkg", "w") as archive:
            archive.writestr(f"{package_id}.nuspec", nuspec)

    def test_gecisli_tracon_bagimliligi_bulunur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._paket(root, "Tracon.Testing.Contracts.Xunit", "Tracon.Abstractions")
            self._paket(root, "Tracon.Abstractions", "Tracon.Core")
            self._paket(root, "Tracon.Core")

            closure = release_extension_samples.tracon_dependency_closure(
                root, "Tracon.Testing.Contracts.Xunit", self.VERSION)

        self.assertEqual(closure, {"Tracon.Abstractions", "Tracon.Core"})

    def test_ucuncu_taraf_bagimlilik_kapanisa_girmez(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._paket(root, "Tracon.Testing.Contracts.Xunit", "Tracon.Abstractions")
            self._paket(root, "Tracon.Abstractions")

            closure = release_extension_samples.tracon_dependency_closure(
                root, "Tracon.Testing.Contracts.Xunit", self.VERSION)

        self.assertEqual(closure, {"Tracon.Abstractions"})

    def test_kaynak_projenin_obj_dizinini_okumaz(self):
        """Kusurun kendisi: `verify` sözleşme paketi için artifacts/obj'e bakıyordu."""
        source = pathlib.Path(release_extension_samples.__file__).read_text(encoding="utf-8")
        self.assertNotIn('_project_assets_json(root, "Tracon.Testing.Contracts.Xunit")', source)

