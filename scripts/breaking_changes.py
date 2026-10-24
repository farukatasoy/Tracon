#!/usr/bin/env python3
"""Breaking-change gate of the release rehearsal (`kapi.py yayin`, Faz 187).

Packages are validated against the last published `v*` release (ApiCompat
package validation, driven by `Directory.Build.targets`). The SDK writes what
it found as a suppression file per project; this module turns those files
into a list of broken public types and dropped target frameworks, and
checks that the release notes name every one of them.

K-602 still allows a breaking change in the preview line. The gate does not
reject one there; it rejects shipping one that the notes do not announce.
From `1.0.0-rc.1` on (Faz 237) the line is frozen: a breaking change is red
whatever the notes say, and a patch release may not grow the public surface.
`release_line` decides which rule a release is judged by.

Callable two ways:
- from `kapi.py yayin` (the functions below; the rehearsal owns the pack);
- as a CLI over an existing report directory:
  `python3 scripts/breaking_changes.py --rapor-dizini <dir> --taban <version> [--surum <version>]`
"""
from __future__ import annotations

import argparse
import dataclasses
import json
import math
import os
import pathlib
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections.abc import Callable, Iterable, Sequence

ROOT = pathlib.Path(__file__).resolve().parent.parent
NUGET_ORG = "https://api.nuget.org/v3/index.json"
TAG_PREFIX = "v"
FIRST_RELEASE_FLAG = "<TraconPackageFirstRelease>true</TraconPackageFirstRelease>"
SEMAPHORE_NAME = "Microsoft.NET.ApiCompat.ValidatePackage.semaphore"
RESULT_FILE = pathlib.PurePath("artifacts", "package", "breaking-changes.json")
REPORT_ROOT = pathlib.PurePath("artifacts", "package", "api-compat")


class BaselineError(Exception):
    """The baseline release could not be resolved; the gate must not run blind."""


class RestoreError(Exception):
    """The isolated baseline restore failed."""


class SemaphoreError(Exception):
    """The package-validation semaphore of a project could not be located."""


# -----------------------------------------------------------------------------
# 187.2 - baseline selection
# -----------------------------------------------------------------------------


def _git(root: pathlib.Path, *args: str) -> subprocess.CompletedProcess[str]:
    """Runs git in `root`. Raises BaselineError when git itself is missing."""
    try:
        return subprocess.run(["git", *args], cwd=root, capture_output=True, text=True, check=False)
    except OSError as exception:
        raise BaselineError(f"git çalıştırılamadı ({exception})") from exception


def resolve_baseline(root: pathlib.Path = ROOT) -> str:
    """The last published version, without the `v` prefix.

    On a tagged HEAD (a tag run) the baseline is the PREVIOUS `v*` tag:
    comparing a release with itself would produce a silent "no breaking
    change". Otherwise it is the last `v*` tag reachable from HEAD.
    """
    shallow = _git(root, "rev-parse", "--is-shallow-repository")
    if shallow.returncode:
        reason = (shallow.stderr or shallow.stdout).strip() or f"çıkış {shallow.returncode}"
        raise BaselineError(f"bu bir git deposu değil ({reason})")
    if shallow.stdout.strip() == "true":
        raise BaselineError("sığ klon: son 'v*' etiketi güvenilir bulunamaz; tam geçmişle klonlayın (fetch-depth: 0)")

    exact = _git(root, "describe", "--tags", "--exact-match", "--match", f"{TAG_PREFIX}*", "HEAD")
    start = "HEAD^" if exact.returncode == 0 else "HEAD"
    found = _git(root, "describe", "--tags", "--abbrev=0", "--match", f"{TAG_PREFIX}*", start)
    if found.returncode:
        where = "HEAD'in üst commit'inden" if start == "HEAD^" else "HEAD'den"
        reason = (found.stderr or found.stdout).strip() or f"çıkış {found.returncode}"
        raise BaselineError(f"{where} erişilen bir '{TAG_PREFIX}*' etiketi yok ({reason})")

    tag = found.stdout.strip()
    version = tag[len(TAG_PREFIX):]
    if not tag.startswith(TAG_PREFIX) or _parse_version(version) is None:
        raise BaselineError(f"etiket bir sürüm adlandırmıyor: '{tag}'")
    return version


_VERSION = re.compile(r"^(?P<core>\d+\.\d+\.\d+)(?:-(?P<pre>[0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$")


def _parse_version(version: str) -> tuple[tuple[int, ...], list[str]] | None:
    match = _VERSION.fullmatch(version)
    if match is None:
        return None
    core = tuple(int(part) for part in match.group("core").split("."))
    pre = match.group("pre").split(".") if match.group("pre") else []
    return core, pre


def compare_versions(left: str, right: str) -> int:
    """SemVer 2.0 precedence, as NuGet orders versions: -1, 0 or 1.

    `1.0.0-preview.2.46` sorts ABOVE `1.0.0-preview.2` (a longer pre-release
    with an equal prefix wins), and every pre-release sorts below its release.
    """
    parsed_left, parsed_right = _parse_version(left), _parse_version(right)
    if parsed_left is None or parsed_right is None:
        raise ValueError(f"not a version: '{left if parsed_left is None else right}'")
    (core_left, pre_left), (core_right, pre_right) = parsed_left, parsed_right
    if core_left != core_right:
        return -1 if core_left < core_right else 1
    if not pre_left or not pre_right:
        return (not pre_left) - (not pre_right)
    for a, b in zip(pre_left, pre_right):
        if a == b:
            continue
        if a.isdigit() and b.isdigit():
            return -1 if int(a) < int(b) else 1
        if a.isdigit() != b.isdigit():
            return -1 if a.isdigit() else 1
        return -1 if a.lower() < b.lower() else 1
    return (len(pre_left) > len(pre_right)) - (len(pre_left) < len(pre_right))


def version_is_above_baseline(version: str, baseline: str) -> bool:
    return compare_versions(version, baseline) > 0


# -----------------------------------------------------------------------------
# 237.2 - release line
# -----------------------------------------------------------------------------

FROZEN_FROM = "1.0.0-rc.1"
UNRELEASED = "Unreleased"

LINE_PREVIEW = "preview"
LINE_MAJOR = "major"
LINE_FROZEN = "frozen"


@dataclasses.dataclass(frozen=True)
class ReleaseLine:
    """Which rule set a release is judged by.

    `preview` - the baseline is below `1.0.0-rc.1`: K-602 allows a breaking
    change if the notes announce it. `major` - the baseline is frozen but the
    version raises the major: the same rule, because a major is where 1.x
    allows a break. `frozen` - baseline `1.0.0-rc.1` or above and the same
    major: a breaking change is red, and the notes cannot save it. `patch`
    marks the frozen releases that also share the baseline's minor; the
    surface is closed there, so an ADDED public member is red too."""

    kind: str
    major: int
    patch: bool = False

    @property
    def frozen(self) -> bool:
        return self.kind == LINE_FROZEN

    def describe(self) -> str:
        if self.kind == LINE_PREVIEW:
            return "Hat: preview (K-602 — kırılma notta adıyla)"
        if self.kind == LINE_MAJOR:
            return "Hat: major artışı (kırılma notta adıyla)"
        if self.patch:
            return f"Hat: donmuş {self.major}.x yama (kırılma ve eklenen public üye reddedilir)"
        return f"Hat: donmuş {self.major}.x (kırılma reddedilir)"


def release_line(baseline: str, version: str | None) -> ReleaseLine:
    """The one function that decides the line (Faz 237, §237.2). Faz 238 reuses
    it for the HTTP and name-set gates; a second copy would drift.

    `version` may be None only while the baseline is a preview: nothing below
    `rc.1` depends on it, and the CLI has no version to give when it reads
    `[Unreleased]`."""
    if compare_versions(baseline, FROZEN_FROM) < 0:
        return ReleaseLine(LINE_PREVIEW, major=0)
    if version is None:
        raise ValueError(f"sürüm gerekli: taban v{baseline} donmuş hatta, hat sürümsüz belirlenemez")
    base_core = _parse_version(baseline)[0]
    new_parsed = _parse_version(version)
    if new_parsed is None:
        raise ValueError(f"not a version: '{version}'")
    new_core = new_parsed[0]
    if new_core[0] != base_core[0]:
        return ReleaseLine(LINE_MAJOR, major=new_core[0])
    return ReleaseLine(LINE_FROZEN, major=base_core[0], patch=new_core[:2] == base_core[:2])


# -----------------------------------------------------------------------------
# 187.3 / 187.7 - baseline package set and the first-release flag
# -----------------------------------------------------------------------------


def _csproj(root: pathlib.Path, project_id: str) -> pathlib.Path:
    return root / "src" / project_id / f"{project_id}.csproj"


def has_first_release_flag(root: pathlib.Path, project_id: str) -> bool:
    return FIRST_RELEASE_FLAG in _csproj(root, project_id).read_text(encoding="utf-8")


def baseline_package_ids(root: pathlib.Path, library_ids: Iterable[str]) -> list[str]:
    """Library packages that have a published baseline to restore.

    `library_ids` comes from the rehearsal (`kapi.py` owns the package profile
    rule); a package flagged `TraconPackageFirstRelease` is skipped."""
    return sorted(project_id for project_id in library_ids if not has_first_release_flag(root, project_id))


def stale_first_release_flags(root: pathlib.Path, baseline_tag: str) -> list[str]:
    """Flagged packages that already existed, packable, in the baseline tag.

    Such a flag would silently switch the baseline check off for a package
    that has one. Answered from git alone; no network."""
    stale = []
    for csproj in sorted((root / "src").glob("*/*.csproj")):
        project_id = csproj.stem
        if FIRST_RELEASE_FLAG not in csproj.read_text(encoding="utf-8"):
            continue
        shown = _git(root, "show", f"{baseline_tag}:src/{project_id}/{project_id}.csproj")
        if shown.returncode == 0 and "<IsPackable>false</IsPackable>" not in shown.stdout:
            stale.append(project_id)
    return stale


# -----------------------------------------------------------------------------
# 187.3 - isolated baseline restore
# -----------------------------------------------------------------------------

_NUGET_CONFIG = f"""<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="{NUGET_ORG}" protocolVersion="3" />
  </packageSources>
</configuration>
"""


def _restore_project(ids: Sequence[str], version: str) -> str:
    downloads = "\n".join(f'    <PackageDownload Include="{project_id}" Version="[{version}]" />' for project_id in ids)
    # The repository's Directory.Build.* and central package management must
    # not reach this project: it lives outside the repository, and the flags
    # below keep it that way if a temp root ever lands inside one.
    return f"""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImportDirectoryBuildProps>false</ImportDirectoryBuildProps>
    <ImportDirectoryBuildTargets>false</ImportDirectoryBuildTargets>
    <ImportDirectoryPackagesProps>false</ImportDirectoryPackagesProps>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
{downloads}
  </ItemGroup>
</Project>
"""


def restore_baselines(
    ids: Sequence[str],
    version: str,
    work_dir: pathlib.Path,
    *,
    run: Callable[..., subprocess.CompletedProcess[str]] = subprocess.run,
) -> pathlib.Path:
    """Restores each baseline package into a cache of its own under `work_dir`.

    🚨 The developer cache is never read: a `1.0.0-preview.1` there was packed
    locally by `ReleaseArtifactFixture`, and a baseline read from it would
    compare the tree with itself. Returns the cache root."""
    cache = work_dir / "packages"
    cache.mkdir(parents=True, exist_ok=True)
    config = work_dir / "NuGet.config"
    config.write_text(_NUGET_CONFIG, encoding="utf-8")
    project = work_dir / "baseline-restore" / "baseline-restore.csproj"
    project.parent.mkdir(parents=True, exist_ok=True)
    project.write_text(_restore_project(ids, version), encoding="utf-8")

    environment = os.environ.copy()
    environment["NUGET_PACKAGES"] = str(cache)
    environment["MSBUILDDISABLENODEREUSE"] = "1"
    command = ["dotnet", "restore", str(project), "--configfile", str(config), "--packages", str(cache)]
    try:
        result = run(command, cwd=work_dir, env=environment, capture_output=True, text=True, check=False)
    except OSError as exception:
        raise RestoreError(f"'dotnet restore' çalıştırılamadı ({exception})") from exception
    if result.returncode:
        output = f"{result.stdout or ''}\n{result.stderr or ''}"
        named = [project_id for project_id in ids if project_id.lower() in output.lower()]
        tail = "\n".join(line for line in output.splitlines() if line.strip())[-2000:]
        subject = ", ".join(named) if named else ", ".join(ids)
        raise RestoreError(
            f"taban paketleri nuget.org'dan alınamadı (v{version}; {subject}). "
            f"Etiket var ama yayın tamamlanmamış olabilir: önce yayını tamamlayın.\n{tail}")
    return cache


def baseline_source_violations(ids: Iterable[str], version: str, cache: pathlib.Path) -> list[str]:
    """Each restored baseline must have come from nuget.org - the proof that
    the isolated restore did what it promised."""
    violations = []
    for project_id in ids:
        directory = cache / project_id.lower() / version.lower()
        metadata = directory / ".nupkg.metadata"
        nupkg = directory / f"{project_id.lower()}.{version.lower()}.nupkg"
        if not metadata.exists() or not nupkg.exists():
            violations.append(f"{project_id}: taban paketi izole cache'te yok ({directory})")
            continue
        try:
            source = json.loads(metadata.read_text(encoding="utf-8")).get("source")
        except (OSError, ValueError) as exception:
            violations.append(f"{project_id}: .nupkg.metadata okunamadı ({exception})")
            continue
        if source != NUGET_ORG:
            violations.append(f"{project_id}: taban kaynağı '{source}', beklenen '{NUGET_ORG}'")
    return violations


def pack_properties(cache: pathlib.Path, version: str, report_dir: pathlib.Path) -> list[str]:
    """The three `-p:` a baseline-validating pack takes (Faz 191 carries them)."""
    return [
        f"-p:TraconPackageBaselineRoot={cache}",
        f"-p:TraconPackageBaselineVersion={version}",
        f"-p:TraconApiCompatReportDir={report_dir}",
    ]


# -----------------------------------------------------------------------------
# 187.5 - proof that validation ran in THIS pack
# -----------------------------------------------------------------------------


def semaphore_path(root: pathlib.Path, project_id: str) -> pathlib.Path:
    """`artifacts/obj/<P>/<release>/<semaphore>`, the configuration directory
    matched case-insensitively: macOS reports `Release`, Linux `release`."""
    project_obj = root / "artifacts" / "obj" / project_id
    searched = project_obj / "[Rr]elease" / SEMAPHORE_NAME
    matches = sorted(
        path for path in project_obj.glob(f"*/{SEMAPHORE_NAME}")
        if path.parent.name.lower() == "release"
    ) if project_obj.is_dir() else []
    if len(matches) != 1:
        found = ", ".join(str(path) for path in matches) or "hiçbiri"
        raise SemaphoreError(
            f"{project_id}: paket doğrulama semaphore'u tek olarak bulunamadı "
            f"(aranan: {searched}; bulunan: {found}). SDK iç adı değiştiyse kapı genişletilmeli.")
    return matches[0]


def validation_not_run(root: pathlib.Path, ids: Iterable[str], since: float) -> list[str]:
    """Packages whose validation did not run after `since` (the pack start).

    `RunPackageValidation` is incremental; its semaphore is touched only when
    it actually runs. The comparison floors `since` to the second because a
    file system may keep one-second modification times."""
    threshold = math.floor(since)
    problems = []
    for project_id in sorted(ids):
        try:
            semaphore = semaphore_path(root, project_id)
        except SemaphoreError as exception:
            problems.append(str(exception))
            continue
        if semaphore.stat().st_mtime < threshold:
            problems.append(f"{project_id}: paket doğrulaması bu koşumda koşmadı ({semaphore} eski)")
    return problems


# -----------------------------------------------------------------------------
# 187.6 - report -> breaking list
# -----------------------------------------------------------------------------


@dataclasses.dataclass
class BreakingChanges:
    types: dict[str, set[str]] = dataclasses.field(default_factory=dict)
    dropped_frameworks: dict[str, set[str]] = dataclasses.field(default_factory=dict)
    errors: list[str] = dataclasses.field(default_factory=list)
    # Types that gained a `required` member. Also in `types`: the report never
    # lists them, the snapshot comparison does (§237.3), and from there on
    # they are broken types like any other.
    required: dict[str, set[str]] = dataclasses.field(default_factory=dict)

    @property
    def is_empty(self) -> bool:
        return not self.types and not self.dropped_frameworks

    def type_count(self) -> int:
        return sum(len(names) for names in self.types.values())

    def framework_count(self) -> int:
        return sum(len(frameworks) for frameworks in self.dropped_frameworks.values())

    def package_count(self) -> int:
        return len(set(self.types) | set(self.dropped_frameworks))


_DOC_ID = re.compile(r"^(?P<kind>[TMPFE]):(?P<name>.+)$")
_ARITY = re.compile(r"`+\d+")


def type_of_doc_id(doc_id: str) -> str | None:
    """The simple name of the type a documentation ID names or declares.

    `T:Ns.Outer.Inner` -> `Inner`; `M:Ns.Type`1.#ctor(System.String)` ->
    `Type`. Namespace and generic arity are dropped; a member ID loses its
    parameter list and member name. Returns None for anything else."""
    match = _DOC_ID.fullmatch(doc_id.strip())
    if match is None:
        return None
    name = match.group("name").split("(", 1)[0]
    segments = [segment for segment in name.split(".") if segment]
    if match.group("kind") != "T":
        segments = segments[:-1]
    if not segments:
        return None
    return _ARITY.sub("", segments[-1]) or None


def _text(element: ET.Element, tag: str) -> str:
    child = element.find(tag)
    return (child.text or "").strip() if child is not None else ""


def read_breaking_changes(report_dir: pathlib.Path) -> BreakingChanges:
    """Reads every `<package>.xml` suppression file of one validation run.

    A record is handled by the first rule that matches:
    `PKV006` -> dropped framework (the SDK writes it without
    `IsBaselineSuppression`) · not a baseline record -> a strict-mode
    incompatibility, which the pack would otherwise have failed on ·
    `CP*` with a documentation ID -> broken type · anything else -> unknown."""
    changes = BreakingChanges()
    for report in sorted(report_dir.glob("*.xml")):
        package = report.stem
        try:
            records = ET.parse(report).getroot().findall("Suppression")
        except (ET.ParseError, OSError) as exception:
            changes.errors.append(f"{package}: rapor okunamadı ({report.name}: {exception})")
            continue
        for record in records:
            diagnostic = _text(record, "DiagnosticId")
            target = _text(record, "Target")
            baseline = _text(record, "IsBaselineSuppression").lower() == "true"
            if diagnostic == "PKV006" and target:
                changes.dropped_frameworks.setdefault(package, set()).add(target)
                continue
            if not baseline:
                changes.errors.append(
                    f"{package}: TFM'ler arası (strict) uyumsuzluk {diagnostic} {target} - yüzey her TFM'de aynı olmalı")
                continue
            type_name = type_of_doc_id(target) if diagnostic.startswith("CP") else None
            if type_name is None:
                changes.errors.append(f"{package}: tanınmayan kayıt {diagnostic} {target} - kapı genişletilmeli")
                continue
            changes.types.setdefault(package, set()).add(type_name)
    return changes


# -----------------------------------------------------------------------------
# 187.6 - release notes and the naming rule
# -----------------------------------------------------------------------------


def release_notes_for(changelog: pathlib.Path, version: str) -> tuple[str | None, str | None]:
    """The notes a release of `version` ships and their heading (K-825).

    Read with `changelog.read_release_notes`, never from `[Unreleased]`
    alone: at tag time that heading is renamed to the version."""
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
    import changelog as changelog_module

    return changelog_module.read_release_notes(changelog, version)


def untagged_cut_hint(changelog: pathlib.Path, baseline: str) -> str | None:
    """A version section above the baseline with no tag yet (Açık Soru 1 = C).

    The cut commit and its tag are pushed together; until the tag exists the
    rehearsal cannot know the section is the one it ships."""
    sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
    import changelog as changelog_module

    for line in changelog.read_text(encoding="utf-8").splitlines():
        match = changelog_module.SECTION_HEADER_PATTERN.match(line)
        if not match or match.group("version") == changelog_module.UNRELEASED:
            continue
        version = match.group("version")
        if _parse_version(version) is not None and version_is_above_baseline(version, baseline):
            return version
        return None
    return None


_CODE_SPAN = re.compile(r"`([^`]+)`")
# A fenced code block is code, not a span. Its three backticks would also pair
# with the prose around it: every fence shifts which text counts as a span, so
# two fences in one release note made the gate miss names written correctly
# (measured, phase 189). Fences are removed before spans are read.
_FENCE = re.compile(r"^[ \t]*(`{3,}|~{3,})[^\n]*\n.*?^[ \t]*\1[ \t]*$", re.MULTILINE | re.DOTALL)
_ANGLE = re.compile(r"<[^<>]*>")
_PAREN = re.compile(r"\([^()]*\)")
_LIST_ITEM = re.compile(r"^\s*(?:[-*+]|\d+[.)])\s+")


def _code_spans(text: str) -> list[str]:
    return [" ".join(span.split()) for span in _CODE_SPAN.findall(_FENCE.sub("", text))]


def _type_parts(span: str) -> set[str]:
    """The dot-separated parts of a code span, generic arguments and
    parameter lists removed: `Ns.Type<T>.Member(int)` -> {Ns, Type, Member}."""
    previous = None
    while previous != span:
        previous = span
        span = _PAREN.sub("", _ANGLE.sub("", span))
    return {part.strip() for part in span.split(".") if part.strip()}


def _list_items(text: str) -> list[str]:
    """Markdown list items, each with its indented or lazy continuation lines."""
    items: list[list[str]] = []
    current: list[str] | None = None
    blank = False
    for line in text.splitlines():
        if _LIST_ITEM.match(line):
            current = [line]
            items.append(current)
            blank = False
        elif current is None:
            continue
        elif not line.strip():
            blank = True
        elif line.startswith("#"):
            current = None
        elif blank and not line[:1].isspace():
            current = None
        else:
            current.append(line)
            blank = False
    return ["\n".join(item) for item in items]


def unnamed_changes(changes: BreakingChanges, notes: str | None) -> list[str]:
    """One line per package with what the notes do not name.

    A type is named when a code span, split at `.`, has a part EQUAL to it -
    never a substring: `*ModelCatalog` does not name `OpenAIModelCatalog`.
    A dropped framework is named when one list item has a span equal to the
    package id and a span equal to the framework."""
    notes = notes or ""
    named_parts: set[str] = set()
    for span in _code_spans(notes):
        named_parts |= _type_parts(span)
    item_spans = [set(_code_spans(item)) for item in _list_items(notes)]

    lines = []
    for package in sorted(set(changes.types) | set(changes.dropped_frameworks)):
        missing_types = sorted(name for name in changes.types.get(package, ()) if name not in named_parts)
        missing_frameworks = sorted(
            framework for framework in changes.dropped_frameworks.get(package, ())
            if not any(package in spans and framework in spans for spans in item_spans)
        )
        parts = []
        if missing_types:
            parts.append(", ".join(missing_types))
        if missing_frameworks:
            parts.append("düşen TFM " + ", ".join(missing_frameworks))
        if parts:
            lines.append(f"{package}: {' · '.join(parts)}")
    return lines


# -----------------------------------------------------------------------------
# 237.3 / 237.4 - what the ApiCompat report cannot see
# -----------------------------------------------------------------------------
#
# Measured 2026-10-24 (Faz 237, §237.3), SDK 10.0.402, baseline validation on:
# an added `required` member is NOT reported (not even when an optional member
# becomes `required`); an interface member without a body is CP0006, an
# abstract member CP0005, an enum renumber CP0011, a removed enum value CP0002,
# an optional parameter or a positional record parameter CP0002. So the gate
# adds two readers of its own: the `required` snapshot and the PublicAPI files,
# both compared with the files of the baseline tag.

API_FILE = re.compile(r"^src/(?P<package>[^/]+)/PublicAPI\.(?:Shipped|Unshipped)\.txt$")
REMOVED_PREFIX = "*REMOVED*"
CONTRACT_PACKAGE = "Tracon.Testing.Contracts.Xunit"
REQUIRED_SNAPSHOT = pathlib.PurePosixPath("docs", "sozlesme", "required-uyeler.txt")
MAX_LISTED = 8


class SurfaceError(Exception):
    """The public surface or a contract snapshot could not be read."""


def _strip_generics(name: str) -> str:
    previous = None
    while previous != name:
        previous = name
        name = _ANGLE.sub("", name)
    return name


def effective_api(shipped: str, unshipped: str) -> set[str]:
    """The public surface the two PublicApiAnalyzers files describe together.

    `Shipped` is the promise, `Unshipped` what is new since; an
    `*REMOVED*` line in `Unshipped` takes a `Shipped` line out. At the `1.0.0`
    fill (K-603) lines move from one file to the other, so only the union
    says whether the surface changed."""
    surface: set[str] = set()
    removed: set[str] = set()
    for text, is_unshipped in ((shipped, False), (unshipped, True)):
        for raw in text.splitlines():
            line = raw.strip().lstrip("\ufeff")
            if not line or line.startswith("#"):
                continue
            if is_unshipped and line.startswith(REMOVED_PREFIX):
                removed.add(line[len(REMOVED_PREFIX):].strip())
            else:
                surface.add(line)
    return surface - removed


def _show(root: pathlib.Path, tag: str, path: str) -> str | None:
    """A file as it was at `tag`, None when the tag has no such path."""
    shown = _git(root, "show", f"{tag}:{path}")
    return shown.stdout if shown.returncode == 0 else None


def api_at_tag(root: pathlib.Path, tag: str) -> dict[str, set[str]]:
    """Package -> its effective public surface at `tag`."""
    listing = _git(root, "ls-tree", "-r", "--name-only", tag, "--", "src")
    if listing.returncode:
        reason = (listing.stderr or listing.stdout).strip() or f"çıkış {listing.returncode}"
        raise BaselineError(f"'{tag}' ağacı listelenemedi ({reason})")
    files: dict[str, dict[str, str]] = {}
    for path in listing.stdout.splitlines():
        match = API_FILE.fullmatch(path)
        if match is not None:
            files.setdefault(match.group("package"), {})[path.rpartition("/")[2]] = _show(root, tag, path) or ""
    return {
        package: effective_api(texts.get("PublicAPI.Shipped.txt", ""), texts.get("PublicAPI.Unshipped.txt", ""))
        for package, texts in files.items()
    }


def api_now(root: pathlib.Path) -> dict[str, set[str]]:
    """Package -> its effective public surface in the working tree."""
    packages: dict[str, set[str]] = {}
    src = root / "src"
    names = sorted({path.parent.name for pattern in ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt")
                    for path in src.glob(f"*/{pattern}")}) if src.is_dir() else []
    for package in names:
        texts = []
        for file_name in ("PublicAPI.Shipped.txt", "PublicAPI.Unshipped.txt"):
            path = src / package / file_name
            texts.append(path.read_text(encoding="utf-8") if path.is_file() else "")
        packages[package] = effective_api(*texts)
    return packages


def type_declarations(surface: Iterable[str]) -> set[str]:
    """Type names in a PublicAPI surface, generic arguments removed. A line
    declares a type when it names neither a member signature nor a member's
    type (the filter `PublicSurfaceBaselineTests` uses)."""
    return {_strip_generics(line) for line in surface if "(" not in line and " -> " not in line}


_API_MEMBER = re.compile(
    r"^(?P<modifiers>(?:(?:abstract|virtual|override|static|sealed|new|readonly)\s+|~)*)"
    r"(?P<name>[^()]+?)\((?P<parameters>.*)\) -> (?P<returns>.+)$")
_TEST_RETURNS = frozenset({"void", "System.Threading.Tasks.Task!", "System.Threading.Tasks.Task"})
_LIFECYCLE = frozenset({"InitializeAsync", "DisposeAsync", "Dispose"})


def contract_test_name(line: str) -> str | None:
    """`Class.Method` when a PublicAPI line declares a contract test, else None.

    A test is an instance method returning `Task` or `void` that is neither a
    constructor nor a lifecycle hook. Hooks the consumer implements return
    other types (`ValueTask`, `bool`) or are abstract, so the shape is enough;
    the measured count is written in the phase document."""
    match = _API_MEMBER.match(line)
    if match is None:
        return None
    if set(match.group("modifiers").split()) & {"abstract", "static"}:
        return None
    if match.group("returns") not in _TEST_RETURNS:
        return None
    owner, _, method = _strip_generics(match.group("name")).rpartition(".")
    type_name = owner.rpartition(".")[2]
    if not type_name or method == type_name or method in _LIFECYCLE:
        return None
    return f"{type_name}.{method}"


def contract_tests(surface: Iterable[str]) -> set[str]:
    """`Class.Method` of every test method a contract surface declares."""
    return {name for name in map(contract_test_name, surface) if name is not None}


def _snapshot_lines(text: str) -> set[str]:
    return {line.strip() for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#")}


def required_member_additions(
    root: pathlib.Path, tag: str, tag_api: dict[str, set[str]], *, snapshot_required: bool = False,
) -> tuple[dict[str, set[str]], list[str], list[str]]:
    """Package -> types that existed at `tag` and gained a `required` member,
    the snapshot lines the tree dropped, and the notes.

    A line the snapshot gained for a type the baseline did not declare is a
    new type, which breaks nobody. A dropped line is a `required` member that
    became optional or went away; it matters to a frozen patch only.

    When the baseline tag has no snapshot yet (the first record) the
    comparison is skipped and a note says so - unless `snapshot_required`: a
    frozen baseline was cut after the file existed, so a missing file there is
    a hole in the gate, not a first record."""
    previous = _show(root, tag, str(REQUIRED_SNAPSHOT))
    if previous is None:
        if snapshot_required:
            raise SurfaceError(
                f"{REQUIRED_SNAPSHOT} '{tag}' etiketinde yok; donmuş bir tabanda anlık görüntü zorunludur "
                "(required üye eklemesi başka türlü görünmez)")
        return {}, [], [f"{REQUIRED_SNAPSHOT} '{tag}' etiketinde yok (ilk kayıt): required üye karşılaştırması atlandı"]
    try:
        current = (root / REQUIRED_SNAPSHOT).read_text(encoding="utf-8")
    except OSError as exception:
        raise SurfaceError(f"{REQUIRED_SNAPSHOT} okunamadı ({exception})") from exception

    declared_by: dict[str, str] = {}
    for package, surface in sorted(tag_api.items()):
        for declaration in type_declarations(surface):
            declared_by.setdefault(declaration, package)

    types: dict[str, set[str]] = {}
    for line in sorted(_snapshot_lines(current) - _snapshot_lines(previous)):
        type_name = line.rpartition(".")[0]
        package = declared_by.get(type_name)
        if package is not None:
            types.setdefault(package, set()).add(type_name.rpartition(".")[2])
    return types, sorted(_snapshot_lines(previous) - _snapshot_lines(current)), []


@dataclasses.dataclass
class SurfaceDelta:
    """What changed since the baseline tag in the places ApiCompat does not look."""

    tag: str
    added_members: dict[str, list[str]] = dataclasses.field(default_factory=dict)
    contract_tests: list[str] = dataclasses.field(default_factory=list)
    required_types: dict[str, set[str]] = dataclasses.field(default_factory=dict)
    required_dropped: list[str] = dataclasses.field(default_factory=list)
    notes: list[str] = dataclasses.field(default_factory=list)


def surface_delta(root: pathlib.Path, baseline: str) -> SurfaceDelta:
    """Compares the working tree with the files of tag `v<baseline>`.

    Raises BaselineError when the tag is missing: a gate that cannot read its
    baseline must be red, never green. A package the tag does not know has no
    baseline; all of its lines count as added members, but it adds no contract
    test to an existing suite. A line that declares a NEW contract test is not
    counted as an added member: the rule table lets a patch add a test that
    measures documented behavior, and the notes must name it (`check`)."""
    tag = f"{TAG_PREFIX}{baseline}"
    if _git(root, "rev-parse", "--verify", "--quiet", f"{tag}^{{commit}}").returncode:
        raise BaselineError(f"taban etiketi '{tag}' bu depoda yok; yüzey karşılaştırması yapılamaz")

    before, after = api_at_tag(root, tag), api_now(root)
    delta = SurfaceDelta(tag=tag)
    if CONTRACT_PACKAGE in before:
        delta.contract_tests = sorted(
            contract_tests(after.get(CONTRACT_PACKAGE, set())) - contract_tests(before[CONTRACT_PACKAGE]))
    new_tests = set(delta.contract_tests)
    for package, surface in sorted(after.items()):
        added = sorted(
            line for line in surface - before.get(package, set())
            if not (package == CONTRACT_PACKAGE and contract_test_name(line) in new_tests))
        if added:
            delta.added_members[package] = added
    delta.required_types, delta.required_dropped, delta.notes = required_member_additions(
        root, tag, before, snapshot_required=compare_versions(baseline, FROZEN_FROM) >= 0)
    return delta


def unnamed_contract_tests(tests: Iterable[str], notes: str | None) -> list[str]:
    """Contract tests the notes do not name. `Class.Method` must stand in ONE
    code span: method names repeat across contract classes, so a bare method
    name or a class name elsewhere does not name the test."""
    spans = [_type_parts(span) for span in _code_spans(notes or "")]
    return sorted(
        test for test in tests
        if not any(set(test.split(".")) <= parts for parts in spans)
    )


def write_result(
    path: pathlib.Path,
    *,
    baseline: str,
    notes_heading: str | None,
    changes: BreakingChanges,
    line: ReleaseLine | None = None,
    delta: SurfaceDelta | None = None,
) -> pathlib.Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    result = {
        "baseline": baseline,
        "notesHeading": notes_heading,
        "types": {package: sorted(names) for package, names in sorted(changes.types.items())},
        "droppedFrameworks": {
            package: sorted(frameworks) for package, frameworks in sorted(changes.dropped_frameworks.items())
        },
    }
    if line is not None:
        result["line"] = line.kind
    if delta is not None:
        result["contractTests"] = list(delta.contract_tests)
    path.write_text(json.dumps(result, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    return path


# -----------------------------------------------------------------------------
# The gate as one call: report dir + notes -> exit code
# -----------------------------------------------------------------------------


def _listed(items: Sequence[str]) -> str:
    shown = ", ".join(items[:MAX_LISTED])
    return shown + (f" … (+{len(items) - MAX_LISTED})" if len(items) > MAX_LISTED else "")


def _describe_breaking(changes: BreakingChanges) -> list[str]:
    lines = []
    for package in sorted(set(changes.types) | set(changes.dropped_frameworks)):
        parts = [
            f"{name} (yeni required üye)" if name in changes.required.get(package, ()) else name
            for name in sorted(changes.types.get(package, ()))
        ]
        if package in changes.dropped_frameworks:
            parts.append("düşen TFM " + ", ".join(sorted(changes.dropped_frameworks[package])))
        lines.append(f"{package}: {' · '.join(parts)}")
    return lines


def check(
    *,
    report_dir: pathlib.Path,
    baseline: str,
    version: str,
    changelog: pathlib.Path,
    result_path: pathlib.Path | None,
    repo: pathlib.Path | None = None,
) -> int:
    """Reads the reports, matches them against the notes, prints the outcome.

    The release line (§237.2) decides the rule. In a preview or a major
    release a broken type or dropped framework passes when the notes name it
    (K-602: the gate rejects the unannounced break, not the break). In a
    frozen release it is red whatever the notes say, and a frozen patch also
    rejects an added public member. A contract test added since the baseline
    must be named in the notes on every line.

    `repo` switches on the readers the report cannot replace: the PublicAPI
    files, the `required` snapshot and the contract tests, all against the
    baseline tag. Returns 0 when green, 1 otherwise. Writes `result_path`
    only when green."""
    try:
        line = release_line(baseline, None if version == UNRELEASED else version)
    except ValueError as exception:
        print(f"❌ Hat belirlenemedi: {exception}")
        return 1
    print(line.describe())

    changes = read_breaking_changes(report_dir)
    if changes.errors:
        print(f"❌ Paket doğrulama raporu kapının tanımadığı kayıt taşıyor (taban v{baseline}):")
        for error in changes.errors:
            print(f"  {error}")
        return 1

    delta: SurfaceDelta | None = None
    if repo is None:
        print("ℹ️ Yüzey karşılaştırması yok (depo verilmedi): eklenen üye, required üye ve contract testi denetlenmedi")
    else:
        try:
            delta = surface_delta(repo, baseline)
        except (BaselineError, SurfaceError) as exception:
            print(f"❌ {exception}")
            return 1
        for note in delta.notes:
            print(f"ℹ️ {note}")
        for package, names in delta.required_types.items():
            changes.types.setdefault(package, set()).update(names)
            changes.required.setdefault(package, set()).update(names)

    failed = False
    contract = delta.contract_tests if delta is not None else []

    if line.frozen and not changes.is_empty:
        print(f"❌ Donmuş hatta kırıcı değişiklik reddedilir (taban v{baseline}, sürüm {version}); sürüm notu bunu kurtarmaz:")
        for entry in _describe_breaking(changes):
            print(f"  {entry}")
        failed = True
    if line.frozen and line.patch and delta is not None and delta.added_members:
        print(f"❌ Yama sürümünde public yüzey büyüyemez (taban v{baseline}, sürüm {version}); eklenen satırlar:")
        for package, added in sorted(delta.added_members.items()):
            print(f"  {package}: {len(added)} satır — {_listed(added)}")
        failed = True
    if line.frozen and line.patch and delta is not None and delta.required_dropped:
        print(
            f"❌ Yama sürümünde required üye opsiyonel olamaz ya da kalkamaz (taban v{baseline}, sürüm {version}); "
            f"anlık görüntüden düşen satırlar: {_listed(delta.required_dropped)}")
        failed = True

    notes, heading = (None, None)
    breaking_needs_notes = not line.frozen and not changes.is_empty
    if breaking_needs_notes or contract:
        notes, heading = release_notes_for(changelog, version)
        if notes is None:
            subject = "Kırıcı değişiklik var" if breaking_needs_notes else "Contract testi eklendi"
            print(f"❌ {subject} ama '{version}' için sürüm notu yok (taban v{baseline}).")
            cut = untagged_cut_hint(changelog, baseline)
            if cut:
                print(
                    f"   '## [{cut}]' bölümü var ama etiketi yok: kesim commit'i ile etiketi birlikte itin "
                    f"(git push --atomic origin main v{cut}).")
            failed = True

        if breaking_needs_notes:
            missing = unnamed_changes(changes, notes)
            if missing:
                print(
                    f"❌ Kırıcı değişiklik sürüm notunda adıyla geçmiyor "
                    f"(taban v{baseline}, not '{heading or 'yok'}'):")
                for missing_line in missing:
                    print(f"  {missing_line}")
                failed = True
        if contract:
            missing_tests = unnamed_contract_tests(contract, notes)
            if missing_tests:
                print(
                    f"❌ Contract testi eklemesi sürüm notunda adıyla geçmiyor "
                    f"(taban v{baseline}, not '{heading or 'yok'}'):")
                for test in missing_tests:
                    print(f"  {test}")
                print("   Her test aynı code span içinde `Sınıf.Metot` olarak anılır.")
                failed = True

    if failed:
        return 1

    breaking_heading = heading if not changes.is_empty else None
    print(
        f"✅ Kırıcı liste: {changes.type_count()} tip, {changes.framework_count()} TFM düşüşü, "
        f"{changes.package_count()} paket — "
        + (f"hepsi '{breaking_heading}' notunda" if breaking_heading else "not gerekmiyor"))
    if delta is not None:
        print(
            f"✅ Contract testi eklemesi (taban {delta.tag}'den beri): {len(contract)}"
            + (f" — hepsi '{heading}' notunda" if contract else ""))
        for test in contract:
            print(f"  {test}")
    if result_path is not None:
        written = write_result(
            result_path, baseline=baseline, notes_heading=breaking_heading, changes=changes, line=line, delta=delta)
        print(f"📄 {written}")
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Kırıcı değişiklik raporunu sürüm notuna karşı denetler (Faz 187, 237).")
    parser.add_argument("--rapor-dizini", required=True, type=pathlib.Path, help="<paket>.xml bastırma raporlarının dizini")
    parser.add_argument("--taban", required=True, help="karşılaştırılan yayınlanmış sürüm, 'v'siz")
    parser.add_argument("--surum", help="notu okunacak sürüm; verilmezse '## [Unreleased]' (donmuş hatta sürüm gerekir)")
    parser.add_argument("--changelog", type=pathlib.Path, default=ROOT / "CHANGELOG.md")
    parser.add_argument(
        "--depo", type=pathlib.Path,
        help="verilirse PublicAPI dosyaları, required anlık görüntüsü ve contract testleri bu deponun taban etiketiyle karşılaştırılır")
    args = parser.parse_args(argv)

    if not args.rapor_dizini.is_dir():
        print(f"❌ Rapor dizini yok: {args.rapor_dizini}")
        return 1
    return check(
        report_dir=args.rapor_dizini,
        baseline=args.taban,
        version=args.surum or UNRELEASED,
        changelog=args.changelog,
        result_path=None,
        repo=args.depo,
    )


if __name__ == "__main__":
    raise SystemExit(main())
