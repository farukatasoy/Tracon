#!/usr/bin/env python3
"""`nswag-postprocess-client.py` testleri — stdlib `unittest`, yeni bağımlılık yok.

Dosya adı alt çizgi taşır (`nswag-postprocess-client_test.py` DEĞİL): bkz.
`dokuman_bakim_test.py`'nin üst yorumu — `unittest discover` tire taşıyan
dosyaları sessizce atlar. Kaynak dosya yine de tire taşır (CLI script
konvansiyonu); bu yüzden aşağıda `importlib` ile yüklenir.

Koşum: python3 -m unittest discover -s scripts -p "*_test.py"
"""
from __future__ import annotations

import contextlib
import importlib.util
import json
import pathlib
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "nswag_postprocess_client", ROOT / "scripts" / "nswag-postprocess-client.py")
nswag_postprocess_client = importlib.util.module_from_spec(spec)
spec.loader.exec_module(nswag_postprocess_client)


# A minimal slice of what NJsonSchema actually emits for the "JsonElement"
# schema component (2026-08-26, measured): a bogus wrapper class that SHADOWS
# System.Text.Json.JsonElement in the same namespace, plus a property and a
# raw-root response method that both reference it unqualified.
SAMPLE_JSON_ELEMENT_SOURCE = """\
namespace Tracon.Client.Generated
{
    public sealed partial class EvalCaseResult
    {
        public JsonElement Scores { get; set; } = default!;

    }

    public virtual async System.Threading.Tasks.Task<JsonElement> TraconOpenAIResponsesAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        var objectResponse_ = await ReadObjectResponseAsync<JsonElement>(response_, headers_, cancellationToken).ConfigureAwait(false);
        if (objectResponse_.Object == null)
        {
            throw new TraconApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
        }
        return objectResponse_.Object;
    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.2.0.0 (NJsonSchema v11.1.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public partial class JsonElement
    {

        private System.Collections.Generic.IDictionary<string, object>? _additionalProperties;

        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

    }

}
"""

# The same shape for "ChatRole" - a value-shaped schema whose real type
# (Microsoft.Extensions.AI.ChatRole) Tracon.Client deliberately does not
# reference, so it maps to "string" (a REFERENCE type) instead.
SAMPLE_CHAT_ROLE_SOURCE = """\
namespace Tracon.Client.Generated
{
    public sealed partial class ChatMessage
    {
        public ChatRole Role { get; set; } = default!;

    }

    [System.CodeDom.Compiler.GeneratedCode("NJsonSchema", "14.2.0.0 (NJsonSchema v11.1.0.0 (Newtonsoft.Json v13.0.0.0))")]
    public partial class ChatRole
    {

        private System.Collections.Generic.IDictionary<string, object>? _additionalProperties;

        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.IDictionary<string, object> AdditionalProperties
        {
            get { return _additionalProperties ?? (_additionalProperties = new System.Collections.Generic.Dictionary<string, object>()); }
            set { _additionalProperties = value; }
        }

    }

}
"""


# A minimal slice of what NJsonSchema emits for a pure `text/event-stream`
# 200 response (2026-09-05, Faz 145): a bare `string` root response, read
# through the SAME JSON-deserializing helper every other operation uses.
SAMPLE_SSE_STRING_RESPONSE_SOURCE = """\
        var status_ = (int)response_.StatusCode;
        if (status_ == 200)
        {
            var objectResponse_ = await ReadObjectResponseAsync<string>(response_, headers_, cancellationToken).ConfigureAwait(false);
            if (objectResponse_.Object == null)
            {
                throw new TraconApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
            }
            return objectResponse_.Object;
        }
        else
        if (status_ == 404)
        {
            var objectResponse_ = await ReadObjectResponseAsync<ProblemDetails>(response_, headers_, cancellationToken).ConfigureAwait(false);
"""



# What NJsonSchema emits for a request DTO's collection properties (measured
# 2026-09-06): every property gets `= default!`, whether or not the declared
# type is nullable. Both shapes appear here on purpose - the pass must rewrite
# only the non-nullable ones.
SAMPLE_NULL_COLLECTION_SOURCE = """\
namespace Tracon.Client.Generated
{
    public partial class AgentRunRequest
    {
        [System.Text.Json.Serialization.JsonPropertyName("message")]
        public string? Message { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("approvals")]
        public System.Collections.Generic.ICollection<ToolApprovalDecision> Approvals { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("attachmentIds")]
        public System.Collections.Generic.ICollection<System.Guid> AttachmentIds { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("headers")]
        public System.Collections.Generic.IDictionary<string, string> Headers { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("parameters")]
        public System.Collections.Generic.IDictionary<string, string>? Parameters { get; set; } = default!;

        [System.Text.Json.Serialization.JsonPropertyName("annotations")]
        public System.Collections.Generic.ICollection<AIAnnotation>? Annotations { get; set; } = default!;
    }
}
"""

class NswagPostprocessClientTestleri(unittest.TestCase):
    def test_json_element_wrapper_class_is_deleted(self):
        source, _ = nswag_postprocess_client.rewrite_colliding_any_types(SAMPLE_JSON_ELEMENT_SOURCE)

        self.assertNotIn("public partial class JsonElement", source)
        self.assertNotIn("JsonExtensionData", source)

    def test_json_element_references_are_qualified_to_the_real_bcl_type(self):
        source, count = nswag_postprocess_client.rewrite_colliding_any_types(SAMPLE_JSON_ELEMENT_SOURCE)

        self.assertEqual(count, 3)  # property + method return type + generic argument
        self.assertIn("public System.Text.Json.JsonElement Scores", source)
        self.assertIn(
            "System.Threading.Tasks.Task<System.Text.Json.JsonElement> TraconOpenAIResponsesAsync", source)
        self.assertIn("ReadObjectResponseAsync<System.Text.Json.JsonElement>", source)
        self.assertNotIn("System.Text.Json.System.Text.Json.JsonElement", source)  # no double-qualification

    def test_value_type_root_response_null_check_is_dropped(self):
        # A struct like System.Text.Json.JsonElement fails CS0019 ("Operator
        # '==' cannot be applied ... and '<null>'") against the null-check
        # NJsonSchema generates for every OTHER (reference-typed) root
        # response - this is the check that made the bug UNCOMPILABLE, not
        # just wrong at runtime, once JsonElement was correctly qualified.
        source, _ = nswag_postprocess_client.rewrite_colliding_any_types(SAMPLE_JSON_ELEMENT_SOURCE)

        self.assertNotIn("objectResponse_.Object == null", source)
        self.assertIn("return objectResponse_.Object;", source)

    def test_chat_role_wrapper_class_is_deleted_and_mapped_to_string(self):
        # ChatRole's real type (Microsoft.Extensions.AI.ChatRole) is
        # deliberately NOT referenced by Tracon.Client - "string" is both
        # dependency-free and the type's actual wire shape.
        source, count = nswag_postprocess_client.rewrite_colliding_any_types(SAMPLE_CHAT_ROLE_SOURCE)

        self.assertEqual(count, 1)
        self.assertNotIn("public partial class ChatRole", source)
        self.assertIn("public string Role", source)
        self.assertNotIn("Microsoft.Extensions.AI", source)

    def test_a_second_wrapper_class_for_the_same_name_is_rejected(self):
        duplicated = SAMPLE_JSON_ELEMENT_SOURCE + SAMPLE_JSON_ELEMENT_SOURCE

        with self.assertRaises(SystemExit):
            nswag_postprocess_client.rewrite_colliding_any_types(duplicated)

    def test_sse_string_response_reads_plain_text_instead_of_json(self):
        source, count = nswag_postprocess_client.rewrite_sse_string_responses(
            SAMPLE_SSE_STRING_RESPONSE_SOURCE)

        self.assertEqual(count, 1)
        self.assertIn(
            "return await response_.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);",
            source)
        # The comment explaining the rewrite is allowed to NAME the helper
        # it replaces; only the actual call site must be gone.
        self.assertNotIn("ReadObjectResponseAsync<string>(", source)
        # The untouched 404 branch (a real JSON ProblemDetails body) must survive.
        self.assertIn("ReadObjectResponseAsync<ProblemDetails>", source)

    def test_sse_string_response_rewrite_is_idempotent_when_nothing_matches(self):
        source, count = nswag_postprocess_client.rewrite_sse_string_responses(
            SAMPLE_JSON_ELEMENT_SOURCE)

        self.assertEqual(count, 0)
        self.assertEqual(source, SAMPLE_JSON_ELEMENT_SOURCE)


    def test_non_nullable_collections_get_an_empty_default(self):
        source, count = nswag_postprocess_client.rewrite_null_collection_defaults(
            SAMPLE_NULL_COLLECTION_SOURCE)

        # Three non-nullable collections: two ICollection, one IDictionary.
        self.assertEqual(count, 3)
        self.assertIn(
            "public System.Collections.Generic.ICollection<ToolApprovalDecision> Approvals "
            "{ get; set; } = new System.Collections.Generic.List<ToolApprovalDecision>();",
            source)
        self.assertIn(
            "public System.Collections.Generic.ICollection<System.Guid> AttachmentIds "
            "{ get; set; } = new System.Collections.Generic.List<System.Guid>();",
            source)
        # A dictionary is initialized with Dictionary, not List.
        self.assertIn(
            "public System.Collections.Generic.IDictionary<string, string> Headers "
            "{ get; set; } = new System.Collections.Generic.Dictionary<string, string>();",
            source)

    def test_a_nullable_collection_keeps_its_null(self):
        source, _ = nswag_postprocess_client.rewrite_null_collection_defaults(
            SAMPLE_NULL_COLLECTION_SOURCE)

        # The `?` annotation is the contract that says "not provided" differs
        # from "provided empty"; erasing it would erase a real distinction.
        self.assertIn(
            "public System.Collections.Generic.IDictionary<string, string>? Parameters "
            "{ get; set; } = default!;",
            source)
        self.assertIn(
            "public System.Collections.Generic.ICollection<AIAnnotation>? Annotations "
            "{ get; set; } = default!;",
            source)

    def test_a_non_collection_property_is_left_alone(self):
        source, _ = nswag_postprocess_client.rewrite_null_collection_defaults(
            SAMPLE_NULL_COLLECTION_SOURCE)

        self.assertIn("public string? Message { get; set; } = default!;", source)

    def test_the_rewrite_is_idempotent(self):
        once, first = nswag_postprocess_client.rewrite_null_collection_defaults(
            SAMPLE_NULL_COLLECTION_SOURCE)
        twice, second = nswag_postprocess_client.rewrite_null_collection_defaults(once)

        self.assertEqual(first, 3)
        self.assertEqual(second, 0)
        self.assertEqual(once, twice)

    def test_a_nested_generic_collection_is_refused_rather_than_rewritten_wrongly(self):
        # `[^<>]*` cannot match a nested generic on purpose: there is no single
        # concrete type this pass could pick for one, so it must fail loudly by
        # not matching (the regeneration then leaves a visible `default!`).
        nested = (
            "        public System.Collections.Generic.ICollection"
            "<System.Collections.Generic.List<string>> Rows { get; set; } = default!;\n")

        source, count = nswag_postprocess_client.rewrite_null_collection_defaults(nested)

        self.assertEqual(count, 0)
        self.assertEqual(source, nested)


# A whole generated operation method, in the shape the sixth pass matches
# (Faz 159): NSwag's XML doc block, the signature, the uniform 200 branch and
# the invariant `finally` tail that ends every operation method.
def sample_operation(name: str = "TraconOpenAIResponses", body_type: str = "System.Text.Json.JsonElement",
                     return_type: str = "System.Text.Json.JsonElement", null_check: bool = False) -> str:
    check = ""
    if null_check:
        check = (
            "                            if (objectResponse_.Object == null)\n"
            "                            {\n"
            '                                throw new TraconApiException("Response was null which was not expected.", '
            "status_, objectResponse_.Text, headers_, null);\n"
            "                            }\n")

    return f"""\
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <summary>
        /// Run endpoint compatible with the OpenAI Responses API.
        /// </summary>
        /// <returns>OK</returns>
        public virtual async System.Threading.Tasks.Task<{return_type}> {name}Async({body_type} body, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {{
            var client_ = _httpClient;
            var disposeClient_ = false;
            try
            {{
                using (var request_ = new System.Net.Http.HttpRequestMessage())
                {{
                    request_.Headers.Accept.Add(System.Net.Http.Headers.MediaTypeWithQualityHeaderValue.Parse("application/json"));

                    var response_ = await client_.SendAsync(request_, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                    var disposeResponse_ = true;
                    try
                    {{
                        var status_ = (int)response_.StatusCode;
                        if (status_ == 200)
                        {{
                            var objectResponse_ = await ReadObjectResponseAsync<{return_type}>(response_, headers_, cancellationToken).ConfigureAwait(false);
{check}                            return objectResponse_.Object;
                        }}
                        else
                        {{
                            throw new TraconApiException("Unexpected.", status_, null, headers_, null);
                        }}
                    }}
                    finally
                    {{
                        if (disposeResponse_)
                            response_.Dispose();
                    }}
                }}
            }}
            finally
            {{
                if (disposeClient_)
                    client_.Dispose();
            }}
        }}

"""


@contextlib.contextmanager
def document(content: dict):
    """A throwaway OpenAPI document with one operation, deleted on exit."""
    with tempfile.TemporaryDirectory() as directory:
        path = pathlib.Path(directory) / "tracon.json"
        path.write_text(json.dumps({"paths": {"/v1/responses": {"post": {
            "operationId": "TraconOpenAIResponses",
            "responses": {"200": {"content": content}},
        }}}}), encoding="utf-8")

        yield str(path)


class StreamingSiblingTests(unittest.TestCase):
    """The sixth pass (Faz 159): a `<operation>StreamAsync` per SSE operation."""

    def test_the_document_reports_which_operations_stream_and_which_are_dual(self):
        with document({"application/json": {}, "text/event-stream": {}}) as path:
            dual = nswag_postprocess_client.read_event_stream_operations(path)
        with document({"text/event-stream": {}}) as path:
            pure = nswag_postprocess_client.read_event_stream_operations(path)
        with document({"application/json": {}}) as path:
            json_only = nswag_postprocess_client.read_event_stream_operations(path)

        self.assertEqual(dual, [("TraconOpenAIResponses", True)])
        self.assertEqual(pure, [("TraconOpenAIResponses", False)])

        # A JSON-only operation gets no sibling at all - the pass must not
        # widen to every operation in the document.
        self.assertEqual(json_only, [])

    def test_a_sibling_streams_frames_and_asks_for_the_event_stream(self):
        added, guarded, source = nswag_postprocess_client.rewrite_streaming_siblings(
            sample_operation(), [("TraconOpenAIResponses", True)])

        self.assertEqual(added, 1)
        self.assertEqual(guarded, 1)
        self.assertIn(
            "public virtual async System.Collections.Generic.IAsyncEnumerable<string> "
            "TraconOpenAIResponsesStreamAsync(System.Text.Json.JsonElement body, "
            "[System.Runtime.CompilerServices.EnumeratorCancellation] System.Threading.CancellationToken",
            source)
        self.assertIn('Parse("text/event-stream")', source)
        self.assertIn("yield return frame_;", source)
        self.assertIn("yield break;", source)

        # The ORIGINAL keeps its own shape: same return type, same Accept.
        self.assertIn(
            "public virtual async System.Threading.Tasks.Task<System.Text.Json.JsonElement> "
            "TraconOpenAIResponsesAsync(",
            source)
        self.assertIn('Parse("application/json")', source)

    def test_both_methods_of_a_dual_operation_name_the_other_one(self):
        _, _, source = nswag_postprocess_client.rewrite_streaming_siblings(
            sample_operation(), [("TraconOpenAIResponses", True)])

        self.assertIn("call TraconOpenAIResponsesStreamAsync for the streaming shape.", source)
        self.assertIn("call TraconOpenAIResponsesAsync for the JSON shape.", source)
        self.assertEqual(source.count("await EnsureContentTypeAsync("), 2)

    def test_a_pure_SSE_operation_gets_a_sibling_but_no_guard_on_the_original(self):
        added, guarded, source = nswag_postprocess_client.rewrite_streaming_siblings(
            sample_operation(name="TraconRunAgent", body_type="AgentRunRequest",
                             return_type="string", null_check=True),
            [("TraconRunAgent", False)])

        self.assertEqual(added, 1)

        # There is no JSON sibling to point at, so the original is untouched
        # and the message says the mismatch is the server's doing.
        self.assertEqual(guarded, 0)
        self.assertEqual(source.count("await EnsureContentTypeAsync("), 1)
        self.assertIn("always answers with Server-Sent Events", source)

    def test_the_pure_SSE_pass_no_longer_sees_a_rewritten_sibling(self):
        # The ORDERING invariant `main` asserts: the sixth pass runs first, and
        # its siblings' 200 branches must not look like anything the fourth
        # pass matches - otherwise it would rewrite them back into a buffered
        # read and silently undo this phase.
        _, _, source = nswag_postprocess_client.rewrite_streaming_siblings(
            sample_operation(name="TraconRunAgent", body_type="AgentRunRequest",
                             return_type="string", null_check=True),
            [("TraconRunAgent", False)])

        _, count = nswag_postprocess_client.rewrite_sse_string_responses(source)

        self.assertEqual(count, 1)

    def test_a_second_run_is_refused_rather_than_duplicating_the_sibling(self):
        _, _, once = nswag_postprocess_client.rewrite_streaming_siblings(
            sample_operation(), [("TraconOpenAIResponses", True)])

        with self.assertRaises(SystemExit):
            nswag_postprocess_client.rewrite_streaming_siblings(
                once, [("TraconOpenAIResponses", True)])

    def test_a_missing_operation_method_is_refused(self):
        with self.assertRaises(SystemExit):
            nswag_postprocess_client.rewrite_streaming_siblings(
                sample_operation(), [("TraconNoSuchOperation", True)])

    def test_an_unrecognizable_200_branch_is_refused(self):
        # If NJsonSchema's template changes, the pass must fail loudly rather
        # than emit a sibling whose 200 branch still deserializes JSON.
        mangled = sample_operation().replace("if (status_ == 200)", "if (status_ == 201)")

        with self.assertRaises(SystemExit):
            nswag_postprocess_client.rewrite_streaming_siblings(
                mangled, [("TraconOpenAIResponses", True)])


class ValueTypeBodyGuardTests(unittest.TestCase):
    """The third pass's body-argument axis (Faz 159)."""

    SOURCE = """\
        public virtual async System.Threading.Tasks.Task<JsonElement> TraconOpenAIResponsesAsync(JsonElement body, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            if (body == null)
                throw new System.ArgumentNullException("body");

            var client_ = _httpClient;
        }
"""

    def test_a_struct_body_gets_a_guard_that_names_the_mistake(self):
        source, _ = nswag_postprocess_client.rewrite_colliding_any_types(self.SOURCE)

        # `body == null` on a struct is CS0019; dropping it outright would let
        # a default(JsonElement) fail deep inside the serializer with
        # "Operation is not valid due to the current state of the object".
        self.assertNotIn("if (body == null)", source)
        self.assertIn("if (body.ValueKind == System.Text.Json.JsonValueKind.Undefined)", source)
        self.assertIn("uninitialized JsonElement", source)
        self.assertIn('"body");', source)

    def test_a_reference_type_body_keeps_its_null_check(self):
        source = """\
        public virtual async System.Threading.Tasks.Task<string> TraconRunAgentAsync(AgentRunRequest body, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            if (body == null)
                throw new System.ArgumentNullException("body");
        }
"""
        rewritten, _ = nswag_postprocess_client.rewrite_colliding_any_types(source)

        self.assertEqual(rewritten, source)


if __name__ == "__main__":
    unittest.main()
