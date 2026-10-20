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


if __name__ == "__main__":
    unittest.main()
