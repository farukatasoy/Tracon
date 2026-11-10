#!/usr/bin/env python3
"""Tests for the single validation gate runner."""
from __future__ import annotations

import contextlib
import dataclasses
import datetime
import gzip
import importlib.util
import io
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import threading
import time
import types
import unittest
import xml.etree.ElementTree as ElementTree
import zipfile
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("gate", ROOT / "scripts" / "gate.py")
gate = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = gate
spec.loader.exec_module(gate)


def _write_src_build_props(root: pathlib.Path, frameworks: str = "net10.0") -> None:
    """Writes the default TFM list of the packages; `_target_frameworks` reads it from here."""
    props = root / "src" / "Directory.Build.props"
    props.parent.mkdir(parents=True, exist_ok=True)
    props.write_text(
        f"<Project><PropertyGroup><TargetFrameworks>{frameworks}</TargetFrameworks></PropertyGroup></Project>\n",
        encoding="utf-8")


def _ci_jobs(text: str) -> dict[str, dict]:
    """The jobs of `ci.yml`: {name: {"if": value | None, "needs": [jobs]}}. No YAML library is needed.

    A block-style `if: >-` value is gathered from the more deeply indented lines that follow.
    """
    jobs: dict[str, dict] = {}
    current = None
    inside = False
    block_if = False
    for line in text.splitlines():
        if re.match(r"^jobs:\s*$", line):
            inside = True
            continue
        if not inside:
            continue
        if block_if:
            if not line.strip() or re.match(r"^ {5,}", line):
                jobs[current]["if"] = f"{jobs[current]['if']} {line.strip()}".strip()
                continue
            block_if = False
        job_line = re.match(r"^  ([A-Za-z0-9_-]+):\s*$", line)
        if job_line:
            current = job_line.group(1)
            jobs[current] = {"if": None, "needs": []}
            continue
        if current is None:
            continue
        prop = re.match(r"^    (if|needs):\s*(.*?)\s*$", line)
        if not prop:
            continue
        if prop.group(1) == "if":
            block_if = re.fullmatch(r"[|>][+-]?", prop.group(2)) is not None
            jobs[current]["if"] = "" if block_if else prop.group(2)
        else:
            jobs[current]["needs"] = [name for name in re.findall(r"[A-Za-z0-9_-]+", prop.group(2))]
    return jobs


CANONICAL_REPO_CONDITION = "github.repository == 'farukatasoy/Tracon'"
# When one of these functions sits in a job-level `if:`, a skipped dependency does not skip the job.
STATUS_FUNCTION = re.compile(r"\b(?:always|failure|cancelled)\(\)")


def _unguarded_jobs(jobs: dict[str, dict]) -> list[str]:
    """Jobs that could run on forks or other repositories, with the reason.

    The condition sits ONLY in `build`; every other job depends on it (directly or through a chain)
    with `needs`, and a skipped dependency skips them too. A status function breaks that chain:
    such a job must carry the repository condition itself.
    """
    problems = []
    for name, job in jobs.items():
        if name == "build":
            continue
        pending, visited = [name], set()
        while pending:
            current = pending.pop()
            if current in visited:
                continue
            visited.add(current)
            pending.extend(jobs[current]["needs"])
        if "build" not in visited:
            problems.append(f"`{name}` job does not depend on `build`; it runs unconditionally")
        condition = job["if"] or ""
        # A condition joined with `||` bypasses the guard: `always() || <condition>` is true in every repository.
        if STATUS_FUNCTION.search(condition) and (CANONICAL_REPO_CONDITION not in condition or "||" in condition):
            problems.append(
                f"`{name}` job's `if:` carries a status function; the `needs` chain may not skip it, "
                "so it must carry the repository condition itself")
    return problems


# The jobs of `ci.yml` inherit the repository condition through `build` (`_unguarded_jobs`). Every job of a
# workflow file that is not chained to it -- the weekly fuzz, the SDK conformance job -- must carry the condition itself.
UNCHAINED_EXCLUDED_FILES = {"ci.yml", "ayna.yml"}


def _unchained_unguarded_jobs(jobs: dict[str, dict]) -> list[str]:
    """Jobs that do not carry the repository condition themselves. `||` bypasses the guard: it is true in every repository."""
    problems = []
    for name, job in jobs.items():
        condition = job["if"] or ""
        if CANONICAL_REPO_CONDITION not in condition or "||" in condition:
            problems.append(f"`{name}` job does not carry the canonical repository condition itself")
    return problems



def _job_block(text: str, name: str) -> str:
    """The lines of job `ad`: from its two-space-indented job header up to the next job header."""
    lines = text.splitlines()
    start = next(i for i, line in enumerate(lines) if re.match(rf"^  {re.escape(name)}:\s*$", line))
    son = next(
        (i for i in range(start + 1, len(lines)) if re.match(r"^  [A-Za-z0-9_-]+:\s*$", lines[i])),
        len(lines))
    return "\n".join(lines[start:son])


def _matrix_entries(block: str) -> list[dict[str, str]]:
    """The `include:` entries of a job: [{key: value}]. Comment lines and quotes are dropped."""
    entries: list[dict[str, str]] = []
    inside = False
    for line in block.splitlines():
        if re.match(r"^\s+include:\s*$", line):
            inside = True
            continue
        if not inside or not line.strip() or line.lstrip().startswith("#"):
            continue
        if not re.match(r"^ {10,}", line):
            break
        entry = re.match(r"^\s*(-\s+)?([A-Za-z_][\w-]*):\s*(.*?)\s*$", line)
        if not entry:
            continue
        value = entry.group(3)
        value = value[1:-1] if len(value) >= 2 and value[0] == value[-1] and value[0] in "'\"" else value
        if entry.group(1):
            entries.append({})
        entries[-1][entry.group(2)] = value
    return entries


def _matrix_link_problems(text: str, default_image: dict[str, str]) -> list[str]:
    """Whether the name, environment variable and image of the platform legs are tied to each other.

    A leg is only a set of environment variables. When the tie breaks (a typo, a deleted key, a wrong image)
    the leg runs with the default image and stays green without testing the version its name claims.
    """
    problems = []
    for job_name in ("postgres-matrix", "sqlserver-matrix", "sqlite-floor", "culture-matrix"):
        block = _job_block(text, job_name)
        entries = _matrix_entries(block)
        if not entries:
            problems.append(f"the matrix of job `{job_name}` could not be read")
            continue
        for key in sorted(set(re.findall(r"\$\{\{\s*matrix\.([A-Za-z_][\w-]*)\s*\}\}", block))):
            for entry in entries:
                if key not in entry:
                    problems.append(
                        f"`{job_name}`: `matrix.{key}` is used but entry {entry} has no `{key}`")
        for entry in entries:
            name = entry.get("name", "")
            image = entry.get("image", "")
            if job_name == "postgres-matrix":
                expected = int(re.match(r"\d+", name).group())
                effective = image or default_image["postgres"]
                matched = re.search(r"[:-]pg(\d+)(?:-|$)", effective)
                if not matched or int(matched.group(1)) != expected:
                    problems.append(f"`{job_name}` cell `{name}`: image `{effective}` does not name version {expected}")
            elif job_name == "sqlserver-matrix":
                expected = int(name[:4])
                effective = image or default_image["sqlserver"]
                matched = re.search(r":(\d{4})(?:-|$)", effective)
                if not matched or int(matched.group(1)) != expected:
                    problems.append(f"`{job_name}` cell `{name}`: image `{effective}` does not name version {expected}")
            elif job_name == "sqlite-floor" and not entry.get("sqlite"):
                problems.append(f"`{job_name}`: the `sqlite` version is empty; the version test is skipped on an empty variable")
    return problems


def _ci_steps(block: str) -> list[str]:
    """The steps of a job block: texts split at the six-space-indented lines that start with `- `."""
    return re.split(r"(?m)^(?=      - )", block)[1:]


def _ci_silent_green_problems(text: str) -> list[str]:
    """The three places where a red run, or a run that executed no tests, could look green.

    1. A job that uses `steps.restore` defines the `id: restore` step BEFORE the first use. If it is not
       defined, `steps.restore.outcome` stays empty, every test step is silently skipped and the job ends green.
    2. The TRX upload carries `if-no-files-found: error` (the job turns red if the test steps are skipped) and
       `overwrite: true` (so an infrastructure retry does not get a 409 on the second upload).
    3. The `nuget-packages` and `bin-*` uploads do NOT carry `overwrite`: re-running the job must fail with a
       409 on the second upload; otherwise the bytes the earlier jobs tested would change silently.
    """
    problems = []
    for name in _ci_jobs(text):
        steps = _ci_steps(_job_block(text, name))
        first_use = next(
            (i for i, step in enumerate(steps) if re.search(r"(?m)^\s+if:.*steps\.restore", step)), None)
        if first_use is not None:
            definition = next(
                (i for i, step in enumerate(steps) if re.search(r"(?m)^\s+id:\s*restore\s*$", step)), None)
            if definition is None or definition > first_use:
                problems.append(f"`{name}`: `steps.restore` is used but no `id: restore` step precedes it")
        for step in steps:
            if "actions/upload-artifact@" not in step:
                continue
            artifact = re.search(r"(?m)^\s+name:\s*(.+?)\s*$", step)
            path = re.search(r"(?m)^\s+path:\s*(\S+)\s*$", step)
            if path and path.group(1).endswith(".trx"):
                if not re.search(r"(?m)^\s+if-no-files-found:\s*error\s*$", step):
                    problems.append(f"`{name}`: the TRX upload does not carry `if-no-files-found: error`")
                if not re.search(r"(?m)^\s+overwrite:\s*true\s*$", step):
                    problems.append(f"`{name}`: the TRX upload does not carry `overwrite: true`")
            if artifact and (artifact.group(1) == "nuget-packages" or artifact.group(1).startswith("bin-")):
                if re.search(r"(?m)^\s+overwrite:", step):
                    problems.append(f"`{name}`: the `{artifact.group(1)}` upload carries `overwrite`")
    return problems


def _default_images() -> dict[str, str]:
    """The image the fixtures use when the variable is empty: an empty `image:` value of a leg means this image."""
    read_images = {}
    for name, path in (
        ("postgres", ROOT / "tests" / "Tracon.PostgreSql.IntegrationTests" / "Infrastructure" / "PostgresFixture.cs"),
        ("sqlserver", ROOT / "tests" / "Tracon.SqlServer.IntegrationTests" / "Infrastructure" / "SqlServerFixture.cs"),
    ):
        hit = re.search(r'const string DefaultImage = "([^"]+)";', path.read_text(encoding="utf-8"))
        read_images[name] = hit.group(1) if hit else ""
    return read_images


class GateTests(unittest.TestCase):
    def test_git_checks_out_all_text_files_with_lf(self):
        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")

        self.assertIn("* text=auto eol=lf", attributes.splitlines())

    def test_first_failure_stops_expensive_gates(self):
        commands = [gate.Command(("first",)), gate.Command(("second",))]
        runner = mock.Mock(side_effect=[mock.Mock(returncode=1), mock.Mock(returncode=0)])
        with tempfile.TemporaryDirectory() as directory:
            result = gate.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 1)
        self.assertEqual(runner.call_count, 1)

    def test_repeated_class_flag_accumulates_patterns(self):
        """`--class A --class B` ran only B (argparse writes the second over the
        first); both architecture tests were believed to have run."""
        with mock.patch.object(gate, "test_commands", side_effect=ValueError("stop")) as commands:
            with contextlib.redirect_stdout(io.StringIO()):
                gate.main(["test", "--project", "Tracon.Core.UnitTests", "--class", "*A*", "--class", "*B*", "*C*"])

        self.assertEqual(commands.call_args.args[1], ["*A*", "*B*", "*C*"])

    def test_mtp_filter_emits_filter_class(self):
        commands = gate.test_commands("Tracon.Generators.UnitTests", ["*Capability*"], environ={})

        self.assertEqual(len(commands), 1)
        self.assertIn("--filter-class", commands[0].args)
        self.assertNotIn("--filter", commands[0].args)
        # Compared by parts, NOT by separator: on Windows `pathlib` produces a backslash,
        # `endswith("/...")` ALWAYS returns False there and the gate turned red only on the
        # `windows-latest` leg (the production code was right).
        parts = pathlib.PurePath(commands[0].args[0]).parts[-3:]
        self.assertEqual(
            parts, ("Tracon.Generators.UnitTests", "release", "Tracon.Generators.UnitTests"))

    def test_multi_tfm_project_runs_each_leg_separately(self):
        # The output of a multi-target project is release_<tfm>/, NOT release/. The packages
        # target a single TFM today; the infrastructure exercises a second TFM through an environment variable.
        self.assertEqual(
            [pathlib.PurePath(command.args[0]).parts[-2]
             for command in gate.test_commands("Tracon.Core.UnitTests", ["*Capability*"], environ={})],
            ["release_net10.0"])
        self.assertEqual(
            [pathlib.PurePath(command.args[0]).parts[-2]
             for command in gate.test_commands("Tracon.Core.UnitTests", ["*Capability*"], environ={"TraconTestTargetFrameworks": "net10.0;net11.0"})],
            ["release_net10.0", "release_net11.0"])

    def test_tfm_selection_runs_a_single_leg(self):
        commands = gate.test_commands("Tracon.Core.UnitTests", ["*X*"], "net11.0", environ={"TraconTestTargetFrameworks": "net10.0;net11.0"})

        self.assertEqual(len(commands), 1)
        self.assertEqual(
            pathlib.PurePath(commands[0].args[0]).parts[-3:],
            ("Tracon.Core.UnitTests", "release_net11.0", "Tracon.Core.UnitTests"))

    def test_requesting_an_unbuilt_tfm_is_not_skipped_silently(self):
        with self.assertRaises(ValueError):
            gate.test_commands("Tracon.Core.UnitTests", ["*X*"], "net7.0", environ={})
        with self.assertRaises(ValueError):
            # A single-target project builds only net10.0; asking for another TFM
            # would run a stale or non-existent binary.
            gate.test_commands("Tracon.Generators.UnitTests", ["*X*"], "net11.0", environ={})

    def test_representative_set_and_tfm_list_are_read_from_the_props_file(self):
        self.assertEqual(gate.test_target_frameworks(environ={}), ("net10.0",))
        self.assertEqual(gate.single_test_target_framework(), "net10.0")
        self.assertEqual(
            sorted(gate.multi_target_test_projects()),
            sorted([
                "Tracon.Core.UnitTests",
                "Tracon.Sql.Shared.UnitTests",
                "Tracon.OpenAI.UnitTests",
                "Tracon.Anthropic.UnitTests",
                "Tracon.Google.UnitTests",
                "Tracon.Azure.UnitTests",
                "Tracon.Sqlite.IntegrationTests",
                "Tracon.Testing.Contracts.Xunit.UnitTests",
            ]))
        # Every name outside the set is a real test project - a typo
        # silently makes the MSBuild condition false and the project stays on net10.
        for project in gate.multi_target_test_projects():
            csproj = ROOT / "tests" / project / f"{project}.csproj"
            self.assertTrue(csproj.exists(), project)
            # The csproj body is read AFTER the props; a single <TargetFramework>
            # written there silently drops the project to net10 and the runtime
            # sentinel still passes on net10.
            self.assertNotIn("<TargetFramework", csproj.read_text(encoding="utf-8"), project)

    def test_tfm_list_is_read_from_the_environment_variable(self):
        # The environment variable of the same name reaches MSBuild as an initial property;
        # the script must look for the same outputs.
        self.assertEqual(gate.test_target_frameworks(environ={"TraconTestTargetFrameworks": "net10.0;net11.0"}), ("net10.0", "net11.0"))
        self.assertEqual(
            [path.name for path in gate.test_output_directories("Tracon.Core.UnitTests", environ={"TraconTestTargetFrameworks": "net10.0;net11.0"})],
            ["release_net10.0", "release_net11.0"])

    def test_stale_single_target_folder_is_not_selected(self):
        # After a project becomes multi-target, the old release/ folder stays on disk;
        # resolution goes by the DECLARATION, not by the DISK.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "tests").mkdir()
            (root / "tests" / "Directory.Build.props").write_text(
                (ROOT / "tests" / "Directory.Build.props").read_text(encoding="utf-8"), encoding="utf-8")
            (root / "artifacts" / "bin" / "Tracon.Core.UnitTests" / "release").mkdir(parents=True)

            outputs = gate.test_output_directories("Tracon.Core.UnitTests", root=root, environ={})

        self.assertNotIn("release", [path.name for path in outputs])

    def test_sync_scan_finds_file_and_directory_copies(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "tests").mkdir()
            (root / "tests" / "Example 2.cs").write_text("", encoding="utf-8")
            (root / "tests" / "resources 2").mkdir()

            found = gate.find_sync_copies(root)

        self.assertEqual({path.name for path in found}, {"Example 2.cs", "resources 2"})

    def test_secret_scan_finds_a_value_line(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "sample.txt").write_text(
                "Pass" + "word=not-a-real-secret-value\n", encoding="utf-8")

            found, skipped = gate.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertIn("sample.txt:1", found[0])
        self.assertEqual(skipped, 0)

    def test_stale_doc_reference_is_found(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "docs").mkdir()
            (root / "docs" / "HERE.md").write_text("", encoding="utf-8")
            (root / "src").mkdir()
            # Written in pieces: this file lives under `scripts/` and falls into the gate's OWN
            # scan; a fixture path written plainly would turn the gate red on its own test
            # source (the same reason and the same method as in the `find_secrets` test).
            missing = "docs/" + "GONE.md"
            present = "docs/" + "HERE.md"
            (root / "src" / "Thing.cs").write_text(
                f"// See {missing} and {present}\n", encoding="utf-8")

            found = gate.find_stale_doc_references(root)

        self.assertEqual(len(found), 1)
        self.assertIn(missing, found[0])

    def test_site_path_is_not_a_stale_reference(self):
        # `docs-site/src/content/docs/...` is a SITE path and does not belong to the `docs/` tree.
        # If it were not told apart, the gate would produce 40+ false positives and be switched off.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "src").mkdir()
            (root / "src" / "Thing.cs").write_text(
                "/// <c>docs-site/src/content/docs/capabilities.md</c>\n", encoding="utf-8")

            self.assertEqual(gate.find_stale_doc_references(root), [])

    def test_placeholder_reference_is_not_stale(self):
        # Scripts and templates write example paths; these never exist
        # and are not a defect.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "scripts").mkdir()
            (root / "scripts" / "example.py").write_text(
                "# docs" "/X.md -> docs" "/old/Y.md; template: docs" "/NN-UPPER-CASE-NAME.md\n",
                encoding="utf-8")

            self.assertEqual(gate.find_stale_doc_references(root), [])

    def test_repo_has_no_stale_doc_references(self):
        # CLASS GATE: the scan that is real in this repository. When a document was deleted
        # (it used to move to an archive first), other files that mentioned it were once left
        # un-updated (43 stale references were measured).
        self.assertEqual(gate.find_stale_doc_references(), [])

    def test_unproven_public_type_breaks_the_scan_gate(self):
        # `public-surface-inventory.py --check` ran in no gate; the strictness of the rule
        # loosened silently twice (two unproven exceptions).
        runner = mock.Mock(side_effect=[
            mock.Mock(returncode=1, stdout="", stderr="audit: 1 unproven type, 0 stale reasons\n"),
            mock.Mock(returncode=0, stdout="Tracon.Abstractions\tTracon.SampleType\tclass\tunproven\n", stderr=""),
        ])
        with mock.patch.object(gate.subprocess, "run", runner):
            found = gate.public_surface_violations()

        self.assertTrue(any("Tracon.SampleType" in line for line in found), found)
        self.assertIn("public type without evidence", "\n".join(found))

    def test_the_inventory_gets_flags_and_a_class_it_accepts(self):
        # The real script must accept the exact class name. The `--check` call runs for real in
        # `test_repo_public_surface_has_no_unproven_type`; the `--list` call runs only when the audit is red.
        runner = mock.Mock(side_effect=[
            mock.Mock(returncode=1, stdout="", stderr="audit: 1 unproven type, 0 stale reasons\n"),
            mock.Mock(returncode=0, stdout="", stderr=""),
        ])
        with mock.patch.object(gate.subprocess, "run", runner):
            gate.public_surface_violations()

        commands = [call.args[0] for call in runner.call_args_list]
        self.assertEqual([command[2:] for command in commands], [["--check"], ["--list", "unproven"]])
        real = subprocess.run(commands[1], capture_output=True, text=True, encoding="utf-8", check=False)
        self.assertNotEqual(real.returncode, 2, real.stderr)

    def test_clean_inventory_does_not_break_the_scan_gate(self):
        runner = mock.Mock(return_value=mock.Mock(returncode=0, stdout="", stderr=""))
        with mock.patch.object(gate.subprocess, "run", runner):
            self.assertEqual(gate.public_surface_violations(), [])

    def test_scan_returns_red_for_an_unproven_public_type(self):
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(gate, "public_surface_violations", return_value=["Tracon.SampleType: unproven"]), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            result = gate.scan(pathlib.Path(directory))

        self.assertEqual(result, 1)
        self.assertIn("Tracon.SampleType", output.getvalue())

    def test_repo_public_surface_has_no_unproven_type(self):
        # CLASS GATE: the check that is real in this repository.
        self.assertEqual(gate.public_surface_violations(), [])

    # The Context7 versioned identity is derived from the first tag of `previousVersions`. If the tag falls
    # behind the latest release, agents read the text of an old version instead of the installed package's.

    def _context7_root(self, directory: str, config: object, changelog_text: str | None) -> pathlib.Path:
        root = pathlib.Path(directory)
        text = config if isinstance(config, str) else json.dumps(config)
        (root / "context7.json").write_text(text, encoding="utf-8")
        if changelog_text is not None:
            (root / "CHANGELOG.md").write_text(changelog_text, encoding="utf-8")
        return root

    _CHANGELOG = "## [Unreleased]\n\n### Added\n- in progress\n\n## [1.0.0-preview.6] - 2026-10-27\n"

    def test_context7_tag_equal_to_latest_release_is_clean(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_root(
                directory, {"previousVersions": [{"tag": "v1.0.0-preview.6"}]}, self._CHANGELOG)

            self.assertEqual(gate.context7_version_drift(root), [])

    def test_context7_tag_is_also_read_as_a_string(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_root(directory, {"previousVersions": ["v1.0.0-preview.6"]}, self._CHANGELOG)

            self.assertEqual(gate.context7_version_drift(root), [])

    def test_context7_tag_behind_latest_release_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_root(
                directory, {"previousVersions": [{"tag": "v1.0.0-preview.5"}]}, self._CHANGELOG)

            found = gate.context7_version_drift(root)

        self.assertEqual(len(found), 1, found)
        self.assertIn("v1.0.0-preview.5", found[0])
        self.assertIn("v1.0.0-preview.6", found[0])

    def test_context7_empty_version_list_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_root(directory, {"previousVersions": []}, self._CHANGELOG)

            self.assertEqual(len(gate.context7_version_drift(root)), 1)

    def test_context7_malformed_json_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_root(directory, "{ broken", self._CHANGELOG)

            self.assertEqual(len(gate.context7_version_drift(root)), 1)

    # The upgrade gate's baseline must be the latest release: the gate moves the latest release's database to today's migrations.

    def _baseline_root(self, directory: str, manifest: object, changelog_text: str | None) -> pathlib.Path:
        root = pathlib.Path(directory)
        target = root / gate.UPGRADE_BASELINE_MANIFEST
        target.parent.mkdir(parents=True)
        target.write_text(manifest if isinstance(manifest, str) else json.dumps(manifest), encoding="utf-8")
        if changelog_text is not None:
            (root / "CHANGELOG.md").write_text(changelog_text, encoding="utf-8")
        return root

    def test_upgrade_baseline_equal_to_latest_release_is_clean(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._baseline_root(directory, {"version": "1.0.0-preview.6"}, self._CHANGELOG)

            self.assertEqual(gate.upgrade_baseline_drift(root), [])

    def test_upgrade_baseline_behind_latest_release_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._baseline_root(directory, {"version": "1.0.0-preview.5"}, self._CHANGELOG)

            found = gate.upgrade_baseline_drift(root)

        self.assertEqual(len(found), 1, found)
        self.assertIn("1.0.0-preview.5", found[0])
        self.assertIn("1.0.0-preview.6", found[0])

    def test_upgrade_baseline_malformed_or_versionless_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._baseline_root(directory, "{ broken", self._CHANGELOG)
            self.assertEqual(len(gate.upgrade_baseline_drift(root)), 1)

        with tempfile.TemporaryDirectory() as directory:
            root = self._baseline_root(directory, {"tag": "v1"}, self._CHANGELOG)
            self.assertEqual(len(gate.upgrade_baseline_drift(root)), 1)

    def test_upgrade_baseline_without_a_dated_release_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._baseline_root(directory, {"version": "1.0.0-preview.6"}, "## [Unreleased]\n")

            self.assertEqual(len(gate.upgrade_baseline_drift(root)), 1)

    def test_repo_upgrade_baseline_is_the_latest_release(self):
        # CLASS GATE: the check that is real in this repository.
        self.assertEqual(gate.upgrade_baseline_drift(), [])

    def test_context7_without_a_dated_release_breaks(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_root(
                directory, {"previousVersions": [{"tag": "v1.0.0-preview.6"}]}, "## [Unreleased]\n")

            self.assertEqual(len(gate.context7_version_drift(root)), 1)

    def test_missing_context7_file_skips_the_check(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(gate.context7_version_drift(pathlib.Path(directory)), [])

    def test_scan_turns_red_on_context7_drift_alone(self):
        # The others are empty: only the tag tells the outcomes apart; if they were not mocked, `scan` would
        # return 1 in both cases and the test would prove nothing.
        empty = {
            "find_sync_copies": [], "find_secrets": ([], 0), "find_stale_doc_references": [],
            "public_surface_violations": [],
        }
        for tag, expected in (("v1.0.0-preview.6", 0), ("v1.0.0-preview.5", 1)):
            with self.subTest(tag=tag), tempfile.TemporaryDirectory() as directory, \
                    contextlib.ExitStack() as stack, contextlib.redirect_stdout(io.StringIO()) as output:
                for name, value in empty.items():
                    stack.enter_context(mock.patch.object(gate, name, return_value=value))
                root = self._context7_root(directory, {"previousVersions": [{"tag": tag}]}, self._CHANGELOG)
                result = gate.scan(root)

            self.assertEqual(result, expected, output.getvalue())
            self.assertEqual("v1.0.0-preview.5" in output.getvalue(), expected == 1)

    def _empty_scan(self, stack: contextlib.ExitStack, directory: str) -> pathlib.Path:
        """Empties the public finding classes of `scan`; only the extension's check then decides the result."""
        for name, value in {
            "find_sync_copies": [], "find_secrets": ([], 0), "find_stale_doc_references": [],
            "public_surface_violations": [], "context7_version_drift": [], "upgrade_baseline_drift": [],
        }.items():
            stack.enter_context(mock.patch.object(gate, name, return_value=value))
        return pathlib.Path(directory)

    def test_scan_turns_red_with_the_extension_finding_class_title_and_violations(self):
        extension = gate.Extension(scan_checks=(
            gate.ScanCheck("clean class", lambda root: []),
            gate.ScanCheck("dirty class", lambda root: ["a.cs:3: trace", "b.cs:4: trace"]),
        ))
        with tempfile.TemporaryDirectory() as directory, contextlib.ExitStack() as stack, \
                contextlib.redirect_stdout(io.StringIO()) as output:
            result = gate.scan(self._empty_scan(stack, directory), extension)

        self.assertEqual(1, result)
        self.assertIn("Scan: ❌ dirty class", output.getvalue())
        self.assertIn("  a.cs:3: trace", output.getvalue())
        self.assertNotIn("clean class", output.getvalue())

    def test_scan_is_green_when_extension_checks_are_clean_and_passes_the_root(self):
        observed = []
        extension = gate.Extension(scan_checks=(gate.ScanCheck("class", lambda root: observed.append(root) or []),))
        with tempfile.TemporaryDirectory() as directory, contextlib.ExitStack() as stack, \
                contextlib.redirect_stdout(io.StringIO()) as output:
            root = self._empty_scan(stack, directory)
            result = gate.scan(root, extension)

        self.assertEqual(0, result, output.getvalue())
        self.assertEqual([root], observed)

    def test_public_root_without_extension_runs_no_extension_finding_class(self):
        self.assertEqual((), gate.PUBLIC.scan_checks)

    def test_scan_truncates_a_long_extension_violation_list_at_twenty(self):
        extension = gate.Extension(scan_checks=(gate.ScanCheck("class", lambda root: [f"d{i}.cs" for i in range(25)]),))
        with tempfile.TemporaryDirectory() as directory, contextlib.ExitStack() as stack, \
                contextlib.redirect_stdout(io.StringIO()) as output:
            gate.scan(self._empty_scan(stack, directory), extension)

        self.assertIn("  d19.cs", output.getvalue())
        self.assertNotIn("d20.cs", output.getvalue())
        self.assertIn("… +5", output.getvalue())

    def test_repo_context7_version_matches_the_latest_release(self):
        # CLASS GATE: the check that is real in this repository.
        self.assertEqual(gate.context7_version_drift(), [])

    def test_map_gap_falls_back_to_the_full_test_run(self):
        projects, full = gate.affected_test_projects(["src/Unknown.Package/Thing.cs"])

        self.assertEqual(projects, ["Tracon.Generators.UnitTests"])
        self.assertTrue(full)

    def test_every_src_change_selects_the_example_compiling_project(self):
        # ESCAPE: an XML <example> block added to src/Tracon.Core made
        # Tracon.Generators.UnitTests fail to BUILD; the inner loop selected
        # Core.UnitTests + AspNetCore.FunctionalTests and never ran the project
        # that was actually failing. ExampleExtractor reads all of
        # src/**/*.cs, not only the Generators.
        projects, full = gate.affected_test_projects(
            ["src/Tracon.Core/Builder/ITraconBuilder.cs"])

        self.assertIn("Tracon.Generators.UnitTests", projects)
        self.assertFalse(full)

    def test_shared_provider_source_selects_the_four_provider_projects(self):
        # src/Tracon.Providers.Shared is not a package; without a map entry
        # every change would fall back to a full run. Its blast radius is four projects.
        for path in ("src/Tracon.Providers.Shared/ModelProviderCore.cs",
                     "tests/Shared/Providers/ModelProviderCoreTests.cs"):
            projects, full = gate.affected_test_projects([path])

            for project in gate.PROVIDER_TEST_PROJECTS:
                self.assertIn(project, projects, path)
            self.assertFalse(full, path)

    def test_shared_source_compiled_into_packages_also_selects_the_options_project(self):
        # Tracon.PackageOptions.UnitTests drives the settings section of every provider, Mcp, audio, workflow and SQL
        # package through the real container. Tracon.Providers.Shared and Tracon.Sql.Shared are shared source
        # compiled into those packages; with that project missing from the map, a registration or validation change
        # would run the options gate only in a full run.
        for path in ("src/Tracon.Providers.Shared/ModelProviderCore.cs",
                     "src/Tracon.Sql.Shared/Stores/SqlJobStore.cs"):
            projects, full = gate.affected_test_projects([path])

            self.assertIn("Tracon.PackageOptions.UnitTests", projects, path)
            self.assertFalse(full, path)

    def test_upgrade_gate_shared_source_selects_four_projects_without_a_full_run(self):
        for path in ("tests/Shared/Upgrade/UpgradeSeed.json", "tests/Shared/Upgrade/UpgradeProbes.cs"):
            projects, full = gate.affected_test_projects([path])

            for project in gate.UPGRADE_GATE_TEST_PROJECTS:
                self.assertIn(project, projects, path)
            self.assertFalse(full, path)

    def test_openai_shared_source_selects_the_two_provider_projects(self):
        # src/Tracon.OpenAI.Shared is not a package; it is compiled into the OpenAI and Azure
        # packages. Without a map entry every change would fall back to a full run.
        projects, full = gate.affected_test_projects(
            ["src/Tracon.OpenAI.Shared/OpenAIPipelineImageGenerator.cs"])

        self.assertIn("Tracon.OpenAI.UnitTests", projects)
        self.assertIn("Tracon.Azure.UnitTests", projects)
        self.assertFalse(full)

    def test_contracts_package_and_sql_shared_source_do_not_fall_back_to_a_full_run(self):
        # 2026-10-13: Tracon.Testing.Contracts.Xunit and
        # Tracon.Sql.Shared were missing from the map; the inner loop widened to a full run on
        # every change, and because that run was built without the UI, all of
        # E2E dropped out. The blast radius is the test projects that compile the package or
        # derive the contract.
        sql_tests = ("Tracon.PostgreSql.IntegrationTests",
                     "Tracon.SqlServer.IntegrationTests",
                     "Tracon.Sqlite.IntegrationTests")
        expectations = {
            "src/Tracon.Testing.Contracts.Xunit/Contracts/JobStoreContract.cs": (
                "Tracon.Testing.Contracts.Xunit.UnitTests", "Tracon.Core.UnitTests",
                *gate.PROVIDER_TEST_PROJECTS, *sql_tests),
            "src/Tracon.Sql.Shared/Stores/SqlJobStore.cs": (
                "Tracon.Sql.Shared.UnitTests", *sql_tests),
        }
        for path, expected in expectations.items():
            projects, full = gate.affected_test_projects([path])

            self.assertFalse(full, path)
            for project in expected:
                self.assertIn(project, projects, path)
                self.assertTrue((ROOT / "tests" / project / f"{project}.csproj").exists(), project)

    def test_sql_provider_selects_the_shared_parity_gates(self):
        # The registration parity (SqlProviderRegistrationParityTests), the migration
        # parity and the SQL text snapshot live in Tracon.Sql.Shared.UnitTests.
        # For a change touching a single Use* file the inner loop used to select only that
        # provider's IntegrationTests project; a forgotten registration line stays silent there.
        for package in ("Tracon.PostgreSql", "Tracon.SqlServer", "Tracon.Sqlite"):
            path = f"src/{package}/{package.replace('.', '')}BuilderExtensions.cs"
            projects, full = gate.affected_test_projects([path])

            self.assertIn("Tracon.Sql.Shared.UnitTests", projects, path)
            self.assertIn(f"{package}.IntegrationTests", projects, path)
            self.assertFalse(full, path)

    def test_core_change_also_selects_the_sql_registration_gate(self):
        # The two cases the registration gate catches arise only in Core/Abstractions:
        # a new store contract forgotten in all three providers at once, and an Auditing*
        # decorator added only to AddTracon. If the inner loop does not run the gate on such a
        # change, the finding shows up only in a full run.
        for path in ("src/Tracon.Core/TraconServiceCollectionExtensions.Registration.Storage.cs",
                     "src/Tracon.Abstractions/Runs/IRunStore.cs"):
            projects, full = gate.affected_test_projects([path])

            self.assertIn("Tracon.Sql.Shared.UnitTests", projects, path)
            self.assertFalse(full, path)

    def test_test_tree_root_file_requires_a_full_run(self):
        # tests/Directory.Build.props reaches EVERY test project. Before this change
        # this path selected NO project and the inner loop would silently stay empty.
        projects, full = gate.affected_test_projects(["tests/Directory.Build.props"])

        self.assertTrue(full)

    def test_tfm_sentinel_selects_only_the_representative_set(self):
        projects, full = gate.affected_test_projects(
            ["tests/Shared/TargetFramework/RuntimeMatchesTargetFrameworkTests.cs"])

        self.assertEqual(sorted(projects), sorted(gate.multi_target_test_projects()))
        self.assertFalse(full)

    def test_test_command_reaching_a_multi_target_project_is_recognised(self):
        self.assertTrue(gate.runs_multi_target_tests([gate.full_solution_test_command()]))
        self.assertTrue(gate.runs_multi_target_tests(
            [gate.Command(("dotnet", "test", "--project", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))]))
        self.assertFalse(gate.runs_multi_target_tests(
            [gate.Command(("dotnet", "test", "--project", "tests/Tracon.Generators.UnitTests/Tracon.Generators.UnitTests.csproj"))]))
        self.assertTrue(gate.runs_multi_target_tests(
            [gate.Command(("dotnet", "test", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))]))
        self.assertFalse(gate.runs_multi_target_tests(
            [gate.Command(("dotnet", "test", "tests/Tracon.Generators.UnitTests/Tracon.Generators.UnitTests.csproj"))]))
        self.assertFalse(gate.runs_multi_target_tests([gate.Command(("dotnet", "build", "Tracon.slnx"))]))

    def test_missing_runtime_is_found_under_dotnet_root(self):
        # Measured: the test apphost resolves the runtime from DOTNET_ROOT,
        # not from the muxer on PATH.
        with tempfile.TemporaryDirectory() as directory:
            shared = pathlib.Path(directory) / "shared" / "Microsoft.NETCore.App"
            for version in ("9.0.10", "10.0.0"):
                (shared / version).mkdir(parents=True)
            runner = mock.Mock()

            missing = gate.missing_test_runtimes(
                ("net10.0", "net11.0"), environ={"DOTNET_ROOT": directory}, runner=runner)

        self.assertEqual(missing, ["net11.0"])
        runner.assert_not_called()

    def test_architecture_specific_dotnet_root_comes_first(self):
        # The apphost reads DOTNET_ROOT_<ARCH> BEFORE DOTNET_ROOT.
        environ = {"DOTNET_ROOT": "/general", "DOTNET_ROOT_ARM64": "/arm64"}

        self.assertEqual(gate._apphost_dotnet_root(environ, "arm64"), "/arm64")
        self.assertEqual(gate._apphost_dotnet_root(environ, "x86_64"), "/general")
        self.assertEqual(gate._apphost_dotnet_root({}, "arm64"), "")

    def test_missing_runtime_is_found_from_the_global_install_location(self):
        # With no DOTNET_ROOT the apphost reads the global install, NOT the
        # muxer on PATH. Even if a custom SDK with net11 is put on PATH, the
        # pre-check must stop when the global root has no net11.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "etc").mkdir()
            (root / "etc" / "install_location").write_text(str(root / "x64") + "\n", encoding="utf-8")
            (root / "etc" / "install_location_arm64").write_text(str(root / "global") + "\n", encoding="utf-8")
            for version in ("9.0.10", "10.0.0"):
                (root / "global" / "shared" / "Microsoft.NETCore.App" / version).mkdir(parents=True)
            runner = mock.Mock()

            missing = gate.missing_test_runtimes(
                ("net10.0", "net11.0"), environ={}, runner=runner,
                system="Darwin", machine="arm64", install_location_dir=root / "etc")

        self.assertEqual(missing, ["net11.0"])
        runner.assert_not_called()

    def test_missing_global_install_file_falls_back_to_the_default_root(self):
        with tempfile.TemporaryDirectory() as directory:
            absent = pathlib.Path(directory) / "etc"

            self.assertEqual(gate._global_dotnet_root("Linux", "x86_64", absent), "/usr/share/dotnet")
            self.assertEqual(gate._global_dotnet_root("Darwin", "arm64", absent), "/usr/local/share/dotnet")
            self.assertIsNone(gate._global_dotnet_root("Windows", "amd64", absent))

    def test_windows_finds_runtimes_from_list_runtimes_output(self):
        stdout = (
            "Microsoft.AspNetCore.App 11.0.0 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]\n"
            "Microsoft.NETCore.App 9.0.20 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n"
            "Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n")
        runner = mock.Mock(return_value=mock.Mock(returncode=0, stdout=stdout))

        with contextlib.redirect_stdout(io.StringIO()) as output:
            ok = gate.require_test_runtimes(("net10.0", "net11.0"), environ={}, runner=runner, system="Windows")

        # The ASP.NET Core 11 runtime does NOT count in place of NETCore 11.
        self.assertFalse(ok)
        self.assertIn("net11.0", output.getvalue())
        # Compared in the platform's own path form, NOT by separator - on
        # windows-latest PurePath produces a backslash (the same class as
        # test_mtp_filter_emits_filter_class).
        self.assertIn(str(pathlib.PurePath("/usr/share/dotnet")), output.getvalue())

    def test_unreadable_runtime_list_does_not_block(self):
        # Without dotnet the real command already stops with 127; the pre-check
        # adds no second misleading error. The same rule applies when no global root is found.
        runner = mock.Mock(side_effect=FileNotFoundError("dotnet"))

        self.assertEqual(
            gate.missing_test_runtimes(("net11.0",), environ={}, runner=runner, system="Windows"), [])
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(
                gate.missing_test_runtimes(
                    ("net11.0",), environ={"DOTNET_ROOT": str(pathlib.Path(directory) / "missing")}, runner=runner),
                [])

    def test_other_shared_test_source_still_requires_a_full_run(self):
        projects, full = gate.affected_test_projects(["tests/Shared/Infrastructure/ProcessRunner.cs"])

        self.assertTrue(full)

    def test_embedded_sample_selects_its_own_test_project(self):
        # ESCAPE: the sixth extension point was added while
        # samples/Tracon.Embedded was left behind. On that day a change under samples/
        # selected NO test project, although Embedded.Tests references exactly that
        # sample through a ProjectReference.
        projects, full = gate.affected_test_projects(
            ["samples/Tracon.Embedded/Program.cs"])

        self.assertEqual(projects, ["Tracon.Embedded.Tests"])
        self.assertFalse(full)

    def test_release_samples_are_not_skipped_silently(self):
        # samples/Tracon.Samples.* is NOT in the solution: no closing
        # run covers them, only `gate.py release`. The selection stays empty
        # but this is NO LONGER silent.
        with contextlib.redirect_stdout(io.StringIO()) as output:
            projects, full = gate.affected_test_projects(
                ["samples/Tracon.Samples.CustomJobHandler/Program.cs"])

        self.assertEqual(projects, [])
        self.assertFalse(full)
        self.assertIn("gate.py release", output.getvalue())

    def test_full_run_command_carries_the_mtp_flags(self):
        # `dotnet test` is in MTP mode. In this mode `-maxcpucount` exits with code 5 WITHOUT
        # RUNNING ANY TEST; the `--` separator no longer exists either.
        command = gate.full_solution_test_command()
        args = command.args

        self.assertEqual(args[:4], ("dotnet", "test", "--solution", "Tracon.slnx"))
        self.assertNotIn("--", args)
        self.assertFalse([arg for arg in args if arg.startswith("-maxcpucount")])
        self.assertEqual(args[args.index("--max-parallel-test-modules") + 1], str(gate.TEST_MAX_CPU_COUNT))
        self.assertIn("--report-trx", args)
        self.assertEqual(args[args.index("--results-directory") + 1], gate.TEST_RESULTS_DIRECTORY)
        self.assertEqual(args[args.index("--results-directory-layout") + 1], "per-module")

    def test_ci_and_gate_use_the_same_trx_path_and_flags(self):
        # The gate (gate.py) and CI (ci.yml) are two SEPARATELY written commands, and changing the
        # TRX path on one side silently empties the other: the CI upload finds no artifact
        # (`if-no-files-found: ignore`) and nobody notices.
        ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        test_lines = [
            line.strip() for line in ci.splitlines()
            if line.strip().startswith("run: dotnet test ") and "Tracon" in line
        ]

        # Test jobs are one step per module (`--project`); the solution command remains only in the culture job
        # (a solution filter without Docker). All of them carry the same TRX flags and do not build (`--no-build`).
        self.assertGreaterEqual(len([line for line in test_lines if "--solution" in line]), 1, test_lines)
        projects = {re.search(r"--project tests/([\w.]+)/", line).group(1) for line in test_lines if "--project" in line}
        self.assertGreaterEqual(len(projects), 25, projects)  # so the parser cannot silently fall to zero if it goes stale
        for line in test_lines:
            self.assertNotIn("-maxcpucount", line)
            self.assertIn(" --no-build", line)
            if "--solution" in line:
                # The solution command runs the modules one by one; a single-project command has no parallel modules.
                self.assertIn("--max-parallel-test-modules 1", line)
            self.assertIn("--report-trx", line)
            self.assertIn(f"--results-directory {gate.TEST_RESULTS_DIRECTORY}", line)
            self.assertIn("--results-directory-layout per-module", line)
        self.assertIn(f"path: {gate.TEST_RESULTS_DIRECTORY}/**/*.trx", ci)
        self.assertNotIn("TestResults/*.trx", ci)

    def test_ci_test_jobs_do_not_show_a_red_or_empty_run_as_green(self):
        text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")

        self.assertEqual(_ci_silent_green_problems(text), [])
        # The parser really reads (it must not silently see zero jobs): thirteen jobs use restore, fourteen jobs
        # define restore, fourteen TRX uploads.
        users = [name for name in _ci_jobs(text) if "steps.restore" in _job_block(text, name)]
        self.assertGreaterEqual(len(users), 13, users)
        self.assertGreaterEqual(text.count("        id: restore\n"), 14)
        self.assertGreaterEqual(text.count("path: artifacts/test-results/**/*.trx\n"), 14)

        def mutated(old: str, replacement: str) -> list[str]:
            self.assertIn(old, text)
            return _ci_silent_green_problems(text.replace(old, replacement, 1))

        restore = "      - name: Restore the dependencies\n        id: restore\n"
        self.assertEqual(len(mutated(restore, "      - name: Restore the dependencies\n")), 1)
        self.assertEqual(len(mutated(restore, "      - name: Restore the dependencies\n        id: restor\n")), 1)
        trx_tail = "          if-no-files-found: error\n          overwrite: true\n"
        self.assertEqual(len(mutated(trx_tail, "          if-no-files-found: ignore\n          overwrite: true\n")), 1)
        self.assertEqual(len(mutated(trx_tail, "          if-no-files-found: error\n")), 1)
        self.assertEqual(
            len(mutated("          name: nuget-packages\n", "          name: nuget-packages\n          overwrite: true\n")), 1)
        self.assertEqual(
            len(mutated("          name: bin-${{ matrix.os }}\n", "          name: bin-${{ matrix.os }}\n          overwrite: true\n")), 1)

    def _trx(self, root: pathlib.Path, project: str, results: list[tuple[str, str]], output_dir: str = "release") -> None:
        # MTP 2 `--results-directory-layout per-module`: <project>/<tfm>_<architecture>/*.trx.
        # `codeBase` carries the path of the binary the test ran from; on Windows it comes with backslashes.
        tfm = "net10.0" if output_dir == "release" else output_dir.removeprefix("release_")
        folder = root / gate.TEST_RESULTS_DIRECTORY / project / f"{tfm}_arm64"
        folder.mkdir(parents=True, exist_ok=True)
        lines = "".join(
            f'<UnitTestResult testName="{name}" outcome="{outcome}" />' for name, outcome in results)
        (folder / "r.trx").write_text(
            '<?xml version="1.0"?><TestRun xmlns='
            '"http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f"<Results>{lines}</Results>"
            f'<TestDefinitions><UnitTest><TestMethod codeBase="C:\\r\\artifacts\\bin\\{project}\\{output_dir}\\{project}.dll"'
            ' className="C" name="M" /></UnitTest></TestDefinitions></TestRun>',
            encoding="utf-8")

    def test_old_trx_location_is_no_longer_scanned(self):
        # The TRX is written to `--results-directory`, not to the TestResults/ next to the binary.
        # A file at the old path does NOT COUNT AS A FAILED TEST - otherwise the scanned place and
        # the written place diverge and the diagnosis silently stays empty.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            old = root / "artifacts" / "bin" / "P" / "release" / "TestResults"
            old.mkdir(parents=True)
            (old / "r.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                '<UnitTestResult testName="N.S.Failing" outcome="Failed" /></Results></TestRun>',
                encoding="utf-8")

            self.assertEqual(gate.failed_tests_from_trx(0, root), [])

    def test_trx_output_folder_is_read_from_codebase_else_single_target_release_is_assumed(self):
        # The branch without `codeBase` was untested. For a multi-target leg the TRX names the folder;
        # without `codeBase` the `release` folder of a single-target project is assumed.
        def tree(method: str):
            return ElementTree.ElementTree(ElementTree.fromstring(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                f"<TestDefinitions><UnitTest>{method}</UnitTest></TestDefinitions></TestRun>"))

        self.assertEqual(
            gate._trx_output_directory(tree('<TestMethod codeBase="C:\\r\\bin\\P\\release_net10.0\\P.dll" name="M" />')),
            "release_net10.0")
        self.assertEqual(
            gate._trx_output_directory(tree('<TestMethod codeBase="/r/bin/P/release/P.dll" name="M" />')),
            "release")
        self.assertEqual(gate._trx_output_directory(tree('<TestMethod name="M" />')), "release")
        self.assertEqual(gate._trx_output_directory(tree('<TestMethod codeBase="P.dll" name="M" />')), "release")

    def test_green_run_with_zero_tests_counts_as_red(self):
        # NEGATIVE CHECK: exit code 0 does not mean "tests ran". If there is no TRX
        # or it is empty, the gate does not return GREEN.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(gate.require_executed_tests(0, root), gate.ZERO_TESTS_EXIT_CODE)
            self.assertIn("no executed test exists", output.getvalue())

            self._trx(root, "P", [("N.S.Passing", "Passed"), ("N.S.Failing", "Failed")])
            self._trx(root, "Q", [("N.S.Passing", "Passed")], output_dir="release_net10.0")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(gate.require_executed_tests(0, root), 0)
            self.assertIn("3 tests, 2 modules", output.getvalue())

    def test_run_commands_stops_when_exit_code_is_zero_but_no_tests_ran(self):
        runner = mock.Mock(return_value=mock.Mock(returncode=0))
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(gate, "executed_tests_from_trx", return_value=(0, 0)), \
                contextlib.redirect_stdout(io.StringIO()):
            result = gate.run_commands(
                "test", [gate.full_solution_test_command()], runner=runner,
                measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, gate.ZERO_TESTS_EXIT_CODE)

    def test_run_commands_does_not_fail_a_command_that_wants_no_trx_for_zero_tests(self):
        # `dotnet build` or a single-project command produces no TRX; the gate does not ask them for a count.
        runner = mock.Mock(return_value=mock.Mock(returncode=0))
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(gate, "executed_tests_from_trx", return_value=(0, 0)), \
                contextlib.redirect_stdout(io.StringIO()):
            result = gate.run_commands(
                "test", [gate.Command(("dotnet", "build", "Tracon.slnx")), gate._dotnet_test_project("P")],
                runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 0)

    def test_contract_package_keeps_the_xunit_lower_bound(self):
        # If the `PackageVersion` of the two sub-packages rises to 4.x, the package
        # silently forces an xunit.v3 3.x consumer. The gate looks at the nuspec.
        project = "Tracon.Testing.Contracts.Xunit"
        good = ('<dependency id="xunit.v3.extensibility.core" version="3.2.2" exclude="Build,Analyzers" />'
               '<dependency id="xunit.v3.assert" version="3.2.2" />')
        self.assertEqual(gate.dependency_floor_errors(project, good + good), [])
        self.assertEqual(gate.dependency_floor_errors("Tracon.Core", ""), [])

        raised = good.replace('"xunit.v3.assert" version="3.2.2"', '"xunit.v3.assert" version="4.0.2"')
        errors = gate.dependency_floor_errors(project, raised)
        self.assertEqual(len(errors), 1)
        self.assertIn("the xunit.v3.assert lower bound is 4.0.2", errors[0])

        missing = gate.dependency_floor_errors(project, '<dependency id="xunit.v3.assert" version="3.2.2" />')
        self.assertEqual(len(missing), 1)
        self.assertIn("the xunit.v3.extensibility.core dependency is missing from the nuspec", missing[0])

    def test_repo_xunit_lower_bound_matches_the_props_file(self):
        props = (ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
        for package, lower_bound in gate.SHIPPED_DEPENDENCY_FLOORS["Tracon.Testing.Contracts.Xunit"].items():
            self.assertIn(f'<PackageVersion Include="{package}" Version="{lower_bound}" />', props)

    def test_single_project_command_uses_the_project_flag(self):
        self.assertEqual(
            gate._dotnet_test_project("P").args,
            ("dotnet", "test", "--project", "tests/P/P.csproj", "-c", "Release", "--no-build"))

    def test_trx_yields_only_the_failed_test(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx(root, "Tracon.Ui.E2ETests", [
                ("Tracon.Ui.E2ETests.UiTests.Passing", "Passed"),
                ("Tracon.Ui.E2ETests.UiTests.Failing", "Failed"),
                ("Tracon.Ui.E2ETests.UiTests.Skipped", "NotExecuted"),
            ])

            failed = gate.failed_tests_from_trx(0, root)

        self.assertEqual(
            failed, [gate.FailedTest("Tracon.Ui.E2ETests", "release", "Tracon.Ui.E2ETests.UiTests.Failing")])

    def test_multi_tfm_trx_is_found_with_its_leg(self):
        # Had only release/ been scanned, a test failing on the net11.0 leg
        # would never be reported and the isolated run would not trigger either.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx(root, "Tracon.Core.UnitTests", [("N.S.Failing", "Failed")], output_dir="release_net11.0")
            self._trx(root, "Tracon.Core.UnitTests", [("N.S.Failing", "Passed")], output_dir="release_net10.0")

            failed = gate.failed_tests_from_trx(0, root)

        self.assertEqual(failed, [gate.FailedTest("Tracon.Core.UnitTests", "release_net11.0", "N.S.Failing")])
        self.assertEqual(failed[0].label, "Tracon.Core.UnitTests (net11.0)")

    def test_stale_trx_is_not_counted(self):
        # The TRX of the previous run is not a failed test of this run.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx(root, "Tracon.Core.UnitTests", [("X.Y.Old", "Failed")])

            self.assertEqual(gate.failed_tests_from_trx(time.time() + 60, root), [])

    def test_isolated_run_keeps_the_exit_code_but_gives_a_verdict(self):
        # CLASS: "fails in the full run, passes in isolation" was recorded SEVEN times and each
        # time the distinction was made by hand (usually by running the FULL suite a second time,
        # ~9 min). The gate does NOT loosen: this only names the next step.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            binary = root / "artifacts" / "bin" / "P" / "release"
            binary.mkdir(parents=True)
            (binary / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=0))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                verdicts = gate.isolate_failed_tests(
                    [gate.FailedTest("P", "release", "N.S.Failing")], runner=runner, root=root)

        self.assertEqual(verdicts, [("P", "N.S.Failing", True)])
        self.assertIn("--filter-method", runner.call_args.args[0])
        self.assertIn("*Failing*", runner.call_args.args[0])
        self.assertIn("PASSED in isolation", output.getvalue())
        self.assertIn("do not rerun the suite", output.getvalue())
        self.assertIn("root cause", output.getvalue())
        self.assertIn("product race", output.getvalue())
        self.assertNotIn("RERUN", output.getvalue())

    def _trx_with_durations(self, root: pathlib.Path, project: str, results: list[tuple[str, str]]) -> None:
        folder = root / gate.TEST_RESULTS_DIRECTORY / project / "net10.0_arm64"
        folder.mkdir(parents=True)
        lines = "".join(
            f'<UnitTestResult testName="{name}" duration="{duration}" outcome="Passed" />' for name, duration in results)
        (folder / "result.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f"<Results>{lines}</Results></TestRun>",
            encoding="utf-8")

    def test_duration_record_writes_milliseconds_per_test(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [("N.S.A", "00:00:01.5000000"), ("N.S.B", "00:01:00.0100000")])

            with contextlib.redirect_stdout(io.StringIO()):
                path = gate.record_test_durations(0.0, root=root)

            with gzip.open(path, "rt", encoding="utf-8") as file:
                record = json.load(file)

        self.assertEqual(record["modules"], {"P/net10.0_arm64": {"N.S.A": 1500, "N.S.B": 60010}})

    def test_duration_record_keeps_the_longest_of_same_named_tests(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [("N.S.A", "00:00:00.1000000"), ("N.S.A", "00:00:00.3000000")])

            with contextlib.redirect_stdout(io.StringIO()):
                path = gate.record_test_durations(0.0, root=root)

            with gzip.open(path, "rt", encoding="utf-8") as file:
                record = json.load(file)

        self.assertEqual(record["modules"]["P/net10.0_arm64"], {"N.S.A": 300})

    def test_duration_record_without_trx_writes_no_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)

            self.assertIsNone(gate.record_test_durations(0.0, root=root))
            self.assertFalse((root / gate.TEST_DURATIONS_DIRECTORY).exists())

    def test_duration_record_skips_an_unparseable_duration(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [("N.S.A", "garbled"), ("N.S.B", "00:00:00.0020000")])

            with contextlib.redirect_stdout(io.StringIO()):
                path = gate.record_test_durations(0.0, root=root)

            with gzip.open(path, "rt", encoding="utf-8") as file:
                record = json.load(file)

        self.assertEqual(record["modules"]["P/net10.0_arm64"], {"N.S.B": 2})

    def test_duration_record_does_not_break_the_gate_on_a_malformed_trx(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            folder = root / gate.TEST_RESULTS_DIRECTORY / "P" / "net10.0_arm64"
            folder.mkdir(parents=True)
            (folder / "result.trx").write_text("<TestRun", encoding="utf-8")

            self.assertIsNone(gate.record_test_durations(0.0, root=root))

    def test_duration_record_does_not_break_the_gate_on_an_empty_trx(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [])

            with contextlib.redirect_stdout(io.StringIO()):
                path = gate.record_test_durations(0.0, root=root)

            with gzip.open(path, "rt", encoding="utf-8") as file:
                record = json.load(file)

        self.assertEqual(record["modules"], {"P/net10.0_arm64": {}})

    def test_duration_record_warns_when_unwritable_and_does_not_break_the_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [("N.S.A", "00:00:00.0010000")])

            with mock.patch.object(gate.gzip, "open", side_effect=OSError("disk full")), \
                    contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertIsNone(gate.record_test_durations(0.0, root=root))

        self.assertIn("could not be written", output.getvalue())

    def test_duration_record_deletes_old_files(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [("N.S.A", "00:00:00.0010000")])
            records = root / gate.TEST_DURATIONS_DIRECTORY
            records.mkdir(parents=True)
            for order in range(gate.TEST_DURATIONS_KEEP + 3):
                (records / f"20000101T0000{order:02d}Z.json.gz").write_bytes(b"")

            with contextlib.redirect_stdout(io.StringIO()):
                gate.record_test_durations(0.0, root=root)

            remaining = sorted(path.name for path in records.glob("*.json.gz"))

        self.assertEqual(len(remaining), gate.TEST_DURATIONS_KEEP)
        self.assertNotIn("20000101T000000Z.json.gz", remaining)

    def _trx_with_times(self, root: pathlib.Path, project: str, start: str, finish: str) -> None:
        folder = root / gate.TEST_RESULTS_DIRECTORY / project / "net10.0_arm64"
        folder.mkdir(parents=True)
        (folder / "result.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<Times creation="{start}" queuing="{start}" start="{start}" finish="{finish}" />'
            '<Results><UnitTestResult testName="N.S.A" duration="00:00:01" outcome="Passed" /></Results></TestRun>',
            encoding="utf-8")

    def test_gate_is_red_when_a_module_exceeds_its_time_limit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_times(root, "P", "2026-11-09T11:03:40.3574990Z", "2026-11-09T11:12:25.866393Z")

            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits={"P": 500})

        self.assertEqual(code, gate.MODULE_TIME_LIMIT_EXIT_CODE)
        self.assertIn("P/net10.0_arm64 526 s > 500 s", output.getvalue())
        self.assertIn("measure", output.getvalue())

    def test_module_under_its_time_limit_is_green_and_printed(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_times(root, "P", "2026-11-09T11:00:00Z", "2026-11-09T11:03:59.9Z")

            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits={"P": 240})

        self.assertEqual(code, 0)
        self.assertIn("P/net10.0_arm64: 240 s · limit 240 s · left 0 s (0%)", output.getvalue())

    def test_module_without_a_limit_does_not_break_the_gate_but_its_time_is_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_times(root, "Q", "2026-11-09T11:00:00Z", "2026-11-09T13:00:00Z")

            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits={"P": 1})

        self.assertEqual(code, 0)
        self.assertIn("Q/net10.0_arm64: 7200 s · no limit", output.getvalue())

    def test_trx_with_an_unreadable_duration_warns_and_does_not_break_the_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx_with_durations(root, "P", [("N.S.A", "00:00:01")])

            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits={"P": 1})

        self.assertEqual(code, 0)
        self.assertIn("could not be read", output.getvalue())

    def test_trx_timestamp_reads_a_seven_digit_fraction_and_the_time_zone(self):
        self.assertAlmostEqual(
            gate._trx_instant("2026-11-09T11:00:01.1234567+01:00") - gate._trx_instant("2026-11-09T10:00:00Z"),
            1.123456, places=5)
        self.assertIsNone(gate._trx_instant("yesterday"))
        self.assertIsNone(gate._trx_instant(None))

    def test_module_time_budgets_only_fall_and_belong_to_real_modules(self):
        # Budgets only fall; this test pins today's values so that a raise
        # or a deletion of a budget cannot go unnoticed (the rule stated in the gate.py comment).
        # Adding a new entry means adding a row to this table too; an existing row can only be lowered.
        pinned = {
            "Tracon.SqlServer.IntegrationTests": 400,
            "Tracon.TwoNode.IntegrationTests": 540,
            "Tracon.Ui.E2ETests": 40,
            "Tracon.AspNetCore.FunctionalTests": 400,
            "Tracon.PostgreSql.IntegrationTests": 170,
            "Tracon.Package.Tests": 310,
            "Tracon.Core.UnitTests": 50,
            "Tracon.Workflows.UnitTests": 40,
            "Tracon.Generators.UnitTests": 30,
            "Tracon.Sqlite.IntegrationTests": 90,
        }
        budgets = gate.TEST_MODULE_TIME_LIMITS_SECONDS

        for module, limit in pinned.items():
            with self.subTest(module=module):
                self.assertIn(module, budgets, "a budget cannot be deleted")
                self.assertLessEqual(budgets[module], limit, "a budget cannot be raised")
        modules = set(_slnx_test_modules())
        self.assertEqual(sorted(set(budgets) - modules), [], "a budget must belong to a test module")
        self.assertTrue(all(isinstance(limit, int) and limit > 0 for limit in budgets.values()))

    def test_green_full_run_enforces_the_time_limit(self):
        command = gate.full_solution_test_command()
        with mock.patch.object(gate, "require_executed_tests", return_value=0), \
                mock.patch.object(gate, "enforce_module_time_limits", return_value=gate.MODULE_TIME_LIMIT_EXIT_CODE) as limit, \
                mock.patch.object(gate, "record_test_durations"), \
                mock.patch.object(gate, "isolate_failed_tests", return_value=[]), \
                mock.patch.object(gate, "failed_tests_from_trx", return_value=[]), \
                mock.patch.object(gate, "_record_measurement"), \
                contextlib.redirect_stdout(io.StringIO()):
            code = gate.run_commands("test", [command], runner=mock.Mock(return_value=mock.Mock(returncode=0)))

        self.assertEqual(code, gate.MODULE_TIME_LIMIT_EXIT_CODE)
        limit.assert_called_once()

    def _time_limited_run(self, command, *, exit_code):
        with mock.patch.object(gate, "require_executed_tests", return_value=0), \
                mock.patch.object(gate, "enforce_module_time_limits", return_value=gate.MODULE_TIME_LIMIT_EXIT_CODE) as limit, \
                mock.patch.object(gate, "record_test_durations"), \
                mock.patch.object(gate, "isolate_failed_tests", return_value=[]), \
                mock.patch.object(gate, "failed_tests_from_trx", return_value=[]), \
                mock.patch.object(gate, "_record_measurement"), \
                contextlib.redirect_stdout(io.StringIO()):
            code = gate.run_commands("test", [command], runner=mock.Mock(return_value=mock.Mock(returncode=exit_code)))
        return code, limit

    def test_red_full_run_keeps_its_exit_code_and_does_not_enforce_the_time_limit(self):
        # A failing test must not be overridden by the time-limit code 9: the reason for the red is the test.
        code, limit = self._time_limited_run(gate.full_solution_test_command(), exit_code=2)

        self.assertEqual(code, 2)
        limit.assert_not_called()

    def test_dotnet_test_that_wants_no_trx_does_not_enforce_the_time_limit(self):
        command = gate.Command(("dotnet", "test", "--project", "P.csproj"))
        self.assertFalse(gate.reports_trx(command))

        code, limit = self._time_limited_run(command, exit_code=0)

        self.assertEqual(code, 0)
        limit.assert_not_called()

    def test_isolated_run_executes_the_binary_of_the_failing_leg(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for output_dir in ("release_net10.0", "release_net11.0"):
                binary = root / "artifacts" / "bin" / "P" / output_dir
                binary.mkdir(parents=True)
                (binary / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                verdicts = gate.isolate_failed_tests(
                    [gate.FailedTest("P", "release_net11.0", "N.S.Failing")], runner=runner, root=root)

        self.assertEqual(pathlib.PurePath(runner.call_args.args[0][0]).parts[-2], "release_net11.0")
        self.assertEqual(verdicts, [("P (net11.0)", "N.S.Failing", False)])
        self.assertIn("P (net11.0) · N.S.Failing", output.getvalue())

    def test_test_failing_in_isolation_too_is_a_real_regression(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            binary = root / "artifacts" / "bin" / "P" / "release"
            binary.mkdir(parents=True)
            (binary / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                verdicts = gate.isolate_failed_tests(
                    [gate.FailedTest("P", "release", "N.S.Failing")], runner=runner, root=root)

        self.assertEqual(verdicts, [("P", "N.S.Failing", False)])
        self.assertIn("REAL regression", output.getvalue())

    def test_frontend_change_opens_the_frontend_in_the_inner_loop(self):
        self.assertTrue(gate.frontend_changed(["src/Tracon.UI/frontend/src/app.tsx"]))
        self.assertFalse(gate.frontend_changed(["src/Tracon.Core/Thing.cs"]))

    def test_inner_loop_that_runs_e2e_builds_with_the_ui(self):
        # A path missing from the map widened the inner loop to a full
        # run, but the build stayed on `-p:TraconFrontendEnabled=false`;
        # without the embedded assets all of E2E failed and a real
        # regression was lost in that noise. Every path that selects E2E (a full
        # run or the E2E project itself) must build with the UI.
        for path in ("src/Unknown.Package/Thing.cs",
                     "tests/Shared/Infrastructure/ProcessRunner.cs",
                     "tests/Tracon.Ui.E2ETests/RunsPageTests.cs"):
            with contextlib.redirect_stdout(io.StringIO()):
                build = gate.inner_loop_commands([path])[0]

            self.assertEqual(build.args[:2], ("dotnet", "build"), path)
            self.assertNotIn("-p:TraconFrontendEnabled=false", build.args, path)

    def test_inner_loop_that_skips_e2e_builds_without_the_ui(self):
        build = gate.inner_loop_commands(["src/Tracon.Core/Thing.cs"])[0]

        self.assertIn("-p:TraconFrontendEnabled=false", build.args)

    def test_inner_loop_base_also_sees_committed_work(self):
        # Once the work was committed, `inner-loop` saw nothing against HEAD,
        # ran only the build and said "green" (two non-compiling
        # <example> blocks stayed invisible for one commit because of this). `--base` supplies the range.
        with mock.patch.object(gate, "changed_paths", return_value=["src/Tracon.Core/Thing.cs"]) as paths, \
                contextlib.redirect_stdout(io.StringIO()):
            code = gate.main(["--print-commands", "inner-loop", "--base", "abc123"])

        self.assertEqual(code, 0)
        paths.assert_called_once_with("abc123")

    def test_inner_loop_with_no_changes_reminds_about_the_base(self):
        with mock.patch.object(gate, "changed_paths", return_value=[]), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            gate.main(["--print-commands", "inner-loop"])

        self.assertIn("--base", output.getvalue())

    def test_closing_commands_carry_the_four_net_gates(self):
        commands = gate.closing_commands("abc123", site=False)
        rendered = [command.display for command in commands]

        self.assertIn("dotnet build Tracon.slnx -c Release", rendered)
        # The full test run is not a single `--solution Tracon.slnx` command; it is staged (StagedTestRunTests).
        for stage in gate.staged_test_commands():
            self.assertIn(stage.display, rendered)
        self.assertNotIn(gate.full_solution_test_command().display, rendered)
        # TraconSkipCleanWorkingTreeCheck: this pack validates the
        # packaging CONTRACT during iteration, not a release candidate - it must
        # not be blocked by the new dirty-tree gate the way `gate.py release` is.
        self.assertIn(
            "dotnet pack Tracon.slnx -c Release --no-build -p:TraconSkipCleanWorkingTreeCheck=true",
            rendered)
        self.assertIn("dotnet format Tracon.slnx --verify-no-changes", rendered)

    def test_closing_starts_with_generation_and_ends_with_the_full_tests(self):
        # October 2026: of 42 red closings, 11 failed on a stale agent map and 11 on a step
        # that ran AFTER the tests. Generation goes first, the most expensive step last.
        rendered = [c.display for c in gate.closing_commands("abc123", site=True, performance=True)]
        generation = [c.display for c in gate.generation_commands()]

        self.assertEqual(rendered[:len(generation)], generation)
        # The tests come last and contiguous: every command of the staged run is `dotnet test`, no other step.
        tests = [c.display for c in gate.staged_test_commands()]
        self.assertEqual(rendered[-len(tests):], tests)
        self.assertEqual(sum(1 for c in rendered if c.startswith("dotnet test")), len(tests))
        tests_start = len(rendered) - len(tests)
        for cheap in (
            "dotnet format Tracon.slnx --verify-no-changes",
            "dotnet pack Tracon.slnx -c Release --no-build -p:TraconSkipCleanWorkingTreeCheck=true",
            "(cd docs-site && npm run check)",
            "python3 scripts/gate.py perf",
        ):
            self.assertLess(rendered.index(cheap), tests_start, cheap)
        # DocFX reads the XML documentation the build writes: the site gate comes after the build.
        self.assertLess(rendered.index("dotnet build Tracon.slnx -c Release"),
                        rendered.index("(cd docs-site && npm run check)"))
        # The site (~35 s, 6 of 11 late reds) comes before format (~2 min).
        self.assertLess(rendered.index("(cd docs-site && npm run check)"),
                        rendered.index("dotnet format Tracon.slnx --verify-no-changes"))

    def test_closing_forces_restore_and_runs_it_before_the_build(self):
        # A cached restore does not see a newly published security advisory
        # (`NU1902`/`NU1903` appear only on a cold or forced restore). The closing
        # proves zero warnings only if it really runs the audit.
        rendered = [c.display for c in gate.closing_commands("abc123", site=False)]

        self.assertIn("dotnet restore Tracon.slnx --force", rendered)
        self.assertLess(rendered.index("dotnet restore Tracon.slnx --force"),
                        rendered.index("dotnet build Tracon.slnx -c Release"))

    def test_closing_generates_the_agent_map_without_checking_it(self):
        # Generating locally makes the check redundant; `--check` stays in CI (ci.yml).
        rendered = [c.display for c in gate.closing_commands("abc123", site=False)]

        self.assertIn("node docs-site/scripts/build-agent-map.mjs", rendered)
        self.assertNotIn("node docs-site/scripts/build-agent-map.mjs --check", rendered)

    def test_public_command_lists_call_no_script_outside_the_public_tree(self):
        # The public tree runs with its own files only: a shipped gate that called a
        # script outside it would fail at its first step.
        for rendered in (
            [c.display for c in gate.closing_commands("abc123", site=True, performance=True)],
            [c.display for c in gate.generation_commands()],
        ):
            for command in rendered:
                self.assertNotIn(".agents", command)
            # Every script the public lists run exists in the public `scripts/` directory.
            for command in [*gate.closing_commands("abc123", site=True, performance=True), *gate.generation_commands()]:
                if command.args[0] == "python3" and command.args[1].endswith(".py"):
                    self.assertTrue((ROOT / command.args[1]).is_file(), command.args[1])

    def test_extension_steps_are_added_to_the_public_list(self):
        extension = gate.Extension(
            program="custom/entry.py",
            generation=(gate.Command(("echo", "generate")),),
            after_scan=lambda base: (gate.Command(("echo", base)),),
        )

        rendered = [c.display for c in gate.closing_commands("abc123", site=False, extension=extension)]

        self.assertEqual(rendered[0], "echo generate")
        self.assertEqual(rendered[rendered.index("python3 custom/entry.py scan") + 1], "echo abc123")
        self.assertNotIn("python3 scripts/gate.py scan", rendered)

    def test_scan_roots_work_without_the_optional_directories(self):
        # In the public tree `docs/` carries only two contract directories and there is no other root.
        with tempfile.TemporaryDirectory() as temp_dir:
            root = pathlib.Path(temp_dir)
            (root / "src").mkdir()
            (root / "docs" / "contracts").mkdir(parents=True)
            (root / "docs" / "contracts" / "error-codes.txt").write_text("x", encoding="utf-8")

            self.assertEqual(gate.find_sync_copies(root), [])
            self.assertEqual(gate.find_stale_doc_references(root), [])
            self.assertEqual(gate.find_secrets(root), ([], 0))

    def test_every_ci_job_is_tied_to_the_canonical_repo_guard(self):
        # No job may spend minutes on forks or other copies of the repository.
        jobs = _ci_jobs((ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8"))

        self.assertEqual(jobs["build"]["if"], CANONICAL_REPO_CONDITION)
        self.assertEqual(_unguarded_jobs(jobs), [])

    def test_ci_guard_catches_a_job_that_breaks_the_chain(self):
        text = (
            "jobs:\n"
            "  build:\n"
            "    if: github.repository == 'farukatasoy/Tracon'\n"
            "  report:\n"
            "    needs: build\n"
            "    if: always()\n"
            "  summary:\n"
            "    needs: [build]\n"
            "    if: >-\n"
            "      github.ref == 'refs/heads/main' &&\n"
            "      !cancelled()\n"
            "    runs-on: ubuntu-latest\n"
            "  safe:\n"
            "    needs: build\n"
            "    if: always() && github.repository == 'farukatasoy/Tracon'\n"
            "  failed:\n"
            "    needs: build\n"
            "    if: failure()\n"
            "  leaky:\n"
            "    needs: build\n"
            "    if: always() || github.repository == 'farukatasoy/Tracon'\n"
            "  detached:\n"
            "    runs-on: ubuntu-latest\n"
        )

        jobs = _ci_jobs(text)
        flagged = sorted({problem.split("`")[1] for problem in _unguarded_jobs(jobs)})

        self.assertEqual(jobs["summary"]["if"], "github.ref == 'refs/heads/main' && !cancelled()")
        self.assertEqual(flagged, ["detached", "failed", "leaky", "report", "summary"])

    def test_every_job_of_the_unchained_workflows_carries_the_canonical_repo_guard_itself(self):
        # Weekly jobs must not spend minutes on forks or other copies either. `ci.yml` inherits the guard
        # through `build`, and the other excluded workflow is not part of the public tree; every remaining
        # workflow file must carry the guard itself.
        files = sorted(
            p for p in (ROOT / ".github" / "workflows").glob("*.yml") if p.name not in UNCHAINED_EXCLUDED_FILES)

        self.assertIn("sdk-conformance.yml", [p.name for p in files])
        self.assertIn("openapi-fuzz.yml", [p.name for p in files])
        for file in files:
            jobs = _ci_jobs(file.read_text(encoding="utf-8"))
            with self.subTest(file=file.name):
                self.assertTrue(jobs, "no job found: the parser could not read the file")
                self.assertEqual(_unchained_unguarded_jobs(jobs), [])

    def test_unchained_guard_check_catches_a_missing_and_a_leaky_guard(self):
        genuine = (ROOT / ".github" / "workflows" / "sdk-conformance.yml").read_text(encoding="utf-8")
        guarded = f"    if: {CANONICAL_REPO_CONDITION}\n"
        self.assertIn(guarded, genuine)

        unguarded = _ci_jobs(genuine.replace(guarded, ""))
        leaky = _ci_jobs(genuine.replace(guarded, f"    if: always() || {CANONICAL_REPO_CONDITION}\n"))

        self.assertEqual(_unchained_unguarded_jobs(_ci_jobs(genuine)), [])
        self.assertEqual(len(_unchained_unguarded_jobs(unguarded)), 1)
        self.assertEqual(len(_unchained_unguarded_jobs(leaky)), 1)

    def test_platform_legs_tie_name_environment_variable_and_image_together(self):
        text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        images = _default_images()

        self.assertTrue(all(images.values()), images)
        self.assertEqual(_matrix_link_problems(text, images), [])
        # The parser really reads: seven PostgreSQL cells, two SQL Server cells.
        self.assertEqual(
            [g["name"] for g in _matrix_entries(_job_block(text, "postgres-matrix"))],
            ["12", "13", "14", "15", "16", "17", "18, ICU tr-TR"])
        self.assertEqual(
            [g["name"] for g in _matrix_entries(_job_block(text, "sqlserver-matrix"))],
            ["2019", "2022, Turkish_CI_AS"])
        self.assertEqual([g["sqlite"] for g in _matrix_entries(_job_block(text, "sqlite-floor"))], ["3.44.0"])

    def test_platform_leg_turns_red_when_the_tie_breaks(self):
        text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        images = _default_images()

        typo = text.replace("TRACON_TEST_POSTGRES_IMAGE: ${{ matrix.image }}", "TRACON_TEST_POSTGRES_IMAGE: ${{ matrix.imge }}")
        wrong_image = text.replace("image: pgvector/pgvector:pg13", "image: pgvector/pgvector:pg14")
        no_image = text.replace("          - name: '13'\n            image: pgvector/pgvector:pg13\n", "          - name: '13'\n            image: ''\n")
        sql_year = text.replace("image: mcr.microsoft.com/mssql/server:2019-latest", "image: mcr.microsoft.com/mssql/server:2022-latest")
        empty_version = text.replace("sqlite: '3.44.0'", "sqlite: ''")
        for changed in (typo, wrong_image, no_image, sql_year, empty_version):
            self.assertNotEqual(changed, text)

        self.assertTrue(any("matrix.imge" in s for s in _matrix_link_problems(typo, images)))
        self.assertEqual(len(_matrix_link_problems(wrong_image, images)), 1)
        self.assertIn("13", _matrix_link_problems(wrong_image, images)[0])
        self.assertEqual(len(_matrix_link_problems(no_image, images)), 1)  # empty image = pg18, cell 13
        self.assertEqual(len(_matrix_link_problems(sql_year, images)), 1)
        self.assertEqual(len(_matrix_link_problems(empty_version, images)), 1)

    def test_default_image_legs_run_on_pull_requests_and_pass_no_image_variable(self):
        # PostgreSQL and SQL Server are outside the all-projects leg; the matrices carry only non-default versions
        # and locales and do not run on PRs. The default image is the only place where these two modules run on PRs as well.
        text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        jobs = _ci_jobs(text)

        for job_name, variable in (
            ("test-postgres", "TRACON_TEST_POSTGRES_IMAGE"),
            ("test-sqlserver", "TRACON_TEST_SQLSERVER_IMAGE"),
        ):
            self.assertIsNone(jobs[job_name]["if"], f"`{job_name}` must run on every event, pull requests included")
            self.assertNotIn(variable, _job_block(text, job_name), f"`{job_name}` uses the fixture's default image")
            self.assertNotIn("matrix.", _job_block(text, job_name))
        for job_name in ("postgres-matrix", "sqlserver-matrix", "sqlite-floor", "culture-matrix"):
            self.assertEqual(jobs[job_name]["if"], "github.event_name != 'pull_request'", job_name)

    def test_ci_runs_every_test_project_exactly_once_on_its_own_operating_system(self):
        # The solution command ran every project on its own; per-module steps silently leave a new test project
        # out. The `test-*` jobs are the all-projects legs: those on ubuntu run the test projects of Tracon.slnx,
        # those on windows run the ones of Tracon.no-docker.slnf EXACTLY ONCE. The matrices (database
        # versions, culture) are separate legs and are not counted here.
        text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        jobs = _ci_jobs(text)
        # WorkerHarness is not a test; it belongs to the second process.
        slnx = (ROOT / "Tracon.slnx").read_text(encoding="utf-8")
        ubuntu_expected = set(re.findall(r'Path="tests/([\w.]+)/[\w.]+\.csproj"', slnx)) - {"Tracon.WorkerHarness"}
        solution_filter = json.loads((ROOT / "Tracon.no-docker.slnf").read_text(encoding="utf-8"))
        windows_expected = {
            hit.group(1) for path in solution_filter["solution"]["projects"]
            if (hit := re.match(r"tests/([\w.]+)/", path))} - {"Tracon.WorkerHarness"}

        running: dict[str, dict[str, int]] = {"ubuntu-latest": {}, "windows-latest": {}}
        for name in jobs:
            if not name.startswith("test-"):
                continue
            block = _job_block(text, name)
            os_name = re.search(r"(?m)^    runs-on:\s*(\S+)", block).group(1)
            for module in re.findall(r"(?m)^\s+run: dotnet test --project tests/([\w.]+)/", block):
                running[os_name][module] = running[os_name].get(module, 0) + 1

        self.assertGreaterEqual(len(ubuntu_expected), 25)
        self.assertEqual(sorted(running["ubuntu-latest"].items()), [(m, 1) for m in sorted(ubuntu_expected)])
        self.assertEqual(sorted(running["windows-latest"].items()), [(m, 1) for m in sorted(windows_expected)])

    def test_ci_job_graph_is_consistent_and_cancel_rules_are_in_place(self):
        text = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        jobs = _ci_jobs(text)

        for name, job in jobs.items():
            for dependency in job["needs"]:
                self.assertIn(dependency, jobs, f"`{name}` waits for the non-existent job `{dependency}`")
        # No cycle: starting from a job without dependencies, every job can be ordered.
        ordered: set[str] = set()
        while len(ordered) < len(jobs):
            ready = [name for name, job in jobs.items() if name not in ordered and set(job["needs"]) <= ordered]
            self.assertTrue(ready, f"dependency cycle: {sorted(set(jobs) - ordered)}")
            ordered.update(ready)

        # Cancellation: on PRs and main the older run is cancelled; a tag run NEVER; the group of a manual trigger
        # contains `run_id` (so runs triggered one after another for measurement do not cancel each other).
        block = re.search(r"(?m)^concurrency:\n((?:  .*\n)+)", text)
        self.assertIsNotNone(block, "ci.yml has no workflow-level `concurrency:`")
        group = re.search(r"(?m)^  group: (.*)$", block.group(1)).group(1)
        cancel = re.search(r"(?m)^  cancel-in-progress: (.*)$", block.group(1)).group(1)
        self.assertIn("github.event_name == 'workflow_dispatch'", group)
        self.assertIn("github.run_id", group)
        self.assertIn("github.ref", group)
        self.assertIn("!startsWith(github.ref, 'refs/tags/')", cancel)
        self.assertIn("github.event_name != 'workflow_dispatch'", cancel)
        # The gates are not skipped because a change is documentation-only.
        self.assertNotIn("paths-ignore", text)
        self.assertIn("PYTHONUTF8: 1", text)

    def test_generate_command_runs_only_the_generators(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            result = gate.main(["--print-commands", "generate"])

        self.assertEqual(result, 0)
        self.assertEqual(
            [line[2:] for line in output.getvalue().splitlines() if line.startswith("$ ")],
            [c.display for c in gate.generation_commands()])

    def test_closing_adds_the_performance_step_conditionally(self):
        with_it = [c.display for c in gate.closing_commands("abc123", site=False, performance=True)]
        without_it = [c.display for c in gate.closing_commands("abc123", site=False, performance=False)]

        self.assertIn("python3 scripts/gate.py perf", with_it)
        self.assertNotIn("python3 scripts/gate.py perf", without_it)

    def test_hot_path_change_triggers_the_performance_gate(self):
        self.assertTrue(gate.performance_gate_triggered(["src/Tracon.Core/Recording/RunEventWriter.cs"]))
        self.assertTrue(gate.performance_gate_triggered(["src/Tracon.Core/Compilation/CompiledAgentCache.cs"]))
        self.assertTrue(gate.performance_gate_triggered(["src/Tracon.Core/Diagnostics/ChatTelemetryLanes.cs"]))
        self.assertTrue(gate.performance_gate_triggered(["src/Tracon.Sql.Shared/Stores/SqlRunStore.cs"]))
        self.assertTrue(gate.performance_gate_triggered(["src/Tracon.Sqlite/Internal/SqliteDataSource.cs"]))
        self.assertTrue(gate.performance_gate_triggered(["src/Tracon.Sqlite/Internal/SqliteDecimalFunctions.cs"]))
        self.assertTrue(gate.performance_gate_triggered(["bench/Tracon.Benchmarks/Program.cs"]))
        self.assertFalse(gate.performance_gate_triggered(["src/Tracon.Core/TraconOptions.cs"]))
        self.assertFalse(gate.performance_gate_triggered([]))

    def test_full_test_run_limits_resource_contention(self):
        # At most TEST_MAX_CPU_COUNT test modules at the same time: light stages run that many modules in one command,
        # heavy modules run on that many lanes with a single module per lane.
        stages = gate.staged_test_commands()
        light = [command for command in stages if command.group is None]
        heavy = [command for command in stages if command.group is not None]

        for command in light:
            self.assertEqual(
                command.args[command.args.index("--max-parallel-test-modules") + 1], str(gate.TEST_MAX_CPU_COUNT))
        for command in heavy:
            self.assertEqual(command.args[command.args.index("--max-parallel-test-modules") + 1], "1")
        self.assertEqual(len({command.lane for command in heavy}), gate.TEST_MAX_CPU_COUNT)

    def test_dry_run_runs_no_command(self):
        runner = mock.Mock()
        result = gate.run_commands(
            "test", [gate.Command(("would-not-run",))], runner=runner, dry_run=True)

        self.assertEqual(result, 0)
        runner.assert_not_called()

    def test_missing_command_stops_with_127(self):
        commands = [gate.Command(("dotnet",)), gate.Command(("second",))]
        runner = mock.Mock(side_effect=FileNotFoundError(2, "No such file or directory", "dotnet"))
        with tempfile.TemporaryDirectory() as directory:
            result = gate.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 127)
        self.assertEqual(runner.call_count, 1)

    def test_subsystem_error_stops_with_126(self):
        commands = [gate.Command(("dotnet",))]
        runner = mock.Mock(side_effect=OSError("broken pipe"))
        with tempfile.TemporaryDirectory() as directory:
            result = gate.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 126)

    def test_missing_git_returns_none_instead_of_a_traceback(self):
        """`_git()` did not catch the FileNotFoundError of `subprocess.run`;
        without git on PATH, `changed_paths()` crashed with a traceback."""
        with mock.patch("subprocess.run", side_effect=FileNotFoundError("git")):
            self.assertIsNone(gate.changed_paths())


def _slnx_test_modules(root: pathlib.Path = ROOT) -> list[str]:
    """The project names of the test modules that Tracon.slnx holds.

    Rule: every project under `tests/` is a test module, except those that reject the xunit/MTP
    binding of `tests/Directory.Build.props` with `IsTestHarnessProject=true` (`Tracon.WorkerHarness`: the
    subject of a two-process proof; it is not a test host and `dotnet test` cannot run it)."""
    modules = []
    for project in ElementTree.parse(root / "Tracon.slnx").getroot().iter("Project"):
        path = pathlib.PurePosixPath(project.get("Path", ""))
        if path.parts[:1] != ("tests",):
            continue
        csproj = (root / path).read_text(encoding="utf-8")
        if re.search(r"<IsTestHarnessProject>\s*true\s*</IsTestHarnessProject>", csproj):
            continue
        modules.append(path.stem)
    return sorted(modules)


def _slnf_projects(path: pathlib.Path) -> list[str]:
    return list(json.loads(path.read_text(encoding="utf-8"))["solution"]["projects"])


def _section_problems(
    modules: list[str], core: str, light: list[str], heavy: dict[str, tuple[str, ...]],
) -> list[str]:
    """Is every test module in EXACTLY ONE place among Core, the light filter and the heavy lanes? A list of problems; empty means fine."""
    places: dict[str, list[str]] = {core: ["core"]}
    for name in light:
        places.setdefault(name, []).append("light filter")
    for lane, names in heavy.items():
        for name in names:
            places.setdefault(name, []).append(f"heavy lane {lane}")
    problems = [f"{name}: not in any stage" for name in modules if name not in places]
    problems += [f"{name}: in more than one place ({', '.join(y)})" for name, y in sorted(places.items()) if len(y) > 1]
    problems += [f"{name}: not a test module in Tracon.slnx" for name in sorted(places) if name not in modules]
    return problems


class GroupRunnerTests(unittest.TestCase):
    """`Command.group` / `Command.lane`: independent steps run at the same time; output, measurement and verdict stay in list order."""

    @staticmethod
    def _execute(commands, behavior, *, measurement_patch=None):
        """`behavior(name)` -> (exit code, output). Returns: (result, screen, start order, measurement lines)."""
        started_names: list[str] = []
        lock = threading.Lock()

        def runner(args, **kwargs):
            with lock:
                started_names.append(args[0])
            code, output_dir = behavior(args[0])
            return mock.Mock(returncode=code, stdout=output_dir)

        with tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()) as screen:
            path = pathlib.Path(folder) / "m.jsonl"
            if measurement_patch is None:
                result = gate.run_commands("test", commands, runner=runner, measurement_path=path)
            else:
                with mock.patch.object(gate, "_record_measurement", side_effect=measurement_patch):
                    result = gate.run_commands("test", commands, runner=runner, measurement_path=path)
            lines = [json.loads(s) for s in path.read_text(encoding="utf-8").splitlines()] if path.exists() else []
        return result, screen.getvalue(), started_names, lines

    @staticmethod
    def _group(*names, group="g"):
        return [gate.Command((name,), group=group) for name in names]

    def test_output_is_printed_in_list_order_not_finish_order(self):
        finished = {name: threading.Event() for name in ("c1", "c2")}
        finish_order: list[str] = []

        def behavior(name):
            if name == "c0":
                for event in finished.values():
                    event.wait(5)
            else:
                finished[name].set()
            finish_order.append(name)
            return 0, f"OUTPUT-{name}\n"

        result, screen, _, _ = self._execute(self._group("c0", "c1", "c2"), behavior)

        self.assertEqual(result, 0)
        self.assertEqual(finish_order[-1], "c0", "c0 did not wait for the others: the commands do not run at the same time")
        self.assertLess(screen.index("OUTPUT-c0"), screen.index("OUTPUT-c1"))
        self.assertLess(screen.index("OUTPUT-c1"), screen.index("OUTPUT-c2"))

    def test_commands_in_a_group_really_run_on_separate_threads(self):
        threads: set[int] = set()

        def behavior(name):
            threads.add(threading.get_ident())
            return 0, ""

        self._execute(self._group("c0", "c1", "c2"), behavior)

        self.assertNotIn(threading.get_ident(), threads)

    def test_first_red_in_list_order_decides_not_the_first_to_finish(self):
        c1_finished = threading.Event()

        def behavior(name):
            if name == "c0":
                c1_finished.wait(5)
                return 3, ""
            if name == "c1":
                c1_finished.set()
                return 5, ""
            return 0, ""

        result, screen, _, lines = self._execute(self._group("c0", "c1", "c2"), behavior)

        self.assertEqual(result, 3)
        self.assertEqual([(s["command"], s["exit_code"]) for s in lines], [("c0", 3), ("c1", 5), ("c2", 0)])
        self.assertIn("Exit 5", screen)  # the sibling's red is visible too, but it does not change the verdict

    def test_red_step_does_not_orphan_its_siblings(self):
        released = threading.Event()
        completed: list[str] = []
        threading.Timer(0.3, released.set).start()

        def behavior(name):
            if name == "c0":
                return 1, ""
            released.wait(10)
            completed.append(name)
            return 0, ""

        result, _, started_names, lines = self._execute(self._group("c0", "c1", "c2"), behavior)

        self.assertEqual(result, 1)
        self.assertCountEqual(started_names, ["c0", "c1", "c2"])
        self.assertCountEqual(completed, ["c1", "c2"], "returned after the red step but the siblings had not finished")
        self.assertEqual(len(lines), 3)

    def test_measurement_lines_are_written_from_the_main_thread_in_list_order(self):
        writers: list[tuple[str, bool]] = []

        def record(stage, command, started, exit_code, path, *, finished=None):
            writers.append((command.args[0], threading.current_thread() is threading.main_thread()))

        def behavior(name):
            time.sleep({"c0": 0.2, "c1": 0.0, "c2": 0.1}[name])
            return 0, ""

        self._execute(self._group("c0", "c1", "c2"), behavior, measurement_patch=record)

        self.assertEqual(writers, [("c0", True), ("c1", True), ("c2", True)])

    def test_measurement_line_carries_the_commands_own_duration_not_its_wait_in_line(self):
        def behavior(name):
            time.sleep(0.4 if name == "c0" else 0.0)
            return 0, ""

        _, _, _, lines = self._execute(self._group("c0", "c1"), behavior)

        durations = {s["command"]: s["duration_seconds"] for s in lines}
        self.assertGreaterEqual(durations["c0"], 0.3)
        self.assertLess(durations["c1"], 0.2)  # c1 was printed after c0 but writes its own duration

    def test_commands_in_a_lane_run_in_order_while_lanes_run_at_the_same_time(self):
        lock = threading.Lock()
        log: list[str] = []
        b1_started = threading.Event()

        def behavior(name):
            with lock:
                log.append(f"start {name}")
            if name == "a1":
                b1_started.wait(5)  # a1 finishes only after b1 has started: the lanes run at the same time
            if name == "b1":
                b1_started.set()
            with lock:
                log.append(f"finish {name}")
            return 0, ""

        commands = [
            gate.Command(("a1",), group="g", lane="a"), gate.Command(("a2",), group="g", lane="a"),
            gate.Command(("b1",), group="g", lane="b"), gate.Command(("b2",), group="g", lane="b"),
        ]
        result, _, _, _ = self._execute(commands, behavior)

        self.assertEqual(result, 0)
        self.assertLess(log.index("start b1"), log.index("finish a1"))
        self.assertLess(log.index("finish a1"), log.index("start a2"))
        self.assertLess(log.index("finish b1"), log.index("start b2"))

    def test_red_lane_does_not_start_later_commands_but_every_lane_starts_its_first(self):
        a1_recorded = threading.Event()

        def record(stage, command, started, exit_code, path, *, finished=None):
            if command.args[0] == "a1":
                a1_recorded.set()

        def behavior(name):
            if name == "a1":
                return 4, ""
            if name == "b1":
                a1_recorded.wait(5)  # b1 finishes only after a1 has been counted red
            return 0, ""

        commands = [
            gate.Command(("a1",), group="g", lane="a"), gate.Command(("b1",), group="g", lane="b"),
            gate.Command(("b2",), group="g", lane="b"), gate.Command(("a2",), group="g", lane="a"),
        ]
        result, screen, started_names, lines = self._execute(commands, behavior, measurement_patch=record)

        self.assertEqual(result, 4)
        self.assertCountEqual(started_names, ["a1", "b1"])
        self.assertEqual(screen.count("skipped"), 2)
        self.assertEqual(lines, [])  # the patch writes no line; the measurement itself is tested separately

    def test_long_running_group_is_not_silent_and_names_the_commands_still_running(self):
        def behavior(name):
            time.sleep(0.4 if name == "slow" else 0.0)
            return 0, ""

        with mock.patch.object(gate, "GROUP_HEARTBEAT_SECONDS", 0.05):
            result, screen, _, _ = self._execute(self._group("slow", "fast"), behavior)

        self.assertEqual(result, 0)
        self.assertRegex(screen, r"… still running \(\d+ s\): slow")

    def test_missing_command_in_a_group_gives_127_and_nobody_is_left_waiting(self):
        def runner(args, **kwargs):
            if args[0] == "c1":
                raise FileNotFoundError(2, "No such file or directory", "c1")
            return mock.Mock(returncode=0, stdout="")

        with tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()):
            result = gate.run_commands(
                "test", self._group("c0", "c1"), runner=runner, measurement_path=pathlib.Path(folder) / "m.jsonl")

        self.assertEqual(result, 127)

    def test_group_does_not_hang_when_the_runner_crashes(self):
        def runner(args, **kwargs):
            if args[0] == "c1":
                raise ValueError("unexpected")
            return mock.Mock(returncode=0, stdout="")

        with tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()) as screen:
            result = gate.run_commands(
                "test", self._group("c0", "c1"), runner=runner, measurement_path=pathlib.Path(folder) / "m.jsonl")

        self.assertEqual(result, gate.INTERNAL_ERROR_EXIT_CODE)
        self.assertIn("unexpected", screen.getvalue())

    def test_group_does_not_change_the_commands_display(self):
        self.assertEqual(gate.Command(("x", "y"), group="g", lane="a").display, gate.Command(("x", "y")).display)

    def test_same_group_name_forms_one_block_only_when_adjacent(self):
        commands = [
            *self._group("g1", "g2"), gate.Command(("plain",)), *self._group("g3", "g4"),
            gate.Command(("single",), group="other"),
        ]

        self.assertEqual(
            [[command.args[0] for command in block] for block in gate._blocks(commands)],
            [["g1", "g2"], ["plain"], ["g3", "g4"], ["single"]])

    def test_ungrouped_commands_run_in_order_and_stop_at_the_first_red_as_before(self):
        result, _, started_names, _ = self._execute(
            [gate.Command((name,)) for name in ("s0", "s1", "s2")], lambda name: (7 if name == "s1" else 0, ""))

        self.assertEqual(result, 7)
        self.assertEqual(started_names, ["s0", "s1"])

    def test_dry_listing_marks_groups_and_lanes_as_comment_lines(self):
        commands = [
            gate.Command(("before",)),
            gate.Command(("a1",), group="g", lane="a"), gate.Command(("b1",), group="g", lane="b"),
            gate.Command(("a2",), group="g", lane="a"),
            gate.Command(("after",)),
        ]
        runner = mock.Mock()
        with contextlib.redirect_stdout(io.StringIO()) as screen:
            result = gate.run_commands("test", commands, runner=runner, dry_run=True)

        lines = screen.getvalue().splitlines()
        self.assertEqual(result, 0)
        runner.assert_not_called()
        self.assertEqual(
            [line[2:] for line in lines if line.startswith("$ ")],
            [command.display for command in commands])
        self.assertTrue(any(line.startswith("# parallel group") for line in lines))
        self.assertTrue(any(line.startswith("# group end") for line in lines))
        # A lane marker is written where the lane changes in the list (a1, b1, a2 -> a, b, a).
        self.assertEqual(
            [line for line in lines if line.startswith("# lane ")], ["# lane a", "# lane b", "# lane a"])


class StagedTestRunTests(unittest.TestCase):
    """The closing's test run: Core alone, the light filter, two heavy lanes; a single TRX check; stop at the first red."""

    def _execute(self, commands, exit_code_for=lambda args: 0, *, zero_tests=0, limit_code=0):
        calls: list[tuple[tuple[str, ...], float]] = []
        lock = threading.Lock()

        def runner(args, **kwargs):
            with lock:
                calls.append((tuple(args), time.time()))
            return mock.Mock(returncode=exit_code_for(tuple(args)), stdout="")

        with mock.patch.object(gate, "require_executed_tests", return_value=zero_tests) as required, \
                mock.patch.object(gate, "enforce_module_time_limits", return_value=limit_code) as limit, \
                mock.patch.object(gate, "record_test_durations") as duration, \
                mock.patch.object(gate, "isolate_failed_tests", return_value=[]) as isolate, \
                mock.patch.object(gate, "failed_tests_from_trx", return_value=[]) as failed, \
                tempfile.TemporaryDirectory() as folder, contextlib.redirect_stdout(io.StringIO()):
            path = pathlib.Path(folder) / "m.jsonl"
            result = gate.run_commands("test", commands, runner=runner, measurement_path=path)
            lines = [json.loads(s) for s in path.read_text(encoding="utf-8").splitlines()] if path.exists() else []
        return types.SimpleNamespace(
            result=result, calls=calls, lines=lines, required=required, limit=limit, duration=duration,
            isolate=isolate, failed=failed)

    @staticmethod
    def _name_containing(*pieces):
        return lambda args: 2 if any(piece in " ".join(args) for piece in pieces) else 0

    # --- order and structure -------------------------------------------------------

    def test_core_alone_is_the_first_command_then_light_then_heavy(self):
        commands = gate.staged_test_commands()

        self.assertEqual(
            commands[0].args[:4],
            ("dotnet", "test", "--project", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))
        self.assertEqual(commands[1].args[:4], ("dotnet", "test", "--solution", "Tracon.fast.slnf"))
        self.assertIsNone(commands[0].group)
        self.assertIsNone(commands[1].group)
        self.assertTrue(all(command.group == gate.HEAVY_TEST_GROUP for command in commands[2:]))
        self.assertEqual(len(commands), 2 + sum(len(names) for names in gate.HEAVY_TEST_LANES.values()))

    def test_docker_modules_run_last_on_the_heavy_lanes(self):
        commands = gate.staged_test_commands()
        for module in ("TwoNode", "SqlServer", "PostgreSql"):
            with self.subTest(module=module):
                place = [i for i, command in enumerate(commands) if f"Tracon.{module}.IntegrationTests" in command.display]
                self.assertEqual(len(place), 1)
                self.assertGreaterEqual(place[0], 2)
        self.assertNotIn("Tracon.fast.slnf", " ".join(" ".join(k.args) for k in commands[2:]))

    def test_sql_server_and_two_node_share_a_lane_so_they_never_run_at_the_same_time(self):
        lane_of = {
            name: lane for lane, names in gate.HEAVY_TEST_LANES.items() for name in names}

        self.assertEqual(lane_of["Tracon.SqlServer.IntegrationTests"], lane_of["Tracon.TwoNode.IntegrationTests"])
        commands = {k.display: k for k in gate.staged_test_commands() if k.group}
        for command in commands.values():
            project = pathlib.PurePath(gate._test_target(command.args)).stem
            self.assertEqual(command.lane, lane_of[project], project)

    def test_lane_count_is_the_number_of_modules_running_at_the_same_time(self):
        self.assertEqual(len(gate.HEAVY_TEST_LANES), gate.TEST_MAX_CPU_COUNT)

    def test_every_stage_carries_the_trx_flags(self):
        for command in gate.staged_test_commands():
            with self.subTest(command=command.display):
                args = command.args
                self.assertTrue(gate.reports_trx(command))
                self.assertNotIn("--", args)
                self.assertEqual(args[args.index("-c") + 1], "Release")
                self.assertIn("--no-build", args)
                self.assertEqual(args[args.index("--results-directory") + 1], gate.TEST_RESULTS_DIRECTORY)
                self.assertEqual(args[args.index("--results-directory-layout") + 1], "per-module")

    def test_inner_loop_full_run_command_stays_a_single_solution_command(self):
        with contextlib.redirect_stdout(io.StringIO()):
            commands = gate.inner_loop_commands(["src/Unknown.Package/Thing.cs"])[1:]

        self.assertEqual([k.display for k in commands], [gate.full_solution_test_command().display])

    def test_closing_tests_are_contiguous_at_the_end_of_the_list_with_test_free_steps_before_them(self):
        commands = gate.closing_commands("abc123", site=True, performance=True)
        tests = gate.staged_test_commands()

        self.assertEqual(commands[-len(tests):], tests)
        self.assertFalse(any(k.display.startswith("dotnet test") for k in commands[:-len(tests)]))

    # --- section guard -------------------------------------------------------------

    def test_slnf_heavy_list_and_core_cover_every_test_module_exactly_once(self):
        light = _slnf_projects(ROOT / gate.FAST_TEST_SOLUTION_FILTER)

        self.assertTrue(all(path.startswith("tests/") for path in light), "the light filter holds only test projects")
        for path in light:
            self.assertTrue((ROOT / path).is_file(), path)
        self.assertEqual(
            json.loads((ROOT / gate.FAST_TEST_SOLUTION_FILTER).read_text(encoding="utf-8"))["solution"]["path"],
            "Tracon.slnx")
        self.assertEqual(
            _section_problems(
                _slnx_test_modules(), gate.CORE_TEST_PROJECT,
                [pathlib.PurePosixPath(path).stem for path in light], gate.HEAVY_TEST_LANES),
            [])

    def test_section_guard_turns_red_for_a_new_module_listed_nowhere(self):
        modules = ["Tracon.A.Tests", "Tracon.B.Tests", "Tracon.New.Tests"]

        problems = _section_problems(modules, "Tracon.A.Tests", ["Tracon.B.Tests"], {"a": ()})

        self.assertEqual(problems, ["Tracon.New.Tests: not in any stage"])

    def test_section_guard_catches_a_module_listed_twice_and_an_unknown_one(self):
        modules = ["Tracon.A.Tests", "Tracon.B.Tests"]

        problems = _section_problems(
            modules, "Tracon.A.Tests", ["Tracon.B.Tests", "Tracon.Typo.Tests"], {"a": ("Tracon.B.Tests",)})

        self.assertEqual(
            problems,
            ["Tracon.B.Tests: in more than one place (light filter, heavy lane a)",
             "Tracon.Typo.Tests: not a test module in Tracon.slnx"])

    def test_helper_project_is_not_a_test_module(self):
        modules = _slnx_test_modules()

        self.assertNotIn("Tracon.WorkerHarness", modules)
        self.assertIn(gate.CORE_TEST_PROJECT, modules)

    # --- run: a single TRX check, stop at the first red ------------------------------

    def test_green_run_calls_the_trx_hooks_once_with_a_time_from_before_the_first_command(self):
        result = self._execute(gate.staged_test_commands())

        self.assertEqual(result.result, 0)
        self.assertEqual(len(result.calls), len(gate.staged_test_commands()))
        for hook in (result.required, result.limit, result.duration):
            hook.assert_called_once()
        since = result.duration.call_args.args[0]
        self.assertEqual(result.required.call_args.args[0], since)
        self.assertEqual(result.limit.call_args.args[0], since)
        self.assertLessEqual(since, min(timestamp for _, timestamp in result.calls))
        result.isolate.assert_not_called()
        self.assertEqual([s["exit_code"] for s in result.lines], [0] * len(gate.staged_test_commands()))

    def test_red_core_does_not_start_later_stages_but_the_diagnosis_hooks_run(self):
        result = self._execute(gate.staged_test_commands(), self._name_containing("Tracon.Core.UnitTests"))

        self.assertEqual(result.result, 2)
        self.assertEqual(len(result.calls), 1)
        self.assertIn("Tracon.Core.UnitTests", " ".join(result.calls[0][0]))
        result.required.assert_not_called()
        result.limit.assert_not_called()
        result.duration.assert_called_once()
        result.isolate.assert_called_once()
        # Measurement: only the Core line, with its own exit code; no line for the heavy stage.
        self.assertEqual(len(result.lines), 1)
        self.assertEqual(result.lines[0]["exit_code"], 2)
        self.assertIn("Tracon.Core.UnitTests", result.lines[0]["command"])

    def test_red_light_stage_does_not_start_the_heavy_lanes(self):
        result = self._execute(gate.staged_test_commands(), self._name_containing("Tracon.fast.slnf"))

        self.assertEqual(result.result, 2)
        self.assertEqual(len(result.calls), 2)
        result.duration.assert_called_once()
        result.isolate.assert_called_once()

    def test_red_heavy_module_stops_its_lane_and_keeps_the_exit_code_the_time_limit_does_not_override_it(self):
        result = self._execute(
            gate.staged_test_commands(), self._name_containing("Tracon.SqlServer.IntegrationTests"), limit_code=90)

        executed = [" ".join(args) for args, _ in result.calls]
        self.assertEqual(result.result, 2)
        self.assertFalse(any("Tracon.Ui.E2ETests" in line for line in executed), "the next command of the red lane started")
        result.limit.assert_not_called()
        result.duration.assert_called_once()
        result.isolate.assert_called_once()

    def test_module_time_limit_gives_its_exit_code_and_writes_one_line_when_the_stages_are_green(self):
        result = self._execute(gate.staged_test_commands(), limit_code=gate.MODULE_TIME_LIMIT_EXIT_CODE)

        self.assertEqual(result.result, gate.MODULE_TIME_LIMIT_EXIT_CODE)
        result.limit.assert_called_once()
        result.duration.assert_called_once()
        checks = [s for s in result.lines if s["command"] == gate.TRX_CHECK.display]
        self.assertEqual([s["exit_code"] for s in checks], [gate.MODULE_TIME_LIMIT_EXIT_CODE])
        self.assertEqual(
            [s["exit_code"] for s in result.lines if s["command"] != gate.TRX_CHECK.display],
            [0] * len(gate.staged_test_commands()))

    def test_green_stages_that_ran_zero_tests_count_as_red(self):
        result = self._execute(gate.staged_test_commands(), zero_tests=gate.ZERO_TESTS_EXIT_CODE)

        self.assertEqual(result.result, gate.ZERO_TESTS_EXIT_CODE)
        result.required.assert_called_once()
        result.limit.assert_not_called()

    def test_a_step_without_tests_separates_two_test_runs_and_each_run_calls_its_own_hooks(self):
        trx = gate.full_solution_test_command()
        result = self._execute([trx, gate.Command(("dotnet", "build", "Tracon.slnx")), trx])

        self.assertEqual(result.result, 0)
        self.assertEqual(result.required.call_count, 2)
        self.assertEqual(result.duration.call_count, 2)

    def test_group_that_wants_no_trx_does_not_call_the_test_hooks(self):
        result = self._execute([gate.Command((name,), group="g") for name in ("c0", "c1")])

        self.assertEqual(result.result, 0)
        result.required.assert_not_called()
        result.duration.assert_not_called()


class PerformanceGateTests(unittest.TestCase):
    """`scripts/gate.py perf` - PURE Python tests of the comparison and baseline
    reading logic. The real benchmark run that needs `dotnet run` and the
    deliberate-regression run are not covered here."""

    def test_growing_allocation_turns_red_and_names_the_method_and_bytes(self):
        baseline = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}
        current = {"A": {"allocatedBytes": 132, "meanNanoseconds": 1.0}}

        exit_code, messages = gate.compare_allocations(baseline, current)

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("A" in message and "132" in message and "+32" in message for message in messages))

    def test_shrinking_allocation_stays_green_but_warns_to_update(self):
        baseline = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}
        current = {"A": {"allocatedBytes": 80, "meanNanoseconds": 1.0}}

        exit_code, messages = gate.compare_allocations(baseline, current)

        self.assertEqual(exit_code, 0)
        self.assertTrue(any("update the baseline" in message for message in messages))

    def test_equal_allocation_passes(self):
        baseline = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}
        current = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}

        exit_code, _messages = gate.compare_allocations(baseline, current)

        self.assertEqual(exit_code, 0)

    def test_benchmark_missing_from_the_baseline_turns_red(self):
        exit_code, messages = gate.compare_allocations({}, {"New": {"allocatedBytes": 10, "meanNanoseconds": 1.0}})

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("New" in message and "not in the baseline" in message for message in messages))

    def test_benchmark_that_produced_no_measurement_turns_red_with_a_clear_message(self):
        # 2026-10-11: when BenchmarkDotNet found a second .csproj with the same name
        # (a copy of the repository inside the tree, such as a git worktree) it could not produce
        # the boilerplate; the report carried `BytesAllocatedPerOperation: null` and the gate
        # crashed with a TypeError instead of a meaningful message.
        report = {"Benchmarks": [{"FullName": "A", "Statistics": None,
                                  "Memory": {"BytesAllocatedPerOperation": None}}]}
        current = gate.parse_benchmark_report(report)

        exit_code, messages = gate.compare_allocations({"A": {"allocatedBytes": 24, "meanNanoseconds": 1.0}}, current)

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("A" in message and "produced no measurement" in message for message in messages))

    def test_benchmark_not_produced_in_this_run_turns_red(self):
        exit_code, messages = gate.compare_allocations({"Old": {"allocatedBytes": 10, "meanNanoseconds": 1.0}}, {})

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("Old" in message and "not produced" in message for message in messages))

    def test_missing_baseline_file_gives_a_clear_error(self):
        with tempfile.TemporaryDirectory() as directory:
            missing = pathlib.Path(directory) / "does-not-exist.json"

            with self.assertRaises(gate.BaselineError):
                gate.load_baseline(missing)

    def test_malformed_baseline_file_gives_a_clear_error(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "baseline.json"
            path.write_text("{ this is not valid json", encoding="utf-8")

            with self.assertRaises(gate.BaselineError):
                gate.load_baseline(path)

    def test_baseline_without_the_benchmarks_key_gives_a_clear_error(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "baseline.json"
            path.write_text('{"somethingElse": true}', encoding="utf-8")

            with self.assertRaises(gate.BaselineError):
                gate.load_baseline(path)

    def test_allocation_and_duration_are_extracted_from_the_benchmarkdotnet_json_report(self):
        report = {
            "Benchmarks": [
                {
                    "FullName": "Tracon.Benchmarks.RunEventWriterBenchmarks.AppendEvent",
                    "Memory": {"BytesAllocatedPerOperation": 176},
                    "Statistics": {"Mean": 123.4},
                },
            ],
        }

        parsed = gate.parse_benchmark_report(report)

        self.assertEqual(
            parsed["Tracon.Benchmarks.RunEventWriterBenchmarks.AppendEvent"],
            {"allocatedBytes": 176, "meanNanoseconds": 123.4})

    def test_reports_of_several_benchmark_classes_are_merged(self):
        """BenchmarkDotNet writes a separate *-report-full.json for EVERY benchmark CLASS,
        not a single file for the whole run - both must be read."""
        with tempfile.TemporaryDirectory() as directory:
            report_dir = pathlib.Path(directory) / "results"
            report_dir.mkdir()
            (report_dir / "A-report-full.json").write_text(json.dumps({
                "Benchmarks": [{"FullName": "A.M", "Memory": {"BytesAllocatedPerOperation": 1}, "Statistics": {"Mean": 1.0}}],
            }), encoding="utf-8")
            (report_dir / "B-report-full.json").write_text(json.dumps({
                "Benchmarks": [{"FullName": "B.M", "Memory": {"BytesAllocatedPerOperation": 2}, "Statistics": {"Mean": 2.0}}],
            }), encoding="utf-8")

            found = gate._benchmark_report_files(report_dir)

        self.assertEqual([path.name for path in found], ["A-report-full.json", "B-report-full.json"])

    def test_baseline_write_and_read_round_trip(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "baseline.json"
            benchmarks = {"A": {"allocatedBytes": 42, "meanNanoseconds": 3.5}}

            gate.write_baseline(benchmarks, path)
            loaded = gate.load_baseline(path)

        self.assertEqual(loaded["benchmarks"], benchmarks)

    def _write_report(self, report_dir: pathlib.Path, file_name: str, full_name: str, allocated_bytes: int) -> None:
        report_dir.mkdir(parents=True, exist_ok=True)
        (report_dir / file_name).write_text(json.dumps({
            "Benchmarks": [{
                "FullName": full_name,
                "Memory": {"BytesAllocatedPerOperation": allocated_bytes},
                "Statistics": {"Mean": 1.0},
            }],
        }), encoding="utf-8")

    def test_performance_gate_returns_the_exit_code_of_a_failing_dotnet_run(self):
        """The orchestration function itself was never tested, only the pure
        comparison logic. `runner` can already be injected - the same pattern as in
        the `run_commands` tests."""
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            gate.write_baseline({"A": {"allocatedBytes": 1, "meanNanoseconds": 1.0}}, baseline_path)
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            exit_code = gate.performance_gate(
                runner=runner,
                baseline_path=baseline_path,
                report_dir=pathlib.Path(directory) / "results")

        self.assertEqual(exit_code, 1)
        runner.assert_called_once()

    def test_performance_gate_gives_a_clear_error_when_no_report_is_produced(self):
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            gate.write_baseline({"A": {"allocatedBytes": 1, "meanNanoseconds": 1.0}}, baseline_path)
            runner = mock.Mock(return_value=mock.Mock(returncode=0))

            exit_code = gate.performance_gate(
                runner=runner,
                baseline_path=baseline_path,
                report_dir=pathlib.Path(directory) / "results")

        self.assertEqual(exit_code, 1)

    def test_performance_gate_compares_end_to_end_without_running_a_real_dotnet(self):
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            report_dir = pathlib.Path(directory) / "results"
            gate.write_baseline({"A.M": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}, baseline_path)

            def fake_runner(*args, **kwargs):
                self._write_report(report_dir, "A-report-full.json", "A.M", 132)
                return mock.Mock(returncode=0)

            exit_code = gate.performance_gate(
                runner=fake_runner, baseline_path=baseline_path, report_dir=report_dir)

        self.assertEqual(exit_code, 1)

    def test_performance_gate_update_writes_the_baseline(self):
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            report_dir = pathlib.Path(directory) / "results"

            def fake_runner(*args, **kwargs):
                self._write_report(report_dir, "A-report-full.json", "A.M", 55)
                return mock.Mock(returncode=0)

            exit_code = gate.performance_gate(
                update=True, runner=fake_runner, baseline_path=baseline_path, report_dir=report_dir)
            written = gate.load_baseline(baseline_path)

        self.assertEqual(exit_code, 0)
        self.assertEqual(written["benchmarks"]["A.M"]["allocatedBytes"], 55)


class ReleaseTests(unittest.TestCase):
    """`scripts/gate.py release` - the pure Python logic. The end-to-end behavior that
    needs `dotnet pack` lives in `ReleaseArtifactTests.cs`."""

    def _write_csproj(self, root: pathlib.Path, project_id: str, body: str = "") -> None:
        directory = root / "src" / project_id
        directory.mkdir(parents=True)
        (directory / f"{project_id}.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk">\n{body}\n</Project>\n', encoding="utf-8")
        _write_src_build_props(root)

    def test_packable_project_ids_exclude_ispackable_false(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Core")
            self._write_csproj(root, "Tracon.Generators", "<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>")

            self.assertEqual(gate.packable_project_ids(root), ["Tracon.Core"])

    def test_package_profile_tells_tool_content_meta_and_library_apart(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Cli", "<PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup>")
            self._write_csproj(
                root, "Tracon.Templates",
                "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput><PackageType>Template</PackageType></PropertyGroup>")
            self._write_csproj(root, "Tracon", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")
            self._write_csproj(root, "Tracon.Core")

            self.assertEqual(gate._package_profile(root, "Tracon.Cli"), "tool")
            self.assertEqual(gate._package_profile(root, "Tracon.Templates"), "content")
            self.assertEqual(gate._package_profile(root, "Tracon"), "meta")
            self.assertEqual(gate._package_profile(root, "Tracon.Core"), "library")

    def test_naming_own_version_does_not_mix_up_another_package(self):
        """The file of another package whose id starts with the 'Tracon.' prefix
        ("Tracon.Core...") must not be taken as the package's own."""
        self.assertTrue(gate._names_own_version("Tracon", "Tracon.1.0.0-preview.1.nupkg"))
        self.assertFalse(gate._names_own_version("Tracon", "Tracon.Core.1.0.0-preview.1.nupkg"))
        self.assertTrue(gate._names_own_version("Tracon.Core", "Tracon.Core.1.0.0-preview.1.nupkg"))

    def test_when_a_version_is_requested_the_exact_file_name_is_looked_up(self):
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)
            (release_dir / "Tracon.Core.1.0.0-preview.1.nupkg").write_bytes(b"")

            found = gate._resolve_nupkg(release_dir, "Tracon.Core", "1.0.0-preview.1")
            missing = gate._resolve_nupkg(release_dir, "Tracon.Core", "1.0.0-preview.2")

        self.assertIsNotNone(found)
        self.assertIsNone(missing)

    def test_when_no_version_is_requested_the_most_recently_written_own_file_is_chosen(self):
        """An older "most recently written" selection could pick a stale file left over from an
        EARLIER --package-version run, because an incremental `dotnet pack` may not regenerate the
        output of an unchanged project. Keeping stale files out of the directory is the job of the
        staging step; here only the choice of the CORRECT (newest) one of the two remaining
        candidates is measured."""
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)
            old = release_dir / "Tracon.Core.1.0.0-preview.1.nupkg"
            new = release_dir / "Tracon.Core.0.0.0-preview.0.400.nupkg"
            old.write_bytes(b"")
            new.write_bytes(b"")
            os.utime(old, (1, 1))
            os.utime(new, (2, 2))

            found = gate._resolve_nupkg(release_dir, "Tracon.Core", None)

        self.assertEqual(found, new)

    def test_target_frameworks_read_the_single_project_override(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Testing", "<PropertyGroup><TargetFrameworks>net10.0</TargetFrameworks></PropertyGroup>")
            self._write_csproj(root, "Tracon.Core")
            _write_src_build_props(root, "net10.0;net11.0")

            self.assertEqual(gate._target_frameworks(root, "Tracon.Testing"), ("net10.0",))
            # The default is READ from the props; it is not repeated in the script.
            self.assertEqual(gate._target_frameworks(root, "Tracon.Core"), ("net10.0", "net11.0"))

            (root / "src" / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                gate._target_frameworks(root, "Tracon.Core")

    @staticmethod
    def _write_fake_nupkg(path: pathlib.Path, *, content: bytes, psmdcp_guid: str = "0" * 32) -> None:
        """A minimal but REAL zip, shaped enough to exercise `_content_fingerprint`:
        one meaningful entry plus the two OPC entries NuGet regenerates with a
        random name on every real `dotnet pack` (see `_VOLATILE_OPC_ENTRY`)."""
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("lib/net10.0/Thing.dll", content)
            archive.writestr("_rels/.rels", f"<Relationships><Relationship Target=\"/package/services/metadata/core-properties/{psmdcp_guid}.psmdcp\" /></Relationships>")
            archive.writestr(f"package/services/metadata/core-properties/{psmdcp_guid}.psmdcp", "<coreProperties />")

    # The "a stale package is cleaned" claim (the old `_clean_stale_packages` silently
    # deleted everything with the same identity BEFORE the run) gave way to the claim
    # "the same identity with different content is not promoted" - moving from staging to
    # release_dir now requires passing a content-fingerprint comparison
    # (not the raw SHA-256 - see `_content_fingerprint`).
    def test_new_package_moves_from_the_staging_directory_to_the_release_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            name = "Tracon.Core.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / name, content=b"content")

            conflicts = gate._promote_staged_packages(staging_dir, release_dir, [name])

            self.assertEqual(conflicts, [])
            self.assertTrue((release_dir / name).exists())
            self.assertFalse((staging_dir / name).exists())

    def test_same_content_fingerprint_passes_as_a_deterministic_no_op(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            release_dir.mkdir()
            name = "Tracon.Core.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / name, content=b"same-content")
            self._write_fake_nupkg(release_dir / name, content=b"same-content")

            conflicts = gate._promote_staged_packages(staging_dir, release_dir, [name])

            self.assertEqual(conflicts, [])

    def test_different_random_opc_name_alone_is_not_a_conflict(self):
        """🚨 Measured (2026-09-03): on EVERY `dotnet pack` run NuGet writes
        `package/services/metadata/core-properties/<random>.psmdcp` with a
        NEW GUID and `_rels/.rels` carries that name - even packing the same commit
        twice in a row produces a DIFFERENT raw SHA-256. Had the raw file hash
        been compared, the SECOND run of an unchanged version would have been
        rejected every time as a 'different artifact'."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            release_dir.mkdir()
            name = "Tracon.Core.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / name, content=b"same-content", psmdcp_guid="a" * 32)
            self._write_fake_nupkg(release_dir / name, content=b"same-content", psmdcp_guid="b" * 32)
            self.assertNotEqual(gate._sha256(staging_dir / name), gate._sha256(release_dir / name))

            conflicts = gate._promote_staged_packages(staging_dir, release_dir, [name])

            self.assertEqual(conflicts, [])

    def test_different_content_stops_the_run_and_keeps_the_existing_artifact(self):
        """Genuinely different content under the same identity (id+version): no file is
        promoted - not even the non-conflicting one - and the existing release_dir stays
        as it is."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            release_dir.mkdir()
            conflicting = "Tracon.Core.1.0.0-preview.1.nupkg"
            clean = "Tracon.Abstractions.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / conflicting, content=b"new-content")
            self._write_fake_nupkg(release_dir / conflicting, content=b"old-content")
            self._write_fake_nupkg(staging_dir / clean, content=b"non-conflicting")

            conflicts = gate._promote_staged_packages(staging_dir, release_dir, [conflicting, clean])

            with zipfile.ZipFile(release_dir / conflicting) as archive:
                untouched_content = archive.read("lib/net10.0/Thing.dll")

            self.assertEqual(conflicts, [conflicting])
            self.assertEqual(untouched_content, b"old-content")
            self.assertFalse((release_dir / clean).exists())
            self.assertTrue((staging_dir / clean).exists())

    def test_manifest_carries_id_file_and_sha256_for_every_package(self):
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)

            manifest_path = gate._write_manifest(
                release_dir,
                version="1.0.0-preview.1",
                commit="0123456789abcdef0123456789abcdef01234567",
                packages=[
                    {"id": "Tracon.Core", "file": "Tracon.Core.1.0.0-preview.1.nupkg", "sha256": "abc",
                     "symbolsFile": "Tracon.Core.1.0.0-preview.1.snupkg", "symbolsSha256": "def"},
                ],
            )
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

        self.assertEqual(manifest["version"], "1.0.0-preview.1")
        self.assertEqual(manifest["dirty"], False)
        self.assertEqual(manifest["packages"][0]["id"], "Tracon.Core")
        self.assertEqual(manifest["packages"][0]["sha256"], "abc")

    def test_release_rejects_a_dirty_tree_early_and_tries_no_pack(self):
        with mock.patch.object(gate, "_git", return_value=[" M src/Directory.Build.props"]):
            with mock.patch("subprocess.run") as run:
                result = gate.release_rehearsal(None)

        self.assertEqual(result, 1)
        run.assert_not_called()

    def test_release_tries_no_pack_when_the_scan_is_red(self):
        """The tag run executes `scan` and a published tag cannot be taken back.
        Everything the scan would fail on (version-tag drift included) must show up BEFORE the tag,
        in the rehearsal; pack is never entered."""
        with mock.patch.object(gate, "_git", return_value=[]), \
                mock.patch.object(gate, "scan", return_value=1) as scan, \
                mock.patch.object(gate, "_pack_release", return_value=1) as pack:
            result = gate.release_rehearsal(None)

        self.assertEqual(result, 1)
        scan.assert_called_once()
        pack.assert_not_called()

    def test_release_without_git_cannot_resolve_the_baseline_and_tries_no_pack(self):
        """Without git the cleanliness check is skipped with a warning (the MSBuild gate
        is the second line of defence). The baseline step has no second line - red
        instead of a silent skip, and pack is never tried."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "src").mkdir()
            output = io.StringIO()

            with mock.patch.object(gate, "_git", return_value=None), \
                    mock.patch.object(gate, "scan", return_value=0):
                with mock.patch("subprocess.run", side_effect=OSError("no git")) as run:
                    with contextlib.redirect_stdout(output):
                        result = gate.release_rehearsal(None, root=root, release_dir=root / "artifacts" / "package" / "release")

        self.assertEqual(result, 1)
        self.assertIn("The baseline could not be resolved", output.getvalue())
        self.assertFalse(any(call.args[0][0] == "dotnet" for call in run.call_args_list))

    # --- Baseline, isolated restore and the order of the breaking gate -------------

    @staticmethod
    def _breaking_changes_module():
        # The gate.py module picks it up with `import breaking_changes`; the tests must patch the same
        # object (the sys.modules entry), not a copy.
        sys.path.insert(0, str(ROOT / "scripts"))
        return importlib.import_module("breaking_changes")

    def _rehearse(self, root: pathlib.Path, *, finish=None, restore=None, baseline="1.0.0-preview.2", not_run=None,
                  default_release_dir=False):
        bc = self._breaking_changes_module()
        pack_calls: list[list[str]] = []

        def fake_run(command, **kwargs):
            pack_calls.append(list(command))
            return mock.Mock(returncode=0)

        output = io.StringIO()
        resolve = mock.Mock(return_value=baseline) if isinstance(baseline, str) else mock.Mock(side_effect=baseline)
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(gate, "_git", return_value=[]))
            stack.enter_context(mock.patch.object(gate, "scan", return_value=0))
            stack.enter_context(mock.patch.object(bc, "resolve_baseline", resolve))
            stack.enter_context(mock.patch.object(
                bc, "restore_baselines", restore or (lambda ids, version, work: work / "packages")))
            stack.enter_context(mock.patch.object(bc, "baseline_source_violations", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "validation_not_run", not_run or mock.Mock(return_value=[])))
            stack.enter_context(mock.patch("subprocess.run", side_effect=fake_run))
            finish_mock = stack.enter_context(mock.patch.object(
                gate, "_finish_release_rehearsal", finish or mock.Mock(return_value=0)))
            stack.enter_context(contextlib.redirect_stdout(output))
            release_dir = None if default_release_dir else root / "artifacts" / "package" / "release"
            result = gate.release_rehearsal(None, root=root, release_dir=release_dir)
        return result, pack_calls, finish_mock, output.getvalue()

    def _library_root(self, directory: str) -> pathlib.Path:
        root = pathlib.Path(directory)
        self._write_csproj(root, "Tracon.Core")
        self._write_csproj(root, "Tracon", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")
        return root

    def test_unresolvable_baseline_tries_no_pack(self):
        bc = self._breaking_changes_module()
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            result, pack_calls, finish, output = self._rehearse(
                root, baseline=bc.BaselineError("no 'v*' tag is reachable from HEAD"))

        self.assertEqual(result, 1)
        self.assertEqual(pack_calls, [])
        finish.assert_not_called()
        self.assertIn("❌ The baseline could not be resolved: no 'v*' tag is reachable from HEAD", output)

    def test_restore_error_names_the_package_and_tries_no_pack(self):
        bc = self._breaking_changes_module()

        def failing_restore(ids, version, work):
            raise bc.RestoreError(f"baseline packages could not be fetched from nuget.org (v{version}; {', '.join(ids)})")

        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            result, pack_calls, finish, output = self._rehearse(root, restore=failing_restore)

        self.assertEqual(result, 1)
        self.assertEqual(pack_calls, [])
        finish.assert_not_called()
        # Only the baseline of the library package is requested; the meta package has no baseline.
        self.assertIn("(v1.0.0-preview.2; Tracon.Core)", output)

    def test_pack_receives_the_baseline_properties_and_the_report_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            result, pack_calls, finish, output = self._rehearse(root)

            gate = finish.call_args.kwargs["breaking_gate"]
            arguments = pack_calls[0]
            self.assertEqual(result, 0)
            self.assertEqual(len(pack_calls), 1)
            self.assertIn("-p:TraconPackageBaselineVersion=1.0.0-preview.2", arguments)
            self.assertIn(f"-p:TraconApiCompatReportDir={gate.report_dir}", arguments)
            self.assertTrue(any(argument.startswith("-p:TraconPackageBaselineRoot=") for argument in arguments))
            self.assertEqual(gate.report_dir.parent, root / "artifacts" / "package" / "api-compat")
            self.assertEqual(gate.baseline, "1.0.0-preview.2")
            self.assertIn("Baseline: v1.0.0-preview.2 (git describe)", output)
            self.assertIn("Baseline packages from the isolated cache (source api.nuget.org): 1", output)

    def test_report_directory_is_unique_per_run(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            _, _, first, _ = self._rehearse(root)
            _, _, second, _ = self._rehearse(root)

            self.assertNotEqual(first.call_args.kwargs["breaking_gate"].report_dir,
                                second.call_args.kwargs["breaking_gate"].report_dir)

    def test_temporary_directories_are_deleted_on_an_exception_and_nothing_is_promoted(self):
        seen: dict[str, pathlib.Path] = {}

        def exploding_finish(**kwargs):
            seen["report"] = kwargs["breaking_gate"].report_dir
            seen["staging"] = kwargs["staging_dir"]
            raise KeyboardInterrupt

        def restore(ids, version, work):
            seen["work"] = work
            return work / "packages"

        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            with self.assertRaises(KeyboardInterrupt):
                self._rehearse(root, finish=exploding_finish, restore=restore)

            self.assertFalse(seen["report"].exists())
            self.assertFalse(seen["staging"].exists())
            self.assertFalse(seen["work"].exists())
            self.assertFalse((root / "artifacts" / "package" / "release").exists())

    def test_rehearsal_writes_to_the_version_and_commit_directory_by_default_and_leaves_release_alone(self):
        """`release/` is the development feed; old versions there used to fail the rehearsal's sample
        gate AFTER minutes of packing (2026-09-24)."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)
            package_root = root / "artifacts" / "package"
            (package_root / "release").mkdir(parents=True)
            stale = package_root / "release" / "Tracon.Core.1.0.0-preview.2.82.nupkg"
            stale.write_bytes(b"old")

            with mock.patch.object(gate, "PACKAGE_RELEASE_DIR", package_root / "release"), \
                    mock.patch.object(gate, "PACKAGE_REHEARSAL_ROOT", package_root / "rehearsal"), \
                    mock.patch.object(gate, "_head_commit", return_value="0123456789abcdef0123"), \
                    mock.patch.object(gate, "_resolve_package_set", return_value=({}, "1.0.0-preview.3")):
                result, _, finish, output = self._rehearse(root, default_release_dir=True)

            self.assertEqual(result, 0, output)
            self.assertEqual(finish.call_args.kwargs["release_dir"], package_root / "rehearsal" / "1.0.0-preview.3")
            self.assertEqual(finish.call_args.kwargs["staging_dir"].parent, package_root / "staging")
            self.assertEqual(stale.read_bytes(), b"old")
            self.assertIn("Rehearsal output:", output)

    def test_rehearsal_directory_is_per_version(self):
        """Still holds INSIDE the directory: a new commit of the same version lands in the same
        directory and different content is rejected."""
        root = pathlib.Path("/x")

        self.assertEqual(gate.rehearsal_release_dir("1.0.0-preview.3", root), root / "1.0.0-preview.3")
        self.assertNotEqual(gate.rehearsal_release_dir("1.0.0-preview.3", root),
                            gate.rehearsal_release_dir("1.0.0-preview.2", root))

    def test_stale_first_release_flag_tries_no_pack(self):
        bc = self._breaking_changes_module()
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            with mock.patch.object(bc, "stale_first_release_flags", return_value=["Tracon.Voice"]):
                result, pack_calls, _, output = self._rehearse(root)

        self.assertEqual(result, 1)
        self.assertEqual(pack_calls, [])
        self.assertIn("remove TraconPackageFirstRelease: Tracon.Voice", output)

    @staticmethod
    def _write_valid_library_nupkg(staging_dir: pathlib.Path, project_id: str, version: str) -> None:
        """Enough of a real package to pass every metadata check that runs
        before the breaking-change gate, so the gate is what decides."""
        nuspec = (
            f'<package><metadata><id>{project_id}</id><license type="file">LICENSE.md</license>'
            "<requireLicenseAcceptance>true</requireLicenseAcceptance>"
            '<repository type="git" url="https://example.invalid" commit="abcdef1234" />'
            f"<releaseNotes>https://tracon.dev/changelog/{version}</releaseNotes></metadata></package>")
        with zipfile.ZipFile(staging_dir / f"{project_id}.{version}.nupkg", "w") as archive:
            for entry in ("icon.png", "README.md", "LICENSE.md"):
                archive.writestr(entry, "x")
            archive.writestr(f"{project_id}.nuspec", nuspec)
            archive.writestr(f"lib/net10.0/{project_id}.xml", "<doc />")
        (staging_dir / f"{project_id}.{version}.snupkg").write_bytes(b"")

    def _finish(self, root: pathlib.Path, gate_result: int):
        staging_dir = root / "staging"
        staging_dir.mkdir()
        release_dir = root / "release"
        self._write_valid_library_nupkg(staging_dir, "Tracon.Core", "1.0.0-preview.2.47")
        sys.path.insert(0, str(ROOT / "scripts"))
        samples = importlib.import_module("release_extension_samples")
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=root / "reports")
        output = io.StringIO()
        with mock.patch.object(gate, "_check_breaking_changes", return_value=gate_result) as checked, \
                mock.patch.object(gate, "_npm_dry_run", return_value=0), \
                mock.patch.object(samples, "verify", return_value=0), \
                contextlib.redirect_stdout(output):
            result = gate._finish_release_rehearsal(
                root=root, release_dir=release_dir, staging_dir=staging_dir, project_ids=["Tracon.Core"],
                requested_version=None, commit="abcdef1234", breaking_gate=breaking_gate)
        checked.assert_called_once_with(root, breaking_gate, "1.0.0-preview.2.47")
        return result, release_dir, output.getvalue()

    def _finish_packages(self, root: pathlib.Path, packages: dict[str, tuple[str, dict[str, bytes]]]):
        """`_finish_release_rehearsal` over hand-made packages: id -> (nuspec
        dependency XML, extra root entries). The breaking gate is green."""
        staging_dir = root / "staging"
        staging_dir.mkdir()
        release_dir = root / "release"
        # A version that is not in tag form: the CHANGELOG gate is not the subject of these tests.
        version = "1.0.0-preview.2.47"
        for project_id, (dependencies, extra) in packages.items():
            project_dir = root / "src" / project_id
            project_dir.mkdir(parents=True, exist_ok=True)
            (project_dir / f"{project_id}.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
            _write_src_build_props(root)
            self._write_valid_library_nupkg(staging_dir, project_id, version)
            nupkg = staging_dir / f"{project_id}.{version}.nupkg"
            with zipfile.ZipFile(nupkg) as archive:
                entries = {name: archive.read(name) for name in archive.namelist()}
            nuspec = entries[f"{project_id}.nuspec"].decode()
            entries[f"{project_id}.nuspec"] = nuspec.replace(
                "</metadata>", f"<dependencies>{dependencies}</dependencies></metadata>").encode()
            entries.update(extra)
            with zipfile.ZipFile(nupkg, "w") as archive:
                for name, data in entries.items():
                    archive.writestr(name, data)
        sys.path.insert(0, str(ROOT / "scripts"))
        samples = importlib.import_module("release_extension_samples")
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=root / "reports")
        output = io.StringIO()
        with mock.patch.object(gate, "_check_breaking_changes", return_value=0), \
                mock.patch.object(gate, "_npm_dry_run", return_value=0), \
                mock.patch.object(samples, "verify", return_value=0), \
                contextlib.redirect_stdout(output):
            result = gate._finish_release_rehearsal(
                root=root, release_dir=release_dir, staging_dir=staging_dir, project_ids=sorted(packages),
                requested_version=None, commit="abcdef1234", breaking_gate=breaking_gate)
        return result, output.getvalue()

    HOSTING = '<dependency id="Microsoft.Agents.AI.Hosting" version="{0}" exclude="Build,Analyzers" />'

    def test_prerelease_third_party_dependency_with_an_open_lower_bound_turns_red(self):
        """`version="x"` means `>= x`; the published preview.2 restored without a warning
        against Hosting 1.22 and then failed at run time."""
        with tempfile.TemporaryDirectory() as directory:
            result, output = self._finish_packages(pathlib.Path(directory), {
                "Tracon.AspNetCore": (self.HOSTING.format("1.20.0-preview.260831.1"), {})})

        self.assertEqual(result, 1, output)
        self.assertIn("is not an exact range: Microsoft.Agents.AI.Hosting 1.20.0-preview.260831.1", output)

    def test_prerelease_third_party_dependency_with_an_exact_range_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            result, output = self._finish_packages(pathlib.Path(directory), {
                "Tracon.AspNetCore": (
                    self.HOSTING.format("[1.20.0-preview.260831.1]")
                    + self.HOSTING.replace("Hosting", "Hosting.A2A").format(
                        "[1.20.0-preview.260831.1, 1.20.0-preview.260831.1]"), {})})

        self.assertEqual(result, 0, output)

    def test_prerelease_range_with_two_different_bounds_is_not_exact(self):
        self.assertIsNone(gate.EXACT_RANGE_PATTERN.fullmatch("[1.0.0-preview.1, 1.0.0-preview.2]"))
        self.assertIsNone(gate.EXACT_RANGE_PATTERN.fullmatch("[1.0.0-preview.1, )"))
        self.assertIsNotNone(gate.EXACT_RANGE_PATTERN.fullmatch("[1.0.0-preview.1]"))

    def _ui_notice(self, root: pathlib.Path, text: bytes) -> None:
        source = root / gate.THIRD_PARTY_NOTICE_PACKAGES["Tracon.UI"]
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_bytes(text)

    def test_ui_package_without_the_license_notice_turns_red(self):
        """The embedded React/TanStack is packed, and the MIT notice travels with the copy."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(root, {"Tracon.UI": ("", {})})

        self.assertEqual(result, 1, output)
        self.assertIn("THIRD-PARTY-NOTICES.txt is not at the package root", output)

    def test_ui_notice_that_differs_from_the_source_turns_red(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(
                root, {"Tracon.UI": ("", {"THIRD-PARTY-NOTICES.txt": b"old\n"})})

        self.assertEqual(result, 1, output)
        self.assertIn("differs from", output)

    def test_ui_notice_equal_to_the_source_passes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(
                root, {"Tracon.UI": ("", {"THIRD-PARTY-NOTICES.txt": b"react\n"})})

        self.assertEqual(result, 0, output)

    def test_nothing_is_promoted_while_the_breaking_gate_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Core")

            result, release_dir, output = self._finish(root, gate_result=1)

            self.assertEqual(result, 1, output)
            self.assertFalse(release_dir.exists())

    def test_packages_are_promoted_when_the_breaking_gate_is_green(self):
        """Control: the fake package passes every check before the gate - the red result
        above comes from the gate, not from the metadata check."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Core")

            result, release_dir, output = self._finish(root, gate_result=0)

            self.assertEqual(result, 0, output)
            self.assertTrue((release_dir / "Tracon.Core.1.0.0-preview.2.47.nupkg").exists())

    def test_gate_is_red_when_the_version_equals_the_baseline(self):
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/missing"))
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = gate._check_breaking_changes(ROOT, breaking_gate, "1.0.0-preview.2")

        self.assertEqual(result, 1)
        self.assertIn("is not above the baseline (v1.0.0-preview.2)", output.getvalue())

    def test_gate_hands_the_repository_to_the_surface_readers(self):
        """The PublicAPI, `required` and contract readers need the repo whose tag is the baseline."""
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/missing"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes

        import contract_breaks

        with mock.patch.object(breaking_changes, "check", return_value=0) as checked, \
                mock.patch.object(contract_breaks, "check", return_value=0):
            result = gate._check_breaking_changes(ROOT, breaking_gate, "1.0.0-preview.3")

        self.assertEqual(result, 0)
        self.assertEqual(checked.call_args.kwargs["repo"], ROOT)
        self.assertEqual(checked.call_args.kwargs["version"], "1.0.0-preview.3")

    def test_gate_reads_the_contract_surfaces_after_the_apicompat_gate(self):
        """The non-.NET surfaces are read after ApiCompat, with the same baseline, version and repo."""
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/missing"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes
        import contract_breaks

        order: list[str] = []
        with mock.patch.object(breaking_changes, "check", side_effect=lambda **_: order.append("apicompat") or 0), \
                mock.patch.object(contract_breaks, "check", side_effect=lambda **_: order.append("contract") or 0) as surfaces:
            result = gate._check_breaking_changes(ROOT, breaking_gate, "1.0.0-preview.3")

        self.assertEqual(result, 0)
        self.assertEqual(order, ["apicompat", "contract"])
        self.assertEqual(surfaces.call_args.kwargs["repo"], ROOT)
        self.assertEqual(surfaces.call_args.kwargs["baseline"], "1.0.0-preview.2")
        self.assertEqual(surfaces.call_args.kwargs["version"], "1.0.0-preview.3")

    def test_apicompat_red_does_not_run_the_contract_reader(self):
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/missing"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes
        import contract_breaks

        with mock.patch.object(breaking_changes, "check", return_value=1), \
                mock.patch.object(contract_breaks, "check", return_value=0) as surfaces:
            result = gate._check_breaking_changes(ROOT, breaking_gate, "1.0.0-preview.3")

        self.assertEqual(result, 1)
        surfaces.assert_not_called()

    def test_contract_reader_red_turns_the_rehearsal_red(self):
        breaking_gate = gate.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/missing"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes
        import contract_breaks

        with mock.patch.object(breaking_changes, "check", return_value=0), \
                mock.patch.object(contract_breaks, "check", return_value=1):
            result = gate._check_breaking_changes(ROOT, breaking_gate, "1.0.0-preview.3")

        self.assertEqual(result, 1)


class BuildOnceChainTests(unittest.TestCase):
    """`pack` · `verify-packages` · `release --package-directory`.

    A fake repository root carries two packages: `Tracon.Core` (library, with baseline
    validation) and `Tracon` (meta, no `.snupkg`)."""

    COMMIT = "0123456789abcdef0123456789abcdef01234567"
    VERSION = "1.0.0-preview.2.47"
    BASELINE = "1.0.0-preview.2"

    @staticmethod
    def _bc():
        sys.path.insert(0, str(ROOT / "scripts"))
        return importlib.import_module("breaking_changes")

    def _root(self, directory: str) -> pathlib.Path:
        root = pathlib.Path(directory) / "repo"
        for project_id, body in (
                ("Tracon.Core", ""),
                ("Tracon", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")):
            project = root / "src" / project_id
            project.mkdir(parents=True)
            (project / f"{project_id}.csproj").write_text(
                f'<Project Sdk="Microsoft.NET.Sdk">\n{body}\n</Project>\n', encoding="utf-8")
        _write_src_build_props(root)
        return root

    def _write_package(self, directory: pathlib.Path, project_id: str, *, version: str | None = None,
                       commit: str | None = None) -> None:
        version = version or self.VERSION
        commit = commit or self.COMMIT
        library = project_id == "Tracon.Core"
        acceptance = "<requireLicenseAcceptance>true</requireLicenseAcceptance>"
        nuspec = (
            f'<package><metadata><id>{project_id}</id><license type="file">LICENSE.md</license>{acceptance}'
            f'<repository type="git" url="https://example.invalid" commit="{commit}" />'
            f"<releaseNotes>https://tracon.dev/changelog/{version}</releaseNotes></metadata></package>")
        with zipfile.ZipFile(directory / f"{project_id}.{version}.nupkg", "w") as archive:
            for entry in ("icon.png", "README.md", "LICENSE.md"):
                archive.writestr(entry, "x")
            archive.writestr(f"{project_id}.nuspec", nuspec)
            if library:
                archive.writestr(f"lib/net10.0/{project_id}.xml", "<doc />")
        if library:
            with zipfile.ZipFile(directory / f"{project_id}.{version}.snupkg", "w") as archive:
                archive.writestr(f"lib/net10.0/{project_id}.pdb", "pdb")

    def _git(self, *, status=(), commit=None):
        head = commit or self.COMMIT

        def fake(*args):
            if args[0] == "status":
                return list(status)
            if args[:2] == ("rev-parse", "HEAD"):
                return [head]
            return []
        return fake

    def _pack(self, root: pathlib.Path, output: pathlib.Path, *, report: bool = True, status=(), not_run=()):
        """`pack_for_ci` with the dotnet pack replaced by a writer of fake packages."""
        bc = self._bc()
        calls: list[list[str]] = []

        def fake_run(command, **kwargs):
            calls.append(list(command))
            self._write_package(output, "Tracon.Core")
            self._write_package(output, "Tracon")
            if report:
                (output / "api-compat" / "Tracon.Core.xml").write_text("<Suppressions />", encoding="utf-8")
            return mock.Mock(returncode=0)

        text = io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(gate, "_git", side_effect=self._git(status=status)))
            stack.enter_context(mock.patch.object(bc, "resolve_baseline", return_value=self.BASELINE))
            stack.enter_context(mock.patch.object(bc, "stale_first_release_flags", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "restore_baselines", lambda ids, version, work: work / "packages"))
            stack.enter_context(mock.patch.object(bc, "baseline_source_violations", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "validation_not_run", return_value=list(not_run)))
            stack.enter_context(mock.patch("subprocess.run", side_effect=fake_run))
            stack.enter_context(contextlib.redirect_stdout(text))
            result = gate.pack_for_ci(output, root=root)
        return result, calls, text.getvalue()

    def _rehearse(self, root: pathlib.Path, package_dir: pathlib.Path, *, commit=None, version=None,
                  breaking=0, baseline=None):
        bc = self._bc()
        samples = importlib.import_module("release_extension_samples")
        release_dir = root / "artifacts" / "package" / "release"
        text = io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(gate, "_git", side_effect=self._git(commit=commit)))
            stack.enter_context(mock.patch.object(bc, "resolve_baseline", return_value=baseline or self.BASELINE))
            not_run = stack.enter_context(mock.patch.object(bc, "validation_not_run", return_value=[]))
            check = stack.enter_context(mock.patch.object(bc, "check", return_value=breaking))
            # The surfaces of the other kinds are read after ApiCompat; their own tests are in contract_breaks_test.py.
            stack.enter_context(mock.patch.object(importlib.import_module("contract_breaks"), "check", return_value=0))
            run = stack.enter_context(mock.patch("subprocess.run"))
            stack.enter_context(mock.patch.object(gate, "_npm_dry_run", return_value=0))
            stack.enter_context(mock.patch.object(samples, "verify", return_value=0))
            stack.enter_context(contextlib.redirect_stdout(text))
            result = gate.rehearse_package_directory(package_dir, version, root=root, release_dir=release_dir)
        return result, release_dir, text.getvalue(), run, not_run, check

    def _packed(self, directory: str, **kwargs) -> tuple[pathlib.Path, pathlib.Path]:
        root = self._root(directory)
        output = pathlib.Path(directory) / "ci-packages"
        result, _, text = self._pack(root, output, **kwargs)
        self.assertEqual(result, 0, text)
        return root, output

    @staticmethod
    def _manifest(directory: pathlib.Path) -> dict:
        return json.loads((directory / "package-manifest.json").read_text(encoding="utf-8"))

    @staticmethod
    def _rewrite_manifest(directory: pathlib.Path, change) -> None:
        path = directory / "package-manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        change(manifest)
        path.write_text(json.dumps(manifest), encoding="utf-8")

    @staticmethod
    def _snapshot(directory: pathlib.Path) -> dict[str, str]:
        return {path.relative_to(directory).as_posix(): gate._sha256(path)
                for path in directory.rglob("*") if path.is_file()}

    # --- pack ------------------------------------------------------------------

    def test_pack_writes_a_single_pack_with_no_build_and_a_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-packages"

            result, calls, text = self._pack(root, output)
            manifest = self._manifest(output)

        self.assertEqual(result, 0, text)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][:2], ["dotnet", "pack"])
        self.assertIn("--no-build", calls[0])
        self.assertIn(f"-p:TraconApiCompatReportDir={output / 'api-compat'}", calls[0])
        self.assertEqual(manifest["commit"], self.COMMIT)
        self.assertEqual(manifest["version"], self.VERSION)
        self.assertEqual(manifest["baseline"], self.BASELINE)
        self.assertEqual([entry["id"] for entry in manifest["packages"]], ["Tracon", "Tracon.Core"])
        self.assertIsNone(manifest["packages"][0]["symbolsFile"])
        self.assertEqual(manifest["apiCompat"], [{
            "id": "Tracon.Core", "report": "api-compat/Tracon.Core.xml",
            "sha256": manifest["apiCompat"][0]["sha256"], "validationRan": True}])

    def test_pack_of_an_unchanged_package_without_a_report_is_a_legitimate_record(self):
        """For an unchanged package the SDK writes no report."""
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory, report=False)

            records = self._manifest(output)["apiCompat"]

        self.assertEqual(records, [{"id": "Tracon.Core", "report": None, "sha256": None, "validationRan": True}])

    def test_pack_carries_no_bypass_flag(self):
        """`pack` is a public command: it offers no way to bypass the cleanliness check."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            _, calls, _ = self._pack(root, pathlib.Path(directory) / "ci-packages")
        help_text = io.StringIO()
        with contextlib.redirect_stdout(help_text), self.assertRaises(SystemExit):
            gate.main(["pack", "--help"])

        for flag in ("TraconSkipCleanWorkingTreeCheck", "TraconAllowDirtyPack"):
            self.assertFalse(any(flag in argument for argument in calls[0]), flag)
            self.assertNotIn(flag, help_text.getvalue())
        self.assertNotIn("--izin", help_text.getvalue())  # legacy-name
        self.assertNotIn("--allow", help_text.getvalue())

    def test_pack_tries_no_pack_on_a_dirty_tree(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-packages"

            result, calls, text = self._pack(root, output, status=["?? notes.txt"])

            self.assertFalse(output.exists())
        self.assertEqual(result, 1)
        self.assertEqual(calls, [])
        self.assertIn("?? notes.txt", text)

    def test_pack_rejects_when_git_is_missing(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            text = io.StringIO()
            with mock.patch.object(gate, "_git", return_value=None), \
                    mock.patch("subprocess.run") as run, contextlib.redirect_stdout(text):
                result = gate.pack_for_ci(pathlib.Path(directory) / "ci-packages", root=root)

        self.assertEqual(result, 1)
        run.assert_not_called()

    def test_pack_rejects_a_non_empty_directory_without_deleting_it(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-packages"
            output.mkdir()
            leftover = output / "Tracon.Core.1.0.0-preview.2.46.nupkg"
            leftover.write_bytes(b"half written")

            result, calls, text = self._pack(root, output)

            self.assertTrue(leftover.exists())
            self.assertEqual(leftover.read_bytes(), b"half written")
        self.assertEqual(result, 1)
        self.assertEqual(calls, [])
        self.assertIn("The output directory is not empty", text)

    def test_pack_rejects_a_second_run_into_the_same_directory(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            before = self._snapshot(output)

            result, calls, _ = self._pack(root, output)

            self.assertEqual(self._snapshot(output), before)
        self.assertEqual(result, 1)
        self.assertEqual(calls, [])

    def test_pack_writes_no_manifest_when_the_validation_did_not_run(self):
        """The semaphore lives in the pack stage; if it is red, the half-written directory stays without
        a manifest and the next run rejects it."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-packages"

            result, _, text = self._pack(root, output, not_run=["Tracon.Core: package validation did not run in this pack"])

            self.assertFalse((output / "package-manifest.json").exists())
            self.assertEqual(gate.package_directory_problems(output), ["package-manifest.json is missing"])
        self.assertEqual(result, 1)
        self.assertIn("The package validation did not run in this pack", text)

    # --- verify-packages --------------------------------------------------------

    def test_verify_packages_green(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)

            self.assertEqual(gate.package_directory_problems(output), [])

    def test_verify_packages_reports_a_single_byte_difference_with_the_file_name(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)
            with (output / f"Tracon.Core.{self.VERSION}.nupkg").open("ab") as handle:
                handle.write(b"\0")

            problems = gate.package_directory_problems(output)

        self.assertEqual(problems, [f"SHA-256 differs: Tracon.Core.{self.VERSION}.nupkg"])

    def test_verify_packages_reports_a_missing_and_an_extra_file(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)
            (output / f"Tracon.Core.{self.VERSION}.snupkg").unlink()
            (output / "Tracon.Extra.1.0.0.nupkg").write_bytes(b"x")
            (output / "api-compat" / "Tracon.Extra.xml").write_text("<x />", encoding="utf-8")

            problems = gate.package_directory_problems(output)

        self.assertIn(f"missing: Tracon.Core.{self.VERSION}.snupkg", problems)
        self.assertIn("file not in the manifest: Tracon.Extra.1.0.0.nupkg", problems)
        self.assertIn("file not in the manifest: api-compat/Tracon.Extra.xml", problems)

    def test_verify_packages_reports_an_empty_a_missing_and_a_malformed_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            (base / "empty").mkdir()
            (base / "no-manifest").mkdir()
            (base / "no-manifest" / "Tracon.Core.1.nupkg").write_bytes(b"x")
            (base / "malformed").mkdir()
            (base / "malformed" / "package-manifest.json").write_text("{", encoding="utf-8")
            (base / "schema").mkdir()
            (base / "schema" / "package-manifest.json").write_text(
                json.dumps({"dirty": False, "packages": [{"id": "X", "file": "../X.nupkg", "sha256": "0" * 64}]}),
                encoding="utf-8")

            self.assertEqual(gate.package_directory_problems(base / "absent"), [f"{base / 'absent'}: directory not found"])
            self.assertEqual(gate.package_directory_problems(base / "empty"), [f"{base / 'empty'}: directory is empty"])
            self.assertEqual(gate.package_directory_problems(base / "no-manifest"), ["package-manifest.json is missing"])
            self.assertTrue(gate.package_directory_problems(base / "malformed")[0].startswith("package-manifest.json could not be read"))
            self.assertIn("package-manifest.json: X carries an invalid file/SHA-256 record",
                          gate.package_directory_problems(base / "schema"))

    def test_verify_packages_command_exit_code(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)
            text = io.StringIO()
            with contextlib.redirect_stdout(text):
                green = gate.main(["verify-packages", str(output)])
                (output / f"Tracon.{self.VERSION}.nupkg").unlink()
                red = gate.main(["verify-packages", str(output)])

        self.assertEqual((green, red), (0, 1))
        self.assertIn(f"missing: Tracon.{self.VERSION}.nupkg", text.getvalue())

    # --- release --dry-run --package-directory ---------------------------------------

    def test_package_directory_mode_calls_no_pack_and_no_semaphore_and_runs_the_shared_path(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            with mock.patch.object(gate, "_finish_release_rehearsal", return_value=0) as finish:
                result, _, text, run, not_run, _ = self._rehearse(root, output)

        self.assertEqual(result, 0, text)
        run.assert_not_called()
        not_run.assert_not_called()
        finish.assert_called_once()
        self.assertEqual(finish.call_args.kwargs["staging_dir"], output)
        self.assertEqual(finish.call_args.kwargs["breaking_gate"].report_dir, output / "api-compat")
        self.assertIsNotNone(finish.call_args.kwargs["input_manifest"])

    def test_package_directory_mode_green_input_unchanged_and_manifest_equal(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            before = self._snapshot(output)

            result, release_dir, text, run, not_run, check = self._rehearse(root, output)

            self.assertEqual(result, 0, text)
            # The pack-time evidence is not called on the shared path either.
            not_run.assert_not_called()
            self.assertEqual(self._snapshot(output), before)
            written = self._manifest(release_dir)
            source = self._manifest(output)
            for field in ("version", "commit", "baseline", "packages"):
                self.assertEqual(written[field], source[field], field)
            self.assertEqual(gate.package_directory_problems(release_dir), [])
            self.assertEqual(check.call_args.kwargs["report_dir"], output / "api-compat")
        run.assert_not_called()
        self.assertIn("The manifest matches the input", text)

    def test_package_directory_mode_makes_no_copy_while_the_breaking_gate_is_red(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, release_dir, _, _, _, _ = self._rehearse(root, output, breaking=1)

            self.assertFalse(release_dir.exists())
        self.assertEqual(result, 1)

    def test_package_directory_mode_reports_a_missing_report_when_there_is_no_report_record(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            (output / "api-compat" / "Tracon.Core.xml").unlink()
            self._rewrite_manifest(output, lambda manifest: manifest.update(apiCompat=[]))

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("report missing: Tracon.Core", text)

    def test_package_directory_mode_is_red_when_the_report_file_is_missing(self):
        """The record claims a report but `api-compat/` is empty: it does not count as 'no breaking change'."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            (output / "api-compat" / "Tracon.Core.xml").unlink()

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("missing: api-compat/Tracon.Core.xml", text)

    def test_package_directory_mode_is_red_without_a_record_that_validation_ran(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory, report=False)
            self._rewrite_manifest(output, lambda manifest: manifest["apiCompat"][0].update(validationRan=False))

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("report missing: Tracon.Core (no record that validation ran)", text)

    def test_package_directory_mode_rejects_another_commit(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, release_dir, text, *_ = self._rehearse(root, output, commit="f" * 40)

            self.assertFalse(release_dir.exists())
        self.assertEqual(result, 1)
        self.assertIn("The packages come from another commit", text)

    def test_nuspec_commit_that_is_not_head_turns_red(self):
        """The manifest commit is right but the package itself names another commit."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            self._write_package(output, "Tracon.Core", commit="e" * 40)
            self._rewrite_manifest(output, lambda manifest: [
                entry.update(sha256=gate._sha256(output / entry["file"]))
                for entry in manifest["packages"] if entry["id"] == "Tracon.Core"])

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn(f"the repository commit '{'e' * 40}' is not HEAD", text)

    def test_package_directory_mode_rejects_another_baseline(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, _, text, *_ = self._rehearse(root, output, baseline="1.0.0-preview.1")

        self.assertEqual(result, 1)
        self.assertIn("validated against another baseline", text)

    def test_package_directory_mode_rejects_two_version_lines(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            self._write_package(output, "Tracon", version="1.0.0-preview.2.48")
            (output / f"Tracon.{self.VERSION}.nupkg").unlink()
            self._rewrite_manifest(output, lambda manifest: [
                entry.update(file=f"Tracon.1.0.0-preview.2.48.nupkg",
                             sha256=gate._sha256(output / "Tracon.1.0.0-preview.2.48.nupkg"))
                for entry in manifest["packages"] if entry["id"] == "Tracon"])

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("not on one version line", text)

    def test_package_directory_mode_checks_the_version_as_the_expected_version(self):
        """--package-version together with --package-directory is allowed."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, _, text, *_ = self._rehearse(root, output, version="1.0.0-preview.3")

        self.assertEqual(result, 1)
        self.assertIn("Package not produced", text)

    def test_package_directory_mode_rejects_a_package_outside_the_input_in_release_dir(self):
        """Rejection only; no file is deleted."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            old = release_dir / "Tracon.Core.1.0.0-preview.2.10.nupkg"
            old.write_bytes(b"old")

            result, _, text, *_ = self._rehearse(root, output)

            self.assertTrue(old.exists())
            self.assertEqual(sorted(path.name for path in release_dir.iterdir()), [old.name])
        self.assertEqual(result, 1)
        self.assertIn("holds packages outside the input (1): Tracon.Core.1.0.0-preview.2.10.nupkg", text)
        self.assertIn(f"rm -rf {release_dir}", text)

    def test_package_directory_mode_protects_the_same_identity_with_different_content(self):
        """On the copy path too: same name, different content -> no file is copied."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            name = f"Tracon.Core.{self.VERSION}.nupkg"
            with zipfile.ZipFile(release_dir / name, "w") as archive:
                archive.writestr("lib/net10.0/Tracon.Core.dll", "other")

            result, _, text, *_ = self._rehearse(root, output)

            self.assertEqual(sorted(path.name for path in release_dir.iterdir()), [name])
        self.assertEqual(result, 1)
        self.assertIn("but DIFFERENT content already exists", text)

    def test_truncated_package_in_release_dir_exits_1_with_the_file_name(self):
        """`_content_fingerprint` gives the file name, not a BadZipFile traceback."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            name = f"Tracon.Core.{self.VERSION}.nupkg"
            (release_dir / name).write_bytes((output / name).read_bytes()[:40])

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn(f"❌ {name}: the package could not be read", text)

    def test_same_fingerprint_with_different_bytes_gives_a_manifest_mismatch(self):
        """An old copy carries the same content with different OPC bytes: a no-op promote
        does not change the raw SHA-256, and the manifest comparison catches it."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            name = f"Tracon.Core.{self.VERSION}.nupkg"
            with zipfile.ZipFile(output / name) as source, zipfile.ZipFile(release_dir / name, "w") as target:
                for entry in source.namelist():
                    target.writestr(entry, source.read(entry))
                target.writestr("package/services/metadata/core-properties/" + "a" * 32 + ".psmdcp", "random")

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("differs from the input manifest: packages", text)

    def test_interrupted_copy_leaves_no_file_under_the_final_name(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            source = base / "input"
            release_dir = base / "release"
            source.mkdir()
            name = "Tracon.Core.1.0.0.nupkg"
            BuildOnceChainTests._zip(source / name)

            with mock.patch("os.replace", side_effect=KeyboardInterrupt), self.assertRaises(KeyboardInterrupt):
                gate._promote_staged_packages(source, release_dir, [name], copy=True)

            self.assertEqual(list(release_dir.iterdir()), [])
            self.assertTrue((source / name).exists())

    def test_copy_mode_does_not_move_the_input(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            source = base / "input"
            release_dir = base / "release"
            source.mkdir()
            name = "Tracon.Core.1.0.0.nupkg"
            BuildOnceChainTests._zip(source / name)

            conflicts = gate._promote_staged_packages(source, release_dir, [name], copy=True)

            self.assertEqual(conflicts, [])
            self.assertEqual(gate._sha256(source / name), gate._sha256(release_dir / name))
            self.assertEqual([path.name for path in release_dir.iterdir()], [name])

    @staticmethod
    def _zip(path: pathlib.Path) -> None:
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("lib/net10.0/Tracon.Core.dll", "dll")

    def test_package_outside_the_manifest_in_the_input_directory_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            (output / "Tracon.Sneaky.1.0.0.nupkg").write_bytes(b"x")

            result, _, text, run, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        run.assert_not_called()
        self.assertIn("file not in the manifest: Tracon.Sneaky.1.0.0.nupkg", text)


# These two constants are written in PIECES: if the full text appeared in the source, it would break
# the scan's own gate. The same technique is used in test_secret_scan_finds_a_value_line.
SECRET_LINE = 'var x = "Pass' + 'word=made-up-credential-value";'
MARKER = "SYNTHETIC" + "-CREDENTIAL"


class SyntheticCredentialTests(unittest.TestCase):
    """The only way to verify a secret scanner is to show it a fake
    credential; an exception is not HIDDEN, it is MARKED."""

    def test_unmarked_credential_is_caught(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            (root / "src").mkdir()
            (root / "src" / "a.cs").write_text(SECRET_LINE, encoding="utf-8")

            found, skipped = gate.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertEqual(skipped, 0)

    def test_marked_line_is_skipped_and_counted(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            (root / "src").mkdir()
            (root / "src" / "a.cs").write_text(SECRET_LINE + "  // " + MARKER, encoding="utf-8")

            found, skipped = gate.find_secrets(root)

        self.assertEqual(found, [])
        self.assertEqual(skipped, 1)

    def test_marker_must_be_on_the_same_line(self):
        # 🚨 A comment on the line above is not enough: the exception must appear on the
        # LINE that carries it, otherwise it gets lost in review.
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            (root / "src").mkdir()
            (root / "src" / "a.cs").write_text("// " + MARKER + "\n" + SECRET_LINE, encoding="utf-8")

            found, skipped = gate.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertEqual(skipped, 0)


class TwoLayerSecretScanTests(unittest.TestCase):
    """2026-09-19. While the gate was GREEN, a real `ApiKeyGenerator` output sat in the archive
    tree. There were two independent gaps: the pattern did not recognise the product's own
    key format, and the scope never walked the two trees that carry real run output
    (the archive and the manual-test records)."""

    # Real format: `ap_` + tenant suffix + `_` + base64url(32 bytes) = 43 characters.
    GENERATED_KEY = "ap_default_" + ("2zKqRNjJ6MVXcwtTxPKuXkouy85tReMOhGAgYr9MGzo")
    # The directories that hold records of past runs are configured by the extension, not by the public gate.
    EXTENSION = gate.Extension(record_dirs=("archive", "manual-records"))

    def _scan_path(self, relative_path, line):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            path = root / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(line + "\n", encoding="utf-8")
            return gate.find_secrets(root, self.EXTENSION)

    def test_generated_key_is_caught_in_the_archive_tree(self):
        # 🚨 This is exactly the case that was missed. An archived record carries REAL run
        # output - it is the one place a generated credential would get pasted.
        found, _ = self._scan_path("archive/past/99-X.md", self.GENERATED_KEY)

        self.assertEqual(len(found), 1)

    def test_generated_key_is_caught_in_the_manual_test_tree(self):
        found, _ = self._scan_path("manual-records/99-X.md", self.GENERATED_KEY)

        self.assertEqual(len(found), 1)

    def test_local_setup_statement_in_the_archive_does_not_break_the_gate(self):
        # In an archived or manual-test record a docker password is not a defect but a
        # reproducibility instruction. Opening the scope to every pattern would have flagged 48 lines,
        # and the exception list would have made the gate itself meaningless.
        found, skipped = self._scan_path("archive/past/99-Y.md", SECRET_LINE)

        self.assertEqual(found, [])
        self.assertEqual(skipped, 0)

    def test_local_setup_statement_in_the_code_tree_is_caught(self):
        found, _ = self._scan_path("src/b.cs", SECRET_LINE)

        self.assertEqual(len(found), 1)

    def test_snake_case_text_is_not_mistaken_for_a_key(self):
        # 🚨 Writing `{43,}` would catch snake_case English text; measured, 70+
        # false positives. The length must be given EXACTLY.
        found, _ = self._scan_path(
            "src/c.cs",
            "ap_on_total_source_code_size_for_JavaScript_files_in_the_TypeScript_x")

        self.assertEqual(found, [])

    def test_short_display_prefix_is_not_mistaken_for_a_key(self):
        # `KeyPrefix` is the 12-character display prefix the product designed itself
        # and it legitimately appears in records.
        found, _ = self._scan_path("archive/past/99-Y.md", 'keyPrefix":"ap_default_2"')

        self.assertEqual(found, [])

    def test_marked_line_in_the_archive_is_skipped_and_counted(self):
        found, skipped = self._scan_path(
            "archive/past/99-X.md", self.GENERATED_KEY + " <!-- " + MARKER + " -->")

        self.assertEqual(found, [])
        self.assertEqual(skipped, 1)


class CapacityCommandTests(unittest.TestCase):
    """Capacity measurement is NOT a GATE."""

    def test_capacity_command_is_not_added_to_the_closing(self):
        # 🚨 If a heavy load run leaks into the standard closing, every change takes hours
        # and the gates stop being run.
        commands = [c.display for c in gate.closing_commands("abc123", site=True, performance=True)]

        self.assertFalse(any("capacity" in command for command in commands))

    def test_capacity_command_is_not_added_to_the_inner_loop_either(self):
        commands = [c.display for c in gate.inner_loop_commands(["src/Tracon.Core/Foo.cs"])]

        self.assertFalse(any("capacity" in command for command in commands))

    def test_capacity_requires_an_exact_version(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            code = gate.capacity_measurement("smoke", "*-*", pathlib.Path("/tmp/capacity-test"))

        self.assertEqual(code, 1)
        self.assertIn("exact", output.getvalue())

    def test_capacity_rejects_an_unknown_profile(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            code = gate.capacity_measurement("absent", "1.0.0", pathlib.Path("/tmp/capacity-test"))

        self.assertEqual(code, 1)
        self.assertIn("does not exist", output.getvalue())


class ModuleTimeBudgetTests(unittest.TestCase):
    """Every test module may carry an absolute wall-time budget; in the closing, a module that has a budget is checked."""

    @staticmethod
    def _trx(root: pathlib.Path, project: str, seconds: float, tfm: str = "net10.0_arm64") -> None:
        folder = root / gate.TEST_RESULTS_DIRECTORY / project / tfm
        folder.mkdir(parents=True, exist_ok=True)
        (folder / "result.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<Times start="2026-11-09T11:00:00Z" finish="{_clock_stamp(seconds)}" />'
            '<Results><UnitTestResult testName="N.S.A" duration="00:00:01" outcome="Passed" /></Results></TestRun>',
            encoding="utf-8")

    def _execute(self, durations: dict[str, float], limits: dict[str, int]) -> tuple[int, str]:
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            for project, seconds in durations.items():
                self._trx(root, project, seconds)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits=limits)
        return code, output.getvalue()

    def test_report_lists_every_module_with_its_time_and_budget_share_slowest_first(self):
        code, text = self._execute({"A": 100, "B": 300, "C": 50}, {"A": 200, "B": 400})

        self.assertEqual(code, 0)
        lines = [line.strip() for line in text.splitlines() if "net10.0_arm64" in line]
        self.assertEqual(lines, [
            "B/net10.0_arm64: 300 s · limit 400 s · left 100 s (25%)",
            "A/net10.0_arm64: 100 s · limit 200 s · left 100 s (50%)",
            "C/net10.0_arm64: 50 s · no limit",
        ])

    def test_slow_module_without_a_budget_does_not_break_the_gate_but_one_over_its_budget_does(self):
        code, text = self._execute({"Slow": 5000, "Limited": 61}, {"Limited": 60})

        self.assertEqual(code, gate.MODULE_TIME_LIMIT_EXIT_CODE)
        self.assertIn("Limited/net10.0_arm64: 61 s · limit 60 s · EXCEEDED by 1 s", text)
        self.assertIn("Limited/net10.0_arm64 61 s > 60 s", text)
        self.assertNotIn("Slow/net10.0_arm64 5000 s >", text)

    def test_budget_passes_exactly_at_the_limit_and_breaks_one_second_over(self):
        self.assertEqual(self._execute({"A": 60}, {"A": 60})[0], 0)
        self.assertEqual(self._execute({"A": 61}, {"A": 60})[0], gate.MODULE_TIME_LIMIT_EXIT_CODE)

    def test_budget_is_per_module_and_each_tfm_leg_is_checked_separately(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx(root, "A", 30, "net10.0_arm64")
            self._trx(root, "A", 90, "net9.0_arm64")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits={"A": 60})

        self.assertEqual(code, gate.MODULE_TIME_LIMIT_EXIT_CODE)
        self.assertIn("A/net9.0_arm64 90 s > 60 s", output.getvalue())
        self.assertNotIn("A/net10.0_arm64 30 s >", output.getvalue())

    def test_module_without_a_budget_and_an_unreadable_duration_warns_and_does_not_break_the_gate(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            folder = root / gate.TEST_RESULTS_DIRECTORY / "Q" / "net10.0_arm64"
            folder.mkdir(parents=True)
            (folder / "result.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results/></TestRun>',
                encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                code = gate.enforce_module_time_limits(0.0, root=root, limits={})

        self.assertEqual(code, 0)
        self.assertIn("Q/net10.0_arm64: the TRX duration could not be read", output.getvalue())

    def test_real_budget_table_uses_real_module_names_and_runs_in_the_closing(self):
        # The table lives in one place; the closing test step reads it by default.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._trx(root, "Tracon.TwoNode.IntegrationTests", gate.TEST_MODULE_TIME_LIMITS_SECONDS[
                "Tracon.TwoNode.IntegrationTests"] + 1)
            with contextlib.redirect_stdout(io.StringIO()):
                code = gate.enforce_module_time_limits(0.0, root=root)

        self.assertEqual(code, gate.MODULE_TIME_LIMIT_EXIT_CODE)

    def test_duration_variability_is_not_checked_in_the_closing(self):
        # Variance checking belongs to the detector (`stability`); the closing knows only the absolute budget.
        commands = [command.display for command in gate.closing_commands("abc123", site=True, performance=True)]

        self.assertFalse(any("stability" in command for command in commands))
        self.assertFalse(any("stability" in command.display for command in gate.staged_test_commands()))
        self.assertFalse(any("stability" in command.display for command in gate.inner_loop_commands(["src/Tracon.Core/A.cs"])))


def _clock_stamp(seconds: float) -> str:
    """`seconds` after `2026-11-09T11:00:00Z`, as a TRX timestamp."""
    instant = datetime.datetime(2026, 11, 9, 11, 0, 0, tzinfo=datetime.timezone.utc) + datetime.timedelta(seconds=seconds)
    return instant.strftime("%Y-%m-%dT%H:%M:%S.%f") + "Z"


def _trx_duration_text(milliseconds: int) -> str:
    return f"{milliseconds // 3600000:02d}:{milliseconds // 60000 % 60:02d}:{(milliseconds % 60000) / 1000:010.7f}"


def _write_run_trx(
    folder: pathlib.Path, tests: dict[str, tuple[int, str]], wall_seconds: float = 10.0, name: str = "result.trx",
) -> pathlib.Path:
    """Synthetic TRX: the order of `tests` is the start order, each test starts 10 ms after the previous one."""
    folder.mkdir(parents=True, exist_ok=True)
    base_time = datetime.datetime(2026, 11, 9, 11, 0, 0, tzinfo=datetime.timezone.utc)
    lines = []
    for order, (test, (milliseconds, outcome)) in enumerate(tests.items()):
        started_names = (base_time + datetime.timedelta(milliseconds=10 * order)).isoformat()
        lines.append(
            f'<UnitTestResult testName="{test}" duration="{_trx_duration_text(milliseconds)}" startTime="{started_names}"'
            f' outcome="{outcome}" />')
    path = folder / name
    path.write_text(
        '<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<Times start="{_clock_stamp(0)}" finish="{_clock_stamp(wall_seconds)}" />'
        f"<Results>{''.join(lines)}</Results></TestRun>",
        encoding="utf-8")
    return path


def _module_run(
    label: str = "M", order: int = 1, *, pressure: bool = False, exit_code: int = 0, wall: float | None = 10.0,
    tests: dict[str, tuple[int, str]] | None = None, start_order: list[str] | None = None, problem: str = "",
    seed: int | None = None,
) -> "gate.ModuleRun":
    tests = {"N.A": (100, "Passed"), "N.B": (50, "Passed")} if tests is None else tests
    return gate.ModuleRun(
        label, order, pressure, seed, exit_code, (wall or 0) + 1, wall,
        {name: duration for name, (duration, _) in tests.items()},
        {name: (outcome,) for name, (_, outcome) in tests.items()},
        tuple(tests if start_order is None else start_order), problem)


class StabilityComparisonTests(unittest.TestCase):
    """Numbers at the threshold: green just inside, red just outside. Result identity and exit-code precedence."""

    # --- thresholds ------------------------------------------------------------------

    def test_thresholds_only_tighten(self):
        # Pins today's values so that a loosening cannot go unnoticed (the rule stated in the gate.py comment).
        self.assertLessEqual(gate.STABILITY_TEST_FLOOR_MS, 200)
        self.assertLessEqual(gate.STABILITY_TEST_MEDIAN_FACTOR, 1)
        self.assertLessEqual(gate.STABILITY_MODULE_WALL_PERCENT, 15)
        self.assertLessEqual(gate.STABILITY_MODULE_WALL_FLOOR_MS, 200)
        self.assertGreaterEqual(gate.STABILITY_DEFAULT_REPEAT, 5)

    def test_exit_codes_are_distinct_and_do_not_collide_with_mtp_or_gate_codes(self):
        codes = {
            gate.STABILITY_TIME_EXIT_CODE, gate.STABILITY_RESULT_EXIT_CODE, gate.STABILITY_USAGE_EXIT_CODE,
            gate.MODULE_TIME_LIMIT_EXIT_CODE}

        self.assertEqual(len(codes), 4)
        self.assertTrue(all(code > 12 for code in (gate.STABILITY_TIME_EXIT_CODE, gate.STABILITY_RESULT_EXIT_CODE)))

    def test_module_wall_spread_passes_at_exactly_15_percent_and_is_red_just_above(self):
        inside, percent = gate.wall_spread_exceeded([100.0, 115.0, 108.0])
        outside, _ = gate.wall_spread_exceeded([100.0, 115.001])

        self.assertFalse(inside)
        self.assertEqual(percent, 15.0)
        self.assertTrue(outside)

    def test_module_wall_spread_relies_on_the_200_ms_floor_for_a_short_module(self):
        # In a 0.5 s module 15% = 75 ms: timer noise. The floor is 200 ms.
        self.assertFalse(gate.wall_spread_exceeded([0.5, 0.7])[0])
        self.assertTrue(gate.wall_spread_exceeded([0.5, 0.701])[0])
        self.assertFalse(gate.wall_spread_exceeded([0.5, 0.58])[0], "16% but 80 ms: below the floor")

    def test_module_wall_spread_is_decided_by_the_percentage_for_a_long_module_not_the_floor(self):
        self.assertFalse(gate.wall_spread_exceeded([400.0, 460.0])[0])
        self.assertTrue(gate.wall_spread_exceeded([400.0, 460.1])[0])

    def test_test_duration_spread_passes_at_the_200_ms_floor_and_is_red_just_above(self):
        inside = gate.duration_spreads([_module_run(tests={"N.T": (10, "Passed")}), _module_run(order=2, tests={"N.T": (210, "Passed")})])
        outside = gate.duration_spreads([_module_run(tests={"N.T": (10, "Passed")}), _module_run(order=2, tests={"N.T": (211, "Passed")})])

        self.assertEqual((inside[0].span_ms, inside[0].allowed_ms, inside[0].exceeded), (200, 200, False))
        self.assertTrue(outside[0].exceeded)

    def test_test_duration_spread_passes_at_one_times_the_median_and_is_red_just_above(self):
        def spreads(*durations):
            return gate.duration_spreads(
                [_module_run(order=i + 1, tests={"N.T": (duration, "Passed")}) for i, duration in enumerate(durations)])

        self.assertFalse(spreads(500, 1000, 1500)[0].exceeded, "spread 1000 = median 1000")
        self.assertTrue(spreads(500, 1000, 1501)[0].exceeded)

    def test_test_duration_is_compared_only_for_tests_measured_in_every_run(self):
        runs = [
            _module_run(tests={"N.A": (10, "Passed"), "N.Only1": (9999, "Passed")}),
            _module_run(order=2, tests={"N.A": (20, "Passed")}),
        ]

        self.assertEqual([spread.name for spread in gate.duration_spreads(runs)], ["N.A"])

    # --- duration verdict -------------------------------------------------------------

    def test_a_drifting_single_test_duration_turns_the_module_time_red_and_names_the_test(self):
        runs = [
            _module_run(order=1, tests={"N.A": (100, "Passed"), "N.Slow": (100, "Passed")}),
            _module_run(order=2, tests={"N.A": (100, "Passed"), "N.Slow": (900, "Passed")}),
            _module_run(order=3, tests={"N.A": (100, "Passed"), "N.Slow": (100, "Passed")}),
        ]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "time")
        self.assertEqual(comparison.result_problems, [])
        self.assertEqual(len(comparison.time_problems), 1)
        self.assertIn("N.Slow", comparison.time_problems[0])
        self.assertIn("800 ms > 200 ms", comparison.time_problems[0])
        self.assertEqual(comparison.worst_test.name, "N.Slow")
        self.assertEqual(gate.stability_exit_code([comparison]), gate.STABILITY_TIME_EXIT_CODE)

    def test_a_drifting_module_wall_time_turns_the_time_red(self):
        runs = [_module_run(order=1, wall=100.0), _module_run(order=2, wall=116.0), _module_run(order=3, wall=101.0)]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "time")
        self.assertIn("module wall time", comparison.time_problems[0])
        self.assertEqual(comparison.wall_spread_percent, 16.0)

    def test_durations_within_the_limit_are_green(self):
        runs = [_module_run(order=1, wall=100.0), _module_run(order=2, wall=114.0), _module_run(order=3, wall=103.0)]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "green")
        self.assertEqual(gate.stability_exit_code([comparison]), 0)

    def test_duration_of_a_cpu_pressured_run_is_reported_but_not_counted_red(self):
        runs = [
            _module_run(order=1, wall=100.0), _module_run(order=2, wall=102.0), _module_run(order=3, wall=101.0),
            _module_run(order=4, pressure=True, wall=300.0, tests={"N.A": (5000, "Passed"), "N.B": (50, "Passed")}),
        ]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "green")
        self.assertEqual(comparison.judged_runs, 3)
        self.assertEqual(comparison.pressured_wall_seconds, (300.0,))
        self.assertEqual(comparison.pressured_runs, 1)

    def test_fewer_than_two_clean_runs_without_pressure_skips_the_time_comparison_and_adds_a_note(self):
        runs = [_module_run(order=1), _module_run(order=2, pressure=True, wall=500.0)]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "green")
        self.assertIn("time could not be compared", comparison.notes[0])

    # --- result identity ------------------------------------------------------------------

    def test_a_test_missing_in_one_run_is_a_result_red_and_names_the_test(self):
        runs = [
            _module_run(order=1), _module_run(order=2, tests={"N.A": (100, "Passed")}), _module_run(order=3),
        ]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertEqual(len(comparison.result_problems), 1)
        self.assertIn("N.B — result changed", comparison.result_problems[0])
        self.assertIn("run 2: missing", comparison.result_problems[0])

    def test_an_extra_test_in_one_run_is_a_result_red(self):
        runs = [
            _module_run(order=1), _module_run(order=2, tests={"N.A": (100, "Passed"), "N.B": (50, "Passed"), "N.New": (1, "Passed")}),
        ]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertIn("N.New", comparison.result_problems[0])

    def test_a_test_whose_result_changed_is_named_and_the_result_red_overrides_the_time_red(self):
        runs = [
            _module_run(order=1, tests={"N.A": (100, "Passed"), "N.B": (50, "Passed")}),
            _module_run(order=2, tests={"N.A": (100, "Passed"), "N.B": (900, "Failed")}),
        ]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertIn("N.B — result changed (run 1: Passed, run 2: Failed)", comparison.result_problems[0])
        self.assertEqual(gate.stability_exit_code([comparison]), gate.STABILITY_RESULT_EXIT_CODE)

    def test_a_test_failing_in_every_run_is_also_a_result_red(self):
        runs = [_module_run(order=i, tests={"N.A": (100, "Failed"), "N.B": (50, "Passed")}, exit_code=2) for i in (1, 2, 3)]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertEqual(comparison.result_problems, ["N.A — failed in every run (Failed)"])

    def test_a_skipped_test_that_is_skipped_in_every_run_has_the_same_result(self):
        runs = [_module_run(order=i, tests={"N.A": (0, "NotExecuted"), "N.B": (50, "Passed")}) for i in (1, 2)]

        self.assertEqual(gate.compare_module("M", runs).verdict, "green")

    def test_a_skipped_test_that_runs_in_one_run_has_a_changed_result(self):
        runs = [
            _module_run(order=1, tests={"N.A": (0, "NotExecuted")}),
            _module_run(order=2, tests={"N.A": (10, "Passed")}),
        ]

        self.assertEqual(gate.compare_module("M", runs).verdict, "result")

    def test_non_zero_exit_code_without_a_failure_in_the_report_is_a_result_red(self):
        runs = [_module_run(order=1), _module_run(order=2, exit_code=8)]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertIn("run 2: exit code 8", comparison.result_problems[0])

    def test_a_run_without_a_report_is_a_result_red(self):
        runs = [_module_run(order=1), _module_run(order=2, exit_code=1, wall=None, tests={}, problem="the TRX report is missing or unreadable (exit 1)")]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertIn("run 2: the TRX report is missing", comparison.result_problems[0])

    def test_a_result_change_in_a_cpu_pressured_run_is_red(self):
        runs = [
            _module_run(order=1), _module_run(order=2),
            _module_run(order=3, pressure=True, tests={"N.A": (100, "Passed"), "N.B": (50, "Failed")}, exit_code=2),
        ]

        comparison = gate.compare_module("M", runs)

        self.assertEqual(comparison.verdict, "result")
        self.assertIn("N.B", comparison.result_problems[0])

    def test_same_named_theory_rows_are_compared_as_a_multiset(self):
        same = _module_run(order=1)
        same_two_rows = dataclasses.replace(same, outcomes={"N.A": ("Passed", "Passed"), "N.B": ("Passed",)})
        missing_row = dataclasses.replace(same, index=2, outcomes={"N.A": ("Passed",), "N.B": ("Passed",)})

        self.assertEqual(gate.compare_module("M", [same_two_rows, missing_row]).verdict, "result")

    # --- exit-code precedence ----------------------------------------------------------------

    def test_result_red_takes_precedence_over_time_red_and_neither_gives_zero(self):
        green = gate.compare_module("Y", [_module_run("Y", 1), _module_run("Y", 2)])
        duration = gate.compare_module("S", [_module_run("S", 1, wall=100.0), _module_run("S", 2, wall=200.0)])
        result = gate.compare_module("R", [_module_run("R", 1), _module_run("R", 2, tests={"N.A": (1, "Failed")}, exit_code=2)])

        self.assertEqual(gate.stability_exit_code([green]), 0)
        self.assertEqual(gate.stability_exit_code([green, duration]), gate.STABILITY_TIME_EXIT_CODE)
        self.assertEqual(gate.stability_exit_code([green, result]), gate.STABILITY_RESULT_EXIT_CODE)
        self.assertEqual(gate.stability_exit_code([duration, result]), gate.STABILITY_RESULT_EXIT_CODE)
        self.assertEqual(gate.stability_exit_code([result, duration]), gate.STABILITY_RESULT_EXIT_CODE)

    # --- order evidence -----------------------------------------------------------------------

    def test_order_similarity_is_one_for_the_same_order_and_zero_for_the_reverse(self):
        self.assertEqual(gate.order_similarity(["a", "b", "c", "d"], ["a", "b", "c", "d"]), 1.0)
        self.assertEqual(gate.order_similarity(["a", "b", "c", "d"], ["d", "c", "b", "a"]), 0.0)
        self.assertEqual(gate.order_similarity(["a", "b", "c"], ["a", "c", "b"]), 0.5)
        self.assertIsNone(gate.order_similarity(["a"], ["a"]))
        self.assertIsNone(gate.order_similarity(["a", "b"], ["x", "y"]))

    def test_a_shuffled_module_with_the_same_start_order_gets_a_no_effect_note(self):
        tests = {f"N.T{i:02d}": (10, "Passed") for i in range(30)}
        runs = [_module_run(order=1, tests=tests), _module_run(order=2, tests=tests)]

        with_shuffle = gate.compare_module("M", runs, shuffled=True)
        without_shuffle = gate.compare_module("M", runs, shuffled=False)

        self.assertTrue(any("no visible effect" in not_ for not_ in with_shuffle.notes))
        self.assertFalse(any("no visible effect" in not_ for not_ in without_shuffle.notes))

    def test_no_no_effect_note_when_the_start_order_really_changed(self):
        names = [f"N.T{i:02d}" for i in range(30)]
        tests = {name: (10, "Passed") for name in names}
        runs = [
            _module_run(order=1, tests=tests, start_order=names),
            _module_run(order=2, tests=tests, start_order=names[::-1]),
        ]

        comparison = gate.compare_module("M", runs, shuffled=True)

        self.assertEqual(comparison.order_similarity, [0.0])
        self.assertEqual(comparison.notes, [])

    def test_no_order_note_for_a_short_module(self):
        runs = [_module_run(order=1), _module_run(order=2)]

        self.assertEqual(gate.compare_module("M", runs, shuffled=True).notes, [])

    # --- TRX reading -----------------------------------------------------------------------------

    def test_trx_report_reads_duration_result_and_start_order(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = pathlib.Path(directory)
            _write_run_trx(folder, {"N.B": (1500, "Passed"), "N.A": (20, "Failed")}, wall_seconds=12.5)

            report = gate.read_module_report(folder)

        self.assertEqual(report.wall_seconds, 12.5)
        self.assertEqual(report.durations_ms, {"N.B": 1500, "N.A": 20})
        self.assertEqual(report.outcomes, {"N.B": ("Passed",), "N.A": ("Failed",)})
        self.assertEqual(report.order, ("N.B", "N.A"))

    def test_trx_report_keeps_the_longest_duration_and_all_results_of_same_named_rows(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = pathlib.Path(directory)
            (folder / "result.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                '<UnitTestResult testName="N.T" duration="00:00:00.1000000" outcome="Passed" />'
                '<UnitTestResult testName="N.T" duration="00:00:00.3000000" outcome="Failed" />'
                "</Results></TestRun>", encoding="utf-8")

            report = gate.read_module_report(folder)

        self.assertEqual(report.durations_ms, {"N.T": 300})
        self.assertEqual(report.outcomes, {"N.T": ("Failed", "Passed")})
        self.assertIsNone(report.wall_seconds)
        self.assertEqual(report.order, ())

    def test_trx_report_missing_or_malformed_gives_none(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = pathlib.Path(directory)
            self.assertIsNone(gate.read_module_report(folder))
            self.assertIsNone(gate.read_module_report(folder / "absent"))
            (folder / "result.trx").write_text("<TestRun", encoding="utf-8")
            self.assertIsNone(gate.read_module_report(folder))


class _FakeBurner:
    """Replaces `CpuBurner`: writes the start/stop events to a shared event list."""

    def __init__(self, count: int, events: list[str]) -> None:
        self.count = count
        self.events = events
        self.running = False

    def start(self) -> None:
        self.running = True
        self.events.append("pressure started")

    def stop(self) -> None:
        if self.running:
            self.events.append("pressure stopped")
        self.running = False

    def alive(self) -> int:
        return self.count if self.running else 0


class StabilityRunTests(unittest.TestCase):
    """`stability_check`: end to end with a fake runner that writes synthetic TRX; no dotnet is called."""

    def setUp(self):
        self._temp_dir = tempfile.TemporaryDirectory()
        self.addCleanup(self._temp_dir.cleanup)
        self.root = pathlib.Path(self._temp_dir.name)
        self.events: list[str] = []
        self.calls: list[list[str]] = []
        self._lock = threading.Lock()

    CORE = gate.StabilityUnit(
        "Tracon.Core.UnitTests", "release_net10.0", "core", None, pathlib.Path("/b/Tracon.Core.UnitTests"))
    LIGHT = gate.StabilityUnit("Tracon.Voice.UnitTests", "release", "light", None, pathlib.Path("/b/Tracon.Voice.UnitTests"))
    HEAVY = gate.StabilityUnit(
        "Tracon.AspNetCore.FunctionalTests", "release", "heavy", "b", pathlib.Path("/b/Tracon.AspNetCore.FunctionalTests"))
    DEFAULT_TESTS = {"N.A": (100, "Passed"), "N.B": (50, "Passed")}

    def _runner(self, behavior=None):
        """`behavior(project, run_number)` -> None (default) | dict | (exit code, dict | None, wall_s)."""
        def fake_run(args, **kwargs):
            folder = pathlib.Path(args[args.index("--results-directory") + 1])
            project = pathlib.Path(args[0]).name
            run_number = int(folder.parent.name.removeprefix("run-"))
            with self._lock:
                self.calls.append(list(args))
                self.events.append(f"{project} run {run_number}")
            result = behavior(project, run_number) if behavior else None
            exit_code, tests, wall = 0, self.DEFAULT_TESTS, 10.0
            if isinstance(result, dict):
                tests = result
            elif result is not None:
                exit_code, tests, wall = result
            if tests is not None:
                _write_run_trx(folder, tests, wall)
            return mock.Mock(returncode=exit_code)
        return fake_run

    def _execute(self, units, repeat=5, behavior=None, **options):
        factory = options.pop("burner_factory", lambda count: _FakeBurner(count, self.events))
        report = self.root / "reports"
        with contextlib.redirect_stdout(io.StringIO()) as screen:
            code = gate.stability_check(
                units, repeat, root=self.root, runner=self._runner(behavior), burner_factory=factory,
                report_directory=report, measurement_path=self.root / "measurement.jsonl", stamp="20261109T000000Z",
                cpu_count=8, **options)
        return types.SimpleNamespace(code=code, screen=screen.getvalue(), report=report)

    def _report_json(self, result):
        return json.loads((result.report / "20261109T000000Z.json").read_text(encoding="utf-8"))

    # --- green path, command shape, order ---------------------------------------------------------

    def test_same_result_and_duration_is_green_exit_zero_and_writes_the_report_and_measurement_line(self):
        result = self._execute([self.CORE, self.LIGHT, self.HEAVY])

        self.assertEqual(result.code, 0)
        self.assertEqual(len(self.calls), 15)
        report = self._report_json(result)
        self.assertEqual(report["exit_code"], 0)
        self.assertEqual(report["repeat"], 5)
        self.assertEqual(
            sorted(report["modules"]),
            ["Tracon.AspNetCore.FunctionalTests", "Tracon.Core.UnitTests (net10.0)", "Tracon.Voice.UnitTests"])
        module = report["modules"]["Tracon.Voice.UnitTests"]
        self.assertEqual(module["verdict"], "green")
        self.assertEqual([run["index"] for run in module["runs"]], [1, 2, 3, 4, 5])
        self.assertEqual(module["runs"][0]["tests"], 2)
        self.assertEqual(
            report["thresholds"],
            {"test_floor_ms": 200, "test_median_factor": 1, "module_wall_percent": 15, "module_wall_floor_ms": 200})
        self.assertEqual(set(report), {"utc", "repeat", "shuffled", "base_seed", "pressure", "thresholds", "exit_code", "modules"})
        line = json.loads((self.root / "measurement.jsonl").read_text(encoding="utf-8").splitlines()[-1])
        self.assertEqual(
            (line["stage"], line["exit_code"], line["command"]), ("stability", 0, "gate.py stability --repeat 5"))
        self.assertIn("Stable", result.screen)

    def test_command_invokes_the_built_binary_with_the_trx_flags_and_builds_nothing(self):
        self._execute([self.LIGHT], repeat=2)

        for args in self.calls:
            self.assertEqual(args[0], str(self.LIGHT.executable))
            self.assertIn("--report-trx", args)
            self.assertEqual(args[args.index("--report-trx-filename") + 1], gate.STABILITY_TRX_NAME)
            self.assertEqual(args[args.index("--timeout") + 1], f"{gate.STABILITY_MODULE_TIMEOUT_MINUTES}m")
            self.assertNotIn("dotnet", args[0])
            self.assertNotIn("build", args)
            self.assertNotIn("--seed", args)
        directories = {args[args.index("--results-directory") + 1] for args in self.calls}
        self.assertEqual(len(directories), 2, "every run writes to its own directory")
        self.assertTrue(all("20261109T000000Z" in folder for folder in directories))

    def test_stages_go_core_then_light_then_heavy_and_no_run_starts_before_the_previous_one_finishes(self):
        self._execute([self.HEAVY, self.LIGHT, self.CORE], repeat=2)

        order = [event for event in self.events if "run" in event]
        self.assertEqual(order[:3], [
            "Tracon.Core.UnitTests run 1", "Tracon.Voice.UnitTests run 1", "Tracon.AspNetCore.FunctionalTests run 1"])
        self.assertEqual(order[3:], [
            "Tracon.Core.UnitTests run 2", "Tracon.Voice.UnitTests run 2", "Tracon.AspNetCore.FunctionalTests run 2"])

    def test_a_red_module_does_not_stop_the_run_because_stability_needs_every_run_to_finish(self):
        result = self._execute([self.CORE, self.LIGHT], behavior=lambda project, no: (2, {"N.A": (1, "Failed")}, 5.0)
                          if project == "Tracon.Core.UnitTests" else None)

        self.assertEqual(len(self.calls), 10)
        self.assertEqual(result.code, gate.STABILITY_RESULT_EXIT_CODE)

    # --- order shuffling ----------------------------------------------------------------------------

    def test_shuffle_gives_every_run_a_different_recorded_seed(self):
        result = self._execute([self.LIGHT], repeat=3, shuffle=True, base_seed=1000)

        seeds = sorted(int(args[args.index("--seed") + 1]) for args in self.calls)
        self.assertEqual(seeds, [1000, 1001, 1002])
        report = self._report_json(result)
        self.assertEqual(report["base_seed"], 1000)
        self.assertTrue(report["shuffled"])
        self.assertEqual([run["seed"] for run in report["modules"]["Tracon.Voice.UnitTests"]["runs"]], [1000, 1001, 1002])
        self.assertIn("seed 1000", result.screen)

    def test_no_seed_is_given_or_reported_while_shuffling_is_off(self):
        result = self._execute([self.LIGHT], repeat=2, base_seed=1000)

        self.assertFalse(any("--seed" in args for args in self.calls))
        self.assertIsNone(self._report_json(result)["base_seed"])

    def test_a_missing_seed_is_derived_from_the_clock_and_written_to_the_report(self):
        result = self._execute([self.LIGHT], repeat=2, shuffle=True)

        self.assertIsInstance(self._report_json(result)["base_seed"], int)

    # --- result and duration errors --------------------------------------------------------------------

    def test_a_result_change_in_one_run_gives_exit_92_and_the_test_name_on_screen(self):
        result = self._execute(
            [self.LIGHT], behavior=lambda project, no: {"N.A": (100, "Passed"), "N.B": (50, "Failed")} if no == 3 else None)

        self.assertEqual(result.code, gate.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("N.B — result changed", result.screen)
        self.assertEqual(self._report_json(result)["exit_code"], 92)
        line = json.loads((self.root / "measurement.jsonl").read_text(encoding="utf-8").splitlines()[-1])
        self.assertEqual(line["exit_code"], 92)

    def test_a_duration_change_alone_gives_exit_91(self):
        result = self._execute([self.LIGHT], behavior=lambda project, no: (0, self.DEFAULT_TESTS, 30.0) if no == 2 else None)

        self.assertEqual(result.code, gate.STABILITY_TIME_EXIT_CODE)
        self.assertIn("module wall time", result.screen)

    def test_when_result_and_duration_change_together_the_result_code_wins(self):
        def behavior(project, no):
            if project == "Tracon.Voice.UnitTests" and no == 2:
                return 0, self.DEFAULT_TESTS, 30.0
            if project == "Tracon.Core.UnitTests" and no == 3:
                return 2, {"N.A": (1, "Failed")}, 10.0
            return None

        self.assertEqual(self._execute([self.CORE, self.LIGHT], behavior=behavior).code, gate.STABILITY_RESULT_EXIT_CODE)

    def test_a_run_without_a_report_is_red_and_a_run_crash_does_not_block_the_others(self):
        result = self._execute([self.CORE, self.LIGHT], behavior=lambda project, no: (1, None, 0) if (project, no) == ("Tracon.Voice.UnitTests", 2) else None)

        self.assertEqual(result.code, gate.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("run 2: the TRX report is missing", result.screen)
        self.assertEqual(len(self.calls), 10)

    def test_a_report_without_tests_is_red(self):
        result = self._execute([self.LIGHT], repeat=2, behavior=lambda project, no: {})

        self.assertEqual(result.code, gate.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("the report holds no test", result.screen)

    def test_timeout_and_start_failure_become_a_run_problem_not_a_crash(self):
        def fake_run(args, **kwargs):
            raise subprocess.TimeoutExpired(args, 1)
        report = self.root / "reports"
        with contextlib.redirect_stdout(io.StringIO()) as screen:
            code = gate.stability_check(
                [self.LIGHT], 2, root=self.root, runner=fake_run, report_directory=report,
                measurement_path=self.root / "measurement.jsonl", stamp="s",
                burner_factory=lambda count: _FakeBurner(count, self.events))

        self.assertEqual(code, gate.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("min", screen.getvalue())
        self.assertIn("did not finish within", screen.getvalue())

    def test_an_unexpected_runner_error_is_a_result_red_and_the_run_does_not_hang(self):
        def fake_run(args, **kwargs):
            raise RuntimeError("exploded")
        with contextlib.redirect_stdout(io.StringIO()) as screen:
            code = gate.stability_check(
                [self.CORE, self.LIGHT], 2, root=self.root, runner=fake_run, report_directory=self.root / "r",
                measurement_path=self.root / "measurement.jsonl", stamp="s",
                burner_factory=lambda count: _FakeBurner(count, self.events))

        self.assertEqual(code, gate.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("the runner crashed", screen.getvalue())

    # --- CPU pressure ---------------------------------------------------------------------------------

    def test_pressure_runs_by_default_in_the_last_two_runs_and_stops_when_they_end(self):
        result = self._execute([self.LIGHT], repeat=5, pressure=True)

        events = self.events
        self.assertEqual(events.count("pressure started"), 1)
        self.assertEqual(events.count("pressure stopped"), 1)
        start = events.index("pressure started")
        self.assertEqual(events[:start], [f"Tracon.Voice.UnitTests run {no}" for no in (1, 2, 3)])
        self.assertEqual(events[start + 1:start + 3], [f"Tracon.Voice.UnitTests run {no}" for no in (4, 5)])
        self.assertEqual(events[-1], "pressure stopped")
        report = self._report_json(result)
        self.assertEqual(report["pressure"], {"runs": [4, 5], "burners": 4})
        runs = report["modules"]["Tracon.Voice.UnitTests"]["runs"]
        self.assertEqual([run["pressured"] for run in runs], [False, False, False, True, True])
        self.assertIn("(4 load processes)", result.screen)

    def test_pressure_runs_are_chosen_by_hand_and_pressure_stops_between_them(self):
        self._execute([self.LIGHT], repeat=5, pressure=True, pressure_runs=[2, 4])

        self.assertEqual(self.events.count("pressure started"), 2)
        self.assertEqual(self.events.count("pressure stopped"), 2)
        self.assertEqual(
            [o for o in self.events if o in ("pressure started", "pressure stopped")],
            ["pressure started", "pressure stopped", "pressure started", "pressure stopped"])

    def test_no_load_process_starts_when_pressure_is_not_requested(self):
        self._execute([self.LIGHT], repeat=3)

        self.assertNotIn("pressure started", self.events)

    def test_an_invalid_pressure_run_and_a_short_repeat_are_rejected_before_anything_runs(self):
        self.assertEqual(self._execute([self.LIGHT], repeat=3, pressure=True, pressure_runs=[4]).code, gate.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self._execute([self.LIGHT], repeat=1).code, gate.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self._execute([], repeat=3).code, gate.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self.calls, [])

    def test_load_processes_are_cleaned_up_on_the_error_path(self):
        # If an unexpected error rises in the middle of a run, the pressure is stopped by `finally`.
        with mock.patch.object(gate, "_execute_stability_run", side_effect=[[], RuntimeError("exploded")]):
            with self.assertRaises(RuntimeError), contextlib.redirect_stdout(io.StringIO()):
                gate.stability_check(
                    [self.LIGHT], 5, pressure=True, pressure_runs=[2, 3], root=self.root,
                    runner=self._runner(), burner_factory=lambda count: _FakeBurner(count, self.events),
                    report_directory=self.root / "r", measurement_path=self.root / "measurement.jsonl")

        self.assertEqual(self.events[-2:], ["pressure started", "pressure stopped"])

    def test_when_pressure_cannot_start_exit_2_before_the_run_and_the_load_is_cleaned_up(self):
        class Broken(_FakeBurner):
            def start(self):
                super().start()
                raise OSError("fork failed")

        result = self._execute(
            [self.LIGHT], repeat=3, pressure=True, burner_factory=lambda count: Broken(count, self.events))

        self.assertEqual(result.code, gate.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self.events[-1], "pressure stopped")
        self.assertIn("CPU pressure could not be set up", result.screen)

    def test_the_run_does_not_start_if_the_pressure_processes_die_right_after_starting(self):
        class Dead(_FakeBurner):
            def alive(self):
                return 0

        result = self._execute([self.LIGHT], repeat=3, pressure=True, burner_factory=lambda count: Dead(count, self.events))

        self.assertEqual(result.code, gate.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self.events[-1], "pressure stopped")

    def test_report_directory_prunes_old_reports_and_trx_directories(self):
        report = self.root / "reports"
        for i in range(gate.STABILITY_REPORTS_KEEP + 3):
            (report).mkdir(exist_ok=True)
            (report / f"2020{i:04d}T000000Z.json").write_text("{}", encoding="utf-8")
            (report / f"2020{i:04d}T000000Z").mkdir()

        self._execute([self.LIGHT], repeat=2)

        reports = sorted(report.glob("*.json"))
        directories = sorted(p for p in report.iterdir() if p.is_dir())
        self.assertEqual(len(reports), gate.STABILITY_REPORTS_KEEP)
        self.assertEqual(len(directories), gate.STABILITY_TRX_DIRECTORIES_KEEP)
        self.assertTrue((report / "20261109T000000Z.json").is_file())
        self.assertTrue((report / "20261109T000000Z").is_dir())


class CpuLoadJobTests(unittest.TestCase):
    """`CpuBurner`: load processes are never left behind, whatever happens."""

    @staticmethod
    def _fake_popen(spawned: list[mock.Mock], *, fail_second: bool = False):
        def popen(args, **kwargs):
            if fail_second and len(spawned) == 1:
                raise OSError("fork failed")
            process = mock.Mock()
            process.poll.return_value = None
            spawned.append(process)
            return process
        return popen

    def test_load_process_count_is_half_the_cpu_count_and_at_least_one(self):
        self.assertEqual(
            [gate.stability_burner_count(n) for n in (1, 2, 3, 8, 10)], [1, 1, 1, 4, 5])

    def test_start_opens_that_many_processes_stop_ends_them_all_and_a_second_stop_is_harmless(self):
        spawned: list[mock.Mock] = []
        pressure = gate.CpuBurner(3, popen=self._fake_popen(spawned))

        pressure.start()
        pressure.start()
        self.assertEqual(len(spawned), 3)
        self.assertEqual(pressure.alive(), 3)
        pressure.stop()
        pressure.stop()

        self.assertFalse(pressure.running)
        for process in spawned:
            process.terminate.assert_called_once()
            process.wait.assert_called_once()

    def test_processes_stop_even_when_the_with_block_raises(self):
        spawned: list[mock.Mock] = []

        with self.assertRaises(RuntimeError):
            with gate.CpuBurner(2, popen=self._fake_popen(spawned)):
                raise RuntimeError("test exploded")

        for process in spawned:
            process.terminate.assert_called_once()

    def test_processes_already_opened_are_stopped_when_start_fails_midway(self):
        spawned: list[mock.Mock] = []
        pressure = gate.CpuBurner(3, popen=self._fake_popen(spawned, fail_second=True))

        with self.assertRaises(OSError):
            pressure.start()

        self.assertEqual(len(spawned), 1)
        spawned[0].terminate.assert_called_once()
        self.assertFalse(pressure.running)

    def test_a_process_that_does_not_exit_is_killed(self):
        spawned: list[mock.Mock] = []
        pressure = gate.CpuBurner(1, popen=self._fake_popen(spawned))
        pressure.start()
        spawned[0].wait.side_effect = [subprocess.TimeoutExpired("x", 5), None]

        pressure.stop()

        spawned[0].kill.assert_called_once()

    def test_a_real_load_process_starts_runs_and_ends_when_stopped(self):
        with gate.CpuBurner(1, lifetime_seconds=60) as pressure:
            process = pressure._processes[0]
            time.sleep(0.2)
            self.assertEqual(pressure.alive(), 1)
            self.assertIsNone(process.poll())

        self.assertIsNotNone(process.poll())

    def test_a_load_process_ends_by_itself_when_its_lifetime_runs_out(self):
        pressure = gate.CpuBurner(1, lifetime_seconds=0.3)
        pressure.start()
        process = pressure._processes[0]
        try:
            process.wait(timeout=20)
        finally:
            pressure.stop()

        self.assertEqual(process.returncode, 0)


class StabilityCommandTests(unittest.TestCase):
    """Module selection, the command line and the run preconditions."""

    def test_plan_covers_every_test_module_exactly_once(self):
        plan = gate.stability_plan()
        light = [project for project, stage, _ in plan if stage == "light"]
        heavy = {}
        for project, stage, lane in plan:
            if stage == "heavy":
                heavy.setdefault(lane, []).append(project)

        self.assertEqual(
            _section_problems(_slnx_test_modules(), gate.CORE_TEST_PROJECT, light, {k: tuple(v) for k, v in heavy.items()}), [])
        self.assertEqual(plan[0], (gate.CORE_TEST_PROJECT, "core", None))

    def test_units_resolve_the_binary_path_from_the_built_output_directory(self):
        units = gate.stability_units(["Tracon.Voice.UnitTests", "Tracon.Core.UnitTests"])

        self.assertEqual([unit.project for unit in units], ["Tracon.Core.UnitTests", "Tracon.Voice.UnitTests"])
        voice = units[1]
        self.assertEqual(voice.executable, ROOT / "artifacts" / "bin" / "Tracon.Voice.UnitTests" / "release" / "Tracon.Voice.UnitTests")
        self.assertEqual((voice.label, voice.stage, voice.lane), ("Tracon.Voice.UnitTests", "light", None))
        core = units[0]
        self.assertTrue(core.output.startswith("release_"), "Core is multi-target: the output directory carries the TFM")
        self.assertIn("(", core.label)

    def test_exclude_list_removes_a_module_project_list_narrows_and_an_unknown_name_is_an_error(self):
        all_units = gate.stability_units()
        excluded = gate.stability_units(exclude=["Tracon.Package.Tests", "Tracon.Ui.E2ETests"])
        only_one = gate.stability_units(["Tracon.Sqlite.IntegrationTests"])

        self.assertEqual(len(all_units) - len(excluded), 2)
        self.assertNotIn("Tracon.Package.Tests", [unit.project for unit in excluded])
        self.assertEqual([unit.project for unit in only_one], ["Tracon.Sqlite.IntegrationTests"])
        self.assertEqual(only_one[0].lane, "b")
        with self.assertRaises(ValueError) as error:
            gate.stability_units(["Tracon.Missing"])
        self.assertIn("Tracon.Missing", str(error.exception))
        with self.assertRaises(ValueError):
            gate.stability_units(exclude=["Tracon.Typo"])

    def _args(self, **changed):
        default = dict(
            repeat=5, project=None, exclude=None, shuffle=False, pressure=False, pressure_runs=None, seed=None,
            output=None, print_commands=False)
        default.update(changed)
        return types.SimpleNamespace(**default)

    def test_missing_built_binary_gives_exit_2_runs_nothing_and_suggests_a_build(self):
        with mock.patch.object(gate, "stability_check") as checks, \
                mock.patch.object(gate, "stability_units", return_value=[
                    gate.StabilityUnit("P", "release", "light", None, pathlib.Path("/absent/P"))]), \
                contextlib.redirect_stdout(io.StringIO()) as screen:
            code = gate.stability_from_arguments(self._args())

        self.assertEqual(code, gate.STABILITY_USAGE_EXIT_CODE)
        checks.assert_not_called()
        self.assertIn("does not build", screen.getvalue())
        self.assertIn("dotnet build", screen.getvalue())

    def test_repeat_must_be_at_least_two_and_an_unknown_module_is_rejected(self):
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(gate.stability_from_arguments(self._args(repeat=1)), gate.STABILITY_USAGE_EXIT_CODE)
            self.assertEqual(gate.stability_from_arguments(self._args(project=["Tracon.Missing"])), gate.STABILITY_USAGE_EXIT_CODE)

    def test_ready_binaries_pass_the_options_to_stability_check(self):
        with tempfile.TemporaryDirectory() as folder:
            binary = pathlib.Path(folder) / "P"
            binary.write_text("", encoding="utf-8")
            unit = gate.StabilityUnit("P", "release", "light", None, binary)
            with mock.patch.object(gate, "stability_check", return_value=0) as checks, \
                    mock.patch.object(gate, "stability_units", return_value=[unit]):
                code = gate.stability_from_arguments(self._args(
                    repeat=7, shuffle=True, pressure_runs=[2, 3], seed=42, output=folder))

        self.assertEqual(code, 0)
        checks.assert_called_once_with(
            [unit], 7, shuffle=True, pressure=True, pressure_runs=[2, 3], base_seed=42,
            report_directory=pathlib.Path(folder))

    def test_print_commands_lists_the_commands_without_running_anything(self):
        with mock.patch.object(gate, "stability_check") as checks, contextlib.redirect_stdout(io.StringIO()) as screen:
            code = gate.stability_from_arguments(self._args(
                project=["Tracon.Voice.UnitTests"], print_commands=True, shuffle=True, pressure=True))

        self.assertEqual(code, 0)
        checks.assert_not_called()
        lines = screen.getvalue().splitlines()
        self.assertTrue(any(line.startswith("# CPU pressure") for line in lines))
        command = next(line for line in lines if line.startswith("$ "))
        self.assertIn("Tracon.Voice.UnitTests", command)
        self.assertIn("--seed 1", command)

    def test_command_line_options_are_parsed(self):
        with mock.patch.object(gate, "stability_from_arguments", return_value=0) as command:
            code = gate.main([
                "stability", "--repeat", "4", "--project", "A", "B", "--project", "C", "--exclude", "D",
                "--shuffle", "--pressure", "--pressure-runs", "2", "3", "--seed", "9", "--output", "x"])

        self.assertEqual(code, 0)
        args = command.call_args.args[0]
        self.assertEqual(
            (args.repeat, args.project, args.exclude, args.shuffle, args.pressure, args.pressure_runs, args.seed, args.output),
            (4, ["A", "B", "C"], ["D"], True, True, [2, 3], 9, "x"))

    def test_default_repeat_is_five_and_the_command_accepts_the_top_level_print_commands_flag(self):
        with mock.patch.object(gate, "stability_from_arguments", return_value=0) as command:
            gate.main(["--print-commands", "stability"])

        args = command.call_args.args[0]
        self.assertEqual((args.repeat, args.print_commands), (5, True))


class StabilityWorkflowTests(unittest.TestCase):
    """`stability.yml`: the weekly stability run; it carries the contracts of ci.yml."""

    WORKFLOWS = ROOT / ".github" / "workflows"

    def setUp(self):
        self.text = (self.WORKFLOWS / "stability.yml").read_text(encoding="utf-8")
        self.uncommented = "\n".join(line for line in self.text.splitlines() if not line.lstrip().startswith("#"))

    def test_it_runs_weekly_and_by_hand_with_no_pull_request_and_no_push(self):
        self.assertRegex(self.uncommented, r"(?m)^on:\n  schedule:\n    - cron: '\d+ \d+ \* \* \d'\n  workflow_dispatch:")
        self.assertNotIn("pull_request", self.uncommented)
        self.assertNotRegex(self.uncommented, r"(?m)^  push:")

    def test_job_carries_the_canonical_repo_guard_a_time_limit_and_only_ubuntu(self):
        jobs = _ci_jobs(self.text)

        self.assertEqual(list(jobs), ["stability"])
        self.assertEqual(_unchained_unguarded_jobs(jobs), [])
        self.assertRegex(self.uncommented, r"(?m)^    timeout-minutes: \d+$")
        self.assertIn("runs-on: ubuntu-latest", self.uncommented)
        self.assertNotIn("windows", self.uncommented.lower())
        self.assertNotIn("macos", self.uncommented.lower())

    def test_utf8_mode_and_permissions_match_ci_yml(self):
        self.assertRegex(self.uncommented, r"(?m)^  PYTHONUTF8: 1$")
        self.assertRegex(self.uncommented, r"(?m)^permissions:\n  contents: read$")
        self.assertRegex(self.uncommented, r"(?m)^concurrency:\n  group: stability\n  cancel-in-progress: false$")

    def test_every_action_is_pinned_to_the_same_sha_as_in_ci_yml(self):
        ci = (self.WORKFLOWS / "ci.yml").read_text(encoding="utf-8")
        ci_pins = set(re.findall(r"uses:\s*(actions/[\w-]+@[0-9a-f]{40})", ci))
        local_actions = re.findall(r"uses:\s*(\S+)", self.uncommented)

        self.assertTrue(local_actions)
        for action in local_actions:
            with self.subTest(action=action):
                self.assertRegex(action, r"^actions/[\w-]+@[0-9a-f]{40}$")
                self.assertIn(action, ci_pins)

    def test_it_builds_once_then_runs_stability_and_always_uploads_the_report(self):
        steps = [line.strip() for line in self.uncommented.splitlines() if line.strip().startswith("- name:")]
        build_at = self.uncommented.index("dotnet build Tracon.slnx -c Release --no-restore")
        stability_at = self.uncommented.index("scripts/gate.py stability")

        self.assertEqual(self.uncommented.count("dotnet build"), 1)
        self.assertLess(build_at, stability_at)
        self.assertIn("--repeat 5 --shuffle --pressure", self.uncommented)
        self.assertEqual(steps[-1], "- name: Upload the report")
        self.assertRegex(self.uncommented, r"- name: Upload the report\n        if: always\(\)")
        self.assertIn("path: artifacts/stability/**", self.uncommented)

    def test_exclude_list_holds_real_modules_and_covers_the_container_package_and_browser_modules(self):
        single_line = re.sub(r"\s+", " ", self.uncommented)
        command = re.search(r"stability --repeat 5 .*?--exclude ((?:Tracon\.[\w.]+ ?)+)", single_line)
        excluded = command.group(1).split()
        modules = _slnx_test_modules()

        self.assertEqual([name for name in excluded if name not in modules], [])
        self.assertEqual(len(set(excluded)), len(excluded))
        self.assertEqual(
            sorted(excluded),
            sorted(["Tracon.SqlServer.IntegrationTests", "Tracon.PostgreSql.IntegrationTests",
                    "Tracon.TwoNode.IntegrationTests", "Tracon.Package.Tests", "Tracon.Ui.E2ETests"]))
        # The command really resolves: there is no unknown name.
        self.assertTrue(gate.stability_units(exclude=excluded))

    def test_file_name_is_the_workflow_name_that_the_remote_state_reader_looks_for(self):
        self.assertEqual(gate.STABILITY_WORKFLOW, "stability.yml")
        self.assertTrue((self.WORKFLOWS / gate.STABILITY_WORKFLOW).is_file())

    def test_the_unchained_workflows_test_also_covers_this_file(self):
        files = [p.name for p in self.WORKFLOWS.glob("*.yml") if p.name not in UNCHAINED_EXCLUDED_FILES]

        self.assertIn("stability.yml", files)

    def test_stability_command_does_not_change_the_ci_tests_or_the_closing(self):
        ci = (self.WORKFLOWS / "ci.yml").read_text(encoding="utf-8")

        self.assertNotRegex(ci, r"gate\.py\s+stability")


class CommandSurfaceTests(unittest.TestCase):
    """The command line of `gate.py` is English: command names, flag names, and every line the commands print."""

    COMMANDS = (
        "scan", "generate", "remote-state", "inner-loop", "close", "perf",
        "test", "stability", "release", "pack", "verify-packages", "capacity")
    RENAMED = {
        "tarama": "scan", "uret": "generate", "uzak-durum": "remote-state", "ic-dongu": "inner-loop",
        "kapanis": "close", "performans": "perf", "kararlilik": "stability", "yayin": "release",
        "paketle": "pack", "paket-dogrula": "verify-packages", "kapasite": "capacity"}
    TURKISH_LETTERS = r"[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]"
    WORKFLOWS = ROOT / ".github" / "workflows"

    @staticmethod
    def _run(argv: list[str]) -> tuple[int | str | None, str, str]:
        """(exit code, stdout, stderr) of `gate.main(argv)`; a run that returns instead of exiting reports its return value."""
        out, err = io.StringIO(), io.StringIO()
        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            try:
                code = gate.main(argv)
            except SystemExit as stop:
                code = stop.code
        return code, out.getvalue(), err.getvalue()

    def test_the_command_set_is_exactly_the_twelve_dictionary_names(self):
        _, subparsers = gate.build_parser()

        self.assertEqual(sorted(subparsers.choices), sorted(self.COMMANDS))
        self.assertEqual(len(subparsers.choices), 12)

    def test_every_old_command_name_exits_2_and_says_the_new_name(self):
        self.assertEqual(gate.RENAMED_COMMANDS, self.RENAMED)
        for old, new in self.RENAMED.items():
            for argv in ([old], ["--print-commands", old], [old, "--" "taban", "HEAD"]):
                with self.subTest(argv=argv):
                    code, out, err = self._run(argv)

                    self.assertEqual(code, 2)
                    self.assertEqual(out, "")
                    self.assertIn(f"'{old}' was renamed to '{new}'", err)

    def test_an_old_command_name_is_not_an_alias(self):
        _, subparsers = gate.build_parser()

        self.assertEqual(set(self.RENAMED) & set(subparsers.choices), set())

    def test_old_flag_names_are_rejected_including_their_abbreviation_forms(self):
        required = {
            "close": ["--base", "HEAD"], "test": ["--class", "A"], "release": ["--dry-run"],
            "pack": ["--output", "x"], "capacity": ["--profile", "smoke", "--package-version", "1.0.0"],
            "verify-packages": ["x"]}
        old_flags = {
            "close": ["--" "taban", "--site-atla"], "inner-loop": ["--" "taban"], "perf": ["--guncelle"],
            "test": ["--" "sinif", "--" "proje"],
            "stability": ["--tekrar", "--haric", "--sira-karistir", "--baski", "--baski-kosumlari", "--tohum", "--cikti"],
            "release": ["--kuru", "--surum", "--paket-dizini"], "pack": ["--cikti"],  # legacy-name
            "capacity": ["--profil", "--surum", "--cikti"]}  # legacy-name
        for command, flags in old_flags.items():
            for flag in flags:
                with self.subTest(command=command, flag=flag):
                    code, _, err = self._run([command, *required.get(command, []), flag, "1"])

                    self.assertEqual(code, 2)
                    self.assertIn("unrecognized arguments", err)
        code, _, err = self._run(["--komutlari-bas", "close", "--base", "HEAD"])
        self.assertEqual(code, 2)
        self.assertIn("unrecognized arguments", err)

    def test_a_call_without_a_command_lists_the_new_names(self):
        code, _, err = self._run([])

        self.assertEqual(code, 2)
        for name in self.COMMANDS:
            self.assertIn(name, err)
        self.assertNotRegex(err, self.TURKISH_LETTERS)

    def test_the_help_of_every_command_has_no_turkish_letter(self):
        for argv in ([], *([name] for name in self.COMMANDS)):
            with self.subTest(command=argv):
                code, out, _ = self._run([*argv, "--help"])

                self.assertEqual(code, 0)
                self.assertTrue(out.strip())
                self.assertNotRegex(out, self.TURKISH_LETTERS)

    def test_the_dry_run_listings_have_no_turkish_letter(self):
        with mock.patch.object(gate, "changed_paths", return_value=["src/Tracon.Core/Thing.cs"]):
            for argv in (
                ["generate"], ["remote-state"], ["perf"], ["verify-packages", "x"], ["pack", "--output", "x"],
                ["release", "--dry-run"], ["release", "--dry-run", "--package-directory", "x"], ["stability"],
                ["close", "--base", "HEAD"], ["inner-loop"],
            ):
                with self.subTest(command=argv):
                    code, out, _ = self._run(["--print-commands", *argv])

                    self.assertEqual(code, 0)
                    self.assertTrue(out.strip())
                    self.assertNotRegex(out, self.TURKISH_LETTERS)

    def test_workflow_script_calls_point_at_existing_files_and_existing_commands(self):
        _, subparsers = gate.build_parser()
        files_seen = commands_seen = 0
        for workflow in sorted(self.WORKFLOWS.glob("*.yml")):
            for number, line in enumerate(workflow.read_text(encoding="utf-8").splitlines(), 1):
                is_comment = line.lstrip().startswith("#")
                for match in re.finditer(r"scripts/([\w.-]+\.py)\b", line):
                    files_seen += 1
                    where = f"{workflow.name}:{number}: {match.group(0)}"
                    self.assertTrue((ROOT / "scripts" / match.group(1)).is_file(), where)
                    if match.group(1) != "gate.py" or is_comment:
                        continue
                    following = re.sub(r"\s#.*$", "", line[match.end():]).split()
                    candidate = next((word for word in following if not word.startswith("-")), None)
                    if candidate is not None and re.fullmatch(r"[a-z][a-z-]*", candidate):
                        commands_seen += 1
                        self.assertIn(candidate, subparsers.choices, where)
        self.assertGreater(files_seen, 0)
        self.assertGreater(commands_seen, 0)

    def test_remote_state_says_so_when_no_stability_run_exists_and_the_exit_code_does_not_change(self):
        sys.path.insert(0, str(ROOT / "scripts"))
        breaking_changes = importlib.import_module("breaking_changes")
        remote_state = importlib.import_module("remote_state")
        green = remote_state.CiState("completed", "success", "https://example.test/run", "abcdef01")

        def check(release: object, stability: object) -> tuple[int, str]:
            def ci_state(root, remote="origin", branch="main", *, workflow="ci.yml", **kwargs):  # noqa: ANN001, ANN202
                return stability if workflow == gate.STABILITY_WORKFLOW else green

            output = io.StringIO()
            with mock.patch.object(breaking_changes, "resolve_baseline", return_value="1.0.0"), \
                    mock.patch.object(remote_state, "packable_ids_at", return_value=["A", "B"]), \
                    mock.patch.object(remote_state, "release_state", return_value=release), \
                    mock.patch.object(remote_state, "ci_state", side_effect=ci_state), \
                    contextlib.redirect_stdout(output):
                code = gate.remote_state_check(ROOT)
            return code, output.getvalue()

        published = remote_state.ReleaseState("1.0.0", (), (), 2)
        incomplete = remote_state.ReleaseState("1.0.0", ("B",), (), 1)

        for release, expected_code in ((published, 0), (incomplete, 1)):
            with self.subTest(exit_code=expected_code):
                code_without_run, text = check(release, None)
                code_with_run, _ = check(release, green)

                self.assertEqual(code_without_run, expected_code)
                self.assertEqual(code_with_run, expected_code)
                self.assertIn("ℹ️ No decided stability run could be read for 'origin'", text)
                self.assertNotIn("is red", text)


class MalformedXmlScanTests(unittest.TestCase):
    def _write(self, root: pathlib.Path, name: str, text: str) -> None:
        path = root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")

    def test_a_double_hyphen_inside_an_xml_comment_is_reported(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write(root, "samples/NuGet.config", "<configuration><!-- run gate.py release --dry-run --></configuration>")
            self._write(root, "Directory.Build.props", "<Project><!-- fine --></Project>")
            self._write(root, "notes.txt", "<not xml")

            found = gate.find_malformed_xml(root)

        self.assertEqual(1, len(found), found)
        self.assertTrue(found[0].startswith(str(pathlib.Path("samples", "NuGet.config")) + ":"), found)

    def test_excluded_build_trees_are_not_read(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write(root, "obj/generated.props", "<broken")

            self.assertEqual([], gate.find_malformed_xml(root))

    def test_scan_turns_red_on_a_malformed_configuration_file_alone(self):
        empty = {
            "find_sync_copies": [], "find_secrets": ([], 0), "find_stale_doc_references": [],
            "public_surface_violations": [], "context7_version_drift": [], "upgrade_baseline_drift": [],
        }
        with tempfile.TemporaryDirectory() as directory, contextlib.ExitStack() as stack, \
                contextlib.redirect_stdout(io.StringIO()) as output:
            for name, value in empty.items():
                stack.enter_context(mock.patch.object(gate, name, return_value=value))
            root = pathlib.Path(directory)
            self._write(root, "Directory.Build.targets", "<Project><!-- a -- b --></Project>")

            result = gate.scan(root)

        self.assertEqual(1, result, output.getvalue())
        self.assertIn("not well-formed XML", output.getvalue())

    def test_scan_hands_its_extension_to_the_secret_scan(self):
        extension = gate.Extension(record_dirs=("archive",))
        with tempfile.TemporaryDirectory() as directory, contextlib.ExitStack() as stack, \
                contextlib.redirect_stdout(io.StringIO()):
            for name, value in {
                "find_sync_copies": [], "find_stale_doc_references": [], "public_surface_violations": [],
                "context7_version_drift": [], "upgrade_baseline_drift": [],
            }.items():
                stack.enter_context(mock.patch.object(gate, name, return_value=value))
            secrets = stack.enter_context(mock.patch.object(gate, "find_secrets", return_value=([], 0)))

            gate.scan(pathlib.Path(directory), extension)

        secrets.assert_called_once_with(pathlib.Path(directory), extension)


if __name__ == "__main__":
    unittest.main()
