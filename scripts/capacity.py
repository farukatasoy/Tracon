#!/usr/bin/env python3
"""Tracon HTTP capacity measurement (Faz 166).

Bu script ÖLÇÜMÜN dış kabuğudur: paketleri exact sürümle üretir, repo dışındaki
geçici bir dizinde izole feed'den tüketen bir host kurar, her hücre için temiz
bir veritabanı ve temiz process'ler açar, driver'ı koşturur ve sonunda raporu
üretir.

🚨 Ölçüm opt-in'dir. Standart kapanışa, PR yoluna veya release hattına
otomatik eklenmez; yalnız `smoke` profili CI'da açık bir adım olarak koşar.

🚨 Bağlantı dizesi hiçbir process argümanında geçmez (K-059 sınırının aparata
uygulanması): yalnız environment ile taşınır ve manifest'e yazılmaz.
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

# Ölçümün tükettiği exact sürüm. Kayan sürüm (`*`, `*-*`) REDDEDİLİR: hangi
# baytların ölçüldüğü bilinmeyen bir kapasite raporunun kanıt değeri yoktur.
EXACT_VERSION = re.compile(r"^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$")

POSTGRES_IMAGE = "pgvector/pgvector:pg18"
CONTAINER_PREFIX = "tracon-capacity-"
READY_TIMEOUT_SECONDS = 120

HOST_PROJECT = "Tracon.CapacityHost"
DRIVER_PROJECT = "Tracon.CapacityDriver"

# Host ve worker process'lerinin kendi execution kaydını yazdığı dizinin adı.
EXECUTIONS_DIR = "executions"


class CapacityError(Exception):
    """Ölçüm başlayamaz veya güvenilir biçimde sürdürülemez."""


# --------------------------------------------------------------------------
# Profil
# --------------------------------------------------------------------------

def load_profile(name: str, profile_dir: pathlib.Path = PROFILE_DIR) -> dict[str, Any]:
    """Profili okur ve ölçülebilirliğini doğrular."""
    path = profile_dir / f"{name}.json"

    if not path.exists():
        available = ", ".join(sorted(p.stem for p in profile_dir.glob("*.json")))
        raise CapacityError(f"'{name}' profili yok. Mevcut: {available}")

    profile = json.loads(path.read_text(encoding="utf-8"))
    problems = validate_profile(profile)

    if problems:
        raise CapacityError(f"'{name}' profili ölçülemez:\n  " + "\n  ".join(problems))

    return profile


def validate_profile(profile: dict[str, Any]) -> list[str]:
    """Profilin ölçülemez olmasının her sebebi.

    🚨 Sıfır veya negatif süre, boş senaryo listesi ve sıfır tekrar burada
    reddedilir. Hiç istek göndermeyen bir profil HIZLI bir profil değildir;
    sessizce boş bir rapor üretmesi kapasite raporunu yalancı yapar."""
    problems: list[str] = []

    if not profile.get("scenarios"):
        problems.append("scenarios boş olamaz")

    for scenario in profile.get("scenarios", []):
        if scenario not in ("buffered", "streaming", "queued"):
            problems.append(f"bilinmeyen senaryo: {scenario}")

    if profile.get("repeats", 0) <= 0:
        problems.append("repeats sıfırdan büyük olmalı")

    measure = profile.get("measureSeconds", 0)
    runs_per_tenant = profile.get("runsPerTenant", 0)

    if measure <= 0 and runs_per_tenant <= 0:
        problems.append("measureSeconds veya runsPerTenant birinden biri sıfırdan büyük olmalı")

    if measure > 0 and runs_per_tenant > 0:
        problems.append("measureSeconds ve runsPerTenant iki ayrı sınırdır; yalnız birini ver")

    if profile.get("warmupSeconds", 0) < 0:
        problems.append("warmupSeconds negatif olamaz")

    for value in profile.get("concurrency", [1]):
        if value <= 0:
            problems.append(f"concurrency sıfırdan büyük olmalı: {value}")

    for value in profile.get("arrivalRates", []):
        if value <= 0:
            problems.append(f"arrivalRates sıfırdan büyük olmalı: {value}")

    for value in profile.get("workerCounts", []):
        if value <= 0:
            problems.append(f"workerCounts sıfırdan büyük olmalı: {value}")

    for shape in profile.get("seedShapes", ["empty"]):
        if shape not in ("empty", "full"):
            problems.append(f"bilinmeyen seed şekli: {shape}")

    if profile.get("seedRuns", 0) < 0:
        problems.append("seedRuns negatif olamaz")

    return problems


def plan_cells(profile: dict[str, Any], run_id: str) -> list[dict[str, Any]]:
    """Profili ölçülecek hücrelere açar.

    Worker sayısı AYRI bir eksendir: sweep basamaklarıyla çaprazlanmaz. Bu
    fonksiyon o ayrımı yapıda zorlar - `workerCounts` dolu bir profil
    concurrency listesinden yalnız TEK bir değer alır."""
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
# Süreç yardımcıları
# --------------------------------------------------------------------------

def run(command: list[str], *, cwd: pathlib.Path | None = None,
        env: dict[str, str] | None = None, check: bool = True,
        capture: bool = False) -> subprocess.CompletedProcess[str]:
    """Bir komutu koşturur. MSBuild düğüm yeniden kullanımı KAPALIDIR.

    🚨 Öksüz MSBuild düğümleri yönlendirilmiş boruyu açık tutar ve bekleyen
    process dakikalarca asılı kalır (MEMORY.md, ölçüldü)."""
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
    """Process belirtilen SATIRI yazana kadar bekler.

    🚨 Sabit bir `sleep` DEĞİL: yüklü bir makinede sabit bekleme process
    testini kırılgan yapar ve açılış süresini ölçüm penceresine sokar."""
    deadline = time.monotonic() + timeout

    while time.monotonic() < deadline:
        if process.poll() is not None:
            remaining = process.stdout.read() if process.stdout else ""
            raise CapacityError(
                f"{label} '{line}' yazmadan çıktı (kod {process.returncode}): {remaining[-2000:]}"
            )

        ready = process.stdout.readline() if process.stdout else ""

        if not ready:
            time.sleep(0.02)
            continue

        if line in ready:
            return

    raise CapacityError(f"{label} {timeout:.0f} sn içinde '{line}' yazmadı")


def stop(process: subprocess.Popen[str] | None, label: str) -> None:
    """Bir process'i önce nazikçe, sonra kesin olarak durdurur.

    Sahip olunmayan hiçbir process'e dokunulmaz: yalnız bu koşumun başlattığı
    handle'lar kapatılır."""
    if process is None or process.poll() is not None:
        return

    try:
        process.send_signal(signal.SIGTERM)
        process.wait(timeout=20)
    except subprocess.TimeoutExpired:
        print(f"⚠️ {label} SIGTERM ile durmadı; kill", flush=True)
        process.kill()
        process.wait(timeout=10)
    except ProcessLookupError:
        pass


# --------------------------------------------------------------------------
# Paket ve izole tüketici
# --------------------------------------------------------------------------

def git(*args: str) -> str:
    result = subprocess.run(["git", *args], cwd=ROOT, text=True, capture_output=True, check=False)
    return result.stdout.strip() if result.returncode == 0 else ""


def working_tree_state() -> tuple[str, bool, str | None]:
    """Commit, kirlilik ve kirliyse diff'in hash'i."""
    commit = git("rev-parse", "HEAD") or "unknown"
    status = git("status", "--porcelain")
    dirty = bool(status)
    diff_hash = None

    if dirty:
        diff = subprocess.run(["git", "diff", "HEAD"], cwd=ROOT, text=True,
                              capture_output=True, check=False).stdout
        diff_hash = hashlib.sha256(diff.encode("utf-8")).hexdigest()[:16]

    return commit, dirty, diff_hash


def pack(version: str, feed: pathlib.Path, *, dirty: bool) -> None:
    """Exact sürümle paketleri üretir ve izole feed'e koyar."""
    if feed.exists():
        shutil.rmtree(feed)

    feed.mkdir(parents=True)

    env = {"MinVerVersionOverride": version}
    command = ["dotnet", "pack", str(PACKABLE_SOLUTION_FILTER), "-c", "Release", "-o", str(feed)]

    if dirty:
        # Repo'nun kendi kapısı: kirli bir pack'in provenance'ı yoktur, bu
        # yüzden sürüm 'dirty' taşımak ZORUNDADIR ve CI'da hiç üretilemez.
        command.append("-p:TraconAllowDirtyPack=true")

    print(f"$ {' '.join(command)}  (MinVerVersionOverride={version})", flush=True)
    run(command, cwd=ROOT, env=env)


def package_hashes(feed: pathlib.Path) -> dict[str, str]:
    return {
        path.name: hashlib.sha256(path.read_bytes()).hexdigest()
        for path in sorted(feed.glob("*.nupkg"))
    }


def prepare_consumer(workspace: pathlib.Path, feed: pathlib.Path, version: str) -> pathlib.Path:
    """Repo DIŞINDA izole bir tüketici kurar ve derler."""
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
        run(["dotnet", "build", str(csproj), "-c", "Release", "--no-restore", version_arg],
            cwd=consumer, env=env)

    verify_isolation(consumer, cache, version)
    return consumer


def verify_isolation(consumer: pathlib.Path, cache: pathlib.Path, version: str) -> None:
    """Host'un GERÇEKTEN izole feed'den ve exact sürümden çözdüğünü kanıtlar.

    🚨 Bu adım olmadan ölçüm sessizce global cache'teki eski bir paketi
    tüketebilir ve rapor hangi baytları ölçtüğünü bilemez."""
    assets = consumer / HOST_PROJECT / "obj" / "project.assets.json"

    if not assets.exists():
        raise CapacityError(f"{HOST_PROJECT} restore çıktısı yok: {assets}")

    text = assets.read_text(encoding="utf-8")

    if str(cache) not in text:
        raise CapacityError(f"{HOST_PROJECT} izole NUGET_PACKAGES kullanmadı")

    if f'"Tracon/{version}"' not in text:
        raise CapacityError(f"{HOST_PROJECT} exact sürüm {version} çözmedi")

    document = json.loads(text)

    for library in document.get("libraries", {}):
        name, _, resolved = library.partition("/")

        if name.startswith("Tracon") and resolved != version:
            raise CapacityError(f"{name} beklenen sürümde değil: {resolved} != {version}")


# --------------------------------------------------------------------------
# Veritabanı
# --------------------------------------------------------------------------

class Database:
    """Koşuma özel PostgreSQL. Yalnız bu koşumun yarattığı şey temizlenir."""

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

        # 🚨 `pg_isready` TEK BAŞINA yetmez: resmî imaj önce yalnız UNIX
        # soketinde dinleyen geçici bir sunucu açar, init script'lerini koşar ve
        # onu KAPATIR. O pencerede pg_isready başarı döner, sonraki ilk sorgu
        # "the database system is shutting down" alır - ölçüldü. Bu yüzden
        # hazırlık TCP üzerinden gerçek bir sorguyla ve ÜST ÜSTE üç kez
        # doğrulanır; geçici sunucu TCP'de hiç dinlemez.
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

        raise CapacityError("PostgreSQL container hazır olmadı")

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
                "Harici bir sunucuda veritabanı yönetimi desteklenmiyor; "
                "TRACON_CAPACITY_CONNECTION olmadan koşun."
            )

        result = run(["docker", "exec", self.container, "psql", "-h", "127.0.0.1",
                      "-U", "capacity", "-d", database, "-v", "ON_ERROR_STOP=1", "-tAc", sql],
                     check=False, capture=True)

        if result.returncode != 0:
            raise CapacityError(f"psql başarısız: {result.stderr.strip()[:500]}")

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
# Host process'leri
# --------------------------------------------------------------------------

def host_executable(consumer: pathlib.Path, project: str) -> pathlib.Path:
    name = project + (".exe" if platform.system() == "Windows" else "")
    path = consumer / project / "bin" / "Release" / "net10.0" / name

    if not path.exists():
        raise CapacityError(f"{project} derlenmedi: {path}")

    return path


def host_environment(*, connection: str, schema: str, mode: str, name: str,
                     workload: dict[str, Any], executions: pathlib.Path,
                     port: int = 0, worker_axis: bool = False,
                     max_jobs: int = 8, pool: int = 100,
                     seed_runs: int = 0) -> dict[str, str]:
    """Bir host process'inin environment'ı.

    🚨 Bağlantı dizesi yalnız BURADA, environment ile taşınır. Bir process
    argümanı `ps` çıktısında görünür; kapasite koşumunun canlı bir credential'ı
    ölçüm boyunca elinde tutması bunu gerçek bir sızıntı yoluna çevirirdi."""
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
        raise CapacityError(f"{label} çıkış {result.returncode}: {result.stdout[-2000:]}{result.stderr[-2000:]}")

    return result.stdout


def free_port() -> int:
    import socket

    with socket.socket() as probe:
        probe.bind(("127.0.0.1", 0))
        return probe.getsockname()[1]


# --------------------------------------------------------------------------
# Şablon veritabanı
# --------------------------------------------------------------------------

def template_name(shape: str, run_id: str) -> str:
    return f"cap_{run_id}_{shape}_tpl".lower()


def build_templates(database: Database, consumer: pathlib.Path, profile: dict[str, Any],
                    run_id: str, schema: str, executions: pathlib.Path) -> dict[str, dict[str, Any]]:
    """Her seed şekli için bir ŞABLON veritabanı kurar.

    🚨 Plandan sapma (gerekçesi faz dokümanında): plan hücre başına izole
    *schema* diyordu. Ölçüldü - `full` fixture'ı public store yolundan yazmak
    10.000 run × 21 store çağrısıdır; 36 dolu hücrenin her birinde tekrarlamak
    seed'i saatlerce kritik yola koyardı. Bunun yerine şekil başına BİR şablon
    veritabanı kurulur ve her hücre ondan `CREATE DATABASE ... TEMPLATE ...`
    ile kendi veritabanını alır. Yalıtım zayıflamaz, GÜÇLENİR (schema değil
    veritabanı sınırı) ve seed süresi hiçbir ölçüm penceresine girmez."""
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
                raise CapacityError("seed modu ne yazdığını raporlamadı")

            seeded = {"runs": int(match.group(1)), "events": int(match.group(2))}

        # 🚨 Beyan edilen değil GERÇEK satır sayıları doğrulanır. Yazdığından
        # fazlasını iddia eden bir seed, "dolu veritabanı" hücresini kurgu yapar.
        actual_runs = int(database.psql(name, f'SELECT COUNT(*) FROM "{schema}".runs'))
        actual_events = int(database.psql(name, f'SELECT COUNT(*) FROM "{schema}".run_events'))

        if shape == "full" and (actual_runs != seeded["runs"] or actual_events != seeded["events"]):
            raise CapacityError(
                f"seed beyanı satır sayısıyla uyuşmuyor: beyan {seeded}, "
                f"gerçek runs={actual_runs} events={actual_events}"
            )

        size = int(database.psql(name, "SELECT pg_database_size(current_database())"))

        templates[shape] = {
            "database": name,
            "runs": actual_runs,
            "events": actual_events,
            "bytes": size,
        }

        print(f"  şablon {shape}: runs={actual_runs} events={actual_events} "
              f"boyut={size / 1024 / 1024:.1f} MiB", flush=True)

    return templates


# --------------------------------------------------------------------------
# Hücre koşumu
# --------------------------------------------------------------------------

def run_cell(*, cell: dict[str, Any], profile: dict[str, Any], database: Database,
             consumer: pathlib.Path, templates: dict[str, dict[str, Any]],
             schema: str, output: pathlib.Path) -> dict[str, Any]:
    """Bir hücreyi temiz veritabanı ve temiz process'lerle koşturur."""
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
    port = free_port()

    host = None
    workers: list[subprocess.Popen[str]] = []

    try:
        host = start_host(
            executable,
            host_environment(
                connection=connection, schema=schema, mode="api", name="host",
                workload=profile["workload"], executions=executions, port=port,
                worker_axis=worker_count is not None,
                max_jobs=profile.get("maxConcurrentJobs", 8),
                pool=profile.get("maxPoolSize", 100),
            ),
            "CAPACITY-HOST-READY",
            "host",
        )

        # 🚨 Worker process'leri ÖLÇÜM PENCERESİNDEN ÖNCE ayağa kalkar ve
        # hazır satırını yazar. Faz 157'nin devir notu bunu açıkça ister:
        # açılış yarışı pencereye girerse ölçtüğün şey process başlatmadır.
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
            "hostPid": host.pid,
            "warmupSeconds": profile.get("warmupSeconds", 0),
            "measureSeconds": profile.get("measureSeconds", 0),
            "runsPerTenant": profile.get("runsPerTenant", 0),
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
            raise CapacityError(f"{cell['cellId']}: driver hücre sonucu yazmadı")

        summary = json.loads(cell_json.read_text(encoding="utf-8"))
        summary["driverExitCode"] = result.returncode
        return summary
    finally:
        for index, worker in enumerate(workers):
            stop(worker, f"worker-{index + 1}")

        stop(host, "host")

        # Yalnız bu koşumun yarattığı veritabanı düşürülür; şablon ve başka
        # hiçbir veritabanı etkilenmez.
        try:
            database.psql("capacity", f'DROP DATABASE IF EXISTS "{name}"')
        except CapacityError as error:
            print(f"⚠️ {name} düşürülemedi: {error}", flush=True)


# --------------------------------------------------------------------------
# Manifest ve rapor
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


def write_manifest(*, output: pathlib.Path, run_id: str, profile: dict[str, Any],
                   version: str, feed: pathlib.Path, database: Database,
                   templates: dict[str, dict[str, Any]], schema: str) -> None:
    """Koşumun karşılaştırılabilirlik kimliği.

    🚨 İçinde bağlantı dizesi, credential, ham environment dökümü ve kullanıcı
    içeriği YOKTUR. Sayının koşulları olmadan seyahat etmesini durduran şey
    budur."""
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
            "responseCacheEnabled": "false",
            "recordingEnabled": "true",
            "retentionEnabled": "false",
            "rateLimitEnabled": "false",
            "quotaEnabled": "false",
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
        "telemetry": {"available": [], "unavailable": {}, "missingMandatory": []},
        "disclaimer": (
            "Measured on one machine, one database and one configuration. "
            "This is not an SLA and not a guaranteed capacity."
        ),
    }

    (output / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")


# 🚨 Sentetik bir canary credential. Gerçek bir secret DEĞİLDİR ve hiçbir yere
# bağlanmaz; amacı redaction taramasının GERÇEKTEN baktığını kanıtlamaktır.
# Hiç credential görmeyen bir tarama, bakmayan bir taramadan ayırt edilemez.
CANARY_CREDENTIAL = "Password=canary-166-not-a-real-secret"  # SYNTHETIC-CREDENTIAL


def run_acceptance(consumer: pathlib.Path, database: Database, profile: dict[str, Any],
                   templates: dict[str, dict[str, Any]], schema: str,
                   output: pathlib.Path, version: str) -> int:
    """Packed host'a karşı kısa kabul koşumu.

    🚨 Yalnız `smoke` profilinde koşar ve önkoşul eksikse SESSİZ SKIP değil
    BAŞARISIZ olur: atlanmış bir provenance kontrolü CI log'unda geçmiş bir
    kontrolden ayırt edilemez."""
    executable = consumer / "Tracon.Capacity.Acceptance" / "bin" / "Release" / "net10.0" / (
        "Tracon.Capacity.Acceptance" + (".exe" if platform.system() == "Windows" else "")
    )

    if not executable.exists():
        raise CapacityError(f"kabul koşumu derlenmedi: {executable}")

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
            print(f"⚠️ {name} düşürülemedi: {error}", flush=True)


def build_report(consumer: pathlib.Path, output: pathlib.Path) -> int:
    driver = host_executable(consumer, DRIVER_PROJECT)
    result = subprocess.run([str(driver), "report", "--run", str(output)],
                            cwd=driver.parent, text=True, check=False)
    return result.returncode


# --------------------------------------------------------------------------
# Giriş
# --------------------------------------------------------------------------

def measure(profile_name: str, version: str, output_root: pathlib.Path,
            *, keep_workspace: bool = False) -> int:
    profile = load_profile(profile_name)

    if not EXACT_VERSION.match(version):
        raise CapacityError(
            f"'{version}' exact bir sürüm değil. Kayan sürüm ('*', '*-*') ile ölçüm yapılmaz: "
            "hangi baytların ölçüldüğü bilinmeyen bir rapor kanıt değildir."
        )

    commit, dirty, _ = working_tree_state()

    if dirty and "dirty" not in version:
        raise CapacityError(
            "Çalışma ağacı kirli. Kirli bir pack'in provenance'ı yoktur; ya commit edin "
            "ya da sürüme 'dirty' koyun (ör. 0.0.0-dirty.capacity166). Kirli koşum yayına giremez."
        )

    run_id = time.strftime("%Y%m%d-%H%M%S") + "-" + profile_name
    output = output_root / run_id
    output.mkdir(parents=True, exist_ok=True)

    schema = "capacity"
    workspace = pathlib.Path(tempfile.mkdtemp(prefix="tracon-capacity-"))
    feed = workspace / "feed"
    database = Database(os.environ.get("TRACON_CAPACITY_CONNECTION"))
    failures = 0

    print(f"▶ kapasite koşumu {run_id} · profil {profile_name} · sürüm {version}", flush=True)
    print(f"  commit {commit}{' (kirli)' if dirty else ''}", flush=True)

    try:
        pack(version, feed, dirty=dirty)
        consumer = prepare_consumer(workspace, feed, version)
        database.start()

        templates = build_templates(database, consumer, profile, run_id, schema,
                                    output / "cells" / "_templates")
        write_manifest(output=output, run_id=run_id, profile=profile, version=version,
                       feed=feed, database=database, templates=templates, schema=schema)

        cells = plan_cells(profile, run_id)
        print(f"  {len(cells)} hücre planlandı", flush=True)

        stopped_axes: set[tuple[Any, ...]] = set()

        for index, cell in enumerate(cells, start=1):
            axis = (tuple(cell["scenarios"]), cell["seedShape"])

            # 🚨 Bir basamak yarım kaldıysa DAHA YÜKSEK basamaklar otomatik
            # koşulmaz: doygunluğa ulaşmış bir eksende üst basamağı koşmak
            # yalnız makineyi daha çok doyurur, yeni bilgi vermez.
            if axis in stopped_axes:
                print(f"  [{index}/{len(cells)}] {cell['cellId']} atlandı "
                      "(aynı eksende daha alçak bir basamak yarım kaldı)", flush=True)
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

        report_code = build_report(consumer, output)

        if profile_name == "smoke":
            print("  kabul koşumu (packed host)", flush=True)
            acceptance_code = run_acceptance(consumer, database, profile, templates,
                                             schema, output, version)

            if acceptance_code:
                print("❌ kabul koşumu düştü", flush=True)
                failures += 1

        for shape in templates.values():
            database.psql("capacity", f'DROP DATABASE IF EXISTS "{shape["database"]}"')
        print(f"📄 {output}/report.md", flush=True)
        return 1 if failures or report_code else 0
    finally:
        database.stop()

        if keep_workspace:
            print(f"  çalışma alanı bırakıldı: {workspace}", flush=True)
        else:
            shutil.rmtree(workspace, ignore_errors=True)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--profil", required=True, help="bench/capacity/profiles altındaki profil adı")
    parser.add_argument("--surum", required=True, help="ölçülecek exact paket sürümü")
    parser.add_argument("--cikti", default=str(DEFAULT_OUTPUT), help="artifact kökü")
    parser.add_argument("--calisma-alanini-birak", action="store_true",
                        help="geçici tüketici dizinini silme (teşhis için)")
    args = parser.parse_args(argv)

    try:
        return measure(args.profil, args.surum, pathlib.Path(args.cikti),
                       keep_workspace=args.calisma_alanini_birak)
    except CapacityError as error:
        print(f"❌ {error}", file=sys.stderr)
        return 1
    except KeyboardInterrupt:
        # 🚨 Kullanıcı kesintisi de PARTIAL rapor üretir; başarıya çevrilmez.
        print("\n⚠️ kesildi - tamamlanan hücreler korundu", file=sys.stderr)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
