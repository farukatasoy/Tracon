#!/usr/bin/env python3
"""Tracon validation gates.

The repository has several validation boundaries. This module is the single
executable source for their commands so CI, skills, and local development do
not grow subtly different copies of the same gate.
"""
from __future__ import annotations

import argparse
import dataclasses
import fnmatch
import hashlib
import json
import os
import pathlib
import re
import shlex
import shutil
import subprocess
import sys
import tempfile
import time
import zipfile
from collections.abc import Callable, Iterable, Mapping, Sequence

ROOT = pathlib.Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "artifacts"
MEASUREMENTS = ARTIFACTS / "kapi-olcum.jsonl"
PACKAGE_RELEASE_DIR = ARTIFACTS / "package" / "release"
PACKABLE_SOLUTION_FILTER = ROOT / "Tracon.src.slnf"
NPM_CLIENT_PACKAGE_DIR = ROOT / "packages" / "tracon-client"
RELEASE_VERSION_PATTERN = re.compile(r"^1\.0\.0-preview\.\d+$")
PRERELEASE_DEPENDENCY_PATTERN = re.compile(r'id="(?P<id>[^"]+)" version="[^"]*-[^"]*"')
REPOSITORY_COMMIT_PATTERN = re.compile(r'<repository[^>]+commit="[0-9a-f]{7,}"[^>]*/>')
RELEASE_NOTES_PATTERN = re.compile(r"<releaseNotes>(?P<url>[^<]*)</releaseNotes>")
K008_EXEMPT_PACKAGE = "Tracon.AspNetCore"
APPLIED_MIGRATION_MANIFEST = pathlib.PurePath("scripts", "applied-migrations.json")
GIT_COMMIT = re.compile(r"^[0-9a-f]{7,40}$")
TEST_MAX_CPU_COUNT = 1

SYNC_ROOTS = ("src", "tests", "samples", "docs", ".agents")
SCAN_EXCLUDED_DIRS = {
    ".git", "artifacts", "node_modules", "dist", ".astro", ".vite", "wwwroot",
    "obj", "bin", "TestResults", "manuel-test", "arsiv", "manuel-test-kosumu",
}
SYNC_NAME = re.compile(r".+ 2(?:\..+)?$")

# A markdown reference into the `docs/` tree, written OUTSIDE it. The negative lookbehind
# keeps `docs-site/src/content/docs/...` out: that is a SITE path, not a path in
# the `docs/` tree, and without the guard this gate produces dozens of false
# positives and gets switched off.
DOC_REFERENCE = re.compile(r"(?<![\w/-])docs/[A-Za-z0-9._/\-]+\.md")
DOC_REFERENCE_ROOTS = ("src", "tests", "samples", "bench", "scripts", "docs-site", ".agents", ".github")
# Metavariable filenames: an illustrative path in a template or in the archiving
# script's own comments. These never resolve and are not a defect.
DOC_REFERENCE_PLACEHOLDER = re.compile(r"(^|[-/])(NN|X|Y)([-.]|$)")
# A migration that already ran against a customer database is BYTE-FROZEN:
# `migration_integrity_violations` rejects any change to it, a comment included,
# and its own docstring says updating the manifest cannot approve one. A stale
# reference inside such a file therefore cannot be repaired — measured, not
# assumed. The gate must not demand the impossible, so those directories are
# exempt; the comment stays true of the moment the migration shipped.
DOC_REFERENCE_FROZEN_DIR = re.compile(r"(^|[\\/])Migrations[A-Za-z]*[\\/]")
SECRET_PATTERN = re.compile(
    r"sk-[a-z]+-[A-Za-z0-9_-]{24,}|AVNS_[A-Za-z0-9]{12,}|(Password|pwd)=[^ \";']{6,}"
)


@dataclasses.dataclass(frozen=True)
class Command:
    """A command executed by a gate."""

    args: tuple[str, ...]
    cwd: pathlib.Path = ROOT

    @property
    def display(self) -> str:
        command = shlex.join(self.args)
        if self.cwd != ROOT:
            command = f"(cd {shlex.quote(str(self.cwd.relative_to(ROOT)))} && {command})"
        return command


Runner = Callable[..., subprocess.CompletedProcess[str]]


def _record_measurement(
    stage: str,
    command: Command,
    started: float,
    exit_code: int,
    measurement_path: pathlib.Path,
) -> None:
    measurement_path.parent.mkdir(parents=True, exist_ok=True)
    row = {
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "stage": stage,
        "command": command.display,
        "duration_seconds": round(time.monotonic() - started, 3),
        "exit_code": exit_code,
    }
    with measurement_path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(row, ensure_ascii=False) + "\n")


def run_commands(
    stage: str,
    commands: Sequence[Command],
    *,
    runner: Runner = subprocess.run,
    measurement_path: pathlib.Path = MEASUREMENTS,
    dry_run: bool = False,
) -> int:
    """Run commands in order and stop at the first non-zero exit code."""
    environment = os.environ.copy()
    environment["MSBUILDDISABLENODEREUSE"] = "1"

    for command in commands:
        print(f"$ {command.display}")
        if dry_run:
            continue

        started = time.monotonic()
        wall_started = time.time()
        try:
            result = runner(
                list(command.args),
                cwd=str(command.cwd),
                env=environment,
                check=False,
            )
            exit_code = int(result.returncode)
        except FileNotFoundError as exception:
            print(f"❌ Komut bulunamadı: {exception.filename}")
            exit_code = 127
        except OSError as exception:
            print(f"❌ Komut çalıştırılamadı: {exception}")
            exit_code = 126

        _record_measurement(stage, command, started, exit_code, measurement_path)
        elapsed = time.monotonic() - started
        if exit_code:
            print(f"❌ Çıkış {exit_code} ({elapsed:.2f} s): {command.display}")
            if command.args[:2] == ("dotnet", "test"):
                isolate_failed_tests(failed_tests_from_trx(wall_started), runner=runner)
            return exit_code
        print(f"✅ {elapsed:.2f} s")

    return 0


def _walk_files(root: pathlib.Path, *, excluded_dirs: set[str] | None = None) -> Iterable[pathlib.Path]:
    excluded = SCAN_EXCLUDED_DIRS if excluded_dirs is None else excluded_dirs
    if not root.exists():
        return
    for directory, directories, files in os.walk(root):
        directories[:] = [name for name in directories if name not in excluded]
        directory_path = pathlib.Path(directory)
        for name in files:
            yield directory_path / name


def find_sync_copies(root: pathlib.Path = ROOT) -> list[pathlib.Path]:
    """Return file and directory names produced by cloud sync copy suffixes."""
    found: list[pathlib.Path] = []
    for relative_root in SYNC_ROOTS:
        base = root / relative_root
        if not base.exists():
            continue
        for directory, directories, files in os.walk(base):
            directories[:] = [name for name in directories if name not in {"node_modules", "obj", "bin"}]
            for name in (*directories, *files):
                if SYNC_NAME.fullmatch(name):
                    found.append(pathlib.Path(directory) / name)
    return sorted(found)


def find_secrets(root: pathlib.Path = ROOT) -> list[str]:
    """Return matching path/line records, ignoring documented test fixtures."""
    found: list[str] = []
    for path in _walk_files(root):
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except (OSError, UnicodeDecodeError):
            continue
        for line_number, line in enumerate(lines, 1):
            if SECRET_PATTERN.search(line):
                found.append(f"{path.relative_to(root)}:{line_number}:{line}")
    return found


def find_stale_doc_references(root: pathlib.Path = ROOT) -> list[str]:
    """Return `path:line:reference` records for `docs/` targets that do not exist.

    Archiving a phase moves `docs/NN-AD.md` to `docs/arsiv/fazlar/NN-AD.md`.
    `faz-arsivle` rewrites the links inside `docs/`; every reference OUTSIDE that
    tree — a migration comment, an analyzer release note that SHIPS in the
    package, a CI workflow — was left pointing at a path that no longer exists.
    """
    found: list[str] = []
    for relative_root in DOC_REFERENCE_ROOTS:
        base = root / relative_root
        if not base.exists():
            continue
        for path in _walk_files(base, excluded_dirs=SCAN_EXCLUDED_DIRS - {"arsiv"}):
            relative = path.relative_to(root)
            if DOC_REFERENCE_FROZEN_DIR.search(str(relative)):
                continue
            try:
                lines = path.read_text(encoding="utf-8").splitlines()
            except (OSError, UnicodeDecodeError):
                continue
            for line_number, line in enumerate(lines, 1):
                for reference in DOC_REFERENCE.findall(line):
                    name = pathlib.PurePosixPath(reference).stem
                    if DOC_REFERENCE_PLACEHOLDER.search(name):
                        continue
                    if (root / reference).exists():
                        continue
                    found.append(f"{relative}:{line_number}:{reference}")
    return sorted(found)


def scan(root: pathlib.Path = ROOT) -> int:
    """Run the synchronization-copy, secret, migration-integrity and doc-reference scans."""
    copies = find_sync_copies(root)
    secrets = find_secrets(root)
    migrations = migration_integrity_violations(root)
    stale_docs = find_stale_doc_references(root)
    if not copies and not secrets and not migrations and not stale_docs:
        print("Tarama: ✅ temiz")
        return 0

    if copies:
        print("Tarama: ❌ senkronizasyon kopyaları")
        for path in copies:
            print(f"  {path.relative_to(root)}")
    if secrets:
        print("Tarama: ❌ olası secret")
        for match in secrets[:20]:
            print(f"  {match}")
        if len(secrets) > 20:
            print(f"  … +{len(secrets) - 20}")
    if migrations:
        print("Tarama: ❌ uygulanmış migration değişikliği")
        for violation in migrations:
            print(f"  {violation}")
    if stale_docs:
        print("Tarama: ❌ bayat doküman referansı (arşivlenen faz)")
        for reference in stale_docs[:20]:
            print(f"  {reference}")
        if len(stale_docs) > 20:
            print(f"  … +{len(stale_docs) - 20}")
    return 1


def _git(*args: str) -> list[str] | None:
    try:
        result = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    except OSError:
        return None
    if result.returncode:
        return None
    return [line for line in result.stdout.splitlines() if line.strip()]


def migration_integrity_violations(
    root: pathlib.Path = ROOT,
    *,
    baseline_commit: str | None = None,
    source_commits: Mapping[str, str] | None = None,
    source_reader: Callable[[str, str], bytes | None] | None = None,
) -> list[str]:
    """Find changes to migrations that already belong to the release line.

    The manifest anchors each migration to immutable Git content. Its base
    commit freezes the release line; an explicit per-file source commit covers
    a known repair whose safe content predates that base. Updating the manifest
    alone can therefore never approve a changed migration byte.
    """
    if baseline_commit is None or source_commits is None:
        manifest = root / APPLIED_MIGRATION_MANIFEST
        try:
            loaded = json.loads(manifest.read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exception:
            return [f"{APPLIED_MIGRATION_MANIFEST}: applied migration manifest okunamadı: {exception}"]
        if not isinstance(loaded, dict):
            return [f"{APPLIED_MIGRATION_MANIFEST}: git tabanı geçersiz"]

        baseline_commit = loaded.get("baselineCommit")
        source_commits = loaded.get("sourceCommits", {})

    if not isinstance(baseline_commit, str) or not GIT_COMMIT.fullmatch(baseline_commit):
        return [f"{APPLIED_MIGRATION_MANIFEST}: baselineCommit geçersiz"]
    if not isinstance(source_commits, Mapping) or not all(
        isinstance(path, str) and isinstance(commit, str) and GIT_COMMIT.fullmatch(commit)
        for path, commit in source_commits.items()
    ):
        return [f"{APPLIED_MIGRATION_MANIFEST}: sourceCommits eşlemesi geçersiz"]

    if source_reader is None:
        def source_reader(commit: str, relative: str) -> bytes | None:
            try:
                result = subprocess.run(
                    ["git", "show", f"{commit}:{relative}"],
                    cwd=root,
                    capture_output=True,
                    check=False,
                )
            except OSError:
                return None
            return result.stdout if result.returncode == 0 else None

    migrations = sorted(
        [*root.glob("src/*/Migrations/*.sql"), *root.glob("src/*/MigrationsKnowledge/*.sql")])
    found = {migration.relative_to(root).as_posix(): migration for migration in migrations}
    violations: list[str] = []

    for relative, migration in found.items():
        source_commit = source_commits.get(relative, baseline_commit)
        source = source_reader(source_commit, relative)
        if source is None:
            violations.append(f"{relative}: git tabanı {source_commit[:12]} dosyayı içermiyor")
            continue
        if migration.read_bytes() != source:
            violations.append(f"{relative}: applied migration içeriği git tabanı {source_commit[:12]} ile farklı")

    for relative in sorted(set(source_commits).difference(found)):
        violations.append(f"{relative}: sourceCommits içinde var ama migration dosyası yok")

    return violations


def changed_paths(base: str = "HEAD") -> list[str] | None:
    """Include the base..working-tree range plus untracked paths.

    `base` defaults to HEAD (uncommitted changes only, used by `ic-dongu`);
    `kapanis` passes its own `--taban` so the performance gate's trigger check
    (see `performance_gate_triggered`) sees the WHOLE phase's changes, not just
    what happens to be uncommitted at closing time.
    """
    paths: set[str] = set()
    for arguments in (("diff", "--name-only", base), ("status", "--porcelain")):
        result = _git(*arguments)
        if result is None:
            return None
        if arguments[0] == "diff":
            paths.update(result)
        else:
            paths.update(line[3:].strip('"') for line in result if line.startswith("?? "))
    return sorted(paths)


def frontend_changed(paths: Iterable[str]) -> bool:
    return any(path.startswith("src/Tracon.UI/") or path.startswith("packages/tracon-client/") for path in paths)


# -----------------------------------------------------------------------------
# `performans` - allocation gate (docs/arsiv/fazlar/116-PERFORMANS-TAHSIS-KAPISI.md)
#
# The gate compares ALLOCATED BYTES, never wall-clock duration: on a shared CI
# runner, allocation is deterministic (same code -> same byte count) while
# duration moves with neighboring jobs (116.1). Duration is still recorded in
# bench/baseline.json, as information only - it never fails the gate.
# -----------------------------------------------------------------------------

PERFORMANCE_HOT_PATHS = (
    "src/Tracon.Core/Recording/RunEventWriter.cs",
    "src/Tracon.Core/Compilation/CompiledAgentCache.cs",
    "src/Tracon.Sql.Shared/Stores/SqlRunStore.cs",
    "bench/",
)

BENCHMARK_PROJECT = ROOT / "bench" / "Tracon.Benchmarks" / "Tracon.Benchmarks.csproj"
BENCHMARK_BASELINE = ROOT / "bench" / "baseline.json"
BENCHMARK_REPORT_DIR = ARTIFACTS / "benchmarks" / "results"


def performance_gate_triggered(paths: Iterable[str]) -> bool:
    """Whether any of the three benchmarked hot paths (or the benchmark project
    itself) changed - the allocation gate only runs when it can move (116.4)."""
    return any(
        path == hot_path or (hot_path.endswith("/") and path.startswith(hot_path))
        for path in paths
        for hot_path in PERFORMANCE_HOT_PATHS
    )


class BaselineError(Exception):
    """Raised when bench/baseline.json is missing or malformed - always
    surfaced as a clear message, never a bare traceback (116, DoD)."""


def load_baseline(path: pathlib.Path = BENCHMARK_BASELINE) -> dict:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exception:
        raise BaselineError(f"{path}: taban çizgisi okunamadı: {exception}") from exception

    try:
        document = json.loads(text)
    except json.JSONDecodeError as exception:
        raise BaselineError(f"{path}: taban çizgisi geçersiz JSON: {exception}") from exception

    if not isinstance(document, dict) or not isinstance(document.get("benchmarks"), dict):
        raise BaselineError(f"{path}: taban çizgisi beklenen 'benchmarks' anahtarını taşımıyor")

    return document


def write_baseline(benchmarks: Mapping[str, Mapping[str, float]], path: pathlib.Path = BENCHMARK_BASELINE) -> None:
    document = {
        "measuredAt": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "benchmarks": {name: dict(entry) for name, entry in sorted(benchmarks.items())},
    }
    path.write_text(json.dumps(document, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def parse_benchmark_report(report: Mapping) -> dict[str, dict[str, float]]:
    """Extract {full benchmark name: {allocatedBytes, meanNanoseconds}} from a
    BenchmarkDotNet FULL JSON export (`--exporters json`)."""
    parsed: dict[str, dict[str, float]] = {}
    for entry in report.get("Benchmarks", []):
        name = entry.get("FullName") or f"{entry.get('Type')}.{entry.get('Method')}"
        memory = entry.get("Memory") or {}
        statistics = entry.get("Statistics") or {}
        parsed[name] = {
            "allocatedBytes": memory.get("BytesAllocatedPerOperation"),
            "meanNanoseconds": statistics.get("Mean"),
        }
    return parsed


def compare_allocations(
    baseline: Mapping[str, Mapping[str, float]],
    current: Mapping[str, Mapping[str, float]],
) -> tuple[int, list[str]]:
    """Compare CURRENT allocation against BASELINE. Zero tolerance: an increase
    is red; a decrease is a warning (the baseline is now stale), never red -
    the gate must not silently measure against a loose old number forever."""
    messages: list[str] = []
    exit_code = 0

    for name in sorted(set(baseline) | set(current)):
        if name not in current:
            messages.append(f"❌ {name}: bu koşumda üretilmedi (baseline'da var - kaldırılmış olabilir)")
            exit_code = 1
            continue
        if name not in baseline:
            messages.append(f"❌ {name}: taban çizgisinde yok - 'kapi.py performans --guncelle' ile ekleyin")
            exit_code = 1
            continue

        baseline_bytes = baseline[name]["allocatedBytes"]
        current_bytes = current[name]["allocatedBytes"]

        if current_bytes > baseline_bytes:
            messages.append(
                f"❌ {name}: tahsis arttı ({baseline_bytes} B → {current_bytes} B, +{current_bytes - baseline_bytes} B)")
            exit_code = 1
        elif current_bytes < baseline_bytes:
            messages.append(
                f"⚠️ {name}: tahsis azaldı ({baseline_bytes} B → {current_bytes} B) - "
                "taban çizgisi güncellenmeli: 'python3 scripts/kapi.py performans --guncelle'")
        else:
            messages.append(f"✅ {name}: {current_bytes} B")

    return exit_code, messages


def _run_benchmark_project(
    *,
    runner: Runner = subprocess.run,
    report_dir: pathlib.Path = BENCHMARK_REPORT_DIR,
) -> subprocess.CompletedProcess[str]:
    # BenchmarkDotNet writes ONE *-report-full.json PER BENCHMARK CLASS, not
    # one combined file for the whole run. A stale file from a renamed or
    # removed benchmark class must not linger and get merged into `current` -
    # the whole results directory is cleared before every run.
    if report_dir.exists():
        shutil.rmtree(report_dir)

    environment = os.environ.copy()
    environment["MSBUILDDISABLENODEREUSE"] = "1"
    return runner(
        ["dotnet", "run", "-c", "Release", "--project", str(BENCHMARK_PROJECT),
         "--", "--filter", "*", "--exporters", "json"],
        cwd=str(ROOT), env=environment, check=False)


def _benchmark_report_files(report_dir: pathlib.Path = BENCHMARK_REPORT_DIR) -> list[pathlib.Path]:
    return sorted(report_dir.glob("*-report-full.json")) if report_dir.exists() else []


def performance_gate(
    *,
    update: bool = False,
    runner: Runner = subprocess.run,
    baseline_path: pathlib.Path = BENCHMARK_BASELINE,
    report_dir: pathlib.Path = BENCHMARK_REPORT_DIR,
) -> int:
    try:
        baseline_document = {} if update else load_baseline(baseline_path)
    except BaselineError as exception:
        print(f"❌ {exception}")
        return 1

    result = _run_benchmark_project(runner=runner, report_dir=report_dir)
    if result.returncode:
        print(f"❌ benchmark koşumu çıkış {result.returncode}")
        return result.returncode

    report_files = _benchmark_report_files(report_dir)
    if not report_files:
        print(f"❌ BenchmarkDotNet JSON raporu bulunamadı ({report_dir})")
        return 1

    current: dict[str, dict[str, float]] = {}
    for report_path in report_files:
        current.update(parse_benchmark_report(json.loads(report_path.read_text(encoding="utf-8"))))

    if update:
        write_baseline(current, baseline_path)
        display_path = baseline_path.relative_to(ROOT) if ROOT in baseline_path.parents else baseline_path
        print(f"✅ taban çizgisi güncellendi: {display_path}")
        for name, entry in sorted(current.items()):
            print(f"  {name}: {entry['allocatedBytes']} B")
        return 0

    exit_code, messages = compare_allocations(baseline_document["benchmarks"], current)
    for message in messages:
        print(message)
    return exit_code


TEST_PROJECTS: dict[str, tuple[str, ...]] = {
    "Tracon.Abstractions": ("Tracon.Core.UnitTests",),
    "Tracon.Core": ("Tracon.Core.UnitTests", "Tracon.AspNetCore.FunctionalTests"),
    "Tracon.Generators": ("Tracon.Generators.UnitTests",),
    "Tracon.PostgreSql": ("Tracon.PostgreSql.IntegrationTests",),
    "Tracon.SqlServer": ("Tracon.SqlServer.IntegrationTests",),
    "Tracon.Sqlite": ("Tracon.Sqlite.IntegrationTests",),
    "Tracon.OpenAI": ("Tracon.OpenAI.UnitTests", "Tracon.Generators.UnitTests"),
    "Tracon.Anthropic": ("Tracon.Anthropic.UnitTests",),
    "Tracon.Google": ("Tracon.Google.UnitTests",),
    "Tracon.Azure": ("Tracon.Azure.UnitTests",),
    "Tracon.Voice": ("Tracon.Voice.UnitTests",),
    "Tracon.Mcp": ("Tracon.Mcp.UnitTests",),
    "Tracon.Workflows": ("Tracon.Workflows.UnitTests",),
    "Tracon.AspNetCore": ("Tracon.AspNetCore.FunctionalTests",),
    "Tracon.UI": ("Tracon.Ui.E2ETests",),
    "Tracon.Testing": ("Tracon.Testing.UnitTests",),
    "Tracon.Client": ("Tracon.Client.UnitTests",),
    "Tracon.Cli": ("Tracon.Cli.FunctionalTests",),
}


# Every src/ change reaches this project, whatever the package: its
# ExampleExtractor enumerates src/**/*.cs and COMPILES every <example> block it
# finds, so an XML doc comment added anywhere under src/ can break it. Measured
# escape: 9433efe4 ("caught only by the full solution test run, not by
# Tracon.Core.UnitTests alone") - the inner loop selected Core.UnitTests +
# AspNetCore.FunctionalTests and never ran the project that actually failed.
# Costs 2.4 s measured (2026-09-04 per-project profile).
EXAMPLE_COMPILING_TEST_PROJECT = "Tracon.Generators.UnitTests"

# A sample directory the solution really tests, and the project that tests it
# (via ProjectReference). Measured escape: a377106e - phase 139 added a sixth
# embedding point, samples/Tracon.Embedded fell behind, and `ic-dongu`
# selected NO test project at all for a samples/ change.
SAMPLE_TEST_PROJECTS: dict[str, str] = {
    "Tracon.Embedded": "Tracon.Embedded.Tests",
}

# These ship as consumer-facing samples but are NOT in Tracon.slnx: they
# compile against packed NuGet packages, so no closing-gate test run covers
# them. Only `kapi.py yayin` (release_extension_samples.py) does. Saying so out
# loud beats selecting nothing silently.
RELEASE_ONLY_SAMPLE_PREFIX = "samples/Tracon.Samples."


def affected_test_projects(paths: Iterable[str]) -> tuple[list[str], bool]:
    projects: set[str] = set()
    needs_full = False
    for path in paths:
        if path.startswith("tests/Shared/"):
            needs_full = True
            continue
        if path.startswith("tests/"):
            match = re.match(r"tests/([^/]+)/", path)
            if match:
                projects.add(match.group(1))
            continue
        if path.startswith("src/"):
            projects.add(EXAMPLE_COMPILING_TEST_PROJECT)
            match = re.match(r"src/([^/]+)/", path)
            package = match.group(1).removesuffix(".csproj") if match else ""
            if package in TEST_PROJECTS:
                projects.update(TEST_PROJECTS[package])
            else:
                needs_full = True
        elif path.startswith("samples/"):
            if path.startswith(RELEASE_ONLY_SAMPLE_PREFIX):
                print(
                    f"⚠️ {path}: bu örnek çözümde değildir; yalnız "
                    "`python3 scripts/kapi.py yayin` onu paketlenmiş sürüme karşı koşar.")
                continue
            match = re.match(r"samples/([^/]+)/", path)
            sample = match.group(1) if match else ""
            if sample in SAMPLE_TEST_PROJECTS:
                projects.add(SAMPLE_TEST_PROJECTS[sample])
        elif path.startswith("packages/") or path.startswith("scripts/"):
            projects.add("Tracon.Client.UnitTests" if path.startswith("packages/") else "Tracon.Core.UnitTests")
    return sorted(projects), needs_full


def _dotnet_test_project(project: str) -> Command:
    return Command(("dotnet", "test", f"tests/{project}/{project}.csproj", "-c", "Release", "--no-build"))


def full_solution_test_command() -> Command:
    """Run test projects with enough isolation for Docker and package tests.

    A solution test run otherwise starts every test executable at once. The
    concurrent Docker containers, Playwright browser, functional hosts, and
    package fixture can starve each other and turn healthy short deadlines into
    timeouts. The resource-heavy test projects must run one at a time because
    each of them can start additional processes and exhaust the local Docker
    memory budget.
    """
    # `-- --report-trx` ci.yml:183 ile AYNI: dusen testin adi makine
    # okunur hale gelir ve `isolate_failed_tests` onu izole tekrar kosar.
    # Olculdu 2026-09-04: TRX yazimi kosum suresini olcum gurultusunun
    # altinda etkiler (557 sn TRX'li, 561 sn TRX'siz).
    return Command((
        "dotnet", "test", "Tracon.slnx", "-c", "Release", "--no-build",
        f"-maxcpucount:{TEST_MAX_CPU_COUNT}", "--", "--report-trx"))


TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


def failed_tests_from_trx(since: float, root: pathlib.Path = ROOT) -> list[tuple[str, str]]:
    """(proje, tam test adi) — `since`'ten sonra yazilmis her TRX'teki dusen test."""
    import xml.etree.ElementTree as elementtree

    failures: list[tuple[str, str]] = []
    for trx in sorted((root / "artifacts" / "bin").glob("*/release/TestResults/*.trx")):
        try:
            if trx.stat().st_mtime < since:
                continue
            tree = elementtree.parse(trx)
        except (OSError, elementtree.ParseError):
            continue
        project = trx.relative_to(root / "artifacts" / "bin").parts[0]
        for result in tree.getroot().iter(TRX_NS + "UnitTestResult"):
            if result.get("outcome") not in (None, "Passed", "NotExecuted"):
                name = result.get("testName")
                if name:
                    failures.append((project, name))
    return failures


def isolate_failed_tests(
    failures: Sequence[tuple[str, str]],
    *,
    runner: Runner = subprocess.run,
    root: pathlib.Path = ROOT,
) -> list[tuple[str, str, bool]]:
    """Dusen her testi TEK BASINA tekrar kos ve hukmu bas.

    `kusur-giderme` Adim 2'nin uc adimli el yordami budur. Bu repoda "tam
    kosumda duser, izole gecer" sinifi YEDI kez kayda gecti: alti vaka
    `docs/hafiza/test-yalitimi.md`'de belgelidir (sinif Faz 103'te acildi;
    Faz 82 · 130 · 134 · 141 ayri testlerle tekrarladi), yedincisini
    2026-09-04 surec denetiminin taban kosumu uretti. Her seferinde ayirt
    etme ELLE yapildi — cogu kez TAM paketi ikinci kez kosarak (~9 dakika,
    olculdu: 553 sn). Izole kosum saniyeler surer (olculdu: 2,5 sn).

    🚨 CIKIS KODUNU DEGISTIRMEZ. Bu bir teshistir, kapi gevsemesi degil:
    kirmizi kirmizi kalir. Verdigi tek sey, bir sonraki adimin ne oldugudur.
    """
    verdicts: list[tuple[str, str, bool]] = []
    for project, name in failures:
        executable = root / "artifacts" / "bin" / project / "release" / project
        # Bir `[Theory]`'nin TRX adi argumanlari da tasir ve onlar NOKTA
        # icerebilir: `...A_provider_name_is_matched(savedAs: "a.b")`. Once
        # arguman kuyrugu atilir, SONRA son parca alinir - ters sira metot
        # adi yerine bir argumani filtreye koyardi.
        method = name.split("(", 1)[0].rsplit(".", 1)[-1]
        if not executable.exists():
            print(f"   ⚠️ {project}: derlenmis ikili yok, izole kosum atlandi")
            continue
        environment = os.environ.copy()
        environment["MSBUILDDISABLENODEREUSE"] = "1"
        result = runner(
            [str(executable), "--filter-method", f"*{method}*"],
            cwd=str(root), env=environment, check=False,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        verdicts.append((project, name, int(result.returncode) == 0))

    if not verdicts:
        return verdicts
    print("\n🔬 İzole yeniden koşum (teşhis — çıkış kodunu DEĞİŞTİRMEZ):")
    for project, name, passed in verdicts:
        print(f"   {'✅ izole GEÇTİ' if passed else '❌ izole de DÜŞTÜ'}  {project} · {name}")
    if all(passed for _, _, passed in verdicts):
        print("   → Hepsi izole geçti: tam koşum kaynak çekişmesi sınıfı"
              " (docs/hafiza/test-altyapisi.md). Tam paketi TEKRAR koş;"
              " ikinci koşumda da düşerse gerçek kusurdur.")
    else:
        print("   → İzole de düşen test GERÇEK regresyondur;"
              " `kusur-giderme` skill'ini koş.")
    return verdicts


def inner_loop_commands(paths: list[str]) -> list[Command]:
    frontend_flag = () if frontend_changed(paths) else ("-p:TraconFrontendEnabled=false",)
    commands = [Command(("dotnet", "build", "Tracon.slnx", "-c", "Release", *frontend_flag))]
    projects, needs_full = affected_test_projects(paths)
    if needs_full:
        print("⚠️ Etkilenen proje haritası eksik; test seçimi tam koşuma genişletildi.")
        return commands + [full_solution_test_command()]
    return commands + [_dotnet_test_project(project) for project in projects]


def closing_commands(base: str, *, site: bool = True, performance: bool = True) -> list[Command]:
    commands = [
        Command(("python3", "scripts/kapi.py", "tarama")),
        Command(("python3", "scripts/dokuman-bakim.py", "--denetle")),
        Command(("python3", "-m", "unittest", "discover", "-s", "scripts", "-p", "*_test.py")),
        Command(("node", "docs-site/scripts/build-agent-map.mjs", "--check")),
        Command(("python3", "scripts/denetim-paketi.py", "--taban", base)),
        Command(("dotnet", "build", "Tracon.slnx", "-c", "Release")),
        full_solution_test_command(),
        # TraconSkipCleanWorkingTreeCheck (Faz 136): this pack validates the
        # PACKAGING CONTRACT (README, icon, K-008) during iteration - it is not
        # a release candidate, so it must still work on an uncommitted tree.
        # The real release rehearsal (`kapi.py yayin`) and the CI `pack` job a
        # `v*` tag actually publishes from both leave this unset and stay fully
        # gated by TraconValidateCleanWorkingTree.
        Command((
            "dotnet", "pack", "Tracon.slnx", "-c", "Release", "--no-build",
            "-p:TraconSkipCleanWorkingTreeCheck=true")),
        # 🚨 NOT --no-restore: measured (2026-08-27) that dotnet format's
        # restore-less MSBuildWorkspace load intermittently fails to resolve
        # PackageReference types for every samples/*.Tests project (a
        # floating-version, local-feed consumer of Tracon.* packages)
        # specifically right after the full solution test run above -
        # reproduced 3 times through this exact command chain, never in
        # isolation. A plain restore immediately before format is a few
        # seconds when nothing changed ("all projects are up-to-date") and
        # reliably avoids the false failure.
        Command(("dotnet", "format", "Tracon.slnx", "--verify-no-changes")),
    ]
    # Path-triggered (116.4): a fifth ALWAYS-ON gate would run BenchmarkDotNet
    # on every phase closing, even when nothing near the three hot paths
    # changed. `kapi.py performans` stays a normal subcommand; `main()` decides
    # whether THIS closing run needs it, the same way `affected_test_projects`
    # already scopes `ic-dongu`.
    if performance:
        commands.append(Command(("python3", "scripts/kapi.py", "performans")))
    if site:
        commands.append(Command(("npm", "run", "check"), ROOT / "docs-site"))
    return commands


def test_command(project: str, patterns: Sequence[str]) -> Command:
    executable = ROOT / "artifacts" / "bin" / project / "release" / project
    return Command((str(executable), "--filter-class", *patterns))


# -----------------------------------------------------------------------------
# `yayin` - release rehearsal (docs/arsiv/fazlar/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md, 97.2)
#
# Writes nothing to a network. It packs the solution (forcing a version via the
# MinVerVersionOverride environment variable when --surum is given - no git tag
# is created), then reads back exactly what `dotnet pack` produced and checks it
# against the packaging contract every published package must satisfy.
# -----------------------------------------------------------------------------


def packable_project_ids(root: pathlib.Path = ROOT) -> list[str]:
    """Package identities the rehearsal must account for - derived from
    src/*/*.csproj, never hand-written, so a new packable project enters this
    list on its own."""
    ids = []
    for csproj in sorted((root / "src").glob("*/*.csproj")):
        if "<IsPackable>false</IsPackable>" in csproj.read_text(encoding="utf-8"):
            continue
        ids.append(csproj.stem)
    return ids


def _package_profile(root: pathlib.Path, project_id: str) -> str:
    text = (root / "src" / project_id / f"{project_id}.csproj").read_text(encoding="utf-8")
    if "<PackAsTool>true</PackAsTool>" in text:
        return "tool"
    if "<IncludeBuildOutput>false</IncludeBuildOutput>" in text:
        return "content" if "<PackageType>Template</PackageType>" in text else "meta"
    return "library"


POLYFORM_LICENSE_FILE = "LICENSE.md"
MIT_LICENSE_FILE = "LICENSE-MIT.md"
LICENSE_FILES = (POLYFORM_LICENSE_FILE, MIT_LICENSE_FILE)

# Faz 160. Her paketin lisansı BURADA açıkça yazılıdır; varsayılan yoktur.
# Yeni bir paket eklendiğinde bu tabloya girmezse kapı hata verir - amaç tam
# olarak budur: lisans bir sonuç değil, bilinçli bir karardır. Üç paket MIT'dir
# ki üçüncü taraf ticari lisans almadan eklenti yazıp test edebilsin ve
# `dotnet new`'in ürettiği koda sahip olsun. Agent çalıştıran her şey PolyForm.
# Kaynak: src/Directory.Build.props (TraconMitLicensed) - iki liste AYNI
# olmalıdır; `PackageLicenseTests` bunu kilitler.
PACKAGE_LICENSES: dict[str, str] = {
    "Tracon": POLYFORM_LICENSE_FILE,
    "Tracon.Abstractions": MIT_LICENSE_FILE,
    "Tracon.Anthropic": POLYFORM_LICENSE_FILE,
    "Tracon.AspNetCore": POLYFORM_LICENSE_FILE,
    "Tracon.Azure": POLYFORM_LICENSE_FILE,
    "Tracon.Cli": POLYFORM_LICENSE_FILE,
    "Tracon.Client": POLYFORM_LICENSE_FILE,
    "Tracon.Core": POLYFORM_LICENSE_FILE,
    "Tracon.Google": POLYFORM_LICENSE_FILE,
    "Tracon.Mcp": POLYFORM_LICENSE_FILE,
    "Tracon.OpenAI": POLYFORM_LICENSE_FILE,
    "Tracon.PostgreSql": POLYFORM_LICENSE_FILE,
    "Tracon.SqlServer": POLYFORM_LICENSE_FILE,
    "Tracon.Sqlite": POLYFORM_LICENSE_FILE,
    "Tracon.Templates": MIT_LICENSE_FILE,
    "Tracon.Testing": POLYFORM_LICENSE_FILE,
    "Tracon.Testing.Contracts.Xunit": MIT_LICENSE_FILE,
    "Tracon.UI": POLYFORM_LICENSE_FILE,
    "Tracon.Voice": POLYFORM_LICENSE_FILE,
    "Tracon.Workflows": POLYFORM_LICENSE_FILE,
}


TARGET_FRAMEWORKS_PATTERN = re.compile(r"<TargetFrameworks>([^<]+)</TargetFrameworks>")

DEFAULT_TARGET_FRAMEWORKS = ("net8.0", "net9.0", "net10.0")


def _target_frameworks(root: pathlib.Path, project_id: str) -> tuple[str, ...]:
    """Most packages inherit net8.0;net9.0;net10.0 from src/Directory.Build.props;
    a project that pins a single framework (e.g. Tracon.Testing) overrides
    TargetFrameworks (plural) explicitly."""
    text = (root / "src" / project_id / f"{project_id}.csproj").read_text(encoding="utf-8")
    match = TARGET_FRAMEWORKS_PATTERN.search(text)
    if match and match.group(1).strip():
        return tuple(part.strip() for part in match.group(1).split(";") if part.strip())
    return DEFAULT_TARGET_FRAMEWORKS


def _names_own_version(project_id: str, file_name: str) -> bool:
    """True if `file_name` is `<project_id>.<version>.<ext>` for THIS project,
    not a different package that merely starts with the same prefix (e.g.
    "Tracon." also prefixes "Tracon.Core...."). A version always
    starts with a digit right after the id, which no package name does."""
    prefix = f"{project_id}."
    return file_name.startswith(prefix) and file_name[len(prefix) : len(prefix) + 1].isdigit()


def _sha256(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 20), b""):
            digest.update(chunk)
    return digest.hexdigest()


# 🚨 MEASURED (2026-09-03): a `.nupkg`/`.snupkg` is an OPC (Open Packaging
# Conventions) zip. NuGet.Packaging writes its core-properties part under a
# RANDOM (Guid.NewGuid()) file name every single `dotnet pack` invocation -
# "package/services/metadata/core-properties/<32 hex>.psmdcp" - and
# `_rels/.rels` embeds that same random name as a relationship target. Packing
# the SAME commit with the SAME MinVerVersionOverride twice in a row therefore
# produces two `.nupkg` files with byte-for-byte IDENTICAL `lib/`, `.nuspec`
# and every other entry, but a DIFFERENT raw SHA-256 - `dotnet pack` itself has
# no deterministic-output guarantee for the outer container, only for the
# compiler's own DLL/PDB output. Comparing raw file hashes here would make
# `kapi.py yayin` report a false "different artifact" conflict on every
# second run against an unchanged commit, which is precisely the false
# positive Faz 136's own manual case 8 exists to rule out.
_VOLATILE_OPC_ENTRY = re.compile(r"^_rels/\.rels$|^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$")


def _content_fingerprint(path: pathlib.Path) -> str:
    """A hash of a `.nupkg`/`.snupkg`'s MEANINGFUL content - every entry
    except the random-named OPC metadata NuGet regenerates on every pack.
    Two packs of the same commit are expected to have the SAME fingerprint;
    the raw file SHA-256 (`_sha256`, used for the published manifest) is not
    expected to match and is not what decides a promote/conflict."""
    digest = hashlib.sha256()
    with zipfile.ZipFile(path) as archive:
        names = sorted(name for name in archive.namelist() if not _VOLATILE_OPC_ENTRY.match(name))
        for name in names:
            digest.update(name.encode("utf-8"))
            digest.update(archive.read(name))
    return digest.hexdigest()


def _promote_staged_packages(staging_dir: pathlib.Path, release_dir: pathlib.Path, file_names: Iterable[str]) -> list[str]:
    """Moves each named file from `staging_dir` into `release_dir` - UNLESS an
    identically named file already lives there with a DIFFERENT content
    fingerprint (`_content_fingerprint`, NOT the raw file hash - see its
    docstring), in which case NOTHING is moved and the mismatched names are
    returned so the caller can report them and abort. `release_dir` is
    therefore never left half-updated: either every file promotes, or none
    does. A same-fingerprint match is a deterministic no-op - the existing
    file already carries what this run would have produced, so it is left in
    place as the one true copy."""
    file_names = list(file_names)
    conflicts = [
        name for name in file_names
        if (release_dir / name).exists() and _content_fingerprint(release_dir / name) != _content_fingerprint(staging_dir / name)
    ]
    if conflicts:
        return conflicts

    release_dir.mkdir(parents=True, exist_ok=True)
    for name in file_names:
        destination = release_dir / name
        if destination.exists():
            continue
        shutil.move(str(staging_dir / name), str(destination))
    return []


def _write_manifest(
    release_dir: pathlib.Path, *, version: str, commit: str, packages: list[dict[str, str | None]]
) -> pathlib.Path:
    manifest = {"version": version, "commit": commit, "dirty": False, "packages": packages}
    manifest_path = release_dir / "package-manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def _resolve_nupkg(release_dir: pathlib.Path, project_id: str, requested_version: str | None) -> pathlib.Path | None:
    if requested_version:
        candidate = release_dir / f"{project_id}.{requested_version}.nupkg"
        return candidate if candidate.exists() else None

    # No requested version: pick the package this project produced MOST
    # RECENTLY. The caller always packs into a fresh, run-unique staging
    # directory (release_rehearsal), so every survivor here is from the run
    # that just completed; "most recent" is only a tie-breaker between a
    # project's own multiple TFM-driven writes, not a defense against
    # cross-run staleness - there is no other run's output to be stale against.
    candidates = [path for path in release_dir.glob(f"{project_id}.*.nupkg") if _names_own_version(project_id, path.name)]
    return max(candidates, key=lambda path: path.stat().st_mtime) if candidates else None


def _nupkg_version(project_id: str, nupkg: pathlib.Path) -> str:
    return nupkg.name[len(project_id) + 1 : -len(".nupkg")]


def _read_nuspec(nupkg: pathlib.Path, project_id: str) -> str:
    with zipfile.ZipFile(nupkg) as archive:
        return archive.read(f"{project_id}.nuspec").decode("utf-8")


def _entry_names(nupkg: pathlib.Path) -> set[str]:
    with zipfile.ZipFile(nupkg) as archive:
        return set(archive.namelist())


def release_rehearsal(
    requested_version: str | None,
    *,
    root: pathlib.Path = ROOT,
    release_dir: pathlib.Path | None = None,
) -> int:
    release_dir = release_dir or PACKAGE_RELEASE_DIR
    project_ids = packable_project_ids(root)

    # Erken ret (136.3): çalışma ağacı denetlenir ÖNCE dakikalarca süren bir
    # `dotnet pack`e girilir. MSBuild kapısı (TraconValidateCleanWorkingTree)
    # zaten aynı sonucu verirdi; bu adım yalnız geri bildirimi öne çeker.
    # Koşulsuzdur - bir yayın provasının kanıt değeri kirli bir ağaçta yoktur,
    # burada TraconAllowDirtyPack karşılığı bir override YOKTUR (Faz 136,
    # Açık Soru 3). git bulunamazsa (kaynak tarball, git PATH'te yok) kapı
    # ATLANIR - `dotnet pack` kendi MSBuild kapısı üzerinden aynı denetimi
    # tekrar dener; burası ikinci savunma hattıdır, tek hat değil.
    status_lines = _git("status", "--porcelain")
    if status_lines is None:
        print("⚠️ git bulunamadı veya bu bir git deposu değil; çalışma ağacı temizliği burada denetlenemedi")
    elif status_lines:
        print("❌ Çalışma ağacı temiz değil ('git status --porcelain'):")
        for line in status_lines:
            print(f"  {line}")
        print("Bir yayın provasının kanıt değeri kirli bir ağaçta yoktur; commit veya stash sonrası tekrar deneyin.")
        return 1

    commit_lines = _git("rev-parse", "HEAD")
    commit = commit_lines[0] if commit_lines else "unknown"

    # Staging dizini koşum başına benzersizdir (Faz 136, Açık Soru 1: paralel
    # koşumlar desteklenmez, ama bu en azından ikisinin BİRBİRİNİN çıktısını
    # ezmesini önler). `artifacts/` .gitignore'dadır - staging burada yaşarsa
    # bir sonraki koşumun kendi "erken ret" denetimini kirletmez.
    staging_root = release_dir.parent / "staging"
    staging_root.mkdir(parents=True, exist_ok=True)
    staging_dir = pathlib.Path(tempfile.mkdtemp(prefix="run-", dir=staging_root))

    try:
        environment = os.environ.copy()
        environment["MSBUILDDISABLENODEREUSE"] = "1"
        if requested_version:
            environment["MinVerVersionOverride"] = requested_version

        pack_command = Command(("dotnet", "pack", str(PACKABLE_SOLUTION_FILTER), "-c", "Release", "-o", str(staging_dir)))
        print(f"$ {pack_command.display}" + (f"  (MinVerVersionOverride={requested_version})" if requested_version else ""), flush=True)
        pack_result = subprocess.run(list(pack_command.args), cwd=root, env=environment, check=False)
        if pack_result.returncode:
            print(f"❌ 'dotnet pack' çıkış {pack_result.returncode}")
            return pack_result.returncode

        return _finish_release_rehearsal(
            root=root,
            release_dir=release_dir,
            staging_dir=staging_dir,
            project_ids=project_ids,
            requested_version=requested_version,
            commit=commit,
        )
    finally:
        shutil.rmtree(staging_dir, ignore_errors=True)


def _finish_release_rehearsal(
    *,
    root: pathlib.Path,
    release_dir: pathlib.Path,
    staging_dir: pathlib.Path,
    project_ids: list[str],
    requested_version: str | None,
    commit: str,
) -> int:
    resolved: dict[str, pathlib.Path] = {}
    missing: list[str] = []
    for project_id in project_ids:
        nupkg = _resolve_nupkg(staging_dir, project_id, requested_version)
        if nupkg is None:
            missing.append(project_id)
        else:
            resolved[project_id] = nupkg

    if missing:
        print(f"❌ Paket üretilmedi: {', '.join(missing)}")
        return 1

    versions = {project_id: _nupkg_version(project_id, nupkg) for project_id, nupkg in resolved.items()}
    distinct_versions = sorted(set(versions.values()))
    if len(distinct_versions) != 1:
        print("❌ Paketler tek bir sürüm hattında değil:")
        for project_id in sorted(versions):
            print(f"  {project_id}: {versions[project_id]}")
        return 1

    resolved_version = distinct_versions[0]
    if requested_version and resolved_version != requested_version:
        print(f"❌ İstenen sürüm '{requested_version}' üretilmedi; üretilen: '{resolved_version}'")
        return 1
    if not RELEASE_VERSION_PATTERN.fullmatch(resolved_version):
        print(
            f"⚠️ Sürüm '1.0.0-preview.N' desenine uymuyor: '{resolved_version}' "
            "(etiketlenmemiş bir koşumda beklenir; --surum ile zorlanmadıysa bu bir hata değildir)"
        )
    else:
        # Yalnız GERÇEK bir sürüm şeklinde çözümlenen koşumda zorlanır - ya
        # `--surum` ile insan tarafından yerel bir provada, ya da gerçek bir
        # `v*` etiketiyle. Etiketlenmemiş her rutin CI koşumu yukarıdaki dalı
        # alır ve bu kapıyı hiç görmez (docs/arsiv/fazlar/123-YAYIN-KRITIK-YOLU.md, 123.3).
        sys.path.insert(0, str(ROOT / "scripts"))
        import changelog

        changelog_path = root / "CHANGELOG.md"
        if not changelog_path.exists():
            print("❌ CHANGELOG.md bulunamadı")
            return 1
        if not changelog.has_section(changelog_path.read_text(encoding="utf-8"), resolved_version):
            print(f"❌ CHANGELOG.md içinde '## [{resolved_version}]' bölümü yok veya boş")
            return 1

    # İki yönlü karşılaştırma: yalnız EKSİK paket değil, beklenmeyen (fazla) bir
    # paket de yakalanmalı - ör. bir test projesinin yanlışlıkla packable hâle
    # gelmesi. `resolved` yalnız `project_ids` üstünden dolduğu için kendi
    # başına bunu göremez; dizindeki `resolved_version`'a ait GERÇEK `.nupkg`
    # kümesi ayrıca taranır.
    produced_ids = {
        path.name[: -len(f".{resolved_version}.nupkg")]
        for path in staging_dir.glob(f"*.{resolved_version}.nupkg")
    }
    unexpected = sorted(produced_ids - set(project_ids))
    if unexpected:
        print(f"❌ Beklenmeyen paket üretildi: {', '.join(unexpected)}")
        return 1

    errors: list[str] = []
    for project_id in sorted(resolved):
        nupkg = resolved[project_id]
        entries = _entry_names(nupkg)
        nuspec = _read_nuspec(nupkg, project_id)
        profile = _package_profile(root, project_id)

        if "icon.png" not in entries:
            errors.append(f"{project_id}: icon.png eksik")
        if "README.md" not in entries:
            errors.append(f"{project_id}: README.md eksik")
        # Lisans üç yönden birden doğrulanır. Yalnız .nuspec'e bakmak yetmez:
        # bir paketin BEYAN ettiği lisans ile İÇİNDEKİ dosya ayrışabilir, ve
        # ayrıştığında tüketicinin eline geçen dosya kazanır.
        expected_license = PACKAGE_LICENSES.get(project_id)
        if expected_license is None:
            errors.append(f"{project_id}: PACKAGE_LICENSES tablosunda yok - lisansı bilinçli olarak seçilmeli")
        else:
            if f'<license type="file">{expected_license}</license>' not in nuspec:
                errors.append(f"{project_id}: .nuspec '<license type=\"file\">{expected_license}</license>' taşımıyor")
            # Ölçüldü (Faz 160): NuGet requireLicenseAcceptance'ı yalnız TRUE
            # iken yazar; false varsayılandır ve element hiç görünmez. Bu yüzden
            # MIT tarafı elementin YOKLUĞU ile doğrulanır, "false" metniyle değil.
            acceptance_declared = "<requireLicenseAcceptance>true</requireLicenseAcceptance>" in nuspec
            if expected_license == MIT_LICENSE_FILE and acceptance_declared:
                errors.append(f"{project_id}: MIT paketi requireLicenseAcceptance istememeli")
            if expected_license == POLYFORM_LICENSE_FILE and not acceptance_declared:
                errors.append(f"{project_id}: requireLicenseAcceptance 'true' olmalı")
            if expected_license not in entries:
                errors.append(f"{project_id}: {expected_license} paketin İÇİNDE yok")
            for other in LICENSE_FILES:
                if other != expected_license and other in entries:
                    errors.append(f"{project_id}: yanlış lisans dosyası da paketlenmiş: {other}")
        if not REPOSITORY_COMMIT_PATTERN.search(nuspec):
            errors.append(f"{project_id}: repository/commit metaverisi eksik")

        release_notes_match = RELEASE_NOTES_PATTERN.search(nuspec)
        if release_notes_match is None:
            errors.append(f"{project_id}: releaseNotes eksik")
        elif "$(Version)" in release_notes_match.group("url"):
            errors.append(f"{project_id}: releaseNotes ham $(Version) taşıyor - çözümlenmemiş")
        elif resolved_version not in release_notes_match.group("url"):
            errors.append(f"{project_id}: releaseNotes çözümlenen sürümü ('{resolved_version}') taşımıyor")

        snupkg_exists = nupkg.with_suffix(".snupkg").exists()
        if profile == "content":
            if snupkg_exists:
                errors.append(f"{project_id}: içerik paketi .snupkg TAŞIMAMALI")
        elif not snupkg_exists:
            errors.append(f"{project_id}: .snupkg eksik")

        if profile in ("library", "tool"):
            expected_xml_count = len(_target_frameworks(root, project_id)) if profile == "library" else 1
            xml_count = sum(1 for name in entries if name.endswith(f"/{project_id}.xml"))
            if xml_count != expected_xml_count:
                errors.append(f"{project_id}: {expected_xml_count} XML doküman dosyası bekleniyordu, {xml_count} bulundu")

        if project_id != K008_EXEMPT_PACKAGE:
            offenders = sorted({
                match.group("id")
                for match in PRERELEASE_DEPENDENCY_PATTERN.finditer(nuspec)
                if not match.group("id").startswith("Tracon")
            })
            if offenders:
                errors.append(f"{project_id}: K-008 sınırı ihlal edildi - ön sürüm bağımlılık: {', '.join(offenders)}")

    if errors:
        print("❌ Metaveri/K-008 sözleşmesi ihlal edildi:")
        for error in errors:
            print(f"  {error}")
        return 1

    # 136.3 - overwrite koruması ve manifest. Staging'de doğrulanmış paketler
    # ancak burada release_dir'e taşınır (promote); `_promote_staged_packages`
    # aynı isimde FARKLI bir SHA-256 bulursa HİÇBİR dosyayı taşımaz ve mevcut
    # release_dir olduğu gibi kalır (hepsi ya da hiçbiri).
    staged_file_names: dict[str, list[str]] = {}
    for project_id in sorted(resolved):
        nupkg = resolved[project_id]
        names = [nupkg.name]
        snupkg = nupkg.with_suffix(".snupkg")
        if snupkg.exists():
            names.append(snupkg.name)
        staged_file_names[project_id] = names

    all_file_names = [name for names in staged_file_names.values() for name in names]
    conflicts = _promote_staged_packages(staging_dir, release_dir, all_file_names)
    if conflicts:
        print("❌ Aynı kimlikte (id+sürüm) FARKLI içerikli bir artifact zaten var - mevcut dosya korundu:")
        for name in conflicts:
            print(f"  {name}")
        return 1

    packages_manifest: list[dict[str, str | None]] = []
    for project_id in sorted(resolved):
        names = staged_file_names[project_id]
        nupkg_path = release_dir / names[0]
        entry: dict[str, str | None] = {
            "id": project_id,
            "file": nupkg_path.name,
            "sha256": _sha256(nupkg_path),
            "symbolsFile": None,
            "symbolsSha256": None,
        }
        if len(names) > 1:
            snupkg_path = release_dir / names[1]
            entry["symbolsFile"] = snupkg_path.name
            entry["symbolsSha256"] = _sha256(snupkg_path)
        packages_manifest.append(entry)

    manifest_path = _write_manifest(release_dir, version=resolved_version, commit=commit, packages=packages_manifest)
    print(f"📄 {manifest_path.relative_to(root)} yazıldı ({len(packages_manifest)} paket)")

    print(f"✅ {len(resolved)} paket, sürüm '{resolved_version}':")
    for project_id in sorted(resolved):
        print(f"  {project_id}  {versions[project_id]}")

    npm_result = _npm_dry_run()
    if npm_result:
        return npm_result

    # Envanterdeki her extension sample'ı ve AOT smoke, tek exact-version bu
    # koşumun ürettiği paketlere karşı izole bir NUGET_PACKAGES cache'iyle
    # çalışır (Faz 103, §103.8; envanter kapısı Faz 123, §123.1). `dotnet
    # pack`/npm gibi ağa hiçbir şey yazmaz.
    sys.path.insert(0, str(ROOT / "scripts"))
    import release_extension_samples

    return release_extension_samples.verify(root, release_dir, resolved_version)


def _npm_dry_run() -> int:
    if shutil.which("npm") is None:
        print("⚠️ npm bulunamadı; 'npm publish --dry-run' ATLANDI")
        return 0

    print(f"$ (cd {NPM_CLIENT_PACKAGE_DIR.relative_to(ROOT)} && npm publish --dry-run)", flush=True)
    try:
        result = subprocess.run(
            ["npm", "publish", "--dry-run"],
            cwd=NPM_CLIENT_PACKAGE_DIR,
            check=False,
            timeout=180,
        )
    except (OSError, subprocess.TimeoutExpired) as exception:
        print(f"⚠️ 'npm publish --dry-run' çalıştırılamadı (ağ erişimi olmayabilir) - ATLANDI: {exception}")
        return 0

    if result.returncode:
        print(f"❌ 'npm publish --dry-run' çıkış {result.returncode}")
        return result.returncode

    print("✅ npm publish --dry-run")
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--komutlari-bas", action="store_true", help="komutları çalıştırmadan listele")
    subparsers = parser.add_subparsers(dest="stage")

    subparsers.add_parser("tarama", help="sync kopyası ve secret taraması")
    subparsers.add_parser("ic-dongu", help="build ve etkilenen test projeleri")
    closing = subparsers.add_parser("kapanis", help="tam doğrulama kapıları")
    closing.add_argument("--taban", required=True, help="faz öncesi commit SHA")
    closing.add_argument("--site-atla", action="store_true", help="docs-site kapısını atla")
    performans = subparsers.add_parser("performans", help="tahsis kapısı - üç sıcak yolu ölçer")
    performans.add_argument(
        "--guncelle", action="store_true",
        help="bench/baseline.json'ı bu koşumun sonucuyla değiştirir (karşılaştırma yapılmaz)")
    test = subparsers.add_parser("test", help="MTP filtresiyle tek test alt kümesi")
    test.add_argument("--sinif", nargs="+", required=True, help="sınıf desenleri")
    test.add_argument("--proje", default="Tracon.Core.UnitTests", help="test proje adı")
    yayin = subparsers.add_parser("yayin", help="yayın provası - ağa hiçbir şey yazmaz")
    yayin.add_argument(
        "--kuru", action="store_true", required=True,
        help="zorunlu: bu faz yalnız kuru koşumu destekler, canlı yayın yolu yok",
    )
    yayin.add_argument("--surum", help="zorlanacak sürüm (MinVerVersionOverride); verilmezse MinVer'in bugünkü değeri kullanılır")

    args = parser.parse_args(argv)
    if args.stage == "tarama":
        return scan()
    if args.stage == "ic-dongu":
        paths = changed_paths()
        if paths is None:
            print("❌ git çağrısı başarısız; etkilenen proje seçilemedi.")
            return 1
        return run_commands("ic-dongu", inner_loop_commands(paths), dry_run=args.komutlari_bas)
    if args.stage == "kapanis":
        # The performance gate is path-triggered against the WHOLE phase's
        # changes (--taban..working tree), not just what is uncommitted right
        # now - a phase that touched a hot path in an earlier commit and only
        # has unrelated uncommitted changes left must still measure it (116.4).
        # A failed git call runs it anyway rather than silently skipping.
        paths = changed_paths(args.taban)
        performance = paths is None or performance_gate_triggered(paths)
        return run_commands(
            "kapanis",
            closing_commands(args.taban, site=not args.site_atla, performance=performance),
            dry_run=args.komutlari_bas,
        )
    if args.stage == "performans":
        if args.komutlari_bas:
            print("$ dotnet run -c Release --project bench/Tracon.Benchmarks -- --filter * --exporters json")
            return 0
        return performance_gate(update=args.guncelle)
    if args.stage == "test":
        return run_commands("test", [test_command(args.proje, args.sinif)], dry_run=args.komutlari_bas)
    if args.stage == "yayin":
        if args.komutlari_bas:
            print("$ dotnet pack ...  (bkz. release_rehearsal)")
            return 0
        return release_rehearsal(args.surum)

    if args.komutlari_bas:
        return run_commands("kapanis", closing_commands("<taban>"), dry_run=True)
    parser.error("bir aşama belirtin: tarama, ic-dongu, kapanis, performans, test veya yayin")
    return 2


if __name__ == "__main__":
    sys.exit(main())
