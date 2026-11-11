#!/usr/bin/env python3
"""Describes the CI runner a job runs on, so a slow or red run can be read against the machine it ran on.

The card is printed at the top of every test job. It answers four questions without a second run:
how many cores and how much memory the runner has, how much disk is free, where the temporary
directories are, and how long a SQLite commit loop takes in each of them. The last number is the
one that tells a slow disk from slow code: the same loop in the system temporary directory and in
the runner's own temporary directory shows whether the location matters.

Standard library only. The card never fails a job: every probe that cannot run becomes a line that
says so.
"""

from __future__ import annotations

import os
import pathlib
import platform
import shutil
import sqlite3
import subprocess
import sys
import tempfile
import time
from collections.abc import Mapping

COMMITS = 300
POWERSHELL_TIMEOUT_SECONDS = 30


def parse_meminfo(text: str) -> int | None:
    """Bytes of physical memory from the text of `/proc/meminfo`, or None when the line is missing."""
    for line in text.splitlines():
        if line.startswith("MemTotal:"):
            parts = line.split()
            if len(parts) >= 2 and parts[1].isdigit():
                return int(parts[1]) * 1024
    return None


def physical_memory_bytes() -> int | None:
    """Physical memory of this machine, or None when no probe for this system works."""
    try:
        if sys.platform.startswith("linux"):
            return parse_meminfo(pathlib.Path("/proc/meminfo").read_text(encoding="utf-8"))
        if sys.platform == "darwin":
            return int(subprocess.run(["sysctl", "-n", "hw.memsize"], capture_output=True, text=True, timeout=10, check=True).stdout)
        if sys.platform == "win32":
            output = _powershell("(Get-CimInstance Win32_ComputerSystem).TotalPhysicalMemory")
            return int(output.strip()) if output else None
    except (OSError, ValueError, subprocess.SubprocessError):
        return None
    return None


def _powershell(command: str) -> str | None:
    """Output of one PowerShell command, or None when PowerShell is missing, slow or fails."""
    for executable in ("pwsh", "powershell"):
        if shutil.which(executable) is None:
            continue
        try:
            completed = subprocess.run(
                [executable, "-NoProfile", "-NonInteractive", "-Command", command],
                capture_output=True,
                text=True,
                timeout=POWERSHELL_TIMEOUT_SECONDS,
                check=True,
            )
        except (OSError, subprocess.SubprocessError):
            return None
        return completed.stdout
    return None


def defender_line() -> str:
    """What Windows Defender is set to scan, as one line; says why when it cannot be read."""
    if sys.platform != "win32":
        return "not applicable (not Windows)"
    output = _powershell(
        "$p = Get-MpPreference; "
        "'realtime-monitoring-disabled=' + $p.DisableRealtimeMonitoring + "
        "'; excluded-paths=' + (($p.ExclusionPath | Sort-Object) -join ',')"
    )
    return output.strip() if output and output.strip() else "could not be read"


def commit_loop_seconds(directory: pathlib.Path, commits: int = COMMITS) -> float:
    """Seconds one fresh SQLite database in `directory` takes to commit `commits` single-row transactions.

    The settings are the ones the test databases use (default journal, `synchronous = NORMAL`), so the
    number is the cost of the same write pattern, not of a different one.
    """
    work = pathlib.Path(tempfile.mkdtemp(prefix="tracon-card-", dir=directory))
    try:
        connection = sqlite3.connect(str(work / "card.db"), isolation_level=None)
        try:
            connection.execute("PRAGMA synchronous = NORMAL")
            connection.execute("CREATE TABLE card (id INTEGER PRIMARY KEY, value TEXT NOT NULL)")
            started = time.perf_counter()
            for number in range(commits):
                connection.execute("BEGIN")
                connection.execute("INSERT INTO card (value) VALUES (?)", (f"row {number}",))
                connection.execute("COMMIT")
            return time.perf_counter() - started
        finally:
            connection.close()
    finally:
        shutil.rmtree(work, ignore_errors=True)


def locations(environ: Mapping[str, str]) -> dict[str, pathlib.Path]:
    """The directories the card measures, by label: the system temporary directory and the runner's own."""
    found: dict[str, pathlib.Path] = {"temp": pathlib.Path(tempfile.gettempdir())}
    runner_temp = environ.get("RUNNER_TEMP")
    if runner_temp and pathlib.Path(runner_temp).is_dir():
        found["runner_temp"] = pathlib.Path(runner_temp)
    return found


def ratio_line(seconds: Mapping[str, float]) -> str | None:
    """How many times faster `runner_temp` is than `temp`; None unless both were measured."""
    if "temp" not in seconds or "runner_temp" not in seconds or seconds["runner_temp"] <= 0:
        return None
    return f"temp / runner_temp = {seconds['temp'] / seconds['runner_temp']:.2f}"


def card(environ: Mapping[str, str] = os.environ) -> list[str]:
    """The lines of the card."""
    lines = [
        f"system: {platform.platform()}",
        f"python: {platform.python_version()}",
        f"cpu cores: {os.cpu_count()}",
    ]
    memory = physical_memory_bytes()
    lines.append(f"memory: {memory / 2**30:.1f} GiB" if memory else "memory: could not be read")

    where = locations(environ)
    for label, path in where.items():
        try:
            usage = shutil.disk_usage(path)
            lines.append(f"{label}: {path} ({usage.free / 2**30:.1f} GiB free of {usage.total / 2**30:.1f})")
        except OSError as error:
            lines.append(f"{label}: {path} (disk usage could not be read: {error})")
    lines.append(f"work directory: {pathlib.Path.cwd()}")
    lines.append(f"defender: {defender_line()}")

    seconds: dict[str, float] = {}
    for label, path in where.items():
        try:
            seconds[label] = commit_loop_seconds(path)
            lines.append(f"sqlite {COMMITS} commits in {label}: {seconds[label]:.2f} s")
        except (OSError, sqlite3.Error) as error:
            lines.append(f"sqlite {COMMITS} commits in {label}: could not run ({error})")
    ratio = ratio_line(seconds)
    if ratio:
        lines.append(f"sqlite {ratio}")
    return lines


def main() -> int:
    """Prints the card. The exit code is always 0: a card is information, not a gate."""
    print("== runner card ==")
    try:
        for line in card():
            print(line)
    except Exception as error:  # noqa: BLE001 - the card must never fail the job that prints it
        print(f"the card could not be completed: {error}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
