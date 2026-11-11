#!/usr/bin/env python3
"""Tests for the breaking-change gate of the release rehearsal.

Pure logic only: a temp git repository, temp files and a fake restore. The
MSBuild wiring and the real `dotnet pack` are covered by
`tests/Tracon.Package.Tests/PackageBaselineWiringTests.cs`; the ordering
inside `gate.py release` by `gate_test.py`.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
import pathlib
import re
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


def _changelog(repo: pathlib.Path, *released: str) -> None:
    """A CHANGELOG whose dated headings are the published versions; the baseline reads only these."""
    sections = "".join(f"## [{version}] - 2026-10-01\n\n- x\n\n" for version in released)
    (repo / "CHANGELOG.md").write_text(f"# Changelog\n\n## [Unreleased]\n\n{sections}", encoding="utf-8")


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


class BaselineSelectionTests(unittest.TestCase):
    def test_an_untagged_head_takes_the_latest_tag_as_the_baseline(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.2")
            _commit(repo, "three")
            _changelog(repo, "1.0.0-preview.2", "1.0.0-preview.1")

            self.assertEqual(bc.resolve_baseline(repo), "1.0.0-preview.2")

    def test_a_tagged_head_takes_the_previous_tag_as_the_baseline(self):
        """A tag run compared with its own tag would be a silent 'no change'."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.2")
            _changelog(repo, "1.0.0-preview.2", "1.0.0-preview.1")

            self.assertEqual(bc.resolve_baseline(repo), "1.0.0-preview.1")

    def test_a_tag_without_a_v_prefix_is_not_a_baseline(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "site-2026")
            _changelog(repo, "1.0.0-preview.1")

            self.assertEqual(bc.resolve_baseline(repo), "1.0.0-preview.1")

    def test_a_burned_tag_is_not_a_baseline(self):
        """A tag with no dated heading never shipped: its packages are not there to restore."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.2")
            _commit(repo, "three")
            _changelog(repo, "1.0.0-preview.1")

            baseline, output = _quiet(lambda: bc.resolve_baseline(repo))

        self.assertEqual(baseline, "1.0.0-preview.1")
        self.assertIn("v1.0.0-preview.2", output)

    def test_a_tagged_head_skips_a_burned_tag_in_between(self):
        """The fix ships as preview.3 and takes over the heading of the burned preview.2."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.2")
            _commit(repo, "fix")
            _git(repo, "tag", "v1.0.0-preview.3")
            _changelog(repo, "1.0.0-preview.3", "1.0.0-preview.1")

            baseline, _ = _quiet(lambda: bc.resolve_baseline(repo))

        self.assertEqual(baseline, "1.0.0-preview.1")

    def test_no_published_tag_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")
            _changelog(repo)

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(repo)

        self.assertIn("burned", str(raised.exception))
        self.assertIn("v1.0.0-preview.1", str(raised.exception))

    def test_a_missing_changelog_is_red_with_no_silent_fallback(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _git(repo, "tag", "v1.0.0-preview.1")
            _commit(repo, "two")

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(repo)

        self.assertIn("CHANGELOG.md is missing", str(raised.exception))

    def test_a_missing_tag_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(repo)

        self.assertIn("tag is reachable", str(raised.exception))

    def test_a_first_tagged_head_without_a_previous_tag_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo(directory)
            _commit(repo, "one")
            _commit(repo, "two")
            _git(repo, "tag", "v1.0.0-preview.1")

            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(repo)

        self.assertIn("the parent of HEAD", str(raised.exception))

    def test_a_directory_that_is_not_a_git_repository_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(bc.BaselineError) as raised:
                bc.resolve_baseline(pathlib.Path(directory))

        self.assertIn("not a git repository", str(raised.exception))

    def test_a_shallow_clone_is_red(self):
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

        self.assertIn("shallow clone", str(raised.exception))

    def test_a_version_equal_to_the_baseline_is_red(self):
        self.assertFalse(bc.version_is_above_baseline("1.0.0-preview.2", "1.0.0-preview.2"))
        self.assertFalse(bc.version_is_above_baseline("1.0.0-preview.1.9", "1.0.0-preview.2"))

    def test_minver_heights_are_above_the_baseline(self):
        self.assertTrue(bc.version_is_above_baseline("1.0.0-preview.2.46", "1.0.0-preview.2"))
        self.assertTrue(bc.version_is_above_baseline("1.0.0-preview.10", "1.0.0-preview.9"))
        self.assertTrue(bc.version_is_above_baseline("1.0.0", "1.0.0-preview.9"))
        self.assertTrue(bc.version_is_above_baseline("1.1.0-preview.0.3", "1.0.0"))


class IsolatedRestoreTests(unittest.TestCase):
    def test_restore_uses_an_isolated_cache_and_only_nuget_org(self):
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

    def test_a_restore_failure_names_the_package(self):
        def fake_run(command, **kwargs):
            return subprocess.CompletedProcess(
                command, 1, "error NU1101: Unable to find package Tracon.Voice. No packages exist with this id", "")

        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(bc.RestoreError) as raised:
                bc.restore_baselines(["Tracon.Core", "Tracon.Voice"], "1.0.0-preview.3", pathlib.Path(directory), run=fake_run)

        message = str(raised.exception)
        self.assertIn("Tracon.Voice", message)
        self.assertIn("v1.0.0-preview.3", message)
        self.assertIn("finish the release first", message)

    def _cache_entry(self, cache: pathlib.Path, project_id: str, version: str, source: str) -> None:
        directory = cache / project_id.lower() / version
        directory.mkdir(parents=True)
        (directory / f"{project_id.lower()}.{version}.nupkg").write_bytes(b"")
        (directory / ".nupkg.metadata").write_text(json.dumps({"version": 2, "source": source}), encoding="utf-8")

    def test_a_nuget_org_source_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            cache = pathlib.Path(directory)
            self._cache_entry(cache, "Tracon.Core", "1.0.0-preview.2", bc.NUGET_ORG)

            self.assertEqual(bc.baseline_source_violations(["Tracon.Core"], "1.0.0-preview.2", cache), [])

    def test_a_source_other_than_nuget_org_is_red(self):
        """The trap the isolated cache exists for: ReleaseArtifactFixture packs
        a local `1.0.0-preview.1` and the developer cache records that feed."""
        with tempfile.TemporaryDirectory() as directory:
            cache = pathlib.Path(directory)
            self._cache_entry(cache, "Tracon.Core", "1.0.0-preview.1", "/Users/x/Tracon/artifacts/package/release")

            violations = bc.baseline_source_violations(["Tracon.Core", "Tracon.Voice"], "1.0.0-preview.1", cache)

        self.assertEqual(len(violations), 2)
        self.assertIn("artifacts/package/release", violations[0])
        self.assertIn("Tracon.Voice", violations[1])

    def test_the_pack_properties_carry_the_three_p_arguments(self):
        cache, report = pathlib.Path("/c"), pathlib.Path("/r")
        properties = bc.pack_properties(cache, "1.0.0-preview.2", report)

        # The path goes to `dotnet` in the operating system's own format (`\c` on
        # Windows); the expected value is built from the same format (measured
        # 2026-09-24 on a Windows CI runner).
        self.assertEqual(properties, [
            f"-p:TraconPackageBaselineRoot={cache}",
            "-p:TraconPackageBaselineVersion=1.0.0-preview.2",
            f"-p:TraconApiCompatReportDir={report}",
        ])


class FirstReleaseFlagTests(unittest.TestCase):
    def test_a_flagged_package_is_not_in_the_restore_list(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _write_csproj(root, "Tracon.Core")
            _write_csproj(root, "Tracon.New", "<PropertyGroup>" + bc.FIRST_RELEASE_FLAG + "</PropertyGroup>")

            self.assertEqual(bc.baseline_package_ids(root, ["Tracon.New", "Tracon.Core"]), ["Tracon.Core"])

    def test_a_flag_on_a_package_the_baseline_has_is_red(self):
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

    # Packages that have no published baseline yet. The flag is required for them: nuget.org has nothing
    # to restore for a rehearsal. `gate.py release` calls the flag stale once the package is in the latest v* tag, and
    # then the entry goes from here too. Tracon.AGUI left with 1.0.0-preview.6.
    NEW_SINCE_LAST_RELEASE: list[str] = []

    def test_in_the_repository_only_an_unpublished_package_carries_the_flag(self):
        """Every other csproj existed in v1.0.0-preview.2 (measured 2026-09-23) and must not carry the flag."""
        flagged = sorted(
            path.name for path in (ROOT / "src").glob("*/*.csproj") if bc.FIRST_RELEASE_FLAG in path.read_text(encoding="utf-8"))

        self.assertEqual(flagged, self.NEW_SINCE_LAST_RELEASE)


class ValidationProofTests(unittest.TestCase):
    def _semaphore(self, root: pathlib.Path, project_id: str, configuration: str, mtime: float) -> pathlib.Path:
        path = root / "artifacts" / "obj" / project_id / configuration / bc.SEMAPHORE_NAME
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(b"")
        os.utime(path, (mtime, mtime))
        return path

    def test_the_semaphore_path_is_found_case_insensitively(self):
        for configuration in ("Release", "release"):
            with self.subTest(configuration=configuration), tempfile.TemporaryDirectory() as directory:
                root = pathlib.Path(directory)
                expected = self._semaphore(root, "Tracon.Core", configuration, time.time())
                self._semaphore(root, "Tracon.Core", "debug_net10.0", time.time())

                self.assertEqual(bc.semaphore_path(root, "Tracon.Core"), expected)

    def test_a_missing_semaphore_puts_the_searched_path_in_the_message(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)

            problems = bc.validation_not_run(root, ["Tracon.Core"], time.time())

        self.assertEqual(len(problems), 1)
        self.assertIn(str(root / "artifacts" / "obj" / "Tracon.Core"), problems[0])
        self.assertIn(bc.SEMAPHORE_NAME, problems[0])

    def test_an_old_semaphore_is_red(self):
        """RunPackageValidation is incremental: an unchanged package skips it
        and leaves the old semaphore - the silent pass this proof exists for."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            pack_started = time.time()
            self._semaphore(root, "Tracon.Core", "Release", pack_started - 3600)
            self._semaphore(root, "Tracon.Voice", "Release", pack_started + 5)

            problems = bc.validation_not_run(root, ["Tracon.Core", "Tracon.Voice"], pack_started)

        self.assertEqual(len(problems), 1)
        self.assertIn("Tracon.Core: package validation did not run in this pack", problems[0])

    def test_the_same_second_passes_at_second_resolution(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            pack_started = 1_000_000.7
            self._semaphore(root, "Tracon.Core", "Release", 1_000_000.0)

            self.assertEqual(bc.validation_not_run(root, ["Tracon.Core"], pack_started), [])


class BreakingChangeListTests(unittest.TestCase):
    def test_doc_ids_reduce_to_their_type(self):
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

    def test_a_target_that_is_not_a_doc_id_is_not_reduced(self):
        for target in ("N:Tracon", "net8.0", "lib/net8.0/Tracon.Core.dll", "M:Method"):
            with self.subTest(target=target):
                self.assertIsNone(bc.type_of_doc_id(target))

    def test_cp0008_reduces_by_its_doc_id(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", _baseline_record("CP0008", "T:Tracon.RunStatus"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(changes.errors, [])
        self.assertEqual(changes.types, {"Tracon.Core": {"RunStatus"}})

    def test_a_repeated_tfm_is_a_single_record(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", *[
                _baseline_record("CP0001", "T:Tracon.AgentCallGraph", tfm) for tfm in ("net8.0", "net9.0", "net10.0")])

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(changes.type_count(), 1)

    def test_pkv006_needs_a_package_and_a_tfm(self):
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
        self.assertEqual(unnamed_deprecation, ["Tracon.Voice: dropped TFM net8.0, net9.0"])
        self.assertEqual(named, [])

    def test_a_record_outside_the_baseline_is_red(self):
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

    def test_pkv007_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", (
                "  <Suppression>\n    <DiagnosticId>PKV007</DiagnosticId>\n    <Target>net10.0-linux-x64</Target>\n"
                "    <IsBaselineSuppression>true</IsBaselineSuppression>\n  </Suppression>"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("unrecognized record PKV007", changes.errors[0])

    def test_a_target_that_is_not_a_doc_id_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            _report(reports, "Tracon.Core", _baseline_record("CP0003", "lib/net10.0/Tracon.Core.dll"))

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("unrecognized record CP0003", changes.errors[0])

    def test_broken_xml_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            reports = pathlib.Path(directory)
            (reports / "Tracon.Core.xml").write_text("<Suppressions><Suppression>", encoding="utf-8")

            changes = bc.read_breaking_changes(reports)

        self.assertEqual(len(changes.errors), 1)
        self.assertIn("Tracon.Core: cannot read the report", changes.errors[0])

    def test_the_real_report_fixture(self):
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

    def test_the_fixture_carries_no_absolute_path(self):
        for fixture in TESTDATA.glob("*.xml"):
            with self.subTest(fixture=fixture.name):
                text = fixture.read_text(encoding="utf-8-sig")
                self.assertNotIn("/Users/", text)
                self.assertNotIn("/private/", text)


class ReleaseNoteMatchingTests(unittest.TestCase):
    def _changes(self, **types: set[str]) -> object:
        changes = bc.BreakingChanges()
        for package, names in types.items():
            changes.types[package.replace("_", ".")] = set(names)
        return changes

    def test_a_missing_type_is_reported_by_name(self):
        changes = self._changes(Tracon_Abstractions={"SchemaReadyGate", "JobPayload"}, Tracon_Core={"TextChunker"})

        missing = bc.unnamed_changes(changes, "- `Tracon.Abstractions`: `JobPayload`.\n- `Tracon.Core`: `TextChunker`.\n")

        self.assertEqual(missing, ["Tracon.Abstractions: SchemaReadyGate"])

    def test_a_wildcard_is_not_accepted(self):
        changes = self._changes(Tracon_OpenAI={"OpenAIModelCatalog", "OpenAIChatClientFactory"})

        missing = bc.unnamed_changes(changes, "- `Tracon.OpenAI`: the `*ChatClientFactory` and `*ModelCatalog` types.\n")

        self.assertEqual(missing, ["Tracon.OpenAI: OpenAIChatClientFactory, OpenAIModelCatalog"])

    def test_a_longer_name_does_not_cover_a_shorter_one(self):
        changes = self._changes(Tracon_OpenAI={"OpenAIChatClientFactory"})

        missing = bc.unnamed_changes(changes, "`OpenAIChatClientFactoryOptions` is unchanged; OpenAIChatClientFactory is internal.")

        # Neither the longer span nor the bare (span-less) word names the type.
        self.assertEqual(missing, ["Tracon.OpenAI: OpenAIChatClientFactory"])

    def test_type_member_and_generic_spans_are_accepted(self):
        changes = self._changes(Tracon_Core={"A", "B", "C", "D", "E"})
        notes = "`A` · `B<T>` · `Tracon.C` · `Outer.D` · `E.Run(string, int)`"

        self.assertEqual(bc.unnamed_changes(changes, notes), [])

    def test_a_span_split_at_a_line_end_is_accepted(self):
        changes = self._changes(Tracon_Core={"SandboxedSkillScriptRunner"})
        notes = "- `SandboxedSkillScriptRunner.RunStoredScriptAsync`\n  takes the skill."

        self.assertEqual(bc.unnamed_changes(changes, notes), [])

    def test_a_fenced_code_block_does_not_shift_span_matching(self):
        # A note that carries two fenced blocks used to miss two correctly written names.
        changes = self._changes(Tracon_Core={"A", "B", "C"})
        block = "  ```csharp\n  new A(x, y: 1);\n  ```\n"
        notes = f"- `A` changed:\n\n{block}\n- `B` changed:\n\n{block}\n- `C` changed.\n"

        self.assertEqual(bc.unnamed_changes(changes, notes), [])

    def test_a_name_found_only_in_a_fenced_block_is_not_accepted(self):
        changes = self._changes(Tracon_Core={"Hidden"})
        notes = "- A type changed:\n\n  ```csharp\n  `Hidden` x;\n  ```\n"

        self.assertEqual(bc.unnamed_changes(changes, notes), ["Tracon.Core: Hidden"])

    def test_a_package_id_needs_an_exact_match(self):
        changes = bc.BreakingChanges()
        changes.dropped_frameworks["Tracon"] = {"net8.0"}

        missing = bc.unnamed_changes(changes, "- `Tracon.Core`: the `net8.0` target is removed.\n")

        self.assertEqual(missing, ["Tracon: dropped TFM net8.0"])

    def test_the_tfm_and_the_package_must_be_in_the_same_item(self):
        changes = bc.BreakingChanges()
        changes.dropped_frameworks["Tracon.Core"] = {"net8.0"}

        split = bc.unnamed_changes(changes, "- `Tracon.Core`: something.\n\n- The `net8.0` target is removed.\n")
        together = bc.unnamed_changes(changes, "- `Tracon.Core`: the\n  `net8.0` target is removed.\n")

        self.assertEqual(split, ["Tracon.Core: dropped TFM net8.0"])
        self.assertEqual(together, [])


class PendingCutTests(unittest.TestCase):
    """A cut waits for its tag when the topmost dated CHANGELOG section is above the baseline."""

    def _cut(self, text: str, baseline: str = "1.0.0-preview.2") -> str | None:
        with tempfile.TemporaryDirectory() as directory:
            changelog = pathlib.Path(directory) / "CHANGELOG.md"
            changelog.write_text(text, encoding="utf-8")
            return bc.pending_cut(changelog, baseline)

    def test_the_topmost_dated_section_above_the_baseline_is_the_cut(self):
        text = "# Changelog\n\n## [Unreleased]\n\n## [1.0.0-preview.3] - 2026-10-01\n- x\n## [1.0.0-preview.2] - 2026-09-20\n- y\n"

        self.assertEqual(self._cut(text), "1.0.0-preview.3")

    def test_a_tagged_release_is_not_a_pending_cut(self):
        text = "# Changelog\n\n## [Unreleased]\n- x\n## [1.0.0-preview.2] - 2026-09-20\n- y\n"

        self.assertIsNone(self._cut(text))

    def test_a_section_with_no_date_is_a_release_still_being_prepared_not_a_cut(self):
        text = "# Changelog\n\n## [1.0.0-preview.3]\n- x\n## [1.0.0-preview.2] - 2026-09-20\n- y\n"

        self.assertIsNone(self._cut(text))

    def test_a_burned_tag_has_no_dated_section_and_is_never_a_cut(self):
        """preview.3 burned: the tag exists, the fix took over the heading as preview.4. The baseline skips the burned
        tag (preview.2); preview.4 is the cut that waits, and nothing about preview.3 shows up."""
        text = "# Changelog\n\n## [1.0.0-preview.4] - 2026-10-05\n- x\n## [1.0.0-preview.2] - 2026-09-20\n- y\n"

        self.assertEqual(self._cut(text), "1.0.0-preview.4")
        self.assertIsNone(self._cut(text, baseline="1.0.0-preview.4"))

    def test_a_heading_that_is_not_a_version_is_not_a_cut(self):
        self.assertIsNone(self._cut("# Changelog\n\n## [not-a-version] - 2026-10-01\n- x\n"))


class GateCallTests(unittest.TestCase):
    """`check`: the report dir + CHANGELOG.md -> exit code, as gate.py calls it."""

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

    def test_a_tag_run_reads_the_version_section(self):
        """At tag time [Unreleased] was renamed; the version section is read."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="", section="### Removed\n- `TextChunker`.\n", version="1.0.0-preview.3")

        self.assertEqual(code, 0, output)
        self.assertIn("all named in the '1.0.0-preview.3' notes", output)
        self.assertEqual(result["notesHeading"], "1.0.0-preview.3")
        self.assertEqual(result["types"], {"Tracon.Core": ["TextChunker"]})

    def test_an_untagged_run_reads_unreleased(self):
        """The last release is tagged (baseline = its tag); the notes of the next one sit under [Unreleased]."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="### Removed\n- `TextChunker`.\n", section="- Other.\n", version="1.0.0-preview.3.47",
            baseline="1.0.0-preview.3")

        self.assertEqual(code, 0, output)
        self.assertEqual(result["notesHeading"], "Unreleased")
        self.assertEqual(result["baseline"], "1.0.0-preview.3")

    def test_a_cut_waiting_for_its_tag_reads_the_notes_of_its_section(self):
        """The cut commit (section dated, empty [Unreleased] above) before its tag exists. MinVer still resolves
        `1.0.0-preview.2.N`, so no section carries that version; the notes come from the cut section."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="", section="### Removed\n- `TextChunker`.\n", version="1.0.0-preview.2.48")

        self.assertEqual(code, 0, output)
        self.assertIn("all named in the '1.0.0-preview.3' notes", output)
        self.assertEqual(result["notesHeading"], "1.0.0-preview.3")

    def test_a_cut_waiting_for_its_tag_still_names_what_the_notes_leave_out(self):
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="", section="### Added\n- Something else.\n", version="1.0.0-preview.2.48")

        self.assertEqual(code, 1)
        self.assertIn("Tracon.Core: TextChunker", output)
        self.assertIn("1.0.0-preview.3", output)
        self.assertIsNone(result)

    def test_changes_after_the_cut_are_named_in_unreleased_and_the_cut_notes_still_count(self):
        """Both sections ship something since the baseline: a break named in either one is announced."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker"),
                                     _baseline_record("CP0001", "T:Tracon.TextSplitter")]},
            unreleased="### Removed\n- `TextSplitter`.\n", section="### Removed\n- `TextChunker`.\n",
            version="1.0.0-preview.2.49")

        self.assertEqual(code, 0, output)
        self.assertEqual(result["notesHeading"], "1.0.0-preview.3 and Unreleased")

    def test_the_version_section_wins_over_a_cut_waiting_for_its_tag(self):
        """A tag run of a version that has its own section never reads another section."""
        code, output, result = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="### Removed\n- `TextChunker`.\n", section="- Other.\n", version="1.0.0-preview.3")

        self.assertEqual(code, 1)
        self.assertIn("Tracon.Core: TextChunker", output)
        self.assertIsNone(result)

    def test_a_cut_section_with_no_body_says_where_to_write_the_notes(self):
        code, output, _ = self._run(
            reports={"Tracon.Core": [_baseline_record("CP0001", "T:Tracon.TextChunker")]},
            unreleased="", section="", version="1.0.0-preview.2.48")

        self.assertEqual(code, 1)
        self.assertIn("no release notes", output)
        self.assertIn("'## [1.0.0-preview.3]' section is the cut waiting for its tag", output)
        self.assertNotIn("git push", output)

    def test_an_empty_list_passes_with_empty_notes(self):
        code, output, result = self._run(reports={}, unreleased="", section="", version="1.0.0-preview.2.47",
                                         changelog_text="# Changelog\n")

        self.assertEqual(code, 0, output)
        self.assertIn("0 types, 0 dropped TFMs, 0 packages", output)
        self.assertEqual(result["types"], {})

    def test_a_strict_record_is_red_even_with_notes(self):
        code, output, _ = self._run(
            reports={"Tracon.Core": ["  <Suppression>\n    <DiagnosticId>CP0001</DiagnosticId>\n"
                                     "    <Target>T:Tracon.X</Target>\n  </Suppression>"]},
            unreleased="- `X`.\n", section="", version="1.0.0-preview.2.47")

        self.assertEqual(code, 1)
        self.assertIn("strict", output)

    def test_the_cli_checks_the_report_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "reports").mkdir()
            (root / "reports" / "Tracon.Voice.xml").write_bytes((TESTDATA / "voice-net10-only.xml").read_bytes())
            changelog = root / "CHANGELOG.md"
            changelog.write_text("## [Unreleased]\n\n- `Tracon.Voice`: `VoiceProviderNames`.\n", encoding="utf-8")
            argv = ["--report-directory", str(root / "reports"), "--base", "1.0.0-preview.2", "--changelog", str(changelog)]

            red, red_output = _quiet(lambda: bc.main(argv))
            changelog.write_text(
                "## [Unreleased]\n\n- `Tracon.Voice`: `VoiceProviderNames`.\n"
                "- `Tracon.Voice`: the `net8.0` and `net9.0` targets.\n", encoding="utf-8")
            green, _ = _quiet(lambda: bc.main(argv))

        self.assertEqual(red, 1)
        self.assertIn("Tracon.Voice: dropped TFM net8.0, net9.0", red_output)
        self.assertEqual(green, 0)


class ReleaseLineTests(unittest.TestCase):
    """One function decides the line; the non-.NET gate reuses it."""

    def test_the_version_order_is_preview_rc_ga(self):
        ordered = ["1.0.0-preview.5", "1.0.0-preview.12", "1.0.0-rc.1", "1.0.0-rc.2", "1.0.0", "1.0.1", "1.1.0", "2.0.0"]

        for lower, higher in zip(ordered, ordered[1:]):
            self.assertEqual(bc.compare_versions(lower, higher), -1, f"{lower} < {higher}")

    def test_the_preview_line(self):
        for version in ("1.0.0-preview.6", "1.0.0-preview.5.47", "1.0.0-rc.1"):
            line = bc.release_line("1.0.0-preview.5", version)

            self.assertEqual(line.kind, bc.LINE_PREVIEW, version)
            self.assertFalse(line.frozen, version)

    def test_a_preview_baseline_heading_to_rc1_is_not_frozen(self):
        """`preview.5` -> `rc.1` is the step that FREEZES; judging it by the frozen rule would reject the freeze."""
        line = bc.release_line("1.0.0-preview.5", "1.0.0-rc.1")

        self.assertEqual(line.describe(), "Line: preview (a break passes when the notes name it)")

    def test_an_rc1_baseline_is_frozen(self):
        for version in ("1.0.0-rc.2", "1.0.0", "1.0.1", "1.1.0", "1.9.0-preview.0.4"):
            line = bc.release_line("1.0.0-rc.1", version)

            self.assertEqual(line.kind, bc.LINE_FROZEN, version)
            self.assertEqual(line.major, 1, version)

    def test_a_patch_means_the_same_major_and_minor(self):
        self.assertTrue(bc.release_line("1.0.0-rc.1", "1.0.0-rc.2").patch)
        self.assertTrue(bc.release_line("1.0.0-rc.1", "1.0.0").patch)
        self.assertTrue(bc.release_line("1.0.0", "1.0.1").patch)
        self.assertTrue(bc.release_line("1.0.0", "1.0.0-preview.0.4").patch)
        self.assertFalse(bc.release_line("1.0.0", "1.1.0").patch)
        self.assertFalse(bc.release_line("1.0.0", "1.1.0-preview.0.5").patch)

    def test_a_major_increase_is_not_frozen(self):
        line = bc.release_line("1.4.0", "2.0.0")

        self.assertEqual(line.kind, bc.LINE_MAJOR)
        self.assertFalse(line.frozen)
        self.assertEqual(line.describe(), "Line: major bump (a break passes when the notes name it)")

    def test_the_line_descriptions(self):
        self.assertEqual(bc.release_line("1.0.0", "1.1.0").describe(), "Line: frozen 1.x (a break is rejected)")
        self.assertEqual(
            bc.release_line("1.0.0", "1.0.1").describe(),
            "Line: frozen 1.x patch (a break or an added public member is rejected)")
        self.assertEqual(bc.release_line("2.3.0", "2.4.0").describe(), "Line: frozen 2.x (a break is rejected)")

    def test_a_preview_baseline_works_without_a_version_but_a_frozen_one_does_not(self):
        self.assertEqual(bc.release_line("1.0.0-preview.5", None).kind, bc.LINE_PREVIEW)
        with self.assertRaises(ValueError):
            bc.release_line("1.0.0-rc.1", None)

    def test_something_that_is_not_a_version_is_an_error(self):
        with self.assertRaises(ValueError):
            bc.release_line("1.0.0", "next")


class FrozenLineGateTests(unittest.TestCase):
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

    def test_a_break_named_in_the_notes_is_red_on_a_frozen_line(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0", version="1.1.0", notes="- `IThing` gained a member.")

        self.assertEqual(code, 1)
        self.assertIn("Line: frozen 1.x (a break is rejected)", output)
        self.assertIn("the release notes cannot save it", output)
        self.assertIn("Tracon.Abstractions: IThing", output)

    def test_the_same_report_is_green_on_a_major_increase(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0", version="2.0.0", notes="- `IThing` gained a member.")

        self.assertEqual(code, 0, output)
        self.assertIn("Line: major bump", output)

    def test_a_break_missing_from_the_notes_is_red_on_a_major_increase(self):
        code, output = self._run(records=[self.BREAK], baseline="1.4.0", version="2.0.0", notes="- Other.")

        self.assertEqual(code, 1)
        self.assertIn("Tracon.Abstractions: IThing", output)

    def test_a_break_from_rc1_to_rc2_is_red(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0-rc.1", version="1.0.0-rc.2", notes="- `IThing` gained a member.")

        self.assertEqual(code, 1)
        self.assertIn("Line: frozen 1.x patch", output)

    def test_a_dropped_tfm_is_red_on_a_frozen_line(self):
        code, output = self._run(
            records=[_framework_record("net8.0")], baseline="1.0.0", version="1.1.0",
            notes="- `Tracon.Abstractions`: the `net8.0` target is removed.")

        self.assertEqual(code, 1)
        self.assertIn("dropped TFM net8.0", output)

    def test_an_empty_list_is_green_on_a_frozen_line(self):
        code, output = self._run(records=[], baseline="1.0.0", version="1.1.0", notes="")

        self.assertEqual(code, 0, output)
        self.assertIn("Line: frozen 1.x", output)
        self.assertIn("0 types, 0 dropped TFMs, 0 packages — no notes needed", output)

    def test_the_preview_line_keeps_its_current_behavior(self):
        code, output = self._run(
            records=[self.BREAK], baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- `IThing` gained a member.")
        red, red_output = self._run(
            records=[self.BREAK], baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="- Other.")

        self.assertEqual(code, 0, output)
        self.assertIn("Line: preview (a break passes when the notes name it)", output)
        self.assertEqual(red, 1)
        self.assertIn("Tracon.Abstractions: IThing", red_output)

    def test_the_line_is_printed_in_every_run_including_a_red_report(self):
        code, output = self._run(
            records=["  <Suppression>\n    <DiagnosticId>CP0001</DiagnosticId>\n    <Target>T:Tracon.X</Target>\n  </Suppression>"],
            baseline="1.0.0-preview.5", version="1.0.0-preview.6", notes="")

        self.assertEqual(code, 1)
        self.assertTrue(output.startswith("Line: preview"), output)

    def test_the_cli_is_red_on_a_frozen_line_and_green_for_the_same_report_with_a_major(self):
        """The same report through the command line: red on a frozen line, green with a major bump."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            _report(root / "reports", "Tracon.Abstractions", self.BREAK)
            changelog = root / "CHANGELOG.md"
            changelog.write_text("## [Unreleased]\n\n- `IThing` gained a member.\n", encoding="utf-8")
            argv = ["--report-directory", str(root / "reports"), "--base", "1.0.0", "--changelog", str(changelog)]

            minor, minor_output = _quiet(lambda: bc.main([*argv, "--package-version", "1.1.0"]))
            major, major_output = _quiet(lambda: bc.main([*argv, "--package-version", "2.0.0"]))
            unknown, unknown_output = _quiet(lambda: bc.main(argv))

        self.assertEqual(minor, 1)
        self.assertIn("Line: frozen 1.x", minor_output)
        self.assertEqual(major, 0, major_output)
        self.assertEqual(unknown, 1)
        self.assertIn("Cannot decide the release line", unknown_output)


def _write(repo: pathlib.Path, relative: str, text: str) -> None:
    path = repo / relative
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")


def _commit_all(repo: pathlib.Path, message: str) -> None:
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", message)


CONTRACT = "src/Tracon.Testing.Contracts.Xunit"
CORE_API = "src/Tracon.Abstractions"
SNAPSHOT = "docs/contracts/required-members.txt"

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


class SurfaceReaderTests(unittest.TestCase):
    """The readers that stand where ApiCompat is blind."""

    def test_the_effective_surface_is_shipped_plus_unshipped(self):
        surface = bc.effective_api("#nullable enable\nA\nB\n", "#nullable enable\nC\n*REMOVED*B\n")

        self.assertEqual(surface, {"A", "C"})

    def test_moving_lines_to_shipped_at_ga_does_not_change_the_surface(self):
        """Lines that only change file are not 'added'."""
        before = bc.effective_api("", "#nullable enable\nA\nB\n")
        after = bc.effective_api("#nullable enable\nA\nB\n", "#nullable enable\n")

        self.assertEqual(before, after)

    def test_the_shape_of_a_contract_test(self):
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

    def test_the_real_contract_surface_counts_its_tests_and_two_helpers(self):
        """Measured 2026-10-24: 696 `[Fact]`/`[Theory]` plus two `protected void` helpers (`Enter`, `UseAmbientTenant`)."""
        surface = bc.api_now(ROOT)[bc.CONTRACT_PACKAGE]
        tests = bc.contract_tests(surface)

        self.assertGreater(len(tests), 600)
        self.assertIn("AgentFileStoreContract.Enter", tests)

    def test_a_first_record_with_no_snapshot_at_the_baseline_tag_gives_a_note(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-preview.5")

            delta = bc.surface_delta(repo, "1.0.0-preview.5")

        self.assertEqual(delta.required_types, {})
        self.assertIn("first record", delta.notes[0])

    def test_a_frozen_baseline_without_a_snapshot_raises_surface_error(self):
        """A frozen baseline was cut after the file existed: a missing snapshot would let a `required` addition through."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-rc.1")

            with self.assertRaises(bc.SurfaceError) as raised:
                bc.surface_delta(repo, "1.0.0-rc.1")

        self.assertIn("a frozen baseline must have the snapshot", str(raised.exception))

    def test_a_line_dropped_from_the_snapshot_is_listed(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\nTracon.Options.Other\n")
            _write(repo, SNAPSHOT, "Tracon.Options.Name\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.required_dropped, ["Tracon.Options.Other"])

    def test_a_new_contract_test_is_not_an_added_member_but_another_line_is(self):
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

    def test_an_added_test_and_member_are_listed(self):
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

    def test_moving_lines_to_shipped_at_ga_leaves_the_list_empty(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            _write(repo, f"{CONTRACT}/PublicAPI.Shipped.txt", CONTRACT_API_V1)
            _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", "#nullable enable\n")
            _write(repo, f"{CORE_API}/PublicAPI.Shipped.txt", "#nullable enable\nTracon.Options\nTracon.Options.Name.get -> string!\n")
            _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt", "#nullable enable\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.contract_tests, [])
        self.assertEqual(delta.added_members, {})

    def test_a_missing_publicapi_file_at_the_baseline_gives_an_empty_list_and_no_error(self):
        """`TraconPackageFirstRelease` precedent: a package the tag does not know has no suite to add a test to."""
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, contract_api=None)
            _write(repo, f"{CONTRACT}/PublicAPI.Shipped.txt", "#nullable enable\n")
            _write(repo, f"{CONTRACT}/PublicAPI.Unshipped.txt", CONTRACT_API_V1)

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.contract_tests, [])
        self.assertIn("Tracon.Testing.Contracts.Xunit", delta.added_members)

    def test_a_missing_baseline_tag_raises_baseline_error(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)

            with self.assertRaises(bc.BaselineError) as raised:
                bc.surface_delta(repo, "9.9.9")

        self.assertIn("v9.9.9", str(raised.exception))

    def test_a_required_member_added_to_an_existing_type_is_a_break(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\n")
            _write(repo, SNAPSHOT, "Tracon.Options.Extra\nTracon.Options.Name\nTracon.NewType.Id\n")

            delta = bc.surface_delta(repo, "1.0.0")

        # `Options` existed at the tag, `NewType` did not: only the first breaks a consumer.
        self.assertEqual(delta.required_types, {"Tracon.Abstractions": {"Options"}})
        self.assertEqual(delta.notes, [])

    def test_removing_a_required_member_from_the_same_type_is_not_a_break(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\nTracon.Options.Other\n")
            _write(repo, SNAPSHOT, "Tracon.Options.Name\n")

            delta = bc.surface_delta(repo, "1.0.0")

        self.assertEqual(delta.required_types, {})

    def test_a_deleted_snapshot_raises_surface_error(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\n")
            (repo / SNAPSHOT).unlink()

            with self.assertRaises(bc.SurfaceError) as raised:
                bc.surface_delta(repo, "1.0.0")

        self.assertIn("required-members.txt", str(raised.exception))

    def test_generic_type_names_match(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="")
            _write(repo, f"{CORE_API}/PublicAPI.Unshipped.txt", "#nullable enable\nTracon.Page<T>\nTracon.Page<T>.Items.get -> T\n")
            _git(repo, "add", "-A")
            _git(repo, "commit", "-q", "-m", "generic")
            _git(repo, "tag", "v1.0.1")
            _write(repo, SNAPSHOT, "Tracon.Page.Items\n")

            delta = bc.surface_delta(repo, "1.0.1")

        self.assertEqual(delta.required_types, {"Tracon.Abstractions": {"Page"}})

    def test_naming_needs_the_same_code_span(self):
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


class SurfaceGateTests(unittest.TestCase):
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

    def test_a_member_added_in_a_patch_release_is_red(self):
        code, output = self._run(version="1.0.1", notes="", edit=self._add_member)

        self.assertEqual(code, 1)
        self.assertIn("Line: frozen 1.x patch", output)
        self.assertIn("A patch release cannot grow the public surface", output)
        self.assertIn("Tracon.Abstractions: 1 line", output)

    def test_a_named_contract_test_in_a_patch_release_is_green(self):
        """The rule table: a patch may add a test that measures documented behavior (a defect fix), if the notes name it."""
        code, output = self._run(version="1.0.1", notes="- Added `RunStoreContract.New_test`.", edit=self._add_test)

        self.assertEqual(code, 0, output)
        self.assertIn("Line: frozen 1.x patch", output)
        self.assertIn("Contract tests added (since baseline v1.0.0): 1", output)

    def test_an_unnamed_contract_test_in_a_patch_release_is_red(self):
        code, output = self._run(version="1.0.1", notes="- Other.", edit=self._add_test)

        self.assertEqual(code, 1)
        self.assertIn("RunStoreContract.New_test", output)
        self.assertNotIn("A patch release cannot grow the public surface", output)

    def test_a_contract_test_plus_another_member_in_a_patch_release_is_red(self):
        def edit(repo):
            self._add_test(repo)
            self._add_member(repo)

        code, output = self._run(version="1.0.1", notes="- Added `RunStoreContract.New_test`.", edit=edit)

        self.assertEqual(code, 1)
        self.assertIn("Tracon.Abstractions: 1 line", output)
        self.assertNotIn("Testing.Contracts.Xunit: ", output)

    def test_a_required_member_becoming_optional_in_a_patch_release_is_red(self):
        def edit(repo):
            _write(repo, SNAPSHOT, "Tracon.Options.Name\n")

        code, output = self._run(
            version="1.0.1", notes="", edit=edit, snapshot="Tracon.Options.Name\nTracon.Options.Other\n")
        minor, minor_output = self._run(
            version="1.1.0", notes="", edit=edit, snapshot="Tracon.Options.Name\nTracon.Options.Other\n")

        self.assertEqual(code, 1)
        self.assertIn("A required member cannot become optional or go away in a patch release", output)
        self.assertIn("Tracon.Options.Other", output)
        self.assertEqual(minor, 0, minor_output)

    def test_a_member_added_in_a_minor_release_is_green(self):
        code, output = self._run(version="1.1.0", notes="", edit=self._add_member)

        self.assertEqual(code, 0, output)
        self.assertIn("Line: frozen 1.x (a break is rejected)", output)

    def test_a_member_added_on_the_preview_line_is_green(self):
        code, output = self._run(version="1.0.0-preview.6", baseline="1.0.0-preview.5", notes="", edit=self._add_member)

        self.assertEqual(code, 0, output)
        self.assertIn("Line: preview", output)

    def test_an_added_contract_test_missing_from_the_notes_is_red_with_the_missing_names(self):
        code, output = self._run(version="1.1.0", notes="- Other.", edit=self._add_test)

        self.assertEqual(code, 1)
        self.assertIn("The release notes do not name the added contract test", output)
        self.assertIn("RunStoreContract.New_test", output)

    def test_an_added_contract_test_named_in_the_notes_is_green(self):
        code, output = self._run(version="1.1.0", notes="- Added `RunStoreContract.New_test`.", edit=self._add_test)

        self.assertEqual(code, 0, output)
        self.assertIn("Contract tests added (since baseline v1.0.0): 1", output)

    def test_the_contract_gate_runs_on_the_preview_line_too(self):
        """The first real run must not be GA day."""
        code, output = self._run(version="1.0.0-preview.6", baseline="1.0.0-preview.5", notes="", edit=self._add_test)

        self.assertEqual(code, 1)
        self.assertIn("Line: preview", output)
        self.assertIn("RunStoreContract.New_test", output)

    def test_no_release_notes_at_all_is_red_and_the_message_says_contract(self):
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
        self.assertIn("A contract test was added but there are no release notes for '1.1.0'", output)

    def test_a_missing_baseline_tag_makes_the_gate_red_not_green(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory)
            reports = pathlib.Path(directory) / "reports"
            reports.mkdir()
            changelog = pathlib.Path(directory) / "CHANGELOG.md"
            changelog.write_text("# Changelog\n", encoding="utf-8")

            code, output = _quiet(lambda: bc.check(
                report_dir=reports, baseline="9.9.9", version="9.9.10", changelog=changelog, result_path=None, repo=repo))

        self.assertEqual(code, 1)
        self.assertIn("baseline tag 'v9.9.9'", output)

    def test_a_required_member_added_on_the_preview_line_is_named_in_the_notes(self):
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

    def test_a_required_member_added_on_a_frozen_line_is_red_even_with_notes(self):
        def edit(repo):
            _write(repo, SNAPSHOT, "Tracon.Options.Extra\nTracon.Options.Name\n")

        code, output = self._run(
            version="1.1.0", notes="- `Options` has a new `required` member `Extra`.", edit=edit, snapshot="Tracon.Options.Name\n")

        self.assertEqual(code, 1)
        self.assertIn("Options (new required member)", output)
        self.assertIn("the release notes cannot save it", output)

    def test_every_line_of_a_red_frozen_run_is_english(self):
        patch_code, patch_output = self._run(version="1.0.1", notes="", edit=self._add_member)

        def required(repo):
            _write(repo, SNAPSHOT, "Tracon.Options.Extra\nTracon.Options.Name\n")

        frozen_code, frozen_output = self._run(version="1.1.0", notes="", edit=required, snapshot="Tracon.Options.Name\n")

        self.assertEqual((1, 1), (patch_code, frozen_code))
        for output in (patch_output, frozen_output):
            self.assertIsNone(OutputLanguageTests.TURKISH_LETTERS.search(output), output)

    def test_every_line_of_a_green_run_is_english(self):
        code, output = self._run(version="1.1.0", notes="")

        self.assertEqual(0, code, output)
        self.assertIn("Break list: 0 types, 0 dropped TFMs, 0 packages", output)
        self.assertIsNone(OutputLanguageTests.TURKISH_LETTERS.search(output), output)

    def test_the_result_file_carries_the_line_and_the_contract_list(self):
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


LEGACY_SNAPSHOT = "docs/sozlesme/required-uyeler.txt"  # legacy-name


def _repo_with_old_snapshot(directory: str, *, old: str, new: str, tag: str = "v1.0.0-preview.8") -> pathlib.Path:
    """The move itself: the tag holds the snapshot at its old path, HEAD holds it at the current one."""
    repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-preview.7")
    _write(repo, LEGACY_SNAPSHOT, old)
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", "baseline with the old snapshot path")
    _git(repo, "tag", tag)
    (repo / LEGACY_SNAPSHOT).unlink()
    _write(repo, SNAPSHOT, new)
    _git(repo, "add", "-A")
    _git(repo, "commit", "-q", "-m", "move the snapshot")
    return repo


class LegacySnapshotPathTests(unittest.TestCase):
    """A tag up to `v1.0.0-preview.8` holds the `required` snapshot under the old directory name."""

    def test_a_tag_with_the_old_snapshot_path_is_compared_and_not_skipped(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _repo_with_old_snapshot(
                directory, old="Tracon.Options.Name\n", new="Tracon.Options.Extra\nTracon.Options.Name\n")

            delta = bc.surface_delta(repo, "1.0.0-preview.8")

        self.assertEqual(delta.required_types, {"Tracon.Abstractions": {"Options"}})
        self.assertEqual(delta.notes, [])

    def test_the_old_path_gives_the_same_delta_as_the_current_path(self):
        with tempfile.TemporaryDirectory() as old_directory, tempfile.TemporaryDirectory() as new_directory:
            from_old = _repo_with_old_snapshot(
                old_directory, old="Tracon.Options.Name\nTracon.Options.Other\n", new="Tracon.Options.Name\nTracon.Options.Extra\n")
            from_current = _baseline_repo(
                new_directory, with_snapshot="Tracon.Options.Name\nTracon.Options.Other\n", tag="v1.0.0-preview.8")
            _write(from_current, SNAPSHOT, "Tracon.Options.Name\nTracon.Options.Extra\n")

            old_delta = bc.surface_delta(from_old, "1.0.0-preview.8")
            current_delta = bc.surface_delta(from_current, "1.0.0-preview.8")

        self.assertEqual(current_delta.required_types, old_delta.required_types)
        self.assertEqual(current_delta.required_dropped, old_delta.required_dropped)
        self.assertEqual(["Tracon.Options.Other"], old_delta.required_dropped)

    def test_the_current_path_wins_when_the_tag_holds_both(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot="Tracon.Options.Name\n", tag="v1.0.0-preview.7")
            _write(repo, LEGACY_SNAPSHOT, "Tracon.Options.Stale\n")
            _git(repo, "add", "-A")
            _git(repo, "commit", "-q", "-m", "an old copy that must stay unread")
            _git(repo, "tag", "v1.0.0-preview.8")

            delta = bc.surface_delta(repo, "1.0.0-preview.8")

        self.assertEqual(delta.required_types, {})
        self.assertEqual(delta.required_dropped, [])

    def test_the_note_names_both_paths_when_the_tag_holds_neither(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-preview.5")

            delta = bc.surface_delta(repo, "1.0.0-preview.5")

        self.assertIn("docs/contracts/required-members.txt", delta.notes[0])
        self.assertIn(LEGACY_SNAPSHOT, delta.notes[0])

    def test_a_frozen_baseline_with_neither_path_is_an_error_that_names_both(self):
        with tempfile.TemporaryDirectory() as directory:
            repo = _baseline_repo(directory, with_snapshot=None, tag="v1.0.0-rc.1")

            with self.assertRaises(bc.SurfaceError) as raised:
                bc.surface_delta(repo, "1.0.0-rc.1")

        self.assertIn("docs/contracts/required-members.txt", str(raised.exception))
        self.assertIn(LEGACY_SNAPSHOT, str(raised.exception))


class OutputLanguageTests(unittest.TestCase):
    """Everything the gate prints is English: a contributor of the public repository reads it."""

    # Written as code points so that this source file holds no Turkish letter itself.
    TURKISH_LETTERS = re.compile("[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]")

    def test_the_release_line_texts_are_english(self):
        lines = [
            bc.release_line("1.0.0-preview.5", None).describe(),
            bc.release_line("1.0.0-rc.1", "2.0.0").describe(),
            bc.release_line("1.0.0", "1.1.0").describe(),
            bc.release_line("1.0.0", "1.0.1").describe(),
        ]

        self.assertTrue(all(line.startswith("Line: ") for line in lines), lines)
        for line in lines:
            self.assertIsNone(self.TURKISH_LETTERS.search(line), line)

    def test_the_help_text_names_the_english_flags_only(self):
        output = io.StringIO()
        with contextlib.redirect_stdout(output), self.assertRaises(SystemExit):
            bc.main(["--help"])

        for flag in ("--report-directory", "--base", "--package-version", "--repository"):
            self.assertIn(flag, output.getvalue())
        self.assertIsNone(self.TURKISH_LETTERS.search(output.getvalue()), output.getvalue())

    def test_the_old_flag_names_are_gone(self):
        with contextlib.redirect_stderr(io.StringIO()), self.assertRaises(SystemExit) as raised:
            bc.main(["--rapor-dizini", ".", "--" "taban", "1.0.0"])

        self.assertEqual(2, raised.exception.code)

    def test_a_missing_report_directory_is_reported_in_english(self):
        with tempfile.TemporaryDirectory() as directory:
            missing = pathlib.Path(directory) / "missing"

            code, output = _quiet(lambda: bc.main(["--report-directory", str(missing), "--base", "1.0.0-preview.2"]))

        self.assertEqual(1, code)
        self.assertIn("The report directory does not exist", output)
        self.assertIsNone(self.TURKISH_LETTERS.search(output), output)


class RepositoryGuardTests(unittest.TestCase):
    def test_the_repository_has_no_compatibility_suppressions_file(self):
        """A report written into the source tree is read back as a suppression
        input by every later pack and hides the break for good."""
        found = sorted(str(path.relative_to(ROOT)) for path in (ROOT / "src").rglob("CompatibilitySuppressions.xml"))

        self.assertEqual(found, [])


if __name__ == "__main__":
    unittest.main()
