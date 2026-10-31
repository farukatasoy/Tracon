#!/usr/bin/env python3
"""Tests for the breaking-change gate of the release rehearsal.

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
    # Measured 2026-09-24: PKV006 carries the framework as its
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
        cache, report = pathlib.Path("/c"), pathlib.Path("/r")
        properties = bc.pack_properties(cache, "1.0.0-preview.2", report)

        # Yol `dotnet`'e işletim sisteminin kendi biçimiyle gider (Windows'ta
        # `\c`); beklenen değer de aynı biçimden kurulur (2026-09-24, CI windows).
        self.assertEqual(properties, [
            f"-p:TraconPackageBaselineRoot={cache}",
            "-p:TraconPackageBaselineVersion=1.0.0-preview.2",
            f"-p:TraconApiCompatReportDir={report}",
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

    # Packages that have no published baseline yet. The flag is required for them (yeni-paket.md): nuget.org has nothing
    # to restore for a rehearsal. `kapi.py yayin` calls the flag stale once the package is in the latest v* tag, and
    # then the entry goes from here too. Tracon.AGUI left with 1.0.0-preview.6.
    NEW_SINCE_LAST_RELEASE: list[str] = []

    def test_depoda_bayrak_yalniz_yayinlanmamis_pakette_var(self):
        """Every other csproj existed in v1.0.0-preview.2 (measured 2026-09-23) and must not carry the flag."""
        flagged = sorted(
            path.name for path in (ROOT / "src").glob("*/*.csproj") if bc.FIRST_RELEASE_FLAG in path.read_text(encoding="utf-8"))

        self.assertEqual(flagged, self.NEW_SINCE_LAST_RELEASE)


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
        """The measured report of Tracon.Voice packed for net10.0 only."""
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
        """The full Tracon.Core report against v1.0.0-preview.2:
        216 records - 207 CP0001, 9 CP0002 - over three frameworks."""
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            (reports / "Tracon.Core.xml").write_bytes((TESTDATA / "core-preview2.xml").read_bytes())

            changes = bc.read_breaking_changes(reports)

        core = changes.types["Tracon.Core"]
        self.assertEqual(changes.errors, [])
        self.assertEqual(changes.dropped_frameworks, {})
        # 69 types went internal; two more types lost a member:
        # TenantProviderCredentialResolver (ctor, ValidatePrefix) and
        # SandboxedSkillScriptRunner.RunStoredScriptAsync.
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
        # Iki çitli blok taşıyan not, doğru yazılmış iki adı kaçırıyordu.
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
        """At tag time [Unreleased] was renamed; the version section is read."""
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
        """The cut commit (section renamed, empty
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


class HatTestleri(unittest.TestCase):
    """One function decides the line; the non-.NET gate reuses it."""

    def test_surum_sirasi_preview_rc_ga(self):
        ordered = ["1.0.0-preview.5", "1.0.0-preview.12", "1.0.0-rc.1", "1.0.0-rc.2", "1.0.0", "1.0.1", "1.1.0", "2.0.0"]

        for lower, higher in zip(ordered, ordered[1:]):
            self.assertEqual(bc.compare_versions(lower, higher), -1, f"{lower} < {higher}")

    def test_preview_hatti(self):
        for version in ("1.0.0-preview.6", "1.0.0-preview.5.47", "1.0.0-rc.1"):
            line = bc.release_line("1.0.0-preview.5", version)

            self.assertEqual(line.kind, bc.LINE_PREVIEW, version)
            self.assertFalse(line.frozen, version)

    def test_preview_tabani_rc1e_giderken_donmus_sayilmaz(self):
        """`preview.5` -> `rc.1` is the step that FREEZES; judging it by the frozen rule would reject the freeze."""
        line = bc.release_line("1.0.0-preview.5", "1.0.0-rc.1")

        self.assertEqual(line.describe(), "Hat: preview (kırılma notta adıyla)")

    def test_rc1_tabani_donmustur(self):
        for version in ("1.0.0-rc.2", "1.0.0", "1.0.1", "1.1.0", "1.9.0-preview.0.4"):
            line = bc.release_line("1.0.0-rc.1", version)

            self.assertEqual(line.kind, bc.LINE_FROZEN, version)
            self.assertEqual(line.major, 1, version)

    def test_yama_ayni_major_ve_minor_demektir(self):
        self.assertTrue(bc.release_line("1.0.0-rc.1", "1.0.0-rc.2").patch)
        self.assertTrue(bc.release_line("1.0.0-rc.1", "1.0.0").patch)
        self.assertTrue(bc.release_line("1.0.0", "1.0.1").patch)
        self.assertTrue(bc.release_line("1.0.0", "1.0.0-preview.0.4").patch)
        self.assertFalse(bc.release_line("1.0.0", "1.1.0").patch)
        self.assertFalse(bc.release_line("1.0.0", "1.1.0-preview.0.5").patch)

    def test_major_artisi_donmus_degildir(self):
        line = bc.release_line("1.4.0", "2.0.0")

        self.assertEqual(line.kind, bc.LINE_MAJOR)
        self.assertFalse(line.frozen)
        self.assertEqual(line.describe(), "Hat: major artışı (kırılma notta adıyla)")

    def test_hat_satirlari(self):
        self.assertEqual(bc.release_line("1.0.0", "1.1.0").describe(), "Hat: donmuş 1.x (kırılma reddedilir)")
        self.assertEqual(
            bc.release_line("1.0.0", "1.0.1").describe(),
            "Hat: donmuş 1.x yama (kırılma ve eklenen public üye reddedilir)")
        self.assertEqual(bc.release_line("2.3.0", "2.4.0").describe(), "Hat: donmuş 2.x (kırılma reddedilir)")

    def test_sürümsüz_preview_tabani_calisir_donmus_taban_calismaz(self):
        self.assertEqual(bc.release_line("1.0.0-preview.5", None).kind, bc.LINE_PREVIEW)
        with self.assertRaises(ValueError):
            bc.release_line("1.0.0-rc.1", None)

    def test_sürüm_degil_hata(self):
        with self.assertRaises(ValueError):
            bc.release_line("1.0.0", "next")


class DonmusHatKapiTestleri(unittest.TestCase):
    """The same ApiCompat report is red in a frozen line and green in a preview or a major."""

    BREAK = _baseline_record("CP0006", "M:Tracon.IThing.Added")

    def _run(self, *, records: list[str], baseline: str, version: str, notes: str, repo=None):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _report(root / "reports", "Tracon.Abstractions", *records)
            changelog = root / "CHANGELOG.md"
            changelog.write_text(f"# Changelog\n\n## [Unreleased]\n\n{notes}\n", encoding="utf-8")
            return _quiet(lambda: bc.check(
                report_dir=root / "reports", baseline=baseline, version="Unreleased" if version is None else version,
                changelog=changelog, result_path=None, repo=repo))

    def test_donmus_hatta_notta_adi_gecen_kirilma_kirmizi(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0", version="1.1.0", notes="- `IThing` gained a member.")

        self.assertEqual(code, 1)
        self.assertIn("Hat: donmuş 1.x (kırılma reddedilir)", output)
        self.assertIn("sürüm notu bunu kurtarmaz", output)
        self.assertIn("Tracon.Abstractions: IThing", output)

    def test_ayni_rapor_major_artisinda_yesil(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0", version="2.0.0", notes="- `IThing` gained a member.")

        self.assertEqual(code, 0, output)
        self.assertIn("Hat: major artışı", output)

    def test_major_artisinda_notta_olmayan_kirilma_kirmizi(self):
        code, output = self._run(records=[self.BREAK], baseline="1.4.0", version="2.0.0", notes="- Other.")

        self.assertEqual(code, 1)
        self.assertIn("Tracon.Abstractions: IThing", output)

    def test_rc1den_rc2ye_kirilma_kirmizi(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0-rc.1", version="1.0.0-rc.2", notes="- `IThing` gained a member.")

        self.assertEqual(code, 1)
        self.assertIn("Hat: donmuş 1.x yama", output)

    def test_donmus_hatta_dusen_tfm_kirmizi(self):
        code, output = self._run(
            records=[_framework_record("net8.0")], baseline="1.0.0", version="1.1.0",
            notes="- `Tracon.Abstractions`: the `net8.0` target is removed.")

        self.assertEqual(code, 1)
        self.assertIn("düşen TFM net8.0", output)

    def test_donmus_hatta_bos_liste_yesil(self):
        code, output = self._run(records=[], baseline="1.0.0", version="1.1.0", notes="")

        self.assertEqual(code, 0, output)
        self.assertIn("Hat: donmuş 1.x", output)
        self.assertIn("0 tip, 0 TFM düşüşü, 0 paket — not gerekmiyor", output)

    def test_preview_hatti_bugunku_davranisini_korur(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- `IThing` gained a member.")
        red, red_output = self._run(
            records=[self.BREAK], baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- Other.")

        self.assertEqual(code, 0, output)
        self.assertIn("Hat: preview (kırılma notta adıyla)", output)
        self.assertEqual(red, 1)
        self.assertIn("Tracon.Abstractions: IThing", red_output)

    def test_hat_her_kosumda_yazilir_kirmizi_rapor_dahil(self):
        code, output = self._run(
            records=["  <Suppression>\n    <DiagnosticId>CP0001</DiagnosticId>\n    <Target>T:Tracon.X</Target>\n  </Suppression>"],
            baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="")

        self.assertEqual(code, 1)
        self.assertTrue(output.startswith("Hat: preview"), output)

    def test_cli_donmus_hat_kirmizi_ayni_rapor_major_ile_yesil(self):
        """Manuel case 3."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _report(root / "reports", "Tracon.Abstractions", self.BREAK)
            changelog = root / "CHANGELOG.md"
            changelog.write_text("## [Unreleased]\n\n- `IThing` gained a member.\n", encoding="utf-8")
            argv = ["--rapor-dizini", str(root / "reports"), "--taban", "1.0.0", "--changelog", str(changelog)]

            minor, minor_output = _quiet(lambda: bc.main([*argv, "--surum", "1.1.0"]))
            major, major_output = _quiet(lambda: bc.main([*argv, "--surum", "2.0.0"]))
            unknown, unknown_output = _quiet(lambda: bc.main(argv))

        self.assertEqual(minor, 1)
        self.assertIn("Hat: donmuş 1.x", minor_output)
        self.assertEqual(major, 0, major_output)
        self.assertEqual(unknown, 1)
        self.assertIn("Hat belirlenemedi", unknown_output)


def _write(repo: pathlib.Path, relative: str, text: str) -> None:
    path = repo / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def _commit_all(repo: pathlib.Path, message: str) -> None:
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", message)


CONTRACT = "src/Tracon.Testing.Contracts.Xunit"
CORE_API = "src/Tracon.Abstractions"
SNAPSHOT = "docs/sozlesme/required-uyeler.txt"

CONTRACT_API_V1 = (
    "#nullable enable\n"
    "Tracon.Testing.Contracts.Storage.RunStoreContract\n"
    "Tracon.Testing.Contracts.Storage.RunStoreContract.Existing_test() -> System.Threading.Tasks.Task!\n"
    "Tracon.Testing.Contracts.Storage.RunStoreContract.RunStoreContract() -> void\n")


def _baseline_repo(
    directory: str, *, with_snapshot: str | None = "", contract_api: str | None = CONTRACT_API_V1, tag: str = "v1.0.0",
) -> pathlib.Path:
    """A repo whose baseline tag carries a PublicAPI surface and a `required` snapshot (None: no snapshot)."""
    repo = _repo(directory)
    _write(repo, f"{CORE_API}/PublicAPI.Shipped.txt", "#nullable enable\n")
    _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt", "#nullable enable\nTracon.Options\nTracon.Options.Name.get -> string!\n")
    if contract_api is not None:
        _write(repo, f"{CONTRACT}/PublicAPI.Shipped.txt", "#nullable enable\n")
        _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", contract_api)
    if with_snapshot is not None:
        _write(repo, SNAPSHOT, with_snapshot)
    _commit_all(repo, "baseline")
    _git(repo, "tag", tag)
    return repo


class YuzeyOkumaTestleri(unittest.TestCase):
    """The readers that stand where ApiCompat is blind."""

    def test_etkin_yuzey_shipped_ile_unshipped_birlesimidir(self):
        surface = bc.effective_api("#nullable enable\nA\nB\n", "#nullable enable\nC\n*REMOVED*B\n")

        self.assertEqual(surface, {"A", "C"})

    def test_ga_dolumu_yuzeyi_degistirmez(self):
        """Lines that only change file are not 'added'."""
        before = bc.effective_api("", "#nullable enable\nA\nB\n")
        after = bc.effective_api("#nullable enable\nA\nB\n", "#nullable enable\n")

        self.assertEqual(before, after)

    def test_contract_testi_bicimi(self):
        surface = [
            "N.RunStoreContract.Test_one() -> System.Threading.Tasks.Task!",
            "N.RunStoreContract.Test_two(int x) -> void",
            "N.RunStoreContract.RunStoreContract() -> void",
            "N.RunStoreContract.InitializeAsync() -> System.Threading.Tasks.ValueTask",
            "N.RunStoreContract.Source.get -> N.IRunStore!",
            "N.RunStoreContract.Source.init -> void",
            "abstract N.RunStoreContract.Seed() -> void",
            "static N.RunStoreContract.Helper() -> void",
            "virtual N.RunStoreContract.Hook() -> System.Threading.Tasks.ValueTask",
            "override N.TenantIsolationContract<TStore>.Overridden_test() -> System.Threading.Tasks.Task!",
            "N.TenantIsolationContract<TStore>.Generic_test() -> System.Threading.Tasks.Task!",
            "N.TenantIsolationContract<TStore>.TenantIsolationContract() -> void",
            "N.RunStoreContract.Value() -> int",
        ]

        self.assertEqual(
            bc.contract_tests(surface),
            {"RunStoreContract.Test_one", "RunStoreContract.Test_two",
             "TenantIsolationContract.Overridden_test", "TenantIsolationContract.Generic_test"})

    def test_gercek_contract_yuzeyi_696_testi_iki_yardimciyla_sayar(self):
        """Measured 2026-10-24: 696 `[Fact]`/`[Theory]` plus two `protected void` helpers (`Enter`, `UseAmbientTenant`)."""
        surface = bc.api_now(ROOT)[bc.CONTRACT_PACKAGE]
        tests = bc.contract_tests(surface)

        self.assertGreater(len(tests), 600)
        self.assertIn("AgentFileStoreContract.Enter", tests)

    def test_ilk_kayit_taban_etiketinde_anlik_goruntu_yoksa_not(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-preview.5")

            delta = bc.surface_delta(repo, "1.0.0-preview.5")

        self.assertEqual(delta.required_types, {})
        self.assertIn("ilk kayıt", delta.notes[0])

    def test_donmus_tabanda_anlik_goruntu_yoksa_surface_error(self):
        """A frozen baseline was cut after the file existed: a missing snapshot would let a `required` addition through."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-rc.1")

            with self.assertRaises(bc.SurfaceError) as raised:
                bc.surface_delta(repo, "1.0.0-rc.1")

        self.assertIn("donmuş bir tabanda anlık görüntü zorunludur", str(raised.exception))

    def test_snapshot_dan_dusen_satir_listelenir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\nTracon.Options.Other\n")
            _write(repo, SNAPSHOT, "Tracon.Options.Name\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.required_dropped, ["Tracon.Options.Other"])

    def test_yeni_contract_testi_eklenen_uye_sayilmaz_baska_satir_sayilir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", CONTRACT_API_V1 + (
                "Tracon.Testing.Contracts.Storage.RunStoreContract.New_test() -> System.Threading.Tasks.Task!\n"
                "Tracon.Testing.Contracts.Storage.RunStoreContract.Hook() -> System.Threading.Tasks.ValueTask\n"))

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.contract_tests, ["RunStoreContract.New_test"])
        self.assertEqual(
            delta.added_members["Tracon.Testing.Contracts.Xunit"],
            ["Tracon.Testing.Contracts.Storage.RunStoreContract.Hook() -> System.Threading.Tasks.ValueTask"])

    def test_eklenen_test_ve_uye_listelenir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", CONTRACT_API_V1 + (
                "Tracon.Testing.Contracts.Storage.RunStoreContract.New_test() -> System.Threading.Tasks.Task!\n"))
            _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt",
                   "#nullable enable\nTracon.Options\nTracon.Options.Name.get -> string!\nTracon.Options.Extra.get -> string!\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.contract_tests, ["RunStoreContract.New_test"])
        self.assertEqual(delta.added_members["Tracon.Abstractions"], ["Tracon.Options.Extra.get -> string!"])
        # A new contract test is listed (and must be named) but is not an added member: a patch may carry one.
        self.assertNotIn("Tracon.Testing.Contracts.Xunit", delta.added_members)

    def test_ga_dolumu_listeyi_bos_birakir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            _write(repo, f"{CONTRACT}/PublicAPI.Shipped.txt", CONTRACT_API_V1)
            _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", "#nullable enable\n")
            _write(repo, f"{CORE_API}/PublicAPI.Shipped.txt", "#nullable enable\nTracon.Options\nTracon.Options.Name.get -> string!\n")
            _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt", "#nullable enable\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.contract_tests, [])
        self.assertEqual(delta.added_members, {})

    def test_tabanda_publicapi_dosyasi_yoksa_liste_bos_hata_yok(self):
        """`TraconPackageFirstRelease` precedent: a package the tag does not know has no suite to add a test to."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, contract_api=None)
            _write(repo, f"{CONTRACT}/PublicAPI.Shipped.txt", "#nullable enable\n")
            _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", CONTRACT_API_V1)

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.contract_tests, [])
        self.assertIn("Tracon.Testing.Contracts.Xunit", delta.added_members)

    def test_taban_etiketi_yoksa_baseline_error(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)

            with self.assertRaises(bc.BaselineError) as raised:
                bc.surface_delta(repo, "9.9.9")

        self.assertIn("v9.9.9", str(raised.exception))

    def test_mevcut_tipe_eklenen_required_uye_kirilmadir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\n")
            _write(repo, SNAPSHOT, "Tracon.Options.Extra\nTracon.Options.Name\nTracon.NewType.Id\n")

            delta = bc.surface_delta(repo, "1.0.0")

        # `Options` existed at the tag, `NewType` did not: only the first breaks a consumer.
        self.assertEqual(delta.required_types, {"Tracon.Abstractions": {"Options"}})
        self.assertEqual(delta.notes, [])

    def test_ayni_tipte_required_uyenin_kalkmasi_kirilma_degildir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\nTracon.Options.Other\n")
            _write(repo, SNAPSHOT, "Tracon.Options.Name\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.required_types, {})

    def test_anlik_goruntu_silinmisse_surface_error(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\n")
            (repo / SNAPSHOT).unlink()

            with self.assertRaises(bc.SurfaceError) as raised:
                bc.surface_delta(repo, "1.0.0")

        self.assertIn("required-uyeler.txt", str(raised.exception))

    def test_jenerik_tip_adlari_eslesir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="")
            _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt", "#nullable enable\nTracon.Page<T>\nTracon.Page<T>.Items.get -> T\n")
            _git(repo, "add", "-A")
            _git(repo, "commit", "-q", "-m", "generic")
            _git(repo, "tag", "v1.0.1")
            _write(repo, SNAPSHOT, "Tracon.Page.Items\n")

            delta = bc.surface_delta(repo, "1.0.1")

        self.assertEqual(delta.required_types, {"Tracon.Abstractions": {"Page"}})

    def test_adlandirma_ayni_code_span_ister(self):
        tests = ["RunStoreContract.New_test", "SessionStoreContract.New_test"]

        self.assertEqual(bc.unnamed_contract_tests(tests, "- `RunStoreContract.New_test` · `SessionStoreContract.New_test`."), [])
        self.assertEqual(
            bc.unnamed_contract_tests(tests, "- `RunStoreContract.New_test` is new; `New_test` too."),
            ["SessionStoreContract.New_test"])
        self.assertEqual(
            bc.unnamed_contract_tests(tests, "- `RunStoreContract` and `SessionStoreContract` got `New_test`."), sorted(tests))
        self.assertEqual(
            bc.unnamed_contract_tests(
                ["RunStoreContract.New_test"], "- `Tracon.Testing.Contracts.Storage.RunStoreContract.New_test()` is new."), [])
        self.assertEqual(bc.unnamed_contract_tests(tests, None), sorted(tests))


class YuzeyKapiTestleri(unittest.TestCase):
    """`check` with a repo: the line decides what the surface readers may do."""

    def _run(self, *, version: str, notes: str, baseline: str = "1.0.0", edit=None, snapshot: str | None = ""):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=snapshot, tag=f"v{baseline}")
            if edit is not None:
                edit(repo)
            reports = pathlib.Path(directory) / "reports"
            reports.mkdir()
            changelog = pathlib.Path(directory) / "CHANGELOG.md"
            changelog.write_text(f"# Changelog\n\n## [Unreleased]\n\n{notes}\n", encoding="utf-8")
            return _quiet(lambda: bc.check(
                report_dir=reports, baseline=baseline, version=version, changelog=changelog, result_path=None, repo=repo))

    @staticmethod
    def _add_test(repo: pathlib.Path) -> None:
        _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", CONTRACT_API_V1 + (
            "Tracon.Testing.Contracts.Storage.RunStoreContract.New_test() -> System.Threading.Tasks.Task!\n"))

    @staticmethod
    def _add_member(repo: pathlib.Path) -> None:
        _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt",
               "#nullable enable\nTracon.Options\nTracon.Options.Name.get -> string!\nTracon.Options.Extra.get -> string!\n")

    def test_yama_surumunde_eklenen_uye_kirmizi(self):
        code, output = self._run(version="1.0.1", notes="", edit=self._add_member)

        self.assertEqual(code, 1)
        self.assertIn("Hat: donmuş 1.x yama", output)
        self.assertIn("Yama sürümünde public yüzey büyüyemez", output)
        self.assertIn("Tracon.Abstractions: 1 satır", output)

    def test_yama_surumunde_adli_contract_testi_yesil(self):
        """The rule table: a patch may add a test that measures documented behavior (a defect fix), if the notes name it."""
        code, output = self._run(version="1.0.1", notes="- Added `RunStoreContract.New_test`.", edit=self._add_test)

        self.assertEqual(code, 0, output)
        self.assertIn("Hat: donmuş 1.x yama", output)
        self.assertIn("Contract testi eklemesi (taban v1.0.0'den beri): 1", output)

    def test_yama_surumunde_adsiz_contract_testi_kirmizi(self):
        code, output = self._run(version="1.0.1", notes="- Other.", edit=self._add_test)

        self.assertEqual(code, 1)
        self.assertIn("RunStoreContract.New_test", output)
        self.assertNotIn("Yama sürümünde public yüzey büyüyemez", output)

    def test_yama_surumunde_contract_testi_ile_baska_uye_kirmizi(self):
        def edit(repo):
            self._add_test(repo)
            self._add_member(repo)

        code, output = self._run(version="1.0.1", notes="- Added `RunStoreContract.New_test`.", edit=edit)

        self.assertEqual(code, 1)
        self.assertIn("Tracon.Abstractions: 1 satır", output)
        self.assertNotIn("Testing.Contracts.Xunit: ", output)

    def test_yama_surumunde_required_uyenin_opsiyonel_olmasi_kirmizi(self):
        def edit(repo):
            _write(repo, SNAPSHOT, "Tracon.Options.Name\n")

        code, output = self._run(
            version="1.0.1", notes="", edit=edit, snapshot="Tracon.Options.Name\nTracon.Options.Other\n")
        minor, minor_output = self._run(
            version="1.1.0", notes="", edit=edit, snapshot="Tracon.Options.Name\nTracon.Options.Other\n")

        self.assertEqual(code, 1)
        self.assertIn("required üye opsiyonel olamaz ya da kalkamaz", output)
        self.assertIn("Tracon.Options.Other", output)
        self.assertEqual(minor, 0, minor_output)

    def test_minor_surumunde_eklenen_uye_yesil(self):
        code, output = self._run(version="1.1.0", notes="", edit=self._add_member)

        self.assertEqual(code, 0, output)
        self.assertIn("Hat: donmuş 1.x (kırılma reddedilir)", output)

    def test_preview_hattinda_eklenen_uye_yesil(self):
        code, output = self._run(version="1.0.0-preview.6", baseline="1.0.0-preview.5", notes="", edit=self._add_member)

        self.assertEqual(code, 0, output)
        self.assertIn("Hat: preview", output)

    def test_eklenen_contract_testi_notta_yoksa_kirmizi_eksik_adlarla(self):
        code, output = self._run(version="1.1.0", notes="- Other.", edit=self._add_test)

        self.assertEqual(code, 1)
        self.assertIn("Contract testi eklemesi sürüm notunda adıyla geçmiyor", output)
        self.assertIn("RunStoreContract.New_test", output)

    def test_eklenen_contract_testi_adiyla_anilirsa_yesil(self):
        code, output = self._run(version="1.1.0", notes="- Added `RunStoreContract.New_test`.", edit=self._add_test)

        self.assertEqual(code, 0, output)
        self.assertIn("Contract testi eklemesi (taban v1.0.0'den beri): 1", output)

    def test_contract_kapisi_preview_hattinda_da_kosar(self):
        """The first real run must not be GA day."""
        code, output = self._run(version="1.0.0-preview.6", baseline="1.0.0-preview.5", notes="", edit=self._add_test)

        self.assertEqual(code, 1)
        self.assertIn("Hat: preview", output)
        self.assertIn("RunStoreContract.New_test", output)

    def test_surum_notu_hic_yoksa_kirmizi_ve_mesaj_contract_diyor(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            self._add_test(repo)
            reports = pathlib.Path(directory) / "reports"
            reports.mkdir()
            changelog = pathlib.Path(directory) / "CHANGELOG.md"
            changelog.write_text("# Changelog\n", encoding="utf-8")

            code, output = _quiet(lambda: bc.check(
                report_dir=reports, baseline="1.0.0", version="1.1.0", changelog=changelog, result_path=None, repo=repo))

        self.assertEqual(code, 1)
        self.assertIn("Contract testi eklendi ama '1.1.0' için sürüm notu yok", output)

    def test_taban_etiketi_yoksa_kapi_kirmizi_yesil_degil(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            reports = pathlib.Path(directory) / "reports"
            reports.mkdir()
            changelog = pathlib.Path(directory) / "CHANGELOG.md"
            changelog.write_text("# Changelog\n", encoding="utf-8")

            code, output = _quiet(lambda: bc.check(
                report_dir=reports, baseline="9.9.9", version="9.9.10", changelog=changelog, result_path=None, repo=repo))

        self.assertEqual(code, 1)
        self.assertIn("taban etiketi 'v9.9.9'", output)

    def test_required_uye_eklenmesi_preview_hattinda_notta_adiyla(self):
        def edit(repo):
            _write(repo, SNAPSHOT, "Tracon.Options.Extra\nTracon.Options.Name\n")

        green, green_output = self._run(
            version="1.0.0-preview.6", baseline="1.0.0-preview.5",
            notes="- `Options` has a new `required` member `Extra`.", edit=edit, snapshot="Tracon.Options.Name\n")
        red, red_output = self._run(
            version="1.0.0-preview.6", baseline="1.0.0-preview.5", notes="- Other.", edit=edit, snapshot="Tracon.Options.Name\n")

        self.assertEqual(green, 0, green_output)
        self.assertEqual(red, 1)
        self.assertIn("Tracon.Abstractions: Options", red_output)

    def test_required_uye_eklenmesi_donmus_hatta_notla_da_kirmizi(self):
        def edit(repo):
            _write(repo, SNAPSHOT, "Tracon.Options.Extra\nTracon.Options.Name\n")

        code, output = self._run(
            version="1.1.0", notes="- `Options` has a new `required` member `Extra`.", edit=edit, snapshot="Tracon.Options.Name\n")

        self.assertEqual(code, 1)
        self.assertIn("Options (yeni required üye)", output)
        self.assertIn("sürüm notu bunu kurtarmaz", output)

    def test_sonuc_dosyasi_hat_ve_contract_listesini_tasir(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            self._add_test(repo)
            reports = pathlib.Path(directory) / "reports"
            reports.mkdir()
            changelog = pathlib.Path(directory) / "CHANGELOG.md"
            changelog.write_text("## [Unreleased]\n\n- `RunStoreContract.New_test`.\n", encoding="utf-8")
            result_path = pathlib.Path(directory) / "out" / "breaking-changes.json"

            code, output = _quiet(lambda: bc.check(
                report_dir=reports, baseline="1.0.0", version="1.1.0", changelog=changelog, result_path=result_path, repo=repo))
            result = json.loads(result_path.read_text(encoding="utf-8"))

        self.assertEqual(code, 0, output)
        self.assertEqual(result["line"], "frozen")
        self.assertEqual(result["contractTests"], ["RunStoreContract.New_test"])


class DepoKorumaTestleri(unittest.TestCase):
    def test_depoda_compatibility_suppressions_dosyasi_yok(self):
        """A report written into the source tree is read back as a suppression
        input by every later pack and hides the break for good (187.4 trap 1)."""
        found = sorted(str(path.relative_to(ROOT)) for path in (ROOT / "src").rglob("CompatibilitySuppressions.xml"))

        self.assertEqual(found, [])


if __name__ == "__main__":
    unittest.main()
