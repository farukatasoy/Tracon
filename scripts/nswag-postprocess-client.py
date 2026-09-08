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

A fourth pass fixes a JSON-vs-SSE class of defect (Faz 145, found by
independent audit): every pure `text/event-stream` 200 response has a bare
`string` schema, so NSwag generates `Task<string>` and reads it through the
SAME `ReadObjectResponseAsync<T>` helper every JSON-bodied operation uses -
which calls `System.Text.Json.JsonSerializer.Deserialize<string>` on the raw
SSE body. A real SSE frame ("event: run.started\ndata: {...}\n\n") is not a
JSON string literal, so every call to one of these methods throws
`JsonException` unconditionally - MEASURED with an isolated repro
(`JsonSerializer.Deserialize<string>(sseText)` -> "'e' is an invalid start of
a value."). This is invisible to `dotnet build` (no compile error) and to
`ClientCoverageTests` (which only checks the method NAME exists, not that a
call succeeds) - nothing in the repo calls a generated SSE client method
against a real server. The fix does not touch the schema (a raw SSE stream
still has no JSON shape to describe): it rewrites just the 200-branch of each
such operation to read the body as plain text instead of through the
JSON-deserializing helper. Matched structurally, not by an operation-name
list: a `status_ == 200` branch that calls `ReadObjectResponseAsync<string>`
IS a pure-SSE-string response in this API - no other operation shape produces
that combination (verified 2026-09-05: exactly 5 occurrences, all under
`status_ == 200`, matching the 5 pure-SSE endpoints; the two dual JSON/SSE
endpoints - `/v1/responses`, `/v1/chat/completions` - generate a JSON return
type instead and are unaffected, a separate pre-existing limitation this pass
does not attempt to fix).

A fifth pass fixes a NULL-COLLECTION class of defect (F-197, found by phase
145's independent audit while writing a real-server test). NJsonSchema emits
`= default!` for EVERY property - including the ones it declares as
NON-nullable collections (`ICollection<T>`/`IDictionary<K,V>` with no `?`).
The declared type promises non-null and the value is null, so nothing warns
the caller: the client then serializes an explicit `"documents": null`, and
System.Text.Json OVERWRITES the server record's own `= []` initializer with
that null. Server code that reads the collection unconditionally
(`request.Documents.Count`, `AgentEndpoints.RunAsync`) throws
NullReferenceException - a 500 for a request the type system called valid.
MEASURED (2026-09-06): a real-server call carrying only `Message` NREs;
`GeneratedClientSseTests` used to fill all four collections by hand purely to
work around it.

The NULLABLE annotation is the discriminator, and it is left alone: a property
NSwag declared `ICollection<T>?` says "not provided" is distinguishable from
"provided empty", and rewriting it would erase a real distinction (measured:
56 such properties, e.g. `AgentRunRequest.Parameters`). Only the 50
non-nullable ones are rewritten, to `new List<T>()`/`new Dictionary<K,V>()`.
This pass fixes the TYPED CLIENT only - a raw HTTP caller can still send an
explicit null, which the server rejects with a 400 instead of a 500
(`RequestBodyBinding`'s RespectNullableAnnotations, same class, K-694).

A sixth pass fixes a CONDITIONAL-SHAPE class of defect (Faz 159). Seven
operations answer 200 with `text/event-stream`; NSwag can express only ONE
return type per operation, so all seven are callable in exactly one shape:
the five pure-SSE ones buffer the WHOLE stream into a `Task<string>` (the
fourth pass above made that at least work), and the two dual ones -
`/v1/responses` and `/v1/chat/completions`, whose 200 declares BOTH
`application/json` and `text/event-stream` - generate only the JSON shape, so
a caller sending `"stream": true` hands an SSE body to a JSON deserializer.
No OpenAPI document can describe a response shape chosen at run time from a
flag in the REQUEST BODY, and no single generated signature can enforce one.

The fix gives every such operation a SIBLING method, `<operation>StreamAsync`,
returning `IAsyncEnumerable<string>` - one raw SSE frame per element, yielded
as the server flushes it - so the choice is made at COMPILE time by which
method is called, and a wrong choice fails at the call site rather than
turning into a runtime surprise. The sibling is a copy of the generated
method, so it keeps NSwag's own URL building, body serialization,
PrepareRequest/ProcessResponse hooks and every typed error branch; only three
things change: the signature, the `Accept` header (`text/event-stream`), and
the 200 branch. Framing and the content-type guard are NOT emitted here - they
are hand-written in src/AgentPrism.Client/AgentPrismApiClient.Sse.cs, where
they can be unit tested and where a regeneration cannot alter them.

Method and body can still disagree (`...StreamAsync` with `"stream": false`,
or the JSON method with `"stream": true`). The client does NOT rewrite the
caller's body - silently changing what was asked for is a worse surprise than
a clear failure - so both methods instead check what the server ACTUALLY
answered and throw a named AgentPrismApiException. MEASURED (2026-09-08):
before this, `...StreamAsync`'s mismatch would have been a silently EMPTY
stream (an SSE parser finds no frames in a JSON document), and the JSON
method's was an opaque "Could not deserialize the response body stream as
System.Text.Json.JsonElement".

Which operations are dual cannot be read off the generated file: NSwag emits
only the FIRST content type as the `Accept` header, so a dual operation looks
exactly like a plain JSON one (verified 2026-09-08). This pass therefore reads
the OpenAPI document itself, which is why the script now takes it as a second
argument - required, not defaulted, so a forgotten path fails loudly instead
of silently generating no siblings.

🚨 ORDERING: this pass runs BEFORE the fourth one, even though it is the sixth
written. Before the fourth pass rewrites them, all seven 200 branches still
have NJsonSchema's single uniform shape (`ReadObjectResponseAsync<T>` +
optional null check + return), so one pattern matches all seven. The fourth
pass then still finds its five - the siblings' 200 branches no longer look
like anything it matches - which `main` asserts.

Usage: nswag-postprocess-client.py <generated-client.cs> <openapi-document.json>
"""

import json
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


# NJsonSchema guards a REQUIRED request body with `if (body == null)`, which
# stops compiling (CS0019) the moment the third pass turns that body's type
# into a struct - the same defect class as the root-response null check below,
# on a different axis (Faz 159, when /v1/responses and /v1/chat/completions
# started declaring a JsonElement body).
#
# Here the guard is REPLACED rather than dropped, unlike the response side.
# `default(JsonElement)` is a real caller mistake, and MEASURED (2026-09-08) it
# otherwise fails deep inside the serializer with "InvalidOperationException:
# Operation is not valid due to the current state of the object" - naming
# neither the parameter, nor the method, nor the cause.
VALUE_TYPE_BODY_GUARDS = {
    "System.Text.Json.JsonElement": (
        "            if (body.ValueKind == System.Text.Json.JsonValueKind.Undefined)\n"
        "                throw new System.ArgumentException(\"The request body is an uninitialized "
        "JsonElement. Build one first, for example with "
        "System.Text.Json.JsonSerializer.SerializeToElement(value).\", \"body\");\n"),
}


def body_null_check_re(qualified_name: str) -> re.Pattern[str]:
    return re.compile(
        # `[(, ]`, not a bare space: the body is the FIRST parameter of the two
        # dual operations, so the character before its type is "(", while a
        # method with a route parameter first has ", " there instead.
        r"(?P<signature>[ ]{8}public virtual async [^\r\n]*?[(, ]"
        + re.escape(qualified_name)
        + r" body[,)][^\r\n]*\r?\n[ ]{8}\{\r?\n)"
        r"[ ]{12}if \(body == null\)\r?\n"
        r'[ ]{16}throw new System\.ArgumentNullException\("body"\);\r?\n')


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

            guard = VALUE_TYPE_BODY_GUARDS.get(qualified_name)

            if guard is None:
                raise SystemExit(
                    f"'{qualified_name}' is a value type with no entry in VALUE_TYPE_BODY_GUARDS. If it "
                    "is ever used as a REQUIRED request body, NJsonSchema's `if (body == null)` guard "
                    "will not compile (CS0019) - add the guard that names an uninitialized value.")

            source, _ = body_null_check_re(qualified_name).subn(
                lambda match: match.group("signature") + guard, source)

        if class_count and reference_count == 0:
            raise SystemExit(
                f"The '{short_name}' wrapper class was removed but no reference was left to "
                "qualify - the class-removal regex likely also ate the properties that used it "
                "(inspect the generated file)."
            )

        total_references += reference_count

    return source, total_references


# A pure-SSE 200 branch: matches the JSON-deserializing helper call, its null
# check, and the return - exactly the block NSwag emits for a bare `string`
# root response type. Whitespace-sensitive on purpose (see module docstring's
# fourth-pass note): a change to NJsonSchema's template should make this stop
# matching rather than silently matching something else.
SSE_STRING_RESPONSE_PATTERN = re.compile(
    r"( *)if \(status_ == 200\)\r?\n"
    r" *\{\r?\n"
    r" *var objectResponse_ = await ReadObjectResponseAsync<string>\(response_, headers_, cancellationToken\)\.ConfigureAwait\(false\);\r?\n"
    r" *if \(objectResponse_\.Object == null\)\r?\n"
    r" *\{\r?\n"
    r' *throw new AgentPrismApiException\("Response was null which was not expected\.", status_, objectResponse_\.Text, headers_, null\);\r?\n'
    r" *\}\r?\n"
    r" *return objectResponse_\.Object;\r?\n"
    r" *\}\r?\n"
)


def rewrite_sse_string_responses(source: str) -> tuple[str, int]:
    def replace(match: re.Match[str]) -> str:
        indent = match.group(1)
        return (
            f"{indent}if (status_ == 200)\n"
            f"{indent}{{\n"
            f"{indent}    // Phase 145: an SSE body is not JSON - ReadObjectResponseAsync<string>\n"
            f"{indent}    // would JsonSerializer.Deserialize<string> the raw \"event: ...\\ndata: ...\"\n"
            f"{indent}    // text and throw. Read it as plain text instead. response_.Content is\n"
            f"{indent}    // nullable per HttpResponseMessage's own contract - same null check\n"
            f"{indent}    // ReadObjectResponseAsync<T> above makes before it reads the body.\n"
            f"{indent}    if (response_.Content == null)\n"
            f"{indent}    {{\n"
            f"{indent}        return string.Empty;\n"
            f"{indent}    }}\n"
            f"{indent}    return await response_.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);\n"
            f"{indent}}}\n"
        )

    return SSE_STRING_RESPONSE_PATTERN.subn(replace, source)


# A NON-nullable generated collection property left at `default!`. The absence
# of `?` after the closing angle bracket is the whole point (see the module
# docstring's fifth-pass note): a nullable collection keeps its null, because
# the annotation is the contract that says "not provided" differs from
# "provided empty". `[^<>]*` deliberately refuses nested generics - NSwag emits
# none among the DTO properties (verified 2026-09-06), and a nested one would
# need a different concrete type than the two below, so it must fail loudly
# by not matching rather than be rewritten wrongly.
NULL_COLLECTION_PATTERN = re.compile(
    r"(?P<prefix>public System\.Collections\.Generic\.)"
    r"(?P<kind>ICollection|IDictionary)"
    r"(?P<args><[^<>]*>)"
    r"(?P<rest> \w+ \{ get; set; \}) = default!;"
)

# The concrete type each generated interface is initialized with.
CONCRETE_COLLECTION = {"ICollection": "List", "IDictionary": "Dictionary"}


def rewrite_null_collection_defaults(source: str) -> tuple[str, int]:
    def replace(match: re.Match[str]) -> str:
        concrete = CONCRETE_COLLECTION[match.group("kind")]
        return (
            f"{match.group('prefix')}{match.group('kind')}{match.group('args')}"
            f"{match.group('rest')} = "
            f"new System.Collections.Generic.{concrete}{match.group('args')}();"
        )

    return NULL_COLLECTION_PATTERN.subn(replace, source)


EVENT_STREAM_MEDIA_TYPE = "text/event-stream"
JSON_MEDIA_TYPE = "application/json"

HTTP_METHODS = ("get", "put", "post", "delete", "options", "head", "patch", "trace")


def read_event_stream_operations(document_path: str) -> list[tuple[str, bool]]:
    """(operationId, is_dual) for every operation whose 200 declares SSE.

    `is_dual` means the SAME 200 also declares application/json - the shape is
    then chosen at run time by the request body's "stream" flag, and the JSON
    method needs the reverse guard as well.
    """
    with open(document_path, encoding="utf-8") as f:
        document = json.load(f)

    operations = []

    for path, path_item in document.get("paths", {}).items():
        for method, operation in path_item.items():
            if method.lower() not in HTTP_METHODS or not isinstance(operation, dict):
                continue

            content = operation.get("responses", {}).get("200", {}).get("content", {})

            if EVENT_STREAM_MEDIA_TYPE not in content:
                continue

            operation_id = operation.get("operationId")

            if not operation_id:
                raise SystemExit(
                    f"Operation {method.upper()} {path} declares {EVENT_STREAM_MEDIA_TYPE} but has no "
                    "operationId, so its generated method name cannot be derived "
                    "(ClientCoverageTests asserts every operation has one).")

            operations.append((operation_id, JSON_MEDIA_TYPE in content))

    return operations


# One whole generated operation method, from its XML doc block to the closing
# brace. The terminator is NSwag's own invariant tail - the outer `finally`
# that disposes the client - which every operation method ends with and
# nothing else in the file contains, so a lazy `.*?` up to it cannot run past
# the method it started in. A change to NJsonSchema's template should make
# this stop matching rather than silently match something else.
def operation_method_re(method_name: str) -> re.Pattern[str]:
    return re.compile(
        r"(?P<doc>(?:[ ]{8}///[^\r\n]*\r?\n)+)"
        r"[ ]{8}public virtual async (?P<returns>[^\r\n]+?) " + re.escape(method_name)
        + r"\((?P<params>[^\r\n]*)\)\r?\n"
        r"(?P<body>[ ]{8}\{\r?\n"
        r".*?"
        r"[ ]{12}finally\r?\n"
        r"[ ]{12}\{\r?\n"
        r"[ ]{16}if \(disposeClient_\)\r?\n"
        r"[ ]{20}client_\.Dispose\(\);\r?\n"
        r"[ ]{12}\}\r?\n"
        r"[ ]{8}\}\r?\n)",
        re.DOTALL,
    )


# The `Accept` header NSwag emits from the operation's FIRST declared response
# content type. The streaming sibling has to ask for the other one.
ACCEPT_HEADER_RE = re.compile(
    r'request_\.Headers\.Accept\.Add\(System\.Net\.Http\.Headers\.'
    r'MediaTypeWithQualityHeaderValue\.Parse\("[^"]*"\)\);')

# NJsonSchema's uniform 200 branch, before the fourth pass rewrites the
# pure-SSE ones. The null check is absent when the root response type is a
# value type (the third pass strips it - JsonElement), so it is optional here.
JSON_200_BRANCH_RE = re.compile(
    r"(?P<open>(?P<indent>[ ]*)if \(status_ == 200\)\r?\n[ ]*\{\r?\n)"
    r"(?P<read>[ ]*var objectResponse_ = await ReadObjectResponseAsync<(?P<type>[\w\.]+)>"
    r"\(response_, headers_, cancellationToken\)\.ConfigureAwait\(false\);\r?\n)"
    r"(?P<nullcheck>[ ]*if \(objectResponse_\.Object == null\)\r?\n"
    r"[ ]*\{\r?\n"
    r'[ ]*throw new AgentPrismApiException\("Response was null which was not expected\.", '
    r"status_, objectResponse_\.Text, headers_, null\);\r?\n"
    r"[ ]*\}\r?\n)?"
    r"(?P<ret>[ ]*return objectResponse_\.Object;\r?\n[ ]*\}\r?\n)")

CANCELLATION_PARAMETER = "System.Threading.CancellationToken cancellationToken"
ENUMERATOR_CANCELLATION = "[System.Runtime.CompilerServices.EnumeratorCancellation] "


def _hint_argument(indent: str, sentences: list[str]) -> str:
    """Renders a C# string-literal argument split across lines, `+`-joined."""
    return ("\n" + f"{indent}    ").join(
        f'"{sentence}"' + (" +" if index < len(sentences) - 1 else "")
        for index, sentence in enumerate(sentences))


def _ensure_content_type_call(indent: str, expect_sse: str, sentences: list[str]) -> str:
    return (
        f"{indent}await EnsureContentTypeAsync(\n"
        f"{indent}    response_,\n"
        f"{indent}    {expect_sse},\n"
        f"{indent}    status_,\n"
        f"{indent}    headers_,\n"
        f"{indent}    {_hint_argument(indent, sentences)},\n"
        f"{indent}    cancellationToken).ConfigureAwait(false);\n")


def _streaming_200_branch(indent: str, json_method: str | None) -> str:
    if json_method is None:
        sentences = [
            "This endpoint always answers with Server-Sent Events, so a different content type "
            "means the server or an intermediary changed the response.",
        ]
    else:
        sentences = [
            "This endpoint chooses its response shape from the \\\"stream\\\" flag in the request "
            "body at run time, which no OpenAPI document can describe. ",
            f"Send \\\"stream\\\": true, or call {json_method} for the JSON shape.",
        ]

    return (
        f"{indent}if (status_ == 200)\n"
        f"{indent}{{\n"
        f"{indent}    // Phase 159: the STREAMING sibling. The body is Server-Sent Events, not\n"
        f"{indent}    // JSON. Framing and this guard are hand-written in\n"
        f"{indent}    // AgentPrismApiClient.Sse.cs so a regeneration cannot change them.\n"
        + _ensure_content_type_call(f"{indent}    ", "true", sentences)
        + "\n"
        f"{indent}    await foreach (var frame_ in ReadServerSentEventFramesConfigured(response_, cancellationToken))\n"
        f"{indent}    {{\n"
        f"{indent}        yield return frame_;\n"
        f"{indent}    }}\n"
        "\n"
        f"{indent}    yield break;\n"
        f"{indent}}}\n")


def _json_200_guard(indent: str, stream_method: str) -> str:
    sentences = [
        "This endpoint chooses its response shape from the \\\"stream\\\" flag in the request "
        "body at run time, which no OpenAPI document can describe. ",
        f"Send \\\"stream\\\": false, or call {stream_method} for the streaming shape.",
    ]

    return (
        f"{indent}// Phase 159: with \"stream\": true in the body this endpoint answers with\n"
        f"{indent}// Server-Sent Events, and the JSON read below would fail with an opaque\n"
        f"{indent}// \"could not deserialize\" message. Name the real cause instead.\n"
        + _ensure_content_type_call(indent, "false", sentences)
        + "\n")


def _streaming_doc(doc: str, method_name: str) -> str:
    """The sibling's XML doc: the operation's own text, with the streaming note."""
    note = (
        "        /// Streaming form: the 200 body is read as Server-Sent Events. Each element is\n"
        "        /// one raw frame; comment-only keep-alive blocks are skipped. Throws\n"
        "        /// <see cref=\"AgentPrismApiException\"/> if the server answers with a different\n"
        "        /// content type - see the AgentPrism docs, \"OpenAI-compatible API\".\n")

    if "        /// </summary>\n" not in doc:
        raise SystemExit(
            f"The generated XML doc for '{method_name}' has no </summary> line to append the "
            "streaming note to (NSwag's doc-comment template changed - inspect the generated file).")

    doc = doc.replace("        /// </summary>\n", note + "        /// </summary>\n", 1)

    return doc.replace(
        "        /// <returns>OK</returns>\n",
        "        /// <returns>The Server-Sent Events frames the server writes, one raw frame per element.</returns>\n",
        1)


def rewrite_streaming_siblings(source: str, operations: list[tuple[str, bool]]) -> tuple[int, int, str]:
    """Adds a `<operation>StreamAsync` sibling for every SSE-declaring operation.

    Also guards the JSON method of a DUAL operation against an SSE answer.
    Returns (siblings added, JSON methods guarded, source).
    """
    siblings = 0
    guarded = 0

    for operation_id, is_dual in sorted(operations):
        json_method = f"{operation_id}Async"
        stream_method = f"{operation_id}StreamAsync"

        # The script as a whole is not idempotent (see the module docstring),
        # and a second run here would append a DUPLICATE sibling - caught only
        # later, by the compiler, as CS0111. Fail where the cause is visible.
        if f"{stream_method}(" in source:
            raise SystemExit(
                f"'{stream_method}' already exists. This script is NOT idempotent: run it once on a "
                "FRESHLY generated client (dotnet nswag run nswag.json), never on its own output.")

        matches = list(operation_method_re(json_method).finditer(source))

        if len(matches) != 1:
            raise SystemExit(
                f"Expected exactly one generated '{json_method}' method, found {len(matches)} "
                "(the OpenAPI document or NSwag's template changed - inspect the generated file).")

        match = matches[0]
        params, body = match.group("params"), match.group("body")

        if CANCELLATION_PARAMETER not in params:
            raise SystemExit(
                f"'{json_method}' has no '{CANCELLATION_PARAMETER}' parameter to mark with "
                "[EnumeratorCancellation] (inspect the generated file).")

        stream_body, accept_count = ACCEPT_HEADER_RE.subn(
            'request_.Headers.Accept.Add(System.Net.Http.Headers.'
            f'MediaTypeWithQualityHeaderValue.Parse("{EVENT_STREAM_MEDIA_TYPE}"));',
            body)

        if accept_count != 1:
            raise SystemExit(
                f"Expected exactly one Accept header in '{json_method}', found {accept_count} "
                "(inspect the generated file).")

        branch = JSON_200_BRANCH_RE.search(stream_body)

        if branch is None:
            raise SystemExit(
                f"'{json_method}' has no recognizable 200 branch to turn into a stream. This pass "
                "must run BEFORE the pure-SSE pass, while all seven branches still share "
                "NJsonSchema's uniform shape (see the module docstring's ORDERING note).")

        stream_body = (
            stream_body[:branch.start()]
            + _streaming_200_branch(branch.group("indent"), json_method if is_dual else None)
            + stream_body[branch.end():])

        # The leading blank line separates the sibling from the method above it;
        # the blank line NSwag already emits after that method's closing brace
        # follows the sibling instead, so spacing stays exactly as generated.
        sibling = (
            "\n"
            + _streaming_doc(match.group("doc"), json_method)
            + "        public virtual async System.Collections.Generic.IAsyncEnumerable<string> "
            + stream_method
            + "(" + params.replace(
                CANCELLATION_PARAMETER, ENUMERATOR_CANCELLATION + CANCELLATION_PARAMETER, 1) + ")\n"
            + stream_body)

        if is_dual:
            guard = JSON_200_BRANCH_RE.search(body)

            if guard is None:
                raise SystemExit(
                    f"'{json_method}' is a dual JSON/SSE operation with no recognizable 200 branch "
                    "to guard (inspect the generated file).")

            body = (
                body[:guard.start()]
                + guard.group("open")
                + _json_200_guard(guard.group("indent") + "    ", stream_method)
                + guard.group("read") + (guard.group("nullcheck") or "") + guard.group("ret")
                + body[guard.end():])

            guarded += 1

        source = source[:match.start("body")] + body + sibling + source[match.end("body"):]
        siblings += 1

    return siblings, guarded, source


def main() -> int:
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <generated-client.cs> <openapi-document.json>", file=sys.stderr)
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

    # BEFORE the pure-SSE pass on purpose - see the module docstring's ORDERING
    # note: all seven 200 branches still share one shape at this point.
    operations = read_event_stream_operations(sys.argv[2])
    sibling_count, guarded_count, source = rewrite_streaming_siblings(source, operations)

    source, sse_string_count = rewrite_sse_string_responses(source)

    pure_sse_count = len(operations) - guarded_count

    if sse_string_count != pure_sse_count:
        raise SystemExit(
            f"The pure-SSE pass rewrote {sse_string_count} response(s) but the document declares "
            f"{pure_sse_count} pure-SSE operation(s). The streaming siblings must not be visible "
            "to that pass, and every pure-SSE method must still be fixed by it "
            "(inspect the generated file).")

    source, null_collection_count = rewrite_null_collection_defaults(source)

    with open(path, "w", encoding="utf-8") as f:
        f.write(source)

    print(
        f"Rewrote {property_count} JsonStringEnumConverter property attribute(s), added "
        f"{enum_count} type-level JsonConverter attribute(s) to enum declarations, qualified "
        f"{any_type_reference_count} colliding any-type reference(s) "
        f"({', '.join(COLLIDING_ANY_TYPES)}), added {sibling_count} streaming sibling method(s) "
        f"and guarded {guarded_count} dual JSON/SSE method(s) against the other shape, fixed "
        f"{sse_string_count} pure-SSE 200 response(s) to read plain text instead of JSON, and gave "
        f"{null_collection_count} non-nullable collection property(ies) an empty "
        f"default instead of null in {path}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
