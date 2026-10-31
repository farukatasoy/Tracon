#!/usr/bin/env python3
"""Release-to-release gate for the surfaces that are not .NET API.

ApiCompat reads .NET assemblies. A consumer also depends on HTTP endpoints,
SSE event names, telemetry names, configuration keys, error codes and CLI
commands; none of them is in an assembly. `OpenApiSnapshotTests` and its
siblings keep a committed file true to the code, but a refreshed file passes
whatever it says: nothing compared it with the LAST RELEASE. This module does.

Two readers, both against the files of the baseline tag (`git show v<tag>:<file>`):

- name sets (`docs/sozlesme/*.txt`): a line that is gone or changed is a break,
  a new line is an addition;
- the OpenAPI document (`docs/openapi/tracon.json`): the rules of the HTTP row
  in `reference/versioning.md`. The rule table sits in one place below.
  A change the rules do not name is a break too (`tanınmayan değişiklik`): a
  gate that cannot classify a change must not pass it.

The release line is the one `breaking_changes.release_line` decides;
there is no second copy. In a preview or a major release a break passes when the
release notes name it; in a frozen release it is red whatever the notes say, and
a frozen patch may not add to any surface either.

Callable two ways:
- from `kapi.py yayin` (`check`, after the ApiCompat gate);
- as a CLI over any git repository:
  `python3 scripts/sozlesme_kirilma.py --depo <dir> --taban <version> [--surum <version>]`
"""
from __future__ import annotations

import argparse
import dataclasses
import importlib.util
import json
import pathlib
import re
import sys
from collections.abc import Callable, Iterable, Sequence

ROOT = pathlib.Path(__file__).resolve().parent.parent


def _load_breaking_changes():
    """The release-line decision, the git readers and the notes parser live there."""
    loaded = sys.modules.get("breaking_changes")
    if loaded is not None:
        return loaded
    spec = importlib.util.spec_from_file_location("breaking_changes", ROOT / "scripts" / "breaking_changes.py")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


bc = _load_breaking_changes()

HTTP_FILE = pathlib.PurePosixPath("docs", "openapi", "tracon.json")
HTTP_LABEL = "HTTP"


class SurfaceError(Exception):
    """A snapshot could not be read; the gate must not run blind."""


@dataclasses.dataclass(frozen=True)
class Change:
    """One difference between the baseline and the working tree.

    `ident` is the text a release note must hold in ONE code span to name the
    change; `alt` is a shorter name that names it too, as a whole word and
    whatever its case (a property is `expectedTools` on the wire and
    `ExpectedTools` in the notes). `subject` and `rule` are what the report
    prints."""

    surface: str
    subject: str
    rule: str
    ident: str
    alt: str = ""


@dataclasses.dataclass
class SurfaceResult:
    surface: str
    breaks: list[Change] = dataclasses.field(default_factory=list)
    additions: list[Change] = dataclasses.field(default_factory=list)
    note: str | None = None


# -----------------------------------------------------------------------------
# Name sets
# -----------------------------------------------------------------------------


@dataclasses.dataclass(frozen=True)
class NameSurface:
    label: str
    path: pathlib.PurePosixPath
    ident: Callable[[str], str]


def _last_token(line: str) -> str:
    return line.rsplit(" ", 1)[-1]


def _second_token(line: str) -> str:
    parts = line.split(" ")
    return parts[1] if len(parts) > 1 else line


def _whole_line(line: str) -> str:
    return line


def _cli_ident(line: str) -> str:
    """`option eval --timeout` -> `--timeout`; `command migrate status` -> `migrate status`."""
    kind, _, rest = line.partition(" ")
    if kind == "option":
        return _last_token(rest)
    return rest


NAME_SURFACES = (
    # `run-events run.started` -> `run.started`
    NameSurface("SSE", pathlib.PurePosixPath("docs", "sozlesme", "sse-olaylari.txt"), _last_token),
    # `instrument tracon.runs Counter<long> …` -> `tracon.runs`; `attribute tracon.tenant.id` -> `tracon.tenant.id`
    NameSurface("telemetri", pathlib.PurePosixPath("docs", "sozlesme", "telemetri.txt"), _second_token),
    NameSurface("yapılandırma", pathlib.PurePosixPath("docs", "sozlesme", "yapilandirma.txt"), _whole_line),
    NameSurface("hata kodu", pathlib.PurePosixPath("docs", "sozlesme", "hata-kodlari.txt"), _whole_line),
    NameSurface("doğrulama kodu", pathlib.PurePosixPath("docs", "sozlesme", "dogrulama-kodlari.txt"), _whole_line),
    NameSurface("CLI", pathlib.PurePosixPath("docs", "sozlesme", "cli.txt"), _cli_ident),
)


def snapshot_lines(text: str) -> set[str]:
    """The names of a snapshot file: `#` lines and blank lines are not names."""
    return {line.strip() for line in text.splitlines() if line.strip() and not line.lstrip().startswith("#")}


def name_set_delta(root: pathlib.Path, tag: str, surface: NameSurface, *, snapshot_required: bool) -> SurfaceResult:
    """Compares one snapshot file with the one at `tag`.

    A baseline that has no such file (the first record) has nothing to compare:
    a note says so - unless `snapshot_required`: a frozen baseline was cut after
    the file existed, so a missing file there is a hole in the gate, not a
    first record."""
    result = SurfaceResult(surface.label)
    previous = bc._show(root, tag, str(surface.path))
    if previous is None:
        if snapshot_required:
            raise SurfaceError(
                f"{surface.path} '{tag}' etiketinde yok; donmuş bir tabanda anlık görüntü zorunludur "
                f"({surface.label} değişikliği başka türlü görünmez)")
        result.note = f"taban yok ({surface.path} '{tag}' etiketinde yok; ilk kayıt)"
        return result
    try:
        current = (root / surface.path).read_text(encoding="utf-8")
    except OSError as exception:
        raise SurfaceError(f"{surface.path} okunamadı ({exception})") from exception

    before, after = snapshot_lines(previous), snapshot_lines(current)
    for line in sorted(before - after):
        result.breaks.append(Change(
            surface.label, line, "satır kalktı ya da değişti", surface.ident(line)))
    for line in sorted(after - before):
        result.additions.append(Change(surface.label, line, "yeni satır", surface.ident(line)))
    return result


# -----------------------------------------------------------------------------
# HTTP: the OpenAPI document
# -----------------------------------------------------------------------------
#
# The rule table (HTTP row). Everything here is one of three things:
#
#   BREAK     only a major may do it
#   ADDITION  a minor may do it, a patch may not
#   (silent)  documentation: description, summary, tags, examples, deprecated
#
# A key, a keyword or a shape that is in none of the three is `tanınmayan
# değişiklik` and counts as a BREAK. The table lists what a 1.x minor may do;
# the gate is allowed to be stricter than the table, never looser.

DOCUMENTATION_KEYS = frozenset({
    "description", "summary", "title", "tags", "examples", "example", "externalDocs", "deprecated",
})
METHODS = ("get", "put", "post", "delete", "options", "head", "patch", "trace")
SUCCESS = re.compile(r"^2\d\d$")
PATH_PARAMETER = re.compile(r"\{[^}]*\}")

REQUEST = "istek"
RESPONSE = "yanıt"


class _Diff:
    """Collects the changes of one comparison."""

    def __init__(self, old: dict, new: dict) -> None:
        self.old = old
        self.new = new
        self.breaks: list[Change] = []
        self.additions: list[Change] = []
        self._seen: set[tuple[str, str, str]] = set()

    def broke(self, subject: str, rule: str, ident: str, alt: str = "") -> None:
        self.breaks.append(Change(HTTP_LABEL, subject, rule, ident, alt))

    def added(self, subject: str, rule: str, ident: str, alt: str = "") -> None:
        self.additions.append(Change(HTTP_LABEL, subject, rule, ident, alt))

    # -- documents ----------------------------------------------------------

    def compare(self) -> None:
        old_ops = _operations(self.old)
        new_ops = _operations(self.new)

        for key in sorted(old_ops):
            if key not in new_ops:
                method, path = old_ops[key][0], old_ops[key][1]
                self.broke(f"{method.upper()} {path}", "uç ya da metot silindi", path)
                continue
            self._operation(old_ops[key], new_ops[key])

        for key in sorted(new_ops.keys() - old_ops.keys()):
            method, path = new_ops[key][0], new_ops[key][1]
            self.added(f"{method.upper()} {path}", "yeni uç", path)

        # Left out on purpose: `openapi` (the generator's format version, not the wire), `info` (title, version),
        # `servers` (the base URL of the host that generated the file), `tags` (documentation) and `paths` and
        # `components`, which are compared above and below. Everything else, `security` included, is compared.
        for key in sorted((self.old.keys() | self.new.keys()) - {"openapi", "info", "servers", "paths", "components", "tags"}):
            if _strip_documentation(self.old.get(key)) != _strip_documentation(self.new.get(key)):
                rule = "belge düzeyi güvenlik gereksinimi değişti" if key == "security" else "tanınmayan değişiklik"
                self.broke(f"belge anahtarı '{key}'", rule, key)

        for key in sorted(((self.old.get("components") or {}).keys() | (self.new.get("components") or {}).keys())
                          - {"schemas", "securitySchemes"}):
            if (self.old.get("components") or {}).get(key) != (self.new.get("components") or {}).get(key):
                self.broke(f"components.{key}", "tanınmayan değişiklik", key)

        self._security_schemes()

    def _security_schemes(self) -> None:
        old = (self.old.get("components") or {}).get("securitySchemes") or {}
        new = (self.new.get("components") or {}).get("securitySchemes") or {}
        for name in sorted(old):
            if name not in new:
                self.broke(f"güvenlik şeması '{name}'", "güvenlik şeması silindi", name)
            elif _strip_documentation(old[name]) != _strip_documentation(new[name]):
                self.broke(f"güvenlik şeması '{name}'", "güvenlik şeması değişti", name)

    # -- operations ---------------------------------------------------------

    def _operation(self, old_entry: tuple, new_entry: tuple) -> None:
        method, path, old_op = old_entry
        _, new_path, new_op = new_entry
        where = f"{method.upper()} {path}"

        if old_op.get("operationId") != new_op.get("operationId"):
            # NSwag names the generated client method from it.
            self.broke(where, "operationId değişti (üretilen istemci yöntemi adı)", path)

        self._parameters(where, (path, new_path), old_op.get("parameters") or [], new_op.get("parameters") or [])
        self._request_body(where, path, old_op.get("requestBody"), new_op.get("requestBody"))
        self._responses(where, path, old_op.get("responses") or {}, new_op.get("responses") or {})

        handled = {"operationId", "parameters", "requestBody", "responses"} | DOCUMENTATION_KEYS
        for key in sorted((old_op.keys() | new_op.keys()) - handled):
            if old_op.get(key) != new_op.get(key):
                self.broke(where, f"tanınmayan değişiklik: {key}", path)

    def _parameters(self, where: str, paths: tuple[str, str], old: list, new: list) -> None:
        """`paths` is the template in the baseline and the template now: a path variable is matched by position."""
        old_by_key = {_parameter_key(self.old, parameter, paths[0]): self._resolve(self.old, parameter) for parameter in old}
        new_by_key = {_parameter_key(self.new, parameter, paths[1]): self._resolve(self.new, parameter) for parameter in new}

        for key in sorted(old_by_key):
            label = f"{key[1]} ({key[0]})"
            if key not in new_by_key:
                self.broke(where, f"istek parametresi silindi: {label}", key[1])
                continue
            before, after = old_by_key[key], new_by_key[key]
            if not before.get("required", False) and after.get("required", False):
                self.broke(where, f"opsiyonel parametre zorunlu oldu: {label}", key[1])
            self._schema(
                REQUEST, f"{where} parametre {key[1]}", key[1],
                before.get("schema") or {}, after.get("schema") or {})
            handled = {"name", "in", "required", "schema"} | DOCUMENTATION_KEYS
            for other in sorted((before.keys() | after.keys()) - handled):
                if before.get(other) != after.get(other):
                    self.broke(where, f"tanınmayan değişiklik: parametre {key[1]} {other}", key[1])

        for key in sorted(new_by_key.keys() - old_by_key.keys()):
            label = f"{key[1]} ({key[0]})"
            if new_by_key[key].get("required", False) or key[0] == "path":
                self.broke(where, f"istekte yeni zorunlu parametre: {label}", key[1])
            else:
                self.added(where, f"istekte yeni opsiyonel parametre: {label}", key[1])

    def _request_body(self, where: str, path: str, old: dict | None, new: dict | None) -> None:
        old = self._resolve(self.old, old) if isinstance(old, dict) and "$ref" in old else old
        new = self._resolve(self.new, new) if isinstance(new, dict) and "$ref" in new else new
        if old is None and new is None:
            return
        if old is None:
            if (new or {}).get("required", False):
                self.broke(where, "istekte yeni zorunlu gövde", path)
            else:
                self.added(where, "istekte yeni opsiyonel gövde", path)
            return
        if new is None:
            self.broke(where, "istek gövdesi silindi", path)
            return
        if not old.get("required", False) and new.get("required", False):
            self.broke(where, "opsiyonel istek gövdesi zorunlu oldu", path)
        self._content(REQUEST, where, path, old.get("content") or {}, new.get("content") or {})
        for key in sorted((old.keys() | new.keys()) - {"content", "required"} - DOCUMENTATION_KEYS):
            if old.get(key) != new.get(key):
                self.broke(where, f"tanınmayan değişiklik: requestBody {key}", path)

    def _responses(self, where: str, path: str, old: dict, new: dict) -> None:
        old_success = {code for code in old if SUCCESS.match(code)}
        new_success = {code for code in new if SUCCESS.match(code)}
        for code in sorted(old_success - new_success):
            self.broke(where, f"başarı durum kodu kalktı: {code}", path)
        for code in sorted(new_success - old_success):
            if old_success & new_success:
                self.added(where, f"yeni başarı durum kodu: {code}", path)

        for code in sorted(old.keys() & new.keys()):
            before, after = self._resolve(self.old, old[code]), self._resolve(self.new, new[code])
            self._content(RESPONSE, f"{where} {code}", path, before.get("content") or {}, after.get("content") or {})
            for key in sorted((before.keys() | after.keys()) - {"content"} - DOCUMENTATION_KEYS):
                if before.get(key) != after.get(key):
                    self.broke(where, f"tanınmayan değişiklik: yanıt {code} {key}", path)
        # A documented 4xx/5xx that appears or goes is documentation: the status a
        # request really gets is proved by the endpoint tests, not by this file.

    def _content(self, direction: str, where: str, ident: str, old: dict, new: dict) -> None:
        for media in sorted(old):
            if media not in new:
                self.broke(where, f"{direction} ortam türü kalktı: {media}", ident)
                continue
            self._schema(direction, where, ident, old[media].get("schema") or {}, new[media].get("schema") or {})
            for key in sorted((old[media].keys() | new[media].keys()) - {"schema"} - DOCUMENTATION_KEYS):
                if old[media].get(key) != new[media].get(key):
                    self.broke(where, f"tanınmayan değişiklik: {media} {key}", ident)
        for media in sorted(new.keys() - old.keys()):
            self.added(where, f"{direction} için yeni ortam türü: {media}", ident)

    # -- schemas ------------------------------------------------------------

    def _resolve(self, document: dict, node: object) -> dict:
        """Follows `$ref` to the object it names; a node that is not an object is empty."""
        hops = 0
        while isinstance(node, dict) and "$ref" in node and hops < 20:
            node = _lookup(document, node["$ref"])
            hops += 1
        return node if isinstance(node, dict) else {}

    def _normalize(self, document: dict, node: object) -> tuple[bool, dict, str]:
        """(nullable, the schema without its null alternative, the name of the schema it came from).

        OpenAPI 3.1 spells nullable as `type: [..., "null"]` or `oneOf: [{type: "null"}, X]`, and X is
        often a `$ref`; the name of the last `$ref` followed is the name of the type."""
        nullable = False
        name = ""
        current: object = node
        for _ in range(10):
            if isinstance(current, dict) and "$ref" in current:
                name = str(current["$ref"]).rsplit("/", 1)[-1]
                current = self._resolve(document, current)
                continue
            if not isinstance(current, dict):
                return nullable, {}, name
            was_nullable, unwrapped = _split_nullable(current)
            nullable = nullable or was_nullable
            if unwrapped is current:
                return nullable, current, name
            current = unwrapped
        return nullable, current if isinstance(current, dict) else {}, name

    def _schema(self, direction: str, where: str, ident: str, old: dict, new: dict, label: str = "") -> None:
        """Compares two schemas; `direction` says who fills the value (REQUEST: the client)."""
        before_null, before, before_name = self._normalize(self.old, old)
        after_null, after, after_name = self._normalize(self.new, new)
        scope = after_name or before_name or label or ident

        if before_name and after_name and before_name != after_name:
            # NSwag names a client type from it.
            self.broke(f"{where} {label or scope}", f"şema başka bir şemaya bağlandı: {before_name} -> {after_name}", before_name)
            return
        # Nullability belongs to the PLACE a schema is used, not to the schema: `Owner` may be `oneOf[null, $ref]` in
        # one property and a plain `$ref` in another. So it is compared before the already-seen shortcut below, which
        # skips only the work that is the same at every place (the shape).
        use = label or scope
        if direction == REQUEST and before_null and not after_null:
            self.broke(f"{where} {use}", "istek alanı artık null olamaz", use, use.rsplit(".", 1)[-1])
        if direction == RESPONSE and not before_null and after_null:
            self.broke(f"{where} {use}", "yanıt alanı artık null olabilir", use, use.rsplit(".", 1)[-1])

        if before_name:
            key = (before_name, direction, "")
            if key in self._seen:
                return
            self._seen.add(key)

        before_type, after_type = _types(before), _types(after)
        if before_type != after_type:
            self.broke(f"{where} {scope}", f"tür değişti: {sorted(before_type)} -> {sorted(after_type)}", scope)
            return

        if before.get("format") != after.get("format"):
            self.broke(f"{where} {scope}", f"biçim değişti: {before.get('format')} -> {after.get('format')}", scope)
        if before.get("pattern") != after.get("pattern"):
            self.broke(f"{where} {scope}", "desen (pattern) değişti", scope)

        self._enum(direction, where, scope, before.get("enum"), after.get("enum"))
        self._properties(direction, where, scope, before, after)

        if "items" in before or "items" in after:
            if "items" in before and "items" in after:
                self._schema(direction, where, ident, before["items"], after["items"], f"{scope}[]")
            else:
                self.broke(f"{where} {scope}", "dizi öğe şeması eklendi ya da kalktı", scope)

        self._additional(direction, where, ident, scope, before.get("additionalProperties"), after.get("additionalProperties"))
        self._combinators(direction, where, ident, scope, before, after)

        handled = {
            "type", "format", "pattern", "enum", "properties", "required", "items", "additionalProperties",
            "oneOf", "anyOf", "allOf", "nullable", "$ref",
        } | DOCUMENTATION_KEYS
        for other in sorted((before.keys() | after.keys()) - handled):
            if before.get(other) != after.get(other):
                self.broke(f"{where} {scope}", f"tanınmayan değişiklik: {other}", scope)

    def _enum(self, direction: str, where: str, scope: str, old: list | None, new: list | None) -> None:
        if old is None and new is None:
            return
        if old is None:
            if direction == REQUEST:
                self.broke(f"{where} {scope}", "istek alanı artık sabit bir değer kümesiyle sınırlı", scope)
            return
        if new is None:
            if direction == RESPONSE:
                self.broke(f"{where} {scope}", "yanıt alanı artık sabit bir değer kümesi değil", scope)
            return
        removed = [value for value in old if value not in new]
        added = [value for value in new if value not in old]
        if removed:
            # A request enum loses a value a client may send; a response enum loses a member of the generated .NET
            # client (`Tracon.Client` is outside PublicAPI tracking, so this gate is its only guard).
            self.broke(f"{where} {scope}", f"{direction} enum değeri silindi: {removed}", scope)
        if added:
            self.added(f"{where} {scope}", f"{direction} enum değeri eklendi: {added}", scope)

    def _properties(self, direction: str, where: str, scope: str, old: dict, new: dict) -> None:
        old_props = old.get("properties") or {}
        new_props = new.get("properties") or {}
        old_required = set(old.get("required") or [])
        new_required = set(new.get("required") or [])

        for name in sorted(old_props):
            member = f"{scope}.{name}"
            if name not in new_props:
                rule = "yanıt alanı silindi" if direction == RESPONSE else "istek alanı silindi"
                self.broke(f"{where} {member}", rule, member, name)
                continue
            if direction == REQUEST and name in new_required and name not in old_required:
                self.broke(f"{where} {member}", "opsiyonel istek alanı zorunlu oldu", member, name)
            if direction == RESPONSE and name in old_required and name not in new_required:
                self.broke(f"{where} {member}", "zorunlu yanıt alanı opsiyonel oldu", member, name)
            self._schema(direction, where, member, old_props[name], new_props[name], member)

        for name in sorted(new_props.keys() - old_props.keys()):
            member = f"{scope}.{name}"
            if direction == REQUEST and name in new_required:
                self.broke(f"{where} {member}", "istekte yeni zorunlu alan", member, name)
            elif direction == REQUEST:
                self.added(f"{where} {member}", "istekte yeni opsiyonel alan", member, name)
            else:
                self.added(f"{where} {member}", "yanıtta yeni alan", member, name)

    def _additional(self, direction: str, where: str, ident: str, scope: str, old: object, new: object) -> None:
        if old == new:
            return
        if isinstance(old, dict) and isinstance(new, dict):
            self._schema(direction, where, ident, old, new, f"{scope}{{}}")
            return
        self.broke(f"{where} {scope}", "additionalProperties değişti", scope)

    def _combinators(self, direction: str, where: str, ident: str, scope: str, old: dict, new: dict) -> None:
        for keyword in ("oneOf", "anyOf", "allOf"):
            before = _members(old.get(keyword))
            after = _members(new.get(keyword))
            if before is None and after is None:
                continue
            before, after = before or {}, after or {}
            for member in sorted(before):
                if member not in after:
                    # A removed alternative narrows what a response holds (safe) and what a request takes (break).
                    if direction == REQUEST or keyword == "allOf":
                        self.broke(f"{where} {scope}", f"{keyword} üyesi kalktı: {member}", scope)
                else:
                    self._schema(direction, where, ident, before[member], after[member], f"{scope}|{member}")
            for member in sorted(after.keys() - before.keys()):
                if keyword == "allOf":
                    self.broke(f"{where} {scope}", f"allOf üyesi eklendi: {member}", scope)
                else:
                    self.added(f"{where} {scope}", f"{keyword} üyesi eklendi: {member}", scope)


def _strip_documentation(node: object) -> object:
    if isinstance(node, dict):
        return {key: _strip_documentation(value) for key, value in node.items() if key not in DOCUMENTATION_KEYS}
    if isinstance(node, list):
        return [_strip_documentation(value) for value in node]
    return node


def _lookup(document: dict, reference: str) -> dict:
    node: object = document
    if not reference.startswith("#/"):
        return {}
    for part in reference[2:].split("/"):
        part = part.replace("~1", "/").replace("~0", "~")
        node = node.get(part) if isinstance(node, dict) else None
        if node is None:
            return {}
    return node if isinstance(node, dict) else {}


def _operations(document: dict) -> dict[tuple[str, str], tuple[str, str, dict]]:
    """(method, path with its variables blanked) -> (method, path, operation).

    A path variable renamed (`{id}` -> `{runId}`) is the same endpoint on the wire."""
    operations: dict[tuple[str, str], tuple[str, str, dict]] = {}
    for path, item in (document.get("paths") or {}).items():
        shared = item.get("parameters") or []
        for method in METHODS:
            operation = item.get(method)
            if operation is None:
                continue
            if shared:
                operation = {**operation, "parameters": [*shared, *(operation.get("parameters") or [])]}
            operations[(method, PATH_PARAMETER.sub("{}", path))] = (method, path, operation)
    return operations


def _parameter_key(document: dict, parameter: dict, path: str) -> tuple[str, str]:
    resolved = parameter
    if "$ref" in parameter:
        resolved = _lookup(document, parameter["$ref"])
    location = resolved.get("in", "")
    name = resolved.get("name", "")
    if location == "path":
        # By position: the name of a path variable is not on the wire.
        names = PATH_PARAMETER.findall(path)
        token = "{" + name + "}"
        name = f"#{names.index(token)}" if token in names else name
    return (location, name)


def _types(schema: dict) -> set[str]:
    declared = schema.get("type")
    if declared is None:
        return set()
    return {declared} if isinstance(declared, str) else {item for item in declared if item != "null"}


def _split_nullable(schema: dict) -> tuple[bool, dict]:
    """(is nullable, the schema without its null alternative). Returns `schema` itself when there is none."""
    nullable = bool(schema.get("nullable"))
    declared = schema.get("type")
    if isinstance(declared, list) and "null" in declared:
        rest = [item for item in declared if item != "null"]
        return True, {**schema, "type": rest[0] if len(rest) == 1 else rest}
    for keyword in ("oneOf", "anyOf"):
        members = schema.get(keyword)
        if isinstance(members, list):
            nulls = [member for member in members if isinstance(member, dict) and member.get("type") == "null"]
            if nulls:
                rest = [member for member in members if member not in nulls]
                others = {key: value for key, value in schema.items() if key != keyword}
                if len(rest) == 1 and isinstance(rest[0], dict) and not others:
                    return True, rest[0]
                return True, {**others, keyword: rest} if rest else others
    return nullable, schema


def _members(value: object) -> dict[str, dict] | None:
    if not isinstance(value, list):
        return None
    members: dict[str, dict] = {}
    for index, member in enumerate(value):
        if not isinstance(member, dict):
            continue
        if "$ref" in member:
            name = member["$ref"].rsplit("/", 1)[-1]
        elif "type" in member:
            name = json.dumps(member.get("type"), sort_keys=True)
        else:
            name = f"#{index}"
        members[name] = member
    return members


def http_delta(root: pathlib.Path, tag: str, *, snapshot_required: bool) -> SurfaceResult:
    """Compares the committed OpenAPI document with the one at `tag`."""
    result = SurfaceResult(HTTP_LABEL)
    previous = bc._show(root, tag, str(HTTP_FILE))
    if previous is None:
        if snapshot_required:
            raise SurfaceError(f"{HTTP_FILE} '{tag}' etiketinde yok; donmuş bir tabanda HTTP karşılaştırması zorunludur")
        result.note = f"taban yok ({HTTP_FILE} '{tag}' etiketinde yok; ilk kayıt)"
        return result
    try:
        old = json.loads(previous)
        new = json.loads((root / HTTP_FILE).read_text(encoding="utf-8"))
    except (OSError, ValueError) as exception:
        raise SurfaceError(f"{HTTP_FILE} okunamadı ({exception})") from exception

    diff = _Diff(old, new)
    diff.compare()
    result.breaks, result.additions = diff.breaks, diff.additions
    return result


def compare_openapi(old: dict, new: dict) -> tuple[list[Change], list[Change]]:
    """The two OpenAPI documents' (breaks, additions); the unit under test."""
    diff = _Diff(old, new)
    diff.compare()
    return diff.breaks, diff.additions


# -----------------------------------------------------------------------------
# The gate as one call
# -----------------------------------------------------------------------------

MAX_LISTED = 12


def surface_results(root: pathlib.Path, baseline: str) -> list[SurfaceResult]:
    """HTTP first, then the name sets. Raises BaselineError when the tag is missing:
    a gate that cannot read its baseline must be red, never green."""
    tag = f"{bc.TAG_PREFIX}{baseline}"
    if bc._git(root, "rev-parse", "--verify", "--quiet", f"{tag}^{{commit}}").returncode:
        raise bc.BaselineError(f"taban etiketi '{tag}' bu depoda yok; sözleşme yüzeyleri karşılaştırılamaz")
    required = bc.compare_versions(baseline, bc.FROZEN_FROM) >= 0
    results = [http_delta(root, tag, snapshot_required=required)]
    results.extend(name_set_delta(root, tag, surface, snapshot_required=required) for surface in NAME_SURFACES)
    return results


def _named(change: Change, notes: str | None) -> bool:
    """A change is named when ONE code span holds its `ident`, or its `alt` as a whole word."""
    spans = bc._code_spans(notes or "")
    if any(change.ident in span for span in spans):
        return True
    if not change.alt:
        return False
    word = re.compile(rf"(?<![A-Za-z0-9_]){re.escape(change.alt)}(?![A-Za-z0-9_])", re.IGNORECASE)
    return any(word.search(span) for span in spans)


def _summary(results: Sequence[SurfaceResult], notes: str | None) -> str:
    parts = []
    for result in results:
        if result.note is not None:
            parts.append(f"{result.surface}: taban yok")
            continue
        count = len(result.breaks)
        text = f"{result.surface}: {count} kırılma"
        if count and notes is not None and all(_named(change, notes) for change in result.breaks):
            text += " (notta)"
        parts.append(text)
    return " · ".join(parts)


def _listed(changes: Iterable[Change]) -> list[str]:
    changes = list(changes)
    lines = [f"  {change.surface}: {change.subject} — {change.rule}" for change in changes[:MAX_LISTED]]
    if len(changes) > MAX_LISTED:
        lines.append(f"  … (+{len(changes) - MAX_LISTED})")
    return lines


def check(
    *,
    repo: pathlib.Path,
    baseline: str,
    version: str,
    changelog: pathlib.Path,
) -> int:
    """Prints one line per surface and the reason of every red. Returns 0 when green.

    `version` is `Unreleased` when the CLI has none (it reads `[Unreleased]`)."""
    try:
        line = bc.release_line(baseline, None if version == bc.UNRELEASED else version)
    except ValueError as exception:
        print(f"❌ Hat belirlenemedi: {exception}")
        return 1
    print(line.describe())

    try:
        results = surface_results(repo, baseline)
    except (bc.BaselineError, SurfaceError) as exception:
        print(f"❌ {exception}")
        return 1

    for result in results:
        if result.note is not None:
            print(f"ℹ️ {result.surface}: {result.note}")

    breaks = [change for result in results for change in result.breaks]
    additions = [change for result in results for change in result.additions]

    notes: str | None = None
    heading: str | None = None
    if breaks and not line.frozen:
        notes, heading = bc.release_notes_for(changelog, version)

    print(f"Sözleşme yüzeyleri: {_summary(results, notes)}")

    failed = False
    if line.frozen and breaks:
        print(f"❌ Donmuş hatta .NET dışı yüzeyde kırılma reddedilir (taban v{baseline}, sürüm {version}); sürüm notu bunu kurtarmaz:")
        for entry in _listed(breaks):
            print(entry)
        failed = True
    if line.frozen and line.patch and additions:
        print(f"❌ Yama sürümünde .NET dışı yüzey büyüyemez (taban v{baseline}, sürüm {version}); eklenenler:")
        for entry in _listed(additions):
            print(entry)
        failed = True

    if breaks and not line.frozen:
        if notes is None:
            print(f"❌ .NET dışı yüzeyde kırılma var ama '{version}' için sürüm notu yok (taban v{baseline}).")
            failed = True
        else:
            unnamed = [change for change in breaks if not _named(change, notes)]
            if unnamed:
                print(f"❌ Kırılma sürüm notunda adıyla geçmiyor (taban v{baseline}, not '{heading or 'yok'}'); "
                      "ad aynı code span içinde yazılır:")
                for entry in _listed(unnamed):
                    print(entry)
                failed = True

    if failed:
        return 1

    if breaks:
        print(f"✅ .NET dışı yüzeylerdeki {len(breaks)} kırılmanın hepsi '{heading or 'yok'}' notunda adıyla geçiyor")
    else:
        print(f"✅ .NET dışı yüzeylerde kırılma yok ({len(additions)} ekleme)")
    return 0


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        description="HTTP, SSE, telemetri, yapılandırma, hata kodu ve CLI yüzeylerini son sürümle karşılaştırır.")
    parser.add_argument("--depo", type=pathlib.Path, default=ROOT, help="karşılaştırılan git deposu (varsayılan: bu depo)")
    parser.add_argument("--taban", required=True, help="karşılaştırılan yayınlanmış sürüm, 'v'siz")
    parser.add_argument("--surum", help="notu okunacak sürüm; verilmezse '## [Unreleased]' (donmuş hatta sürüm gerekir)")
    parser.add_argument("--changelog", type=pathlib.Path, help="varsayılan: <depo>/CHANGELOG.md")
    args = parser.parse_args(argv)

    repo = args.depo.resolve()
    return check(
        repo=repo,
        baseline=args.taban,
        version=args.surum or bc.UNRELEASED,
        changelog=args.changelog or repo / "CHANGELOG.md",
    )


if __name__ == "__main__":
    raise SystemExit(main())
