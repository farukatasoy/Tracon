#!/usr/bin/env python3
"""`nswag-postprocess-client.py` testleri — stdlib `unittest`, yeni bağımlılık yok.

Dosya adı alt çizgi taşır (`nswag-postprocess-client_test.py` DEĞİL): bkz.
`dokuman_bakim_test.py`'nin üst yorumu — `unittest discover` tire taşıyan
dosyaları sessizce atlar. Kaynak dosya yine de tire taşır (CLI script
konvansiyonu); bu yüzden aşağıda `importlib` ile yüklenir.

Koşum: python3 -m unittest discover -s scripts -p "*_test.py"
"""
from __future__ import annotations

import importlib.util
import pathlib
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
namespace AgentPrism.Client.Generated
{
    public sealed partial class EvalCaseResult
    {
        public JsonElement Scores { get; set; } = default!;

    }

    public virtual async System.Threading.Tasks.Task<JsonElement> AgentPrismOpenAIResponsesAsync(System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
    {
        var objectResponse_ = await ReadObjectResponseAsync<JsonElement>(response_, headers_, cancellationToken).ConfigureAwait(false);
        if (objectResponse_.Object == null)
        {
            throw new AgentPrismApiException("Response was null which was not expected.", status_, objectResponse_.Text, headers_, null);
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
# (Microsoft.Extensions.AI.ChatRole) AgentPrism.Client deliberately does not
# reference, so it maps to "string" (a REFERENCE type) instead.
SAMPLE_CHAT_ROLE_SOURCE = """\
namespace AgentPrism.Client.Generated
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
            "System.Threading.Tasks.Task<System.Text.Json.JsonElement> AgentPrismOpenAIResponsesAsync", source)
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
        # deliberately NOT referenced by AgentPrism.Client - "string" is both
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


if __name__ == "__main__":
    unittest.main()
