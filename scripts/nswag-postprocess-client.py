#!/usr/bin/env python3
"""Rewrites the NON-generic JsonStringEnumConverter NSwag emits into the
AOT-safe closed-generic form, in place, on the NSwag-generated client.

NJsonSchema's System.Text.Json output puts a per-property
[JsonConverter(typeof(JsonStringEnumConverter))] on the discriminator "Type"
properties of MEAI's polymorphic AIContent hierarchy (~30 properties). The
non-generic JsonStringEnumConverter is a JsonConverterFactory: at runtime it
resolves the concrete enum type via `MakeGenericType`, which is
[RequiresDynamicCode] and fails a Native AOT publish. A property-level
[JsonConverter] always wins over anything registered on
JsonSerializerOptions.Converters, so this cannot be fixed by configuration
alone - the attribute itself has to name the closed generic
JsonStringEnumConverter<TEnum> (docs/arsiv/fazlar/83-TIPLI-ISTEMCI-VE-CLI.md, section 83.6).

Every OTHER enum (the ~37 plain business enums: RunStatus, ApiKeyScope, ...)
carries no per-property attribute at all. MEASURED (2026-08-22): registering
them globally via the Converters list on AgentPrismClientJsonContext's
[JsonSourceGenerationOptions] does NOT take effect for property-reachable
enum types - the source generator still bakes in the default numeric
EnumConverter<T> for them, so a wire value like "Running" throws a
JsonException instead of parsing. A functional test caught this
(RunKind failed to deserialize "Agent" - ClientTenantScopeTests). The second
pass below closes the gap the same way the first one does: a type-level
[JsonConverter(typeof(JsonStringEnumConverter<T>))] directly on each `enum`
declaration, which IS honored reliably by the source generator.

Usage: nswag-postprocess-client.py <generated-client.cs>
"""

import re
import sys

JSON_CONVERTER_ATTR = "System.Text.Json.Serialization.JsonConverter"
FACTORY = "System.Text.Json.Serialization.JsonStringEnumConverter"
NON_GENERIC_USAGE = f"[{JSON_CONVERTER_ATTR}(typeof({FACTORY}))]"

# Matches the attribute followed by the property declaration whose type it
# converts. Captures: (1) leading whitespace + "public ", (2) the property
# type name, (3) an optional "?" for a nullable property, (4) the rest of the
# line (property name onward).
PROPERTY_PATTERN = re.compile(
    re.escape(NON_GENERIC_USAGE) + r"\r?\n"
    r"(?P<prefix>\s*public\s+)(?P<type>[A-Za-z_]\w*)(?P<nullable>\??)(?P<rest>\s+\w+)"
)

# Matches an enum declaration with no [JsonConverter] attribute above it yet
# (the property-level pass above may already have decorated the TYPE
# reference on a property, but never the enum's own declaration).
ENUM_DECLARATION_PATTERN = re.compile(r"(?P<indent>[ \t]*)public enum (?P<name>\w+)\r?\n")


def rewrite_property_attributes(source: str) -> tuple[str, int]:
    count = 0

    def replace(match: re.Match[str]) -> str:
        nonlocal count
        count += 1
        enum_type = match.group("type")
        return (
            f"[{JSON_CONVERTER_ATTR}(typeof({FACTORY}<{enum_type}>))]\n"
            f"{match.group('prefix')}{enum_type}{match.group('nullable')}{match.group('rest')}"
        )

    return PROPERTY_PATTERN.sub(replace, source), count


def rewrite_enum_declarations(source: str) -> tuple[str, int]:
    count = 0

    def replace(match: re.Match[str]) -> str:
        nonlocal count
        count += 1
        indent, name = match.group("indent"), match.group("name")
        return (
            f"{indent}[{JSON_CONVERTER_ATTR}(typeof({FACTORY}<{name}>))]\n"
            f"{indent}public enum {name}\n"
        )

    return ENUM_DECLARATION_PATTERN.sub(replace, source), count


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <generated-client.cs>", file=sys.stderr)
        return 1

    path = sys.argv[1]
    with open(path, encoding="utf-8") as f:
        source = f.read()

    source, property_count = rewrite_property_attributes(source)

    remaining = source.count(NON_GENERIC_USAGE)
    if remaining:
        raise SystemExit(
            f"{remaining} non-generic JsonStringEnumConverter usage(s) left after rewrite "
            "(regex did not match every occurrence - inspect the generated file)."
        )

    source, enum_count = rewrite_enum_declarations(source)

    with open(path, "w", encoding="utf-8") as f:
        f.write(source)

    print(
        f"Rewrote {property_count} JsonStringEnumConverter property attribute(s) and added "
        f"{enum_count} type-level JsonConverter attribute(s) to enum declarations in {path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
