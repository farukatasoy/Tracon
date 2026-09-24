#!/usr/bin/env python3
"""Tests for the breaking-change gate of the release rehearsal (Faz 187).

Pure logic only: a temp git repository, temp files and a fake restore. The
MSBuild wiring and the real `dotnet pack` are covered by
`tests/Tracon.Package.Tests/PackageBaselineWiringTests.cs`; the ordering
inside `kapi.py yayin` by `kapi_test.py` (`YayinTestleri`).
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
TESTDATA = ROOT / "scripts" / "testdata" / "breaking-changes"
spec = importlib.util.spec_from_file_location("breaking_changes", ROOT / "scripts" / "breaking_changes.py")
bc = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = bc
spec.loader.exec_module(bc)


def _git(repo: pathlib.Path, *args: str) -> str:
    environment = {
        **os.environ,
        "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@example.invalid",
        "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@example.invalid",
        "GIT_CONFIG_GLOBAL": os.devnull, "GIT_CONFIG_NOSYSTEM": "1",
    }
    return subprocess.run(["git", *args], cwd=repo, env=environment, check=True, capture_output=True, text=True).stdout


def _commit(repo: pathlib.Path, message: str) -> None:
    _git(repo, "commit", "--allow-empty", "-q", "-m", message)


def _repo(directory: str) -> pathlib.Path:
    repo = pathlib.Path(directory) / "repo"
    repo.mkdir()
    _git(repo, "init", "-q", "-b", "main")
    return repo


def _write_csproj(root: pathlib.Path, project_id: str, body: str = "") -> pathlib.Path:
    path = root / "src" / project_id / f"{project_id}.csproj"
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(f'<Project Sdk="Microsoft.NET.Sdk">\n{body}\n</Project>\n', encoding="utf-8")
    return path


def _report(directory: pathlib.Path, package: str, *records: str) -> pathlib.Path:
    directory.mkdir(parents=True, exist_ok=True)
    path = directory / f"{package}.xml"
    path.write_text(
        '﻿<?xml version="1.0" encoding="utf-8"?>\n'
        '<Suppressions xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        'xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n' + "\n".join(records) + "\n</Suppressions>\n",
        encoding="utf-8")
    return path


def _baseline_record(diagnostic: str, target: str, tfm: str = "net10.0") -> str:
    return (
        f"  <Suppression>\n    <DiagnosticId>{diagnostic}</DiagnosticId>\n    <Target>{target}</Target>\n"
        f"    <Left>lib/{tfm}/X.dll</Left>\n    <Right>lib/{tfm}/X.dll</Right>\n"
        f"    <IsBaselineSuppression>true</IsBaselineSuppression>\n  </Suppression>")


def _framework_record(tfm: str) -> str:
    # Measured 2026-09-24 (187.0 step 4): PKV006 carries the framework as its
    # Target and NO Left/Right/IsBaselineSuppression element.
    return f"  <Suppression>\n    <DiagnosticId>PKV006</DiagnosticId>\n    <Target>{tfm}</Target>\n  </Suppression>"


def _quiet(call):
    output = io.StringIO()
    with contextlib.redirect_stdout(output):
        result = call()
    return result, output.getvalue()


class TabanSecimiTestleri(unittest.TestCase):
    def test_etiketsiz_headde_taban_son_etikettir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.2")
            _commit(repo, "three")

            self.assertEqual(bc.resolve_baseline(repo), "1.0.0-preview.2")

    def test_etiketli_headde_taban_bir_onceki_etikettir(self):
        """A tag run compared with its own tag would be a silent 'no change'."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.2")

            self.assertEqual(bc.resolve_baseline(repo), "1.0.0-preview.1")

    def test_v_olmayan_etiket_taban_sayilmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "site-2026")

            self.assertEqual(bc.resolve_baseline(repo), "1.0.0-preview.1")

    def test_etiket_yoksa_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(repo)

        self.assertIn("etiketi yok", str(raised.exception))

    def test_ilk_etiketli_headde_onceki_etiket_yoksa_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.1")

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(repo)

        self.assertIn("üst commit", str(raised.exception))

    def test_git_deposu_degilse_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(pathlib.Path(directory))

        self.assertIn("git deposu değil", str(raised.exception))

    def test_sig_klon_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            clone = pathlib.Path(directory) / "clone"
            subprocess.run(
                ["git", "clone", "-q", "--depth", "1", repo.as_uri(), str(clone)],
                check=True, capture_output=True)

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(clone)

        self.assertIn("sığ klon", str(raised.exception))

    def test_surum_tabana_esitse_kirmizi(self):
        self.assertFalse(bc.version_is_above_baseline("1.0.0-preview.2", "1.0.0-preview.2"))
        self.assertFalse(bc.version_is_above_baseline("1.0.0-preview.1.9", "1.0.0-preview.2"))

    def test_minver_yuksekligi_tabanin_ustundedir(self):
        self.assertTrue(bc.version_is_above_baseline("1.0.0-preview.2.46", "1.0.0-preview.2"))
        self.assertTrue(bc.version_is_above_baseline("1.0.0-preview.10", "1.0.0-preview.9"))
        self.assertTrue(bc.version_is_above_baseline("1.0.0", "1.0.0-preview.9"))
        self.assertTrue(bc.version_is_above_baseline("1.1.0-preview.0.3", "1.0.0"))


class IzoleRestoreTestleri(unittest.TestCase):
    def test_restore_izole_cache_ve_yalniz_nugetorg_kullanir(self):
        calls = []

        def fake_run(command, **kwargs):
            calls.append((command, kwargs))
            return subprocess.CompletedProcess(command, 0, "", "")

        with tempfile.TemporaryDirectory() as directory:
            work = pathlib.Path(directory)
            cache = bc.restore_baselines(["Tracon.Core", "Tracon.Voice"], "1.0.0-preview.2", work, run=fake_run)

            config = (work / "NuGet.config").read_text(encoding="utf-8")
            project = (work / "baseline-restore" / "baseline-restore.csproj").read_text(encoding="utf-8")

            self.assertEqual(cache, work / "packages")
            self.assertEqual(len(calls), 1)
            command, kwargs = calls[0]
            self.assertEqual(kwargs["env"]["NUGET_PACKAGES"], str(cache))
            self.assertIn("--packages", command)
            self.assertEqual(command[command.index("--packages") + 1], str(cache))
            self.assertEqual(command[command.index("--configfile") + 1], str(work / "NuGet.config"))
            self.assertIn("<clear />", config)
            self.assertEqual(config.count("<add "), 1)
            self.assertIn(bc.NUGET_ORG, config)
            self.assertIn('<PackageDownload Include="Tracon.Core" Version="[1.0.0-preview.2]" />', project)
            self.assertIn('<PackageDownload Include="Tracon.Voice" Version="[1.0.0-preview.2]" />', project)
            self.assertIn("<ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>", project)
            self.assertIn("<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>", project)

    def test_restore_hatasi_paketi_adiyla_bildirir(self):
        def fake_run(command, **kwargs):
            return subprocess.CompletedProcess(
                command, 1, "error NU1101: Unable to find package Tracon.Voice. No packages exist with this id", "")

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(bc.RestoreError) as raised:
                bc.restore_baselines(["Tracon.Core", "Tracon.Voice"], "1.0.0-preview.3", pathlib.Path(directory), run=fake_run)

        message = str(raised.exception)
        self.assertIn("Tracon.Voice", message)
        self.assertIn("v1.0.0-preview.3", message)
        self.assertIn("yayını tamamlayın", message)

    def _cache_entry(self, cache: pathlib.Path, project_id: str, version: str, source: str) -> None:
        directory = cache / project_id.lower() / version
        directory.mkdir(parents=True)
        (directory / f"{project_id.lower()}.{version}.nupkg").write_bytes(b"")
        (directory / ".nupkg.metadata").write_text(json.dumps({"version": 2, "source": source}), encoding="utf-8")

    def test_kaynak_nugetorg_ise_gecer(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = pathlib.Path(directory)
            self._cache_entry(cache, "Tracon.Core", "1.0.0-preview.2", bc.NUGET_ORG)

            self.assertEqual(bc.baseline_source_violations(["Tracon.Core"], "1.0.0-preview.2", cache), [])

    def test_kaynak_nugetorg_degilse_kirmizi(self):
        """The trap the isolated cache exists for: ReleaseArtifactFixture packs
        a local `1.0.0-preview.1` and the developer cache records that feed."""
        with tempfile.TemporaryDirectory() as directory:
            cache = pathlib.Path(directory)
            self._cache_entry(cache, "Tracon.Core", "1.0.0-preview.1", "/Users/x/Tracon/artifacts/package/release")

            violations = bc.baseline_source_violations(["Tracon.Core", "Tracon.Voice"], "1.0.0-preview.1", cache)

        self.assertEqual(len(violations), 2)
        self.assertIn("artifacts/package/release", violations[0])
        self.assertIn("Tracon.Voice", violations[1])

    def test_pack_ozellikleri_uc_p_tasir(self):
        properties = bc.pack_properties(pathlib.Path("/c"), "1.0.0-preview.2", pathlib.Path("/r"))

        self.assertEqual(properties, [
            "-p:TraconPackageBaselineRoot=/c",
            "-p:TraconPackageBaselineVersion=1.0.0-preview.2",
            "-p:TraconApiCompatReportDir=/r",
        ])


class IlkYayinBayragiTestleri(unittest.TestCase):
    def test_bayrakli_paket_restore_listesinde_yok(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_csproj(root, "Tracon.Core")
            _write_csproj(root, "Tracon.New", "<PropertyGroup>" + bc.FIRST_RELEASE_FLAG + "</PropertyGroup>")

            self.assertEqual(bc.baseline_package_ids(root, ["Tracon.New", "Tracon.Core"]), ["Tracon.Core"])

    def test_tabanda_var_olan_pakette_bayrak_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _write_csproj(repo, "Tracon.Voice")
            _write_csproj(repo, "Tracon.Generators", "<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>")
            _git(repo, "add", "-A")
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.2")
            flag = "<PropertyGroup>" + bc.FIRST_RELEASE_FLAG + "</PropertyGroup>"
            _write_csproj(repo, "Tracon.Voice", flag)
            _write_csproj(repo, "Tracon.Generators", flag)
            _write_csproj(repo, "Tracon.New", flag)

            stale = bc.stale_first_release_flags(repo, "v1.0.0-preview.2")

        # Tracon.New did not exist in the tag; Tracon.Generators was not packable there.
        self.assertEqual(stale, ["Tracon.Voice"])

    def test_depoda_bayrak_bugun_yok(self):
        """21 csproj all existed in v1.0.0-preview.2 (measured 2026-09-23)."""
        flagged = [path.name for path in (ROOT / "src").glob("*/*.csproj") if bc.FIRST_RELEASE_FLAG in path.read_text(encoding="utf-8")]

        self.assertEqual(flagged, [])


class DogrulamaKanitiTestleri(unittest.TestCase):
    def _semaphore(self, root: pathlib.Path, project_id: str, configuration: str, mtime: float) -> pathlib.Path:
        path = root / "artifacts" / "obj" / project_id / configuration / bc.SEMAPHORE_NAME
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"")
        os.utime(path, (mtime, mtime))
        return path

    def test_semaphore_yolu_harf_duyarsiz_bulunur(self):
        for configuration in ("Release", "release"):
            with self.subTest(configuration=configuration), tempfile.TemporaryDirectory() as directory:
                root = pathlib.Path(directory)
                expected = self._semaphore(root, "Tracon.Core", configuration, time.time())
                self._semaphore(root, "Tracon.Core", "debug_net10.0", time.time())

                self.assertEqual(bc.semaphore_path(root, "Tracon.Core"), expected)

    def test_semaphore_bulunamazsa_aranan_yol_mesajda(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)

            problems = bc.validation_not_run(root, ["Tracon.Core"], time.time())

        self.assertEqual(len(problems), 1)
        self.assertIn(str(root / "artifacts" / "obj" / "Tracon.Core"), problems[0])
        self.assertIn(bc.SEMAPHORE_NAME, problems[0])

    def test_eski_semaphore_kirmizi(self):
        """RunPackageValidation is incremental: an unchanged package skips it
        and leaves the old semaphore - the silent pass this proof exists for."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            pack_started = time.time()
            self._semaphore(root, "Tracon.Core", "Release", pack_started - 3600)
            self._semaphore(root, "Tracon.Voice", "Release", pack_started + 5)

            problems = bc.validation_not_run(root, ["Tracon.Core", "Tracon.Voice"], pack_started)

        self.assertEqual(len(problems), 1)
        self.assertIn("Tracon.Core: paket doğrulaması bu koşumda koşmadı", problems[0])

    def test_saniye_cozunurlukte_ayni_saniye_gecer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            pack_started = 1_000_000.7
            self._semaphore(root, "Tracon.Core", "Release", 1_000_000.0)

            self.assertEqual(bc.validation_not_run(root, ["Tracon.Core"], pack_started), [])


class KiriciListeTestleri(unittest.TestCase):
    def test_docid_indirgeme(self):
        cases = {
            "T:Tracon.AgentCallGraph": "AgentCallGraph",
            "T:Tracon.Outer.Inner": "Inner",
            "T:Tracon.Cache`2": "Cache",
            "M:Tracon.TenantProviderCredentialResolver.#ctor(Microsoft.Extensions.Configuration.IConfiguration)":
                "TenantProviderCredentialResolver",
            "M:Tracon.TenantProviderCredentialResolver.ValidatePrefix(System.String)": "TenantProviderCredentialResolver",
            "M:Tracon.Store`1.Save``1(``0,System.Threading.CancellationToken)": "Store",
            "P:Tracon.Options.Timeout": "Options",
            "P:Tracon.Map.Item(System.String)": "Map",
            "F:Tracon.Limits.Max": "Limits",
            "E:Tracon.Bus.Raised": "Bus",
            "M:Tracon.Outer.Inner.Run": "Inner",
        }
        for doc_id, expected in cases.items():
            with self.subTest(doc_id=doc_id):
                self.assertEqual(bc.type_of_doc_id(doc_id), expected)

    def test_docid_olmayan_hedef_indirgenmez(self):
        for target in ("N:Tracon", "net8.0", "lib/net8.0/Tracon.Core.dll", "M:Method"):
            with self.subTest(target=target):
                self.assertIsNone(bc.type_of_doc_id(target))

    def test_cp0008_docid_ile_indirgenir(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", _baseline_record("CP0008", "T:Tracon.RunStatus"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(changes.errors, [])
        self.assertEqual(changes.types, {"Tracon.Core": {"RunStatus"}})

    def test_tfm_tekrari_tek_kayit(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", *[
                _baseline_record("CP0001", "T:Tracon.AgentCallGraph", tfm) for tfm in ("net8.0", "net9.0", "net10.0")])

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(changes.type_count(), 1)

    def test_pkv006_paket_ve_tfm_ister(self):
        """The measured report of Tracon.Voice packed for net10.0 only (187.0 step 4)."""
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            (reports / "Tracon.Voice.xml").write_bytes((TESTDATA / "voice-net10-only.xml").read_bytes())

            changes = bc.read_breaking_changes(reports)
            unnamed_deprecation = bc.unnamed_changes(changes, (
                "- The `net8.0` and `net9.0` targets. Every package drops them.\n"
                "- `Tracon.Voice`: `VoiceProviderNames`.\n"))
            named = bc.unnamed_changes(changes, (
                "- `Tracon.Voice`: `VoiceProviderNames`.\n"
                "- `Tracon.Core`, `Tracon.Voice`: the `net8.0` and `net9.0` targets\n"
                "  are removed.\n"))

        self.assertEqual(changes.errors, [])
        self.assertEqual(changes.dropped_frameworks, {"Tracon.Voice": {"net8.0", "net9.0"}})
        self.assertEqual(changes.types, {"Tracon.Voice": {"VoiceProviderNames"}})
        self.assertEqual(unnamed_deprecation, ["Tracon.Voice: düşen TFM net8.0, net9.0"])
        self.assertEqual(named, [])

    def test_taban_disi_kayit_kirmizi(self):
        """ApiCompatGenerateSuppressionFile turns a strict-mode error into a
        record too; without this rule a TFM-specific member would pass."""
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Voice", (
                "  <Suppression>\n    <DiagnosticId>CP0002</DiagnosticId>\n"
                "    <Target>M:Tracon.VoiceOptions.Probe</Target>\n"
                "    <Left>lib/net10.0/Tracon.Voice.dll</Left>\n    <Right>lib/net8.0/Tracon.Voice.dll</Right>\n"
                "  </Suppression>"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("strict", changes.errors[0])
        self.assertIn("M:Tracon.VoiceOptions.Probe", changes.errors[0])

    def test_pkv007_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", (
                "  <Suppression>\n    <DiagnosticId>PKV007</DiagnosticId>\n    <Target>net10.0-linux-x64</Target>\n"
                "    <IsBaselineSuppression>true</IsBaselineSuppression>\n  </Suppression>"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("tanınmayan kayıt PKV007", changes.errors[0])

    def test_docid_olmayan_hedef_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", _baseline_record("CP0003", "lib/net10.0/Tracon.Core.dll"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("tanınmayan kayıt CP0003", changes.errors[0])

    def test_bozuk_xml_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            (reports / "Tracon.Core.xml").write_text("<Suppressions><Suppression>", encoding="utf-8")

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("Tracon.Core: rapor okunamadı", changes.errors[0])

    def test_gercek_rapor_fikstur(self):
        """The full Tracon.Core report against v1.0.0-preview.2 (187.0 step 7):
        216 records - 207 CP0001, 9 CP0002 - over three frameworks."""
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            (reports / "Tracon.Core.xml").write_bytes((TESTDATA / "core-preview2.xml").read_bytes())

            changes = bc.read_breaking_changes(reports)

        core = changes.types["Tracon.Core"]
        self.assertEqual(changes.errors, [])
        self.assertEqual(changes.dropped_frameworks, {})
        # 69 types went internal (K-850); two more types lost a member:
        # TenantProviderCredentialResolver (ctor, ValidatePrefix) and
        # SandboxedSkillScriptRunner.RunStoredScriptAsync (Faz 186).
        self.assertEqual(len(core), 71)
        self.assertIn("AgentCallGraph", core)
        self.assertIn("WorkflowDefinitionValidator", core)
        self.assertIn("TenantProviderCredentialResolver", core)
        self.assertIn("SandboxedSkillScriptRunner", core)

    def test_fikstur_mutlak_yol_tasimaz(self):
        for fixture in TESTDATA.glob("*.xml"):
            with self.subTest(fixture=fixture.name):
                text = fixture.read_text(encoding="utf-8-sig")
                self.assertNotIn("/Users/", text)
                self.assertNotIn("/private/", text)


class SurumNotuEslesmeTestleri(unittest.TestCase):
    def _changes(self, **types: set[str]) -> object:
        changes = bc.BreakingChanges()
        for package, names in types.items():
            changes.types[package.replace("_", ".")] = set(names)
        return changes

    def test_eksik_tip_adiyla_raporlanir(self):
        changes = self._changes(Tracon_Abstractions={"SchemaReadyGate", "JobPayload"}, Tracon_Core={"TextChunker"})

        missing = bc.unnamed_changes(changes, "- `Tracon.Abstractions`: `JobPayload`.\n- `Tracon.Core`: `TextChunker`.\n")

        self.assertEqual(missing, ["Tracon.Abstractions: SchemaReadyGate"])

    def test_joker_kabul_edilmez(self):
        changes = self._changes(Tracon_OpenAI={"OpenAIModelCatalog", "OpenAIChatClientFactory"})

        missing = bc.unnamed_changes(changes, "- `Tracon.OpenAI`: the `*ChatClientFactory` and `*ModelCatalog` types.\n")

        self.assertEqual(missing, ["Tracon.OpenAI: OpenAIChatClientFactory, OpenAIModelCatalog"])

    def test_uzun_ad_kisa_adi_karsilamaz(self):
        changes = self._changes(Tracon_OpenAI={"OpenAIChatClientFactory"})

        missing = bc.unnamed_changes(changes, "`OpenAIChatClientFactoryOptions` is unchanged; OpenAIChatClientFactory is internal.")

        # Neither the longer span nor the bare (span-less) word names the type.
        self.assertEqual(missing, ["Tracon.OpenAI: OpenAIChatClientFactory"])

    def test_tip_uye_ve_jenerik_span_kabul_edilir(self):
        changes = self._changes(Tracon_Core={"A", "B", "C", "D", "E"})
        notes = "`A` · `B<T>` · `Tracon.C` · `Outer.D` · `E.Run(string, int)`"

        self.assertEqual(bc.unnamed_changes(changes, notes), [])

    def test_satir_sonunda_bolunen_span_kabul_edilir(self):
        changes = self._changes(Tracon_Core={"SandboxedSkillScriptRunner"})
        notes = "- `SandboxedSkillScriptRunner.RunStoredScriptAsync`\n  takes the skill."

        self.assertEqual(bc.unnamed_changes(changes, notes), [])

    def test_citli_kod_blogu_span_eslesmesini_kaydirmaz(self):
        # Faz 189: iki çitli blok taşıyan not, doğru yazılmış iki adı kaçırıyordu.
        changes = self._changes(Tracon_Core={"A", "B", "C"})
        blok = "  ```csharp\n  new A(x, y: 1);\n  ```\n"
        notes = f"- `A` changed:\n\n{blok}\n- `B` changed:\n\n{blok}\n- `C` changed.\n"

        self.assertEqual(bc.unnamed_changes(changes, notes), [])

    def test_yalniz_citli_blokta_gecen_ad_kabul_edilmez(self):
        changes = self._changes(Tracon_Core={"Hidden"})
        notes = "- A type changed:\n\n  ```csharp\n  `Hidden` x;\n  ```\n"

        self.assertEqual(bc.unnamed_changes(changes, notes), ["Tracon.Core: Hidden"])

    def test_paket_kimligi_tam_esitlik_ister(self):
        changes = bc.BreakingChanges()
        changes.dropped_frameworks["Tracon"] = {"net8.0"}

        missing = bc.unnamed_changes(changes, "- `Tracon.Core`: the `net8.0` target is removed.\n")

        self.assertEqual(missing, ["Tracon: düşen TFM net8.0"])

    def test_tfm_ve_paket_ayni_maddede_olmali(self):
        changes = bc.BreakingChanges()
        changes.dropped_frameworks["Tracon.Core"] = {"net8.0"}

        split = bc.unnamed_changes(changes, "- `Tracon.Core`: something.\n\n- The `net8.0` target is removed.\n")
        together = bc.unnamed_changes(changes, "- `Tracon.Core`: the\n  `net8.0` target is removed.\n")

        self.assertEqual(split, ["Tracon.Core: düşen TFM net8.0"])
        self.assertEqual(together, [])


class KapiCagrisiTestleri(unittest.TestCase):
    """`check`: the report dir + CHANGELOG.md -> exit code, as kapi.py calls it."""

    CHANGELOG = (
        "# Changelog\n\n## [Unreleased]\n\n{unreleased}\n"
        "## [1.0.0-preview.3] - 2026-10-01\n\n{section}\n"
        "## [1.0.0-preview.2] - 2026-09-20\n\n### Added\n- Things.\n")

    def _run(self, *, reports: dict[str, list[str]], unreleased: str, section: str, version: str,
             baseline: str = "1.0.0-preview.2", changelog_text: str | None = None):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for package, records in reports.items():
                _report(root / "reports", package, *records)
            (root / "reports").mkdir(exist_ok=True)
            changelog = root / "CHANGELOG.md"
            changelog.write_text(
                changelog_text if changelog_text is not None
                else self.CHANGELOG.format(unreleased=unreleased, section=section), encoding="utf-8")
            result_path = root / "out" / "breaking-changes.json"
            code, output = _quiet(lambda: bc.check(
                report_dir=root / "reports", baseline=baseline, version=version,
                changelog=changelog, result_path=result_path))
            result = json.loads(result_path.read_text(encoding="utf-8")) if result_path.exists() else None
        return code, output, result

    def test_etiket_kosumunda_surum_bolumu_okunur(self):
        """K-825: at tag time [Unreleased] was renamed; the version section is read."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="", section="### Removed\n- `TextChunker`.\n", version="1.0.0-preview.3")

        self.assertEqual(code, 0, output)
        self.assertIn("hepsi '1.0.0-preview.3' notunda", output)
        self.assertEqual(result["notesHeading"], "1.0.0-preview.3")
        self.assertEqual(result["types"], {"Tracon.Core": ["TextChunker"]})

    def test_etiketsiz_kosumda_unreleased_okunur(self):
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="### Removed\n- `TextChunker`.\n", section="- Other.\n", version="1.0.0-preview.2.47")

        self.assertEqual(code, 0, output)
        self.assertEqual(result["notesHeading"], "Unreleased")
        self.assertEqual(result["baseline"], "1.0.0-preview.2")

    def test_kesilmis_etiketsiz_surum(self):
        """Açık Soru 1 = C: the cut commit (section renamed, empty
        [Unreleased] above) pushed without its tag. MinVer still resolves
        `1.0.0-preview.2.N`; the notes are not found and the message says to
        push the cut and the tag together."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="", section="### Removed\n- `TextChunker`.\n", version="1.0.0-preview.2.48")

        self.assertEqual(code, 1)
        self.assertIn("git push --atomic origin main v1.0.0-preview.3", output)
        self.assertIn("Tracon.Core: TextChunker", output)
        self.assertIsNone(result)

    def test_bos_liste_bos_notla_gecer(self):
        code, output, result = self._run(reports={}, unreleased="", section="", version="1.0.0-preview.2.47",
                                         changelog_text="# Changelog\n")

        self.assertEqual(code, 0, output)
        self.assertIn("0 tip, 0 TFM düşüşü, 0 paket", output)
        self.assertEqual(result["types"], {})

    def test_strict_kayit_notla_da_kirmizi(self):
        code, output, _ = self._run(
            reports={"Tracon.Core": ["  <Suppression>\n    <DiagnosticId>CP0001</DiagnosticId>\n"
                                     "    <Target>T:Tracon.X</Target>\n  </Suppression>"]},
            unreleased="- `X`.\n", section="", version="1.0.0-preview.2.47")

        self.assertEqual(code, 1)
        self.assertIn("strict", output)

    def test_cli_rapor_dizinini_denetler(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "reports").mkdir()
            (root / "reports" / "Tracon.Voice.xml").write_bytes((TESTDATA / "voice-net10-only.xml").read_bytes())
            changelog = root / "CHANGELOG.md"
            changelog.write_text("## [Unreleased]\n\n- `Tracon.Voice`: `VoiceProviderNames`.\n", encoding="utf-8")
            argv = ["--rapor-dizini", str(root / "reports"), "--taban", "1.0.0-preview.2", "--changelog", str(changelog)]

            red, red_output = _quiet(lambda: bc.main(argv))
            changelog.write_text(
                "## [Unreleased]\n\n- `Tracon.Voice`: `VoiceProviderNames`.\n"
                "- `Tracon.Voice`: the `net8.0` and `net9.0` targets.\n", encoding="utf-8")
            green, _ = _quiet(lambda: bc.main(argv))

        self.assertEqual(red, 1)
        self.assertIn("Tracon.Voice: düşen TFM net8.0, net9.0", red_output)
        self.assertEqual(green, 0)


class DepoKorumaTestleri(unittest.TestCase):
    def test_depoda_compatibility_suppressions_dosyasi_yok(self):
        """A report written into the source tree is read back as a suppression
        input by every later pack and hides the break for good (187.4 trap 1)."""
        found = sorted(str(path.relative_to(ROOT)) for path in (ROOT / "src").rglob("CompatibilitySuppressions.xml"))

        self.assertEqual(found, [])


if __name__ == "__main__":
    unittest.main()
