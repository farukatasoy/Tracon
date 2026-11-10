#!/usr/bin/env python3
"""Tests for `elevenlabs-drift.py`.

The script runs over the network against the live document and not in CI; its logic (`$ref`
resolution, union flattening, field path and type comparison) is tested here on a small
OpenAPI fragment.
"""
from __future__ import annotations

import contextlib
import copy
import importlib.util
import io
import pathlib
import re
import sys
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("elevenlabs_drift", ROOT / "scripts" / "elevenlabs-drift.py")
drift = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = drift
spec.loader.exec_module(drift)


DOCUMENT = {
    "openapi": "3.1.0",
    "paths": {
        "/v1/speak": {
            "parameters": [{"name": "output_format", "in": "query"}],
            "post": {
                "requestBody": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Body"}}}},
                "responses": {"200": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Reply"}}}}},
            },
        },
        "/v1/listen": {
            "post": {
                "parameters": [{"$ref": "#/components/parameters/Page"}],
                "requestBody": {"content": {"multipart/form-data": {"schema": {"properties": {"file": {"type": "string"}}}}}},
                "responses": {"200": {"content": {"application/json": {"schema": {"$ref": "#/components/schemas/Reply"}}}}},
            },
        },
    },
    "components": {
        "parameters": {"Page": {"name": "page_token", "in": "query"}},
        "schemas": {
            "Body": {
                "type": "object",
                "properties": {
                    "text": {"type": "string"},
                    "seed": {"anyOf": [{"type": "integer"}, {"type": "null"}]},
                    "ids": {"type": "array", "items": {"type": "string"}},
                    "settings": {"$ref": "#/components/schemas/Settings"},
                },
            },
            "Settings": {"type": "object", "properties": {"speed": {"type": "number"}, "boost": {"type": ["boolean", "null"]}}},
            "Reply": {
                "type": "object",
                "properties": {
                    "items": {"type": "array", "items": {"$ref": "#/components/schemas/Item"}},
                    "total": {"type": "integer"},
                    "mystery": {},
                },
            },
            "Item": {"properties": {"id": {"type": "string"}}},
        },
    },
}


def declaration(**overrides):
    """A declaration that matches the document; `overrides` replaces fields of the first operation."""
    speak = {
        "method": "POST",
        "path": "/v1/speak",
        "query": ["output_format"],
        "request": ["text", "seed", "ids[]", "settings", "settings.speed", "settings.boost"],
        "response": ["items[]", "items[].id", "total", "mystery"],
        "types": {
            "request": {
                "text": "string", "seed": "integer", "ids[]": "array",
                "settings": "object", "settings.speed": "number", "settings.boost": "boolean",
            },
            "response": {"items[]": "array", "items[].id": "string", "total": "integer", "mystery": "string"},
        },
    }
    speak.update(copy.deepcopy(overrides))
    return {"operations": [speak]}


class FieldPathTests(unittest.TestCase):
    def test_a_matching_declaration_has_no_problems(self):
        self.assertEqual(drift.check(DOCUMENT, declaration()), [])

    def test_a_missing_operation_is_reported(self):
        problems = drift.check(DOCUMENT, declaration(path="/v1/missing"))
        self.assertEqual(problems, ["POST /v1/missing: the operation is gone"])

    def test_a_missing_query_parameter_is_reported(self):
        problems = drift.check(DOCUMENT, declaration(query=["output_format", "gone"]))
        self.assertEqual(problems, ["POST /v1/speak: query parameter 'gone' is gone"])

    def test_query_parameters_declared_on_the_operation_or_through_a_ref_are_found(self):
        listen = {"method": "POST", "path": "/v1/listen", "query": ["page_token"], "request": ["file"], "response": ["total"]}
        self.assertEqual(drift.check(DOCUMENT, {"operations": [listen]}), [])

    def test_a_missing_field_is_reported_including_nested_ones(self):
        problems = drift.check(DOCUMENT, declaration(request=["text", "settings.gone", "items[].gone"], types={}))
        self.assertEqual(problems, [
            "POST /v1/speak: request field 'settings.gone' is gone",
            "POST /v1/speak: request field 'items[].gone' is gone",
        ])

    def test_an_array_item_field_is_found_through_unions_and_refs(self):
        alternative_list = drift.alternatives(DOCUMENT, DOCUMENT["components"]["schemas"]["Reply"]["properties"]["items"])
        self.assertTrue(any(option.get("type") == "array" for option in alternative_list))
        self.assertTrue(drift.has_path(DOCUMENT, DOCUMENT["components"]["schemas"]["Reply"], ["items[]", "id"]))
        self.assertFalse(drift.has_path(DOCUMENT, DOCUMENT["components"]["schemas"]["Reply"], ["items", "id"]))

    def test_a_side_without_a_body_is_reported_when_fields_are_expected(self):
        document = copy.deepcopy(DOCUMENT)
        del document["paths"]["/v1/speak"]["post"]["requestBody"]
        problems = drift.check(document, declaration(types={}))
        self.assertEqual(problems, ["POST /v1/speak: has no request body schema but Tracon relies on 6 field(s)"])

    def test_a_ref_cycle_does_not_loop_forever(self):
        document = {"a": {"$ref": "#/a"}}
        with self.assertRaises(ValueError):
            drift.resolve(document, document["a"])


class TypeTests(unittest.TestCase):
    def test_a_type_change_is_reported_even_when_the_name_is_unchanged(self):
        document = copy.deepcopy(DOCUMENT)
        document["components"]["schemas"]["Body"]["properties"]["text"] = {"type": "integer"}
        problems = drift.check(document, declaration())
        self.assertEqual(problems, ["POST /v1/speak: request field 'text' is integer but Tracon declares string"])

    def test_a_type_unioned_with_null_keeps_its_type(self):
        # `seed`: anyOf [integer, null] and `settings.boost`: ["boolean", "null"].
        self.assertEqual(drift.find_types(DOCUMENT, DOCUMENT["components"]["schemas"]["Body"], ["seed"]), {"integer"})
        self.assertEqual(
            drift.find_types(DOCUMENT, DOCUMENT["components"]["schemas"]["Body"], ["settings", "boost"]), {"boolean"})

    def test_an_integer_declaration_fits_a_number_field_but_not_the_reverse(self):
        document = copy.deepcopy(DOCUMENT)
        document["components"]["schemas"]["Body"]["properties"]["seed"] = {"type": "number"}
        self.assertEqual(drift.check(document, declaration()), [])

        document["components"]["schemas"]["Settings"]["properties"]["speed"] = {"type": "integer"}
        self.assertEqual(
            drift.check(document, declaration()),
            ["POST /v1/speak: request field 'settings.speed' is integer but Tracon declares number"])

    def test_array_and_object_types_are_told_apart(self):
        document = copy.deepcopy(DOCUMENT)
        document["components"]["schemas"]["Body"]["properties"]["ids"] = {"type": "string"}
        document["components"]["schemas"]["Body"]["properties"]["settings"] = {"type": "array", "items": {"type": "string"}}
        problems = drift.check(document, declaration(request=["ids[]", "settings"], types={"request": {"ids[]": "array", "settings": "object"}}))
        self.assertEqual(problems, [
            "POST /v1/speak: request field 'ids[]' is string but Tracon declares array",
            "POST /v1/speak: request field 'settings' is array but Tracon declares object",
        ])

    def test_a_field_the_document_gives_no_type_fits_any_declaration(self):
        # `mystery: {}`: the schema has no type. There is nothing to compare; the field existing is enough.
        self.assertEqual(drift.find_types(DOCUMENT, DOCUMENT["components"]["schemas"]["Reply"], ["mystery"]), set())
        self.assertTrue(drift.type_matches("string", set()))

    def test_a_field_without_a_declared_type_is_checked_for_existence_only(self):
        # Multipart form fields declare no type (on the wire they are all text).
        listen = {"method": "POST", "path": "/v1/listen", "query": [], "request": ["file"], "response": [], "types": {"request": {}, "response": {}}}
        self.assertEqual(drift.check(DOCUMENT, {"operations": [listen]}), [])

    def test_every_json_field_in_the_declaration_file_carries_a_type(self):
        # The real declaration: every field has a type except the form request (speech-to-text).
        import json
        declaration_file = json.loads(drift.FIXTURE.read_text(encoding="utf-8"))
        for operation in declaration_file["operations"]:
            label = f"{operation['method']} {operation['path']}"
            for side in ("request", "response"):
                types = operation["types"][side]
                if operation["path"] == "/v1/speech-to-text" and side == "request":
                    self.assertEqual(types, {}, f"{label}: a form declares no types")
                    continue
                self.assertEqual(sorted(types), sorted(operation[side]), f"{label}: {side}")
                self.assertTrue(set(types.values()) <= {"string", "integer", "number", "boolean", "array", "object"})


class CommandLineTests(unittest.TestCase):
    def _exit_code_and_text(self, *argv: str) -> tuple[object, str]:
        out, err = io.StringIO(), io.StringIO()

        with mock.patch.object(sys, "argv", ["elevenlabs-drift.py", *argv]):
            with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
                with self.assertRaises(SystemExit) as raised:
                    drift.main()

        return raised.exception.code, out.getvalue() + err.getvalue()

    def test_the_help_text_is_english_and_names_the_english_flag(self):
        code, text = self._exit_code_and_text("--help")

        self.assertEqual(code, 0)
        self.assertIsNone(re.search("[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]", text))  # Turkish letters
        self.assertIn("--file", text)

    def test_the_old_turkish_flag_is_refused(self):
        code, _ = self._exit_code_and_text("--dosya", "x.json")  # legacy-name

        self.assertEqual(code, 2)


if __name__ == "__main__":
    unittest.main()
