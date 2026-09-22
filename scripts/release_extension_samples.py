#!/usr/bin/env python3
"""Verify extension samples, the Native AOT smoke, and the net8.0 consumer against one exact packed Tracon version."""

from __future__ import annotations

import os
import pathlib
import subprocess
import tempfile


SAMPLE_TEST_PROJECTS = (
    "Tracon.Samples.FileRunStore.Tests",
    "Tracon.Samples.CustomModelProvider.Tests",
    "Tracon.Samples.CustomRunJudge.Tests",
    "Tracon.Samples.CustomAgentSource.Tests",
    "Tracon.Samples.CustomTool.Tests",
    "Tracon.Samples.CustomJobHandler.Tests",
)

# Deliberate, justified exclusions from SAMPLE_TEST_PROJECTS: a sample test
# project directory that exists but is not run by this gate, with the reason
# WHY. Empty by default - a project that lands here without a reason string
# fails validate_sample_inventory just as loudly as one that is missing
# entirely (Faz 123, BL-052: a bare tuple update let a real sample - added in
# Faz 120 - go unrun for three phases because nothing compared the tuple
# against the samples/ directory it claims to enumerate).
SAMPLE_TEST_EXCLUSIONS: dict[str, str] = {}

AOT_PROJECT = "Tracon.Samples.ExtensionAotSmoke"

# Faz 183. Every other sample runs on net10.0 (samples/Directory.Build.props),
# so without this one no consumer path ever restored the net8.0 dependency group
# of the packed packages or ran them on the net8.0 runtime.
NET8_CONSUMER_PROJECT = "Tracon.Samples.Net8Consumer"
NET8_CONSUMER_FRAMEWORK = "net8.0"


def validate_sample_inventory(root: pathlib.Path) -> list[str]:
    """The samples/Tracon.Samples.*.Tests directory inventory must match
    SAMPLE_TEST_PROJECTS union SAMPLE_TEST_EXCLUSIONS exactly - neither a new
    sample missing from both, nor a stale tuple/exclusion entry naming a
    project that no longer exists."""
    errors: list[str] = []
    samples = root / "samples"

    inventory = {
        path.parent.name
        for path in samples.glob("Tracon.Samples.*.Tests/*.csproj")
    }
    accounted = set(SAMPLE_TEST_PROJECTS) | set(SAMPLE_TEST_EXCLUSIONS)

    for name in sorted(inventory - accounted):
        errors.append(
            f"samples/{name} is not in SAMPLE_TEST_PROJECTS and has no "
            "SAMPLE_TEST_EXCLUSIONS entry (scripts/release_extension_samples.py)"
        )

    for name in sorted(accounted - inventory):
        errors.append(f"SAMPLE_TEST_PROJECTS/SAMPLE_TEST_EXCLUSIONS names a project that does not exist: {name}")

    for name, reason in SAMPLE_TEST_EXCLUSIONS.items():
        if not reason or not reason.strip():
            errors.append(f"SAMPLE_TEST_EXCLUSIONS['{name}'] has no reason")

    return errors


def validate_sample_contract(root: pathlib.Path, version: str) -> list[str]:
    """Return deterministic release-consumer configuration violations."""
    errors: list[str] = validate_sample_inventory(root)
    samples = root / "samples"

    if not version or "*" in version:
        errors.append("exact Tracon sample package version is required")

    for project in (*SAMPLE_TEST_PROJECTS, AOT_PROJECT, NET8_CONSUMER_PROJECT):
        csproj = samples / project / f"{project}.csproj"
        if not csproj.exists():
            errors.append(f"missing release sample project: {project}")

    # Without its own <TargetFramework> the consumer inherits net10.0 from
    # samples/Directory.Build.props and still passes - proving nothing.
    net8_csproj = samples / NET8_CONSUMER_PROJECT / f"{NET8_CONSUMER_PROJECT}.csproj"
    if net8_csproj.exists() and (
        f"<TargetFramework>{NET8_CONSUMER_FRAMEWORK}</TargetFramework>"
        not in net8_csproj.read_text(encoding="utf-8")
    ):
        errors.append(f"{net8_csproj.relative_to(root)} must target {NET8_CONSUMER_FRAMEWORK}")

    for csproj in sorted(samples.glob("Tracon.Samples.*/*.csproj")):
        project_text = csproj.read_text(encoding="utf-8")
        if 'VersionOverride="*-*"' in project_text:
            errors.append(f"{csproj.relative_to(root)} uses a wildcard VersionOverride")
        for line in project_text.splitlines():
            if "ProjectReference" in line and "src/Tracon" in line.replace("\\", "/"):
                errors.append(f"{csproj.relative_to(root)} references Tracon source")

    return errors


def _run(command: list[str], *, root: pathlib.Path, environment: dict[str, str]) -> int:
    print(f"$ {' '.join(command)}", flush=True)
    return subprocess.run(command, cwd=root, env=environment, check=False).returncode


def _project_assets_json(root: pathlib.Path, project: str) -> pathlib.Path:
    """This repo centralizes intermediate output under the SDK's
    <ArtifactsPath> (Directory.Build.props): a project's obj/ lives at
    artifacts/obj/<ProjectName>/, never beside its own .csproj."""
    return root / "artifacts" / "obj" / project / "project.assets.json"


def _net8_consumer_apphost(root: pathlib.Path) -> pathlib.Path:
    """Single-framework output under the SDK's <ArtifactsPath>: artifacts/bin/<P>/release/."""
    name = NET8_CONSUMER_PROJECT + (".exe" if os.name == "nt" else "")
    return root / "artifacts" / "bin" / NET8_CONSUMER_PROJECT / "release" / name


def _current_runtime_identifier(*, root: pathlib.Path, environment: dict[str, str]) -> str:
    """Native AOT publish needs a concrete RID; `--use-current-runtime` does not
    reliably pull the matching runtime pack into an isolated NUGET_PACKAGES
    cache, so the RID is resolved once, explicitly, and passed to both restore
    and publish."""
    result = subprocess.run(
        ["dotnet", "--info"], cwd=root, env=environment, check=True, capture_output=True, text=True,
    )
    for line in result.stdout.splitlines():
        stripped = line.strip()
        if stripped.startswith("RID:"):
            return stripped.split(":", 1)[1].strip()
    raise RuntimeError("'dotnet --info' did not report a RID")


def verify(root: pathlib.Path, release_dir: pathlib.Path, version: str) -> int:
    errors = validate_sample_contract(root, version)
    stale = sorted(
        path.name for path in release_dir.glob("Tracon*.nupkg")
        if not path.name.endswith(f".{version}.nupkg")
    )
    if stale:
        errors.append(f"release feed contains stale Tracon packages: {', '.join(stale)}")

    if errors:
        print("❌ Extension sample contract ihlal edildi:")
        for error in errors:
            print(f"  {error}")
        return 1

    with tempfile.TemporaryDirectory(prefix="tracon-release-samples-") as temporary:
        temp = pathlib.Path(temporary)
        package_cache = temp / "packages"
        package_cache.mkdir()
        nuget_config = temp / "NuGet.config"
        nuget_config.write_text(
            f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="tracon-release" value="{release_dir}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="tracon-release"><package pattern="Tracon*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
""",
            encoding="utf-8",
        )

        environment = os.environ.copy()
        environment["NUGET_PACKAGES"] = str(package_cache)
        property_arg = f"-p:TraconSamplePackageVersion={version}"

        for project in SAMPLE_TEST_PROJECTS:
            csproj = root / "samples" / project / f"{project}.csproj"
            restore = ["dotnet", "restore", str(csproj), "--configfile", str(nuget_config), property_arg]
            if _run(restore, root=root, environment=environment):
                return 1
            test = ["dotnet", "test", str(csproj), "-c", "Release", "--no-restore", property_arg]
            if _run(test, root=root, environment=environment):
                return 1

            assets = _project_assets_json(root, project)
            if str(package_cache) not in assets.read_text(encoding="utf-8"):
                print(f"❌ {project}: restore isolated NUGET_PACKAGES kullanmadı")
                return 1

        contract_assets = _project_assets_json(root, "Tracon.Testing.Contracts.Xunit").read_text(encoding="utf-8")
        if '"Tracon.Core/' in contract_assets:
            print("❌ Testing.Contracts.Xunit graph'ına Tracon.Core sızdı")
            return 1

        aot_csproj = root / "samples" / AOT_PROJECT / f"{AOT_PROJECT}.csproj"
        # Native AOT publish needs a concrete RID; the sample carries none of its
        # own (a real consumer publishes for whatever RID their deploy target
        # is), so this run resolves it to the machine actually running the gate.
        # A separate `dotnet restore` step (as used for the other samples) does
        # NOT reliably add the RID-specific ILCompiler runtime package to an
        # isolated NUGET_PACKAGES cache; restore and publish run as ONE command
        # instead, matching how a real consumer publishes.
        rid = _current_runtime_identifier(root=root, environment=environment)
        publish_dir = temp / "aot"
        publish = [
            "dotnet", "publish", str(aot_csproj), "-c", "Release",
            "--configfile", str(nuget_config), "-r", rid, property_arg, "-o", str(publish_dir),
        ]
        if _run(publish, root=root, environment=environment):
            return 1

        aot_assets = _project_assets_json(root, AOT_PROJECT).read_text(encoding="utf-8")
        if '"Tracon.Testing/' in aot_assets or '"Tracon.Testing.Contracts.Xunit/' in aot_assets:
            print("❌ Meta package graph'ına testing paketi sızdı")
            return 1

        executable = publish_dir / AOT_PROJECT
        if _run([str(executable)], root=root, environment=environment):
            return 1

        # The program itself exits non-zero unless it runs on the net8.0
        # runtime; a missing runtime fails here with the host's own message.
        #
        # 🚨 Built, then started through its apphost - NOT `dotnet run`.
        # Measured (Faz 183): `dotnet run` hands the app the MUXER's own root as
        # DOTNET_ROOT, so a net8.0 runtime installed under the caller's
        # DOTNET_ROOT is invisible to it. The apphost resolves the runtime the
        # way a deployed framework-dependent app does, and the way every test
        # leg of the framework matrix does.
        net8_csproj = root / "samples" / NET8_CONSUMER_PROJECT / f"{NET8_CONSUMER_PROJECT}.csproj"
        restore = ["dotnet", "restore", str(net8_csproj), "--configfile", str(nuget_config), property_arg]
        if _run(restore, root=root, environment=environment):
            return 1
        build = ["dotnet", "build", str(net8_csproj), "-c", "Release", "--no-restore", property_arg]
        if _run(build, root=root, environment=environment):
            return 1
        if _run([str(_net8_consumer_apphost(root))], root=root, environment=environment):
            return 1
        net8_assets = _project_assets_json(root, NET8_CONSUMER_PROJECT).read_text(encoding="utf-8")
        if str(package_cache) not in net8_assets:
            print(f"❌ {NET8_CONSUMER_PROJECT}: restore isolated NUGET_PACKAGES kullanmadı")
            return 1

    print(f"✅ {len(SAMPLE_TEST_PROJECTS)} exact-version packed sample, Native AOT smoke"
          f" ve {NET8_CONSUMER_FRAMEWORK} tüketici smoke: {version}")
    return 0
