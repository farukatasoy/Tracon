#!/usr/bin/env python3
"""Verify extension samples against one exact packed AgentPrism version."""

from __future__ import annotations

import os
import pathlib
import subprocess
import tempfile


SAMPLE_TEST_PROJECTS = (
    "AgentPrism.Samples.FileRunStore.Tests",
    "AgentPrism.Samples.CustomModelProvider.Tests",
    "AgentPrism.Samples.CustomRunJudge.Tests",
    "AgentPrism.Samples.CustomAgentSource.Tests",
    "AgentPrism.Samples.CustomTool.Tests",
)
AOT_PROJECT = "AgentPrism.Samples.ExtensionAotSmoke"


def validate_sample_contract(root: pathlib.Path, version: str) -> list[str]:
    """Return deterministic release-consumer configuration violations."""
    errors: list[str] = []
    samples = root / "samples"

    if not version or "*" in version:
        errors.append("exact AgentPrism sample package version is required")

    for project in (*SAMPLE_TEST_PROJECTS, AOT_PROJECT):
        csproj = samples / project / f"{project}.csproj"
        if not csproj.exists():
            errors.append(f"missing release sample project: {project}")

    for csproj in sorted(samples.glob("AgentPrism.Samples.*/*.csproj")):
        project_text = csproj.read_text(encoding="utf-8")
        if 'VersionOverride="*-*"' in project_text:
            errors.append(f"{csproj.relative_to(root)} uses a wildcard VersionOverride")
        for line in project_text.splitlines():
            if "ProjectReference" in line and "src/AgentPrism" in line.replace("\\", "/"):
                errors.append(f"{csproj.relative_to(root)} references AgentPrism source")

    return errors


def _run(command: list[str], *, root: pathlib.Path, environment: dict[str, str]) -> int:
    print(f"$ {' '.join(command)}", flush=True)
    return subprocess.run(command, cwd=root, env=environment, check=False).returncode


def _project_assets_json(root: pathlib.Path, project: str) -> pathlib.Path:
    """This repo centralizes intermediate output under the SDK's
    <ArtifactsPath> (Directory.Build.props): a project's obj/ lives at
    artifacts/obj/<ProjectName>/, never beside its own .csproj."""
    return root / "artifacts" / "obj" / project / "project.assets.json"


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
        path.name for path in release_dir.glob("AgentPrism*.nupkg")
        if not path.name.endswith(f".{version}.nupkg")
    )
    if stale:
        errors.append(f"release feed contains stale AgentPrism packages: {', '.join(stale)}")

    if errors:
        print("❌ Extension sample contract ihlal edildi:")
        for error in errors:
            print(f"  {error}")
        return 1

    with tempfile.TemporaryDirectory(prefix="agentprism-release-samples-") as temporary:
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
    <add key="agentprism-release" value="{release_dir}" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="agentprism-release"><package pattern="AgentPrism*" /></packageSource>
    <packageSource key="nuget.org"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
""",
            encoding="utf-8",
        )

        environment = os.environ.copy()
        environment["NUGET_PACKAGES"] = str(package_cache)
        property_arg = f"-p:AgentPrismSamplePackageVersion={version}"

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

        contract_assets = _project_assets_json(root, "AgentPrism.Testing.Contracts.Xunit").read_text(encoding="utf-8")
        if '"AgentPrism.Core/' in contract_assets:
            print("❌ Testing.Contracts.Xunit graph'ına AgentPrism.Core sızdı")
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
        if '"AgentPrism.Testing/' in aot_assets or '"AgentPrism.Testing.Contracts.Xunit/' in aot_assets:
            print("❌ Meta package graph'ına testing paketi sızdı")
            return 1

        executable = publish_dir / AOT_PROJECT
        if _run([str(executable)], root=root, environment=environment):
            return 1

    print(f"✅ Beş exact-version packed sample ve Native AOT smoke: {version}")
    return 0
