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

A third pass fixes a NAME COLLISION class of defect: a server-side property
typed as an "opaque" .NET value type - one whose own [JsonConverter] makes it
serialize as something other than a plain JSON object, so ASP.NET Core's
OpenApi generator cannot describe its shape and emits an EMPTY {} schema
component named after the type's own short name (System.Text.Json.JsonElement
-> schema "JsonElement", Microsoft.Extensions.AI.ChatRole -> schema
"ChatRole"). NJsonSchema's "any type" setting (anyType: "object") only
applies to schemas it INLINES; a NAMED component with inlineNamedAny: false
instead gets its own generated POCO class, using the component's name
verbatim, which then SHADOWS the real type in the SAME namespace
(AgentPrism.Client.Generated) - every property typed this way deserializes
against a bogus empty class ([JsonExtensionData]-backed, object-only) instead
of the real one. MEASURED (2026-08-26): discovered via JsonElement, whose 16
affected properties throw JsonException on any wire value that is not a JSON
object (an array, e.g. EvalCaseResult.Scores) - a pre-existing, previously
undetected defect found while adding the eval CLI command (EvalCommandTests,
docs/arsiv/fazlar/115-EVALIN-BASSIZ-KOSUCUSU.md). CLASS SWEEP found a second instance:
ChatRole (ChatMessage.Role) has the identical empty-schema shape for the
identical reason (its own [JsonConverter(typeof(ChatRole.Converter))]).
Renaming a colliding schema would only rename the collision, not fix it: a
POCO can still never hold a JSON array or scalar, or apply the real type's own
converter. Each bogus class is deleted outright and every reference to it
qualified to the real type instead (COLLIDING_ANY_TYPES below).

If a FUTURE domain type gets exposed through AgentPrism's API and also has
its own opaque [JsonConverter], detect it the same way this class of defect
was found: after regenerating docs/openapi/agentprism.json, run
`python3 -c "import json; s = json.load(open('docs/openapi/agentprism.json'))
['components']['schemas']; print([k for k, v in s.items() if v == {}])"` -
add every name beyond JsonElement/ChatRole to COLLIDING_ANY_TYPES.

Usage: nswag-postprocess-client.py <generated-client.cs>
"""

import re
import sys

JSON_CONVERTER_ATTR = "System.Text.Json.Serialization.JsonConverter"
FACTORY = "System.Text.Json.Serialization.JsonStringEnumConverter"
NON_GENERIC_USAGE = f"[{JSON_CONVERTER_ATTR}(typeof({FACTORY}))]"

# Bogus wrapper class short name -> (the type it collides with and should be
# replaced by everywhere, whether that type is a VALUE type). JsonElement maps
# to the real BCL type directly - System.Text.Json is already referenced.
# ChatRole maps to "string" instead of the real Microsoft.Extensions.AI.ChatRole:
# AgentPrism.Client deliberately carries NO dependency on Microsoft.Extensions.AI
# (a typed HTTP client has no reason to reference the agent-framework
# package), and ChatRole's own [JsonConverter] serializes it as a plain JSON
# string ("assistant", "user", ...) - confirmed by round-tripping it directly
# - so "string" is both dependency-free and the actual wire shape, not a
# workaround. The value-type flag matters for null_check_re below: "string" is
# a REFERENCE type, so its (nonexistent, measured) root-response null check
# must be left alone rather than stripped.
COLLIDING_ANY_TYPES = {
    "JsonElement": ("System.Text.Json.JsonElement", True),
    "ChatRole": ("string", False),
}


def any_type_class_re(short_name: str) -> re.Pattern[str]:
    # The bogus wrapper class NJsonSchema generates for an empty-schema
    # component - a fixed shape (one [JsonExtensionData] catch-all property),
    # so a lazy match up to the first FOUR-SPACE-INDENTED closing brace
    # reliably finds the class's own "}" rather than the property's (indented
    # 8 spaces).
    return re.compile(
        r"    \[System\.CodeDom\.Compiler\.GeneratedCode\([^\r\n]*\)\]\r?\n"
        rf"    public partial class {re.escape(short_name)}\r?\n"
        r".*?\r?\n    \}\r?\n\r?\n",
        re.DOTALL,
    )


def bare_reference_re(short_name: str) -> re.Pattern[str]:
    # Whole-word, not already qualified with a preceding ".": every REMAINING
    # reference (property types, generic type arguments, method return types)
    # after the bogus class above is deleted.
    return re.compile(rf"(?<!\.)\b{re.escape(short_name)}\b")


def null_check_re(qualified_name: str) -> re.Pattern[str]:
    # NJsonSchema's "success" branch always null-checks a root response type
    # ("if (objectResponse_.Object == null)") because every OTHER generated
    # DTO is a class (reference type). A value type collision (JsonElement,
    # ChatRole, ...) is a STRUCT: CS0019, "Operator '==' cannot be applied to
    # operands of type '<T>' and '<null>'". A default struct value is never
    # meaningfully "null", so the check is dropped rather than reworked -
    # matches how a value-type root response would need to behave if
    # NJsonSchema's template were type-aware. Only fires where the type is
    # read as a ROOT response type directly
    # (ReadObjectResponseAsync<qualified_name>); most collisions (ChatRole) are
    # reachable only through a DTO property and never match this pattern at
    # all - that is expected, not an error.
    escaped = re.escape(qualified_name)
    return re.compile(
        rf"(var objectResponse_ = await ReadObjectResponseAsync<{escaped}>\([^;]*;\r?\n)"
        r"[ \t]*if \(objectResponse_\.Object == null\)\r?\n"
        r"[ \t]*\{\r?\n"
        r"[ \t]*throw new AgentPrismApiException\(\"Response was null which was not expected\.\", status_, objectResponse_\.Text, headers_, null\);\r?\n"
        r"[ \t]*\}\r?\n"
    )

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


def rewrite_colliding_any_types(source: str) -> tuple[str, int]:
    total_references = 0

    for short_name, (qualified_name, is_value_type) in COLLIDING_ANY_TYPES.items():
        source, class_count = any_type_class_re(short_name).subn("", source)

        if class_count > 1:
            raise SystemExit(
                f"Expected at most one generated '{short_name}' wrapper class, found "
                f"{class_count} (the OpenAPI document changed shape - inspect the generated file)."
            )

        source, reference_count = bare_reference_re(short_name).subn(qualified_name, source)

        if is_value_type:
            source, _ = null_check_re(qualified_name).subn(r"\1", source)

        if class_count and reference_count == 0:
            raise SystemExit(
                f"The '{short_name}' wrapper class was removed but no reference was left to "
                "qualify - the class-removal regex likely also ate the properties that used it "
                "(inspect the generated file)."
            )

        total_references += reference_count

    return source, total_references


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
    source, any_type_reference_count = rewrite_colliding_any_types(source)

    with open(path, "w", encoding="utf-8") as f:
        f.write(source)

    print(
        f"Rewrote {property_count} JsonStringEnumConverter property attribute(s), added "
        f"{enum_count} type-level JsonConverter attribute(s) to enum declarations, and qualified "
        f"{any_type_reference_count} colliding any-type reference(s) "
        f"({', '.join(COLLIDING_ANY_TYPES)}) in {path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
