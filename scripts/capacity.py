#!/usr/bin/env python3
"""Tracon HTTP capacity measurement.

This script is the outer shell of the MEASUREMENT: it builds the packages with an exact
version, sets up a host that consumes them from an isolated feed in a temporary directory
outside the repository, opens a clean database and clean processes for every cell, runs the
driver and writes the report at the end.

🚨 The measurement is opt-in. It is not added to the standard close, the PR path or the
release pipeline; only the `smoke` profile runs in CI, as an explicit step.

🚨 The connection string never appears in a process argument (the secret boundary applied to
the harness): it travels only in the environment and is not written to the manifest.
"""

from __future__ import annotations

import argparse
import json
import hashlib
import os
import pathlib
import platform
import re
import shutil
import signal
import subprocess
import sys
import tempfile
import time
import uuid
from typing import Any

ROOT = pathlib.Path(__file__).resolve().parent.parent
CAPACITY_DIR = ROOT / "bench" / "capacity"
PROFILE_DIR = CAPACITY_DIR / "profiles"
DEFAULT_OUTPUT = ROOT / "artifacts" / "capacity"
PACKABLE_SOLUTION_FILTER = ROOT / "Tracon.src.slnf"

# The exact version the measurement consumes. A floating version (`*`, `*-*`) is REJECTED: a
# capacity report that cannot say which bytes were measured has no evidential value.
EXACT_VERSION = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")

POSTGRES_IMAGE = "pgvector/pgvector:pg18"
CONTAINER_PREFIX = "tracon-capacity-"
READY_TIMEOUT_SECONDS = 120

HOST_PROJECT = "Tracon.CapacityHost"
DRIVER_PROJECT = "Tracon.CapacityDriver"

# Name of the directory where the host and worker processes write their own execution records.
EXECUTIONS_DIR = "executions"


class CapacityError(Exception):
    """The measurement cannot start or cannot continue reliably."""


# --------------------------------------------------------------------------
# Profile
# --------------------------------------------------------------------------

def load_profile(name: str, profile_dir: pathlib.Path = PROFILE_DIR) -> dict[str, Any]:
    """Reads the profile and validates that it can be measured."""
    path = profile_dir / f"{name}.json"

    if not path.exists():
        available = ", ".join(sorted(p.stem for p in profile_dir.glob("*.json")))
        raise CapacityError(f"Profile '{name}' does not exist. Available: {available}")

    profile = json.loads(path.read_text(encoding="utf-8"))
    problems = validate_profile(profile)

    if problems:
        raise CapacityError(f"Profile '{name}' cannot be measured:\n  " + "\n  ".join(problems))

    return profile


def validate_profile(profile: dict[str, Any]) -> list[str]:
    """Every reason the profile cannot be measured.

    🚨 A zero or negative duration, an empty scenario list and zero repeats are rejected here.
    A profile that sends no request is not a FAST profile; silently producing an empty report
    would make the capacity report a lie."""
    problems: list[str] = []

    if not profile.get("scenarios"):
        problems.append("scenarios must not be empty")

    for scenario in profile.get("scenarios", []):
        if scenario not in ("buffered", "streaming", "queued"):
            problems.append(f"unknown scenario: {scenario}")

    if profile.get("repeats", 0) <= 0:
        problems.append("repeats must be greater than zero")

    measure = profile.get("measureSeconds", 0)
    runs_per_tenant = profile.get("runsPerTenant", 0)
    ramp = profile.get("tenantRamp")

    if ramp is not None:
        # The tenant ramp IS the limit of the measurement: a duration or a run count on top of it would make two limits.
        if measure > 0 or runs_per_tenant > 0:
            problems.append("tenantRamp is the measurement limit itself; measureSeconds and runsPerTenant must be zero")

        problems.extend(validate_tenant_ramp(ramp))
    else:
        if measure <= 0 and runs_per_tenant <= 0:
            problems.append("one of measureSeconds or runsPerTenant must be greater than zero")

        if measure > 0 and runs_per_tenant > 0:
            problems.append("measureSeconds and runsPerTenant are two separate limits; set only one")

    if profile.get("warmupSeconds", 0) < 0:
        problems.append("warmupSeconds must not be negative")

    for value in profile.get("concurrency", [1]):
        if value <= 0:
            problems.append(f"concurrency must be greater than zero: {value}")

    for value in profile.get("arrivalRates", []):
        if value <= 0:
            problems.append(f"arrivalRates must be greater than zero: {value}")

    for value in profile.get("workerCounts", []):
        if value <= 0:
            problems.append(f"workerCounts must be greater than zero: {value}")

    for value in profile.get("apiHostCounts", []):
        if value <= 0:
            problems.append(f"apiHostCounts must be greater than zero: {value}")

    # The three axes (worker count, host count, arrival rate) are not crossed with each other: each is
    # measured with its own fixed load. Given together, plan_cells would run only one and silently drop the others.
    axes = [name for name in ("workerCounts", "apiHostCounts", "arrivalRates") if profile.get(name)]

    if len(axes) > 1:
        problems.append(f"{' and '.join(axes)} are separate axes; set only one")

    if ramp is not None and axes:
        problems.append(f"tenantRamp is measured alone; it does not combine with {' and '.join(axes)}")

    for shape in profile.get("seedShapes", ["empty"]):
        if shape not in ("empty", "full"):
            problems.append(f"unknown seed shape: {shape}")

    if profile.get("seedRuns", 0) < 0:
        problems.append("seedRuns must not be negative")

    return problems


def validate_tenant_ramp(ramp: Any) -> list[str]:
    """Every reason the tenant ramp cannot be measured.

    🚨 The driver validates the same rules again on its side; this check only keeps a run that takes
    hours from being rejected in the first cell, AFTER the packing and build steps."""
    if not isinstance(ramp, dict):
        return ["tenantRamp must be an object"]

    problems: list[str] = []
    stages = ramp.get("stages")

    if not isinstance(stages, list) or not stages:
        return ["tenantRamp.stages must not be empty"]

    previous = 0

    for index, stage in enumerate(stages):
        stage = stage if isinstance(stage, dict) else {}
        tenants = stage.get("tenants", 0)

        if not isinstance(tenants, int) or tenants <= previous:
            problems.append(f"tenantRamp.stages[{index}].tenants must be an integer greater than the previous stage ({previous})")
        else:
            previous = tenants

        if stage.get("runsPerNewTenant", 2) <= 0:
            problems.append(f"tenantRamp.stages[{index}].runsPerNewTenant must be greater than zero")

        if stage.get("holdSeconds", 60) < 0:
            problems.append(f"tenantRamp.stages[{index}].holdSeconds must not be negative")

    if previous > 99_999:
        problems.append("tenantRamp must not exceed 99999 tenants")

    if ramp.get("idleSeconds", 30) < 0:
        problems.append("tenantRamp.idleSeconds must not be negative")

    return problems


def tenant_tag_requested(ramp: dict[str, Any] | None) -> bool:
    """Whether the host writes the tenant into metric labels: the `tenantTag` field of the profile or the
    `TRACON_CAPACITY_TENANT_TAG=1` environment variable. The variable matters only in a ramp run."""
    if ramp is None:
        return False

    return bool(ramp.get("tenantTag", False)) or os.environ.get("TRACON_CAPACITY_TENANT_TAG", "0") == "1"


def driver_ramp(ramp: dict[str, Any] | None) -> dict[str, Any] | None:
    """Converts the ramp of the profile into the `TenantRampSpec` of the driver; keys that belong to the host (`tenantTag`) do not go to the driver."""
    if ramp is None:
        return None

    return {key: value for key, value in ramp.items() if key not in ("tenantTag",)}


def plan_cells(profile: dict[str, Any], run_id: str) -> list[dict[str, Any]]:
    """Expands the profile into the cells to measure.

    The worker count is a SEPARATE axis: it is not crossed with the sweep steps. This
    function enforces that separation in its structure - a profile with `workerCounts`
    takes only ONE value from the concurrency list."""
    cells: list[dict[str, Any]] = []
    scenarios = profile["scenarios"]
    repeats = profile["repeats"]
    seed_shapes = profile.get("seedShapes", ["empty"])
    grouping = profile.get("scenarioGrouping", "separate")
    groups = [scenarios] if grouping == "mixed" else [[s] for s in scenarios]

    def add(group: list[str], seed: str, repeat: int, **extra: Any) -> None:
        index = len(cells)
        cells.append({
            "runId": run_id,
            "cellId": "{:03d}-{}".format(index, extra.get("label", "cell")),
            "profile": profile["name"],
            "scenarios": group,
            "seedShape": seed,
            "repeat": repeat,
            **{k: v for k, v in extra.items() if k != "label"},
        })

    if profile.get("workerCounts"):
        concurrency = profile["fixedConcurrency"]
        for workers in profile["workerCounts"]:
            for repeat in range(1, repeats + 1):
                add(
                    ["queued"], seed_shapes[0], repeat,
                    concurrency=concurrency, workerCount=workers,
                    label=f"queued-w{workers}-r{repeat}",
                )
        return cells

    if profile.get("apiHostCounts"):
        # The host count is a SEPARATE axis too: at a fixed load only the number of HTTP hosts changes.
        # Each host takes its lease with its own worker - that is how two replicas behave.
        concurrency = profile["fixedConcurrency"]
        for group in groups:
            name = group[0] if len(group) == 1 else "mixed"
            for hosts in profile["apiHostCounts"]:
                for repeat in range(1, repeats + 1):
                    add(
                        group, seed_shapes[0], repeat,
                        concurrency=concurrency, apiHostCount=hosts,
                        label=f"{name}-h{hosts}-r{repeat}",
                    )
        return cells

    if profile.get("arrivalRates"):
        for rate in profile["arrivalRates"]:
            for repeat in range(1, repeats + 1):
                add(
                    ["queued"], seed_shapes[0], repeat,
                    concurrency=profile.get("fixedConcurrency", 16),
                    arrivalRatePerSecond=rate,
                    label=f"queued-a{rate}-r{repeat}",
                )
        return cells

    for group in groups:
        for concurrency in profile.get("concurrency", [1]):
            for seed in seed_shapes:
                for repeat in range(1, repeats + 1):
                    name = group[0] if len(group) == 1 else "mixed"
                    add(
                        group, seed, repeat, concurrency=concurrency,
                        label=f"{name}-c{concurrency}-{seed}-r{repeat}",
                    )

    return cells


# --------------------------------------------------------------------------
# Process helpers
# --------------------------------------------------------------------------

def run(command: list[str], *, cwd: pathlib.Path | None = None,
        env: dict[str, str] | None = None, check: bool = True,
        capture: bool = False) -> subprocess.CompletedProcess[str]:
    """Runs a command. MSBuild node reuse is DISABLED.

    🚨 Orphaned MSBuild nodes keep the redirected pipe open and the waiting
    process hangs for minutes (measured)."""
    environment = dict(os.environ)
    environment["MSBUILDDISABLENODEREUSE"] = "1"

    if env:
        environment.update(env)

    return subprocess.run(
        command, cwd=cwd, env=environment, check=check, text=True,
        capture_output=capture,
    )


def wait_for_line(process: subprocess.Popen[str], line: str, timeout: float,
                  label: str) -> None:
    """Waits until the process writes the given LINE.

    🚨 NOT a fixed `sleep`: on a loaded machine a fixed wait makes the process
    test fragile and lets the startup time into the measurement window."""
    deadline = time.monotonic() + timeout

    # 🚨 The consumed lines are KEPT. This loop reads the output of the process, so
    # nothing was left when it died and the error message came out EMPTY - measured:
    # the port conflict of the second worker was reported as "code -6:" without a
    # word of explanation, and finding the cause meant running the process by hand.
    seen: list[str] = []

    while time.monotonic() < deadline:
        if process.poll() is not None:
            if process.stdout:
                seen.append(process.stdout.read())

            detail = "".join(seen).strip() or "(the process wrote nothing)"
            raise CapacityError(
                f"{label} exited without writing '{line}' (code {process.returncode}): {detail[-2000:]}"
            )

        ready = process.stdout.readline() if process.stdout else ""

        if not ready:
            time.sleep(0.02)
            continue

        seen.append(ready)

        if line in ready:
            return

    raise CapacityError(
        f"{label} did not write '{line}' within {timeout:.0f} s; output so far: "
        f"{''.join(seen).strip()[-2000:] or '(nothing)'}"
    )


def stop(process: subprocess.Popen[str] | None, label: str) -> None:
    """Stops a process politely first, then for certain.

    No process this run does not own is touched: only the handles this run
    started are closed."""
    if process is None or process.poll() is not None:
        return

    try:
        process.send_signal(signal.SIGTERM)
        process.wait(timeout=20)
    except subprocess.TimeoutExpired:
        print(f"⚠️ {label} did not stop on SIGTERM; killing it", flush=True)
        process.kill()
        process.wait(timeout=10)
    except ProcessLookupError:
        pass


# --------------------------------------------------------------------------
# Package and isolated consumer
# --------------------------------------------------------------------------

def git(*args: str) -> str:
    result = subprocess.run(["git", *args], cwd=ROOT, text=True, capture_output=True, check=False)
    return result.stdout.strip() if result.returncode == 0 else ""


def working_tree_state() -> tuple[str, bool, str | None]:
    """Commit, dirtiness and, when dirty, the hash of the diff."""
    commit = git("rev-parse", "HEAD") or "unknown"
    status = git("status", "--porcelain")
    dirty = bool(status)
    diff_hash = None

    if dirty:
        diff = subprocess.run(["git", "diff", "HEAD"], cwd=ROOT, text=True,
                              capture_output=True, check=False).stdout
        diff_hash = hashlib.sha256(diff.encode("utf-8")).hexdigest()[:16]

    return commit, dirty, diff_hash


def extra_msbuild_arguments() -> list[str]:
    """Extra MSBuild arguments from the `TRACON_CAPACITY_MSBUILD_ARGS` environment variable (separated by spaces).

    On a shared machine packing and building are limited with `-m:3`; while developing the harness the front-end
    build is skipped with `-p:TraconFrontendEnabled=false`. 🚨 The value is written to the manifest: the
    measurement of a package built without its front end must stay distinguishable from one built with it."""
    return os.environ.get("TRACON_CAPACITY_MSBUILD_ARGS", "").split()


def pack(version: str, feed: pathlib.Path, *, dirty: bool) -> None:
    """Builds the packages with an exact version and puts them in the isolated feed."""
    if feed.exists():
        shutil.rmtree(feed)

    feed.mkdir(parents=True)

    env = {"MinVerVersionOverride": version}
    command = ["dotnet", "pack", str(PACKABLE_SOLUTION_FILTER), "-c", "Release", "-o", str(feed)]

    if dirty:
        # The repository's own gate: a dirty pack has no provenance, so the version
        # MUST carry 'dirty' and can never be produced in CI.
        command.append("-p:TraconAllowDirtyPack=true")

    command.extend(extra_msbuild_arguments())

    print(f"$ {' '.join(command)}  (MinVerVersionOverride={version})", flush=True)
    run(command, cwd=ROOT, env=env)


def reusable_feed(version: str, reuse_feed: pathlib.Path) -> pathlib.Path:
    """The condition for reusing a feed built earlier.

    🚨 Only while developing the harness: when `src/` has not changed the packages are identical, and rebuilding
    17 packages on every attempt takes minutes. A run that is not dirty CANNOT use this: the provenance of every
    number to be published comes from its own `pack`."""
    if "dirty" not in version:
        raise CapacityError("--ready-feed works only with a 'dirty' version; a publishable run builds its own packages")

    feed = reuse_feed.resolve()

    if not (feed / f"Tracon.{version}.nupkg").exists():
        raise CapacityError(f"Tracon.{version}.nupkg is not in {feed}")

    return feed


def package_hashes(feed: pathlib.Path) -> dict[str, str]:
    return {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(feed.glob("*.nupkg"))
    }


def prepare_consumer(workspace: pathlib.Path, feed: pathlib.Path, version: str) -> pathlib.Path:
    """Sets up and builds an isolated consumer OUTSIDE the repository."""
    consumer = workspace / "capacity"
    shutil.copytree(
        CAPACITY_DIR, consumer,
        ignore=shutil.ignore_patterns("bin", "obj", "*.user"),
    )

    cache = workspace / "packages"
    cache.mkdir()

    (consumer / "NuGet.config").write_text(
        f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="tracon-capacity" value="{feed}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="tracon-capacity"><package pattern="Tracon*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
""",
        encoding="utf-8",
    )

    env = {"NUGET_PACKAGES": str(cache)}
    version_arg = f"-p:TraconCapacityPackageVersion={version}"

    for project in (HOST_PROJECT, DRIVER_PROJECT, "Tracon.Capacity.Acceptance"):
        csproj = consumer / project / f"{project}.csproj"

        if not csproj.exists():
            continue

        run(["dotnet", "restore", str(csproj), "--configfile", str(consumer / "NuGet.config"), version_arg],
            cwd=consumer, env=env)
        run(["dotnet", "build", str(csproj), "-c", "Release", "--no-restore", version_arg,
             *extra_msbuild_arguments()],
            cwd=consumer, env=env)

    verify_isolation(consumer, cache, version)
    return consumer


def verify_isolation(consumer: pathlib.Path, cache: pathlib.Path, version: str) -> None:
    """Proves that the host REALLY resolved from the isolated feed and at the exact version.

    🚨 Without this step the measurement could silently consume an old package from the
    global cache and the report could not tell which bytes it measured."""
    assets = consumer / HOST_PROJECT / "obj" / "project.assets.json"

    if not assets.exists():
        raise CapacityError(f"{HOST_PROJECT} has no restore output: {assets}")

    text = assets.read_text(encoding="utf-8")

    if str(cache) not in text:
        raise CapacityError(f"{HOST_PROJECT} did not use the isolated NUGET_PACKAGES")

    if f'"Tracon/{version}"' not in text:
        raise CapacityError(f"{HOST_PROJECT} did not resolve exact version {version}")

    document = json.loads(text)

    for library in document.get("libraries", {}):
        name, _, resolved = library.partition("/")

        if name.startswith("Tracon") and resolved != version:
            raise CapacityError(f"{name} is not at the expected version: {resolved} != {version}")


# --------------------------------------------------------------------------
# Database
# --------------------------------------------------------------------------

class Database:
    """A PostgreSQL private to the run. Only what this run created is cleaned up."""

    def __init__(self, external: str | None) -> None:
        self.external = external
        self.container: str | None = None
        self.admin = external or ""
        self.version = "unknown"
        self.image: str | None = None

    def start(self) -> None:
        if self.external:
            self.version = self._server_version(self.external)
            return

        self.container = CONTAINER_PREFIX + uuid.uuid4().hex[:8]
        self.image = POSTGRES_IMAGE
        port = self._free_port()

        run([
            "docker", "run", "-d", "--rm", "--name", self.container,
            "-e", "POSTGRES_PASSWORD=capacity",
            "-e", "POSTGRES_USER=capacity",
            "-e", "POSTGRES_DB=capacity",
            "-p", f"{port}:5432",
            POSTGRES_IMAGE,
        ], capture=True)

        self.admin = (
            f"Host=127.0.0.1;Port={port};Database=capacity;"
            "Username=capacity;Password=capacity;Include Error Detail=true"  # SYNTHETIC-CREDENTIAL
        )

        # 🚨 `pg_isready` ALONE is not enough: the official image first starts a temporary
        # server that listens only on the UNIX socket, runs the init scripts and then SHUTS
        # it down. In that window pg_isready succeeds and the next query gets "the database
        # system is shutting down" - measured. So readiness is verified with a real query
        # over TCP, THREE times in a row; the temporary server never listens on TCP.
        deadline = time.monotonic() + READY_TIMEOUT_SECONDS
        consecutive = 0

        while time.monotonic() < deadline:
            probe = run(["docker", "exec", self.container, "psql", "-h", "127.0.0.1",
                         "-U", "capacity", "-d", "capacity", "-tAc", "SELECT 1"],
                        check=False, capture=True)

            if probe.returncode == 0 and probe.stdout.strip() == "1":
                consecutive += 1

                if consecutive >= 3:
                    self.version = self._server_version(self.admin)
                    return
            else:
                consecutive = 0

            time.sleep(0.5)

        raise CapacityError("The PostgreSQL container did not become ready")

    def _server_version(self, connection: str) -> str:
        if not self.container:
            return "unknown (external server)"

        result = run(["docker", "exec", self.container, "psql", "-h", "127.0.0.1",
                      "-U", "capacity", "-d", "capacity",
                      "-tAc", "SHOW server_version"], check=False, capture=True)
        return result.stdout.strip() if result.returncode == 0 else "unknown"

    def psql(self, database: str, sql: str) -> str:
        if not self.container:
            raise CapacityError(
                "Database management on an external server is not supported; "
                "run without TRACON_CAPACITY_CONNECTION."
            )

        result = run(["docker", "exec", self.container, "psql", "-h", "127.0.0.1",
                      "-U", "capacity", "-d", database, "-v", "ON_ERROR_STOP=1", "-tAc", sql],
                     check=False, capture=True)

        if result.returncode != 0:
            raise CapacityError(f"psql failed: {result.stderr.strip()[:500]}")

        return result.stdout.strip()

    def connection_for(self, database: str) -> str:
        return re.sub(r"Database=[^;]+", f"Database={database}", self.admin)

    @staticmethod
    def _free_port() -> int:
        import socket

        with socket.socket() as probe:
            probe.bind(("127.0.0.1", 0))
            return probe.getsockname()[1]

    def stop(self) -> None:
        if self.container:
            run(["docker", "rm", "-f", self.container], check=False, capture=True)
            self.container = None


# --------------------------------------------------------------------------
# Host processes
# --------------------------------------------------------------------------

def host_executable(consumer: pathlib.Path, project: str) -> pathlib.Path:
    name = project + (".exe" if platform.system() == "Windows" else "")
    path = consumer / project / "bin" / "Release" / "net10.0" / name

    if not path.exists():
        raise CapacityError(f"{project} was not built: {path}")

    return path


def host_environment(*, connection: str, schema: str, mode: str, name: str,
                     workload: dict[str, Any], executions: pathlib.Path,
                     port: int = 0, worker_axis: bool = False,
                     max_jobs: int = 8, pool: int = 100,
                     seed_runs: int = 0, open_tenants: bool = False,
                     tenant_tag: bool = False) -> dict[str, str]:
    """The environment of one host process.

    🚨 The connection string travels ONLY HERE, in the environment. A process argument
    shows up in the `ps` output; since a capacity run holds a live credential for the
    whole measurement, that would be a real leak path."""
    return {
        "TRACON_CAPACITY_MODE": mode,
        "TRACON_CAPACITY_CONNECTION": connection,
        "TRACON_CAPACITY_SCHEMA": schema,
        "TRACON_CAPACITY_NAME": name,
        "TRACON_CAPACITY_PORT": str(port),
        "TRACON_CAPACITY_WORKLOAD": json.dumps(workload),
        "TRACON_CAPACITY_EXECUTIONS": str(executions),
        "TRACON_CAPACITY_WORKER_AXIS": "1" if worker_axis else "0",
        "TRACON_CAPACITY_MAX_JOBS": str(max_jobs),
        "TRACON_CAPACITY_POOL": str(pool),
        "TRACON_CAPACITY_SEED_RUNS": str(seed_runs),
        "TRACON_CAPACITY_OPEN_TENANTS": "1" if open_tenants else "0",
        "TRACON_CAPACITY_TENANT_TAG": "1" if tenant_tag else "0",
        "DOTNET_ENVIRONMENT": "Production",
    }


def start_host(executable: pathlib.Path, environment: dict[str, str],
               ready_line: str, label: str) -> subprocess.Popen[str]:
    merged = dict(os.environ)
    merged.update(environment)
    merged["MSBUILDDISABLENODEREUSE"] = "1"

    process = subprocess.Popen(
        [str(executable)], cwd=executable.parent, env=merged, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT, bufsize=1,
    )

    wait_for_line(process, ready_line, READY_TIMEOUT_SECONDS, label)
    return process


def run_to_completion(executable: pathlib.Path, environment: dict[str, str],
                      label: str, timeout: float = 3600) -> str:
    merged = dict(os.environ)
    merged.update(environment)
    merged["MSBUILDDISABLENODEREUSE"] = "1"

    result = subprocess.run(
        [str(executable)], cwd=executable.parent, env=merged, text=True,
        capture_output=True, check=False, timeout=timeout,
    )

    if result.returncode != 0:
        raise CapacityError(f"{label} exit code {result.returncode}: {result.stdout[-2000:]}{result.stderr[-2000:]}")

    return result.stdout


def free_port() -> int:
    import socket

    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


# --------------------------------------------------------------------------
# Template database
# --------------------------------------------------------------------------

def template_name(shape: str, run_id: str) -> str:
    return f"cap_{run_id}_{shape}_tpl".lower()


def build_templates(database: Database, consumer: pathlib.Path, profile: dict[str, Any],
                    run_id: str, schema: str, executions: pathlib.Path) -> dict[str, dict[str, Any]]:
    """Builds one TEMPLATE database per seed shape.

    🚨 Isolating every cell with its own *schema* is too slow. Measured: writing the
    `full` fixture through the public store path is 10,000 runs x 21 store calls, and
    repeating that in each of the 36 full cells would put the seed on the critical path
    for hours. Instead ONE template database is built per shape and every cell takes its
    own database from it with `CREATE DATABASE ... TEMPLATE ...`. Isolation does not
    weaken, it gets STRONGER (a database boundary instead of a schema one) and the seed
    time never enters a measurement window."""
    executable = host_executable(consumer, HOST_PROJECT)
    templates: dict[str, dict[str, Any]] = {}

    for shape in profile.get("seedShapes", ["empty"]):
        name = template_name(shape, run_id)
        database.psql("capacity", f'DROP DATABASE IF EXISTS "{name}"')
        database.psql("capacity", f'CREATE DATABASE "{name}"')
        database.psql(name, "CREATE EXTENSION IF NOT EXISTS vector")

        connection = database.connection_for(name)

        run_to_completion(
            executable,
            host_environment(connection=connection, schema=schema, mode="migrate",
                             name="migrate", workload=profile["workload"],
                             executions=executions),
            f"migrate({shape})",
        )

        seeded = {"runs": 0, "events": 0}

        if shape == "full":
            output = run_to_completion(
                executable,
                host_environment(connection=connection, schema=schema, mode="seed",
                                 name="seed", workload=profile["workload"],
                                 executions=executions,
                                 seed_runs=profile.get("seedRuns", 0)),
                "seed(full)",
                timeout=7200,
            )

            match = re.search(r"CAPACITY-SEEDED runs=(\d+) events=(\d+)", output)

            if not match:
                raise CapacityError("the seed mode did not report what it wrote")

            seeded = {"runs": int(match.group(1)), "events": int(match.group(2))}

        # 🚨 The ACTUAL row counts are verified, not the declared ones. A seed that
        # claims more than it wrote turns the "full database" cell into fiction.
        actual_runs = int(database.psql(name, f'SELECT COUNT(*) FROM "{schema}".runs'))
        actual_events = int(database.psql(name, f'SELECT COUNT(*) FROM "{schema}".run_events'))

        if shape == "full" and (actual_runs != seeded["runs"] or actual_events != seeded["events"]):
            raise CapacityError(
                f"the seed declaration does not match the row counts: declared {seeded}, "
                f"actual runs={actual_runs} events={actual_events}"
            )

        size = int(database.psql(name, "SELECT pg_database_size(current_database())"))

        templates[shape] = {
            "database": name,
            "runs": actual_runs,
            "events": actual_events,
            "bytes": size,
        }

        print(f"  template {shape}: runs={actual_runs} events={actual_events} "
              f"size={size / 1024 / 1024:.1f} MiB", flush=True)

    return templates


# --------------------------------------------------------------------------
# Running a cell
# --------------------------------------------------------------------------

def run_cell(*, cell: dict[str, Any], profile: dict[str, Any], database: Database,
             consumer: pathlib.Path, templates: dict[str, dict[str, Any]],
             schema: str, output: pathlib.Path) -> dict[str, Any]:
    """Runs one cell with a clean database and clean processes."""
    cell_dir = output / "cells" / cell["cellId"]
    cell_dir.mkdir(parents=True, exist_ok=True)
    executions = cell_dir / EXECUTIONS_DIR
    executions.mkdir(exist_ok=True)

    template = templates[cell["seedShape"]]
    name = f"cap_{cell['runId']}_{cell['cellId'].replace('-', '_')}".lower()[:60]

    database.psql("capacity", f'DROP DATABASE IF EXISTS "{name}"')
    database.psql("capacity", f'CREATE DATABASE "{name}" TEMPLATE "{template["database"]}"')

    connection = database.connection_for(name)
    executable = host_executable(consumer, HOST_PROJECT)
    worker_count = cell.get("workerCount")
    api_host_count = cell.get("apiHostCount")
    ramp = profile.get("tenantRamp")
    port = free_port()

    host = None
    workers: list[subprocess.Popen[str]] = []
    extra_hosts: list[subprocess.Popen[str]] = []
    extra_ports: list[int] = []

    try:
        host = start_host(
            executable,
            host_environment(
                connection=connection, schema=schema, mode="api", name="host",
                workload=profile["workload"], executions=executions, port=port,
                worker_axis=worker_count is not None,
                max_jobs=profile.get("maxConcurrentJobs", 8),
                pool=profile.get("maxPoolSize", 100),
                open_tenants=ramp is not None,
                tenant_tag=tenant_tag_requested(ramp),
            ),
            "CAPACITY-HOST-READY",
            "host",
        )

        # Host axis: every HTTP host after the first one starts on its own port with its own in-process
        # worker and shares the same database. They are ready BEFORE the measurement window, like the workers below.
        for index in range(1, api_host_count or 1):
            extra_port = free_port()
            extra_ports.append(extra_port)
            extra_hosts.append(start_host(
                executable,
                host_environment(
                    connection=connection, schema=schema, mode="api", name=f"host-{index + 1}",
                    workload=profile["workload"], executions=executions, port=extra_port,
                    worker_axis=False,
                    max_jobs=profile.get("maxConcurrentJobs", 8),
                    pool=profile.get("maxPoolSize", 100),
                ),
                "CAPACITY-HOST-READY",
                f"host-{index + 1}",
            ))

        # 🚨 The worker processes come up BEFORE THE MEASUREMENT WINDOW and write their
        # ready line. If the startup race entered the window, what you measured would
        # be process startup.
        for index in range(worker_count or 0):
            workers.append(start_host(
                executable,
                host_environment(
                    connection=connection, schema=schema, mode="worker",
                    name=f"worker-{index + 1}", workload=profile["workload"],
                    executions=executions, worker_axis=True,
                    max_jobs=profile.get("maxConcurrentJobs", 8),
                    pool=profile.get("maxPoolSize", 100),
                ),
                "CAPACITY-WORKER-READY",
                f"worker-{index + 1}",
            ))

        spec = {
            "runId": cell["runId"],
            "cellId": cell["cellId"],
            "profile": cell["profile"],
            "scenarios": cell["scenarios"],
            "seedShape": cell["seedShape"],
            "repeat": cell["repeat"],
            "concurrency": cell.get("concurrency", 1),
            "arrivalRatePerSecond": cell.get("arrivalRatePerSecond"),
            "workerCount": worker_count,
            "workerPids": [w.pid for w in workers],
            "apiHostCount": api_host_count,
            "extraBaseAddresses": [f"http://127.0.0.1:{p}/tracon" for p in extra_ports],
            "extraHostPids": [h.pid for h in extra_hosts],
            "hostPid": host.pid,
            "warmupSeconds": profile.get("warmupSeconds", 0),
            "measureSeconds": profile.get("measureSeconds", 0),
            "runsPerTenant": profile.get("runsPerTenant", 0),
            "tenantRamp": driver_ramp(ramp),
            "drainTimeoutSeconds": profile.get("drainTimeoutSeconds", 300),
            "requestTimeoutSeconds": profile.get("requestTimeoutSeconds", 120),
            "baseAddress": f"http://127.0.0.1:{port}/tracon",
            "tenants": ["capacity-a", "capacity-b"],
            "agent": "capacity-agent",
            "schema": schema,
            "outputDirectory": str(cell_dir),
            "workload": profile["workload"],
            "limits": profile["limits"],
            "resourceSampleIntervalSeconds": profile.get("resourceSampleIntervalSeconds", 1),
            "retentionEnabled": False,
            "workloadSeed": profile.get("workloadSeed"),
        }

        spec_path = cell_dir / "spec.json"
        spec_path.write_text(json.dumps(spec, indent=2), encoding="utf-8")

        driver = host_executable(consumer, DRIVER_PROJECT)
        merged = dict(os.environ)
        merged["TRACON_CAPACITY_CONNECTION"] = connection
        merged["TRACON_CAPACITY_EXECUTIONS"] = str(executions)

        result = subprocess.run(
            [str(driver), "cell", "--spec", str(spec_path)],
            cwd=driver.parent, env=merged, text=True, check=False,
        )

        cell_json = cell_dir / "cell.json"

        if not cell_json.exists():
            raise CapacityError(f"{cell['cellId']}: the driver did not write a cell result")

        summary = json.loads(cell_json.read_text(encoding="utf-8"))
        summary["driverExitCode"] = result.returncode
        return summary
    finally:
        for index, worker in enumerate(workers):
            stop(worker, f"worker-{index + 1}")

        for index, extra in enumerate(extra_hosts):
            stop(extra, f"host-{index + 2}")

        stop(host, "host")

        # Only the database this run created is dropped; neither the template nor any
        # other database is affected.
        try:
            database.psql("capacity", f'DROP DATABASE IF EXISTS "{name}"')
        except CapacityError as error:
            print(f"⚠️ {name} could not be dropped: {error}", flush=True)


# --------------------------------------------------------------------------
# Manifest and report
# --------------------------------------------------------------------------

def physical_memory_bytes() -> int | None:
    try:
        if platform.system() == "Darwin":
            return int(subprocess.run(["sysctl", "-n", "hw.memsize"], text=True,
                                      capture_output=True, check=True).stdout.strip())

        if platform.system() == "Linux":
            return os.sysconf("SC_PAGE_SIZE") * os.sysconf("SC_PHYS_PAGES")
    except (subprocess.CalledProcessError, ValueError, OSError, AttributeError):
        return None

    return None


def telemetry_coverage(output: pathlib.Path) -> dict[str, Any]:
    """Telemetry coverage merged from the cells of the run.

    🚨 The coverage is part of the manifest because it used to live only in the untracked
    `cell.json` files, and the answer to "which measurements were available" for a published
    number disappeared together with the machine. The cells are written as the run progresses,
    so this function is called when the manifest is written AGAIN.
    """
    available: set[str] = set()
    unavailable: dict[str, str] = {}
    missing: set[str] = set()
    host_run_worker: set[bool] = set()

    for path in sorted(output.glob("cells/*/cell.json")):
        cell = json.loads(path.read_text(encoding="utf-8"))
        coverage = cell.get("telemetry", {})
        available.update(coverage.get("available", []))
        unavailable.update(coverage.get("unavailable", {}))
        missing.update(coverage.get("missingMandatory", []))

        if cell.get("workers") is not None:
            host_run_worker.add(bool(cell["workers"].get("hostRunWorker")))

    coverage: dict[str, Any] = {
        "available": sorted(available),
        "unavailable": dict(sorted(unavailable.items())),
        "missingMandatory": sorted(missing),
    }

    if host_run_worker:
        # 🚨 Gate of the worker axis: the host's OWN worker must be off, otherwise
        # "1 worker" is two workers. The value is read at run time; if the stored
        # record did not carry it, the evidence would stay only in the untracked file.
        coverage["hostRunWorker"] = sorted(host_run_worker)

    return coverage


def write_manifest(*, output: pathlib.Path, run_id: str, profile: dict[str, Any],
                   version: str, feed: pathlib.Path, database: Database,
                   templates: dict[str, dict[str, Any]], schema: str) -> None:
    """The comparability identity of the run.

    🚨 It holds NO connection string, credential, raw environment dump or user
    content. That is what keeps a number from travelling without the conditions
    it was measured under."""
    commit, dirty, diff_hash = working_tree_state()

    manifest = {
        "runId": run_id,
        "profile": profile["name"],
        "startedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()),
        "commit": commit,
        "dirty": dirty,
        "diffHash": diff_hash,
        "packageVersion": version,
        "packageHashes": package_hashes(feed),
        "operatingSystem": f"{platform.system()} {platform.release()}",
        "architecture": platform.machine(),
        "processorCount": os.cpu_count() or 0,
        "physicalMemoryBytes": physical_memory_bytes(),
        "runtimeVersion": subprocess.run(["dotnet", "--version"], text=True,
                                         capture_output=True, check=False).stdout.strip(),
        "postgreSqlVersion": database.version,
        "databaseImage": database.image,
        "effectiveSettings": {
            "schema": schema,
            "maxConcurrentJobs": str(profile.get("maxConcurrentJobs", 8)),
            "maxPoolSize": str(profile.get("maxPoolSize", 100)),
            "pollIntervalSeconds": "0.1",
            "leaseDurationSeconds": "120",
            "responseCacheEnabled": str(os.environ.get("TRACON_CAPACITY_RESPONSE_CACHE", "0") == "1").lower(),
            "recordingEnabled": "true",
            "retentionEnabled": "false",
            "rateLimitEnabled": str(os.environ.get("TRACON_CAPACITY_RATE_LIMIT", "off").lower() == "instance").lower(),
            "msbuildExtraArguments": " ".join(extra_msbuild_arguments()) or "none",
            "observability": os.environ.get("TRACON_CAPACITY_OBSERVABILITY", "on"),
            "tenantEndpoints": str(os.environ.get("TRACON_CAPACITY_TENANT_ENDPOINTS", "0") == "1").lower(),
            "telemetryLaneLimit": os.environ.get("TRACON_CAPACITY_TELEMETRY_LANE_LIMIT", "product default"),
            "openTenancy": str(profile.get("tenantRamp") is not None).lower(),
            "tenantTag": str(tenant_tag_requested(profile.get("tenantRamp"))).lower(),
            "tenantRamp": json.dumps(profile.get("tenantRamp")) if profile.get("tenantRamp") else "none",
            "quotaEnabled": str(os.environ.get("TRACON_CAPACITY_QUOTA", "0") == "1").lower(),
            "onlineEvalEnabled": str(os.environ.get("TRACON_CAPACITY_ONLINE_EVAL", "0") == "1").lower(),
            "telemetryExporters": "none",
            "warmupSeconds": str(profile.get("warmupSeconds", 0)),
            "measureSeconds": str(profile.get("measureSeconds", 0)),
            "drainTimeoutSeconds": str(profile.get("drainTimeoutSeconds", 300)),
        },
        "seed": {
            shape: f"runs={value['runs']} events={value['events']} bytes={value['bytes']}"
            for shape, value in templates.items()
        },
        "workload": profile["workload"],
        "limits": profile["limits"],
        "workloadSeed": profile.get("workloadSeed"),
        "telemetry": telemetry_coverage(output),
        "disclaimer": (
            "Measured on one machine, one database and one configuration. "
            "This is not an SLA and not a guaranteed capacity."
        ),
    }

    (output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")


# 🚨 A synthetic canary credential. It is NOT a real secret and connects to nothing; its
# purpose is to prove that the redaction scan REALLY looks. A scan that never sees a
# credential cannot be told apart from a scan that does not look.
CANARY_CREDENTIAL = "Password=canary-not-a-real-secret"  # SYNTHETIC-CREDENTIAL


def run_acceptance(consumer: pathlib.Path, database: Database, profile: dict[str, Any],
                   templates: dict[str, dict[str, Any]], schema: str,
                   output: pathlib.Path, version: str) -> int:
    """A short acceptance run against the packed host.

    🚨 It runs only in the `smoke` profile and, when a precondition is missing, it FAILS
    instead of skipping SILENTLY: a skipped provenance check cannot be told apart from
    a passed one in the CI log."""
    executable = consumer / "Tracon.Capacity.Acceptance" / "bin" / "Release" / "net10.0" / (
        "Tracon.Capacity.Acceptance" + (".exe" if platform.system() == "Windows" else "")
    )

    if not executable.exists():
        raise CapacityError(f"the acceptance run was not built: {executable}")

    name = f"cap_{output.name}_acceptance".lower().replace("-", "_")[:60]
    database.psql("capacity", f'DROP DATABASE IF EXISTS "{name}"')
    database.psql("capacity", f'CREATE DATABASE "{name}" TEMPLATE "{templates["empty"]["database"]}"')

    connection = database.connection_for(name)
    executions = output / "acceptance" / EXECUTIONS_DIR
    executions.mkdir(parents=True, exist_ok=True)
    port = free_port()
    host = None

    try:
        environment = host_environment(
            connection=connection, schema=schema, mode="api", name="host",
            workload=profile["workload"], executions=executions, port=port,
            max_jobs=profile.get("maxConcurrentJobs", 8),
            pool=profile.get("maxPoolSize", 100),
        )
        environment["TRACON_CAPACITY_CANARY"] = CANARY_CREDENTIAL

        host = start_host(host_executable(consumer, HOST_PROJECT), environment,
                          "CAPACITY-HOST-READY", "acceptance host")

        merged = dict(os.environ)
        merged.update({
            "TRACON_CAPACITY_ACCEPTANCE_BASE": f"http://127.0.0.1:{port}/tracon",
            "TRACON_CAPACITY_CONNECTION": connection,
            "TRACON_CAPACITY_SCHEMA": schema,
            "TRACON_CAPACITY_VERSION": version,
            "TRACON_CAPACITY_ASSETS": str(consumer / HOST_PROJECT / "obj" / "project.assets.json"),
            "TRACON_CAPACITY_CACHE": str(consumer.parent / "packages"),
            "TRACON_CAPACITY_HOSTDIR": str(consumer / HOST_PROJECT / "bin" / "Release" / "net10.0"),
            "TRACON_CAPACITY_ARTIFACTS": str(output),
            "TRACON_CAPACITY_CANARY": CANARY_CREDENTIAL,
            "TRACON_CAPACITY_EXECUTIONS": str(executions),
        })

        result = subprocess.run([str(executable)], cwd=executable.parent, env=merged,
                                text=True, check=False)
        return result.returncode
    finally:
        stop(host, "acceptance host")
        try:
            database.psql("capacity", f'DROP DATABASE IF EXISTS "{name}"')
        except CapacityError as error:
            print(f"⚠️ {name} could not be dropped: {error}", flush=True)


def build_report(consumer: pathlib.Path, output: pathlib.Path) -> int:
    driver = host_executable(consumer, DRIVER_PROJECT)
    result = subprocess.run([str(driver), "report", "--run", str(output)],
                            cwd=driver.parent, text=True, check=False)
    return result.returncode


# --------------------------------------------------------------------------
# Entry point
# --------------------------------------------------------------------------

def measure(profile_name: str, version: str, output_root: pathlib.Path,
            *, keep_workspace: bool = False, reuse_feed: pathlib.Path | None = None) -> int:
    profile = load_profile(profile_name)

    if not EXACT_VERSION.match(version):
        raise CapacityError(
            f"'{version}' is not an exact version. A floating version ('*', '*-*') is not measured: "
            "a report that does not say which bytes it measured is not evidence."
        )

    commit, dirty, _ = working_tree_state()

    if dirty and "dirty" not in version:
        raise CapacityError(
            "The working tree is dirty. A dirty pack has no provenance: commit your changes "
            "or put 'dirty' in the version (for example 0.0.0-dirty.capacity1). A dirty run cannot be published."
        )

    run_id = time.strftime("%Y%m%d-%H%M%S") + "-" + profile_name
    output = output_root / run_id
    output.mkdir(parents=True, exist_ok=True)

    schema = "capacity"
    workspace = pathlib.Path(tempfile.mkdtemp(prefix="tracon-capacity-"))
    feed = workspace / "feed"
    database = Database(os.environ.get("TRACON_CAPACITY_CONNECTION"))
    failures = 0

    print(f"▶ capacity run {run_id} · profile {profile_name} · version {version}", flush=True)
    print(f"  commit {commit}{' (dirty)' if dirty else ''}", flush=True)

    try:
        if reuse_feed is not None:
            feed = reusable_feed(version, reuse_feed)
            print(f"  reusing packages: {feed}", flush=True)
        else:
            pack(version, feed, dirty=dirty)

        consumer = prepare_consumer(workspace, feed, version)
        database.start()

        templates = build_templates(database, consumer, profile, run_id, schema,
                                    output / "cells" / "_templates")
        write_manifest(output=output, run_id=run_id, profile=profile, version=version,
                       feed=feed, database=database, templates=templates, schema=schema)

        cells = plan_cells(profile, run_id)
        print(f"  {len(cells)} cells planned", flush=True)

        stopped_axes: set[tuple[Any, ...]] = set()

        for index, cell in enumerate(cells, start=1):
            axis = (tuple(cell["scenarios"]), cell["seedShape"])

            # 🚨 When a step is left incomplete, HIGHER steps are not run automatically:
            # running the upper step on an axis that already reached saturation only
            # saturates the machine further and gives no new information.
            if axis in stopped_axes:
                print(f"  [{index}/{len(cells)}] {cell['cellId']} skipped "
                      "(a lower step on the same axis did not finish)", flush=True)
                continue

            print(f"  [{index}/{len(cells)}] {cell['cellId']}", flush=True)

            try:
                summary = run_cell(cell=cell, profile=profile, database=database,
                                   consumer=consumer, templates=templates,
                                   schema=schema, output=output)
            except CapacityError as error:
                print(f"    ❌ {error}", flush=True)
                failures += 1
                stopped_axes.add(axis)
                continue

            status = summary.get("status", "unknown")
            print(f"    {status} · {summary.get('throughputPerSecond', 0)}/s", flush=True)

            if status.startswith("incomplete"):
                stopped_axes.add(axis)

            if status == "invalid":
                failures += 1

        # The manifest is refreshed AFTER the cells: the telemetry coverage is
        # known only once the cells are written.
        write_manifest(output=output, run_id=run_id, profile=profile, version=version,
                       feed=feed, database=database, templates=templates, schema=schema)

        report_code = build_report(consumer, output)

        if profile_name == "smoke":
            print("  acceptance run (packed host)", flush=True)
            acceptance_code = run_acceptance(consumer, database, profile, templates,
                                             schema, output, version)

            if acceptance_code:
                print("❌ the acceptance run failed", flush=True)
                failures += 1

        for shape in templates.values():
            database.psql("capacity", f'DROP DATABASE IF EXISTS "{shape["database"]}"')
        print(f"📄 {output}/report.md", flush=True)
        return 1 if failures or report_code else 0
    finally:
        database.stop()

        if keep_workspace:
            print(f"  workspace kept: {workspace}", flush=True)
        else:
            shutil.rmtree(workspace, ignore_errors=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=(__doc__ or "").split("\n", 1)[0], allow_abbrev=False)
    parser.add_argument("--profile", required=True, help="profile name under bench/capacity/profiles")
    parser.add_argument("--package-version", required=True, help="exact package version to measure")
    parser.add_argument("--output", default=str(DEFAULT_OUTPUT), help="artifact root directory")
    parser.add_argument("--keep-workspace", action="store_true",
                        help="do not delete the temporary consumer directory (for diagnosis)")
    parser.add_argument("--ready-feed", default=None,
                        help="package feed built earlier (only with a 'dirty' version; skips packing while you work on the harness)")
    args = parser.parse_args(argv)

    try:
        return measure(args.profile, args.package_version, pathlib.Path(args.output),
                       keep_workspace=args.keep_workspace,
                       reuse_feed=pathlib.Path(args.ready_feed) if args.ready_feed else None)
    except CapacityError as error:
        print(f"❌ {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        # 🚨 A user interruption also yields a PARTIAL report; it is not turned into success.
        print("\n⚠️ interrupted - completed cells are kept", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
