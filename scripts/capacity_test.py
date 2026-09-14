#!/usr/bin/env python3
"""scripts/capacity.py için testler.

Ölçümün kendisini değil, ölçümü BAŞLATMAYI yöneten kararları test eder:
profil doğrulama, hücre planı ve exact sürüm kapısı. Bu üçü yanlışsa koşum
saatler sürer ve sonunda yorumlanamayan bir rapor verir.
"""

from __future__ import annotations

import pathlib
import sys
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))

import capacity  # noqa: E402


def profile(**overrides):
    base = {
        "name": "test",
        "scenarios": ["buffered"],
        "scenarioGrouping": "separate",
        "concurrency": [1],
        "seedShapes": ["empty"],
        "repeats": 1,
        "warmupSeconds": 0,
        "measureSeconds": 0,
        "runsPerTenant": 2,
        "workload": {},
        "limits": {},
    }
    base.update(overrides)
    return base


class ProfileValidationTests(unittest.TestCase):
    def test_usable_profile_has_no_problem(self):
        self.assertEqual(capacity.validate_profile(profile()), [])

    def test_a_profile_with_no_bound_is_refused(self):
        problems = capacity.validate_profile(profile(runsPerTenant=0, measureSeconds=0))
        self.assertTrue(any("measureSeconds veya runsPerTenant" in p for p in problems))

    def test_two_bounds_at_once_are_refused(self):
        problems = capacity.validate_profile(profile(runsPerTenant=2, measureSeconds=60))
        self.assertTrue(any("iki ayrı sınırdır" in p for p in problems))

    def test_zero_repeats_is_refused(self):
        problems = capacity.validate_profile(profile(repeats=0))
        self.assertTrue(any("repeats" in p for p in problems))

    def test_negative_warmup_is_refused(self):
        problems = capacity.validate_profile(profile(warmupSeconds=-1))
        self.assertTrue(any("warmupSeconds" in p for p in problems))

    def test_zero_concurrency_is_refused(self):
        problems = capacity.validate_profile(profile(concurrency=[0]))
        self.assertTrue(any("concurrency" in p for p in problems))

    def test_zero_arrival_rate_is_refused(self):
        problems = capacity.validate_profile(profile(arrivalRates=[0]))
        self.assertTrue(any("arrivalRates" in p for p in problems))

    def test_unknown_scenario_is_refused(self):
        problems = capacity.validate_profile(profile(scenarios=["websocket"]))
        self.assertTrue(any("bilinmeyen senaryo" in p for p in problems))

    def test_unknown_seed_shape_is_refused(self):
        problems = capacity.validate_profile(profile(seedShapes=["huge"]))
        self.assertTrue(any("bilinmeyen seed" in p for p in problems))

    def test_empty_scenarios_is_refused(self):
        problems = capacity.validate_profile(profile(scenarios=[]))
        self.assertTrue(any("scenarios" in p for p in problems))


class ShippedProfileTests(unittest.TestCase):
    """Repo'nun kendi profilleri de kapıdan geçer."""

    def test_every_shipped_profile_is_measurable(self):
        for path in sorted(capacity.PROFILE_DIR.glob("*.json")):
            with self.subTest(profile=path.stem):
                loaded = capacity.load_profile(path.stem)
                self.assertEqual(capacity.validate_profile(loaded), [])

    def test_the_smoke_profile_is_bounded_by_count_so_CI_time_is_predictable(self):
        smoke = capacity.load_profile("smoke")
        self.assertGreater(smoke["runsPerTenant"], 0)
        self.assertEqual(smoke["measureSeconds"], 0)

    def test_the_soak_profile_writes_its_chosen_load_explicitly(self):
        # Öneri algoritması workload'u sessizce değiştiremez: seçilen
        # concurrency profilde AÇIK bir sayı olarak durur.
        soak = capacity.load_profile("soak")
        self.assertEqual(soak["concurrency"], [8])
        self.assertEqual(soak["measureSeconds"], 1800)

    def test_the_worker_axis_is_separate_and_not_crossed_with_the_sweep_steps(self):
        workers = capacity.load_profile("workers")
        self.assertEqual(workers["workerCounts"], [1, 2, 4])
        self.assertEqual(workers["scenarios"], ["queued"])
        self.assertIn("fixedConcurrency", workers)


class CellPlanTests(unittest.TestCase):
    def test_a_separate_profile_crosses_scenario_concurrency_seed_and_repeat(self):
        cells = capacity.plan_cells(
            profile(scenarios=["buffered", "streaming"], concurrency=[1, 8],
                    seedShapes=["empty", "full"], repeats=3),
            "run")
        self.assertEqual(len(cells), 2 * 2 * 2 * 3)

    def test_a_mixed_profile_produces_one_cell_per_load_point(self):
        cells = capacity.plan_cells(
            profile(scenarios=["buffered", "streaming", "queued"],
                    scenarioGrouping="mixed", concurrency=[8], repeats=1),
            "run")
        self.assertEqual(len(cells), 1)
        self.assertEqual(cells[0]["scenarios"], ["buffered", "streaming", "queued"])

    def test_the_worker_axis_never_multiplies_the_concurrency_list(self):
        # 🚨 Eksen çaprazlanırsa 1/2/4 worker × 4 basamak çıkar ve rapor
        # "worker sayısı" eksenini okunamaz hale getirir.
        cells = capacity.plan_cells(
            profile(scenarios=["queued"], concurrency=[1, 8, 32, 64],
                    fixedConcurrency=8, workerCounts=[1, 2, 4], repeats=2),
            "run")
        self.assertEqual(len(cells), 3 * 2)
        self.assertTrue(all(c["concurrency"] == 8 for c in cells))
        self.assertEqual(sorted({c["workerCount"] for c in cells}), [1, 2, 4])

    def test_the_arrival_axis_is_also_separate(self):
        cells = capacity.plan_cells(
            profile(scenarios=["queued"], concurrency=[1, 8],
                    fixedConcurrency=16, arrivalRates=[1, 4], repeats=1),
            "run")
        self.assertEqual(len(cells), 2)
        self.assertEqual(sorted(c["arrivalRatePerSecond"] for c in cells), [1, 4])

    def test_cell_ids_are_unique(self):
        cells = capacity.plan_cells(
            profile(scenarios=["buffered", "streaming"], concurrency=[1, 8], repeats=3),
            "run")
        self.assertEqual(len({c["cellId"] for c in cells}), len(cells))


class ExactVersionTests(unittest.TestCase):
    def test_a_floating_version_is_not_exact(self):
        for version in ("*", "*-*", "1.0.0-*", ""):
            with self.subTest(version=version):
                self.assertIsNone(capacity.EXACT_VERSION.match(version))

    def test_an_exact_version_is_accepted(self):
        for version in ("1.0.0", "1.0.0-preview.3", "0.0.0-dirty.capacity166"):
            with self.subTest(version=version):
                self.assertIsNotNone(capacity.EXACT_VERSION.match(version))


class CanaryTests(unittest.TestCase):
    def test_the_planted_canary_is_credential_shaped(self):
        # Yakalanamayacak bir canary her redaction testini boşa çıkarır.
        self.assertIn("Password=", capacity.CANARY_CREDENTIAL)


if __name__ == "__main__":
    unittest.main()
