#!/usr/bin/env python3
"""AgentPrism validation gates.

The repository has several validation boundaries. This module is the single
executable source for their commands so CI, skills, and local development do
not grow subtly different copies of the same gate.
"""
from __future__ import annotations

import argparse
import dataclasses
import fnmatch
import json
import os
import pathlib
import re
import shlex
import shutil
import subprocess
import sys
import time
import zipfile
from collections.abc import Callable, Iterable, Mapping, Sequence

ROOT = pathlib.Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "artifacts"
MEASUREMENTS = ARTIFACTS / "kapi-olcum.jsonl"
PACKAGE_RELEASE_DIR = ARTIFACTS / "package" / "release"
PACKABLE_SOLUTION_FILTER = ROOT / "AgentPrism.src.slnf"
NPM_CLIENT_PACKAGE_DIR = ROOT / "packages" / "agentprism-client"
RELEASE_VERSION_PATTERN = re.compile(r"^1\.0\.0-preview\.\d+$")
PRERELEASE_DEPENDENCY_PATTERN = re.compile(r'id="(?P<id>[^"]+)" version="[^"]*-[^"]*"')
REPOSITORY_COMMIT_PATTERN = re.compile(r'<repository[^>]+commit="[0-9a-f]{7,}"[^>]*/>')
K008_EXEMPT_PACKAGE = "AgentPrism.AspNetCore"
APPLIED_MIGRATION_MANIFEST = pathlib.PurePath("scripts", "applied-migrations.json")
GIT_COMMIT = re.compile(r"^[0-9a-f]{7,40}$")

SYNC_ROOTS = ("src", "tests", "samples", "docs", ".agents")
SCAN_EXCLUDED_DIRS = {
    ".git", "artifacts", "node_modules", "dist", ".astro", ".vite", "wwwroot",
    "obj", "bin", "TestResults", "manuel-test", "arsiv", "manuel-test-kosumu",
}
SYNC_NAME = re.compile(r".+ 2(?:\..+)?$")
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


def scan(root: pathlib.Path = ROOT) -> int:
    """Run the synchronization-copy, secret, and migration-integrity scans."""
    copies = find_sync_copies(root)
    secrets = find_secrets(root)
    migrations = migration_integrity_violations(root)
    if not copies and not secrets and not migrations:
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


def changed_paths() -> list[str] | None:
    """Include committed-range, working-tree, and untracked paths."""
    paths: set[str] = set()
    for arguments in (("diff", "--name-only", "HEAD"), ("status", "--porcelain")):
        result = _git(*arguments)
        if result is None:
            return None
        if arguments[0] == "diff":
            paths.update(result)
        else:
            paths.update(line[3:].strip('"') for line in result if line.startswith("?? "))
    return sorted(paths)


def frontend_changed(paths: Iterable[str]) -> bool:
    return any(path.startswith("src/AgentPrism.UI/") or path.startswith("packages/agentprism-client/") for path in paths)


TEST_PROJECTS: dict[str, tuple[str, ...]] = {
    "AgentPrism.Abstractions": ("AgentPrism.Core.UnitTests",),
    "AgentPrism.Core": ("AgentPrism.Core.UnitTests", "AgentPrism.AspNetCore.FunctionalTests"),
    "AgentPrism.Generators": ("AgentPrism.Generators.UnitTests",),
    "AgentPrism.PostgreSql": ("AgentPrism.PostgreSql.IntegrationTests",),
    "AgentPrism.SqlServer": ("AgentPrism.SqlServer.IntegrationTests",),
    "AgentPrism.Sqlite": ("AgentPrism.Sqlite.IntegrationTests",),
    "AgentPrism.OpenAI": ("AgentPrism.OpenAI.UnitTests", "AgentPrism.Generators.UnitTests"),
    "AgentPrism.Anthropic": ("AgentPrism.Anthropic.UnitTests",),
    "AgentPrism.Google": ("AgentPrism.Google.UnitTests",),
    "AgentPrism.Azure": ("AgentPrism.Azure.UnitTests",),
    "AgentPrism.Voice": ("AgentPrism.Voice.UnitTests",),
    "AgentPrism.Mcp": ("AgentPrism.Mcp.UnitTests",),
    "AgentPrism.Workflows": ("AgentPrism.Workflows.UnitTests",),
    "AgentPrism.AspNetCore": ("AgentPrism.AspNetCore.FunctionalTests",),
    "AgentPrism.UI": ("AgentPrism.Ui.E2ETests",),
    "AgentPrism.Testing": ("AgentPrism.Testing.UnitTests",),
    "AgentPrism.Client": ("AgentPrism.Client.UnitTests",),
    "AgentPrism.Cli": ("AgentPrism.Cli.FunctionalTests",),
}


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
            match = re.match(r"src/([^/]+)/", path)
            package = match.group(1).removesuffix(".csproj") if match else ""
            if package in TEST_PROJECTS:
                projects.update(TEST_PROJECTS[package])
            else:
                needs_full = True
        elif path.startswith("packages/") or path.startswith("scripts/"):
            projects.add("AgentPrism.Client.UnitTests" if path.startswith("packages/") else "AgentPrism.Core.UnitTests")
    return sorted(projects), needs_full


def _dotnet_test_project(project: str) -> Command:
    return Command(("dotnet", "test", f"tests/{project}/{project}.csproj", "-c", "Release", "--no-build"))


def inner_loop_commands(paths: list[str]) -> list[Command]:
    frontend_flag = () if frontend_changed(paths) else ("-p:AgentPrismFrontendEnabled=false",)
    commands = [Command(("dotnet", "build", "AgentPrism.slnx", "-c", "Release", *frontend_flag))]
    projects, needs_full = affected_test_projects(paths)
    if needs_full:
        print("⚠️ Etkilenen proje haritası eksik; test seçimi tam koşuma genişletildi.")
        return commands + [Command(("dotnet", "test", "AgentPrism.slnx", "-c", "Release", "--no-build"))]
    return commands + [_dotnet_test_project(project) for project in projects]


def closing_commands(base: str, *, site: bool = True) -> list[Command]:
    commands = [
        Command(("python3", "scripts/kapi.py", "tarama")),
        Command(("python3", "scripts/dokuman-bakim.py", "--denetle")),
        Command(("python3", "-m", "unittest", "discover", "-s", "scripts", "-p", "*_test.py")),
        Command(("node", "docs-site/scripts/build-agent-map.mjs", "--check")),
        Command(("python3", "scripts/denetim-paketi.py", "--taban", base)),
        Command(("dotnet", "build", "AgentPrism.slnx", "-c", "Release")),
        Command(("dotnet", "test", "AgentPrism.slnx", "-c", "Release", "--no-build")),
        Command(("dotnet", "pack", "AgentPrism.slnx", "-c", "Release", "--no-build")),
        Command(("dotnet", "format", "AgentPrism.slnx", "--verify-no-changes", "--no-restore")),
    ]
    if site:
        commands.append(Command(("npm", "run", "check"), ROOT / "docs-site"))
    return commands


def test_command(project: str, patterns: Sequence[str]) -> Command:
    executable = ROOT / "artifacts" / "bin" / project / "release" / project
    return Command((str(executable), "--filter-class", *patterns))


# -----------------------------------------------------------------------------
# `yayin` - release rehearsal (docs/97-SURUM-POLITIKASI-VE-YAYIN-PROVASI.md, 97.2)
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


TARGET_FRAMEWORKS_PATTERN = re.compile(r"<TargetFrameworks>([^<]+)</TargetFrameworks>")

DEFAULT_TARGET_FRAMEWORKS = ("net8.0", "net9.0", "net10.0")


def _target_frameworks(root: pathlib.Path, project_id: str) -> tuple[str, ...]:
    """Most packages inherit net8.0;net9.0;net10.0 from src/Directory.Build.props;
    a project that pins a single framework (e.g. AgentPrism.Testing) overrides
    TargetFrameworks (plural) explicitly."""
    text = (root / "src" / project_id / f"{project_id}.csproj").read_text(encoding="utf-8")
    match = TARGET_FRAMEWORKS_PATTERN.search(text)
    if match and match.group(1).strip():
        return tuple(part.strip() for part in match.group(1).split(";") if part.strip())
    return DEFAULT_TARGET_FRAMEWORKS


def _names_own_version(project_id: str, file_name: str) -> bool:
    """True if `file_name` is `<project_id>.<version>.<ext>` for THIS project,
    not a different package that merely starts with the same prefix (e.g.
    "AgentPrism." also prefixes "AgentPrism.Core...."). A version always
    starts with a digit right after the id, which no package name does."""
    prefix = f"{project_id}."
    return file_name.startswith(prefix) and file_name[len(prefix) : len(prefix) + 1].isdigit()


def _clean_stale_packages(release_dir: pathlib.Path, project_ids: Iterable[str]) -> None:
    if not release_dir.exists():
        return
    for project_id in project_ids:
        for path in release_dir.glob(f"{project_id}.*"):
            if path.suffix in (".nupkg", ".snupkg") and _names_own_version(project_id, path.name):
                path.unlink()


def _resolve_nupkg(release_dir: pathlib.Path, project_id: str, requested_version: str | None) -> pathlib.Path | None:
    if requested_version:
        candidate = release_dir / f"{project_id}.{requested_version}.nupkg"
        return candidate if candidate.exists() else None

    # No requested version: pick the package this project produced MOST
    # RECENTLY. _clean_stale_packages already removed every earlier artifact
    # of THIS project before packing, so any survivor here is from the run
    # that just completed; "most recent" is only a tie-breaker between a
    # project's own multiple TFM-driven writes, not a defense against
    # cross-run staleness.
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

    # 🚨 MEASURED: without this, a stale .nupkg from an EARLIER invocation (e.g.
    # a prior --surum run) can outlive an incremental `dotnet pack` that decides
    # a project's inputs are unchanged and skips re-creating its output. The
    # "most recently written" resolution below would then silently pick that
    # stale file over the one this run actually produced - two packages ending
    # up on a different version than the rest, exactly what the "single line"
    # assertion below exists to catch. Removing each tracked project's own
    # previous artifacts first forces a real rebuild for every one of them.
    _clean_stale_packages(release_dir, project_ids)

    environment = os.environ.copy()
    environment["MSBUILDDISABLENODEREUSE"] = "1"
    if requested_version:
        environment["MinVerVersionOverride"] = requested_version

    pack_command = Command(("dotnet", "pack", str(PACKABLE_SOLUTION_FILTER), "-c", "Release"))
    print(f"$ {pack_command.display}" + (f"  (MinVerVersionOverride={requested_version})" if requested_version else ""), flush=True)
    pack_result = subprocess.run(list(pack_command.args), cwd=root, env=environment, check=False)
    if pack_result.returncode:
        print(f"❌ 'dotnet pack' çıkış {pack_result.returncode}")
        return pack_result.returncode

    resolved: dict[str, pathlib.Path] = {}
    missing: list[str] = []
    for project_id in project_ids:
        nupkg = _resolve_nupkg(release_dir, project_id, requested_version)
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

    # İki yönlü karşılaştırma: yalnız EKSİK paket değil, beklenmeyen (fazla) bir
    # paket de yakalanmalı - ör. bir test projesinin yanlışlıkla packable hâle
    # gelmesi. `resolved` yalnız `project_ids` üstünden dolduğu için kendi
    # başına bunu göremez; dizindeki `resolved_version`'a ait GERÇEK `.nupkg`
    # kümesi ayrıca taranır.
    produced_ids = {
        path.name[: -len(f".{resolved_version}.nupkg")]
        for path in release_dir.glob(f"*.{resolved_version}.nupkg")
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
        if '<license type="expression">MIT</license>' not in nuspec:
            errors.append(f"{project_id}: MIT license expression eksik")
        if not REPOSITORY_COMMIT_PATTERN.search(nuspec):
            errors.append(f"{project_id}: repository/commit metaverisi eksik")

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
                if not match.group("id").startswith("AgentPrism")
            })
            if offenders:
                errors.append(f"{project_id}: K-008 sınırı ihlal edildi - ön sürüm bağımlılık: {', '.join(offenders)}")

    if errors:
        print("❌ Metaveri/K-008 sözleşmesi ihlal edildi:")
        for error in errors:
            print(f"  {error}")
        return 1

    print(f"✅ {len(resolved)} paket, sürüm '{resolved_version}':")
    for project_id in sorted(resolved):
        print(f"  {project_id}  {versions[project_id]}")

    return _npm_dry_run()


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
    test = subparsers.add_parser("test", help="MTP filtresiyle tek test alt kümesi")
    test.add_argument("--sinif", nargs="+", required=True, help="sınıf desenleri")
    test.add_argument("--proje", default="AgentPrism.Core.UnitTests", help="test proje adı")
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
        return run_commands(
            "kapanis",
            closing_commands(args.taban, site=not args.site_atla),
            dry_run=args.komutlari_bas,
        )
    if args.stage == "test":
        return run_commands("test", [test_command(args.proje, args.sinif)], dry_run=args.komutlari_bas)
    if args.stage == "yayin":
        if args.komutlari_bas:
            print("$ dotnet pack ...  (bkz. release_rehearsal)")
            return 0
        return release_rehearsal(args.surum)

    if args.komutlari_bas:
        return run_commands("kapanis", closing_commands("<taban>"), dry_run=True)
    parser.error("bir aşama belirtin: tarama, ic-dongu, kapanis, test veya yayin")
    return 2


if __name__ == "__main__":
    sys.exit(main())
