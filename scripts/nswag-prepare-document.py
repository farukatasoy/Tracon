#!/usr/bin/env python3
"""Prepares docs/openapi/agentprism.json for NSwag client generation.

Two transforms, both applied to a throwaway copy - the committed document is
never touched:

1. Strip the '/agentprism' MapAgentPrism prefix from every path. The document
   is generated with every path relative to the DEFAULT MapAgentPrism prefix;
   that prefix is a runtime parameter
   (AgentPrismEndpointRouteBuilderExtensions.cs), so a client generated
   straight from the document would 404 against any consumer that calls
   MapAgentPrism with a custom prefix. AgentPrismClientOptions.BaseAddress
   carries the prefix instead (docs/83-TIPLI-ISTEMCI-VE-CLI.md, section 83.3).

2. Close every object schema (`additionalProperties: false`) that does not
   already say otherwise. Every schema here is generated from a concrete C#
   type - none of them is meant to accept arbitrary extra JSON keys - but
   NJsonSchema's C# generator treats an unspecified `additionalProperties` as
   "allowed" and synthesizes a `JsonExtensionData`-backed `AdditionalProperties`
   catch-all property on EVERY generated DTO. For roughly 30 of MEAI's
   polymorphic `AIContent`/`ToolCallContent` schemas, the JSON schema also
   has a REAL property literally named "additionalProperties" (mirroring a
   same-named property on the source C# type), and the synthesized catch-all
   collides with it: CS0102, "type already contains a definition for
   AdditionalProperties". Closing the schemas removes the synthesized
   property everywhere, which is also the more correct contract for DTOs that
   have no legitimate use for a client-side catch-all (section 83.6).

Usage: nswag-prepare-document.py <input.json> <output.json> [prefix]
"""

import json
import sys

DEFAULT_PREFIX = "/agentprism"


def strip_prefix(document: dict, prefix: str) -> None:
    paths = document.get("paths", {})
    stripped_paths = {}
    for path, item in paths.items():
        if not path.startswith(prefix):
            raise SystemExit(f"Path '{path}' does not start with expected prefix '{prefix}'")
        stripped = path[len(prefix):]
        stripped_paths[stripped or "/"] = item
    document["paths"] = stripped_paths


def close_schemas(document: dict) -> int:
    schemas = document.get("components", {}).get("schemas", {})
    closed = 0
    for schema in schemas.values():
        if "properties" in schema and "additionalProperties" not in schema:
            schema["additionalProperties"] = False
            closed += 1
    return closed


def main() -> int:
    if len(sys.argv) not in (3, 4):
        print(f"Usage: {sys.argv[0]} <input.json> <output.json> [prefix]", file=sys.stderr)
        return 1

    input_path, output_path = sys.argv[1], sys.argv[2]
    prefix = sys.argv[3] if len(sys.argv) == 4 else DEFAULT_PREFIX

    with open(input_path, encoding="utf-8") as f:
        document = json.load(f)

    strip_prefix(document, prefix)
    closed = close_schemas(document)

    with open(output_path, "w", encoding="utf-8") as f:
        json.dump(document, f, indent=2, ensure_ascii=False)
        f.write("\n")

    paths = document["paths"]
    print(f"Stripped '{prefix}' from {len(paths)} paths, closed {closed} schemas -> {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
