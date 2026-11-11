"""Tests for evidence_selftest.py: the self-test filters three named tests and judges their evidence."""

from __future__ import annotations

import pathlib
import sys
import tempfile
import unittest
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import evidence_selftest  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parent.parent


def _write(directory: pathlib.Path, name: str, text: str = "x") -> None:
    (directory / name).write_text(text, encoding="utf-8")


class SelfTestFilterTests(unittest.TestCase):
    def test_exactly_three_tests_are_named_and_each_is_one_method_of_one_project(self):
        self.assertEqual(sorted(evidence_selftest.SELF_TESTS), ["host", "page", "pulse"])
        for kind, test in evidence_selftest.SELF_TESTS.items():
            command = evidence_selftest.command(test)
            self.assertEqual(command.count("--filter-method"), 1, kind)
            self.assertNotIn("--filter-class", command, kind)
            self.assertIn("--no-build", command, kind)
            self.assertNotIn("*", test.method, kind)

    def test_every_named_method_exists_in_the_test_source_and_turns_the_switch_on(self):
        for kind, test in evidence_selftest.SELF_TESTS.items():
            matches = [
                path for path in (ROOT / "tests" / test.project).rglob("*.cs")
                if f"Task {test.method}(" in path.read_text(encoding="utf-8")
            ]
            self.assertEqual(len(matches), 1, f"{kind}: {test.method}")
            self.assertIn("EvidenceSelfTest.SkipUnlessEnabled();", matches[0].read_text(encoding="utf-8"), kind)

    def test_the_evidence_directory_is_one_of_its_own_under_the_uploaded_results(self):
        directory = evidence_selftest.evidence_directory(evidence_selftest.SELF_TESTS["pulse"], pathlib.Path("/repo"))
        self.assertEqual(directory, pathlib.Path("/repo/artifacts/test-results/Tracon.Core.UnitTests/evidence-selftest"))


class EvidenceCheckTests(unittest.TestCase):
    def test_a_complete_pulse_evidence_passes_and_each_missing_piece_is_named(self):
        with tempfile.TemporaryDirectory() as raw:
            directory = pathlib.Path(raw)
            self.assertEqual(
                evidence_selftest.check_pulse(directory),
                ["pulse.txt was not written", "the failed test left no evidence file"],
            )
            _write(directory, "pulse.txt", "2026-11-10T00:00:00.000Z process stall 900 ms")
            self.assertIn("pulse.txt does not name a thread pool delay", evidence_selftest.check_pulse(directory))
            _write(directory, "pulse.txt", "2026-11-10T00:00:00.000Z thread pool delay 900 ms")
            _write(directory, "X.PulseSelfTests.A_wait-1234.txt")
            self.assertEqual(evidence_selftest.check_pulse(directory), [])

    def test_the_host_evidence_needs_the_referenced_line_and_the_warning_and_not_the_unrelated_line(self):
        with tempfile.TemporaryDirectory() as raw:
            directory = pathlib.Path(raw)
            self.assertEqual(evidence_selftest.check_host(directory), ["the failed test left no evidence file"])
            _write(directory, "X.HostFailureEvidenceTests.A_failed-1.txt", "Information job failed (ref: 5e1f7a02)")
            self.assertEqual(evidence_selftest.check_host(directory), ["the warning line is missing"])
            _write(directory, "X.HostFailureEvidenceTests.A_failed-1.txt",
                   "Information job failed (ref: 5e1f7a02)\nWarning a warning the note must keep\nInformation an unrelated line the note must leave out")
            self.assertEqual(evidence_selftest.check_host(directory), ["an unrelated line was kept"])
            _write(directory, "X.HostFailureEvidenceTests.A_failed-1.txt",
                   "Information job failed (ref: 5e1f7a02)\nWarning a warning the note must keep")
            self.assertEqual(evidence_selftest.check_host(directory), [])

    def test_the_page_evidence_needs_a_non_empty_trace_and_the_page_note(self):
        with tempfile.TemporaryDirectory() as raw:
            directory = pathlib.Path(raw)
            self.assertEqual(evidence_selftest.check_page(directory), ["no trace file was left", "the failed test left no evidence file"])
            (directory / "T-1.trace.zip").write_bytes(b"")
            _write(directory, "X.PageFailureEvidenceTests.A_failed-1.txt", "address: http://127.0.0.1:1/tracon/audit")
            self.assertEqual(evidence_selftest.check_page(directory), ["the trace file is empty"])
            (directory / "T-1.trace.zip").write_bytes(b"PK")
            self.assertEqual(evidence_selftest.check_page(directory), [])


class OutputCheckTests(unittest.TestCase):
    MESSAGE = ("Waited 1 s for x; it never happened. Polls: 3; longest gap between polls: 889 ms; "
               "longest process stall: 4 ms; longest thread pool delay: 884 ms.")

    def test_a_message_with_the_poll_count_and_a_pool_delay_passes(self):
        self.assertEqual(evidence_selftest.check_pulse_output(self.MESSAGE), [])

    def test_a_message_without_the_numbers_is_named(self):
        self.assertEqual(
            evidence_selftest.check_pulse_output("Waited 1 s for x; it never happened."),
            ["the timeout message does not carry the poll count and the longest gap",
             "the timeout message does not name a thread pool delay above zero"])

    def test_a_pool_delay_of_zero_is_not_starvation(self):
        self.assertEqual(
            evidence_selftest.check_pulse_output(self.MESSAGE.replace("884 ms.", "0 ms.")),
            ["the timeout message does not name a thread pool delay above zero"])


class RunTests(unittest.TestCase):
    def _run(self, returncode: int, prepare=None, stdout: str = OutputCheckTests.MESSAGE) -> int:
        def fake(command, **kwargs):  # noqa: ANN001, ANN202
            if prepare:
                prepare(pathlib.Path(kwargs["env"][evidence_selftest.DIRECTORY_VARIABLE]))
            return mock.Mock(returncode=returncode, stdout=stdout, stderr="")

        with tempfile.TemporaryDirectory() as root, mock.patch.object(evidence_selftest.subprocess, "run", side_effect=fake):
            return evidence_selftest.run("pulse", pathlib.Path(root), {})

    def test_a_test_that_did_not_fail_is_red_whatever_evidence_exists(self):
        def prepare(directory: pathlib.Path) -> None:
            _write(directory, "pulse.txt", "thread pool delay")
            _write(directory, "X.PulseSelfTests.A-1.txt")

        self.assertEqual(self._run(0, prepare), 1)

    def test_a_filter_that_matched_nothing_is_red(self):
        self.assertEqual(self._run(8), 1)

    def test_a_failed_test_without_its_evidence_is_red(self):
        self.assertEqual(self._run(2), 1)

    def test_a_failed_test_with_its_evidence_is_green(self):
        def prepare(directory: pathlib.Path) -> None:
            _write(directory, "pulse.txt", "thread pool delay")
            _write(directory, "X.PulseSelfTests.A-1.txt")

        self.assertEqual(self._run(2, prepare), 0)

    def test_a_pulse_failure_that_is_not_the_starved_wait_is_red_even_with_its_files(self):
        def prepare(directory: pathlib.Path) -> None:
            _write(directory, "pulse.txt", "thread pool delay")
            _write(directory, "X.PulseSelfTests.A-1.txt")

        self.assertEqual(self._run(2, prepare, stdout="System.InvalidOperationException: something else broke"), 1)


if __name__ == "__main__":
    unittest.main()
