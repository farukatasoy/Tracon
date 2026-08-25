#!/usr/bin/env python3
"""Tests for the single validation gate runner."""
from __future__ import annotations

import importlib.util
import os
import pathlib
import sys
import tempfile
import unittest
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("kapi", ROOT / "scripts" / "kapi.py")
kapi = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kapi
spec.loader.exec_module(kapi)


class KapiTestleri(unittest.TestCase):
    def test_first_failure_stops_pahali_kapilari(self):
        commands = [kapi.Command(("first",)), kapi.Command(("second",))]
        runner = mock.Mock(side_effect=[mock.Mock(returncode=1), mock.Mock(returncode=0)])
        with tempfile.TemporaryDirectory() as directory:
            result = kapi.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 1)
        self.assertEqual(runner.call_count, 1)

    def test_mtp_filtresi_filter_class_uretir(self):
        command = kapi.test_command("AgentPrism.Core.UnitTests", ["*Capability*"])

        self.assertIn("--filter-class", command.args)
        self.assertNotIn("--filter", command.args)
        self.assertTrue(command.args[0].endswith("/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests"))

    def test_sync_taramasi_dosya_ve_dizin_kopyasini_bulur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "tests").mkdir()
            (root / "tests" / "Example 2.cs").write_text("", encoding="utf-8")
            (root / "tests" / "resources 2").mkdir()

            found = kapi.find_sync_copies(root)

        self.assertEqual({path.name for path in found}, {"Example 2.cs", "resources 2"})

    def test_secret_taramasi_deger_satirini_bulur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "sample.txt").write_text(
                "Pass" + "word=not-a-real-secret-value\n", encoding="utf-8")

            found = kapi.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertIn("sample.txt:1", found[0])

    def test_harita_eksigi_tam_test_kosumuna_duser(self):
        projects, full = kapi.affected_test_projects(["src/Unknown.Package/Thing.cs"])

        self.assertEqual(projects, [])
        self.assertTrue(full)

    def test_frontend_degisikligi_ic_dongude_frontendi_acar(self):
        self.assertTrue(kapi.frontend_changed(["src/AgentPrism.UI/frontend/src/app.tsx"]))
        self.assertFalse(kapi.frontend_changed(["src/AgentPrism.Core/Thing.cs"]))

    def test_kapanis_komutlari_dort_net_kapisini_tasir(self):
        commands = kapi.closing_commands("abc123", site=False)
        rendered = [command.display for command in commands]

        self.assertIn("dotnet build AgentPrism.slnx -c Release", rendered)
        self.assertIn("dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1", rendered)
        self.assertIn("dotnet pack AgentPrism.slnx -c Release --no-build", rendered)
        self.assertIn("dotnet format AgentPrism.slnx --verify-no-changes --no-restore", rendered)

    def test_tam_test_kosumu_kaynak_cekismesini_sinirlar(self):
        commands = kapi.closing_commands("abc123", site=False)
        rendered = [command.display for command in commands]

        self.assertIn(
            "dotnet test AgentPrism.slnx -c Release --no-build -maxcpucount:1",
            rendered)

    def test_dry_run_komut_calistirmaz(self):
        runner = mock.Mock()
        result = kapi.run_commands(
            "test", [kapi.Command(("would-not-run",))], runner=runner, dry_run=True)

        self.assertEqual(result, 0)
        runner.assert_not_called()

    def test_komut_bulunamazsa_127_ile_durur(self):
        commands = [kapi.Command(("dotnet",)), kapi.Command(("second",))]
        runner = mock.Mock(side_effect=FileNotFoundError(2, "No such file or directory", "dotnet"))
        with tempfile.TemporaryDirectory() as directory:
            result = kapi.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 127)
        self.assertEqual(runner.call_count, 1)

    def test_alt_sistem_hatasi_126_ile_durur(self):
        commands = [kapi.Command(("dotnet",))]
        runner = mock.Mock(side_effect=OSError("boru kırıldı"))
        with tempfile.TemporaryDirectory() as directory:
            result = kapi.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 126)

    def test_git_yoksa_traceback_yerine_none_doner(self):
        """Faz 91 denetimi: `_git()` `subprocess.run`'ın FileNotFoundError'ını
        yakalamıyordu; git PATH'te yoksa `changed_paths()` traceback ile çöküyordu."""
        with mock.patch("subprocess.run", side_effect=FileNotFoundError("git")):
            self.assertIsNone(kapi.changed_paths())

    def test_uygulanmis_migration_git_tabanıyla_ayni_olmalidir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            migration = root / "src" / "Example" / "Migrations" / "0001_initial.sql"
            migration.parent.mkdir(parents=True)
            original = b"CREATE TABLE example ();\n"
            migration.write_bytes(original)
            source = lambda commit, relative: original if (commit, relative) == ("abcdef1", migration.relative_to(root).as_posix()) else None

            self.assertEqual(
                kapi.migration_integrity_violations(
                    root,
                    baseline_commit="abcdef1",
                    source_commits={},
                    source_reader=source),
                [])

            migration.write_text("CREATE TABLE changed_example ();\n", encoding="utf-8")

            violations = kapi.migration_integrity_violations(
                root,
                baseline_commit="abcdef1",
                source_commits={},
                source_reader=source)

        self.assertEqual(len(violations), 1)
        self.assertIn("0001_initial.sql", violations[0])
        self.assertIn("git tabanı", violations[0])

    def test_manifesti_değiştirmek_uygulanmis_migration_değişikliğini_onaylayamaz(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            migration = root / "src" / "Example" / "Migrations" / "0001_initial.sql"
            migration.parent.mkdir(parents=True)
            original = b"CREATE TABLE example ();\n"
            migration.write_text("CREATE TABLE changed_example ();\n", encoding="utf-8")
            source = lambda commit, relative: original if (commit, relative) == ("abcdef1", migration.relative_to(root).as_posix()) else None

            violations = kapi.migration_integrity_violations(
                root,
                baseline_commit="abcdef1",
                source_commits={},
                source_reader=source)

        self.assertEqual(len(violations), 1)
        self.assertIn("git tabanı", violations[0])


class YayinTestleri(unittest.TestCase):
    """`scripts/kapi.py yayin` (Faz 97, 97.2) - saf Python mantığı. `dotnet
    pack`'e ihtiyaç duyan uçtan uca davranış `ReleaseArtifactTests.cs`'te ve bu
    fazın manuel kabul case'lerinde (MT-PKG-097..100)."""

    def _write_csproj(self, root: pathlib.Path, project_id: str, body: str = "") -> None:
        directory = root / "src" / project_id
        directory.mkdir(parents=True)
        (directory / f"{project_id}.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk">\n{body}\n</Project>\n', encoding="utf-8")

    def test_paketlenebilir_proje_kimlikleri_isPackable_false_olani_disler(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "AgentPrism.Core")
            self._write_csproj(root, "AgentPrism.Generators", "<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>")

            self.assertEqual(kapi.packable_project_ids(root), ["AgentPrism.Core"])

    def test_paket_profili_arac_icerik_meta_ve_kutuphaneyi_ayirir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "AgentPrism.Cli", "<PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup>")
            self._write_csproj(
                root, "AgentPrism.Templates",
                "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput><PackageType>Template</PackageType></PropertyGroup>")
            self._write_csproj(root, "AgentPrism", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")
            self._write_csproj(root, "AgentPrism.Core")

            self.assertEqual(kapi._package_profile(root, "AgentPrism.Cli"), "tool")
            self.assertEqual(kapi._package_profile(root, "AgentPrism.Templates"), "content")
            self.assertEqual(kapi._package_profile(root, "AgentPrism"), "meta")
            self.assertEqual(kapi._package_profile(root, "AgentPrism.Core"), "library")

    def test_kendi_surumunu_adlandirma_baska_paketi_karistirmiyor(self):
        """'AgentPrism.' önekiyle başlayan başka bir paketin dosyasını
        ("AgentPrism.Core...") kendi paketiymiş gibi almamalı."""
        self.assertTrue(kapi._names_own_version("AgentPrism", "AgentPrism.1.0.0-preview.1.nupkg"))
        self.assertFalse(kapi._names_own_version("AgentPrism", "AgentPrism.Core.1.0.0-preview.1.nupkg"))
        self.assertTrue(kapi._names_own_version("AgentPrism.Core", "AgentPrism.Core.1.0.0-preview.1.nupkg"))

    def test_surum_istenmisse_tam_dosya_adi_aranir(self):
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)
            (release_dir / "AgentPrism.Core.1.0.0-preview.1.nupkg").write_bytes(b"")

            found = kapi._resolve_nupkg(release_dir, "AgentPrism.Core", "1.0.0-preview.1")
            missing = kapi._resolve_nupkg(release_dir, "AgentPrism.Core", "1.0.0-preview.2")

        self.assertIsNotNone(found)
        self.assertIsNone(missing)

    def test_surum_istenmemisse_en_son_yazilan_kendi_dosyasi_secilir(self):
        """Faz 97 denetimi: eski bir 'en son yazılan' seçimi, ÖNCEKİ bir
        --surum koşumundan kalan bayat dosyayı seçebiliyordu, çünkü artımlı
        `dotnet pack` değişmeyen bir projenin çıktısını yeniden üretmeyebilir.
        `_clean_stale_packages` bu testin varsaydığı ön koşulu sağlar; burada
        yalnız kalan iki adaydan DOĞRU (en yeni) olanın seçildiği ölçülüyor."""
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)
            old = release_dir / "AgentPrism.Core.1.0.0-preview.1.nupkg"
            new = release_dir / "AgentPrism.Core.0.0.0-preview.0.400.nupkg"
            old.write_bytes(b"")
            new.write_bytes(b"")
            os.utime(old, (1, 1))
            os.utime(new, (2, 2))

            found = kapi._resolve_nupkg(release_dir, "AgentPrism.Core", None)

        self.assertEqual(found, new)

    def test_hedef_frameworkler_tekil_override_okur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "AgentPrism.Testing", "<PropertyGroup><TargetFrameworks>net10.0</TargetFrameworks></PropertyGroup>")
            self._write_csproj(root, "AgentPrism.Core")

            self.assertEqual(kapi._target_frameworks(root, "AgentPrism.Testing"), ("net10.0",))
            self.assertEqual(kapi._target_frameworks(root, "AgentPrism.Core"), ("net8.0", "net9.0", "net10.0"))

    def test_bayat_paketler_yalniz_kendi_kimligi_icin_temizlenir(self):
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)
            (release_dir / "AgentPrism.Core.0.0.0-preview.0.1.nupkg").write_bytes(b"")
            (release_dir / "AgentPrism.Core.0.0.0-preview.0.1.snupkg").write_bytes(b"")
            (release_dir / "AgentPrism.Abstractions.0.0.0-preview.0.1.nupkg").write_bytes(b"")

            kapi._clean_stale_packages(release_dir, ["AgentPrism.Core"])

            remaining = {path.name for path in release_dir.iterdir()}

        self.assertEqual(remaining, {"AgentPrism.Abstractions.0.0.0-preview.0.1.nupkg"})


if __name__ == "__main__":
    unittest.main()
