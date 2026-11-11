"""Tests for runner_card.py: the card describes the runner and never fails the job."""

from __future__ import annotations

import io
import pathlib
import sys
import tempfile
import unittest
from contextlib import redirect_stdout
from unittest import mock

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import runner_card  # noqa: E402


class RunnerCardTests(unittest.TestCase):
    def test_memory_is_read_from_the_meminfo_text(self):
        self.assertEqual(runner_card.parse_meminfo("MemFree: 1 kB\nMemTotal:       16384 kB\n"), 16384 * 1024)

    def test_a_meminfo_without_the_line_gives_none(self):
        self.assertIsNone(runner_card.parse_meminfo("MemFree: 1 kB\n"))
        self.assertIsNone(runner_card.parse_meminfo("MemTotal: many kB\n"))

    def test_the_commit_loop_measures_a_positive_time_and_leaves_nothing_behind(self):
        with tempfile.TemporaryDirectory() as directory:
            seconds = runner_card.commit_loop_seconds(pathlib.Path(directory), commits=20)
            self.assertGreater(seconds, 0)
            self.assertEqual(list(pathlib.Path(directory).iterdir()), [])

    def test_the_runner_temp_directory_is_measured_when_it_exists(self):
        with tempfile.TemporaryDirectory() as directory:
            found = runner_card.locations({"RUNNER_TEMP": directory})
            self.assertEqual(found["runner_temp"], pathlib.Path(directory))
            self.assertIn("temp", found)

    def test_a_runner_temp_that_is_missing_or_unset_is_left_out(self):
        self.assertNotIn("runner_temp", runner_card.locations({}))
        self.assertNotIn("runner_temp", runner_card.locations({"RUNNER_TEMP": str(pathlib.Path("no") / "such" / "directory")}))

    def test_the_ratio_says_how_many_times_faster_the_runner_temp_is(self):
        self.assertEqual(runner_card.ratio_line({"temp": 6.0, "runner_temp": 2.0}), "temp / runner_temp = 3.00")

    def test_the_ratio_is_absent_unless_both_were_measured(self):
        self.assertIsNone(runner_card.ratio_line({"temp": 6.0}))
        self.assertIsNone(runner_card.ratio_line({"temp": 6.0, "runner_temp": 0.0}))

    def test_the_card_names_the_cores_the_disk_and_the_commit_loop(self):
        with tempfile.TemporaryDirectory() as directory:
            text = "\n".join(runner_card.card({"RUNNER_TEMP": directory}))
        for expected in ("cpu cores:", "memory:", "temp:", "runner_temp:", "defender:", "sqlite 300 commits in temp:", "sqlite temp / runner_temp ="):
            self.assertIn(expected, text)

    def test_the_card_never_fails_the_job_even_when_a_probe_breaks(self):
        output = io.StringIO()
        with mock.patch.object(runner_card, "card", side_effect=RuntimeError("probe broke")), redirect_stdout(output):
            self.assertEqual(runner_card.main(), 0)
        self.assertIn("the card could not be completed: probe broke", output.getvalue())


if __name__ == "__main__":
    unittest.main()
