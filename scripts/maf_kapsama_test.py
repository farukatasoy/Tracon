#!/usr/bin/env python3
"""`maf-kapsama.py` testleri (Faz 224).

Betik ağ istemez; hepsi CI'da koşar. Gerçek repoya karşı iki test vardır: matrisin
§5'i ayrıştırılabilir olmalı ve üretilen temas tablosu taze olmalıdır. Geri kalanı
geçici dizinde kurulan küçük girdilerle sınanır.
"""
from __future__ import annotations

import contextlib
import importlib.util
import io
import pathlib
import sys
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("maf_kapsama", ROOT / "scripts" / "maf-kapsama.py")
kapsama = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kapsama
spec.loader.exec_module(kapsama)


def matris(*satirlar: str, baslik: str = "## 5. Yetenek matrisi") -> str:
    """En küçük matris: §5 başlığı, bir alt bölüm, verilen satırlar ve §6."""
    govde = "\n".join(satirlar)
    return (
        "# Başlık\n\n"
        f"{baslik}\n\n"
        "### 5.1 Agent çekirdeği\n\n"
        "| | Yetenek (MAF) | Tracon bugün | Neden | Kendi | Takip | Etki | Not |\n"
        "|---|---|---|---|---|---|---|---|\n"
        f"{govde}\n\n"
        "## 6. Dalga 1\n\n"
        "| ✅ | `DisaridaKalir` | — | — | — | ✗ | — | — |\n"
    )


def satir(durum: str, yetenek: str, takip: str = "✗") -> str:
    return f"| {durum} | {yetenek} | — | — | — | {takip} | — | — |"


class SatirOkumaTests(unittest.TestCase):
    def test_reads_status_tracking_section_and_tokens(self) -> None:
        satirlar = kapsama.satirlari_oku(matris(
            satir("✅", "`AIAgent` · `RunAsync`/`RunStreamingAsync`", "✓"),
            satir("❌", "`AIAgentBuilder.Use(Func<AIAgent, FunctionInvocationContext>)`", "✗⁺"),
        ))

        self.assertEqual(len(satirlar), 2)
        self.assertEqual((satirlar[0].bolum, satirlar[0].durum, satirlar[0].takip), ("5.1", "✅", "✓"))
        self.assertEqual(satirlar[0].jetonlar, ("AIAgent", "RunAsync", "RunStreamingAsync"))
        self.assertEqual(satirlar[1].jetonlar, ("AIAgent", "AIAgentBuilder", "Func", "FunctionInvocationContext", "Use"))
        self.assertEqual(satirlar[1].takip, "✗⁺")

    def test_ignores_rows_outside_section_5_and_rows_without_a_status(self) -> None:
        satirlar = kapsama.satirlari_oku(matris(
            satir("✅", "`Icerde`"),
            "| | `BaslikGibi` | — | — | — | — | — | — |",
        ))

        self.assertEqual([s.jetonlar for s in satirlar], [("Icerde",)])

    def test_text_outside_backticks_and_short_names_are_not_tokens(self) -> None:
        satirlar = kapsama.satirlari_oku(matris(satir("🟡", "Yapılandırılmış çıktı: `RunAsync<T>` · `ChatOptions.ResponseFormat` ✔")))

        self.assertEqual(satirlar[0].jetonlar, ("ChatOptions", "ResponseFormat", "RunAsync"))

    def test_a_missing_section_is_an_error_not_an_empty_table(self) -> None:
        with self.assertRaisesRegex(ValueError, "§5"):
            kapsama.satirlari_oku(matris(satir("✅", "`AIAgent`"), baslik="## 5. Başka bir şey"))

    def test_a_section_without_rows_is_an_error(self) -> None:
        with self.assertRaisesRegex(ValueError, "satır"):
            kapsama.satirlari_oku(matris())

    def test_the_real_matrix_parses(self) -> None:
        satirlar = kapsama.satirlari_oku(kapsama.MATRIS.read_text(encoding="utf-8"))

        self.assertGreaterEqual(len(satirlar), 100)
        self.assertTrue(all(s.bolum.startswith("5.") for s in satirlar))


class KaynakJetonlariTests(unittest.TestCase):
    def test_comments_strings_and_char_literals_are_not_contact(self) -> None:
        kod = '\n'.join([
            "// AIAgent in a line comment",
            "/* ChatOptions in a block",
            "   comment */",
            'var url = "http://x/AgentSession"; var real = new ChatClientAgent();',
            "var quote = '\"'; var after = typeof(AgentResponse);",
            'var raw = """',
            "    HarnessAgent in a raw string",
            '    """;',
            'var verbatim = @"C:\\AgentThread"" still inside"; TodoProvider p;',
        ])

        jetonlar = kapsama.kaynak_jetonlari(kod)

        self.assertIn("ChatClientAgent", jetonlar)
        self.assertIn("AgentResponse", jetonlar)
        self.assertIn("TodoProvider", jetonlar)
        for disarida in ("AIAgent", "ChatOptions", "AgentSession", "HarnessAgent", "AgentThread"):
            self.assertNotIn(disarida, jetonlar)

    def test_a_four_quote_raw_string_closes_on_four_quotes(self) -> None:
        kod = '\n'.join([
            'var raw = """"',
            '    contains """ and RunAsync but stays text',
            '    """";',
            "var real = new ChatClientAgent();",
        ])

        jetonlar = kapsama.kaynak_jetonlari(kod)

        self.assertIn("ChatClientAgent", jetonlar)
        self.assertNotIn("RunAsync", jetonlar)

    def test_a_string_inside_an_interpolation_hole_does_not_end_the_string(self) -> None:
        jetonlar = kapsama.kaynak_jetonlari('var s = $"{x ?? "ChatClientAgent"}"; var after = new TodoProvider();')

        self.assertNotIn("ChatClientAgent", jetonlar)
        self.assertIn("TodoProvider", jetonlar)

    def test_verbatim_interpolated_strings_in_both_prefix_orders(self) -> None:
        kod = '\n'.join([
            'var a = @$"line one',
            '    ChatClientAgent in the text";',
            'var b = $@"{d} AgentSession";',
            r'var c = @$"C:\{d}\"; new LoggingAgent();',
        ])

        jetonlar = kapsama.kaynak_jetonlari(kod)

        self.assertNotIn("ChatClientAgent", jetonlar)
        self.assertNotIn("AgentSession", jetonlar)
        self.assertIn("LoggingAgent", jetonlar)


class TemasTests(unittest.TestCase):
    def kur(self, kok: pathlib.Path, dosyalar: dict[str, str]) -> None:
        (kok / "docs").mkdir()
        (kok / "docs" / "MAF-KAPSAMA.md").write_text(matris(
            satir("✅", "`ChatClientAgent` · `AgentResponse`", "✓"),
            satir("❌", "`MessageInjectingChatClient`"),
        ), encoding="utf-8")
        for yol, metin in dosyalar.items():
            hedef = kok / yol
            hedef.parent.mkdir(parents=True, exist_ok=True)
            hedef.write_text(metin, encoding="utf-8")

    def test_table_lists_the_contact_set_per_area(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            kok = pathlib.Path(d)
            self.kur(kok, {
                "src/A/Agent.cs": "class X { ChatClientAgent a; }",
                "tests/A/T.cs": "class T { AgentResponse r; MessageInjectingChatClient m; }",
                "src/A/obj/Gen.cs": "class G { AgentResponse r; }",
                "src/A/Client.g.cs": "class G { AgentResponse r; }",
            })

            satirlar = kapsama.temas_uret(kok).splitlines()

        veri = [s for s in satirlar if not s.startswith("#")]
        self.assertEqual(veri[0], "bolum\tdurum\ttakip\tyetenek\tsrc\ttests\tsamples")
        self.assertEqual(veri[1].split("\t")[4:], ["ChatClientAgent", "AgentResponse", "-"])
        self.assertEqual(veri[2].split("\t")[4:], ["-", "MessageInjectingChatClient", "-"])

    def test_a_row_without_tokens_is_marked_unmeasured_not_without_contact(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            kok = pathlib.Path(d)
            self.kur(kok, {})
            (kok / "docs" / "MAF-KAPSAMA.md").write_text(matris(satir("🟡", "Anthropic kısayolları")), encoding="utf-8")

            veri = [s for s in kapsama.temas_uret(kok).splitlines() if not s.startswith("#")]

        self.assertEqual(veri[1].split("\t")[4:], ["?", "?", "?"])

    def test_table_is_byte_identical_across_runs_and_ignores_counts(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            kok = pathlib.Path(d)
            self.kur(kok, {"src/A/Agent.cs": "class X { ChatClientAgent a; }"})
            ilk = kapsama.temas_uret(kok)
            (kok / "src" / "A" / "Second.cs").write_text("class Y { ChatClientAgent b; }", encoding="utf-8")
            ikinci = kapsama.temas_uret(kok)

        self.assertEqual(ilk, ikinci)

    def test_an_unreadable_source_file_is_an_error(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            kok = pathlib.Path(d)
            self.kur(kok, {})
            (kok / "src").mkdir(exist_ok=True)
            (kok / "src" / "Bad.cs").write_bytes(b"\xff\xfe\xfa not utf-8")

            with self.assertRaises(UnicodeDecodeError):
                kapsama.temas_uret(kok)

    def test_the_committed_table_is_fresh(self) -> None:
        self.assertEqual(kapsama.TEMAS.read_text(encoding="utf-8"), kapsama.temas_uret())


class CarpiCirciriTests(unittest.TestCase):
    def denetle(self, *takipler: str, taban: int) -> list[str]:
        with tempfile.TemporaryDirectory() as d:
            kok = pathlib.Path(d)
            (kok / "docs").mkdir()
            (kok / "docs" / "MAF-KAPSAMA.md").write_text(
                matris(*(satir("❌", f"`Tip{i}`", t) for i, t in enumerate(takipler))), encoding="utf-8")
            return kapsama.carpi_denetle(kok, taban=taban)

    def test_equal_count_is_clean(self) -> None:
        self.assertEqual(self.denetle("✗", "✗", "✓", taban=2), [])

    def test_a_new_cross_row_is_a_finding(self) -> None:
        bulgular = self.denetle("✗", "✗", "✗", taban=2)

        self.assertEqual(len(bulgular), 1)
        self.assertIn("arttı", bulgular[0])

    def test_a_stale_baseline_is_a_finding(self) -> None:
        bulgular = self.denetle("✗", "✓", taban=2)

        self.assertEqual(len(bulgular), 1)
        self.assertIn("1", bulgular[0])

    def test_cross_plus_rows_do_not_count(self) -> None:
        self.assertEqual(self.denetle("✗", "✗", "✗⁺", "✗⁺", taban=2), [])

    def test_a_cross_with_a_suffix_still_counts(self) -> None:
        self.assertEqual(self.denetle("✗", "✗ F-600", taban=2), [])

    def test_the_real_matrix_matches_the_baseline(self) -> None:
        self.assertEqual(kapsama.carpi_denetle(), [])


DUMP_ESKI = """\
########## Microsoft.Agents.AI  (3 public tip) ##########

>>> Microsoft.Agents.AI.AIAgent   base: Object
    virtual String Name { get; }
    Task<AgentResponse> RunAsync(String message)

>>> Microsoft.Agents.AI.ChatClientAgentOptions   base: Object
    prop Boolean AllowConcurrentInvocation { get; set; }
    prop String Name { get; set; }

>>> Microsoft.Agents.AI.Gone   base: Object
    ctor()
"""

DUMP_YENI = """\
########## Microsoft.Agents.AI  (3 public tip) ##########

>>> Microsoft.Agents.AI.AIAgent   base: Object
    virtual String Name { get; }
    Task<AgentResponse> RunAsync(String message)
    prop Boolean AllowConcurrentInvocation { get; set; }

>>> Microsoft.Agents.AI.ChatClientAgentOptions   base: Object
    prop String Name { get; set; }

>>> Microsoft.Agents.AI.New   base: Object
    ctor()
"""


class FarkTests(unittest.TestCase):
    def test_a_member_moved_between_types_shows_in_both(self) -> None:
        fark = kapsama.fark(kapsama.dump_oku(DUMP_ESKI), kapsama.dump_oku(DUMP_YENI))

        uye = "prop Boolean AllowConcurrentInvocation { get; set; }"
        self.assertEqual(fark.eklenen_uyeler, {"Microsoft.Agents.AI.AIAgent": [uye]})
        self.assertEqual(fark.kaldirilan_uyeler, {"Microsoft.Agents.AI.ChatClientAgentOptions": [uye]})
        self.assertEqual(fark.eklenen_tipler, ["Microsoft.Agents.AI.New"])
        self.assertEqual(fark.kaldirilan_tipler, ["Microsoft.Agents.AI.Gone"])

    def test_a_base_or_sealed_change_is_a_breaking_finding(self) -> None:
        eski = ">>> Microsoft.Extensions.AI.FailoverChatClient   base: DelegatingChatClient   [abstract]\n    ctor()\n"
        yeni = ">>> Microsoft.Extensions.AI.FailoverChatClient   base: Object   [sealed]\n    ctor()\n"

        sonuc = kapsama.fark(kapsama.dump_oku(eski), kapsama.dump_oku(yeni))

        self.assertEqual(sonuc.degisen_basliklar, {
            "Microsoft.Extensions.AI.FailoverChatClient": ("base: DelegatingChatClient [abstract]", "base: Object [sealed]"),
        })
        self.assertTrue(sonuc.kirici_olabilir)

    def test_an_assembly_that_did_not_load_is_an_error(self) -> None:
        with self.assertRaisesRegex(ValueError, "yuklenemedi"):
            kapsama.dump_oku(DUMP_ESKI + "!! yuklenemedi: Microsoft.Agents.AI.Workflows\n")

    def test_an_empty_dump_is_an_error(self) -> None:
        with self.assertRaisesRegex(ValueError, ">>>"):
            kapsama.dump_oku("########## nothing ##########\n")

    def test_exit_code_is_1_on_removal_and_0_on_additions_only(self) -> None:
        with tempfile.TemporaryDirectory() as d:
            eski = pathlib.Path(d) / "eski.txt"
            yeni = pathlib.Path(d) / "yeni.txt"
            eski.write_text(DUMP_ESKI, encoding="utf-8")
            yeni.write_text(DUMP_YENI, encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()) as cikti:
                kaldirmali = kapsama.main(["fark", str(eski), str(yeni)])
            yalniz_ekleme = DUMP_ESKI + "\n>>> Microsoft.Agents.AI.Extra   base: Object\n    ctor()\n"
            yeni.write_text(yalniz_ekleme, encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()):
                eklemeli = kapsama.main(["fark", str(eski), str(yeni)])

        self.assertEqual(kaldirmali, 1)
        self.assertIn("Microsoft.Agents.AI.Gone", cikti.getvalue())
        self.assertEqual(eklemeli, 0)


if __name__ == "__main__":
    unittest.main()
