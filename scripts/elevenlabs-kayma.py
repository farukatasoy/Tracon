#!/usr/bin/env python3
"""ElevenLabs'in yayimlanan OpenAPI belgesini Tracon.Voice'in bagimlilik beyaniyla karsilastirir.

`Tracon.Voice` ElevenLabs'e ham `HttpClient` ile konusur (K-216); resmi bir .NET
SDK'si yoktur. Bu yuzden sağlayicinin sozlesme degisikligini derleyici gormez.
`tests/Tracon.Voice.UnitTests/Fixtures/elevenlabs-operations.json` Tracon'un
kullandigi operasyonlari, sorgu parametrelerini ve alan yollarini BEYAN eder;
birim testi (`ElevenLabsOperationDriftTests`) o beyanin KODLA birebir ayni
oldugunu kanitlar. Bu betik beyanin CANLI belgede hala gecerli olup olmadigini
sorar. Agdan etkilenir, bu yuzden CI'da kosmaz: `nuget-danismani` Adim 0.2 ve
`kusur-giderme` turlarinda elle kosulur.

Kullanim:
    python3 scripts/elevenlabs-kayma.py                 # canli belgeyi indirir
    python3 scripts/elevenlabs-kayma.py --dosya x.json  # yerel belgeyle (test)

Cikis kodu 0: beyan canli belgede gecerli. 1: kayma var (liste basilir). 2: belge indirilemedi.
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


def has_path(document: dict, schema: dict, path: list[str]) -> bool:
    """True when `path` (e.g. ['voices[]', 'voice_id']) exists under `schema`."""
    if not path:
        return True

    head, rest = path[0], path[1:]
    is_array = head.endswith("[]")
    name = head[:-2] if is_array else head

    for option in alternatives(document, schema):
        properties = option.get("properties", {})
        if name not in properties:
            continue
        child = properties[name]
        if is_array:
            for candidate in alternatives(document, child):
                if candidate.get("type") == "array" and "items" in candidate:
                    if has_path(document, candidate["items"], rest):
                        return True
        elif has_path(document, child, rest):
            return True

    return False


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
            for field in fields:
                if not has_path(document, schema, field.split(".")):
                    problems.append(f"{label}: {side} field '{field}' is gone")

    return problems


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dosya", help="Canli belge yerine yerel bir OpenAPI JSON dosyasi")
    arguments = parser.parse_args()

    if arguments.dosya:
        document = json.loads(pathlib.Path(arguments.dosya).read_text(encoding="utf-8"))
    else:
        try:
            with urllib.request.urlopen(LIVE_DOCUMENT, timeout=60) as response:  # noqa: S310 - sabit https adres
                document = json.loads(response.read())
        except urllib.error.URLError as error:
            # Sertifika dogrulamasi ASLA kapatilmaz. python.org kurulumunun CA paketi
            # yoksa (macOS) belgeyi curl indirir ve --dosya ile verilir.
            print(f"❌ Canli belge indirilemedi: {error.reason}", file=sys.stderr)
            print(f"   curl -s {LIVE_DOCUMENT} -o openapi.json && "
                  "python3 scripts/elevenlabs-kayma.py --dosya openapi.json", file=sys.stderr)
            return 2

    declaration = json.loads(FIXTURE.read_text(encoding="utf-8"))
    problems = check(document, declaration)

    if problems:
        print(f"❌ ElevenLabs kaymasi: {len(problems)} sorun")
        for problem in problems:
            print(f"  {problem}")
        return 1

    fields = sum(len(o["request"]) + len(o["response"]) + len(o["query"]) for o in declaration["operations"])
    print(f"✅ {len(declaration['operations'])} operasyon, {fields} beyan canli belgede gecerli "
          f"(OpenAPI {document.get('openapi')}, {len(document.get('paths', {}))} yol)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
