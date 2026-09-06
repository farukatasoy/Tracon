#!/usr/bin/env python3
"""Tests for the advisory audit evidence package."""
from __future__ import annotations

import importlib.util
import pathlib
import subprocess
import sys
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location(
    "denetim_paketi", ROOT / "scripts" / "denetim-paketi.py")
denetim_paketi = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = denetim_paketi
spec.loader.exec_module(denetim_paketi)


class DenetimPaketiTestleri(unittest.TestCase):
    def test_iddiasiz_test_adayini_bulur(self):
        diff = """diff --git a/tests/X.cs b/tests/X.cs
+++ b/tests/X.cs
+    [Fact]
+    public void New_test()
+    {
+        var value = 1;
+    }
"""

        self.assertEqual(denetim_paketi.test_theater_candidates(diff), ["New_test"])

    def test_shouldly_iddiasi_tasiyan_test_aday_SAYILMAZ(self):
        # 🚨 Var olan test yalniz POZITIF yonu kanitliyordu (iddiasiz test
        # yakalanir); NEGATIF yon hic denenmemisti ve kusur tam orada yasadi.
        # `\bShould\b` bu depodaki 7488 Shouldly iddiasinin hicbirini
        # eslestirmiyordu, yani tarayici HER yeni testi aday sayiyordu.
        diff = """diff --git a/tests/X.cs b/tests/X.cs
+++ b/tests/X.cs
+    [Fact]
+    public void New_test()
+    {
+        entry.Action.ShouldBe("skill.create");
+    }
"""

        self.assertEqual(denetim_paketi.test_theater_candidates(diff), [])

    def test_her_shouldly_bicimi_taninir(self):
        for iddia in ("ShouldBe", "ShouldContain", "ShouldNotBeNull",
                      "ShouldBeTrue", "ShouldBeEmpty", "ShouldHaveSingleItem"):
            with self.subTest(iddia=iddia):
                diff = f"""diff --git a/tests/X.cs b/tests/X.cs
+++ b/tests/X.cs
+    [Fact]
+    public void New_test()
+    {{
+        value.{iddia}();
+    }}
"""

                self.assertEqual(denetim_paketi.test_theater_candidates(diff), [])

    def test_gercekten_iddiasiz_test_HALA_aday(self):
        # Duzeltme tarayiciyi kor etmemeli: gevsetilen tek sey Shouldly'nin
        # taninmasidir, iddia ARAMA sarti degil.
        diff = """diff --git a/tests/X.cs b/tests/X.cs
+++ b/tests/X.cs
+    [Fact]
+    public async Task New_test()
+    {
+        await store.SaveAsync(skill);
+    }
"""

        self.assertEqual(denetim_paketi.test_theater_candidates(diff), ["New_test"])

    def test_imza_govde_kaymasi_adayini_etiketler(self):
        diff = """diff --git a/src/X.cs b/src/X.cs
+++ b/src/X.cs
+    public ValueTask CompleteAsync(RunCost? cost = null)
"""

        candidates = denetim_paketi.signature_body_candidates(diff)

        self.assertTrue(any("CompleteAsync" in candidate for candidate in candidates))
        self.assertTrue(all("ADAY" not in candidate for candidate in candidates))

    def test_imza_taramasi_markdown_kelimelerini_aday_saymaz(self):
        diff = """diff --git a/docs/plan.md b/docs/plan.md
+++ b/docs/plan.md
+Public API and AgentDefinition remain unchanged.
"""

        self.assertEqual(denetim_paketi.signature_body_candidates(diff), [])

    def _run_historical(self, base: str, target: str) -> str:
        result = subprocess.run(
            ["python3", str(ROOT / "scripts" / "denetim-paketi.py"),
             "--taban", base, "--hedef", target],
            cwd=ROOT, capture_output=True, text=True, check=False)
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout

    def test_faz20_tekrar_oynatma_cost_adayini_gosterir(self):
        output = self._run_historical("7717ff1", "9b05f4b")
        self.assertIn("Cost", output)
        self.assertIn("ADAY", output)

    def test_faz68_tekrar_oynatma_cache_adayini_gosterir(self):
        output = self._run_historical("03f1ac3", "2f5d4d0")
        self.assertIn("cached_input_cost", output)
        self.assertIn("ADAY", output)

    def test_faz73_tekrar_oynatma_iddiasiz_test_adayini_gosterir(self):
        output = self._run_historical("4580ed4", "049ff30")
        self.assertIn("İddiası olmayan test metotları", output)
        self.assertIn("ADAY", output)

    def test_adaylar_cikis_kodunu_kirmaz(self):
        self.assertEqual(denetim_paketi.report("HEAD", "HEAD"), 0)

    def test_gecersiz_taban_sha_cikis_kodu_2_doner(self):
        """Faz 91 denetimi: plan 'taban sha yok' hata yolunu vaat ediyordu
        ama hiçbir test bunu doğrulamıyordu."""
        self.assertEqual(denetim_paketi.report("not-a-real-sha-xyz", "HEAD"), 2)

    def test_git_yoksa_traceback_yerine_cikis_kodu_2_doner(self):
        """Faz 91 denetimi: `git()` `subprocess.run`'ın FileNotFoundError'ını
        yakalamıyordu; git PATH'te yoksa `report()` traceback ile çöküyordu."""
        with mock.patch("subprocess.run", side_effect=FileNotFoundError("git")):
            self.assertEqual(denetim_paketi.report("HEAD", "HEAD"), 2)

if __name__ == "__main__":
    unittest.main()
