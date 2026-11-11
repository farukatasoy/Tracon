#!/usr/bin/env python3
"""Proves that a failed test leaves its evidence, by failing three named tests on purpose and looking for what they leave.

Evidence that nobody has seen arrive is a claim. The test projects carry three tests that fail only
when `TRACON_EVIDENCE_SELFTEST=1` is set (without it they are skipped):

  pulse  Tracon.Core.UnitTests              a wait that times out while the thread pool is starved
  host   Tracon.AspNetCore.FunctionalTests  a failed job whose host log holds the line its `ref:` points at
  page   Tracon.Ui.E2ETests                 a failed page test that must leave its trace and the page it was on

This script runs ONE of them (never a whole project, so it cannot swallow a real red), expects it
to fail, and then checks the evidence directory for the file that failure must have left. The
directory is a fresh one of its own, so an older pulse file or trace cannot pass the check.

Exit code: 0 when the test failed and the evidence is there; 1 otherwise.
Standard library only.
"""

from __future__ import annotations

import argparse
import dataclasses
import os
import pathlib
import re
import shutil
import subprocess
import sys
from collections.abc import Callable, Mapping, Sequence

ROOT = pathlib.Path(__file__).resolve().parent.parent
VARIABLE = "TRACON_EVIDENCE_SELFTEST"
DIRECTORY_VARIABLE = "TRACON_TEST_EVIDENCE_DIR"
TESTS_FAILED_EXIT_CODE = 2


@dataclasses.dataclass(frozen=True)
class SelfTest:
    """One test that fails on purpose and the evidence its failure must leave."""

    project: str
    method: str
    check: Callable[[pathlib.Path], list[str]]
    output_check: Callable[[str], list[str]] = lambda _output: []


def _read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8", errors="replace")


def check_pulse(directory: pathlib.Path) -> list[str]:
    """Problems with the heartbeat evidence; empty when it is all there."""
    problems: list[str] = []
    pulse = directory / "pulse.txt"
    if not pulse.is_file():
        problems.append("pulse.txt was not written")
    elif "thread pool delay" not in _read(pulse):
        problems.append("pulse.txt does not name a thread pool delay")
    if not any("PulseSelfTests" in path.name for path in directory.glob("*.txt")):
        problems.append("the failed test left no evidence file")
    return problems


def check_host(directory: pathlib.Path) -> list[str]:
    """Problems with the host log evidence; empty when the right lines are there and only those."""
    files = [path for path in directory.glob("*HostFailureEvidenceTests*.txt")]
    if not files:
        return ["the failed test left no evidence file"]
    text = "\n".join(_read(path) for path in files)
    problems = []
    if "(ref: 5e1f7a02)" not in text:
        problems.append("the line the failure's ref points at is missing")
    if "a warning the note must keep" not in text:
        problems.append("the warning line is missing")
    if "an unrelated line the note must leave out" in text:
        problems.append("an unrelated line was kept")
    return problems


def check_page(directory: pathlib.Path) -> list[str]:
    """Problems with the browser evidence; empty when a trace and the page note are there."""
    problems: list[str] = []
    traces = list(directory.glob("*.trace.zip"))
    if not traces:
        problems.append("no trace file was left")
    elif not any(path.stat().st_size > 0 for path in traces):
        problems.append("the trace file is empty")
    notes = list(directory.glob("*PageFailureEvidenceTests*.txt"))
    if not notes:
        problems.append("the failed test left no evidence file")
    elif not any("address: " in _read(path) and "/audit" in _read(path) for path in notes):
        problems.append("the page note does not name the address the test was on")
    return problems


def check_pulse_output(output: str) -> list[str]:
    """Problems with the timeout message the starved wait failed with; the message is in the test run's output.

    The evidence files exist for any failure of the test, so only the message proves that the wait itself carried the numbers:
    the poll count, and a thread pool delay above zero (the pool was blocked on purpose).
    """
    problems: list[str] = []
    if not re.search(r"Polls: [1-9][0-9]*; longest gap between polls: [0-9]+ ms", output):
        problems.append("the timeout message does not carry the poll count and the longest gap")
    if not re.search(r"longest thread pool delay: [1-9][0-9]* ms", output):
        problems.append("the timeout message does not name a thread pool delay above zero")
    return problems


SELF_TESTS: Mapping[str, SelfTest] = {
    "pulse": SelfTest(
        "Tracon.Core.UnitTests",
        "A_wait_that_times_out_on_a_starved_thread_pool_names_the_starvation",
        check_pulse,
        check_pulse_output,
    ),
    "host": SelfTest("Tracon.AspNetCore.FunctionalTests", "A_failed_job_leaves_the_line_its_reference_points_at", check_host),
    "page": SelfTest("Tracon.Ui.E2ETests", "A_failed_page_test_leaves_its_trace_and_the_page_it_was_on", check_page),
}


def command(test: SelfTest) -> list[str]:
    """The test command: one project, one method, nothing built."""
    project = f"tests/{test.project}/{test.project}.csproj"
    return ["dotnet", "test", "--project", project, "-c", "Release", "--no-build", "--filter-method", f"*{test.method}*"]


def evidence_directory(test: SelfTest, root: pathlib.Path = ROOT) -> pathlib.Path:
    """A directory of its own beside the real results, so the artifact upload carries it and nothing old can pass."""
    return root / "artifacts" / "test-results" / test.project / "evidence-selftest"


def run(kind: str, root: pathlib.Path = ROOT, environ: Mapping[str, str] = os.environ) -> int:
    """Runs one self-test; the exit code is the script's."""
    test = SELF_TESTS[kind]
    directory = evidence_directory(test, root)
    shutil.rmtree(directory, ignore_errors=True)
    directory.mkdir(parents=True)

    env = dict(environ)
    env[VARIABLE] = "1"
    env[DIRECTORY_VARIABLE] = str(directory)
    completed = subprocess.run(command(test), cwd=root, env=env, text=True, capture_output=True, check=False)
    print(completed.stdout)
    if completed.stderr:
        print(completed.stderr, file=sys.stderr)

    if completed.returncode != TESTS_FAILED_EXIT_CODE:
        print(
            f"❌ {kind}: the test was expected to fail (exit code {TESTS_FAILED_EXIT_CODE}) and exited with {completed.returncode}. "
            "Exit code 8 means the filter matched no test; 0 means the test did not fail on purpose.",
            file=sys.stderr,
        )
        return 1
    problems = test.check(directory) + test.output_check(completed.stdout or "")
    if problems:
        for problem in problems:
            print(f"❌ {kind}: {problem}", file=sys.stderr)
        return 1
    print(f"✅ {kind}: the test failed and left its evidence in {directory}")
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("kind", choices=sorted(SELF_TESTS))
    return run(parser.parse_args(argv).kind)


if __name__ == "__main__":
    sys.exit(main())
