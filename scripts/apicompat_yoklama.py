#!/usr/bin/env python3
"""Measures what ApiCompat's baseline validation reports and what it misses (Faz 237, §237.3).

The 1.x rule table (`reference/versioning.md`) forbids changes that the SDK may
not see. This script packs a tiny library as a baseline, then packs one
variant of it per kind of change and reads the suppression file ApiCompat
writes - the file `breaking_changes.py` reads in the release rehearsal.

Measured 2026-10-24 on SDK 10.0.402: the SDK does not report a `required`
member added to an existing type, nor an optional member that becomes
`required`; `scripts/breaking_changes.py` carries its own reader for that. Run
the script again when the .NET SDK moves. A difference from `EXPECTED` is a
finding: the gate's assumptions need a new look, in either direction.

    python3 scripts/apicompat_yoklama.py
"""
from __future__ import annotations

import os
import pathlib
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
from collections.abc import Sequence

# The diagnostics ApiCompat writes for each variant, baseline validation only
# (the mode of the rehearsal). `-` means no record at all.
EXPECTED: dict[str, frozenset[str]] = {
    "V_IFACE_ABSTRACT": frozenset({"CP0006"}),
    "V_IFACE_DEFAULT": frozenset(),
    "V_ABSTRACT_MEMBER": frozenset({"CP0005"}),
    "V_REQUIRED_NEW": frozenset(),
    "V_OPTIONAL_TO_REQUIRED": frozenset(),
    "V_REQUIRED_TO_OPTIONAL": frozenset(),
    "V_ENUM_RENUMBER": frozenset({"CP0011"}),
    "V_ENUM_INSERT": frozenset({"CP0011"}),
    "V_ENUM_DELETE": frozenset({"CP0002"}),
    "V_ENUM_ADD": frozenset(),
    "V_METHOD_OPTIONAL": frozenset({"CP0002"}),
    "V_METHOD_OVERLOAD": frozenset(),
    "V_RECORD_POSITIONAL": frozenset({"CP0002"}),
    "V_RECORD_POSITIONAL_OPTIONAL": frozenset({"CP0002"}),
    "V_NEW_OPTIONAL_MEMBER": frozenset(),
    "V_NEW_TYPE": frozenset(),
    "CONTROL_NONE": frozenset(),
}

DESCRIPTIONS = {
    "V_IFACE_ABSTRACT": "interface member without a body",
    "V_IFACE_DEFAULT": "interface member with a default body",
    "V_ABSTRACT_MEMBER": "abstract member on an abstract class",
    "V_REQUIRED_NEW": "new `required` member on an existing type",
    "V_OPTIONAL_TO_REQUIRED": "existing optional member becomes `required`",
    "V_REQUIRED_TO_OPTIONAL": "`required` member becomes optional",
    "V_ENUM_RENUMBER": "enum value gets another number",
    "V_ENUM_INSERT": "enum value inserted before another (implicit renumber)",
    "V_ENUM_DELETE": "enum value removed",
    "V_ENUM_ADD": "enum value added (control)",
    "V_METHOD_OPTIONAL": "optional parameter added to a method",
    "V_METHOD_OVERLOAD": "new overload (control)",
    "V_RECORD_POSITIONAL": "positional record parameter added",
    "V_RECORD_POSITIONAL_OPTIONAL": "positional record parameter with a default added",
    "V_NEW_OPTIONAL_MEMBER": "new optional member (control)",
    "V_NEW_TYPE": "new type (control)",
    "CONTROL_NONE": "no change (control)",
}

PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <PackageId>Probe.Lib</PackageId>
    <Version>$(ProbeVersion)</Version>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
    <ImportDirectoryPackagesProps>false</ImportDirectoryPackagesProps>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
    <EnablePackageValidation>true</EnablePackageValidation>
  </PropertyGroup>
  <PropertyGroup Condition=" '$(ProbeBaseline)' != '' ">
    <PackageValidationBaselinePath>$(ProbeBaseline)</PackageValidationBaselinePath>
    <ApiCompatGenerateSuppressionFile>true</ApiCompatGenerateSuppressionFile>
    <ApiCompatSuppressionOutputFile>$(ProbeReport)</ApiCompatSuppressionOutputFile>
  </PropertyGroup>
</Project>
"""

SOURCE = """namespace Probe;

public interface IThing
{
    void Existing();
#if V_IFACE_ABSTRACT
    void Added();
#endif
#if V_IFACE_DEFAULT
    void Added() { }
#endif
}

public abstract class Base
{
    public abstract void A();
#if V_ABSTRACT_MEMBER
    public abstract void B();
#endif
}

public class Opts
{
    public required string Name { get; init; }
#if V_REQUIRED_NEW
    public required string Extra { get; init; }
#endif
#if V_NEW_OPTIONAL_MEMBER
    public string? Extra { get; init; }
#endif
}

public class WasRequired
{
#if V_REQUIRED_TO_OPTIONAL
    public string? Name { get; init; }
#else
    public required string Name { get; init; }
#endif
}

public class OptionalBecomesRequired
{
#if V_OPTIONAL_TO_REQUIRED
    public required string Name { get; init; }
#else
    public string? Name { get; init; }
#endif
}

#if V_ENUM_INSERT
public enum Color { Red, Green, Yellow, Blue }
#else
public enum Color
{
    Red = 0,
    Green = 1,
#if V_ENUM_RENUMBER
    Blue = 5,
#elif !V_ENUM_DELETE
    Blue = 2,
#endif
#if V_ENUM_ADD
    Yellow = 3,
#endif
}
#endif

public class Svc
{
#if V_METHOD_OPTIONAL
    public void M(string a, int b = 0) { }
#else
    public void M(string a) { }
#endif
}

public class Svc2
{
    public void M(string a) { }
#if V_METHOD_OVERLOAD
    public void M(string a, int b) { }
#endif
}

#if V_RECORD_POSITIONAL
public record Rec(string A, int B);
#elif V_RECORD_POSITIONAL_OPTIONAL
public record Rec(string A, int B = 0);
#else
public record Rec(string A);
#endif

#if V_NEW_TYPE
public class AddedType { }
#endif
"""


def _pack(project: pathlib.Path, output: pathlib.Path, *properties: str) -> subprocess.CompletedProcess[str]:
    environment = {**os.environ, "MSBUILDDISABLENODEREUSE": "1", "DOTNET_CLI_TELEMETRY_OPTOUT": "1"}
    for directory in ("bin", "obj"):
        _remove(project.parent / directory)
    return subprocess.run(
        ["dotnet", "pack", str(project), "-c", "Release", "-o", str(output), "-nologo", "-v", "q", *properties],
        cwd=project.parent, env=environment, capture_output=True, text=True, check=False)


def _remove(directory: pathlib.Path) -> None:
    if not directory.exists():
        return
    for path in sorted(directory.rglob("*"), reverse=True):
        path.unlink() if path.is_file() or path.is_symlink() else path.rmdir()
    directory.rmdir()


def diagnostics(report: pathlib.Path) -> frozenset[str]:
    """The diagnostic ids of one suppression file; an absent file is no record."""
    if not report.is_file():
        return frozenset()
    records = ET.parse(report).getroot().findall("Suppression")
    return frozenset((record.findtext("DiagnosticId") or "").strip() for record in records)


def measure(work: pathlib.Path) -> dict[str, frozenset[str]]:
    project = work / "Probe.Lib.csproj"
    project.write_text(PROJECT, encoding="utf-8")
    (work / "Probe.cs").write_text(SOURCE, encoding="utf-8")

    baseline = _pack(project, work / "baseline", "-p:ProbeVersion=1.0.0")
    if baseline.returncode:
        raise SystemExit(f"❌ Taban paketi üretilemedi:\n{baseline.stdout}\n{baseline.stderr}")
    baseline_package = work / "baseline" / "Probe.Lib.1.0.0.nupkg"

    measured: dict[str, frozenset[str]] = {}
    for variant in EXPECTED:
        report = work / "reports" / f"{variant}.xml"
        report.parent.mkdir(exist_ok=True)
        packed = _pack(
            project, work / "variants" / variant, "-p:ProbeVersion=1.0.1", f"-p:DefineConstants={variant}",
            f"-p:ProbeBaseline={baseline_package}", f"-p:ProbeReport={report}")
        if packed.returncode:
            raise SystemExit(f"❌ '{variant}' paketlenemedi:\n{packed.stdout}\n{packed.stderr}")
        measured[variant] = diagnostics(report)
    return measured


def render(measured: dict[str, frozenset[str]]) -> tuple[list[str], int]:
    """The result table and the number of variants that differ from `EXPECTED`."""
    lines = [f"{'Variant':<30} {'Measured':<10} {'Expected':<10} Change", "-" * 96]
    different = 0
    for variant, found in measured.items():
        expected = EXPECTED[variant]
        mark = "" if found == expected else "  ❌ FARKLI"
        different += found != expected
        lines.append(
            f"{variant:<30} {', '.join(sorted(found)) or '-':<10} {', '.join(sorted(expected)) or '-':<10} "
            f"{DESCRIPTIONS[variant]}{mark}")
    return lines, different


def main(argv: Sequence[str] | None = None) -> int:
    del argv
    with tempfile.TemporaryDirectory(prefix="tracon-apicompat-yoklama-") as directory:
        lines, different = render(measure(pathlib.Path(directory)))
    print("\n".join(lines))
    if different:
        print(f"\n❌ {different} varyant beklenenden farklı: ApiCompat davranışı değişti; "
              "scripts/breaking_changes.py ve reference/versioning.md varsayımları gözden geçirilmeli.")
        return 1
    print("\n✅ ApiCompat beklenen gibi davranıyor: `required` üye eklemesi ve opsiyonelden `required`'a geçiş görünmez.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
