"""Tests for scripts/nswag-prepare-document.py (the transforms of the throwaway NSwag copy)."""
import importlib.util
import pathlib
import unittest

_SPEC = importlib.util.spec_from_file_location(
    "nswag_prepare_document", pathlib.Path(__file__).with_name("nswag-prepare-document.py")
)
prepare = importlib.util.module_from_spec(_SPEC)
_SPEC.loader.exec_module(prepare)


class StripNullFromEnumsTests(unittest.TestCase):
    def test_removes_null_from_an_enum_component_and_keeps_the_values_in_order(self):
        document = {"components": {"schemas": {"Kind": {"enum": ["A", "B", None]}}}}

        stripped = prepare.strip_null_from_enums(document)

        self.assertEqual(1, stripped)
        self.assertEqual(["A", "B"], document["components"]["schemas"]["Kind"]["enum"])

    def test_leaves_an_enum_without_null_and_a_non_enum_alone(self):
        document = {
            "components": {
                "schemas": {
                    "Plain": {"enum": ["A", "B"]},
                    "Object": {"type": "object", "properties": {"x": {"type": "string"}}},
                }
            }
        }

        self.assertEqual(0, prepare.strip_null_from_enums(document))
        self.assertEqual(["A", "B"], document["components"]["schemas"]["Plain"]["enum"])
        self.assertNotIn("enum", document["components"]["schemas"]["Object"])

    def test_keeps_the_null_branch_of_a_property_that_references_the_enum(self):
        # The nullability of the PROPERTY stays in the oneOf null branch; only the shared component loses its null.
        property_schema = {"oneOf": [{"type": "null"}, {"$ref": "#/components/schemas/Kind"}]}
        document = {"components": {"schemas": {"Kind": {"enum": ["A", None]}, "Holder": {"properties": {"kind": property_schema}}}}}

        prepare.strip_null_from_enums(document)

        self.assertEqual({"type": "null"}, document["components"]["schemas"]["Holder"]["properties"]["kind"]["oneOf"][0])

    def test_a_document_without_schemas_is_not_an_error(self):
        self.assertEqual(0, prepare.strip_null_from_enums({}))


class AnyOfNullToOneOfTests(unittest.TestCase):
    """NSwag reads `oneOf [null, X]` as a nullable reference and `anyOf [null, X]` as a new anonymous class."""

    def test_rewrites_a_nullable_wrapper_for_nswag_and_keeps_the_branches_in_order(self):
        property_schema = {"anyOf": [{"type": "null"}, {"$ref": "#/components/schemas/JsonElement"}]}
        document = {"components": {"schemas": {"Holder": {"properties": {"payload": property_schema}}}}}

        rewritten = prepare.anyof_null_to_oneof(document)

        self.assertEqual(1, rewritten)
        converted = document["components"]["schemas"]["Holder"]["properties"]["payload"]
        self.assertNotIn("anyOf", converted)
        self.assertEqual([{"type": "null"}, {"$ref": "#/components/schemas/JsonElement"}], converted["oneOf"])

    def test_rewrites_a_wrapper_inside_a_response_schema_of_an_operation_too(self):
        wrapper = {"anyOf": [{"type": "null"}, {"type": "string"}]}
        document = {"paths": {"/x": {"get": {"responses": {"200": {"content": {"application/json": {"schema": wrapper}}}}}}}}

        self.assertEqual(1, prepare.anyof_null_to_oneof(document))
        self.assertIn("oneOf", document["paths"]["/x"]["get"]["responses"]["200"]["content"]["application/json"]["schema"])

    def test_leaves_an_anyof_without_a_null_branch_alone(self):
        schema = {"anyOf": [{"type": "string"}, {"type": "integer"}]}

        self.assertEqual(0, prepare.anyof_null_to_oneof({"x": schema}))
        self.assertIn("anyOf", schema)

    def test_leaves_an_existing_oneof_alone(self):
        schema = {"oneOf": [{"type": "null"}, {"$ref": "#/components/schemas/Kind"}]}

        self.assertEqual(0, prepare.anyof_null_to_oneof({"x": schema}))
        self.assertEqual(2, len(schema["oneOf"]))

    def test_does_not_touch_an_example_that_looks_like_a_schema(self):
        # An example value is data: a nested anyOf there is not a schema keyword.
        schema = {"type": "object", "example": {"anyOf": [{"type": "null"}, 1]}}

        self.assertEqual(0, prepare.anyof_null_to_oneof(schema))
        self.assertIn("anyOf", schema["example"])

    def test_the_description_hoist_reads_a_wrapper_that_was_an_anyof(self):
        # Order matters: the rewrite runs first, so the existing hoist (which reads oneOf) keeps the XML documentation
        # of a nullable JsonElement member.
        wrapper = {"anyOf": [{"type": "null"}, {"description": "The payload.", "$ref": "#/components/schemas/JsonElement"}]}
        document = {"components": {"schemas": {"Holder": {"properties": {"payload": wrapper}}}}}

        prepare.anyof_null_to_oneof(document)
        hoisted = prepare.hoist_nullable_descriptions(document)

        self.assertEqual(1, hoisted)
        self.assertEqual("The payload.", document["components"]["schemas"]["Holder"]["properties"]["payload"]["description"])


if __name__ == "__main__":
    unittest.main()
