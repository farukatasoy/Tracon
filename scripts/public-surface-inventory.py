#!/usr/bin/env python3
"""Public API surface inventory — the outside evidence for every public type.

`PublicAPI.Unshipped.txt` is a list of members; it does not say WHY a type is
public. This script puts every public TYPE (not member) in one of four classes:

  consumer   — a consumer sees it: a site page, a sample, template output, a package
               README, an XML `<example>`/`<code>` block, or a registration entry point
  seam       — an extension point: a public interface (today the set that
               `SeamContractDocumentationTests` scans), an abstract class, or the
               `Tracon.Testing.Contracts.Xunit` contract package
  justified  — no evidence, but public on purpose; the reason is written in
               `scripts/public-surface-reasons.tsv`
  unproven   — none of the above; its default fate is `internal`

If a type stays, every type in its public signature MUST stay too — otherwise the
compiler reports `CS0050`/`CS0051`/`CS0053`. So evidence does not stop at one type:
the signature closure (`signature:`) is computed from the root types, and a type that
enters the closure takes the class of its root. When a surviving type's XML
documentation names an exception with `<exception cref>`, that exception is part of
the contract too (`exception:`) — the consumer catches it and the implementer of a
seam throws it; the compiler does not enforce this.
The GA freeze pass runs the same script: the `unproven` column is that pass's work list.

Usage:
    python3 scripts/public-surface-inventory.py                   # package table
    python3 scripts/public-surface-inventory.py --list unproven   # candidates
    python3 scripts/public-surface-inventory.py --type Tracon.RunRecord
    python3 scripts/public-surface-inventory.py --json            # machine output
    python3 scripts/public-surface-inventory.py --check           # unproven > 0 → 1

An OpenAPI schema is NOT evidence: a .NET consumer never touches an HTTP DTO,
`Tracon.Client` generates its own DTOs. An HTTP request/response DTO is born
`internal`; the shipped OpenAPI description is carried by `[Description]`.

The measure is the type NAME, not the member. A name match can give a false positive
(a type's name equal to another type's property name) — a false positive KEEPS the
type, so it errs on the safe side. A false negative (making a type `internal` that a
consumer really uses) is caught by the packaged samples:
`python3 scripts/gate.py release --dry-run`.
"""
from __future__ import annotations

import argparse
import dataclasses
import json
import pathlib
import re
import subprocess
import sys
from collections import deque

ROOT = pathlib.Path(__file__).resolve().parent.parent
REASONS_PATH = pathlib.Path("scripts") / "public-surface-reasons.tsv"

# The contract package never counts as consumer evidence: its classes are the surface a
# consumer DERIVES from, so by definition they are in the "seam" class.
CONTRACT_PACKAGES = frozenset({"Tracon.Testing.Contracts.Xunit"})

# The registration entry-point rule lives in `capability_entry_points.py`; its C# copy is
# `CapabilityEntryPoints.cs`. A registration entry point is a member a consumer calls to
# switch a capability on; the coverage gate already forces each one to be named in the
# capability map and shown in an example.
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from capability_entry_points import EXTENSION_METHOD, is_registration_extension  # noqa: E402

# A line that is not a type declaration carries either a signature `(` or a type ` -> `
# (the same filter as `PublicSurfaceBaselineTests`).
MEMBER_MARKER = re.compile(r"\(| -> ")
# Modifiers at the start of member lines (`static`, `override` ...). `~` (the
# nullable-oblivious marker) sticks on without a space: `~Tracon.X.Y`, `~override Tracon.X`.
MODIFIERS = re.compile(r"^~?(?:[a-z]+\s+)*")
GENERIC_PARAMETERS = re.compile(r"<[^<>]*>")
TRACON_NAME = re.compile(r"Tracon(?:\.[A-Za-z_][A-Za-z0-9_]*)+")
IDENTIFIER = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
DECLARATION = re.compile(
    r"(?P<modifiers>(?:\b(?:public|internal|protected|private|static|sealed|abstract|"
    r"partial|readonly|ref|unsafe|new|file)\s+)*)"
    r"\b(?P<kind>class|interface|struct|enum|record\s+struct|record\s+class|record)\s+"
    r"(?P<name>[A-Za-z_][A-Za-z0-9_]*)\b")
DELEGATE = re.compile(
    r"(?P<modifiers>(?:\b(?:public|internal|protected|private|unsafe|new)\s+)*)"
    r"\bdelegate\s+[^;(]+?\s(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(")
XML_EXAMPLE = re.compile(r"<(example|code)>(?P<body>.*?)</\1>", re.DOTALL)
DOCUMENTED_EXCEPTION = re.compile(
    r'///.*?<exception\s+cref="(?:T:)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?P<name>[A-Za-z_][A-Za-z0-9_]*)"')
LINKED_SOURCE = re.compile(r'<Compile\s+Include="\.\./(?P<directory>[^/"]+)/\*\*/\*\.cs"')

CORPUS_EXTENSIONS = frozenset({".md", ".mdx", ".cs", ".razor", ".cshtml"})

CLASSES = ("consumer", "seam", "justified", "unproven")

Key = tuple[str, str]  # (package, full name) — the same full name can be public in several packages

# The field names of `--json`. They are a contract and are listed explicitly, so a
# dataclass field can be renamed without changing the output.
JSON_FIELDS = {
    "package": "package", "name": "name", "members": "members", "kind": "kind", "source": "source",
    "header_names": "header_names", "documented_exceptions": "documented_exceptions",
    "evidence_class": "class", "reason": "reason",
}


# --------------------------------------------------------------------------
# Data model
# --------------------------------------------------------------------------

@dataclasses.dataclass
class PublicType:
    package: str
    name: str                    # full name with the generic parameter list dropped
    members: list[str] = dataclasses.field(default_factory=list)
    kind: str = "?"              # interface · class · static · abstract · record · struct · enum · delegate
    source: str | None = None    # repository-relative path of the declaration
    header_names: set[str] = dataclasses.field(default_factory=set)  # full names of the bases and constraints
    documented_exceptions: set[str] = dataclasses.field(default_factory=set)  # full names from `<exception cref>`
    evidence_class: str = "unproven"
    reason: str = ""             # first evidence: `site:<path>`, `signature:<type>`, `exception:<type>`, `seam:interface` ...

    @property
    def key(self) -> Key:
        return (self.package, self.name)

    @property
    def simple_name(self) -> str:
        return self.name.rsplit(".", 1)[-1]


@dataclasses.dataclass
class Corpus:
    """Text a consumer can see: file → the identifiers in that file."""
    files: list[tuple[str, str, frozenset[str]]]  # (label, path, identifiers)
    texts: dict[str, str]                        # path → text (for the attribute search)

    @classmethod
    def build(cls, entries: list[tuple[str, str, str]]) -> "Corpus":
        return cls(
            [(label, path, frozenset(IDENTIFIER.findall(text))) for label, path, text in entries],
            {path: text for _, path, text in entries},
        )

    def find(self, name: str) -> str | None:
        for label, path, names in self.files:
            if name in names:
                return f"{label}:{path}"
        return None

    def find_attribute(self, short_name: str) -> str | None:
        """`[Short]`, `[Short(...)]`, `[A, Short]` — use without the `Attribute` suffix."""
        pattern = re.compile(rf"[\[,]\s*(?:[A-Za-z_][A-Za-z0-9_.]*\.)?{re.escape(short_name)}\s*[\](,]")
        for label, path, names in self.files:
            if short_name in names and pattern.search(self.texts[path]):
                return f"{label}:{path}"
        return None


# --------------------------------------------------------------------------
# Parsing
# --------------------------------------------------------------------------

def _before_signature(body: str) -> str:
    cut = len(body)
    for marker in ("(", " -> ", " = "):
        position = body.find(marker)
        if position >= 0:
            cut = min(cut, position)
    return body[:cut]


def _owner_path(line: str) -> str:
    """The owner path of a line: without modifiers, without generic parameters, the part before the signature."""
    path = _before_signature(MODIFIERS.sub("", line))
    while True:
        plain = GENERIC_PARAMETERS.sub("", path)
        if plain == path:
            return path
        path = plain


def _longest_prefix(path: str, names) -> str | None:
    parts = path.split(".")
    for length in range(len(parts), 0, -1):
        candidate = ".".join(parts[:length])
        if candidate in names:
            return candidate
    return None


def parse_unshipped(package: str, text: str) -> dict[str, PublicType]:
    """The types and their members from a package's Unshipped text (key: full name)."""
    types: dict[str, PublicType] = {}
    members: list[str] = []
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        if MEMBER_MARKER.search(line):
            members.append(line)
            continue
        name = _owner_path(line)
        types[name] = PublicType(package, name)
    for line in members:
        owner = _longest_prefix(_owner_path(line), types)
        if owner is not None:
            types[owner].members.append(line)
    return types


def name_index(types: dict[Key, PublicType]) -> dict[str, list[Key]]:
    index: dict[str, list[Key]] = {}
    for key in sorted(types):
        index.setdefault(key[1], []).append(key)
    return index


def exception_references(public_type: PublicType, index: dict[str, list[Key]]) -> set[Key]:
    """The public exceptions that the type's XML documentation names with `<exception cref>`."""
    return {key for name in public_type.documented_exceptions - {public_type.name} for key in index.get(name, [])}


def signature_references(public_type: PublicType, index: dict[str, list[Key]]) -> set[Key]:
    """The types in the signatures of the type's public members, in its base list, and its container."""
    names: set[str] = set()
    for line in public_type.members:
        body = MODIFIERS.sub("", line)
        # The owner path names the type itself; references start at the signature.
        for name in TRACON_NAME.findall(body[len(_before_signature(body)):]):
            target = _longest_prefix(name, index)
            if target is not None:
                names.add(target)
    names |= public_type.header_names
    # A nested public type is invisible from outside when its container is not public.
    container = public_type.name.rsplit(".", 1)[0]
    if container in index:
        names.add(container)
    names.discard(public_type.name)
    return {key for name in names for key in index[name]}


def scan_declarations(types: dict[Key, PublicType], sources: dict[str, list[tuple[str, str]]]) -> None:
    """Fills in the kind, the source file, and the (base, constraint) type names in the declaration header.

    `sources`: package → [(repository-relative path, text)]; linked source trees
    (`*.Shared`) are in the list of every package that compiles them. The header runs from
    where the name ends to the first `{` or `;` at depth zero.

    `<exception cref>` is collected at file level: every public type declared in the file
    takes all the documented exceptions of the file. Matching at member level would need
    parsing declaration boundaries; since a file mostly carries one type and its helper
    records, the approach errs on the safe side (it keeps).
    """
    simple_names: dict[str, set[str]] = {}
    for _, name in types:
        simple_names.setdefault(name.rsplit(".", 1)[-1], set()).add(name)

    for package, files in sources.items():
        package_types = {item.simple_name: item for item in types.values() if item.package == package}
        if not package_types:
            continue
        for path, text in files:
            exceptions = {target for match in DOCUMENTED_EXCEPTION.finditer(text)
                          for target in simple_names.get(match.group("name"), set())}
            for match in DECLARATION.finditer(text):
                public_type = package_types.get(match.group("name"))
                modifiers = match.group("modifiers")
                if public_type is None or "public" not in modifiers:
                    continue
                kind = re.sub(r"\s+", " ", match.group("kind"))
                if kind == "class" and "static" in modifiers:
                    kind = "static"
                elif kind == "class" and "abstract" in modifiers:
                    kind = "abstract"
                elif kind.startswith("record"):
                    kind = "record"
                if public_type.kind in ("?", "class", "record"):
                    public_type.kind = kind
                public_type.source = public_type.source or path
                public_type.documented_exceptions |= exceptions
                for identifier in IDENTIFIER.findall(_header(text, match.end())):
                    public_type.header_names |= simple_names.get(identifier, set())
            for match in DELEGATE.finditer(text):
                public_type = package_types.get(match.group("name"))
                if public_type is not None and "public" in match.group("modifiers"):
                    public_type.kind = "delegate"
                    public_type.source = public_type.source or path


def _header(text: str, start: int) -> str:
    depth = 0
    end = min(len(text), start + 4000)
    for position in range(start, end):
        character = text[position]
        if character in "(<[":
            depth += 1
        elif character in ")>]":
            depth -= 1
        elif character in "{;" and depth <= 0:
            return text[start:position]
    return text[start:end]


def xml_examples(text: str) -> str:
    """The `<example>` and `<code>` blocks in `///` comments — the shipped usage examples."""
    comment = "\n".join(
        line.strip()[3:] for line in text.splitlines() if line.strip().startswith("///"))
    return "\n".join(match.group("body") for match in XML_EXAMPLE.finditer(comment))


def read_reasons(text: str) -> dict[str, str]:
    """`<full name>\\t<reason>` lines. A line without a reason is an error."""
    reasons: dict[str, str] = {}
    for number, line in enumerate(text.splitlines(), start=1):
        if not line.strip() or line.startswith("#"):
            continue
        name, _, reason = line.partition("\t")
        if not reason.strip():
            raise ValueError(f"{REASONS_PATH}:{number}: '{name.strip()}' has no reason")
        reasons[name.strip()] = reason.strip()
    return reasons


# --------------------------------------------------------------------------
# Classification
# --------------------------------------------------------------------------

def classify(
    types: dict[Key, PublicType],
    corpus: Corpus,
    reasons: dict[str, str],
) -> None:
    """Finds the roots and spreads the signature closure. Priority: consumer > seam > justified."""
    roots: dict[str, list[tuple[Key, str]]] = {evidence_class: [] for evidence_class in CLASSES[:3]}
    for public_type in types.values():
        public_type.evidence_class, public_type.reason = "unproven", ""
        evidence = _consumer_evidence(public_type, corpus)
        if evidence:
            roots["consumer"].append((public_type.key, evidence))
            continue
        evidence = _seam_evidence(public_type)
        if evidence:
            roots["seam"].append((public_type.key, evidence))
            continue
        if public_type.name in reasons:
            roots["justified"].append((public_type.key, f"reason:{reasons[public_type.name]}"))

    index = name_index(types)
    assigned: set[Key] = set()
    for evidence_class in CLASSES[:3]:
        queue: deque[Key] = deque()
        for key, evidence in roots[evidence_class]:
            if key not in assigned:
                assigned.add(key)
                types[key].evidence_class, types[key].reason = evidence_class, evidence
                queue.append(key)
        while queue:
            origin = types[queue.popleft()]
            for step, targets in (("signature", signature_references(origin, index)),
                                  ("exception", exception_references(origin, index))):
                for target in sorted(targets):
                    if target not in assigned:
                        assigned.add(target)
                        types[target].evidence_class, types[target].reason = evidence_class, f"{step}:{origin.name}"
                        queue.append(target)


def _consumer_evidence(public_type: PublicType, corpus: Corpus) -> str | None:
    if public_type.package in CONTRACT_PACKAGES:
        return None
    evidence = corpus.find(public_type.simple_name)
    if evidence:
        return evidence
    if public_type.simple_name.endswith("Attribute") and len(public_type.simple_name) > len("Attribute"):
        evidence = corpus.find_attribute(public_type.simple_name[: -len("Attribute")])
        if evidence:
            return evidence
    for line in public_type.members:
        extension = EXTENSION_METHOD.match(line)
        if extension is None:
            continue
        if is_registration_extension(extension.group("name"), extension.group("receiver")):
            return f"entry-point:{extension.group('name')}"
        # A consumer calling an extension method writes the METHOD's name, not the class's.
        evidence = corpus.find(extension.group("name"))
        if evidence:
            return f"extension:{extension.group('name')}@{evidence}"
    # An OpenAPI schema is not evidence: the description is carried by `[Description]`,
    # and the type can stay `internal`.
    return None


def _seam_evidence(public_type: PublicType) -> str | None:
    if public_type.package in CONTRACT_PACKAGES:
        return "seam:contract-package"
    if public_type.kind == "interface":
        return "seam:interface"
    if public_type.kind == "abstract":
        return "seam:abstract-class"
    return None


def stale_reasons(types: dict[Key, PublicType], reasons: dict[str, str]) -> list[str]:
    """A reason written for a type that is no longer public or that has evidence of its own."""
    by_name: dict[str, list[PublicType]] = {}
    for public_type in types.values():
        by_name.setdefault(public_type.name, []).append(public_type)
    problems: list[str] = []
    for name in sorted(reasons):
        matching = by_name.get(name)
        if not matching:
            problems.append(f"{name}: not public (or renamed) — delete the line")
        elif all(item.evidence_class != "justified" for item in matching):
            public_type = matching[0]
            problems.append(
                f"{name}: now '{public_type.evidence_class}' ({public_type.reason}) — the reason is not needed, delete the line")
    return problems


# --------------------------------------------------------------------------
# Reading the repository
# --------------------------------------------------------------------------

def _git_files(root: pathlib.Path, *paths: str) -> list[str]:
    output = subprocess.run(
        ["git", "ls-files", "--", *paths], cwd=root, check=True, capture_output=True, text=True)
    return [line for line in output.stdout.splitlines() if line]


def _read(root: pathlib.Path, path: str) -> str:
    return (root / path).read_text(encoding="utf-8", errors="replace")


def collect_corpus(root: pathlib.Path) -> Corpus:
    entries: list[tuple[str, str, str]] = []

    def add(label: str, paths: list[str]) -> None:
        for path in paths:
            if pathlib.PurePosixPath(path).suffix in CORPUS_EXTENSIONS and (root / path).is_file():
                entries.append((label, path, _read(root, path)))

    # `api/` and `http-api/` are generated and untracked: they list every public type, so they are not evidence.
    add("site", _git_files(root, "docs-site/src/content/docs"))
    add("sample", _git_files(root, "samples"))
    add("template", _git_files(root, "src/Tracon.Templates/content"))
    src = _git_files(root, "src")
    add("readme", ["README.md"] + [
        path for path in src if re.fullmatch(r"src/[^/]+/README\.md", path) and ".Shared/" not in path])
    for path in src:
        if path.endswith(".cs") and ".Shared/" not in path:
            example = xml_examples(_read(root, path))
            if example:
                entries.append(("example", path, example))
    return Corpus.build(entries)


def collect_sources(root: pathlib.Path, package: str) -> list[tuple[str, str]]:
    """The package's own `.cs` files and the `*.Shared` trees its csproj links."""
    directories = [f"src/{package}"]
    csproj = root / "src" / package / f"{package}.csproj"
    if csproj.is_file():
        directories += [f"src/{match.group('directory')}"
                        for match in LINKED_SOURCE.finditer(csproj.read_text(encoding="utf-8"))]
    return [(path, _read(root, path)) for path in _git_files(root, *directories) if path.endswith(".cs")]


def build_inventory(root: pathlib.Path = ROOT) -> tuple[dict[Key, PublicType], dict[str, str]]:
    types: dict[Key, PublicType] = {}
    sources: dict[str, list[tuple[str, str]]] = {}
    for file in sorted((root / "src").glob("*/PublicAPI.Unshipped.txt")):
        package = file.parent.name
        for public_type in parse_unshipped(package, file.read_text(encoding="utf-8")).values():
            types[public_type.key] = public_type
        sources[package] = collect_sources(root, package)
    scan_declarations(types, sources)

    reasons = read_reasons(_read(root, REASONS_PATH.as_posix())) if (root / REASONS_PATH).is_file() else {}

    classify(types, collect_corpus(root), reasons)
    return types, reasons


# --------------------------------------------------------------------------
# Report
# --------------------------------------------------------------------------

def package_table(types: dict[Key, PublicType]) -> list[tuple[str, dict[str, int]]]:
    counts: dict[str, dict[str, int]] = {}
    for public_type in types.values():
        row = counts.setdefault(public_type.package, {evidence_class: 0 for evidence_class in CLASSES})
        row[public_type.evidence_class] += 1
    return sorted(counts.items())


def render_table(types: dict[Key, PublicType]) -> str:
    rows = [
        "| Package | Total | Consumer | Seam | Justified | Unproven |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    totals = {evidence_class: 0 for evidence_class in CLASSES}
    for package, counts in package_table(types):
        for evidence_class in CLASSES:
            totals[evidence_class] += counts[evidence_class]
        rows.append(
            f"| `{package}` | {sum(counts.values())} | "
            + " | ".join(str(counts[evidence_class]) for evidence_class in CLASSES) + " |")
    rows.append(
        f"| **Total** | **{sum(totals.values())}** | "
        + " | ".join(f"**{totals[evidence_class]}**" for evidence_class in CLASSES) + " |")
    return "\n".join(rows)


def evidence_chain(types: dict[Key, PublicType], name: str) -> list[str]:
    """Traces why a type stays, up to its root (the `signature:` and `exception:` steps)."""
    index = name_index(types)
    lines: list[str] = []
    for key in index.get(name, []):
        step, seen = types[key], set()
        while step.key not in seen:
            seen.add(step.key)
            lines.append(f"{step.name} [{step.package}, {step.kind}] → {step.evidence_class}: {step.reason}")
            kind, _, parent = step.reason.partition(":")
            if kind not in ("signature", "exception"):
                break
            step = types[index[parent][0]]
        lines.append("")
    return lines


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="Public API surface inventory: the outside evidence for every public type.")
    parser.add_argument("--list", choices=CLASSES + ("all",), help="list the types of one class")
    parser.add_argument("--package", help="only this package")
    parser.add_argument("--type", help="show the evidence chain of one type (full name)")
    parser.add_argument("--json", action="store_true", help="write the whole inventory as JSON")
    parser.add_argument("--check", action="store_true",
                        help="exit with code 1 when a type has no evidence or a reason is stale")
    args = parser.parse_args(argv)

    types, reasons = build_inventory()
    problems = stale_reasons(types, reasons)
    if args.package:
        types = {key: item for key, item in types.items() if item.package == args.package}
    ordered = sorted(types.values(), key=lambda item: item.key)

    if args.json:
        print(json.dumps(
            [{JSON_FIELDS[field]: (sorted(value) if isinstance(value, set) else value)
              for field, value in dataclasses.asdict(item).items()}
             for item in ordered],
            ensure_ascii=False, indent=1))
        return 0

    if args.type:
        chain = evidence_chain(types, args.type)
        if not chain:
            print(f"{args.type}: not in the inventory (the type is not public or the name is wrong)", file=sys.stderr)
            return 2
        print("\n".join(chain).rstrip())
        return 0

    if args.list:
        for item in ordered:
            if args.list == "all" or item.evidence_class == args.list:
                print("\t".join((item.package, item.name, item.kind, item.evidence_class, item.reason, item.source or "-")))
        return 0

    print(render_table(types))
    for problem in problems:
        print(f"stale reason: {problem}", file=sys.stderr)
    unproven = sum(1 for item in ordered if item.evidence_class == "unproven")
    if args.check and (unproven or problems):
        print(f"check failed: {unproven} type(s) without evidence, {len(problems)} stale reason(s)", file=sys.stderr)
        if unproven:
            print("hint: if the type is an HTTP request or response DTO, make it `internal` and write the "
                  "description with `[Description]`; otherwise show the type in a site page, a sample or an "
                  "`<example>`, or write a reason in scripts/public-surface-reasons.tsv.",
                  file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
