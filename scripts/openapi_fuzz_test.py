#!/usr/bin/env python3
"""Tests for scripts/openapi_fuzz.py.

They cover the decisions the run makes before and after Schemathesis does the fuzzing: which legs
exist, what a leg's requests carry, which seeded value a generated case gets, what the summary
counts. Nothing here starts the host, a container or Schemathesis; the run itself is exercised by
running it.
"""

from __future__ import annotations

import ast
import collections
import os
import pathlib
import re
import sys
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import openapi_fuzz  # noqa: E402

SCRIPTS = pathlib.Path(__file__).resolve().parent


class LegTests(unittest.TestCase):
    def test_a_role_leg_is_parsed(self):
        self.assertEqual(openapi_fuzz.parse_leg("role:reader"), openapi_fuzz.Leg(role="reader"))

    def test_a_scope_leg_uses_the_admin_role_because_a_key_only_narrows_a_role(self):
        leg = openapi_fuzz.parse_leg("scope:RunsRead+AgentsRead")

        self.assertEqual(leg.role, "admin")
        self.assertEqual(leg.scopes, ("RunsRead", "AgentsRead"))
        self.assertEqual(leg.name, "scope-RunsRead+AgentsRead")

    def test_anything_else_is_refused(self):
        for text in ("role:root", "scope:", "scope:Runs Read", "rol:admin", "admin"):
            with self.subTest(text=text), self.assertRaises(openapi_fuzz.FuzzError):
                openapi_fuzz.parse_leg(text)

    def test_roles_all_is_every_role_once(self):
        legs = openapi_fuzz.expand_legs(["roles:all", "role:admin"], [])

        self.assertEqual([leg.role for leg in legs], list(openapi_fuzz.ROLES))

    def test_scopes_each_is_one_leg_per_scope_of_the_document(self):
        legs = openapi_fuzz.expand_legs(["scopes:each"], ["RunsRead", "AuditRead"])

        self.assertEqual([leg.scopes for leg in legs], [("RunsRead",), ("AuditRead",)])

    def test_scopes_each_without_a_scope_list_is_an_error_not_an_empty_run(self):
        with self.assertRaises(openapi_fuzz.FuzzError):
            openapi_fuzz.expand_legs(["scopes:each"], [])

    def test_the_anonymous_leg_sends_no_role_and_the_key_is_never_a_command_line_header(self):
        self.assertEqual(openapi_fuzz.leg_headers(openapi_fuzz.Leg(role="anonymous")), [])
        headers = openapi_fuzz.leg_headers(openapi_fuzz.parse_leg("scope:RunsRead"))

        self.assertEqual(headers, ["X-Tracon-Demo-Role: admin"])
        self.assertFalse(any("Authorization" in header for header in headers))


class CoverageTests(unittest.TestCase):
    STATS = {
        "responses": {
            "GET /a": {"404": 7},
            "GET /b": {"401": 2, "403": 1},
            "GET /c": {"200": 3, "404": 4},
            "POST /d": {"400": 5, "501": 2},
        }
    }

    def test_operations_that_only_ever_saw_a_404_are_counted_apart(self):
        coverage = openapi_fuzz.summarize_coverage(self.STATS)

        self.assertEqual(coverage.only_not_found, ("GET /a",))

    def test_operations_that_only_ever_saw_a_refusal_are_counted_apart(self):
        coverage = openapi_fuzz.summarize_coverage(self.STATS)

        self.assertEqual(coverage.only_refused, ("GET /b",))

    def test_an_operation_without_a_single_2xx_never_succeeded(self):
        coverage = openapi_fuzz.summarize_coverage(self.STATS)

        self.assertEqual(coverage.never_succeeded, ("GET /a", "GET /b", "POST /d"))
        self.assertEqual(coverage.operations, 4)
        self.assertEqual(coverage.requests, 24)
        self.assertEqual(coverage.statuses["404"], 11)
        self.assertEqual(dict(coverage.status_501), {"POST /d": 2})

    def test_check_failures_sum_failed_evaluations_per_check(self):
        stats = {"checks": {"a": {"GET /x": {"failure": 2, "success": 5}, "GET /y": {"failure": 1}}, "b": {"GET /x": {"success": 9}}}}

        self.assertEqual(openapi_fuzz.check_failures(stats), {"a": 3})

    def test_run_stats_round_trip_through_json_shape(self):
        stats = openapi_fuzz.RunStats()
        stats.record_response("GET /x", 200)
        stats.record_response("GET /x", 200)
        stats.record_response("GET /x", 404)
        stats.record_check("not_a_server_error", "GET /x", "success")

        data = stats.to_json()

        self.assertEqual(data["responses"], {"GET /x": {"200": 2, "404": 1}})
        self.assertEqual(data["checks"], {"not_a_server_error": {"GET /x": {"success": 1}}})


class LogParsingTests(unittest.TestCase):
    LOG = (
        "Test cases:\n  23935 generated, 8 found 8 unique failures\n\n"
        "Failures:\n  ❌ ST101 Server error: 8\n  ❌ ST103 Undocumented status code: 161\n\n"
        "_________________ POST /tracon/api/sessions/{sessionId}/branch _________________\n"
        "Reproduce with:\n\n"
        "    curl -X POST -H 'X-Tracon-Demo-Role: admin' http://127.0.0.1:5399/tracon/api/sessions/x/branch\n\n"
        "    st replay abc\n"
    )

    def test_failure_groups_are_read_from_the_summary(self):
        groups = openapi_fuzz.parse_failure_groups(self.LOG)

        self.assertEqual(groups, {"ST101 Server error": 8, "ST103 Undocumented status code": 161})

    def test_the_generated_case_count_is_read(self):
        self.assertEqual(openapi_fuzz.parse_test_cases(self.LOG), 23935)
        self.assertEqual(openapi_fuzz.parse_test_cases("nothing"), 0)

    def test_the_curl_lines_carry_the_operation_and_the_host_is_a_variable(self):
        lines = openapi_fuzz.extract_repro(self.LOG, "http://127.0.0.1:5399")

        self.assertEqual(len(lines), 1)
        self.assertIn("POST /tracon/api/sessions/{sessionId}/branch", lines[0])
        self.assertIn("$HOST/tracon/api/sessions/x/branch", lines[0])
        self.assertNotIn("5399", lines[0])


class SeedTests(unittest.TestCase):
    def setUp(self):
        self.plan = openapi_fuzz.SeedPlan()
        self.plan.add("agent", "fuzz-agent", "support")
        self.plan.add("agent", "gone-1", "gone-2", disposable=True)
        self.plan.add("run", "run-1")

    def test_every_rule_pattern_compiles_and_every_pool_name_is_a_string(self):
        for pattern, kinds in openapi_fuzz.PATH_POOL_RULES:
            re.compile(pattern)
            self.assertTrue(all(isinstance(kind, str) and kind for kind in kinds.values()), pattern)

    def test_the_first_matching_rule_decides(self):
        self.assertEqual(openapi_fuzz.pool_kinds_for("/tracon/api/agents/{name}/versions/{a}/diff/{b}"), {"name": "agent", "a": "version", "b": "version"})
        self.assertEqual(openapi_fuzz.pool_kinds_for("/tracon/api/runs/{a}/compare/{b}"), {"a": "run", "b": "run"})
        self.assertEqual(openapi_fuzz.pool_kinds_for("/tracon/api/agents"), {})

    def test_the_decision_is_a_function_of_the_case_alone(self):
        first = openapi_fuzz.choose_seed(self.plan, method="get", template="/tracon/api/agents/{name}", path_parameters={"name": "x"})
        again = openapi_fuzz.choose_seed(self.plan, method="get", template="/tracon/api/agents/{name}", path_parameters={"name": "x"})

        self.assertEqual(first, again)

    def test_about_the_configured_share_of_cases_gets_a_seeded_value(self):
        swapped = 0
        total = 2000

        for index in range(total):
            choice = openapi_fuzz.choose_seed(self.plan, method="get", template="/tracon/api/agents/{name}", path_parameters={"name": f"n{index}"})
            swapped += choice.path_parameters is not None

        share = swapped / total * 100
        self.assertGreater(share, openapi_fuzz.INJECT_PERCENT - 8)
        self.assertLess(share, openapi_fuzz.INJECT_PERCENT + 8)

    def test_a_seeded_value_comes_from_the_stable_pool_for_reads(self):
        values = set()

        for index in range(300):
            choice = openapi_fuzz.choose_seed(self.plan, method="GET", template="/tracon/api/agents/{name}", path_parameters={"name": f"n{index}"})

            if choice.path_parameters is not None:
                values.add(choice.path_parameters["name"])

        self.assertEqual(values, {"fuzz-agent", "support"})

    def test_a_delete_draws_only_from_the_disposable_pool(self):
        values = set()

        for index in range(300):
            choice = openapi_fuzz.choose_seed(self.plan, method="DELETE", template="/tracon/api/agents/{name}", path_parameters={"name": f"n{index}"})

            if choice.path_parameters is not None:
                values.add(choice.path_parameters["name"])

        self.assertEqual(values, {"gone-1", "gone-2"})

    def test_a_delete_with_no_disposable_resource_is_left_alone_rather_than_aimed_at_a_stable_one(self):
        for index in range(100):
            choice = openapi_fuzz.choose_seed(self.plan, method="DELETE", template="/tracon/api/api-keys/{id}", path_parameters={"id": f"n{index}"})

            self.assertIsNone(choice.path_parameters)

    def test_a_route_without_a_rule_is_never_changed(self):
        choice = openapi_fuzz.choose_seed(self.plan, method="GET", template="/tracon/api/stats", path_parameters={})

        self.assertIsNone(choice.path_parameters)
        self.assertFalse(choice.has_body)

    def test_a_body_that_repeats_the_route_name_repeats_the_seeded_name(self):
        bodies = []

        for index in range(300):
            choice = openapi_fuzz.choose_seed(self.plan, method="PUT", template="/tracon/api/agents/{name}", path_parameters={"name": f"n{index}"})

            if choice.has_body:
                name = choice.path_parameters["name"] if choice.path_parameters is not None else f"n{index}"
                self.assertEqual(choice.body["name"], name)
                bodies.append(choice.body)

        self.assertGreater(len(bodies), 50)

    def test_a_body_marker_that_the_route_does_not_define_is_a_mistake_in_the_table(self):
        for (method, template), body in openapi_fuzz.GOOD_BODIES.items():
            markers = set(re.findall(r"\{(\w+)\}", repr(body))) - {"n"}
            route = set(re.findall(r"\{(\w+)\}", template))

            self.assertLessEqual(markers, route, f"{method} {template}")

    def test_every_body_route_exists_with_a_pool_or_needs_none(self):
        for (method, template) in openapi_fuzz.GOOD_BODIES:
            self.assertIn(method, ("POST", "PUT"))
            route = re.findall(r"\{(\w+)\}", template)
            kinds = openapi_fuzz.pool_kinds_for(template)

            self.assertTrue(set(route) <= set(kinds), f"{method} {template} has a path parameter no pool rule covers")

    def test_the_plan_survives_a_round_trip_through_a_file(self):
        import tempfile

        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / "seed.json"
            openapi_fuzz.save_seed(self.plan, path)
            loaded = openapi_fuzz.load_seed(path)

        self.assertEqual(loaded.stable, self.plan.stable)
        self.assertEqual(loaded.disposable, self.plan.disposable)


class EnvironmentTests(unittest.TestCase):
    def test_the_hosts_own_configuration_does_not_leak_into_a_run(self):
        with mock.patch.dict(os.environ, {"Tracon__Providers__OpenAI__ApiKey": "real", "ASPNETCORE_URLS": "http://x", "KEEP": "1"}):
            environment = openapi_fuzz.scrubbed_environment({"Tracon__Demo__Roles__Enabled": "true"})

        self.assertNotIn("Tracon__Providers__OpenAI__ApiKey", environment)
        self.assertNotIn("ASPNETCORE_URLS", environment)
        self.assertEqual(environment["KEEP"], "1")
        self.assertEqual(environment["Tracon__Demo__Roles__Enabled"], "true")

    def test_a_database_store_without_docker_or_a_connection_string_says_so(self):
        store = openapi_fuzz.Store("postgres", pathlib.Path("."), None)

        with mock.patch("shutil.which", return_value=None), self.assertRaises(openapi_fuzz.FuzzError) as caught:
            store.start()

        self.assertIn("--database-env", str(caught.exception))

    def test_an_empty_connection_variable_is_an_error_not_an_in_memory_run(self):
        store = openapi_fuzz.Store("sqlserver", pathlib.Path("."), "TRACON_FUZZ_TEST_EMPTY")

        with mock.patch.dict(os.environ, {"TRACON_FUZZ_TEST_EMPTY": ""}), self.assertRaises(openapi_fuzz.FuzzError):
            store.start()

    def test_the_memory_store_adds_nothing_to_the_host_environment(self):
        self.assertEqual(openapi_fuzz.Store("memory", pathlib.Path("."), None).start(), {})


class PolicyFileTests(unittest.TestCase):
    """The files that decide what the run accepts and which version it runs."""

    def test_the_pinned_schemathesis_is_the_one_the_script_names(self):
        text = (SCRIPTS / "openapi-fuzz-requirements.txt").read_text(encoding="utf-8")

        self.assertIn(f"\nschemathesis=={openapi_fuzz.SCHEMATHESIS_VERSION} \\\n", text)

    def test_every_requirement_is_pinned_with_a_hash(self):
        text = (SCRIPTS / "openapi-fuzz-requirements.txt").read_text(encoding="utf-8")
        entries = [block for block in re.split(r"\n(?=[A-Za-z0-9_.-]+==)", text) if re.match(r"[A-Za-z0-9_.-]+==", block)]

        self.assertGreater(len(entries), 20)

        for block in entries:
            self.assertIn("--hash=sha256:", block, block.splitlines()[0])

    def test_the_accepted_answers_are_one_named_status_on_named_operations_and_nothing_wider(self):
        self.assertGreater(len(openapi_fuzz.ACCEPTED_ANSWERS), 8)

        for (method, template), status in openapi_fuzz.ACCEPTED_ANSWERS.items():
            self.assertIn(method, ("GET", "POST", "PUT", "DELETE"))
            self.assertTrue(template.startswith("/tracon/"), template)
            # Only "cannot do that here" and "upstream unreachable"; a 500 is never acceptable.
            self.assertIn(status, (501, 502), f"{method} {template}")

    def test_an_accepted_answer_is_accepted_only_on_its_own_operation_and_status(self):
        accepts = openapi_fuzz.accepts_answer

        self.assertTrue(accepts("get", "/tracon/api/voice/voices", 501))
        self.assertFalse(accepts("GET", "/tracon/api/voice/voices", 500), "a crash on a listed operation still fails")
        self.assertFalse(accepts("GET", "/tracon/api/voice/voices", 502), "a different server status still fails")
        self.assertFalse(accepts("POST", "/tracon/api/voice/voices", 501), "another method of the same path still fails")
        self.assertFalse(accepts("GET", "/tracon/api/agents", 501), "an unlisted operation still fails")
        self.assertTrue(accepts("GET", "/tracon/api/mcp-servers/{name}/prompts", 502))

    def test_no_schemathesis_configuration_file_is_involved(self):
        # The configuration applies only the first of several per-operation entries; the table above does not.
        self.assertFalse((SCRIPTS / "openapi-fuzz.toml").exists())

    def test_the_status_code_check_reports_and_does_not_gate(self):
        self.assertIn("not_a_server_error", openapi_fuzz.GATING_CHECKS)
        self.assertIn("status_code_conformance", openapi_fuzz.REPORT_CHECKS)
        self.assertFalse(set(openapi_fuzz.GATING_CHECKS) & set(openapi_fuzz.REPORT_CHECKS))

    def test_the_hooks_define_only_hook_names_schemathesis_knows(self):
        tree = ast.parse((SCRIPTS / "openapi_fuzz_hooks.py").read_text(encoding="utf-8"))
        defined = {
            node.name
            for node in tree.body
            if isinstance(node, ast.FunctionDef) and any("hook" in ast.dump(decorator) for decorator in node.decorator_list)
        }

        self.assertEqual(defined, {"before_call", "map_case", "filter_failure", "after_call", "after_validate"})

    def test_a_path_filter_reaches_the_schemathesis_command_and_nothing_else_changes(self):
        base = dict(store="memory", examples=5, report_examples=5, seed=1, workers=1, phases=None, gating=("a",), reporting=(), seed_data=False, output=pathlib.Path("."), schemathesis=pathlib.Path("st"), base_url="http://h")
        leg = openapi_fuzz.Leg(role="admin")

        plain = openapi_fuzz.schemathesis_command(openapi_fuzz.Settings(only=None, **base), leg, checks=("a",), examples=5, directory=pathlib.Path("."), junit="x.xml")
        narrow = openapi_fuzz.schemathesis_command(openapi_fuzz.Settings(only="/agents", **base), leg, checks=("a",), examples=5, directory=pathlib.Path("."), junit="x.xml")

        self.assertNotIn("--include-path-regex", plain)
        self.assertEqual(narrow[narrow.index("--include-path-regex") + 1], "/agents")
        self.assertEqual([part for part in narrow if part not in ("--include-path-regex", "/agents")], plain)

    def test_the_summary_names_a_failing_leg(self):
        settings = mock.Mock(store="memory", seed=1, examples=10, gating=("a",), reporting=())
        leg = openapi_fuzz.Leg(role="reader")
        coverage = openapi_fuzz.summarize_coverage({"responses": {"GET /x": {"404": 1}}})
        result = openapi_fuzz.LegResult(leg, {}, {"gate: ST101 Server error": 2}, {"gate": 1}, coverage, 5, True, [])

        text = openapi_fuzz.format_summary(settings, [result], {"GET /v": 3}, [])

        self.assertIn("| `role-reader` | FAIL | 2 gating, 0 reported |", text)
        self.assertIn("- GET /v: 3", text)


if __name__ == "__main__":
    unittest.main()
