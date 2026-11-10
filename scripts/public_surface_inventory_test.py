#!/usr/bin/env python3
"""Tests for the public API surface inventory.

The inventory is a decision tool: a type counted wrongly is either frozen for nothing or
shown as an `internal` candidate in a way that would break a consumer. The tests pin every
kind of evidence, the signature closure and the class priority on small known example sets.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import pathlib
import re
import subprocess
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "public_surface_inventory", ROOT / "scripts" / "public-surface-inventory.py")
inventory = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = inventory
spec.loader.exec_module(inventory)


CORE_UNSHIPPED = """#nullable enable
Tracon.RunRecord
Tracon.RunRecord.Status.get -> Tracon.RunStatus
Tracon.RunRecord.Status.init -> void
Tracon.RunStatus
Tracon.RunStatus.Completed = 2 -> Tracon.RunStatus
Tracon.IRunStore
Tracon.IRunStore.GetAsync(string! id, System.Threading.CancellationToken cancellationToken) -> System.Threading.Tasks.ValueTask<Tracon.RunRecord?>
Tracon.RunStoreBase
Tracon.RunStoreBase.Probe() -> Tracon.ProbeResult!
Tracon.ProbeResult
Tracon.Cache<TKey>
Tracon.Cache<TKey>.Get(TKey key) -> Tracon.CacheEntry!
Tracon.CacheEntry
Tracon.Outer
Tracon.Outer.Inner
Tracon.Outer.Inner.Value.get -> int
Tracon.TraconBuilderExtensions
static Tracon.TraconBuilderExtensions.AddWidget(this Tracon.ITraconBuilder! builder, System.Action<Tracon.WidgetOptions!>? configure = null) -> Tracon.ITraconBuilder!
Tracon.WidgetOptions
Tracon.WidgetOptions.Mode.get -> Tracon.WidgetMode
Tracon.WidgetMode
Tracon.WidgetMode.Fast = 0 -> Tracon.WidgetMode
Tracon.StringExtensions
static Tracon.StringExtensions.Shorten(this string! value) -> string!
Tracon.SampleToolAttribute
Tracon.Helper
static Tracon.Helper.Compute() -> int
Tracon.HelperResult
Tracon.AgentDescriptor
Tracon.Justified
Tracon.DerivedHandler
Tracon.HandlerBase
Tracon.IThrowingStore
Tracon.StoreFailedException
Tracon.StoreFailedException.StoreFailedException(Tracon.StoreFailure reason) -> void
Tracon.StoreFailure
Tracon.StoreFailure.Gone = 0 -> Tracon.StoreFailure
Tracon.UndocumentedException
"""

CORE_SOURCE = """
namespace Tracon;

public sealed record RunRecord { }
public enum RunStatus { Completed = 2 }
public interface IRunStore { }
public abstract class RunStoreBase { }
public sealed class ProbeResult { }
public sealed class Cache<TKey> where TKey : notnull { }
public sealed class CacheEntry { }
public static class Outer { public sealed class Inner { } }
public static class TraconBuilderExtensions { }
public sealed class WidgetOptions { }
public enum WidgetMode { Fast }
public static class StringExtensions { }
public sealed class SampleToolAttribute : System.Attribute { }
public static class Helper { }
public readonly record struct HelperResult(int Value);
public sealed record AgentDescriptor(string Name);
public sealed class Justified { }
public sealed class DerivedHandler : HandlerBase, IRunStore { }
public class HandlerBase { }
internal sealed class RunRecord2 { }
"""

THROWING_STORE_SOURCE = """
namespace Tracon;

/// <summary>A seam whose contract names the exception it throws.</summary>
public interface IThrowingStore
{
    /// <exception cref="StoreFailedException">The record is gone.</exception>
    void Load();
}
"""

EXCEPTION_SOURCE = """
namespace Tracon;

public sealed class StoreFailedException : System.Exception { }
public enum StoreFailure { Gone }
public sealed class UndocumentedException : System.Exception { }
"""


def _build_inventory(corpus_entries, reasons=None, extra_packages=None):
    types = {}
    for public_type in inventory.parse_unshipped("Tracon.Core", CORE_UNSHIPPED).values():
        types[public_type.key] = public_type
    sources = {"Tracon.Core": [
        ("src/Tracon.Core/Types.cs", CORE_SOURCE),
        ("src/Tracon.Core/IThrowingStore.cs", THROWING_STORE_SOURCE),
        ("src/Tracon.Core/Exceptions.cs", EXCEPTION_SOURCE),
    ]}
    for package, (unshipped, source) in (extra_packages or {}).items():
        for public_type in inventory.parse_unshipped(package, unshipped).values():
            types[public_type.key] = public_type
        sources[package] = [(f"src/{package}/Types.cs", source)]
    inventory.scan_declarations(types, sources)
    inventory.classify(types, inventory.Corpus.build(corpus_entries), reasons or {})
    return types


def _public_type(types, name, package="Tracon.Core"):
    return types[(package, name)]


class ParseUnshippedTests(unittest.TestCase):
    def test_type_lines_and_member_lines_are_told_apart(self):
        types = inventory.parse_unshipped("Tracon.Core", CORE_UNSHIPPED)
        self.assertIn("Tracon.RunRecord", types)
        self.assertNotIn("Tracon.RunRecord.Status", types)
        self.assertEqual(len(types["Tracon.RunRecord"].members), 2)

    def test_an_enum_member_is_not_counted_as_a_type(self):
        types = inventory.parse_unshipped("Tracon.Core", CORE_UNSHIPPED)
        self.assertNotIn("Tracon.RunStatus.Completed", types)
        self.assertEqual(types["Tracon.RunStatus"].members,
                         ["Tracon.RunStatus.Completed = 2 -> Tracon.RunStatus"])

    def test_a_generic_type_drops_its_parameter_list_and_keeps_its_members(self):
        types = inventory.parse_unshipped("Tracon.Core", CORE_UNSHIPPED)
        self.assertIn("Tracon.Cache", types)
        self.assertEqual(len(types["Tracon.Cache"].members), 1)

    def test_a_member_line_with_modifiers_attaches_to_its_owner(self):
        types = inventory.parse_unshipped("Tracon.Core", CORE_UNSHIPPED)
        self.assertEqual(len(types["Tracon.TraconBuilderExtensions"].members), 1)

    def test_a_nested_type_member_attaches_to_the_nested_type_not_the_container(self):
        types = inventory.parse_unshipped("Tracon.Core", CORE_UNSHIPPED)
        self.assertEqual(len(types["Tracon.Outer.Inner"].members), 1)
        self.assertEqual(types["Tracon.Outer"].members, [])


class ScanDeclarationsTests(unittest.TestCase):
    def setUp(self):
        self.types = _build_inventory([])

    def test_the_kind_is_read_from_the_declaration(self):
        expected = {
            "Tracon.RunRecord": "record",
            "Tracon.RunStatus": "enum",
            "Tracon.IRunStore": "interface",
            "Tracon.RunStoreBase": "abstract",
            "Tracon.Outer": "static",
            "Tracon.HelperResult": "record",
            "Tracon.ProbeResult": "class",
        }
        for name, kind in expected.items():
            self.assertEqual(_public_type(self.types, name).kind, kind, name)

    def test_the_base_list_enters_the_closure(self):
        self.assertEqual(_public_type(self.types, "Tracon.DerivedHandler").header_names,
                         {"Tracon.HandlerBase", "Tracon.IRunStore"})


class ClassificationTests(unittest.TestCase):
    def test_unproven_is_the_default(self):
        types = _build_inventory([])
        self.assertEqual(_public_type(types, "Tracon.Helper").evidence_class, "unproven")

    def test_a_name_on_a_site_page_is_consumer_evidence(self):
        types = _build_inventory([("site", "docs-site/src/content/docs/a.md", "Call `Helper.Compute()`.")])
        self.assertEqual(_public_type(types, "Tracon.Helper").evidence_class, "consumer")
        self.assertEqual(_public_type(types, "Tracon.Helper").reason, "site:docs-site/src/content/docs/a.md")

    def test_a_name_does_not_match_as_a_substring(self):
        types = _build_inventory([("site", "a.md", "HelperResultSet and MyHelper")])
        self.assertEqual(_public_type(types, "Tracon.Helper").evidence_class, "unproven")
        self.assertEqual(_public_type(types, "Tracon.HelperResult").evidence_class, "unproven")

    def test_a_registration_entry_point_is_consumer_without_a_corpus_and_keeps_its_options_by_closure(self):
        types = _build_inventory([])
        extension = _public_type(types, "Tracon.TraconBuilderExtensions")
        self.assertEqual((extension.evidence_class, extension.reason), ("consumer", "entry-point:AddWidget"))
        self.assertEqual(_public_type(types, "Tracon.WidgetOptions").reason, "signature:Tracon.TraconBuilderExtensions")
        self.assertEqual(_public_type(types, "Tracon.WidgetMode").reason, "signature:Tracon.WidgetOptions")

    def test_a_builder_extension_without_a_prefix_is_a_registration_entry_point(self):
        # `Configure` became a builder extension; a prefix rule would have left it unproven.
        unshipped = (
            "Tracon.ConfigureOnlyExtensions\n"
            "static Tracon.ConfigureOnlyExtensions.Configure(this Tracon.ITraconBuilder! builder, "
            "System.Action<Tracon.TraconOptions!>! configure) -> Tracon.ITraconBuilder!\n")
        source = "namespace Tracon;\npublic static class ConfigureOnlyExtensions { }\n"
        types = _build_inventory([], extra_packages={"Tracon.Extra": (unshipped, source)})
        public_type = _public_type(types, "Tracon.ConfigureOnlyExtensions", package="Tracon.Extra")
        self.assertEqual((public_type.evidence_class, public_type.reason), ("consumer", "entry-point:Configure"))

    def test_an_extension_class_finds_evidence_by_method_name(self):
        types = _build_inventory([("sample", "samples/A/Program.cs", 'var s = "x".Shorten();')])
        self.assertEqual(_public_type(types, "Tracon.StringExtensions").reason,
                         "extension:Shorten@sample:samples/A/Program.cs")

    def test_an_attribute_finds_evidence_by_its_short_name(self):
        types = _build_inventory([("sample", "samples/A/Tools.cs", "[SampleTool(\"x\")] public static int F() => 1;")])
        self.assertEqual(_public_type(types, "Tracon.SampleToolAttribute").evidence_class, "consumer")

    def test_an_attribute_short_name_in_plain_text_is_not_evidence(self):
        types = _build_inventory([("site", "a.md", "The SampleTool mode is fast.")])
        self.assertEqual(_public_type(types, "Tracon.SampleToolAttribute").evidence_class, "unproven")

    def test_an_openapi_schema_is_not_consumer_evidence(self):
        # An HTTP DTO is born internal; appearing in the shipped document does not keep it public.
        # The document does not enter the corpus (`.json` is not an extension) and the classifier takes no schema name.
        self.assertNotIn(".json", inventory.CORPUS_EXTENSIONS)
        types = _build_inventory([])
        self.assertEqual(_public_type(types, "Tracon.AgentDescriptor").evidence_class, "unproven")

    def test_an_interface_and_an_abstract_class_are_seams_and_keep_their_signatures(self):
        types = _build_inventory([])
        self.assertEqual(_public_type(types, "Tracon.IRunStore").reason, "seam:interface")
        self.assertEqual(_public_type(types, "Tracon.RunStoreBase").reason, "seam:abstract-class")
        self.assertEqual((_public_type(types, "Tracon.RunRecord").evidence_class, _public_type(types, "Tracon.RunRecord").reason),
                         ("seam", "signature:Tracon.IRunStore"))
        self.assertEqual(_public_type(types, "Tracon.RunStatus").reason, "signature:Tracon.RunRecord")
        self.assertEqual(_public_type(types, "Tracon.ProbeResult").reason, "signature:Tracon.RunStoreBase")

    def test_consumer_evidence_beats_seam(self):
        types = _build_inventory([("site", "a.md", "Read a `RunRecord` from the store.")])
        self.assertEqual(_public_type(types, "Tracon.RunRecord").evidence_class, "consumer")

    def test_the_closure_keeps_the_base_class_and_the_container(self):
        types = _build_inventory([("site", "a.md", "DerivedHandler and Inner")])
        self.assertEqual(_public_type(types, "Tracon.HandlerBase").reason, "signature:Tracon.DerivedHandler")
        self.assertEqual(_public_type(types, "Tracon.Outer").reason, "signature:Tracon.Outer.Inner")

    def test_a_generic_type_member_signature_enters_the_closure(self):
        types = _build_inventory([("site", "a.md", "Cache")])
        self.assertEqual(_public_type(types, "Tracon.CacheEntry").reason, "signature:Tracon.Cache")

    def test_a_documented_exception_and_its_signature_enter_the_closure(self):
        types = _build_inventory([])
        exception_type = _public_type(types, "Tracon.StoreFailedException")
        self.assertEqual((exception_type.evidence_class, exception_type.reason), ("seam", "exception:Tracon.IThrowingStore"))
        self.assertEqual(_public_type(types, "Tracon.StoreFailure").reason, "signature:Tracon.StoreFailedException")

    def test_an_undocumented_exception_stays_out_of_the_closure(self):
        types = _build_inventory([])
        self.assertEqual(_public_type(types, "Tracon.UndocumentedException").evidence_class, "unproven")

    def test_a_justified_type_is_told_apart_from_unproven(self):
        types = _build_inventory([], reasons={"Tracon.Justified": "generated code calls it"})
        self.assertEqual((_public_type(types, "Tracon.Justified").evidence_class, _public_type(types, "Tracon.Justified").reason),
                         ("justified", "reason:generated code calls it"))

    def test_the_contract_package_is_a_seam_as_a_whole(self):
        types = _build_inventory([], extra_packages={
            "Tracon.Testing.Contracts.Xunit": (
                "Tracon.Testing.Contracts.StoreContract\nTracon.Testing.Contracts.StoreContract.Run() -> void\n",
                "namespace Tracon.Testing.Contracts; public class StoreContract { }"),
        })
        self.assertEqual(_public_type(types, "Tracon.Testing.Contracts.StoreContract",
                                      "Tracon.Testing.Contracts.Xunit").reason, "seam:contract-package")

    def test_the_same_full_name_in_two_packages_is_two_types(self):
        shared = ("Tracon.MigrationRunner\nTracon.MigrationRunner.ProviderName.get -> string!\n",
                      "namespace Tracon; public sealed class MigrationRunner { }")
        types = _build_inventory([("site", "a.md", "MigrationRunner")],
                                 extra_packages={"Tracon.PostgreSql": shared, "Tracon.Sqlite": shared})
        self.assertEqual(_public_type(types, "Tracon.MigrationRunner", "Tracon.PostgreSql").evidence_class, "consumer")
        self.assertEqual(_public_type(types, "Tracon.MigrationRunner", "Tracon.Sqlite").evidence_class, "consumer")


class HelperTests(unittest.TestCase):
    def test_xml_examples_are_read_only_from_triple_slash_comments(self):
        text = (
            "/// <example>\n/// <code>\n/// builder.AddWidget();\n/// </code>\n/// </example>\n"
            "// <code>Hidden()</code>\n")
        example = inventory.xml_examples(text)
        self.assertIn("AddWidget", example)
        self.assertNotIn("Hidden", example)

    def test_a_line_without_a_reason_is_an_error(self):
        with self.assertRaises(ValueError):
            inventory.read_reasons("Tracon.Foo\t\n")

    def test_reason_lines_are_read_and_comments_skipped(self):
        self.assertEqual(inventory.read_reasons("# title\nTracon.Foo\twhy\n"), {"Tracon.Foo": "why"})

    def test_a_stale_reason_is_reported(self):
        types = _build_inventory([("site", "a.md", "Helper")])
        problems = inventory.stale_reasons(types, {"Tracon.Missing": "x", "Tracon.Helper": "y"})
        self.assertEqual(len(problems), 2)
        self.assertTrue(problems[0].startswith("Tracon.Helper: now 'consumer'"))
        self.assertTrue(problems[1].startswith("Tracon.Missing: not public"))

    def test_the_table_total_row_sums_the_classes(self):
        types = _build_inventory([("site", "a.md", "Helper")])
        table = inventory.render_table(types)
        self.assertIn("| `Tracon.Core` |", table)
        self.assertTrue(table.splitlines()[-1].startswith(f"| **Total** | **{len(types)}** |"))


class EndToEndInventoryTests(unittest.TestCase):
    """`build_inventory` in a real git directory: the path that collects the corpus is tested too."""

    DOCUMENT = '{"components": {"schemas": {"AgentDescriptor": {"type": "object"}}}}'

    def _make_repository(self, root: pathlib.Path) -> None:
        files = {
            "src/Tracon.Core/PublicAPI.Unshipped.txt": "#nullable enable\nTracon.AgentDescriptor\nTracon.Documented\n",
            "src/Tracon.Core/Types.cs": (
                "namespace Tracon;\npublic sealed record AgentDescriptor(string Name);\n"
                "public sealed class Documented { }\n"),
            # The document carries the schema name; the type has no other evidence.
            "docs/openapi/tracon.json": self.DOCUMENT,
            "docs-site/src/content/docs/schema.json": self.DOCUMENT,
            # Positive control: corpus collection works, otherwise the test would pass for nothing.
            "docs-site/src/content/docs/guide.md": "Use Documented from the store.\n",
        }
        for path, text in files.items():
            target = root / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(text, encoding="utf-8")
        subprocess.run(["git", "init", "-q"], cwd=root, check=True)
        subprocess.run(["git", "add", "-A"], cwd=root, check=True)

    def test_a_type_whose_schema_name_is_in_a_document_stays_unproven_without_other_evidence(self):
        with tempfile.TemporaryDirectory() as t:
            root = pathlib.Path(t)
            self._make_repository(root)

            types, _ = inventory.build_inventory(root)

        self.assertEqual(types[("Tracon.Core", "Tracon.Documented")].evidence_class, "consumer")
        self.assertEqual(types[("Tracon.Core", "Tracon.AgentDescriptor")].evidence_class, "unproven")

    def test_a_collector_that_puts_document_reading_back_in_the_corpus_breaks_this_test(self):
        # Negative control: does the test really see the change that should break it?
        real_collector = inventory.collect_corpus

        def collector_that_also_reads_the_document(root):
            corpus = real_collector(root)
            entries = [(label, path, corpus.texts[path]) for label, path, _ in corpus.files] + [
                ("document", "docs/openapi/tracon.json", (root / "docs/openapi/tracon.json").read_text(encoding="utf-8"))]
            return inventory.Corpus.build(entries)

        with tempfile.TemporaryDirectory() as t:
            root = pathlib.Path(t)
            self._make_repository(root)

            with mock.patch.object(inventory, "collect_corpus", collector_that_also_reads_the_document):
                types, _ = inventory.build_inventory(root)

        self.assertNotEqual(types[("Tracon.Core", "Tracon.AgentDescriptor")].evidence_class, "unproven")


class CommandLineTests(unittest.TestCase):
    def _exit_code_and_text(self, *argv: str) -> tuple[object, str]:
        out, err = io.StringIO(), io.StringIO()

        with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
            with self.assertRaises(SystemExit) as raised:
                inventory.main(list(argv))

        return raised.exception.code, out.getvalue() + err.getvalue()

    def test_the_help_text_is_english_and_names_the_english_flags(self):
        code, text = self._exit_code_and_text("--help")

        self.assertEqual(code, 0)
        self.assertIsNone(re.search("[\u00e7\u011f\u0131\u00f6\u015f\u00fc\u00c7\u011e\u0130\u00d6\u015e\u00dc]", text))  # Turkish letters

        for flag in ("--list", "--package", "--type", "--check"):
            self.assertIn(flag, text)

    def test_the_old_turkish_flags_and_class_names_are_refused(self):
        # The old class name for "unproven" is written with unicode escapes (it carries Turkish letters).
        for argv in (("--liste", "unproven"), ("--paket", "x"), ("--tip", "x"), ("--denetle",),
                     ("--list", "kan\u0131ts\u0131z"), ("--list", "hepsi")):
            code, _ = self._exit_code_and_text(*argv)
            self.assertEqual(code, 2, argv)

    def test_the_class_names_are_english(self):
        self.assertEqual(inventory.CLASSES, ("consumer", "seam", "justified", "unproven"))

    def test_the_json_keys_are_english(self):
        types = _build_inventory([("site", "a.md", "Helper")])
        out = io.StringIO()

        with mock.patch.object(inventory, "build_inventory", return_value=(types, {})):
            with contextlib.redirect_stdout(out):
                self.assertEqual(inventory.main(["--json"]), 0)

        first = json.loads(out.getvalue())[0]
        self.assertEqual(set(first), set(inventory.JSON_FIELDS.values()))


if __name__ == "__main__":
    unittest.main()
