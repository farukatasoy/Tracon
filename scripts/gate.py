#!/usr/bin/env python3
"""Tracon validation gates.

The repository has several validation boundaries. This module is the single
executable source for their commands so CI, skills, and local development do
not grow subtly different copies of the same gate.
"""
from __future__ import annotations

import argparse
import concurrent.futures
import dataclasses
import datetime
import fnmatch
import gzip
import hashlib
import json
import os
import pathlib
import platform
import queue
import re
import shlex
import shutil
import statistics
import subprocess
import sys
import tempfile
import threading
import time
import xml.etree.ElementTree as ElementTree
import zipfile
from collections.abc import Callable, Iterable, Mapping, Sequence
from typing import Any

ROOT = pathlib.Path(__file__).resolve().parent.parent
ARTIFACTS = ROOT / "artifacts"
MEASUREMENTS = ARTIFACTS / "gate-timings.jsonl"
PACKAGE_RELEASE_DIR = ARTIFACTS / "package" / "release"
# Output of `release --dry-run` (pack mode): one SEPARATE directory per version. `release/`
# is the feed of the development loop (`dotnet pack`, the close gate and the package tests
# write there); when the rehearsal promoted into it, every old version failed the rehearsal's
# sample gate, because no file is ever deleted. In a separate directory versions never mix.
PACKAGE_REHEARSAL_ROOT = ARTIFACTS / "package" / "rehearsal"
PACKABLE_SOLUTION_FILTER = ROOT / "Tracon.src.slnf"
PACKAGE_MANIFEST_NAME = "package-manifest.json"
PACKAGE_REPORT_DIR_NAME = "api-compat"
PACKAGE_FILE_SUFFIXES = (".nupkg", ".snupkg")
NPM_CLIENT_PACKAGE_DIR = ROOT / "packages" / "tracon-client"
RELEASE_VERSION_PATTERN = re.compile(r"^1\.0\.0-preview\.\d+$")
PRERELEASE_DEPENDENCY_PATTERN = re.compile(r'id="(?P<id>[^"]+)" version="(?P<range>[^"]*-[^"]*)"')
# `[v]` or `[v, v]` - the two ways NuGet spells an exact range.
EXACT_RANGE_PATTERN = re.compile(r"\[(?P<version>[^,\]\s]+)(?:\s*,\s*(?P=version))?\]")
REPOSITORY_COMMIT_PATTERN = re.compile(r'<repository[^>]+commit="(?P<commit>[0-9a-f]{7,})"[^>]*/>')
RELEASE_NOTES_PATTERN = re.compile(r"<releaseNotes>(?P<url>[^<]*)</releaseNotes>")
PRERELEASE_MAF_EXEMPT_PACKAGE = "Tracon.AspNetCore"
# The shipped contract package does not force xunit.v3 3.x on its consumers.
# The repository's own tests run on xunit.v3 4.x and every consumer in this repo (samples
# included) is on 4.x; no run measures a 3.x consumer. This gate pins the lower bound by
# reading the nuspec.
SHIPPED_DEPENDENCY_FLOORS = {
    "Tracon.Testing.Contracts.Xunit": {"xunit.v3.extensibility.core": "3.2.2", "xunit.v3.assert": "3.2.2"},
}
NUSPEC_DEPENDENCY_PATTERN = re.compile(r'<dependency id="(?P<id>[^"]+)" version="(?P<range>[^"]*)"')
# A package that carries third-party code INSIDE the package also carries that code's
# license notice. The value is the source file in the repo; the copy inside the package
# must be byte-for-byte equal to it (did packing take the file that was checked?).
THIRD_PARTY_NOTICE_NAME = "THIRD-PARTY-NOTICES.txt"
THIRD_PARTY_NOTICE_PACKAGES = {"Tracon.UI": pathlib.PurePath("src", "Tracon.UI", THIRD_PARTY_NOTICE_NAME)}
APPLIED_MIGRATION_DIRECTORIES = ("Migrations", "MigrationsKnowledge", "MigrationsViews")
# Two test projects at the same time. It used to be 1 (three workers produced fake
# timeouts for minutes). Once the fixed waits became condition waits it was measured on the
# same machine with the same code: 1 module took 863 and 879 s, 2 modules took
# 607 · 658 · 524 s, and all three runs had 0 red among 16,962 tests.
# ci.yml runs ONE module per test job with `--project` and the jobs run in parallel with each
# other; runner hardware was not measured. `--max-parallel-test-modules 1` is used only in the
# culture job's `--solution` run (the modules run in series). A close-gate red that passes in
# an isolated run is no reason to lower the number: that test either carries a resource or
# timing assumption or shows a product race, and that is what gets fixed.
#
# `dotnet test` runs in MTP mode (`test.runner` in global.json). In this mode the old
# `-maxcpucount:N` exits with code 5 WITHOUT RUNNING ANY TEST; the equivalent is
# `--max-parallel-test-modules N`. The number stays the same: a "module" is one test
# binary (in a multi-targeted project every TFM leg is a separate module).
#
# The closing test run keeps the same limit in two forms: a run of several light modules (the solution
# filter) gets it as `--max-parallel-test-modules`, and the heavy modules run in exactly this many
# lanes (`HEAVY_TEST_LANES`), one module per lane at a time. At most this many modules run at once.
TEST_MAX_CPU_COUNT = 2

# MTP 2 writes the TRX to `--results-directory`, not next to the test binary; the
# `per-module` layout gives `<project>/<tfm>_<arch>/*.trx`. The test steps of ci.yml and
# its upload-results step carry the SAME path (`gate_test.py` checks both together).
TEST_RESULTS_DIRECTORY = "artifacts/test-results"
TEST_DURATIONS_DIRECTORY = "artifacts/test-durations"
TEST_DURATIONS_KEEP = 20

# Absolute wall-time budget per module, in seconds: the span between `Times start` and `finish`
# in the TRX, container startup included. A budget is a yardstick, not a verdict: wall time measures the
# machine as much as the code (the same tests took 282 s on a quiet machine and 412 s on one that was
# swapping). A module over its budget is printed and recorded in the measurement file; the exit code
# stays with the tests. Every test module may carry a budget; the table lives in one place and it is
# this file. The time of a module without a budget is reported on every run too. The budget is the
# yardstick of the local gate (two modules at the same time, a developer machine); CI does not
# run this command and runner hardware was not measured.
#
# An entry is added by THIS rule: at the same code size (test count +-5%) there are at least
# THREE green full-module runs with a TRX wall time; budget = slowest run + 10%, rounded up to
# 10 s. A budget is never set from an estimate or from one run. No budget is set for a module
# whose slowest measurement is under 10 s: rounding up to 10 s would give a multiple of the
# measured time, which is no limit. The evidence for each entry stays here:
#   SqlServer 400: in the full gate with two modules at the same time and other modules
#     beside them (not an empty machine); slowest run 361 s. Target 240 s; the budget comes
#     down to the target.
#   TwoNode 540: same measurement; slowest run 489 s. Target 500 s.
#   Ui.E2ETests 40: five `stability` runs, 117 tests, slowest 32.3 s x 1.10 = 35.5 s -> 40 s
#     (the earlier 60 s came from the 47.9 s measurement of the CI matrix: not from this machine).
#   The next seven come from the same five `stability` runs (same test count; slowest run, TWO
#   runs under CPU pressure INCLUDED, so the budget is more generous than a normal run of the
#   gate): AspNetCore 359.7 s (4296 tests) -> 400 · PostgreSql 149.4 s (1805) -> 170 ·
#   Package 275.9 s (139) -> 310 · Core 42.1 s (7373; one run was red, slowest of the green
#   runs) -> 50 · Workflows 31.2 s (703) -> 40 · Generators 23.8 s (414) -> 30 ·
#   Sqlite 75.1 s (1880) -> 90. Not re-derived from these runs: SqlServer and TwoNode keep
#   their entries above (the slowest measurements from the pressured runs, 387.7 and 643.4 s,
#   would raise them and the table only goes down); Cli has no budget (10.2 s: rounding up to
#   20 s would be no limit) and neither do the modules under 10 s.
#
# 🚨 The number only goes DOWN. A budget is not raised because a module exceeded it: first the cause
# is measured (`artifacts/test-durations/` keeps a record per test). A run on a machine that was slow
# that day (format and build took at least 4 times as long as usual) is read as such, not budgeted for.
# Time VARIANCE is not checked here: the `stability` command reports it.
TEST_MODULE_TIME_LIMITS_SECONDS: dict[str, int] = {
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

# A root that does not exist is skipped, so the public tree (where `docs` holds two
# contract directories) and the development repository run the same list.
SYNC_ROOTS = ("src", "tests", "samples", "docs")
SCAN_EXCLUDED_DIRS = {
    ".git", "artifacts", "node_modules", "dist", ".astro", ".vite", "wwwroot",
    "obj", "bin", "TestResults",
}
SYNC_NAME = re.compile(r".+ 2(?:\..+)?$")

# A markdown reference into the `docs/` tree, written OUTSIDE it. The negative lookbehind
# keeps `docs-site/src/content/docs/...` out: that is a SITE path, not a path in
# the `docs/` tree, and without the guard this gate produces dozens of false
# positives and gets switched off.
DOC_REFERENCE = re.compile(r"(?<![\w/-])docs" r"/[A-Za-z0-9._/\-]+\.md")
DOC_REFERENCE_ROOTS = ("src", "tests", "samples", "bench", "scripts", "docs-site", ".github")
# Metavariable filenames: an illustrative path in a template or in a comment.
# These never resolve and are not a defect.
DOC_REFERENCE_PLACEHOLDER = re.compile(r"(^|[-/])(NN|X|Y)([-.]|$)")
# A migration that already ran against a customer database is BYTE-FROZEN:
# the publishing gate rejects any change to it, a comment included. A stale
# reference inside such a file therefore cannot be repaired — measured, not
# assumed. The gate must not demand the impossible, so those directories are
# exempt; the comment stays true of the moment the migration shipped.
DOC_REFERENCE_FROZEN_DIR = re.compile(r"(^|[\\/])Migrations[A-Za-z]*[\\/]")
# 🚨 Two layers (2026-09-19). While a single pattern ran over a single scope, the gate was
# green and a REAL `ApiKeyGenerator` output sat in a record of a past run.
# There were two independent gaps and either one alone was enough: the pattern did not know
# the product's OWN key format (`ap_*`), and the scope never walked the trees that hold
# records of past runs - yet those are the only place where a generated credential
# gets pasted, because they carry real run output.
#
# Opening the scope to every pattern is NOT the fix: measured, 48 of 51 matches
# are localhost docker passwords, and flagging all of them would produce what the
# SYNTHETIC_MARKER comment below forbids. The difference is the SHAPE: a credential shape
# is never legitimate in a document, while a local setup phrase is the point of a run record.
#
# 🚨 This comment cannot spell the phrase out - the file goes through the gate's OWN scan and
# an example written in plain text turns the gate red on its own source. `gate_test.py`
# writes its fixture in pieces for the same reason (`"Pass" + "word="`). Measured, and it happened.
CREDENTIAL_SHAPE_PATTERN = re.compile(
    r"sk-[a-z]+-[A-Za-z0-9_-]{24,}"
    r"|AVNS_[A-Za-z0-9]{12,}"
    # `ApiKeyGenerator`: `ap_` + tenant suffix (≤12) + `_` + base64url(32 bytes) = 43 characters.
    # The length is given EXACTLY: writing `{43,}` catches snake_case English text such as
    # `ap_on_total_source_code_size_for_...` - measured, 70+ false positives.
    r"|\bap_[a-z0-9]{1,12}_[A-Za-z0-9_-]{43}\b"
    r"|\bghp_[A-Za-z0-9]{36}\b"
    r"|\bAKIA[0-9A-Z]{16}\b"
    r"|\bAIza[A-Za-z0-9_-]{35}\b"
    r"|\bwhsec_[A-Za-z0-9]{24,}\b"
)
# The local setup phrase. It is searched for only in the code tree; in the records of past
# runs a docker password is no defect, it is a reproduction instruction.
LOCAL_CREDENTIAL_PATTERN = re.compile(r"(?:Password|pwd)=[^ \";']{6,}")
# Validating a secret scanner needs a fake credential.
# The exception is not HIDDEN, it is MARKED: a line that carries this marker is skipped and
# the number of skipped lines is reported.
SYNTHETIC_MARKER = "SYNTHETIC-CREDENTIAL"


@dataclasses.dataclass(frozen=True)
class Command:
    """A command executed by a gate."""

    args: tuple[str, ...]
    cwd: pathlib.Path = ROOT
    group: str | None = None
    """Commands that carry the same group name and follow each other form one block that runs concurrently.

    The group does not change what a command is: `display` and the measurement row stay the same."""
    lane: str | None = None
    """Inside a group, commands of one lane run one after another; every lane runs beside the others.

    A command without a lane is a lane of its own."""

    @property
    def display(self) -> str:
        command = shlex.join(self.args)
        if self.cwd != ROOT:
            command = f"(cd {shlex.quote(str(self.cwd.relative_to(ROOT)))} && {command})"
        return command


Runner = Callable[..., subprocess.CompletedProcess[str]]


def _no_commands(base: str) -> tuple[Command, ...]:
    return ()


def _generic_tag_hint(version: str) -> str:
    return f"Push the tag v{version} on the commit its packages name (the `commit` of their nuspec)."


@dataclasses.dataclass(frozen=True)
class ScanCheck:
    """One more finding class for `scan`: a heading and the function that returns its violations.

    The function gets the repository root and returns the violations; an empty result is clean. It must
    fail red, not skip: a check that cannot run returns a violation that says so.
    """

    heading: str
    violations: Callable[[pathlib.Path], Sequence[str]]


@dataclasses.dataclass(frozen=True)
class Extension:
    """Steps a wrapper adds around the public gates.

    The public tree runs every gate in this file with none. A repository that holds
    more than the public tree (extra scan roots, extra checks) passes its own
    extension to `main`, so this file never has to name those steps.
    """

    program: str = "scripts/gate.py"
    """The entry point the closing run calls for `scan`, so the wrapper scans its own roots."""
    generation: tuple[Command, ...] = ()
    """Generators that run before the public ones."""
    after_scan: Callable[[str], Sequence[Command]] = _no_commands
    """Checks that run right after `scan`; the argument is the `close --base` commit."""
    scan_checks: tuple[ScanCheck, ...] = ()
    """More finding classes for `scan` itself, so they run before a commit and before `after_scan`."""
    sync_roots: tuple[str, ...] = ()
    doc_reference_roots: tuple[str, ...] = ()
    ci_remote: str = "origin"
    """The git remote whose CI run `remote-state` reads. A repository that mirrors its code elsewhere names the mirror."""
    tag_hint: Callable[[str], str] = _generic_tag_hint
    """What `remote-state` tells the maintainer to do about a release that is on nuget.org but has no tag; the argument is the version."""
    record_dirs: tuple[str, ...] = ()
    """Directory names that hold records of past runs. The credential-shape scan walks them, but the local
    setup phrase (a database password in a reproduction recipe) is legitimate there and is not searched for."""


PUBLIC = Extension()


def _record_measurement(
    stage: str,
    command: Command,
    started: float,
    exit_code: int,
    measurement_path: pathlib.Path,
    *,
    finished: float | None = None,
) -> None:
    """Append one row. `finished` is the monotonic end of a command that ran on another thread."""
    measurement_path.parent.mkdir(parents=True, exist_ok=True)
    row = {
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "stage": stage,
        "command": command.display,
        "duration_seconds": round((time.monotonic() if finished is None else finished) - started, 3),
        "exit_code": exit_code,
    }
    with measurement_path.open("a", encoding="utf-8") as stream:
        stream.write(json.dumps(row, ensure_ascii=False) + "\n")


@dataclasses.dataclass(frozen=True)
class _Outcome:
    """What one executed command left behind. The main thread reports it; a worker never prints."""

    command: Command
    exit_code: int
    started: float
    finished: float
    output: str = ""
    notice: str = ""

    @property
    def seconds(self) -> float:
        return self.finished - self.started


INTERNAL_ERROR_EXIT_CODE = 70  # the runner itself failed on a worker thread (not a child's exit code)


def _execute(command: Command, runner: Runner, environment: Mapping[str, str], *, capture: bool) -> _Outcome:
    """Run one command. With `capture` its output is held back (stderr merged) instead of reaching the terminal."""
    options: dict[str, Any] = {"cwd": str(command.cwd), "env": environment, "check": False}
    if capture:
        options.update(
            stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding="utf-8", errors="replace")
    started = time.monotonic()
    output = ""
    notice = ""
    try:
        result = runner(list(command.args), **options)
        exit_code = int(result.returncode)
        if capture and isinstance(result.stdout, str):
            output = result.stdout
    except FileNotFoundError as exception:
        notice = f"❌ Command not found: {exception.filename}"
        exit_code = 127
    except OSError as exception:
        notice = f"❌ Command could not run: {exception}"
        exit_code = 126
    return _Outcome(command, exit_code, started, time.monotonic(), output, notice)


def _report(stage: str, outcome: _Outcome, measurement_path: pathlib.Path) -> int:
    """Print what a command left, write its measurement row, and return its exit code. Main thread only."""
    if outcome.output:
        print(outcome.output, end="" if outcome.output.endswith("\n") else "\n", flush=True)
    if outcome.notice:
        print(outcome.notice)
    _record_measurement(
        stage, outcome.command, outcome.started, outcome.exit_code, measurement_path, finished=outcome.finished)
    if outcome.exit_code:
        print(f"❌ Exit {outcome.exit_code} ({outcome.seconds:.2f} s): {outcome.command.display}", flush=True)
    else:
        print(f"✅ {outcome.seconds:.2f} s", flush=True)
    return outcome.exit_code


def _blocks(commands: Sequence[Command]) -> list[list[Command]]:
    """Split a command list into blocks: neighbours that share a group name form one block, every other command is alone."""
    blocks: list[list[Command]] = []
    for command in commands:
        if command.group is not None and blocks and blocks[-1][0].group == command.group:
            blocks[-1].append(command)
        else:
            blocks.append([command])
    return blocks


def _lanes(block: Sequence[Command]) -> list[list[int]]:
    """Indexes of a block grouped by lane, in order of first appearance. A command without a lane is its own lane."""
    lanes: dict[tuple[str, object], list[int]] = {}
    for index, command in enumerate(block):
        key = ("lane", command.lane) if command.lane is not None else ("alone", index)
        lanes.setdefault(key, []).append(index)
    return list(lanes.values())


def _is_parallel(block: Sequence[Command]) -> bool:
    return block[0].group is not None and len(_lanes(block)) > 1


def _block_reports_trx(block: Sequence[Command]) -> bool:
    return any(reports_trx(command) for command in block)


def _group_heading(block: Sequence[Command]) -> str:
    return f"parallel group «{block[0].group}»: {len(_lanes(block))} lanes, {len(block)} commands"


def _print_plan(blocks: Sequence[Sequence[Command]]) -> None:
    """`--print-commands`: every command as `$ ...`; group and lane markers are `#` lines so a parser can skip them."""
    for block in blocks:
        parallel = _is_parallel(block)
        if parallel:
            print(f"# {_group_heading(block)}")
        lane = None
        for command in block:
            if parallel and command.lane is not None and command.lane != lane:
                lane = command.lane
                print(f"# lane {lane}")
            print(f"$ {command.display}")
        if parallel:
            print(f"# group end «{block[0].group}»")


GROUP_HEARTBEAT_SECONDS = 60.0
"""A running group says so this often: its output is held back, and a quiet terminal looks like a hang."""


def _label(command: Command) -> str:
    """A short name for a command in progress lines: the test target of a `dotnet test`, else the command, cut."""
    if command.args[:2] == ("dotnet", "test"):
        return pathlib.PurePath(_test_target(command.args)).stem
    text = command.display
    return text if len(text) <= 80 else text[:77] + "..."


def _run_sequential(
    stage: str, commands: Sequence[Command], runner: Runner, environment: Mapping[str, str],
    measurement_path: pathlib.Path,
) -> int:
    for command in commands:
        print(f"$ {command.display}", flush=True)
        exit_code = _report(stage, _execute(command, runner, environment, capture=False), measurement_path)
        if exit_code:
            return exit_code
    return 0


def _run_group(
    stage: str, block: Sequence[Command], runner: Runner, environment: Mapping[str, str],
    measurement_path: pathlib.Path,
) -> int:
    """Run a block's lanes at the same time and report it as if it had run in list order.

    - Output is held back per command and printed in list order, as soon as every command before it was printed.
    - Measurement rows are written here, on the calling thread, in list order.
    - The first non-zero exit code in LIST order is the result, whatever finished first.
    - The first command of every lane always starts, so the independent checks of a group all report. Once one
      command is red, a lane starts no FURTHER command, but a command that is already running is awaited: no
      child process is left behind.
    """
    lanes = _lanes(block)
    outcomes: list[_Outcome | None] = [None] * len(block)
    running: dict[int, float] = {}
    finished: queue.Queue[int] = queue.Queue()
    red = threading.Event()

    def work(lane: Sequence[int]) -> None:
        for position, index in enumerate(lane):
            if position == 0 or not red.is_set():
                running[index] = time.monotonic()
                try:
                    outcome = _execute(block[index], runner, environment, capture=True)
                except Exception as exception:  # a defect here must not leave the calling thread waiting
                    now = time.monotonic()
                    outcome = _Outcome(
                        block[index], INTERNAL_ERROR_EXIT_CODE, now, now,
                        notice=f"❌ The command runner crashed: {exception!r}")
                outcomes[index] = outcome
                running.pop(index, None)
                if outcome.exit_code:
                    red.set()
            finished.put(index)

    print(f"# {_group_heading(block)}; output prints in list order", flush=True)
    exit_code = 0
    group_started = time.monotonic()
    with concurrent.futures.ThreadPoolExecutor(max_workers=len(lanes)) as pool:
        for lane in lanes:
            pool.submit(work, lane)
        done: set[int] = set()
        printed = 0
        while printed < len(block):
            try:
                index = finished.get(timeout=GROUP_HEARTBEAT_SECONDS)
            except queue.Empty:
                names = ", ".join(_label(block[active]) for active in sorted(running))
                print(f"   … still running ({time.monotonic() - group_started:.0f} s): {names}", flush=True)
                continue
            done.add(index)
            early = outcomes[index]
            if index != printed and early is not None:
                print(f"   … finished, its output waits for its turn: {_label(early.command)}"
                      f" ({early.seconds:.1f} s, exit {early.exit_code})", flush=True)
            while printed in done:
                outcome = outcomes[printed]
                if outcome is None:
                    print(f"   ⏭ skipped (another command in the group is red): {block[printed].display}")
                else:
                    print(f"$ {outcome.command.display}")
                    result = _report(stage, outcome, measurement_path)
                    exit_code = exit_code or result
                printed += 1
    return exit_code


def _run_block(
    stage: str, block: Sequence[Command], runner: Runner, environment: Mapping[str, str],
    measurement_path: pathlib.Path,
) -> int:
    if _is_parallel(block):
        return _run_group(stage, block, runner, environment, measurement_path)
    return _run_sequential(stage, block, runner, environment, measurement_path)


TRX_CHECK = Command(("gate.py", "trx-check"))
"""Stands for the checks of a whole test run in the measurement file; a row exists only when they fail."""


def _run_test_stages(
    stage: str, blocks: Sequence[Sequence[Command]], runner: Runner, environment: Mapping[str, str],
    measurement_path: pathlib.Path,
) -> int:
    """Run test blocks one after another as ONE test run.

    The first red block ends the run, so a later and slower block never starts (fail fast). The checks over the
    TRX files then run once, over everything written since the first block began: the executed-test count, the
    module time report (it never changes the exit code), the duration record (one file per run) and, after a red
    run, the isolated rerun.
    """
    since = time.time()
    exit_code = 0
    for block in blocks:
        exit_code = _run_block(stage, block, runner, environment, measurement_path)
        if exit_code:
            break
    if not exit_code:
        check_started = time.monotonic()
        exit_code = require_executed_tests(since)
        if exit_code:
            print(f"❌ Exit {exit_code}: TRX checks of the test run")
            _record_measurement(stage, TRX_CHECK, check_started, exit_code, measurement_path)
        else:
            report_module_times(since, stage=stage, measurement_path=measurement_path)
    record_test_durations(since)
    if exit_code:
        isolate_failed_tests(failed_tests_from_trx(since), runner=runner)
    return exit_code


def run_commands(
    stage: str,
    commands: Sequence[Command],
    *,
    runner: Runner = subprocess.run,
    measurement_path: pathlib.Path = MEASUREMENTS,
    dry_run: bool = False,
) -> int:
    """Run commands in order and stop at the first non-zero exit code.

    Neighbours that share a `Command.group` run concurrently (see `_run_group`). Neighbouring blocks that ask for
    TRX reports are one test run: their checks run once, after the last of them.
    """
    blocks = _blocks(commands)
    if dry_run:
        _print_plan(blocks)
        return 0

    environment = os.environ.copy()
    environment["MSBUILDDISABLENODEREUSE"] = "1"

    position = 0
    while position < len(blocks):
        if _block_reports_trx(blocks[position]):
            end = position
            while end < len(blocks) and _block_reports_trx(blocks[end]):
                end += 1
            exit_code = _run_test_stages(stage, blocks[position:end], runner, environment, measurement_path)
            position = end
        else:
            exit_code = _run_block(stage, blocks[position], runner, environment, measurement_path)
            position += 1
        if exit_code:
            return exit_code
    return 0


def reports_trx(command: Command) -> bool:
    """True for a `dotnet test` that was asked for TRX reports (the full-solution run)."""
    return command.args[:2] == ("dotnet", "test") and "--report-trx" in command.args


ZERO_TESTS_EXIT_CODE = 8  # MTP's own "zero tests" exit code


def require_executed_tests(since: float, root: pathlib.Path = ROOT) -> int:
    """0 if the run left TRX reports with tests in them, else `ZERO_TESTS_EXIT_CODE`.

    Exit code 0 does not mean "tests ran". If the run wrote no TRX at all
    (the path changed, a flag was swallowed) or the TRX files are empty, the gate returns
    RED, not green; it prints the detail so that "green" is read together with the count."""
    tests, modules = executed_tests_from_trx(since, root)
    if tests == 0:
        print(f"❌ The test run exited with code 0, but no executed test exists under {TEST_RESULTS_DIRECTORY}"
              f" ({modules} TRX). The gate does not count as green: a flag may have been swallowed or the TRX path may have changed.")
        return ZERO_TESTS_EXIT_CODE
    print(f"   {tests} tests, {modules} modules (TRX: {TEST_RESULTS_DIRECTORY})")
    return 0


def _trx_instant(value: str | None) -> float | None:
    """TRX timestamp (`2026-11-09T11:03:40.357499Z`) -> epoch seconds; `None` if it cannot be parsed.

    .NET can write up to seven fraction digits; `datetime` reads six, the rest is dropped."""
    match = re.fullmatch(r"(\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d)(?:\.(\d+))?(Z|[+-]\d\d:\d\d)?", value or "")
    if match is None:
        return None
    stamp, fraction, zone = match.groups()
    text = stamp + "." + (fraction or "0")[:6].ljust(6, "0") + ("+00:00" if zone in (None, "Z") else zone)
    try:
        return datetime.datetime.fromisoformat(text).timestamp()
    except ValueError:
        return None


def trx_wall_seconds(tree: Any) -> float | None:
    """Wall time of one TRX: the span between `Times start` and `finish`, in seconds. `None` if unreadable."""
    times = tree.getroot().find(TRX_NS + "Times")
    if times is None:
        return None
    start, finish = _trx_instant(times.get("start")), _trx_instant(times.get("finish"))
    if start is None or finish is None or finish < start:
        return None
    return finish - start


def _record_module_time(
    stage: str, label: str, seconds: float, limit: int, measurement_path: pathlib.Path,
) -> None:
    """Append the row of one module that ran over its budget: module, wall time, budget.

    The row has the shape of every other measurement row plus `limit_seconds`. A file that cannot be written
    warns; the report is not a reason for the gate to fail."""
    row = {
        "utc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "stage": stage,
        "command": f"gate.py module-time {label}",
        "duration_seconds": round(seconds, 3),
        "exit_code": 0,
        "limit_seconds": limit,
    }
    try:
        measurement_path.parent.mkdir(parents=True, exist_ok=True)
        with measurement_path.open("a", encoding="utf-8") as stream:
            stream.write(json.dumps(row, ensure_ascii=False) + "\n")
    except OSError as exception:
        print(f"   ⚠️ The over-budget row of {label} could not be written to {measurement_path}: {exception}")


def report_module_times(
    since: float,
    root: pathlib.Path = ROOT,
    limits: dict[str, int] | None = None,
    *,
    stage: str = "test",
    measurement_path: pathlib.Path = MEASUREMENTS,
) -> list[str]:
    """Print the wall time of every module; return the modules over their budget (`label 526 s > 500 s`).

    This is a report: it never changes an exit code. The wall time of every module is printed on every run, so
    getting close to a budget shows before it is passed. A module over its budget gets a `⏱️` line and one row
    in the measurement file. A module without a budget is only reported (`no limit`). A TRX whose time cannot
    be read warns. Time variance is not checked here; `stability` reports it."""
    limits = TEST_MODULE_TIME_LIMITS_SECONDS if limits is None else limits
    results = root / TEST_RESULTS_DIRECTORY
    measured: list[tuple[float, str, int | None]] = []
    for project, trx, tree in _recent_trx_reports(since, root):
        label = trx.parent.relative_to(results).as_posix()
        seconds = trx_wall_seconds(tree)
        if seconds is None:
            print(f"   ⚠️ {label}: the TRX duration could not be read, the time budget was not checked")
            continue
        measured.append((seconds, label, limits.get(project)))
    if measured:
        print("   ⏱️  Module times (TRX wall time; a module without a budget is only reported):")
    exceeded: list[str] = []
    for seconds, label, limit in sorted(measured, key=lambda row: (-row[0], row[1])):
        if limit is None:
            print(f"      {label}: {seconds:.0f} s · no limit")
            continue
        headroom = limit - seconds
        remaining = f"left {headroom:.0f} s ({100 * headroom / limit:.0f}%)" if headroom >= 0 else f"EXCEEDED by {-headroom:.0f} s"
        print(f"      {label}: {seconds:.0f} s · limit {limit} s · {remaining}")
        if seconds > limit:
            exceeded.append(f"{label} {seconds:.0f} s > {limit} s")
            _record_module_time(stage, label, seconds, limit, measurement_path)
    if exceeded:
        print(f"⏱️ Over the time budget (a report; the exit code stays with the tests): {'; '.join(exceeded)}.")
        print("   → Read it before you go on: measure which test got slower"
              f" ({TEST_DURATIONS_DIRECTORY}/, time per test). A machine that was slow that day"
              " (format and build took at least 4 times their normal time) explains it. Do not raise"
              " a budget to make the line go away.")
    return exceeded


def _walk_files(root: pathlib.Path, *, excluded_dirs: set[str] | None = None) -> Iterable[pathlib.Path]:
    excluded = SCAN_EXCLUDED_DIRS if excluded_dirs is None else excluded_dirs
    if not root.exists():
        return
    for directory, directories, files in os.walk(root):
        directories[:] = [name for name in directories if name not in excluded]
        directory_path = pathlib.Path(directory)
        for name in files:
            yield directory_path / name


def find_sync_copies(
    root: pathlib.Path = ROOT, roots: Sequence[str] = SYNC_ROOTS,
) -> list[pathlib.Path]:
    """Return file and directory names produced by cloud sync copy suffixes."""
    found: list[pathlib.Path] = []
    for relative_root in roots:
        base = root / relative_root
        if not base.exists():
            continue
        for directory, directories, files in os.walk(base):
            directories[:] = [name for name in directories if name not in {"node_modules", "obj", "bin"}]
            for name in (*directories, *files):
                if SYNC_NAME.fullmatch(name):
                    found.append(pathlib.Path(directory) / name)
    return sorted(found)


def find_secrets(root: pathlib.Path = ROOT, extension: Extension | None = None) -> tuple[list[str], int]:
    """Return matching path/line records and how many marked fixtures were skipped.

    🚨 The only way to test a secret SCANNER is to show it something shaped like a
    credential; the capacity harness deliberately uses a SYNTHETIC canary both in
    its redaction tests and in every run it makes (it connects to nothing). Those
    lines would turn this gate red.

    The fix is to MARK, not to hide: the line itself must carry SYNTHETIC_MARKER, so
    the exception stays visible in code review. The number of skipped lines is
    reported too - an exception list that grows silently is the same as having no gate.

    The scan has two layers (see CREDENTIAL_SHAPE_PATTERN). The shape scan runs
    everywhere, the trees of past-run records included; the local setup phrase is
    searched for only in the code tree."""
    found: list[str] = []
    skipped = 0
    # Records of past runs (the extension names them) carry the local setup phrase on purpose; only the
    # credential shape is searched for there - a real key that was missed once sat in exactly such a tree.
    record_dirs = frozenset((extension or PUBLIC).record_dirs)
    for path in _walk_files(root, excluded_dirs=SCAN_EXCLUDED_DIRS):
        try:
            lines = path.read_text(encoding="utf-8").splitlines()
        except (OSError, UnicodeDecodeError):
            continue
        relative = path.relative_to(root)
        patterns = [CREDENTIAL_SHAPE_PATTERN]
        if not record_dirs.intersection(relative.parts):
            patterns.append(LOCAL_CREDENTIAL_PATTERN)
        for line_number, line in enumerate(lines, 1):
            if not any(pattern.search(line) for pattern in patterns):
                continue
            if SYNTHETIC_MARKER in line:
                skipped += 1
                continue
            found.append(f"{relative}:{line_number}:{line}")
    return found, skipped


def find_stale_doc_references(
    root: pathlib.Path = ROOT, roots: Sequence[str] = DOC_REFERENCE_ROOTS,
) -> list[str]:
    """Return `path:line:reference` records for `docs/` targets that do not exist.

    A document under `docs/` is deleted or moved when its work is done, and the tooling
    that does it rewrites the references INSIDE `docs/`. Every reference OUTSIDE `docs/`
    — a migration comment, an analyzer release note that SHIPS in the package, a CI
    workflow — could be left pointing at a path that no longer exists.
    """
    found: list[str] = []
    for relative_root in roots:
        base = root / relative_root
        if not base.exists():
            continue
        for path in _walk_files(base, excluded_dirs=SCAN_EXCLUDED_DIRS):
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


PUBLIC_SURFACE_AUDIT = pathlib.Path("scripts") / "public-surface-inventory.py"


def public_surface_violations(root: pathlib.Path = ROOT) -> list[str]:
    """Ratchet: public types with no external evidence, and stale justifications.

    A public type must be seen by a consumer (site page, sample, template, README),
    be a seam, or carry a written reason in
    `scripts/public-surface-reasons.tsv`. Without this scan the audit ran only when
    someone remembered it, and two unproven exception types slipped in twice.
    """
    script = root / PUBLIC_SURFACE_AUDIT
    if not script.is_file():
        return []
    # The audit's messages contain non-ASCII characters (`—`, `→`); pin UTF-8 on both ends
    # so a cp1252 console (Windows) cannot turn the audit itself into a crash.
    run = dict(cwd=root, capture_output=True, text=True, encoding="utf-8", check=False,
               env={**os.environ, "PYTHONUTF8": "1"})
    result = subprocess.run([sys.executable, str(script), "--check"], **run)
    if result.returncode == 0:
        return []
    found = [line for line in result.stderr.splitlines() if line.strip()]
    listing = subprocess.run([sys.executable, str(script), "--list", "unproven"], **run)
    for line in listing.stdout.splitlines():
        columns = line.split("\t")
        if len(columns) >= 2:
            found.append(f"{columns[1]}: public type without evidence ({columns[0]})")
    return found


CONTEXT7_CONFIG = pathlib.Path("context7.json")


def context7_version_drift(root: pathlib.Path = ROOT) -> list[str]:
    """The first `previousVersions` tag of `context7.json` must be the latest release.

    Context7 builds the versioned library ID from that tag. The release cut dates a
    CHANGELOG.md section; if the same commit leaves the tag behind, coding agents read
    an older release than the one consumers install. No `context7.json`, no check.
    """
    config_path = root / CONTEXT7_CONFIG
    if not config_path.is_file():
        return []
    try:
        config = json.loads(config_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exception:
        return [f"{CONTEXT7_CONFIG} could not be read: {exception}"]

    versions = config.get("previousVersions") if isinstance(config, dict) else None
    first = versions[0] if isinstance(versions, list) and versions else None
    tag = first.get("tag") if isinstance(first, dict) else first
    if not isinstance(tag, str) or not tag:
        return [f"{CONTEXT7_CONFIG}: the first item of `previousVersions` must be the tag of the latest release; no tag found"]

    sys.path.insert(0, str(ROOT / "scripts"))
    import changelog

    changelog_path = root / "CHANGELOG.md"
    latest = changelog.latest_released_version(
        changelog_path.read_text(encoding="utf-8")) if changelog_path.is_file() else None
    if latest is None:
        return [f"{CONTEXT7_CONFIG}: CHANGELOG.md has no dated version section; the tag could not be verified"]
    if tag != f"v{latest}":
        return [f"{CONTEXT7_CONFIG}: the first `previousVersions` tag is {tag}; the latest release in CHANGELOG.md is v{latest}"]
    return []


UPGRADE_BASELINE_MANIFEST = pathlib.Path("tests", "Shared", "Upgrade", "UpgradeBaseline.json")


def upgrade_baseline_drift(root: pathlib.Path = ROOT) -> list[str]:
    """The upgrade gate's baseline must be the latest release.

    The gate builds the database of the last release and moves it to today's migrations. Its
    manifest names that release; a release cut that leaves the manifest behind keeps proving the
    upgrade from a version consumers no longer run. No manifest, no check.
    """
    manifest_path = root / UPGRADE_BASELINE_MANIFEST
    if not manifest_path.is_file():
        return []
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, ValueError) as exception:
        return [f"{UPGRADE_BASELINE_MANIFEST} could not be read: {exception}"]

    version = manifest.get("version") if isinstance(manifest, dict) else None
    if not isinstance(version, str) or not version:
        return [f"{UPGRADE_BASELINE_MANIFEST}: no `version`"]

    sys.path.insert(0, str(ROOT / "scripts"))
    import changelog

    changelog_path = root / "CHANGELOG.md"
    latest = changelog.latest_released_version(
        changelog_path.read_text(encoding="utf-8")) if changelog_path.is_file() else None
    if latest is None:
        return [f"{UPGRADE_BASELINE_MANIFEST}: CHANGELOG.md has no dated version section; the baseline could not be verified"]
    if version != latest:
        return [f"{UPGRADE_BASELINE_MANIFEST}: the baseline is {version}; the latest release in CHANGELOG.md is {latest}. "
                f"`python3 scripts/upgrade_baseline.py --write` rewrites the baseline from the tag"]
    return []


XML_CONFIGURATION_SUFFIXES = frozenset({".config", ".props", ".targets", ".csproj"})


def find_malformed_xml(root: pathlib.Path = ROOT) -> list[str]:
    """Return `path: error` records for build and package configuration files that are not well-formed XML.

    An XML comment may not contain two hyphens in a row, and a command line such as `--dry-run` pasted into
    one makes every restore or build that reads the file fail. Such a rewrite compiles nowhere else, so this
    scan is the only place that sees it before a restore does."""
    found: list[str] = []
    for path in _walk_files(root):
        if path.suffix.lower() not in XML_CONFIGURATION_SUFFIXES:
            continue
        try:
            ElementTree.parse(path)
        except (ElementTree.ParseError, OSError) as error:
            found.append(f"{path.relative_to(root)}: {error}")
    return found


def scan(root: pathlib.Path = ROOT, extension: Extension = PUBLIC) -> int:
    """Run the synchronization-copy, secret, doc-reference, XML, public-surface, Context7-version and upgrade-baseline scans, then the extension's own."""
    copies = find_sync_copies(root, (*SYNC_ROOTS, *extension.sync_roots))
    secrets, synthetic = find_secrets(root, extension)
    stale_docs = find_stale_doc_references(root, (*DOC_REFERENCE_ROOTS, *extension.doc_reference_roots))
    malformed_xml = find_malformed_xml(root)
    public_surface = public_surface_violations(root)
    context7 = context7_version_drift(root)
    upgrade_baseline = upgrade_baseline_drift(root)
    extra = [(check.heading, list(check.violations(root))) for check in extension.scan_checks]
    if (not copies and not secrets and not stale_docs and not malformed_xml and not public_surface and not context7
            and not upgrade_baseline and not any(violations for _, violations in extra)):
        print("Scan: ✅ clean"
              + (f" ({synthetic} marked synthetic credentials skipped)" if synthetic else ""))
        return 0

    if copies:
        print("Scan: ❌ sync copies")
        for path in copies:
            print(f"  {path.relative_to(root)}")
    if secrets:
        print("Scan: ❌ possible secret")
        for match in secrets[:20]:
            print(f"  {match}")
        if len(secrets) > 20:
            print(f"  … +{len(secrets) - 20}")
    if stale_docs:
        print("Scan: ❌ stale doc reference (the target file no longer exists)")
        for reference in stale_docs[:20]:
            print(f"  {reference}")
        if len(stale_docs) > 20:
            print(f"  … +{len(stale_docs) - 20}")
    if malformed_xml:
        print("Scan: ❌ configuration file is not well-formed XML (an XML comment cannot contain two hyphens in a row)")
        for violation in malformed_xml[:20]:
            print(f"  {violation}")
    if public_surface:
        print("Scan: ❌ public surface without evidence — make the type `internal`, mention it by name in a guide, "
              "or write a reason in `scripts/public-surface-reasons.tsv`")
        for violation in public_surface:
            print(f"  {violation}")
    if context7:
        print("Scan: ❌ Context7 version drift — a release cut also updates the `context7.json` tag")
        for violation in context7:
            print(f"  {violation}")
    if upgrade_baseline:
        print("Scan: ❌ the upgrade gate baseline is behind the latest release — a release cut also moves the baseline forward")
        for violation in upgrade_baseline:
            print(f"  {violation}")
    for heading, violations in extra:
        if violations:
            print(f"Scan: ❌ {heading}")
            for violation in violations[:20]:
                print(f"  {violation}")
            if len(violations) > 20:
                print(f"  … +{len(violations) - 20}")
    return 1


def _git(*args: str) -> list[str] | None:
    try:
        result = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    except OSError:
        return None
    if result.returncode:
        return None
    return [line for line in result.stdout.splitlines() if line.strip()]


def changed_paths(base: str = "HEAD") -> list[str] | None:
    """Include the base..working-tree range plus untracked paths.

    `base` defaults to HEAD (uncommitted changes only, used by `inner-loop`);
    `close` passes its own `--base` so the performance gate's trigger check
    (see `performance_gate_triggered`) sees the WHOLE change set, not just, not just
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
# `perf` - allocation gate
#
# The gate compares ALLOCATED BYTES, never wall-clock duration: on a shared CI
# runner, allocation is deterministic (same code -> same byte count) while
# duration moves with neighboring jobs. Duration is still recorded in
# bench/baseline.json, as information only - it never fails the gate.
# -----------------------------------------------------------------------------

PERFORMANCE_HOT_PATHS = (
    "src/Tracon.Core/Recording/RunEventWriter.cs",
    "src/Tracon.Core/Compilation/CompiledAgentCache.cs",
    "src/Tracon.Core/Diagnostics/ChatTelemetryLanes.cs",
    "src/Tracon.Sql.Shared/Stores/SqlRunStore.cs",
    # The in-memory store is the development default: its list query computes a tree total for each row of a page.
    "src/Tracon.Core/Storage/InMemoryRunStore.Queries.cs",
    # Every SQLite store call opens a connection: the open is a hot path of its own.
    "src/Tracon.Sqlite/Internal/SqliteDataSource.cs",
    "src/Tracon.Sqlite/Internal/SqliteDecimalFunctions.cs",
    "bench/",
)

BENCHMARK_PROJECT = ROOT / "bench" / "Tracon.Benchmarks" / "Tracon.Benchmarks.csproj"
BENCHMARK_BASELINE = ROOT / "bench" / "baseline.json"
BENCHMARK_REPORT_DIR = ARTIFACTS / "benchmarks" / "results"


def performance_gate_triggered(paths: Iterable[str]) -> bool:
    """Whether any of the four benchmarked hot paths (or the benchmark project
    itself) changed - the allocation gate only runs when it can move."""
    return any(
        path == hot_path or (hot_path.endswith("/") and path.startswith(hot_path))
        for path in paths
        for hot_path in PERFORMANCE_HOT_PATHS
    )


class BaselineError(Exception):
    """Raised when bench/baseline.json is missing or malformed - always
    surfaced as a clear message, never a bare traceback."""


def load_baseline(path: pathlib.Path = BENCHMARK_BASELINE) -> dict:
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as exception:
        raise BaselineError(f"{path}: the baseline could not be read: {exception}") from exception

    try:
        document = json.loads(text)
    except json.JSONDecodeError as exception:
        raise BaselineError(f"{path}: the baseline is not valid JSON: {exception}") from exception

    if not isinstance(document, dict) or not isinstance(document.get("benchmarks"), dict):
        raise BaselineError(f"{path}: the baseline lacks the expected 'benchmarks' key")

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
            messages.append(f"❌ {name}: not produced in this run (it is in the baseline - it may have been removed)")
            exit_code = 1
            continue
        if name not in baseline:
            messages.append(f"❌ {name}: not in the baseline - add it with 'gate.py perf --update'")
            exit_code = 1
            continue

        baseline_bytes = baseline[name]["allocatedBytes"]
        current_bytes = current[name]["allocatedBytes"]

        # A benchmark BenchmarkDotNet could not build or run still appears in
        # the report, with no measurement (2026-10-11: a second project of the
        # same name under .claude/worktrees). Comparing None crashed the gate.
        if current_bytes is None:
            messages.append(
                f"❌ {name}: produced no measurement - see the error in the BenchmarkDotNet output "
                "(for example a second Tracon.Benchmarks.csproj with the same name)")
            exit_code = 1
            continue

        if current_bytes > baseline_bytes:
            messages.append(
                f"❌ {name}: allocation grew ({baseline_bytes} B → {current_bytes} B, +{current_bytes - baseline_bytes} B)")
            exit_code = 1
        elif current_bytes < baseline_bytes:
            messages.append(
                f"⚠️ {name}: allocation shrank ({baseline_bytes} B → {current_bytes} B) - "
                "update the baseline: 'python3 scripts/gate.py perf --update'")
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
        print(f"❌ The benchmark run exited with {result.returncode}")
        return result.returncode

    report_files = _benchmark_report_files(report_dir)
    if not report_files:
        print(f"❌ BenchmarkDotNet JSON report not found ({report_dir})")
        return 1

    current: dict[str, dict[str, float]] = {}
    for report_path in report_files:
        current.update(parse_benchmark_report(json.loads(report_path.read_text(encoding="utf-8"))))

    if update:
        write_baseline(current, baseline_path)
        display_path = baseline_path.relative_to(ROOT) if ROOT in baseline_path.parents else baseline_path
        print(f"✅ baseline updated: {display_path}")
        for name, entry in sorted(current.items()):
            print(f"  {name}: {entry['allocatedBytes']} B")
        return 0

    exit_code, messages = compare_allocations(baseline_document["benchmarks"], current)
    for message in messages:
        print(message)
    return exit_code


# src/Tracon.Providers.Shared and tests/Shared/Providers are linked into all four provider
# test projects at once (<Compile Include>); the blast radius is exactly these four.
PROVIDER_TEST_PROJECTS: tuple[str, ...] = (
    "Tracon.OpenAI.UnitTests",
    "Tracon.Anthropic.UnitTests",
    "Tracon.Google.UnitTests",
    "Tracon.Azure.UnitTests",
)

# tests/Shared/Upgrade is the source of the upgrade gate; it is linked into the integration
# projects of the three SQL providers and (for the data files) into Sql.Shared.UnitTests.
UPGRADE_GATE_TEST_PROJECTS: tuple[str, ...] = (
    "Tracon.Sqlite.IntegrationTests",
    "Tracon.PostgreSql.IntegrationTests",
    "Tracon.SqlServer.IntegrationTests",
    "Tracon.Sql.Shared.UnitTests",
)

TEST_PROJECTS: dict[str, tuple[str, ...]] = {
    # Tracon.Sql.Shared.UnitTests is selected here too: the two cases the registration parity
    # gate (SqlProviderRegistrationParityTests) catches -- a new store forgotten in all three
    # providers at once, and an Auditing* decorator added only to AddTracon -- are born
    # only from a Core/Abstractions change.
    "Tracon.Abstractions": ("Tracon.Core.UnitTests", "Tracon.Sql.Shared.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.Core": (
        "Tracon.Core.UnitTests", "Tracon.AspNetCore.FunctionalTests", "Tracon.Sql.Shared.UnitTests",
        "Tracon.PackageOptions.UnitTests"),
    "Tracon.Generators": ("Tracon.Generators.UnitTests",),
    # The shared gates of the three SQL providers (registration parity, migration parity,
    # SQL text snapshot) live in Tracon.Sql.Shared.UnitTests. If a change that touches a single
    # Use* file does not select them too, drift shows up only in the full run.
    # The two-node acceptance set runs on both server providers; when the query of one provider
    # (counter upsert, lease, cancellation request) changes, it is selected too. Tracon.Sqlite is not
    # selected: SQLite is not in this set.
    "Tracon.PostgreSql": (
        "Tracon.PostgreSql.IntegrationTests", "Tracon.Sql.Shared.UnitTests", "Tracon.TwoNode.IntegrationTests",
        "Tracon.PackageOptions.UnitTests"),
    "Tracon.SqlServer": (
        "Tracon.SqlServer.IntegrationTests", "Tracon.Sql.Shared.UnitTests", "Tracon.TwoNode.IntegrationTests",
        "Tracon.PackageOptions.UnitTests"),
    "Tracon.Sqlite": ("Tracon.Sqlite.IntegrationTests", "Tracon.Sql.Shared.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.OpenAI": ("Tracon.OpenAI.UnitTests", "Tracon.Generators.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.Anthropic": ("Tracon.Anthropic.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.Google": ("Tracon.Google.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.Azure": ("Tracon.Azure.UnitTests", "Tracon.PackageOptions.UnitTests"),
    # NOT a package: shared source compiled into the four provider packages.
    # Without an entry here it would fall back to the full run; but the full blast radius is these
    # four projects and the PackageOptions project, which drives the provider option sections
    # through the real container.
    "Tracon.Providers.Shared": (*PROVIDER_TEST_PROJECTS, "Tracon.PackageOptions.UnitTests"),
    # NOT a package; shared source compiled into the two packages that use the OpenAI
    # client library (Tracon.OpenAI, Tracon.Azure).
    "Tracon.OpenAI.Shared": ("Tracon.OpenAI.UnitTests", "Tracon.Azure.UnitTests"),
    "Tracon.Voice": ("Tracon.Voice.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.Mcp": ("Tracon.Mcp.UnitTests", "Tracon.PackageOptions.UnitTests"),
    "Tracon.Workflows": ("Tracon.Workflows.UnitTests", "Tracon.PackageOptions.UnitTests"),
    # AG-UI shares it: turn setup (AgentTurnService) lives in AspNetCore, so a change there selects the AG-UI tests too.
    "Tracon.AspNetCore": ("Tracon.AspNetCore.FunctionalTests", "Tracon.AGUI.FunctionalTests"),
    "Tracon.AGUI": ("Tracon.AGUI.FunctionalTests",),
    "Tracon.UI": ("Tracon.Ui.E2ETests",),
    "Tracon.Testing": ("Tracon.Testing.UnitTests",),
    # The shipped contract package: its own unit tests and every project that derives
    # the contract (the in-memory stores in Core, the provider options contract in the four
    # providers, the store contracts in the three SQL providers). While it was missing from
    # this map every change widened to the full run (2026-10-13).
    "Tracon.Testing.Contracts.Xunit": (
        "Tracon.Testing.Contracts.Xunit.UnitTests", "Tracon.Core.UnitTests",
        *PROVIDER_TEST_PROJECTS,
        "Tracon.PostgreSql.IntegrationTests", "Tracon.SqlServer.IntegrationTests",
        "Tracon.Sqlite.IntegrationTests"),
    # NOT a package: shared source compiled into the three SQL packages.
    "Tracon.Sql.Shared": (
        "Tracon.Sql.Shared.UnitTests", "Tracon.PostgreSql.IntegrationTests",
        "Tracon.SqlServer.IntegrationTests", "Tracon.Sqlite.IntegrationTests",
        "Tracon.PackageOptions.UnitTests"),
    "Tracon.Client": ("Tracon.Client.UnitTests",),
    "Tracon.Cli": ("Tracon.Cli.FunctionalTests",),
}


# Every src/ change reaches this project, whatever the package: its
# ExampleExtractor enumerates src/**/*.cs and COMPILES every <example> block it
# finds, so an XML doc comment added anywhere under src/ can break it. Measured
# escape: a change was caught only by the full solution test run, not by
# Tracon.Core.UnitTests alone - the inner loop selected Core.UnitTests +
# AspNetCore.FunctionalTests and never ran the project that actually failed.
# Costs 2.4 s measured (2026-09-04 per-project profile).
EXAMPLE_COMPILING_TEST_PROJECT = "Tracon.Generators.UnitTests"

# The only test project that needs the UI built into the solution: it serves
# the embedded assets and waits for pages a UI-less build never ships.
UI_TEST_PROJECT = "Tracon.Ui.E2ETests"

# A sample directory the solution really tests, and the project that tests it
# (via ProjectReference). Measured escape: a sixth embedding point,
# samples/Tracon.Embedded, fell behind, and `inner-loop` selected NO test
# project at all for a samples/ change.
SAMPLE_TEST_PROJECTS: dict[str, str] = {
    "Tracon.Embedded": "Tracon.Embedded.Tests",
}

# These ship as consumer-facing samples but are NOT in Tracon.slnx: they
# compile against packed NuGet packages, so no closing-gate test run covers
# them. Only `gate.py release` (release_extension_samples.py) does. Saying so out
# loud beats selecting nothing silently.
RELEASE_ONLY_SAMPLE_PREFIX = "samples/Tracon.Samples."


# -----------------------------------------------------------------------------
# Test framework matrix
#
# A representative set of test projects runs on every framework the packages
# ship; the rest stays on net10.0. The set and the framework list live in ONE
# place, tests/Directory.Build.props, and are read from there - a second copy
# here would drift the first time the set changes. A multi-targeted project's
# output moves from artifacts/bin/<P>/release/ to release_<tfm>/, so every path
# this script builds to a test executable or a TRX goes through these helpers.
# -----------------------------------------------------------------------------

TESTS_BUILD_PROPS = pathlib.PurePath("tests", "Directory.Build.props")
TEST_FRAMEWORKS_ENV = "TraconTestTargetFrameworks"
_TEST_FRAMEWORKS_PROPERTY = re.compile(
    r"<TraconTestTargetFrameworks\b[^>]*>(?P<value>[^<]+)</TraconTestTargetFrameworks>")
_SINGLE_TEST_FRAMEWORK_PROPERTY = re.compile(r"<TargetFramework\b[^>]*>(?P<value>[^<]+)</TargetFramework>")
_MULTI_TARGET_PROPERTY = re.compile(
    r"<TraconMultiTargetTest\b(?P<condition>[^>]*)>true</TraconMultiTargetTest>", re.DOTALL)
_PROJECT_NAME_CONDITION = re.compile(r"'\$\(MSBuildProjectName\)'\s*==\s*'(?P<name>[^']+)'")


def _tests_build_props(root: pathlib.Path) -> str:
    return (root / TESTS_BUILD_PROPS).read_text(encoding="utf-8")


def test_target_frameworks(
    root: pathlib.Path = ROOT, environ: Mapping[str, str] = os.environ,
) -> tuple[str, ...]:
    """The frameworks a multi-targeted test project builds for.

    An environment variable of the same name wins, exactly as it does for
    MSBuild (environment variables are initial properties) - a run narrowed
    that way must look for the same output directories MSBuild produced."""
    override = environ.get(TEST_FRAMEWORKS_ENV, "").strip()
    if override:
        value = override
    else:
        match = _TEST_FRAMEWORKS_PROPERTY.search(_tests_build_props(root))
        if match is None:
            raise ValueError(f"{TESTS_BUILD_PROPS}: TraconTestTargetFrameworks not found")
        value = match.group("value")
    return tuple(part.strip() for part in value.split(";") if part.strip())


def single_test_target_framework(root: pathlib.Path = ROOT) -> str:
    match = _SINGLE_TEST_FRAMEWORK_PROPERTY.search(_tests_build_props(root))
    if match is None:
        raise ValueError(f"{TESTS_BUILD_PROPS}: single TargetFramework not found")
    return match.group("value").strip()


def multi_target_test_projects(root: pathlib.Path = ROOT) -> tuple[str, ...]:
    match = _MULTI_TARGET_PROPERTY.search(_tests_build_props(root))
    if match is None:
        return ()
    return tuple(found.group("name") for found in _PROJECT_NAME_CONDITION.finditer(match.group("condition")))


def test_output_directories(
    project: str,
    framework: str | None = None,
    *,
    root: pathlib.Path = ROOT,
    environ: Mapping[str, str] = os.environ,
) -> list[pathlib.Path]:
    """Every output directory a test project's executable lands in.

    🚨 Resolved from the DECLARED frameworks, never from what exists on disk: a
    project that just became multi-targeted still has a stale release/ folder
    from its last single-framework build, and running that binary would report
    on code that no longer exists."""
    project_bin = root / "artifacts" / "bin" / project
    if project in multi_target_test_projects(root):
        frameworks = test_target_frameworks(root, environ)
        if framework is not None:
            if framework not in frameworks:
                raise ValueError(f"{project} is not built for {framework} (built for: {', '.join(frameworks)})")
            frameworks = (framework,)
        return [project_bin / f"release_{name}" for name in frameworks]

    only = single_test_target_framework(root)
    if framework is not None and framework != only:
        raise ValueError(f"{project} is built only for {only}; the multi-TFM set lives in {TESTS_BUILD_PROPS}")
    return [project_bin / "release"]


def _runtime_majors_in(directory: pathlib.Path) -> set[int]:
    majors: set[int] = set()
    if directory.is_dir():
        for child in directory.iterdir():
            head = child.name.split(".", 1)[0]
            if child.is_dir() and head.isdigit():
                majors.add(int(head))
    return majors


_APPHOST_ARCHITECTURES = {"arm64": "arm64", "aarch64": "arm64", "x86_64": "x64", "amd64": "x64"}
DOTNET_INSTALL_LOCATION_DIR = pathlib.Path("/etc/dotnet")
_DEFAULT_GLOBAL_DOTNET_ROOT = {"Darwin": "/usr/local/share/dotnet", "Linux": "/usr/share/dotnet"}


def _apphost_architecture(machine: str | None = None) -> str | None:
    return _APPHOST_ARCHITECTURES.get((machine or platform.machine()).lower())


def _apphost_dotnet_root(environ: Mapping[str, str], machine: str | None = None) -> str:
    """The root the apphost reads first: DOTNET_ROOT_<ARCH> wins over DOTNET_ROOT."""
    architecture = _apphost_architecture(machine)
    keys = ((f"DOTNET_ROOT_{architecture.upper()}",) if architecture else ()) + ("DOTNET_ROOT",)
    for key in keys:
        value = environ.get(key, "").strip()
        if value:
            return value
    return ""


def _global_dotnet_root(
    system: str | None = None,
    machine: str | None = None,
    install_location_dir: pathlib.Path = DOTNET_INSTALL_LOCATION_DIR,
) -> str | None:
    """Where a Unix apphost looks when no DOTNET_ROOT is set.

    Measured (this machine): /etc/dotnet/install_location_arm64 names
    /usr/local/share/dotnet while the plain install_location names .../x64 -
    the architecture-specific file wins. None on Windows (registry-based):
    the caller then falls back to the muxer on PATH."""
    system = system or platform.system()
    if system not in _DEFAULT_GLOBAL_DOTNET_ROOT:
        return None
    architecture = _apphost_architecture(machine)
    names = ((f"install_location_{architecture}",) if architecture else ()) + ("install_location",)
    for name in names:
        try:
            lines = (install_location_dir / name).read_text(encoding="utf-8").splitlines()
        except OSError:
            continue
        if lines and lines[0].strip():
            return lines[0].strip()
    return _DEFAULT_GLOBAL_DOTNET_ROOT[system]


def installed_runtime_majors(
    environ: Mapping[str, str] = os.environ,
    runner: Runner = subprocess.run,
    *,
    system: str | None = None,
    machine: str | None = None,
    install_location_dir: pathlib.Path = DOTNET_INSTALL_LOCATION_DIR,
) -> tuple[str, set[int]] | None:
    """(root, majors) of the Microsoft.NETCore.App runtimes a test apphost can load.

    Measured: `dotnet test` starts each MTP test through its apphost,
    and the apphost resolves the runtime from DOTNET_ROOT_<ARCH> or DOTNET_ROOT,
    then from the GLOBAL install location - never from the `dotnet` muxer that
    happens to be on PATH. A private SDK put first on PATH without DOTNET_ROOT
    would otherwise pass this check while every leg it serves fails."""
    dotnet_root = _apphost_dotnet_root(environ, machine) or _global_dotnet_root(
        system, machine, install_location_dir)
    if dotnet_root:
        shared = pathlib.Path(dotnet_root) / "shared" / "Microsoft.NETCore.App"
        return (dotnet_root, _runtime_majors_in(shared)) if shared.is_dir() else None
    try:
        result = runner(["dotnet", "--list-runtimes"], capture_output=True, text=True, check=False)
    except OSError:
        return None
    if int(result.returncode) != 0:
        return None
    majors: set[int] = set()
    root = "dotnet"
    for line in str(result.stdout).splitlines():
        match = re.match(r"Microsoft\.NETCore\.App (?P<major>\d+)\.\S+ \[(?P<path>[^\]]+)\]", line.strip())
        if match:
            majors.add(int(match.group("major")))
            root = str(pathlib.PurePath(match.group("path")).parent.parent)
    return root, majors


def _missing_frameworks(frameworks: Iterable[str], majors: set[int]) -> list[str]:
    return [
        framework for framework in frameworks
        if int(framework.removeprefix("net").split(".", 1)[0]) not in majors
    ]


def missing_test_runtimes(
    frameworks: Iterable[str],
    *,
    environ: Mapping[str, str] = os.environ,
    runner: Runner = subprocess.run,
    system: str | None = None,
    machine: str | None = None,
    install_location_dir: pathlib.Path = DOTNET_INSTALL_LOCATION_DIR,
) -> list[str]:
    """Frameworks whose runtime the test apphost cannot find.

    Without this a missing runtime surfaces minutes later as a failed
    test leg, and the isolated re-run then calls it a REAL regression. An
    unreadable runtime list blocks nothing: the real command then fails on its
    own, with its own message."""
    found = installed_runtime_majors(
        environ, runner, system=system, machine=machine, install_location_dir=install_location_dir)
    return [] if found is None else _missing_frameworks(frameworks, found[1])


def require_test_runtimes(
    frameworks: Iterable[str],
    *,
    environ: Mapping[str, str] = os.environ,
    runner: Runner = subprocess.run,
    system: str | None = None,
    machine: str | None = None,
    install_location_dir: pathlib.Path = DOTNET_INSTALL_LOCATION_DIR,
    purpose: str = "Multi-TFM test projects",
) -> bool:
    found = installed_runtime_majors(
        environ, runner, system=system, machine=machine, install_location_dir=install_location_dir)
    if found is None:
        return True
    root, majors = found
    missing = _missing_frameworks(frameworks, majors)
    if not missing:
        return True
    print(f"❌ {purpose} need these runtimes: {', '.join(missing)} — not found under `{root}`"
          f" (installed: {', '.join(str(major) for major in sorted(majors)) or 'none'}).")
    print("   The test apphost resolves the runtime from DOTNET_ROOT_<ARCH>/DOTNET_ROOT, or else from the"
          " global install — NOT from the `dotnet` on PATH."
          " Fix: set DOTNET_ROOT to the directory where those runtimes are installed.")
    return False


def _test_target(args: Sequence[str]) -> str:
    """The project or solution a `dotnet test` command targets.

    MTP mode takes the target with `--solution` / `--project`; the first positional
    argument (the old form) is still accepted."""
    for flag in ("--solution", "--project"):
        if flag in args and args.index(flag) + 1 < len(args):
            return args[args.index(flag) + 1]
    return args[2]


def runs_multi_target_tests(commands: Iterable[Command], root: pathlib.Path = ROOT) -> bool:
    """True if a `dotnet test` in `commands` reaches a multi-targeted project."""
    multi = set(multi_target_test_projects(root))
    for command in commands:
        if command.args[:2] != ("dotnet", "test") or len(command.args) < 3:
            continue
        target = pathlib.PurePath(_test_target(command.args))
        if target.suffix in (".slnx", ".slnf", ".sln") or target.stem in multi or target.name in multi:
            return True
    return False


def affected_test_projects(paths: Iterable[str]) -> tuple[list[str], bool]:
    projects: set[str] = set()
    needs_full = False
    for path in paths:
        if path.startswith("tests/Shared/Providers/"):
            projects.update(PROVIDER_TEST_PROJECTS)
            continue
        if path.startswith("tests/Shared/TargetFramework/"):
            projects.update(multi_target_test_projects())
            continue
        if path.startswith("tests/Shared/Upgrade/"):
            projects.update(UPGRADE_GATE_TEST_PROJECTS)
            continue
        if path.startswith("tests/Shared/"):
            needs_full = True
            continue
        if path.startswith("tests/"):
            match = re.match(r"tests/([^/]+)/", path)
            if match:
                projects.add(match.group(1))
            else:
                # tests/Directory.Build.props and friends reach every test
                # project. This branch used to select NOTHING, silently.
                needs_full = True
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
                    f"⚠️ {path}: this sample is not in the solution; only "
                    "`python3 scripts/gate.py release` runs it against the packed release.")
                continue
            match = re.match(r"samples/([^/]+)/", path)
            sample = match.group(1) if match else ""
            if sample in SAMPLE_TEST_PROJECTS:
                projects.add(SAMPLE_TEST_PROJECTS[sample])
        elif path.startswith("packages/") or path.startswith("scripts/"):
            projects.add("Tracon.Client.UnitTests" if path.startswith("packages/") else "Tracon.Core.UnitTests")
    return sorted(projects), needs_full


def _dotnet_test_project(project: str) -> Command:
    return Command((
        "dotnet", "test", "--project", f"tests/{project}/{project}.csproj", "-c", "Release", "--no-build"))


def _trx_test_command(target_flag: str, target: str, parallel_modules: int) -> Command:
    """A `dotnet test` run that writes TRX reports, with the flags every gate test run shares.

    The TRX flags equal those of the CI test steps: a failed test is machine-readable and
    `isolate_failed_tests` can rerun it alone. Measured (2026-09-04): writing TRX stays below the noise of a run
    (557 s with, 561 s without)."""
    return Command((
        "dotnet", "test", target_flag, target, "-c", "Release", "--no-build",
        "--max-parallel-test-modules", str(parallel_modules),
        "--report-trx", "--results-directory", TEST_RESULTS_DIRECTORY,
        "--results-directory-layout", "per-module"))


def full_solution_test_command() -> Command:
    """Run test projects with enough isolation for Docker and package tests.

    A solution test run otherwise starts every test executable at once. The
    concurrent Docker containers, Playwright browser, functional hosts, and
    package fixture can starve each other and turn healthy short deadlines into
    timeouts. Two projects at a time is the measured limit (TEST_MAX_CPU_COUNT):
    three starved each other, two ran green three times once the tests stopped
    sleeping fixed times.

    The closing run does not use this single command any more (see `staged_test_commands`);
    the quick inner loop does, when it cannot name the affected projects.
    """
    return _trx_test_command("--solution", "Tracon.slnx", TEST_MAX_CPU_COUNT)


# The closing test run, in the order it runs:
#   1. Core alone. Its 7 000 tests take about 40 s. MTP picks the module order inside one solution
#      run and does not let a caller choose it (Core started 641 s into a 996 s run once), so a red
#      Core test is only reliably early when Core is a run of its own.
#   2. The light modules, in a solution filter (`FAST_TEST_SOLUTION_FILTER`): about a minute at
#      two modules at a time.
#   3. The heavy modules, in `TEST_MAX_CPU_COUNT` lanes that run beside each other. A lane is a chain
#      of one-module runs, so at most that many heavy modules run at once, as before, and the order is
#      the same every run. The containers start last. An earlier trial that ran the heavy modules
#      right behind a light wave lost 121 SQL Server tests to a starved container (2026-09-04) and its
#      cause was never isolated: this order narrows the exposure and does not prove it away. The
#      acceptance runs of the closing gate are the evidence; if the red returns, a wait for container
#      health between stages 2 and 3 is a separate hypothesis to measure first.
# A red stage ends the run; later stages do not start.
CORE_TEST_PROJECT = "Tracon.Core.UnitTests"
FAST_TEST_SOLUTION_FILTER = "Tracon.fast.slnf"
HEAVY_TEST_GROUP = "heavy-tests"

# Wall seconds of the last full gate (two modules at a time): TwoNode 444, AspNetCore 372, SqlServer 303,
# Package 158, PostgreSql 111, Sqlite 70, E2E 33. Lane a adds up to 780, lane b to 711.
# - SqlServer and TwoNode share a lane: they never run together, by construction. They compete for the
#   container budget of one machine.
# - E2E ends lane a. It starts when lane b has finished, so the browser runs on a quiet machine
#   instead of beside the other heavy modules.
# The number of lanes is `TEST_MAX_CPU_COUNT`.
HEAVY_TEST_LANES: dict[str, tuple[str, ...]] = {
    "a": ("Tracon.TwoNode.IntegrationTests", "Tracon.SqlServer.IntegrationTests", "Tracon.Ui.E2ETests"),
    "b": (
        "Tracon.AspNetCore.FunctionalTests", "Tracon.PostgreSql.IntegrationTests",
        "Tracon.Package.Tests", "Tracon.Sqlite.IntegrationTests"),
}


def staged_test_commands() -> list[Command]:
    """The closing test run: Core alone, then the light modules, then the heavy lanes."""
    commands = [
        _trx_test_command("--project", f"tests/{CORE_TEST_PROJECT}/{CORE_TEST_PROJECT}.csproj", TEST_MAX_CPU_COUNT),
        _trx_test_command("--solution", FAST_TEST_SOLUTION_FILTER, TEST_MAX_CPU_COUNT),
    ]
    for lane, projects in HEAVY_TEST_LANES.items():
        commands.extend(
            dataclasses.replace(
                _trx_test_command("--project", f"tests/{project}/{project}.csproj", 1),
                group=HEAVY_TEST_GROUP, lane=lane)
            for project in projects)
    return commands


TRX_NS = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"


@dataclasses.dataclass(frozen=True)
class FailedTest:
    """A failed test and the output directory whose executable produced it."""

    project: str
    output: str
    name: str

    @property
    def label(self) -> str:
        """`Project` for a single-framework project, `Project (net10.0)` for one leg of a multi-targeted one."""
        framework = self.output.removeprefix("release").removeprefix("_")
        return f"{self.project} ({framework})" if framework else self.project


def _recent_trx_reports(since: float, root: pathlib.Path) -> Iterable[tuple[str, pathlib.Path, Any]]:
    """Every TRX written after `since`: (project, file, parsed tree).

    The layout is `--results-directory-layout per-module`,
    `<TEST_RESULTS_DIRECTORY>/<project>/<tfm>_<arch>/*.trx`. An unreadable file
    is skipped; with no TRX at all it yields nothing (the caller reads that as "zero tests")."""
    import xml.etree.ElementTree as elementtree

    for trx in sorted((root / TEST_RESULTS_DIRECTORY).glob("*/*/*.trx")):
        try:
            if trx.stat().st_mtime < since:
                continue
            tree = elementtree.parse(trx)
        except (OSError, elementtree.ParseError):
            continue
        yield trx.relative_to(root / TEST_RESULTS_DIRECTORY).parts[0], trx, tree


def _trx_output_directory(tree: Any) -> str:
    """Name of the output folder of the test binary under `artifacts/bin/<project>/`.

    The TRX carries each module's DLL path as `codeBase`; the folder name is `release` or,
    in a multi-targeted project, `release_<tfm>`. The path is read from the TRX itself, not
    from the DECLARED framework: a stale `release/` folder cannot make the wrong binary
    run. Without `codeBase` the `release` folder of a single-target project is assumed;
    `codeBase` is always present in live TRX files, so this branch never lands on a
    multi-targeted leg."""
    for method in tree.getroot().iter(TRX_NS + "TestMethod"):
        code_base = method.get("codeBase")
        if code_base:
            parts = re.split(r"[\\/]", code_base)
            if len(parts) >= 2 and (parts[-2] == "release" or parts[-2].startswith("release_")):
                return parts[-2]
    return "release"


def failed_tests_from_trx(since: float, root: pathlib.Path = ROOT) -> list[FailedTest]:
    """Every failed test in every TRX written after `since`.

    Each TFM leg of a multi-targeted project is a SEPARATE TRX; the leg's
    binary (`release_<tfm>`) is read from the TRX. Without that, the failed tests of those
    projects would vanish SILENTLY and the isolated rerun would never trigger.

    The TRX is no longer next to the binary, it lives under `TEST_RESULTS_DIRECTORY`.
    Scanning the old path finds NO failed test at all and the gate says "nothing to run in
    isolation" - red stays red, but the diagnosis disappears silently."""
    failures: list[FailedTest] = []
    for project, _, tree in _recent_trx_reports(since, root):
        output = _trx_output_directory(tree)
        for result in tree.getroot().iter(TRX_NS + "UnitTestResult"):
            if result.get("outcome") not in (None, "Passed", "NotExecuted"):
                name = result.get("testName")
                if name:
                    failures.append(FailedTest(project, output, name))
    return failures


def executed_tests_from_trx(since: float, root: pathlib.Path = ROOT) -> tuple[int, int]:
    """(executed test count, module count) from the TRX files written after `since`.

    In MTP mode a wrong flag (such as `-maxcpucount`) can end with an exit code
    WITHOUT running any test; the reverse is true too - a run with exit code 0
    and no TRX looks "green". This count is the gate against that silence."""
    tests = 0
    modules = 0
    for _, _, tree in _recent_trx_reports(since, root):
        modules += 1
        tests += sum(1 for _ in tree.getroot().iter(TRX_NS + "UnitTestResult"))
    return tests, modules


def _trx_duration_ms(value: str | None) -> int | None:
    """A TRX duration in the form `HH:MM:ss.fffffff`; `None` if it cannot be parsed."""
    match = re.fullmatch(r"(\d+):(\d\d):(\d\d(?:\.\d+)?)", value or "")
    if match is None:
        return None
    hours, minutes, seconds = match.groups()
    return round((int(hours) * 3600 + int(minutes) * 60 + float(seconds)) * 1000)


def record_test_durations(since: float, root: pathlib.Path = ROOT) -> pathlib.Path | None:
    """Record the time per test from the TRX files written after `since`.

    Test times must not drift between iterations; measuring that needs the
    per-test time of every full run. The TRX is overwritten by the next run, this
    record is not: `artifacts/test-durations/<utc>.json.gz`, the last 20 runs.

    It is only a report: it does NOT CHANGE THE EXIT CODE. It warns if it cannot write. Two
    results with the same name (`[Theory]` rows that show the same name) keep the longest."""
    results = root / TEST_RESULTS_DIRECTORY
    modules: dict[str, dict[str, int]] = {}
    for _, trx, tree in _recent_trx_reports(since, root):
        tests = modules.setdefault(trx.parent.relative_to(results).as_posix(), {})
        for result in tree.getroot().iter(TRX_NS + "UnitTestResult"):
            name = result.get("testName")
            duration = _trx_duration_ms(result.get("duration"))
            if name and duration is not None:
                tests[name] = max(duration, tests.get(name, 0))
    if not modules:
        return None

    stamp = time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    directory = root / TEST_DURATIONS_DIRECTORY
    path = directory / f"{stamp}.json.gz"
    try:
        directory.mkdir(parents=True, exist_ok=True)
        with gzip.open(path, "wt", encoding="utf-8") as file:
            json.dump({"utc": stamp, "modules": modules}, file, ensure_ascii=False, separators=(",", ":"))
        for old in sorted(directory.glob("*.json.gz"))[:-TEST_DURATIONS_KEEP]:
            old.unlink()
    except OSError as exception:
        print(f"⚠️ The test duration record could not be written: {exception}")
        return None
    print(f"⏱️  Test durations: {path.relative_to(root).as_posix()}")
    return path


def isolate_failed_tests(
    failures: Sequence[FailedTest],
    *,
    runner: Runner = subprocess.run,
    root: pathlib.Path = ROOT,
) -> list[tuple[str, str, bool]]:
    """Rerun every failed test ALONE and print the verdict.

    This is the three-step manual aid used while a defect is investigated. In this repo the
    class "fails in the full run, passes in isolation" was recorded SEVEN times, the seventh
    one on 2026-09-04. Every time the two causes were told apart BY HAND - most often by
    running the FULL suite a second time (~9 minutes, measured: 553 s). An isolated run
    takes seconds (measured: 2.5 s).

    🚨 It does NOT CHANGE THE EXIT CODE. This is a diagnosis, not a loosening of the gate:
    red stays red. All it gives is what the next step is.
    """
    verdicts: list[tuple[str, str, bool]] = []
    for failure in failures:
        executable = root / "artifacts" / "bin" / failure.project / failure.output / failure.project
        # The TRX name of a `[Theory]` carries the arguments too, and they can contain
        # a DOT: `...A_provider_name_is_matched(savedAs: "a.b")`. The argument tail is
        # dropped FIRST, THEN the last segment is taken - the reverse order would put an
        # argument into the filter instead of the method name.
        method = failure.name.split("(", 1)[0].rsplit(".", 1)[-1]
        if not executable.exists():
            print(f"   ⚠️ {failure.label}: no built binary, the isolated run was skipped")
            continue
        environment = os.environ.copy()
        environment["MSBUILDDISABLENODEREUSE"] = "1"
        result = runner(
            [str(executable), "--filter-method", f"*{method}*"],
            cwd=str(root), env=environment, check=False,
            stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        verdicts.append((failure.label, failure.name, int(result.returncode) == 0))

    if not verdicts:
        return verdicts
    print("\n🔬 Isolated rerun (diagnosis — does NOT change the exit code):")
    for label, name, passed in verdicts:
        print(f"   {'✅ PASSED in isolation' if passed else '❌ FAILED in isolation too'}  {label} · {name}")
    if all(passed for _, _, passed in verdicts):
        print("   → All passed in isolation: the test carries a time, order, or shared-resource"
              " assumption, or there is a product race that shows only under load.")
        print("   → STOP: do not rerun the suite. Waiting for green is not a fix."
              " Find out which cause it is and fix the root cause.")
    else:
        print("   → A test that fails in isolation too is a REAL regression;"
              " investigate the root cause.")
    return verdicts


def inner_loop_commands(paths: list[str]) -> list[Command]:
    projects, needs_full = affected_test_projects(paths)
    # The E2E suite serves the UI assets embedded by this build. A run
    # that reaches it (a full run, or the E2E project itself) built without
    # them failed every E2E test and buried the real regression in that noise.
    needs_frontend = frontend_changed(paths) or needs_full or UI_TEST_PROJECT in projects
    frontend_flag = () if needs_frontend else ("-p:TraconFrontendEnabled=false",)
    commands = [Command(("dotnet", "build", "Tracon.slnx", "-c", "Release", *frontend_flag))]
    if needs_full:
        print("⚠️ The affected-project map is incomplete; the test selection grew to a full run.")
        return commands + [full_solution_test_command()]
    return commands + [_dotnet_test_project(project) for project in projects]


def generation_commands(extension: Extension = PUBLIC) -> list[Command]:
    """Regenerate the committed generated files whose freshness CI checks.

    Measured (October 2026): 11 of 42 red closing runs stopped at
    `build-agent-map.mjs --check` - the map was edited around but never
    regenerated. That is a forgetting class: a check can only report it, a
    generator removes it. CI keeps checking the same files (`ci.yml` runs
    `build-agent-map.mjs --check`), so a
    regenerated file that was never committed is still caught there.
    """
    return [
        *extension.generation,
        Command(("node", "docs-site/scripts/build-agent-map.mjs")),
    ]


READ_ONLY_GROUP = "read-only"


def closing_commands(
    base: str, *, site: bool = True, performance: bool = True, extension: Extension = PUBLIC,
) -> list[Command]:
    # Cheapest first; the test run (about 20 min, November 2026) is the most expensive step
    # and runs LAST. Measured (October 2026): 11 of 42 red closing runs failed
    # in a step that used to run AFTER the tests - docs-site check 6,
    # `perf` 4, format 1 - about 144 minutes of test time thrown away.
    # Generation runs first so every later check sees the regenerated files.
    #
    # The read-only checks that follow it do not depend on each other, so they run at the same time as
    # one group (about 115 s instead of 180 s). They only read the tree and the network. Everything that
    # writes or builds (generation, restore, build, pack, site, format, performance) stays one after another.
    read_only = [
        Command(("python3", extension.program, "scan")),
        *extension.after_scan(base),
        # Reads the remote: the last release tag's packages and the last CI run. Early and cheap (a few
        # seconds), because a half-published release is the kind of red no local step can show.
        Command(("python3", extension.program, "remote-state")),
        Command(("python3", "-m", "unittest", "discover", "-s", "scripts", "-p", "*_test.py")),
    ]
    commands = [
        *generation_commands(extension),
        *(dataclasses.replace(command, group=READ_ONLY_GROUP) for command in read_only),
        # A cached restore is a no-op and never re-runs the NuGet audit, so
        # an advisory published after the last restore stayed invisible here while
        # CI's cold checkout turned it into NU1902/NU1903 (a warning-as-error).
        # `--force` re-evaluates every project (~5 s measured, 2026-10-17).
        Command(("dotnet", "restore", "Tracon.slnx", "--force")),
        Command(("dotnet", "build", "Tracon.slnx", "-c", "Release")),
        # TraconSkipCleanWorkingTreeCheck: this pack validates the
        # PACKAGING CONTRACT (README, icon) during iteration - it is not
        # a release candidate, so it must still work on an uncommitted tree.
        # The real release rehearsal (`gate.py release`) and `gate.py pack` -
        # the build job's pack that a `v*` tag publishes - both leave
        # this unset and stay fully gated by TraconValidateCleanWorkingTree.
        Command((
            "dotnet", "pack", "Tracon.slnx", "-c", "Release", "--no-build",
            "-p:TraconSkipCleanWorkingTreeCheck=true")),
    ]
    # After the build: DocFX reads the XML documentation the build wrote. Before
    # format and the tests: ~35 s and the most frequent late red (6 of 11).
    # The earlier fix made `SourceLanguageTests` skip the gitignored trees the site build
    # generates, so building the site first no longer changes a test result.
    if site:
        commands.append(Command(("npm", "run", "check"), ROOT / "docs-site"))
    # 🚨 NOT --no-restore: measured (2026-08-27) that dotnet format's
    # restore-less MSBuildWorkspace load intermittently fails to resolve
    # PackageReference types for every samples/*.Tests project (a
    # floating-version, local-feed consumer of Tracon.* packages)
    # specifically right after the full solution test run (format then ran
    # after it) - reproduced 3 times through that command chain, never in
    # isolation. A plain restore immediately before format is a few
    # seconds when nothing changed ("all projects are up-to-date") and
    # reliably avoids the false failure.
    commands.append(Command(("dotnet", "format", "Tracon.slnx", "--verify-no-changes")))
    # Path-triggered: a fifth ALWAYS-ON gate would run BenchmarkDotNet
    # on every phase closing, even when nothing near the four hot paths
    # changed. `gate.py perf` stays a normal subcommand; `main()` decides
    # whether THIS closing run needs it, the same way `affected_test_projects`
    # already scopes `inner-loop`.
    if performance:
        commands.append(Command(("python3", "scripts/gate.py", "perf")))
    commands.extend(staged_test_commands())
    return commands


def test_commands(
    project: str,
    patterns: Sequence[str],
    framework: str | None = None,
    *,
    root: pathlib.Path = ROOT,
    environ: Mapping[str, str] = os.environ,
) -> list[Command]:
    """One filtered run per framework leg; `framework` narrows a multi-targeted project to one."""
    return [
        Command((str(output / project), "--filter-class", *patterns))
        for output in test_output_directories(project, framework, root=root, environ=environ)
    ]


# -----------------------------------------------------------------------------
# `stability` - does one set of built binaries behave the same way, run after run?
#
# The gate answers "is this tree green once". This command answers the other question: the SAME
# bytes, run N times, must give the same results in about the same time. It never builds. It runs the
# test executables that already sit in `artifacts/bin/` (the same files `test` runs), keeps the TRX of
# every run in a directory of its own, and compares the runs:
#
#   - result identity: the same tests with the same outcomes in every run, also under CPU pressure and
#     in a shuffled order. Any difference is red and names the test. A test that fails in every run is
#     red too: a stable red is not a stable result.
#   - time: per test, the spread between the slowest and the fastest run is expected to stay within
#     max(STABILITY_TEST_FLOOR_MS, STABILITY_TEST_MEDIAN_FACTOR x the median of that test); per module,
#     the slowest wall time within the fastest by STABILITY_MODULE_WALL_PERCENT
#     (with a floor of STABILITY_MODULE_WALL_FLOOR_MS, so a one-second module is not judged by
#     scheduler noise). Only the runs without CPU pressure are judged; pressured runs are reported.
#     A spread past the limit is a REPORT (verdict `time`, listed in the table, the findings and the report
#     file) and never changes the exit code: wall time measures the machine as much as the code.
#
# Shuffled order: every xunit v3 test executable of this repository offers `--seed <integer>`
# ("Set the randomization seed"). With `--shuffle` each run gets a different, recorded seed, so a
# failure is replayed with the same seed. xunit documents the order of test COLLECTIONS as random from
# run to run and the order of classes, methods and cases as stable; it does not say that a seed
# reorders them. The command therefore measures instead of assuming: it compares the order in which
# the tests STARTED (TRX `startTime`) between runs and prints the share of neighbouring pairs that kept
# their order. A value close to 1.0 in a shuffled module is reported as "no visible effect".
#
# The thresholds below may only be tightened.
# -----------------------------------------------------------------------------

STABILITY_REPORT_DIRECTORY = ARTIFACTS / "stability"
STABILITY_REPORTS_KEEP = 20
STABILITY_TRX_DIRECTORIES_KEEP = 3
STABILITY_DEFAULT_REPEAT = 5
STABILITY_MIN_REPEAT = 2
STABILITY_TRX_NAME = "result.trx"
STABILITY_MODULE_TIMEOUT_MINUTES = 45
"""A module run that takes longer is cut by the test platform's own `--timeout` and counts as a red run."""

STABILITY_TEST_FLOOR_MS = 200
STABILITY_TEST_MEDIAN_FACTOR = 1
STABILITY_MODULE_WALL_PERCENT = 15
STABILITY_MODULE_WALL_FLOOR_MS = 200

STABILITY_RESULT_EXIT_CODE = 92
STABILITY_USAGE_EXIT_CODE = 2
STABILITY_RUN_TIMEOUT_EXIT_CODE = 124
STABILITY_LAUNCH_EXIT_CODE = 126

STABILITY_SHUFFLE_NO_EFFECT = 0.98
STABILITY_SHUFFLE_MIN_TESTS = 20
STABILITY_PASSING_OUTCOMES = ("Passed", "NotExecuted")
STABILITY_LISTED_PROBLEMS = 8
"""How many findings per module reach the terminal; the report file holds more."""

STABILITY_BURNER_LIFETIME_SECONDS = 3 * 3600
"""A burner stops itself after this long, whatever happens to the command that started it."""
_BURNER_SOURCE = (
    "import os, sys, time\n"
    "parent, lifetime = int(sys.argv[1]), float(sys.argv[2])\n"
    "end = time.monotonic() + lifetime\n"
    "while True:\n"
    "    for _ in range(200000):\n"
    "        pass\n"
    "    if time.monotonic() > end or (os.name == 'posix' and os.getppid() != parent):\n"
    "        break\n"
)


class CpuBurner:
    """Busy Python processes that take CPU time from the test run, never more than `count`.

    A burner ends in three ways, so none is left behind: `stop()` (also from `__exit__`), the lifetime it
    was given, and - on POSIX - the death of the process that started it."""

    def __init__(
        self,
        count: int,
        *,
        popen: Callable[..., Any] = subprocess.Popen,
        lifetime_seconds: float = STABILITY_BURNER_LIFETIME_SECONDS,
    ) -> None:
        self.count = count
        self._popen = popen
        self._lifetime = lifetime_seconds
        self._processes: list[Any] = []

    @property
    def running(self) -> bool:
        return bool(self._processes)

    def alive(self) -> int:
        return sum(1 for process in self._processes if process.poll() is None)

    def start(self) -> None:
        if self.running:
            return
        try:
            for _ in range(self.count):
                self._processes.append(self._popen(
                    [sys.executable, "-c", _BURNER_SOURCE, str(os.getpid()), str(self._lifetime)],
                    stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL))
        except BaseException:
            self.stop()
            raise

    def stop(self) -> None:
        processes, self._processes = self._processes, []
        for process in processes:
            try:
                process.terminate()
            except OSError:
                pass
        for process in processes:
            try:
                process.wait(timeout=5)
            except Exception:
                try:
                    process.kill()
                    process.wait(timeout=5)
                except Exception:
                    pass

    def __enter__(self) -> "CpuBurner":
        self.start()
        return self

    def __exit__(self, *exception: object) -> None:
        self.stop()


def stability_burner_count(cpu_count: int | None = None) -> int:
    """Half of the logical CPUs, at least one."""
    return max(1, (cpu_count or os.cpu_count() or 1) // 2)


@dataclasses.dataclass(frozen=True)
class StabilityUnit:
    """One test executable of a stability run: a module, or one framework leg of a multi-targeted module."""

    project: str
    output: str
    stage: str
    lane: str | None
    executable: pathlib.Path

    @property
    def label(self) -> str:
        framework = self.output.removeprefix("release").removeprefix("_")
        return f"{self.project} ({framework})" if framework else self.project

    @property
    def slug(self) -> str:
        return f"{self.project}__{self.output}"


@dataclasses.dataclass(frozen=True)
class ModuleRun:
    """What one run of one test executable left behind."""

    label: str
    index: int
    pressured: bool
    seed: int | None
    exit_code: int
    process_seconds: float
    wall_seconds: float | None = None
    durations_ms: Mapping[str, int] = dataclasses.field(default_factory=dict)
    outcomes: Mapping[str, tuple[str, ...]] = dataclasses.field(default_factory=dict)
    order: tuple[str, ...] = ()
    problem: str = ""
    """Why the run left no usable report (empty when it did)."""

    @property
    def failed_tests(self) -> list[str]:
        return sorted(
            name for name, outcomes in self.outcomes.items()
            if any(outcome not in STABILITY_PASSING_OUTCOMES for outcome in outcomes))

    @property
    def clean(self) -> bool:
        return not self.problem and self.exit_code == 0 and not self.failed_tests


@dataclasses.dataclass(frozen=True)
class DurationSpread:
    """How far apart the runs of one test were, and how far apart they may be."""

    name: str
    span_ms: int
    allowed_ms: float
    durations_ms: tuple[int, ...]

    @property
    def ratio(self) -> float:
        return self.span_ms / self.allowed_ms

    @property
    def exceeded(self) -> bool:
        return self.span_ms > self.allowed_ms


@dataclasses.dataclass
class ModuleComparison:
    """The verdict on one module over all its runs."""

    label: str
    runs: int
    judged_runs: int
    pressured_runs: int
    wall_seconds: tuple[float, ...] = ()
    """Wall times of the judged (clean, unpressured) runs."""
    wall_spread_percent: float | None = None
    wall_exceeded: bool = False
    pressured_wall_seconds: tuple[float, ...] = ()
    worst_test: DurationSpread | None = None
    slow_tests: list[DurationSpread] = dataclasses.field(default_factory=list)
    result_problems: list[str] = dataclasses.field(default_factory=list)
    time_problems: list[str] = dataclasses.field(default_factory=list)
    order_similarity: list[float] = dataclasses.field(default_factory=list)
    notes: list[str] = dataclasses.field(default_factory=list)

    @property
    def verdict(self) -> str:
        if self.result_problems:
            return "result"
        if self.time_problems:
            return "time"
        return "green"


@dataclasses.dataclass(frozen=True)
class _ModuleReport:
    wall_seconds: float | None
    durations_ms: dict[str, int]
    outcomes: dict[str, tuple[str, ...]]
    order: tuple[str, ...]


def read_module_report(directory: pathlib.Path) -> _ModuleReport | None:
    """The newest TRX in `directory` as durations, outcomes and start order; `None` when there is none to read."""
    import xml.etree.ElementTree as elementtree

    try:
        files = sorted(directory.glob("*.trx"), key=lambda path: path.stat().st_mtime)
        if not files:
            return None
        tree = elementtree.parse(files[-1])
    except (OSError, elementtree.ParseError):
        return None
    durations: dict[str, int] = {}
    outcomes: dict[str, list[str]] = {}
    started: dict[str, float] = {}
    for result in tree.getroot().iter(TRX_NS + "UnitTestResult"):
        name = result.get("testName")
        if not name:
            continue
        duration = _trx_duration_ms(result.get("duration"))
        if duration is not None:
            durations[name] = max(duration, durations.get(name, 0))
        outcomes.setdefault(name, []).append(result.get("outcome") or "Passed")
        begin = _trx_instant(result.get("startTime"))
        if begin is not None and begin < started.get(name, begin + 1):
            started[name] = begin
    return _ModuleReport(
        trx_wall_seconds(tree),
        durations,
        {name: tuple(sorted(found)) for name, found in outcomes.items()},
        tuple(sorted(started, key=lambda name: (started[name], name))))


def wall_spread_exceeded(walls_seconds: Sequence[float]) -> tuple[bool, float]:
    """(over the limit?, spread in percent of the fastest run). Integer milliseconds: a value exactly on the limit passes."""
    milliseconds = [round(seconds * 1000) for seconds in walls_seconds]
    fastest, slowest = min(milliseconds), max(milliseconds)
    spread = slowest - fastest
    percent = 100.0 * spread / fastest if fastest else (0.0 if spread == 0 else float("inf"))
    allowed_times_100 = max(fastest * STABILITY_MODULE_WALL_PERCENT, STABILITY_MODULE_WALL_FLOOR_MS * 100)
    return spread * 100 > allowed_times_100, percent


def duration_spreads(runs: Sequence[ModuleRun]) -> list[DurationSpread]:
    """The spread of every test that every run measured, widest relative to its limit first."""
    if len(runs) < 2:
        return []
    common = set(runs[0].durations_ms)
    for run in runs[1:]:
        common &= set(run.durations_ms)
    spreads: list[DurationSpread] = []
    for name in common:
        durations = tuple(run.durations_ms[name] for run in runs)
        allowed = max(STABILITY_TEST_FLOOR_MS, STABILITY_TEST_MEDIAN_FACTOR * statistics.median(durations))
        spreads.append(DurationSpread(name, max(durations) - min(durations), allowed, durations))
    return sorted(spreads, key=lambda spread: (-spread.ratio, spread.name))


def order_similarity(reference: Sequence[str], other: Sequence[str]) -> float | None:
    """Share of neighbouring pairs of `reference` that `other` starts in the same order; `None` for fewer than two tests."""
    position = {name: index for index, name in enumerate(other)}
    common = [name for name in reference if name in position]
    if len(common) < 2:
        return None
    kept = sum(1 for first, second in zip(common, common[1:]) if position[first] < position[second])
    return kept / (len(common) - 1)


def _result_differences(usable: Sequence[ModuleRun]) -> list[str]:
    problems: list[str] = []
    names = sorted(set().union(*(run.outcomes for run in usable)))
    for name in names:
        per_run = [run.outcomes.get(name) for run in usable]
        if len(set(per_run)) > 1:
            shown = ", ".join(
                f"run {run.index}: {'/'.join(found) if found else 'missing'}" for run, found in zip(usable, per_run))
            problems.append(f"{name} — result changed ({shown})")
        elif per_run[0] and any(outcome not in STABILITY_PASSING_OUTCOMES for outcome in per_run[0]):
            problems.append(f"{name} — failed in every run ({'/'.join(per_run[0])})")
    return problems


def compare_module(label: str, runs: Sequence[ModuleRun], *, shuffled: bool = False) -> ModuleComparison:
    """Compare the runs of one module: result identity over all runs, time over the clean unpressured ones."""
    ordered = sorted(runs, key=lambda run: run.index)
    comparison = ModuleComparison(
        label, len(ordered), 0, sum(1 for run in ordered if run.pressured))
    for run in ordered:
        if run.problem:
            comparison.result_problems.append(f"run {run.index}: {run.problem}")
    usable = [run for run in ordered if not run.problem]
    comparison.result_problems.extend(_result_differences(usable))
    for run in usable:
        if run.exit_code != 0 and not run.failed_tests:
            comparison.result_problems.append(
                f"run {run.index}: exit code {run.exit_code} but the report lists no failed test")

    judged = [run for run in ordered if run.clean and not run.pressured]
    comparison.judged_runs = len(judged)
    comparison.pressured_wall_seconds = tuple(
        run.wall_seconds for run in ordered if run.clean and run.pressured and run.wall_seconds is not None)
    if len(judged) < 2:
        comparison.notes.append(
            f"time could not be compared: {len(judged)} clean unpressured runs (at least 2 are needed)")
    else:
        spreads = duration_spreads(judged)
        comparison.slow_tests = [spread for spread in spreads if spread.exceeded]
        comparison.worst_test = spreads[0] if spreads else None
        for spread in comparison.slow_tests:
            comparison.time_problems.append(
                f"{spread.name} — test time span {spread.span_ms} ms > {spread.allowed_ms:.0f} ms"
                f" (runs: {', '.join(str(ms) for ms in spread.durations_ms)})")
        walls = [run.wall_seconds for run in judged if run.wall_seconds is not None]
        if len(walls) < len(judged):
            comparison.notes.append("the wall time could not be read in some TRX files; the module time could not be compared")
        elif walls:
            comparison.wall_seconds = tuple(walls)
            comparison.wall_exceeded, comparison.wall_spread_percent = wall_spread_exceeded(walls)
            if comparison.wall_exceeded:
                comparison.time_problems.insert(
                    0, f"module wall time: slowest {max(walls):.1f} s, fastest {min(walls):.1f} s"
                       f" (spread {comparison.wall_spread_percent:.1f}% > {STABILITY_MODULE_WALL_PERCENT}%)")

    if len(usable) >= 2:
        reference = usable[0].order
        for run in usable[1:]:
            similarity = order_similarity(reference, run.order)
            if similarity is not None:
                comparison.order_similarity.append(similarity)
        if (shuffled and comparison.order_similarity
                and len(reference) >= STABILITY_SHUFFLE_MIN_TESTS
                and min(comparison.order_similarity) >= STABILITY_SHUFFLE_NO_EFFECT):
            comparison.notes.append(
                "shuffle has no visible effect: the start order is almost the same across runs"
                " (there may be one collection only, or the runner ignores the seed)")
    return comparison


def stability_exit_code(comparisons: Iterable[ModuleComparison]) -> int:
    """`STABILITY_RESULT_EXIT_CODE` when any module's result differs, else 0.

    A time problem is a report and has no exit code: the verdict stays `time` in the table and the report file."""
    if any(comparison.verdict == "result" for comparison in comparisons):
        return STABILITY_RESULT_EXIT_CODE
    return 0


def _stability_seconds(values: Sequence[float]) -> str:
    if not values:
        return "-"
    return f"{min(values):.1f} / {statistics.median(values):.1f} / {max(values):.1f}"


def _stability_table_cells(comparisons: Sequence[ModuleComparison], *, shuffled: bool) -> tuple[list[str], list[list[str]]]:
    """The header and the rows of the stability table: per module the wall times, the worst test and the verdict."""
    symbols = {"green": "✅", "time": "⏱️ time", "result": "❌ result"}
    header = ["module", "runs", "wall s (min / median / max)", "spread %", "pressured max s", "worst test (span / limit ms)"]
    if shuffled:
        header.append("order")
    header.append("verdict")
    rows: list[list[str]] = []
    for comparison in comparisons:
        worst = comparison.worst_test
        row = [
            comparison.label,
            f"{comparison.runs}",
            _stability_seconds(comparison.wall_seconds),
            "-" if comparison.wall_spread_percent is None else f"{comparison.wall_spread_percent:.1f}",
            f"{max(comparison.pressured_wall_seconds):.1f}" if comparison.pressured_wall_seconds else "-",
            "-" if worst is None else f"{worst.span_ms} / {worst.allowed_ms:.0f}",
        ]
        if shuffled:
            row.append(f"{min(comparison.order_similarity):.2f}" if comparison.order_similarity else "-")
        row.append(symbols[comparison.verdict])
        rows.append(row)
    return header, rows


def format_stability_table(comparisons: Sequence[ModuleComparison], *, shuffled: bool) -> list[str]:
    """One human table: per module the wall times, the worst test and the verdict."""
    header, rows = _stability_table_cells(comparisons, shuffled=shuffled)
    widths = [max(len(header[column]), *(len(row[column]) for row in rows)) if rows else len(header[column])
              for column in range(len(header))]
    lines = ["  ".join(cell.ljust(widths[column]) for column, cell in enumerate(header)).rstrip()]
    lines.append("  ".join("-" * width for width in widths))
    lines.extend("  ".join(cell.ljust(widths[column]) for column, cell in enumerate(row)).rstrip() for row in rows)
    return lines


def format_stability_markdown(comparisons: Sequence[ModuleComparison], *, shuffled: bool) -> list[str]:
    """The stability table as a markdown table, for a job summary."""
    header, rows = _stability_table_cells(comparisons, shuffled=shuffled)
    lines = ["| " + " | ".join(header) + " |", "|" + "|".join(" --- " for _ in header) + "|"]
    lines.extend("| " + " | ".join(row) + " |" for row in rows)
    return lines


def stability_verdict_lines(comparisons: Sequence[ModuleComparison], exit_code: int) -> list[str]:
    """What the run says about itself: the result verdict, then which modules moved in time, then green if neither."""
    lines: list[str] = []
    if exit_code == STABILITY_RESULT_EXIT_CODE:
        lines.append(f"❌ Result unstable (exit {STABILITY_RESULT_EXIT_CODE}): fix the instability before you go on.")
    timed = [comparison.label for comparison in comparisons if comparison.verdict == "time"]
    if timed:
        lines.append(
            f"⏱️ Time spread passed the limit in {len(timed)} module(s): {', '.join(timed)}. "
            "It is a report and does not change the exit code; read the findings before you trust the numbers.")
    if not lines:
        lines.append("✅ Stable: the results are the same and the times stay within the limits.")
    return lines


def write_step_summary(path: pathlib.Path, lines: Sequence[str]) -> None:
    """Append `lines` to the job summary file of the CI run. A file that cannot be written warns and nothing else."""
    try:
        with path.open("a", encoding="utf-8") as stream:
            stream.write("\n".join(lines) + "\n")
    except OSError as exception:
        print(f"⚠️ The job summary could not be written to {path}: {exception}")


def stability_payload(
    comparisons: Sequence[ModuleComparison],
    runs: Mapping[str, Sequence[ModuleRun]],
    *,
    stamp: str,
    repeat: int,
    shuffled: bool,
    base_seed: int | None,
    pressure_runs: Sequence[int],
    burner_count: int,
    exit_code: int,
) -> dict[str, Any]:
    """The report file: thresholds, per run facts, and every finding (the terminal lists only the first few)."""
    modules: dict[str, Any] = {}
    for comparison in comparisons:
        module_runs = sorted(runs[comparison.label], key=lambda run: run.index)
        modules[comparison.label] = {
            "verdict": comparison.verdict,
            "runs": [
                {
                    "index": run.index,
                    "pressured": run.pressured,
                    "seed": run.seed,
                    "exit_code": run.exit_code,
                    "process_seconds": round(run.process_seconds, 3),
                    "wall_seconds": None if run.wall_seconds is None else round(run.wall_seconds, 3),
                    "tests": len(run.outcomes),
                    "problem": run.problem,
                }
                for run in module_runs
            ],
            "judged_runs": comparison.judged_runs,
            "wall_seconds": [round(seconds, 3) for seconds in comparison.wall_seconds],
            "wall_spread_percent": None if comparison.wall_spread_percent is None else round(comparison.wall_spread_percent, 2),
            "pressured_wall_seconds": [round(seconds, 3) for seconds in comparison.pressured_wall_seconds],
            "worst_test": None if comparison.worst_test is None else dataclasses.asdict(comparison.worst_test),
            "slow_tests": [dataclasses.asdict(spread) for spread in comparison.slow_tests[:50]],
            "result_problems": comparison.result_problems[:200],
            "time_problems": comparison.time_problems[:200],
            "order_similarity": [round(value, 4) for value in comparison.order_similarity],
            "notes": comparison.notes,
        }
    return {
        "utc": stamp,
        "repeat": repeat,
        "shuffled": shuffled,
        "base_seed": base_seed,
        "pressure": {"runs": list(pressure_runs), "burners": burner_count if pressure_runs else 0},
        "thresholds": {
            "test_floor_ms": STABILITY_TEST_FLOOR_MS,
            "test_median_factor": STABILITY_TEST_MEDIAN_FACTOR,
            "module_wall_percent": STABILITY_MODULE_WALL_PERCENT,
            "module_wall_floor_ms": STABILITY_MODULE_WALL_FLOOR_MS,
        },
        "exit_code": exit_code,
        "modules": modules,
    }


def _prune_stability_reports(directory: pathlib.Path) -> None:
    """Keep the newest reports, and the TRX directories of only the newest few."""
    for old in sorted(directory.glob("*.json"))[:-STABILITY_REPORTS_KEEP]:
        old.unlink()
    for old in sorted(path for path in directory.iterdir() if path.is_dir())[:-STABILITY_TRX_DIRECTORIES_KEEP]:
        shutil.rmtree(old, ignore_errors=True)


def stability_plan(root: pathlib.Path = ROOT) -> list[tuple[str, str, str | None]]:
    """(project, stage, lane) of every test module, in the order the closing run starts them."""
    plan: list[tuple[str, str, str | None]] = [(CORE_TEST_PROJECT, "core", None)]
    filter_file = json.loads((root / FAST_TEST_SOLUTION_FILTER).read_text(encoding="utf-8"))
    for path in filter_file["solution"]["projects"]:
        plan.append((pathlib.PurePosixPath(path).stem, "light", None))
    for lane, projects in HEAVY_TEST_LANES.items():
        plan.extend((project, "heavy", lane) for project in projects)
    return plan


def stability_units(
    projects: Sequence[str] | None = None,
    exclude: Sequence[str] = (),
    *,
    root: pathlib.Path = ROOT,
    environ: Mapping[str, str] = os.environ,
) -> list[StabilityUnit]:
    """The executables to run. `projects` limits the set, `exclude` removes from it; a name nobody knows is an error."""
    plan = stability_plan(root)
    known = {project for project, _, _ in plan}
    unknown = sorted((set(projects or ()) | set(exclude)) - known)
    if unknown:
        raise ValueError(f"unknown test module: {', '.join(unknown)}")
    units: list[StabilityUnit] = []
    for project, stage, lane in plan:
        if (projects and project not in projects) or project in exclude:
            continue
        for output in test_output_directories(project, root=root, environ=environ):
            units.append(StabilityUnit(project, output.name, stage, lane, output / project))
    return units


def _stability_command(unit: StabilityUnit, directory: pathlib.Path, seed: int | None) -> list[str]:
    arguments = [
        str(unit.executable), "--report-trx", "--report-trx-filename", STABILITY_TRX_NAME,
        "--results-directory", str(directory), "--timeout", f"{STABILITY_MODULE_TIMEOUT_MINUTES}m",
        "--ansi", "off", "--progress", "off"]
    if seed is not None:
        arguments += ["--seed", str(seed)]
    return arguments


def _run_stability_unit(
    unit: StabilityUnit, index: int, pressured: bool, seed: int | None, run_directory: pathlib.Path,
    runner: Runner, environment: Mapping[str, str], root: pathlib.Path,
) -> ModuleRun:
    """Run one executable once. Never raises: whatever goes wrong becomes the `problem` of the run."""
    directory = run_directory / unit.slug
    started = time.monotonic()
    exit_code = 0
    problem = ""
    try:
        directory.mkdir(parents=True, exist_ok=True)
        with (directory / "output.log").open("wb") as log:
            result = runner(
                _stability_command(unit, directory, seed), cwd=str(root), env=environment, check=False,
                stdout=log, stderr=subprocess.STDOUT, timeout=(STABILITY_MODULE_TIMEOUT_MINUTES + 2) * 60)
        exit_code = int(result.returncode)
    except subprocess.TimeoutExpired:
        exit_code, problem = STABILITY_RUN_TIMEOUT_EXIT_CODE, f"did not finish within {STABILITY_MODULE_TIMEOUT_MINUTES} min"
    except FileNotFoundError as exception:
        exit_code, problem = 127, f"binary not found: {exception.filename}"
    except OSError as exception:
        exit_code, problem = STABILITY_LAUNCH_EXIT_CODE, f"the process could not start: {exception}"
    except BaseException as exception:  # a defect here must not leave the calling thread waiting
        exit_code, problem = INTERNAL_ERROR_EXIT_CODE, f"the runner crashed: {exception!r}"
    process_seconds = time.monotonic() - started
    report = None if problem else read_module_report(directory)
    if report is None or not report.outcomes:
        return ModuleRun(
            unit.label, index, pressured, seed, exit_code, process_seconds,
            problem=problem or (
                f"the TRX report is missing or unreadable (exit {exit_code})" if report is None
                else f"the report holds no test (exit {exit_code})"))
    return ModuleRun(
        unit.label, index, pressured, seed, exit_code, process_seconds, report.wall_seconds,
        report.durations_ms, report.outcomes, report.order)


def _stability_progress(run: ModuleRun) -> str:
    if run.problem:
        return f"   ❌ {run.label}: {run.problem}"
    failed = len(run.failed_tests)
    wall = "?" if run.wall_seconds is None else f"{run.wall_seconds:.1f}"
    mark = "✅" if run.clean else "❌"
    detail = f", {failed} failed" if failed else (f", exit {run.exit_code}" if run.exit_code else "")
    return f"   {mark} {run.label}: {wall} s, {len(run.outcomes)} tests{detail}"


def _execute_stability_run(
    units: Sequence[StabilityUnit], index: int, pressured: bool, seed: int | None, run_directory: pathlib.Path,
    runner: Runner, environment: Mapping[str, str], root: pathlib.Path,
) -> list[ModuleRun]:
    """One run of every selected module, in the order and with the concurrency of the closing test run.

    Core alone first, then the light modules `TEST_MAX_CPU_COUNT` at a time, then the heavy lanes beside each
    other. A red module does not stop the run: stability needs every run to finish."""
    finished: list[ModuleRun] = []
    running: dict[str, float] = {}

    def stage(chains: list[list[StabilityUnit]], workers: int) -> None:
        arrived: queue.Queue[ModuleRun] = queue.Queue()
        total = sum(len(chain) for chain in chains)

        def work(chain: Sequence[StabilityUnit]) -> None:
            for unit in chain:
                running[unit.label] = time.monotonic()
                try:
                    run = _run_stability_unit(unit, index, pressured, seed, run_directory, runner, environment, root)
                except BaseException as exception:  # the calling thread counts results: one must always arrive
                    run = ModuleRun(
                        unit.label, index, pressured, seed, INTERNAL_ERROR_EXIT_CODE, 0.0,
                        problem=f"the runner crashed: {exception!r}")
                finally:
                    running.pop(unit.label, None)
                arrived.put(run)

        began = time.monotonic()
        with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, workers)) as pool:
            for chain in chains:
                pool.submit(work, chain)
            for _ in range(total):
                while True:
                    try:
                        run = arrived.get(timeout=GROUP_HEARTBEAT_SECONDS)
                        break
                    except queue.Empty:
                        names = ", ".join(sorted(running))
                        print(f"   … still running ({time.monotonic() - began:.0f} s): {names}", flush=True)
                finished.append(run)
                print(_stability_progress(run), flush=True)

    core = [unit for unit in units if unit.stage == "core"]
    if core:
        stage([core], 1)
    light = [[unit] for unit in units if unit.stage == "light"]
    if light:
        stage(light, TEST_MAX_CPU_COUNT)
    lanes: dict[str, list[StabilityUnit]] = {}
    for unit in units:
        if unit.stage == "heavy":
            lanes.setdefault(unit.lane or unit.project, []).append(unit)
    if lanes:
        stage(list(lanes.values()), len(lanes))
    return finished


def stability_finding_lines(comparisons: Sequence[ModuleComparison]) -> list[str]:
    """The findings per module, the first few of each (the report file holds the rest)."""
    lines: list[str] = []
    for comparison in comparisons:
        findings = [("result", problem) for problem in comparison.result_problems]
        findings += [("time", problem) for problem in comparison.time_problems]
        if not findings and not comparison.notes:
            continue
        lines.extend(["", comparison.label])
        for kind, problem in findings[:STABILITY_LISTED_PROBLEMS]:
            lines.append(f"   {'❌' if kind == 'result' else '⏱️'} {problem}")
        if len(findings) > STABILITY_LISTED_PROBLEMS:
            lines.append(f"   … {len(findings) - STABILITY_LISTED_PROBLEMS} more findings in the report file")
        lines.extend(f"   ℹ️ {note}" for note in comparison.notes)
    return lines


def stability_check(
    units: Sequence[StabilityUnit],
    repeat: int = STABILITY_DEFAULT_REPEAT,
    *,
    shuffle: bool = False,
    pressure: bool = False,
    pressure_runs: Sequence[int] | None = None,
    base_seed: int | None = None,
    root: pathlib.Path = ROOT,
    runner: Runner = subprocess.run,
    burner_factory: Callable[[int], CpuBurner] = CpuBurner,
    report_directory: pathlib.Path | None = None,
    measurement_path: pathlib.Path = MEASUREMENTS,
    stamp: str | None = None,
    cpu_count: int | None = None,
    summary_path: pathlib.Path | None = None,
) -> int:
    """Run the units `repeat` times, compare the runs, write the report, return the exit code.

    0 when every result is the same, whatever the time did (a time spread is reported, not failed);
    `STABILITY_RESULT_EXIT_CODE` when any result differs (or a test fails); `STABILITY_USAGE_EXIT_CODE` when the
    command itself could not be set up. CPU pressure runs only for the runs in `pressure_runs` (default: the last
    two). The table and the verdict are also appended to `summary_path` (the job summary of a CI run) when given."""
    if repeat < STABILITY_MIN_REPEAT:
        print(f"❌ --repeat must be at least {STABILITY_MIN_REPEAT}.")
        return STABILITY_USAGE_EXIT_CODE
    if not units:
        print("❌ There is no test module to run.")
        return STABILITY_USAGE_EXIT_CODE
    if pressure and pressure_runs is None:
        pressure_runs = [repeat - 1, repeat] if repeat >= 3 else [repeat]
    pressured_runs = sorted(set(pressure_runs or ())) if pressure else []
    if any(number < 1 or number > repeat for number in pressured_runs):
        print(f"❌ --pressure-runs must be between 1 and {repeat}.")
        return STABILITY_USAGE_EXIT_CODE
    if not shuffle:
        base_seed = None
    elif base_seed is None:
        base_seed = int(time.time()) % 1_000_000_000

    started = time.monotonic()
    stamp = stamp or time.strftime("%Y%m%dT%H%M%SZ", time.gmtime())
    report_directory = STABILITY_REPORT_DIRECTORY if report_directory is None else report_directory
    run_root = report_directory / stamp
    environment = os.environ.copy()
    environment["MSBUILDDISABLENODEREUSE"] = "1"
    burners = stability_burner_count(cpu_count)
    burner = burner_factory(burners)
    shuffle_text = f"on (seed {base_seed} + run number - 1)" if shuffle else "off"
    pressure_text = (
        "runs " + ", ".join(str(number) for number in pressured_runs) + f" ({burners} load processes)"
        if pressured_runs else "none")
    print(f"Stability: {repeat} runs, {len(units)} test binaries; shuffle: {shuffle_text}; "
          f"CPU pressure: {pressure_text}", flush=True)
    collected: dict[str, list[ModuleRun]] = {unit.label: [] for unit in units}
    try:
        for index in range(1, repeat + 1):
            pressured = index in pressured_runs
            if pressured and not burner.running:
                try:
                    burner.start()
                except OSError as exception:
                    print(f"❌ CPU pressure could not be set up: {exception}")
                    return STABILITY_USAGE_EXIT_CODE
                if burner.alive() < burner.count:
                    print("❌ CPU pressure could not be set up: the load processes ended as soon as they started.")
                    return STABILITY_USAGE_EXIT_CODE
            elif not pressured and burner.running:
                burner.stop()
            seed = None if base_seed is None or not shuffle else base_seed + index - 1
            print(f"\nRun {index}/{repeat}"
                  f"{' · CPU pressure' if pressured else ''}{f' · seed {seed}' if seed is not None else ''}", flush=True)
            for run in _execute_stability_run(
                    units, index, pressured, seed, run_root / f"run-{index}", runner, environment, root):
                collected[run.label].append(run)
    finally:
        burner.stop()

    comparisons = [compare_module(unit_label, collected[unit_label], shuffled=shuffle) for unit_label in collected]
    exit_code = stability_exit_code(comparisons)
    table = format_stability_table(comparisons, shuffled=shuffle)
    findings = stability_finding_lines(comparisons)
    verdict = stability_verdict_lines(comparisons, exit_code)
    print("\n" + "\n".join(table))
    if findings:
        print("\n".join(findings))
    if pressured_runs:
        print("\nThe times of CPU-pressured runs are reported but do not count as red; under pressure only a result difference is red.")
    print("\n".join(verdict), flush=True)
    if summary_path is not None:
        write_step_summary(summary_path, [
            "### Stability", "", *verdict, "", *format_stability_markdown(comparisons, shuffled=shuffle),
            *(["", "```text", *findings, "```"] if findings else [])])

    payload = stability_payload(
        comparisons, collected, stamp=stamp, repeat=repeat, shuffled=shuffle, base_seed=base_seed,
        pressure_runs=pressured_runs, burner_count=burners, exit_code=exit_code)
    try:
        report_directory.mkdir(parents=True, exist_ok=True)
        report_path = report_directory / f"{stamp}.json"
        report_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        _prune_stability_reports(report_directory)
        print(f"📄 Report: {report_path}")
    except OSError as exception:
        print(f"⚠️ The stability report could not be written: {exception}")
    _record_measurement(
        "stability",
        Command(("gate.py", "stability", "--repeat", str(repeat), *(("--shuffle",) if shuffle else ()),
                 *(("--pressure",) if pressure else ()))),
        started, exit_code, measurement_path)
    return exit_code


def stability_dry_run(units: Sequence[StabilityUnit], repeat: int, shuffle: bool, pressure: bool) -> None:
    """`--print-commands`: the commands of one run, with the markers of the run loop as `#` lines."""
    print(f"# {repeat} runs; each run executes the commands below: Core alone, light modules {TEST_MAX_CPU_COUNT} at a time, heavy lanes side by side")
    if pressure:
        print(f"# CPU pressure: {stability_burner_count()} load processes for the last runs")
    for unit in units:
        print("$ " + shlex.join(_stability_command(unit, pathlib.Path("<run>") / unit.slug, 1 if shuffle else None)))


def stability_from_arguments(args: argparse.Namespace) -> int:
    """The `stability` subcommand: resolve the modules, refuse to run without built binaries, then `stability_check`."""
    try:
        units = stability_units(args.project, args.exclude or ())
    except ValueError as error:
        print(f"❌ {error}")
        return STABILITY_USAGE_EXIT_CODE
    pressure = args.pressure or bool(args.pressure_runs)
    if args.print_commands:
        stability_dry_run(units, args.repeat, args.shuffle, pressure)
        return 0
    missing = [unit.label for unit in units if not unit.executable.is_file()]
    if missing:
        print(f"❌ No built test binary: {', '.join(missing)}")
        print("   `stability` does not build. Run `dotnet build Tracon.slnx -c Release` first.")
        return STABILITY_USAGE_EXIT_CODE
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    return stability_check(
        units, args.repeat, shuffle=args.shuffle, pressure=pressure,
        pressure_runs=args.pressure_runs, base_seed=args.seed,
        report_directory=pathlib.Path(args.output) if args.output else None,
        summary_path=pathlib.Path(summary) if summary else None)


# -----------------------------------------------------------------------------
# `release` - release rehearsal
#
# Writes nothing to a network. It packs the solution (forcing a version via the
# MinVerVersionOverride environment variable when --package-version is given - no git tag
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


# Every package is MIT. The .nuspec names the license by its SPDX expression; the text
# rides along as a plain file at the package root, because MIT asks for the notice to
# travel with every copy. NuGet refuses a declared license FILE next to an expression,
# so the file is checked here by presence and bytes, not through the .nuspec.
LICENSE_EXPRESSION = "MIT"
LICENSE_FILE = "LICENSE.md"
RETIRED_LICENSE_FILE = "LICENSE-MIT.md"


TARGET_FRAMEWORKS_PATTERN = re.compile(r"<TargetFrameworks>([^<]+)</TargetFrameworks>")

SRC_BUILD_PROPS = pathlib.Path("src") / "Directory.Build.props"


def _frameworks_in(path: pathlib.Path) -> tuple[str, ...]:
    match = TARGET_FRAMEWORKS_PATTERN.search(path.read_text(encoding="utf-8"))
    if match and match.group(1).strip():
        return tuple(part.strip() for part in match.group(1).split(";") if part.strip())
    return ()


def _target_frameworks(root: pathlib.Path, project_id: str) -> tuple[str, ...]:
    """Most packages inherit the framework list from src/Directory.Build.props;
    a project that pins a single framework (e.g. Tracon.Cli, which is packed as
    a tool and so cannot cross-target) overrides
    TargetFrameworks (plural) explicitly. The default is read, not restated:
    adding a framework to the packages is then one edit."""
    own = _frameworks_in(root / "src" / project_id / f"{project_id}.csproj")
    if own:
        return own
    inherited = _frameworks_in(root / SRC_BUILD_PROPS)
    if not inherited:
        raise ValueError(f"{SRC_BUILD_PROPS}: <TargetFrameworks> not found")
    return inherited


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
# `gate.py release` report a false "different artifact" conflict on every
# second run against an unchanged commit, which is precisely the false
# positive a hand-run check of this command exists to rule out.
_VOLATILE_OPC_ENTRY = re.compile(r"^_rels/\.rels$|^package/services/metadata/core-properties/[0-9a-f]{32}\.psmdcp$")


class PackageFileError(Exception):
    """A package file that cannot be read as a zip. The message names the file."""


def _content_fingerprint(path: pathlib.Path) -> str:
    """A hash of a `.nupkg`/`.snupkg`'s MEANINGFUL content - every entry
    except the random-named OPC metadata NuGet regenerates on every pack.
    Two packs of the same commit are expected to have the SAME fingerprint;
    the raw file SHA-256 (`_sha256`, used for the published manifest) is not
    expected to match and is not what decides a promote/conflict."""
    digest = hashlib.sha256()
    try:
        with zipfile.ZipFile(path) as archive:
            names = sorted(name for name in archive.namelist() if not _VOLATILE_OPC_ENTRY.match(name))
            for name in names:
                digest.update(name.encode("utf-8"))
                digest.update(archive.read(name))
    except (zipfile.BadZipFile, OSError) as exception:
        raise PackageFileError(f"{path.name}: the package could not be read ({exception})") from exception
    return digest.hexdigest()


def _promote_staged_packages(
    staging_dir: pathlib.Path,
    release_dir: pathlib.Path,
    file_names: Iterable[str],
    *,
    copy: bool = False,
) -> list[str]:
    """Moves each named file from `staging_dir` into `release_dir` - UNLESS an
    identically named file already lives there with a DIFFERENT content
    fingerprint (`_content_fingerprint`, NOT the raw file hash - see its
    docstring), in which case NOTHING is moved and the mismatched names are
    returned so the caller can report them and abort. `release_dir` is
    therefore never left half-updated: either every file promotes, or none
    does. A same-fingerprint match is a deterministic no-op - the existing
    file already carries what this run would have produced, so it is left in
    place as the one true copy.

    `copy=True` (`release --package-directory`) leaves `staging_dir`
    untouched: the input is the artifact CI verified and it must not change.
    Each file is copied under a temporary name that does not end in
    `.nupkg`/`.snupkg` and then renamed with `os.replace`, so an interrupted
    copy never leaves a truncated file under a package name."""
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
        if not copy:
            shutil.move(str(staging_dir / name), str(destination))
            continue
        temporary = release_dir / f".{name}.{os.getpid()}.partial"
        try:
            shutil.copyfile(staging_dir / name, temporary)
            os.replace(temporary, destination)
        finally:
            temporary.unlink(missing_ok=True)
    return []


def _write_manifest(
    release_dir: pathlib.Path,
    *,
    version: str,
    commit: str,
    packages: list[dict[str, str | None]],
    baseline: str | None = None,
    api_compat: list[dict[str, object]] | None = None,
) -> pathlib.Path:
    manifest: dict[str, object] = {"version": version, "commit": commit, "dirty": False}
    if baseline is not None:
        manifest["baseline"] = baseline
    manifest["packages"] = packages
    if api_compat is not None:
        manifest["apiCompat"] = api_compat
    manifest_path = release_dir / PACKAGE_MANIFEST_NAME
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


def dependency_floor_errors(project_id: str, nuspec: str) -> list[str]:
    """Errors where the nuspec's lower bound of a pinned dependency differs from `SHIPPED_DEPENDENCY_FLOORS`.

    The lower bound is written as `version="3.2.2"` (= `>= 3.2.2`) or `[3.2.2, )`. Raising it is
    a compatibility decision, not an update; a missing dependency is an error too,
    otherwise the gate would pass empty."""
    errors: list[str] = []
    for dependency, floor in SHIPPED_DEPENDENCY_FLOORS.get(project_id, {}).items():
        ranges = [
            match.group("range") for match in NUSPEC_DEPENDENCY_PATTERN.finditer(nuspec)
            if match.group("id") == dependency
        ]
        if not ranges:
            errors.append(f"the {dependency} dependency is missing from the nuspec (lower bound {floor} expected)")
            continue
        wrong = sorted({value for value in ranges if value.replace(" ", "") not in (floor, f"[{floor},)")})
        if wrong:
            errors.append(
                f"the {dependency} lower bound is {', '.join(wrong)}; the shipped lower bound is {floor} - "
                "raising it forces consumers onto xunit.v3 4")
    return errors


def _license_file_error(root: pathlib.Path, nupkg: pathlib.Path) -> str | None:
    """None when the package root carries the repository's license text byte for byte."""
    expected_path = root / LICENSE_FILE
    if not expected_path.is_file():
        return f"{LICENSE_FILE} is missing from the repository root"
    with zipfile.ZipFile(nupkg) as archive:
        if LICENSE_FILE not in archive.namelist():
            return f"{LICENSE_FILE} is not INSIDE the package"
        packed = archive.read(LICENSE_FILE)
    if packed != expected_path.read_bytes():
        return f"{LICENSE_FILE} in the package differs from the repository's {LICENSE_FILE}"
    return None


def _third_party_notice_error(root: pathlib.Path, nupkg: pathlib.Path, source: pathlib.PurePath) -> str | None:
    """None when the package root carries the checked-in notice byte for byte."""
    expected_path = root / source
    if not expected_path.is_file():
        return f"{source} is missing - the license notice source is absent"
    with zipfile.ZipFile(nupkg) as archive:
        if THIRD_PARTY_NOTICE_NAME not in archive.namelist():
            return f"{THIRD_PARTY_NOTICE_NAME} is not at the package root"
        packed = archive.read(THIRD_PARTY_NOTICE_NAME)
    if packed != expected_path.read_bytes():
        return f"{THIRD_PARTY_NOTICE_NAME} in the package differs from {source}"
    return None


def _resolve_package_set(
    directory: pathlib.Path, project_ids: Sequence[str], requested_version: str | None
) -> tuple[dict[str, pathlib.Path], str] | None:
    """Every packable project's `.nupkg` in `directory`, on ONE version line.

    Prints the reason and returns None when a package is missing, the set
    spans two versions, the requested version was not produced, or a package
    outside `project_ids` sits next to them. The pack stage (`pack`) and the
    artifact stage (`_finish_release_rehearsal`) share this identity check."""
    resolved: dict[str, pathlib.Path] = {}
    missing: list[str] = []
    for project_id in project_ids:
        nupkg = _resolve_nupkg(directory, project_id, requested_version)
        if nupkg is None:
            missing.append(project_id)
        else:
            resolved[project_id] = nupkg

    if missing:
        print(f"❌ Package not produced: {', '.join(missing)}")
        return None

    versions = {project_id: _nupkg_version(project_id, nupkg) for project_id, nupkg in resolved.items()}
    distinct_versions = sorted(set(versions.values()))
    if len(distinct_versions) != 1:
        print("❌ The packages are not on one version line:")
        for project_id in sorted(versions):
            print(f"  {project_id}: {versions[project_id]}")
        return None

    resolved_version = distinct_versions[0]
    if requested_version and resolved_version != requested_version:
        print(f"❌ The requested version '{requested_version}' was not produced; produced: '{resolved_version}'")
        return None

    # Two-way comparison: not only a MISSING package, an unexpected (extra) package
    # must be caught too - e.g. a test project that became packable by accident.
    # `resolved` is filled only from `project_ids`, so it cannot see this on its own; the
    # set of REAL `.nupkg` files for `resolved_version` in the directory is scanned separately.
    produced_ids = {
        path.name[: -len(f".{resolved_version}.nupkg")]
        for path in directory.glob(f"*.{resolved_version}.nupkg")
    }
    unexpected = sorted(produced_ids - set(project_ids))
    if unexpected:
        print(f"❌ Unexpected package produced: {', '.join(unexpected)}")
        return None
    return resolved, resolved_version


def _package_file_names(resolved: Mapping[str, pathlib.Path]) -> dict[str, list[str]]:
    """`<id>` -> `[<nupkg>]` or `[<nupkg>, <snupkg>]`, in project order."""
    names: dict[str, list[str]] = {}
    for project_id in sorted(resolved):
        nupkg = resolved[project_id]
        names[project_id] = [nupkg.name]
        if nupkg.with_suffix(".snupkg").exists():
            names[project_id].append(nupkg.with_suffix(".snupkg").name)
    return names


def _package_manifest_entries(directory: pathlib.Path, file_names: Mapping[str, list[str]]) -> list[dict[str, str | None]]:
    entries: list[dict[str, str | None]] = []
    for project_id in sorted(file_names):
        names = file_names[project_id]
        entry: dict[str, str | None] = {
            "id": project_id,
            "file": names[0],
            "sha256": _sha256(directory / names[0]),
            "symbolsFile": None,
            "symbolsSha256": None,
        }
        if len(names) > 1:
            entry["symbolsFile"] = names[1]
            entry["symbolsSha256"] = _sha256(directory / names[1])
        entries.append(entry)
    return entries


# -----------------------------------------------------------------------------
# One build, one pack. `pack` packs the build CI just tested;
# `verify-packages` proves a directory still holds exactly what its manifest
# names; `release --package-directory` rehearses those files without packing again.
# -----------------------------------------------------------------------------


def _is_plain_relative(name: object) -> bool:
    """A manifest path is a plain relative POSIX path inside its directory."""
    if not isinstance(name, str) or not name or "\\" in name:
        return False
    path = pathlib.PurePosixPath(name)
    return not path.is_absolute() and ".." not in path.parts and "." not in path.parts


def _manifest_files(manifest: object) -> tuple[dict[str, str], list[str]]:
    """Every file a manifest names -> its SH, and the schema problems."""
    problems: list[str] = []
    files: dict[str, str] = {}
    if not isinstance(manifest, dict):
        return files, [f"{PACKAGE_MANIFEST_NAME}: the root is not a JSON object"]
    if manifest.get("dirty") is not False:
        problems.append(f"{PACKAGE_MANIFEST_NAME}: 'dirty' is not false")

    def add(owner: str, name: object, digest: object) -> None:
        if not _is_plain_relative(name) or not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest):
            problems.append(f"{PACKAGE_MANIFEST_NAME}: {owner} carries an invalid file/SHA-256 record")
        elif name in files:
            problems.append(f"{PACKAGE_MANIFEST_NAME}: {name} is named twice")
        else:
            files[name] = digest

    packages = manifest.get("packages")
    if not isinstance(packages, list) or not packages:
        problems.append(f"{PACKAGE_MANIFEST_NAME}: 'packages' is empty or not a list")
        packages = []
    for entry in packages:
        if not isinstance(entry, dict):
            problems.append(f"{PACKAGE_MANIFEST_NAME}: 'packages' holds an item that is not an object")
            continue
        owner = str(entry.get("id"))
        add(owner, entry.get("file"), entry.get("sha256"))
        if entry.get("symbolsFile") is not None or entry.get("symbolsSha256") is not None:
            add(owner, entry.get("symbolsFile"), entry.get("symbolsSha256"))

    records = manifest.get("apiCompat", [])
    if not isinstance(records, list):
        problems.append(f"{PACKAGE_MANIFEST_NAME}: 'apiCompat' is not a list")
        records = []
    for record in records:
        if not isinstance(record, dict):
            problems.append(f"{PACKAGE_MANIFEST_NAME}: 'apiCompat' holds an item that is not an object")
            continue
        if record.get("report") is not None or record.get("sha256") is not None:
            add(f"{record.get('id')} report", record.get("report"), record.get("sha256"))
    return files, problems


def _load_manifest(directory: pathlib.Path) -> tuple[object, str | None]:
    path = directory / PACKAGE_MANIFEST_NAME
    if not path.is_file():
        return None, f"{PACKAGE_MANIFEST_NAME} is missing"
    try:
        return json.loads(path.read_text(encoding="utf-8")), None
    except (OSError, ValueError) as exception:
        return None, f"{PACKAGE_MANIFEST_NAME} could not be read: {exception}"


def package_directory_problems(directory: pathlib.Path) -> list[str]:
    """Why `directory` does NOT hold exactly the files its manifest names.

    Every named file (package, symbols package, api-compat report) must
    exist with the recorded raw SH, and no `.nupkg`/`.snupkg` or report
    outside the manifest may sit in the directory. Raw SHA-256 is the right
    measure here - unlike `_content_fingerprint` - because the chain never
    packs twice: the bytes that were hashed are the bytes that are pushed."""
    if not directory.is_dir():
        return [f"{directory}: directory not found"]
    if not any(directory.iterdir()):
        return [f"{directory}: directory is empty"]
    manifest, error = _load_manifest(directory)
    if error:
        return [error]
    files, problems = _manifest_files(manifest)
    for name, digest in sorted(files.items()):
        path = directory / name
        if not path.is_file():
            problems.append(f"missing: {name}")
        elif _sha256(path) != digest:
            problems.append(f"SHA-256 differs: {name}")

    for path in sorted(directory.rglob("*")):
        relative = path.relative_to(directory).as_posix()
        is_package = path.suffix in PACKAGE_FILE_SUFFIXES
        is_report = path.suffix == ".xml" and path.parent.name == PACKAGE_REPORT_DIR_NAME
        if path.is_file() and (is_package or is_report) and relative not in files:
            problems.append(f"file not in the manifest: {relative}")
    return problems


def verify_package_directory(directory: pathlib.Path) -> int:
    problems = package_directory_problems(directory)
    if problems:
        print(f"❌ {directory} does not match its manifest:")
        for problem in problems:
            print(f"  {problem}")
        return 1
    manifest, _ = _load_manifest(directory)
    files, _ = _manifest_files(manifest)
    print(f"✅ {directory}: {len(files)} files match the SHA-256 values in the manifest")
    return 0


def release_version_line(version: str, *, publish: bool) -> tuple[str | None, str | None]:
    """The `$GITHUB_ENV` line that stamps a manually triggered run with its release version: `(line, error)`.

    An empty `version` is an ordinary run and needs no stamp, unless the run was asked to publish: a publishing
    run without a version has nothing to publish. The shape is the one `release --dry-run` enforces
    (`RELEASE_VERSION_PATTERN`), so the stamp and the rehearsal cannot disagree about what a release version is."""
    if not version:
        if publish:
            return None, "a run that publishes needs a release version, and the version input is empty"
        return None, None
    if not RELEASE_VERSION_PATTERN.fullmatch(version):
        return None, f"'{version}' is not a release version (expected 1.0.0-preview.N, no spaces)"
    return f"MinVerVersionOverride={version}", None


def stamp_release_version(version: str, publish: str, env_file: pathlib.Path | None) -> int:
    """`gate.py release-version`: validate the version input of a manual run and stamp the job's environment with it.

    MinVer reads `MinVerVersionOverride`, so every step after this one builds and packs that version. The value is
    validated before it is written: nothing but a release version reaches `$GITHUB_ENV`."""
    line, error = release_version_line(version, publish=publish.strip().lower() == "true")
    if error:
        print(f"❌ {error}")
        return 1
    if line is None:
        print("ℹ️ No release version was given: this is an ordinary run, the version is whatever MinVer computes.")
        return 0
    if env_file is not None:
        with env_file.open("a", encoding="utf-8", newline="\n") as stream:
            stream.write(line + "\n")
    print(f"✅ This run builds and packs version {version} ({line.split('=')[0]})")
    return 0


def _manifest_release(directory: pathlib.Path) -> tuple[str, str, list[str]] | None:
    """`(version, commit, package ids)` of a package directory's manifest, or `None` after printing why not."""
    manifest, error = _load_manifest(directory)
    if error:
        print(f"❌ {error}")
        return None
    entries = manifest.get("packages") if isinstance(manifest, dict) else None
    version = manifest.get("version") if isinstance(manifest, dict) else None
    commit = manifest.get("commit") if isinstance(manifest, dict) else None
    ids = [entry.get("id") for entry in entries if isinstance(entry, dict)] if isinstance(entries, list) else []
    if not (isinstance(version, str) and isinstance(commit, str) and ids and all(isinstance(i, str) for i in ids)):
        print(f"❌ {PACKAGE_MANIFEST_NAME} does not name a version, a commit and the packages")
        return None
    return version, commit, ids


def publish_guard(
    directory: pathlib.Path,
    *,
    fetcher: Callable[[str], tuple[int | None, str]] | None = None,
) -> int:
    """`gate.py publish-guard`: may these packages go to nuget.org, given what the version already has there?

    nuget.org has no way back. A second run of the same version builds new bytes, and `--skip-duplicate` would
    quietly mix two builds under one version. So: no package of the version there yet, or only packages of THIS
    commit (the failed job of the same run, run again), lets the push go on. A package of another commit stops it,
    and so does nuget.org not answering: a push that cannot be checked is not made."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import remote_state

    release = _manifest_release(directory)
    if release is None:
        return 1
    version, commit, ids = release

    state = remote_state.release_commits(ids, version, fetcher=fetcher or remote_state.fetch)
    if state.unreachable:
        print(f"❌ nuget.org could not say whether version {version} is already there "
              f"({len(state.unreachable)}/{len(ids)} packages could not be read): {', '.join(state.unreachable)}")
        print("   Nothing is pushed blind. Run the job again when nuget.org answers.")
        return 1

    foreign = state.foreign(commit)
    if foreign:
        print(f"❌ Version {version} is already on nuget.org from another build: this run packed commit {commit}, but")
        for package, named in sorted(foreign.items()):
            print(f"  {package} names {named or '(no commit)'}")
        print("   A new build is new bytes and the version is taken. The release is half done: cut the next number.")
        return 1

    if not state.commits:
        print(f"✅ Version {version}: none of the {len(ids)} packages is on nuget.org yet; the push may go on.")
    else:
        print(f"✅ Version {version}: {len(state.commits)} of {len(ids)} packages are already on nuget.org from this "
              "commit (a job run again); the push skips them.")
    return 0


def verify_published(
    version: str,
    commit: str,
    *,
    root: pathlib.Path = ROOT,
    fetcher: Callable[[str], tuple[int | None, str]] | None = None,
) -> int:
    """`gate.py verify-published`: is every package of `version` on nuget.org, and does each one name `commit`?

    The release page is opened only after this: it must describe packages that exist and were built from the
    commit the tag points at."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import remote_state

    ids = packable_project_ids(root)
    state = remote_state.release_commits(ids, version, fetcher=fetcher or remote_state.fetch)
    problems: list[str] = []
    if state.unreachable:
        problems.append(f"nuget.org could not be read for {', '.join(state.unreachable)}")
    if state.absent:
        problems.append(f"not on nuget.org: {', '.join(state.absent)}")
    problems.extend(f"{package} names {named or '(no commit)'}, not {commit}"
                    for package, named in sorted(state.foreign(commit).items()))
    if problems:
        print(f"❌ Version {version} is not published from commit {commit}:")
        for problem in problems:
            print(f"  {problem}")
        return 1
    print(f"✅ Version {version}: all {len(ids)} packages are on nuget.org and name commit {commit}")
    return 0


def _dirty_tree_rejected(*, require_git: bool) -> bool:
    """The early git rejection, shared by every pack entry point.

    Returns True (after printing why) when the working tree is dirty, or when
    git is unavailable and the caller cannot work without a commit."""
    status_lines = _git("status", "--porcelain")
    if status_lines is None:
        if require_git:
            print("❌ git was not found or this is not a git repository; the package cannot be tied to a commit")
            return True
        print("⚠️ git was not found or this is not a git repository; the working tree could not be checked for cleanliness here")
        return False
    if status_lines:
        print("❌ The working tree is not clean ('git status --porcelain'):")
        for line in status_lines:
            print(f"  {line}")
        print("A release rehearsal proves nothing on a dirty tree; commit or stash, then try again.")
        return True
    return False


def _head_commit() -> str:
    commit_lines = _git("rev-parse", "HEAD")
    return commit_lines[0] if commit_lines else "unknown"


def _library_ids(root: pathlib.Path, project_ids: Iterable[str]) -> list[str]:
    return [project_id for project_id in project_ids if _package_profile(root, project_id) == "library"]


def pack_command(
    output_dir: pathlib.Path | str,
    pack_properties: Sequence[str],
    *,
    no_build: bool,
) -> Command:
    """The ONE `dotnet pack` a release artifact comes from.

    `--no-build` packs the build the caller already tested (`pack`, CI);
    without it the pack builds first (`release --dry-run`, a human's rehearsal).
    Neither shape carries `TraconSkipCleanWorkingTreeCheck` or
    `TraconAllowDirtyPack`: a release candidate stays gated."""
    return Command((
        "dotnet", "pack", str(PACKABLE_SOLUTION_FILTER), "-c", "Release",
        *(("--no-build",) if no_build else ()),
        "-o", str(output_dir), *pack_properties,
    ))


@dataclasses.dataclass(frozen=True)
class PackedRelease:
    """What the pack stage hands the artifact stage."""

    baseline: str
    library_ids: tuple[str, ...]
    baseline_ids: tuple[str, ...]


def _pack_release(
    root: pathlib.Path,
    output_dir: pathlib.Path,
    report_dir: pathlib.Path,
    *,
    no_build: bool,
    requested_version: str | None,
) -> PackedRelease | int:
    """The pack stage: every check that needs `obj/`, the pack start time or
    the temporary baseline cache runs HERE, next to the pack.

    Returns an exit code on failure. The artifact stage
    (`_finish_release_rehearsal`) never repeats these - in `release-dryrun`
    there is no `obj/` to read."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes

    # Baseline: the last published `v*` version. It is resolved BEFORE the pack,
    # because the pack needs the baseline path. Without git the cleanliness check is
    # skipped with a warning (the MSBuild gate is the second line); the baseline has no
    # second line, so there is no silent skip here.
    try:
        baseline = breaking_changes.resolve_baseline(root)
    except breaking_changes.BaselineError as exception:
        print(f"❌ The baseline could not be resolved: {exception}")
        return 1
    print(f"Baseline: v{baseline} (git describe)")

    stale_flags = breaking_changes.stale_first_release_flags(root, f"{breaking_changes.TAG_PREFIX}{baseline}")
    if stale_flags:
        print(f"❌ The first-release flag is stale - the package already existed at tag v{baseline}; "
              f"remove TraconPackageFirstRelease: {', '.join(stale_flags)}")
        return 1

    library_ids = _library_ids(root, packable_project_ids(root))
    baseline_ids = breaking_changes.baseline_package_ids(root, library_ids)
    # The baseline cache lives OUTSIDE the repo: the developer's cache is never read.
    baseline_work = pathlib.Path(tempfile.mkdtemp(prefix="tracon-baseline-"))
    try:
        try:
            baseline_cache = breaking_changes.restore_baselines(baseline_ids, baseline, baseline_work)
        except breaking_changes.RestoreError as exception:
            print(f"❌ The baseline restore failed: {exception}")
            return 1
        source_violations = breaking_changes.baseline_source_violations(baseline_ids, baseline, baseline_cache)
        if source_violations:
            print("❌ The baseline package did not come from nuget.org:")
            for violation in source_violations:
                print(f"  {violation}")
            return 1
        print(f"Baseline packages from the isolated cache (source api.nuget.org): {len(baseline_ids)}")

        environment = os.environ.copy()
        environment["MSBUILDDISABLENODEREUSE"] = "1"
        if requested_version:
            environment["MinVerVersionOverride"] = requested_version

        command = pack_command(
            output_dir, breaking_changes.pack_properties(baseline_cache, baseline, report_dir), no_build=no_build)
        print(f"$ {command.display}" + (f"  (MinVerVersionOverride={requested_version})" if requested_version else ""), flush=True)
        pack_started = time.time()
        pack_result = subprocess.run(list(command.args), cwd=root, env=environment, check=False)
        if pack_result.returncode:
            print(f"❌ 'dotnet pack' exited with {pack_result.returncode}")
            return pack_result.returncode
    finally:
        shutil.rmtree(baseline_work, ignore_errors=True)

    # The evidence is computed in the process that runs the pack: the semaphore sits in this
    # machine's artifacts/obj/. It is NOT moved to the artifact stage -
    # `release-dryrun` has no obj/; the result goes into the `pack` manifest.
    not_run = breaking_changes.validation_not_run(root, library_ids, pack_started)
    if not_run:
        print("❌ The package validation did not run in this pack:")
        for line in not_run:
            print(f"  {line}")
        return 1
    return PackedRelease(baseline=baseline, library_ids=tuple(library_ids), baseline_ids=tuple(baseline_ids))


def rehearsal_release_dir(version: str, rehearsal_root: pathlib.Path | None = None) -> pathlib.Path:
    """`release --dry-run` pack-mode output: one directory per version.

    Never `release/`: that is the development feed every `dotnet pack`, the
    closing gate and the package tests write into, so a rehearsal promoted
    there met every older version and its sample gate failed after minutes of
    packing. Nothing is deleted; a directory of its own leaves nothing to
    delete. Inside it the same rule still holds: an `<id, version>` pair names one
    artifact, so a NEW commit rehearsed under the same version is refused and
    the existing file stays; the same commit twice is a no-op."""
    return (rehearsal_root or PACKAGE_REHEARSAL_ROOT) / version


def release_rehearsal(
    requested_version: str | None,
    *,
    root: pathlib.Path = ROOT,
    release_dir: pathlib.Path | None = None,
) -> int:
    project_ids = packable_project_ids(root)

    # Early rejection: the working tree is checked BEFORE a `dotnet pack` that
    # takes minutes starts. The MSBuild gate (TraconValidateCleanWorkingTree) would
    # give the same result anyway; this step only brings the feedback forward.
    # It is unconditional - a release rehearsal proves nothing on a dirty tree,
    # so there is NO override here that corresponds to TraconAllowDirtyPack.
    # If git cannot be found (a source tarball, git not on PATH) the gate is
    # SKIPPED - `dotnet pack` tries the same check again through its own MSBuild gate;
    # this is the second line of defense, not the only one.
    if _dirty_tree_rejected(require_git=False):
        return 1
    # The tag run runs the same scan and a published tag cannot be taken back.
    # Everything the scan would fail on (a drifted version tag included) shows up
    # here before the tag exists; the pack that takes minutes is not started.
    if scan(root) != 0:
        return 1
    commit = _head_commit()

    # The result of an earlier green run must not mislead if this run ends red.
    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes

    (root / breaking_changes.RESULT_FILE).unlink(missing_ok=True)

    # The staging directory is unique per run (parallel runs are not
    # supported, but this at least keeps two of them from overwriting EACH OTHER's output).
    # `artifacts/` is in .gitignore - if staging lives there it does not dirty the next
    # run's own "early rejection" check. The report directory is per run for the same
    # reason; also, an output file that disappears makes the SDK re-run its incremental
    # validation target (measured).
    package_root = release_dir.parent if release_dir else PACKAGE_RELEASE_DIR.parent
    staging_root = package_root / "staging"
    staging_root.mkdir(parents=True, exist_ok=True)
    staging_dir = pathlib.Path(tempfile.mkdtemp(prefix="run-", dir=staging_root))
    report_root = package_root / PACKAGE_REPORT_DIR_NAME
    report_root.mkdir(parents=True, exist_ok=True)
    report_dir = pathlib.Path(tempfile.mkdtemp(prefix="run-", dir=report_root))

    try:
        packed = _pack_release(root, staging_dir, report_dir, no_build=False, requested_version=requested_version)
        if isinstance(packed, int):
            return packed

        if release_dir is None:
            package_set = _resolve_package_set(staging_dir, project_ids, requested_version)
            if package_set is None:
                return 1
            release_dir = rehearsal_release_dir(package_set[1])
            print(f"📦 Rehearsal output: {release_dir.relative_to(root) if release_dir.is_relative_to(root) else release_dir}")

        return _finish_release_rehearsal(
            root=root,
            release_dir=release_dir,
            staging_dir=staging_dir,
            project_ids=project_ids,
            requested_version=requested_version,
            commit=commit,
            breaking_gate=BreakingChangeGate(baseline=packed.baseline, report_dir=report_dir),
        )
    finally:
        shutil.rmtree(staging_dir, ignore_errors=True)
        shutil.rmtree(report_dir, ignore_errors=True)


def pack_for_ci(output_dir: pathlib.Path, *, root: pathlib.Path = ROOT) -> int:
    """`gate.py pack` - packs the build the caller already tested.

    `output_dir` must be missing or empty: an interrupted run leaves a
    manifest-less directory that the next run REFUSES instead of deleting
    (a partial set must never be mistaken for a verified one). The manifest
    is written last and records, besides every package's raw SH, the
    baseline and one api-compat record per library package with a baseline -
    the pack-time proof `release --package-directory` cannot recompute."""
    if output_dir.exists() and (not output_dir.is_dir() or any(output_dir.iterdir())):
        print(f"❌ The output directory is not empty: {output_dir} - nothing was deleted; give an empty or new directory")
        return 1
    if _dirty_tree_rejected(require_git=True):
        return 1
    commit = _head_commit()
    project_ids = packable_project_ids(root)

    output_dir.mkdir(parents=True, exist_ok=True)
    report_dir = output_dir / PACKAGE_REPORT_DIR_NAME
    report_dir.mkdir()
    packed = _pack_release(root, output_dir, report_dir, no_build=True, requested_version=None)
    if isinstance(packed, int):
        return packed

    package_set = _resolve_package_set(output_dir, project_ids, None)
    if package_set is None:
        return 1
    resolved, version = package_set

    api_compat: list[dict[str, object]] = []
    for project_id in sorted(packed.baseline_ids):
        report = report_dir / f"{project_id}.xml"
        # For a package with no difference the SDK does NOT write a report. "No report" is a
        # legitimate value; the semaphore proved that the validation ran (above).
        api_compat.append({
            "id": project_id,
            "report": report.relative_to(output_dir).as_posix() if report.exists() else None,
            "sha256": _sha256(report) if report.exists() else None,
            "validationRan": True,
        })

    manifest_path = _write_manifest(
        output_dir,
        version=version,
        commit=commit,
        baseline=packed.baseline,
        packages=_package_manifest_entries(output_dir, _package_file_names(resolved)),
        api_compat=api_compat,
    )
    reports = sum(1 for record in api_compat if record["report"])
    print(f"📄 {manifest_path} written: {len(resolved)} packages, version '{version}', "
          f"{len(api_compat)} api-compat records ({reports} reports)")
    return 0


def _report_record_problems(manifest: Mapping[str, object], expected_ids: Iterable[str]) -> list[str]:
    """Every expected library package carries a record that says validation
    ran. The expected set comes from the rehearsal's OWN checkout, never from
    the manifest: an empty `api-compat/` is not "no breaking change"."""
    records = {
        record.get("id"): record
        for record in manifest.get("apiCompat", []) or []
        if isinstance(record, dict)
    }
    problems = []
    expected = sorted(expected_ids)
    for project_id in expected:
        record = records.get(project_id)
        if record is None:
            problems.append(f"report missing: {project_id}")
        elif record.get("validationRan") is not True:
            problems.append(f"report missing: {project_id} (no record that validation ran)")
    for project_id in sorted(set(records) - set(expected), key=str):
        problems.append(f"unexpected api-compat record: {project_id}")
    return problems


def rehearse_package_directory(
    package_dir: pathlib.Path,
    requested_version: str | None,
    *,
    root: pathlib.Path = ROOT,
    release_dir: pathlib.Path | None = None,
) -> int:
    """`gate.py release --dry-run --package-directory` - rehearses packages it did NOT
    produce. `dotnet pack` never runs; `package_dir` is never modified."""
    release_dir = release_dir or PACKAGE_RELEASE_DIR
    if _dirty_tree_rejected(require_git=True):
        return 1
    commit = _head_commit()

    if verify_package_directory(package_dir):
        return 1
    manifest, _ = _load_manifest(package_dir)
    assert isinstance(manifest, dict)  # package_directory_problems proved it

    if manifest.get("commit") != commit:
        print(f"❌ The packages come from another commit: manifest '{manifest.get('commit')}', HEAD '{commit}'")
        return 1

    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes

    try:
        baseline = breaking_changes.resolve_baseline(root)
    except breaking_changes.BaselineError as exception:
        print(f"❌ The baseline could not be resolved: {exception}")
        return 1
    print(f"Baseline: v{baseline} (git describe)")
    if manifest.get("baseline") != baseline:
        print(f"❌ The packages were validated against another baseline: manifest 'v{manifest.get('baseline')}', this checkout 'v{baseline}'")
        return 1

    project_ids = packable_project_ids(root)
    problems = _report_record_problems(
        manifest, breaking_changes.baseline_package_ids(root, _library_ids(root, project_ids)))
    if problems:
        print("❌ The proof of the breaking-change report is missing:")
        for problem in problems:
            print(f"  {problem}")
        return 1

    # Reject if `release_dir` holds packages outside the input. It is said before the
    # copy starts; no file is deleted.
    input_files = {entry["file"] for entry in manifest["packages"]} | {
        entry["symbolsFile"] for entry in manifest["packages"] if entry.get("symbolsFile")}
    foreign = sorted(
        path.name for path in release_dir.glob("*")
        if path.suffix in PACKAGE_FILE_SUFFIXES and path.name not in input_files
    ) if release_dir.is_dir() else []
    if foreign:
        print(f"❌ {release_dir} holds packages outside the input ({len(foreign)}): {', '.join(foreign[:5])}"
              + (" …" if len(foreign) > 5 else ""))
        print(f"   Old local versions are not cleaned up; if you need to: rm -rf {release_dir}")
        return 1

    (root / breaking_changes.RESULT_FILE).unlink(missing_ok=True)
    return _finish_release_rehearsal(
        root=root,
        release_dir=release_dir,
        staging_dir=package_dir,
        project_ids=project_ids,
        requested_version=requested_version,
        commit=commit,
        breaking_gate=BreakingChangeGate(baseline=baseline, report_dir=package_dir / PACKAGE_REPORT_DIR_NAME),
        input_manifest=manifest,
    )


@dataclasses.dataclass(frozen=True)
class BreakingChangeGate:
    """What the breaking-change gate needs from the pack it follows.

    Only artifact-time input: the pack-time proof (semaphore) was checked by
    `_pack_release` and never travels here."""

    baseline: str
    report_dir: pathlib.Path


def _check_breaking_changes(root: pathlib.Path, gate: BreakingChangeGate, resolved_version: str) -> int:
    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes

    if not breaking_changes.version_is_above_baseline(resolved_version, gate.baseline):
        print(f"❌ The resolved version '{resolved_version}' is not above the baseline (v{gate.baseline}); "
              "comparing a version with itself would give a silent 'no breaking changes'")
        return 1

    result = breaking_changes.check(
        report_dir=gate.report_dir,
        baseline=gate.baseline,
        version=resolved_version,
        changelog=root / "CHANGELOG.md",
        result_path=root / breaking_changes.RESULT_FILE,
        repo=root,
    )
    if result:
        return result

    # ApiCompat reads .NET assemblies; the comparison with the last release for HTTP, SSE,
    # telemetry, configuration, error codes and the CLI is a separate reader and runs AFTER
    # ApiCompat (the release-line statement is printed twice; the second belongs to these surfaces).
    import contract_breaks

    return contract_breaks.check(
        repo=root,
        baseline=gate.baseline,
        version=resolved_version,
        changelog=root / "CHANGELOG.md",
    )


_MANIFEST_COMPARED_FIELDS = ("version", "commit", "dirty", "baseline", "packages")


def _release_cut_error(changelog_path: pathlib.Path, version: str, baseline: str) -> str | None:
    """Why `CHANGELOG.md` does not back a release of `version` that is about to be published, or `None`.

    A release is cut before it is published: its section carries a date, and it is the one cut waiting for its tag.
    The given version, the changelog and the packages must name the same release; the packages are compared
    with the version by the caller."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes
    import changelog as changelog_module

    if version not in changelog_module.released_versions(changelog_path.read_text(encoding="utf-8")):
        return (f"CHANGELOG.md has no dated '## [{version}] - <date>' section: cut the release first, "
                "because the tag is placed on the commit these packages were built from")
    cut = breaking_changes.pending_cut(changelog_path, baseline)
    if cut is not None and cut != version:
        return f"The cut waiting for its tag is '{cut}', not '{version}': the version input and CHANGELOG.md disagree"
    return None


def _finish_release_rehearsal(
    *,
    root: pathlib.Path,
    release_dir: pathlib.Path,
    staging_dir: pathlib.Path,
    project_ids: list[str],
    requested_version: str | None,
    commit: str,
    breaking_gate: BreakingChangeGate,
    input_manifest: Mapping[str, object] | None = None,
) -> int:
    """The artifact stage, shared by both rehearsal modes. It reads only the
    package directory, git and `CHANGELOG.md`.

    `input_manifest` marks the package-directory mode: `staging_dir` is the
    verified input, so files are COPIED (never moved) and the manifest written
    to `release_dir` must equal the input's field by field."""
    try:
        return _finish_release_rehearsal_unchecked(
            root=root, release_dir=release_dir, staging_dir=staging_dir, project_ids=project_ids,
            requested_version=requested_version, commit=commit, breaking_gate=breaking_gate,
            input_manifest=input_manifest)
    except PackageFileError as exception:
        print(f"❌ {exception}")
        return 1


def _finish_release_rehearsal_unchecked(
    *,
    root: pathlib.Path,
    release_dir: pathlib.Path,
    staging_dir: pathlib.Path,
    project_ids: list[str],
    requested_version: str | None,
    commit: str,
    breaking_gate: BreakingChangeGate,
    input_manifest: Mapping[str, object] | None,
) -> int:
    package_set = _resolve_package_set(staging_dir, project_ids, requested_version)
    if package_set is None:
        return 1
    resolved, resolved_version = package_set
    versions = {project_id: _nupkg_version(project_id, nupkg) for project_id, nupkg in resolved.items()}

    if not RELEASE_VERSION_PATTERN.fullmatch(resolved_version):
        print(
            f"⚠️ The version does not match the '1.0.0-preview.N' pattern: '{resolved_version}' "
            "(expected in an untagged run; not an error unless --package-version forced it)"
        )
    else:
        # Enforced only in a run that resolves to a REAL version shape - either a local
        # rehearsal by a person with `--package-version`, or a real `v*` tag. Every
        # routine CI run without a tag takes the branch above and never sees this gate.
        sys.path.insert(0, str(ROOT / "scripts"))
        import changelog

        changelog_path = root / "CHANGELOG.md"
        if not changelog_path.exists():
            print("❌ CHANGELOG.md not found")
            return 1
        # The version section is written at TAG time: the `## [Unreleased]` heading is
        # turned into the version and the ship date. Until then the notes of the next
        # version sit under Unreleased, so the rehearsal reads them from there. What the
        # gate protects is not "is there a section" but that NO VERSION SHIPS WITHOUT NOTES.
        _, notes_heading = changelog.read_release_notes(changelog_path, resolved_version)
        if notes_heading is None:
            print(
                f"❌ CHANGELOG.md has neither a '## [{resolved_version}]' nor a "
                "'## [Unreleased]' section; both are missing or empty"
            )
            return 1
        if notes_heading == changelog.UNRELEASED:
            print(
                f"ℹ️ The release notes were read from the '## [Unreleased]' section; "
                f"when 'v{resolved_version}' is cut, this heading is renamed "
                f"to '## [{resolved_version}] - <date>' "
                "(release notes rule)"
            )
        # Packages that are about to be published (a given version over a package directory) must have been
        # cut: the tag that follows the publish is placed on a commit whose CHANGELOG carries the dated section.
        if input_manifest is not None and requested_version:
            cut_error = _release_cut_error(changelog_path, requested_version, breaking_gate.baseline)
            if cut_error:
                print(f"❌ {cut_error}")
                return 1

    errors: list[str] = []
    for project_id in sorted(resolved):
        nupkg = resolved[project_id]
        try:
            entries = _entry_names(nupkg)
            nuspec = _read_nuspec(nupkg, project_id)
        except (zipfile.BadZipFile, KeyError, OSError) as exception:
            errors.append(f"{project_id}: {nupkg.name} could not be read ({exception})")
            continue
        profile = _package_profile(root, project_id)

        if "icon.png" not in entries:
            errors.append(f"{project_id}: icon.png is missing")
        if "README.md" not in entries:
            errors.append(f"{project_id}: README.md is missing")
        # The license is verified from three sides at once: what the .nuspec DECLARES, that
        # it asks for no acceptance prompt, and the text INSIDE the package. A project file
        # that overrides one of them would otherwise ship terms the others do not state.
        if f'<license type="expression">{LICENSE_EXPRESSION}</license>' not in nuspec:
            errors.append(
                f"{project_id}: the .nuspec does not carry "
                f"'<license type=\"expression\">{LICENSE_EXPRESSION}</license>'")
        if '<license type="file">' in nuspec:
            errors.append(f"{project_id}: the .nuspec declares a license file; the license is an expression")
        # Measured: NuGet writes requireLicenseAcceptance only when it is TRUE; false is
        # the default and the element never shows up.
        if "<requireLicenseAcceptance>true</requireLicenseAcceptance>" in nuspec:
            errors.append(f"{project_id}: an MIT package must not ask for requireLicenseAcceptance")
        license_error = _license_file_error(root, nupkg)
        if license_error is not None:
            errors.append(f"{project_id}: {license_error}")
        if RETIRED_LICENSE_FILE in entries:
            errors.append(f"{project_id}: the retired license file is packed: {RETIRED_LICENSE_FILE}")
        # Not only EXISTENCE, the VALUE too. In a PR two jobs check out the same merge
        # commit; a package that comes from another commit fails here. Without git
        # (commit 'unknown') only existence is checked.
        repository_match = REPOSITORY_COMMIT_PATTERN.search(nuspec)
        if repository_match is None:
            errors.append(f"{project_id}: the repository/commit metadata is missing")
        elif commit != "unknown" and repository_match.group("commit") != commit:
            errors.append(
                f"{project_id}: the repository commit '{repository_match.group('commit')}' is not HEAD ('{commit}')")

        release_notes_match = RELEASE_NOTES_PATTERN.search(nuspec)
        if release_notes_match is None:
            errors.append(f"{project_id}: releaseNotes is missing")
        elif "$(Version)" in release_notes_match.group("url"):
            errors.append(f"{project_id}: releaseNotes holds a raw $(Version) - not resolved")
        elif resolved_version not in release_notes_match.group("url"):
            errors.append(f"{project_id}: releaseNotes does not carry the resolved version ('{resolved_version}')")

        # `content` and `meta` compile nothing (IncludeBuildOutput=false), so there is no
        # .pdb to make symbols from; both write IncludeSymbols=false. In `content` an empty
        # symbols package breaks the build with NU5017; in `meta` it did NOT break (the
        # nuspec lists dependencies) and a .snupkg with 0 PDBs shipped silently - measured
        # 2026-09-15.
        snupkg_exists = nupkg.with_suffix(".snupkg").exists()
        if profile in ("content", "meta"):
            if snupkg_exists:
                errors.append(f"{project_id}: a package with no build output must NOT carry a .snupkg")
        elif not snupkg_exists:
            errors.append(f"{project_id}: .snupkg is missing")

        if profile in ("library", "tool"):
            expected_xml_count = len(_target_frameworks(root, project_id)) if profile == "library" else 1
            xml_count = sum(1 for name in entries if name.endswith(f"/{project_id}.xml"))
            if xml_count != expected_xml_count:
                errors.append(f"{project_id}: expected {expected_xml_count} XML doc files, found {xml_count}")

        prerelease = [
            (match.group("id"), match.group("range"))
            for match in PRERELEASE_DEPENDENCY_PATTERN.finditer(nuspec)
            if not match.group("id").startswith("Tracon")
        ]
        if project_id != PRERELEASE_MAF_EXEMPT_PACKAGE:
            offenders = sorted({dependency for dependency, _ in prerelease})
            if offenders:
                errors.append(f"{project_id}: the prerelease boundary was violated - prerelease dependencies: {', '.join(offenders)}")
        # A prerelease upstream breaks between its own previews. An open lower bound
        # (`version="x"` = `>= x`) let a consumer's upgrade restore without a warning
        # and then fail at run time; with an exact range NuGet reports it at
        # restore (NU1608 / NU1107).
        open_ranges = sorted({
            f"{dependency} {version_range}"
            for dependency, version_range in prerelease
            if not EXACT_RANGE_PATTERN.fullmatch(version_range)
        })
        if open_ranges:
            errors.append(f"{project_id}: a prerelease dependency is not an exact range: {', '.join(open_ranges)}")

        errors.extend(f"{project_id}: {error}" for error in dependency_floor_errors(project_id, nuspec))

        notice_source = THIRD_PARTY_NOTICE_PACKAGES.get(project_id)
        if notice_source is not None:
            notice_error = _third_party_notice_error(root, nupkg, notice_source)
            if notice_error:
                errors.append(f"{project_id}: {notice_error}")

    if errors:
        print("❌ The metadata/prerelease contract was violated:")
        for error in errors:
            print(f"  {error}")
        return 1

    # BEFORE the promote: a red breaking-change gate leaves no package in the release
    # directory. Reading the notes needs the resolved version, so it runs AFTER version
    # resolution. It reads on every run (not only a version-shaped one): it allows a
    # breaking change, it does not allow shipping one unannounced.
    breaking_result = _check_breaking_changes(root, breaking_gate, resolved_version)
    if breaking_result:
        return breaking_result

    # Overwrite protection and the manifest. Packages validated in staging are moved
    # to release_dir only here (promote); if `_promote_staged_packages` finds a DIFFERENT
    # SHA-256 under the same name it moves NO file and the existing release_dir stays as
    # it is (all or nothing). In package-directory mode it copies instead of moving - the
    # input is the artifact CI validated.
    staged_file_names = _package_file_names(resolved)
    all_file_names = [name for names in staged_file_names.values() for name in names]
    conflicts = _promote_staged_packages(staging_dir, release_dir, all_file_names, copy=input_manifest is not None)
    if conflicts:
        print("❌ An artifact with the same identity (id+version) but DIFFERENT content already exists - the existing file was kept:")
        for name in conflicts:
            print(f"  {name}")
        if input_manifest is None:
            print(f"   If you rehearse this version from a new commit on purpose, remove the old candidate: rm -rf {release_dir}")
        return 1

    packages_manifest = _package_manifest_entries(release_dir, staged_file_names)
    manifest_path = _write_manifest(
        release_dir, version=resolved_version, commit=commit, baseline=breaking_gate.baseline, packages=packages_manifest)
    print(f"📄 {manifest_path.relative_to(root) if manifest_path.is_relative_to(root) else manifest_path} "
          f"written ({len(packages_manifest)} packages)")

    if input_manifest is not None:
        written = json.loads(manifest_path.read_text(encoding="utf-8"))
        differing = [field for field in _MANIFEST_COMPARED_FIELDS if written.get(field) != input_manifest.get(field)]
        if differing:
            print(f"❌ The manifest in {release_dir} differs from the input manifest: {', '.join(differing)}")
            print(f"   An old copy with the same name may remain; if you need to: rm -rf {release_dir}")
            return 1
        print(f"✅ The manifest matches the input: {len(packages_manifest)} packages, version, commit, baseline")
        if verify_package_directory(release_dir):
            return 1

    print(f"✅ {len(resolved)} packages, version '{resolved_version}':")
    for project_id in sorted(resolved):
        print(f"  {project_id}  {versions[project_id]}")

    npm_result = _npm_dry_run()
    if npm_result:
        return npm_result

    # Every extension sample in the inventory and the AOT smoke run against the packages
    # this run produced, at the one exact version, with an isolated NUGET_PACKAGES cache
    # (the inventory gate). Like `dotnet pack`/npm, it writes nothing to the network.
    sys.path.insert(0, str(ROOT / "scripts"))
    import release_extension_samples

    return release_extension_samples.verify(root, release_dir, resolved_version)


def _npm_dry_run() -> int:
    if shutil.which("npm") is None:
        print("⚠️ npm was not found; 'npm publish --dry-run' was SKIPPED")
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
        print(f"⚠️ 'npm publish --dry-run' could not run (there may be no network access) - SKIPPED: {exception}")
        return 0

    if result.returncode:
        print(f"❌ 'npm publish --dry-run' exited with {result.returncode}")
        return result.returncode

    print("✅ npm publish --dry-run")
    return 0


def capacity_measurement(profile: str, version: str, output: pathlib.Path) -> int:
    """Run the capacity measurement.

    🚨 This is NOT A GATE. Load and fault measurements are reports, and no
    threshold is tied to the time; `close` does not call this command and no
    profile is added to the standard close, the PR path or the release line.
    The measurement logic lives in `scripts/capacity.py`; only the command
    surface is gathered here, in one place."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import capacity  # noqa: PLC0415 - lazy import for the command surface

    try:
        return capacity.measure(profile, version, output)
    except capacity.CapacityError as error:
        print(f"❌ {error}")
        return 1


STABILITY_WORKFLOW = "stability.yml"
"""The scheduled workflow of the public repository that runs `stability` (see `.github/workflows`)."""


def _tag_exists(root: pathlib.Path, tag: str) -> bool:
    try:
        return subprocess.run(
            ["git", "rev-parse", "-q", "--verify", f"refs/tags/{tag}"], cwd=root, capture_output=True, check=False,
        ).returncode == 0
    except OSError:
        return False


def _untagged_release_state(
    root: pathlib.Path, baseline: str, tag_hint: Callable[[str], str],
) -> int:
    """A cut waits for its tag: say whether it is still waiting to be published, or published and left untagged.

    Fails only for a proven gap: nuget.org answered and has packages of the cut, yet the tag does not exist.
    Nothing on nuget.org yet is a plain fact (the release is cut and the publish run comes next); a nuget.org that
    does not answer is a warning."""
    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes
    import remote_state

    changelog_path = root / "CHANGELOG.md"
    cut = breaking_changes.pending_cut(changelog_path, baseline) if changelog_path.is_file() else None
    tag = f"{breaking_changes.TAG_PREFIX}{cut}"
    if cut is None or _tag_exists(root, tag):
        return 0

    package_ids = packable_project_ids(root)
    state = remote_state.release_state(package_ids, cut)

    if state.published:
        print(f"❌ {tag} is on nuget.org ({state.published}/{len(package_ids)} packages) but has no tag. {tag_hint(cut)}")
        if state.missing:
            print(f"   {len(state.missing)} packages are not there: the publish run stopped half way; run its failed job again "
                  f"(a new run of the same version would build new bytes): {', '.join(state.missing)}")
        return 1
    if state.unreachable:
        print(f"⚠️ {tag} is cut and has no tag; nuget.org was not reachable ({len(state.unreachable)}/{len(package_ids)} "
              "packages could not be read), so it is not known whether it was published.")
        return 0
    print(f"ℹ️ {tag} is cut and waits to be published: none of its packages is on nuget.org yet, and it has no tag.")
    return 0


def remote_state_check(
    root: pathlib.Path = ROOT,
    ci_remote: str = "origin",
    tag_hint: Callable[[str], str] = _generic_tag_hint,
) -> int:
    """Reads what only the remote knows: did the last release tag reach nuget.org, and what did the last CI run say.

    Fails only for a proven gap: nuget.org answered and a package the release tag should have pushed is not
    there, or a cut release is on nuget.org and has no tag. Everything the run cannot read (no network, no `gh`)
    is a warning, so a closing run offline still finishes, and a red CI run is a warning because the branch may
    be mid-repair.
    """
    sys.path.insert(0, str(ROOT / "scripts"))
    import breaking_changes
    import remote_state

    exit_code = 0

    try:
        version = breaking_changes.resolve_baseline(root)
    except breaking_changes.BaselineError as exception:
        print(f"⚠️ The last release tag could not be resolved; the nuget.org check was skipped: {exception}")
        version = None

    if version is not None:
        # The set the tag held, not the working tree's: a project added since is not part of that release.
        package_ids = remote_state.packable_ids_at(root, f"{breaking_changes.TAG_PREFIX}{version}")

        if not package_ids:
            print(f"⚠️ The tree of tag v{version} could not be read; the package set came from the working tree.")
            package_ids = packable_project_ids(root)

        state = remote_state.release_state(package_ids, version)

        if state.missing:
            print(f"❌ Tag v{version} exists but {len(state.missing)} packages are missing on nuget.org: {', '.join(state.missing)}")
            print("   The tag run may have stopped half way; check the related Actions run. The tag does not move: a fix ships under the next number.")
            exit_code = 1

        if state.unreachable:
            print(f"⚠️ nuget.org was not reachable ({len(state.unreachable)}/{len(package_ids)} packages could not be read); the release check is incomplete.")

        if not state.missing and not state.unreachable:
            print(f"✅ v{version}: all {state.published} packages are on nuget.org")

        exit_code = max(exit_code, _untagged_release_state(root, version, tag_hint))

    ci = remote_state.ci_state(root, ci_remote)

    if ci is None:
        print(f"ℹ️ No decided CI run could be read for '{ci_remote}' (no gh, no session or access, no network, or all runs were skipped).")
    elif ci.is_red:
        print(f"⚠️ The last CI run of '{ci_remote}' is red ({ci.conclusion}, {ci.sha}): {ci.url}")
    elif ci.status == "completed" and ci.conclusion == "success":
        print(f"✅ The last CI run of '{ci_remote}' is green ({ci.sha})")
    else:
        print(f"ℹ️ The last CI run of '{ci_remote}' has not decided yet: {ci.status} {ci.conclusion}".rstrip() + f" ({ci.sha})")

    # The weekly stability run (`stability.yml`) is a warning only: a red one means the tests are unstable.
    stability = remote_state.ci_state(root, ci_remote, workflow=STABILITY_WORKFLOW)

    if stability is None:
        print(f"ℹ️ No decided stability run could be read for '{ci_remote}' (it has not run yet, or there is no gh or no network).")
    elif stability.is_red:
        print(f"⚠️ The last stability run of '{ci_remote}' is red ({stability.conclusion}, {stability.sha}): {stability.url}")
        print("   The tests are unstable: fix the instability first (the report is in the `stability-report` artifact).")
    elif stability.status == "completed" and stability.conclusion == "success":
        print(f"✅ The last stability run of '{ci_remote}' is green ({stability.sha})")
    else:
        print(f"ℹ️ The last stability run of '{ci_remote}' has not decided yet: {stability.status} {stability.conclusion}".rstrip())

    return exit_code


RENAMED_COMMANDS = {
    "tarama": "scan",
    "uret": "generate",
    "uzak-durum": "remote-state",
    "ic-dongu": "inner-loop",
    "kapanis": "close",
    "performans": "perf",
    "kararlilik": "stability",
    "yayin": "release",
    "paketle": "pack",
    "paket-dogrula": "verify-packages",
    "kapasite": "capacity",
}
"""Old sub-command name -> new name. An old name is not an alias: it fails and says the new name."""


class _CommandLineParser(argparse.ArgumentParser):
    """No flag abbreviations: an old flag name must not work as a short form of its new name."""

    def __init__(self, **options: Any) -> None:
        super().__init__(allow_abbrev=False, **options)


def build_parser() -> tuple[argparse.ArgumentParser, Any]:
    """The command line: the parser and its sub-command action (its `choices` hold the command names)."""
    parser = _CommandLineParser(description=__doc__)
    parser.add_argument("--print-commands", action="store_true", help="list the commands without running them")
    subparsers = parser.add_subparsers(dest="stage")

    subparsers.add_parser(
        "scan",
        help="scan for sync copies, secrets, migrations, stale doc references, public types without evidence "
             "and the Context7 version tag")
    subparsers.add_parser("generate", help="regenerate the generated files: the agent map and the document indexes")
    subparsers.add_parser(
        "remote-state",
        help="are the packages of the last release tag on nuget.org, and what did the last CI run say (read only)")
    inner_loop = subparsers.add_parser("inner-loop", help="build and the affected test projects")
    inner_loop.add_argument(
        "--base", default="HEAD",
        help="base of the change range; to also see committed work, pass the commit before your work")
    closing = subparsers.add_parser("close", help="the full validation gates")
    closing.add_argument("--base", required=True, help="the commit before your work (SHA)")
    closing.add_argument("--skip-site", action="store_true", help="skip the docs-site gate")
    perf = subparsers.add_parser("perf", help="allocation gate - measures the four hot paths")
    perf.add_argument(
        "--update", action="store_true",
        help="replace bench/baseline.json with the result of this run (no comparison)")
    test = subparsers.add_parser("test", help="one test subset by MTP filter")
    # extend: `--class A B` and `--class A --class B` are the same. The default behavior
    # would silently overwrite the first flag with the second (measured in one close run).
    test.add_argument("--class", dest="classes", nargs="+", action="extend", required=True, help="class patterns")
    test.add_argument("--project", default="Tracon.Core.UnitTests", help="test project name")
    test.add_argument(
        "--tfm",
        help="one leg of a multi-TFM project (for example net10.0); without it every leg runs")
    stability = subparsers.add_parser(
        "stability",
        help="run the same built test binaries N times; measure the spread in results and time (does not build)")
    stability.add_argument(
        "--repeat", type=int, default=STABILITY_DEFAULT_REPEAT,
        help=f"number of runs (default {STABILITY_DEFAULT_REPEAT}, at least {STABILITY_MIN_REPEAT})")
    stability.add_argument(
        "--project", nargs="+", action="extend", help="only these test modules (default: all)")
    stability.add_argument("--exclude", nargs="+", action="extend", help="do not run these test modules")
    stability.add_argument(
        "--shuffle", action="store_true",
        help="give every run a different xunit `--seed`; the seeds are reported")
    stability.add_argument(
        "--pressure", action="store_true",
        help="keep half of the CPUs busy with load processes during some runs (default: the last two runs)")
    stability.add_argument(
        "--pressure-runs", nargs="+", type=int, metavar="N", help="numbers of the runs that get pressure (1-based)")
    stability.add_argument("--seed", type=int, help="seed of the first run (default: derived from the clock)")
    stability.add_argument(
        "--output", help=f"report directory (default {STABILITY_REPORT_DIRECTORY.relative_to(ROOT).as_posix()})")
    release = subparsers.add_parser("release", help="release rehearsal - writes nothing to the network")
    release.add_argument(
        "--dry-run", action="store_true", required=True,
        help="required: this command supports only a dry run, there is no live release path",
    )
    release.add_argument(
        "--package-version",
        help="version to force (MinVerVersionOverride); with --package-directory, the expected version; "
             "without it the current MinVer value is used")
    release.add_argument(
        "--package-directory",
        help="rehearse without packing: the output of 'gate.py pack'; the directory does not change")
    release_version = subparsers.add_parser(
        "release-version",
        help="validate the version input of a manual run and stamp the job's environment with it (MinVerVersionOverride)")
    release_version.add_argument("version", help="the version input; empty means an ordinary run")
    release_version.add_argument(
        "--publish", default="false", help="'true' when the run was asked to publish: then an empty version is refused")
    release_version.add_argument(
        "--env-file", help="append the environment line here ($GITHUB_ENV); without it the version is only validated")
    guard = subparsers.add_parser(
        "publish-guard", help="may these verified packages go to nuget.org, given what their version has there already")
    guard.add_argument("--package-directory", required=True, help="the verified set: the directory with the manifest")
    published = subparsers.add_parser(
        "verify-published", help="is every package of a version on nuget.org, built from the given commit")
    published.add_argument("--version", required=True)
    published.add_argument("--commit", required=True, help="the commit the packages must name (a tag run: github.sha)")
    # CI's single pack. There is NO bypass option.
    pack = subparsers.add_parser("pack", help="pack the tested build with --no-build, and write a manifest")
    pack.add_argument(
        "--output", required=True,
        help="an empty or missing output directory; a directory with files is refused (nothing is deleted)")
    verify_packages = subparsers.add_parser(
        "verify-packages", help="check that the files in a directory match the SHA-256 values in the manifest")
    verify_packages.add_argument("directory", help="directory that holds package-manifest.json")
    # The capacity measurement is NOT A GATE (a load measurement is a report).
    # `close` never runs it; it is here only to keep the command surface in
    # one place. The heavy profiles run by explicit command, started on purpose.
    capacity = subparsers.add_parser(
        "capacity",
        help="HTTP capacity measurement - opt-in, not a gate (scripts/capacity.py)")
    capacity.add_argument("--profile", required=True,
                          help="smoke | sweep | arrival | workers | soak | nodes")
    capacity.add_argument("--package-version", required=True,
                          help="exact package version to measure; a floating version is refused")
    capacity.add_argument("--output", default=str(ARTIFACTS / "capacity"),
                          help="artifact root (default artifacts/capacity)")
    return parser, subparsers


def main(argv: Sequence[str] | None = None, extension: Extension = PUBLIC) -> int:
    parser, subparsers = build_parser()
    arguments = list(sys.argv[1:] if argv is None else argv)
    requested = next((token for token in arguments if not token.startswith("-")), None)
    if requested in RENAMED_COMMANDS:
        parser.error(f"'{requested}' was renamed to '{RENAMED_COMMANDS[requested]}'")

    args = parser.parse_args(arguments)
    if args.stage == "scan":
        return scan(extension=extension)
    if args.stage == "generate":
        return run_commands("generate", generation_commands(extension), dry_run=args.print_commands)
    if args.stage == "capacity":
        return capacity_measurement(args.profile, args.package_version, pathlib.Path(args.output))
    if args.stage == "inner-loop":
        paths = changed_paths(args.base)
        if paths is None:
            print("❌ The git call failed; the affected projects could not be selected.")
            return 1
        if not paths:
            # After a commit, HEAD..working tree is empty and only the
            # build runs - a "green" that tested nothing.
            print(f"⚠️ No change against {args.base}; only the build runs. "
                  "To test committed work, pass `--base <the commit before your work>`.")
        commands = inner_loop_commands(paths)
        if not args.print_commands and runs_multi_target_tests(commands) \
                and not require_test_runtimes(test_target_frameworks()):
            return 1
        return run_commands("inner-loop", commands, dry_run=args.print_commands)
    if args.stage == "close":
        # The performance gate is path-triggered against the WHOLE change set's
        # changes (--base..working tree), not just what is uncommitted right
        # now - a phase that touched a hot path in an earlier commit and only
        # has unrelated uncommitted changes left must still measure it.
        # A failed git call runs it anyway rather than silently skipping.
        paths = changed_paths(args.base)
        performance = paths is None or performance_gate_triggered(paths)
        commands = closing_commands(
            args.base, site=not args.skip_site, performance=performance, extension=extension)
        if not args.print_commands and runs_multi_target_tests(commands) \
                and not require_test_runtimes(test_target_frameworks()):
            return 1
        return run_commands("close", commands, dry_run=args.print_commands)
    if args.stage == "remote-state":
        if args.print_commands:
            print("(network) api.nuget.org flat container + `gh run list` -> the last release tag and the last CI run")
            return 0
        return remote_state_check(ci_remote=extension.ci_remote, tag_hint=extension.tag_hint)
    if args.stage == "perf":
        if args.print_commands:
            print("$ dotnet run -c Release --project bench/Tracon.Benchmarks -- --filter * --exporters json")
            return 0
        return performance_gate(update=args.update)
    if args.stage == "test":
        try:
            commands = test_commands(args.project, args.classes, args.tfm)
        except ValueError as error:
            print(f"❌ {error}")
            return 2
        if not args.print_commands and args.project in multi_target_test_projects():
            frameworks = (args.tfm,) if args.tfm else test_target_frameworks()
            if not require_test_runtimes(frameworks):
                return 1
        return run_commands("test", commands, dry_run=args.print_commands)
    if args.stage == "stability":
        return stability_from_arguments(args)
    if args.stage == "verify-packages":
        if args.print_commands:
            print(f"(no dotnet call) {args.directory}/{PACKAGE_MANIFEST_NAME} -> the raw SHA-256 of every file")
            return 0
        return verify_package_directory(pathlib.Path(args.directory).resolve())
    if args.stage == "release-version":
        if args.print_commands:
            print("(no dotnet call) validate the version input -> MinVerVersionOverride=<version> into the environment file")
            return 0
        return stamp_release_version(
            args.version, args.publish, pathlib.Path(args.env_file) if args.env_file else None)
    if args.stage == "publish-guard":
        if args.print_commands:
            print("(network) api.nuget.org flat container: the nuspec commit of every package of the manifest's version")
            return 0
        return publish_guard(pathlib.Path(args.package_directory).resolve())
    if args.stage == "verify-published":
        if args.print_commands:
            print("(network) api.nuget.org flat container: every packable package of the version names the commit")
            return 0
        return verify_published(args.version, args.commit)
    if args.stage == "pack":
        if args.print_commands:
            print(f"$ {pack_command(args.output, ('<baseline properties>',), no_build=True).display}")
            return 0
        # resolve(): `dotnet pack` runs relative to the root, while Python would resolve against the cwd.
        return pack_for_ci(pathlib.Path(args.output).resolve())
    if args.stage == "release":
        if args.print_commands:
            if args.package_directory:
                print(f"(no dotnet pack) verify-packages {args.package_directory} -> shared path -> "
                      f"copy to {PACKAGE_RELEASE_DIR.relative_to(ROOT)} -> verify-packages")
            else:
                print(f"$ {pack_command('<staging>', ('<baseline properties>',), no_build=False).display}")
            return 0
        if args.package_directory:
            return rehearse_package_directory(pathlib.Path(args.package_directory).resolve(), args.package_version)
        return release_rehearsal(args.package_version)

    if args.print_commands:
        return run_commands("close", closing_commands("<base>", extension=extension), dry_run=True)
    parser.error(f"name a stage: {', '.join(subparsers.choices)}")
    return 2


if __name__ == "__main__":
    sys.exit(main())
