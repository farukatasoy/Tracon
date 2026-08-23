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
import subprocess
import sys
import time
from collections.abc import Callable, Iterable, Sequence

ROOT = pathlib.Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "artifacts"
MEASUREMENTS = ARTIFACTS / "kapi-olcum.jsonl"

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
    """Run the synchronization-copy and secret scans."""
    copies = find_sync_copies(root)
    secrets = find_secrets(root)
    if not copies and not secrets:
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
    return 1


def _git(*args: str) -> list[str] | None:
    try:
        result = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    except OSError:
        return None
    if result.returncode:
        return None
    return [line for line in result.stdout.splitlines() if line.strip()]


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

    if args.komutlari_bas:
        return run_commands("kapanis", closing_commands("<taban>"), dry_run=True)
    parser.error("bir aşama belirtin: tarama, ic-dongu, kapanis veya test")
    return 2


if __name__ == "__main__":
    sys.exit(main())
