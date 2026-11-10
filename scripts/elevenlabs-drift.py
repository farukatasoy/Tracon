#!/usr/bin/env python3
"""Compares the OpenAPI document ElevenLabs publishes with the dependency declaration of Tracon.Voice.

`Tracon.Voice` talks to ElevenLabs through a raw `HttpClient`; there is no official .NET
SDK. The compiler therefore cannot see the provider changing its contract.
`tests/Tracon.Voice.UnitTests/Fixtures/elevenlabs-operations.json` DECLARES the operations
Tracon uses, their query parameters, their field paths and the JSON type of every field;
a unit test (`ElevenLabsOperationDriftTests`) proves the declaration matches the code
exactly. This script asks whether the declaration still holds in the LIVE document.
It depends on the network, so it does not run in CI: run it by hand before a release and
during defect rounds.

Usage:
    python3 scripts/elevenlabs-drift.py                 # downloads the live document
    python3 scripts/elevenlabs-drift.py --file x.json    # with a local document (tests)

Exit code 0: the declaration holds in the live document. 1: drift found (the list is printed).
2: the document could not be downloaded.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import sys
import urllib.error
import urllib.request

ROOT = pathlib.Path(__file__).resolve().parent.parent
FIXTURE = ROOT / "tests" / "Tracon.Voice.UnitTests" / "Fixtures" / "elevenlabs-operations.json"
LIVE_DOCUMENT = "https://api.elevenlabs.io/openapi.json"


def resolve(document: dict, node: dict) -> dict:
    """Follows `$ref` chains inside the document."""
    seen = 0
    while isinstance(node, dict) and "$ref" in node:
        target = document
        for part in node["$ref"].lstrip("#/").split("/"):
            target = target[part.replace("~1", "/").replace("~0", "~")]
        node = target
        seen += 1
        if seen > 32:
            raise ValueError("$ref cycle")
    return node


def alternatives(document: dict, node: dict) -> list[dict]:
    """Every concrete schema a node can be: unions (`anyOf`/`oneOf`/`allOf`) are flattened."""
    node = resolve(document, node)
    parts = [node]
    for key in ("anyOf", "oneOf", "allOf"):
        for option in node.get(key, []):
            parts.extend(alternatives(document, option))
    return parts


def schema_types(document: dict, node: dict) -> set[str]:
    """JSON types a property can have, `null` left out. Empty when the document does not say."""
    found: set[str] = set()
    for option in alternatives(document, node):
        declared = option.get("type")
        if isinstance(declared, str):
            found.add(declared)
        elif isinstance(declared, list):
            found.update(item for item in declared if isinstance(item, str))
        elif "properties" in option:
            found.add("object")
        elif "items" in option:
            found.add("array")
    found.discard("null")
    return found


def find_types(document: dict, schema: dict, path: list[str]) -> set[str] | None:
    """Live types of the property at `path` (e.g. ['voices[]', 'voice_id']), or None when it is gone.

    An empty set means the property exists but the document gives it no type."""
    if not path:
        return set()

    head, rest = path[0], path[1:]
    is_array = head.endswith("[]")
    name = head[:-2] if is_array else head

    for option in alternatives(document, schema):
        properties = option.get("properties", {})
        if name not in properties:
            continue
        child = properties[name]
        if not rest:
            return schema_types(document, child)
        if is_array:
            for candidate in alternatives(document, child):
                if candidate.get("type") == "array" and "items" in candidate:
                    found = find_types(document, candidate["items"], rest)
                    if found is not None:
                        return found
        else:
            found = find_types(document, child, rest)
            if found is not None:
                return found

    return None


def has_path(document: dict, schema: dict, path: list[str]) -> bool:
    """True when `path` (e.g. ['voices[]', 'voice_id']) exists under `schema`."""
    return find_types(document, schema, path) is not None


def type_matches(declared: str, live: set[str]) -> bool:
    """Whether what Tracon declares fits what the document says.

    The document saying nothing fits anything. An integer Tracon sends or reads fits a
    `number`; a number does not fit an `integer` (1.5 would be refused)."""
    if not live:
        return True
    if declared == "integer":
        return bool(live & {"integer", "number"})
    return declared in live


def operation_parameters(document: dict, path_item: dict, operation: dict) -> set[str]:
    names: set[str] = set()
    for parameter in [*path_item.get("parameters", []), *operation.get("parameters", [])]:
        names.add(resolve(document, parameter)["name"])
    return names


def request_schema(document: dict, operation: dict) -> dict | None:
    content = resolve(document, operation.get("requestBody", {})).get("content", {})
    for media in ("application/json", "multipart/form-data"):
        if media in content:
            return content[media].get("schema")
    return None


def response_schema(document: dict, operation: dict) -> dict | None:
    content = resolve(document, operation.get("responses", {}).get("200", {})).get("content", {})
    return content.get("application/json", {}).get("schema")


def check(document: dict, declaration: dict) -> list[str]:
    problems: list[str] = []

    for declared in declaration["operations"]:
        label = f"{declared['method']} {declared['path']}"
        path_item = document.get("paths", {}).get(declared["path"])
        operation = (path_item or {}).get(declared["method"].lower())

        if operation is None:
            problems.append(f"{label}: the operation is gone")
            continue

        live_parameters = operation_parameters(document, path_item, operation)
        for name in declared["query"]:
            if name not in live_parameters:
                problems.append(f"{label}: query parameter '{name}' is gone")

        for side, schema in (("request", request_schema(document, operation)),
                             ("response", response_schema(document, operation))):
            fields = declared[side]
            if not fields:
                continue
            if schema is None:
                problems.append(f"{label}: has no {side} body schema but Tracon relies on {len(fields)} field(s)")
                continue
            declared_types = declared.get("types", {}).get(side, {})
            for field in fields:
                live = find_types(document, schema, field.split("."))
                if live is None:
                    problems.append(f"{label}: {side} field '{field}' is gone")
                elif field in declared_types and not type_matches(declared_types[field], live):
                    problems.append(
                        f"{label}: {side} field '{field}' is {'/'.join(sorted(live))} "
                        f"but Tracon declares {declared_types[field]}")

    return problems


def main() -> int:
    parser = argparse.ArgumentParser(
        description=("Compares the OpenAPI document ElevenLabs publishes with the dependency declaration "
                     "of Tracon.Voice. Exit code 0: the declaration holds in the live document. "
                     "1: drift found (the list is printed). 2: the document could not be downloaded."))
    parser.add_argument("--file", help="a local OpenAPI JSON file instead of the live document")
    arguments = parser.parse_args()

    if arguments.file:
        document = json.loads(pathlib.Path(arguments.file).read_text(encoding="utf-8"))
    else:
        try:
            with urllib.request.urlopen(LIVE_DOCUMENT, timeout=60) as response:  # noqa: S310 - fixed https address
                document = json.loads(response.read())
        except urllib.error.URLError as error:
            # Certificate verification is NEVER switched off. When a python.org install has no
            # CA bundle (macOS), download the document with curl and pass it with --file.
            print(f"❌ Could not download the live document: {error.reason}", file=sys.stderr)
            print(f"   curl -s {LIVE_DOCUMENT} -o openapi.json && "
                  "python3 scripts/elevenlabs-drift.py --file openapi.json", file=sys.stderr)
            return 2

    declaration = json.loads(FIXTURE.read_text(encoding="utf-8"))
    problems = check(document, declaration)

    if problems:
        print(f"❌ ElevenLabs drift: {len(problems)} problem(s)")
        for problem in problems:
            print(f"  {problem}")
        return 1

    fields = sum(len(o["request"]) + len(o["response"]) + len(o["query"]) for o in declaration["operations"])
    print(f"✅ {len(declaration['operations'])} operation(s), {fields} declaration(s) hold in the live document "
          f"(OpenAPI {document.get('openapi')}, {len(document.get('paths', {}))} path(s))")
    return 0


if __name__ == "__main__":
    sys.exit(main())
