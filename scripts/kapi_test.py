#!/usr/bin/env python3
"""Tests for the single validation gate runner."""
from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import os
import pathlib
import sys
import tempfile
import time
import unittest
import zipfile
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("kapi", ROOT / "scripts" / "kapi.py")
kapi = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kapi
spec.loader.exec_module(kapi)


class KapiTestleri(unittest.TestCase):
    def test_git_tum_metin_dosyalarini_lf_olarak_checkout_eder(self):
        attributes = (ROOT / ".gitattributes").read_text(encoding="utf-8")

        self.assertIn("* text=auto eol=lf", attributes.splitlines())

    def test_first_failure_stops_pahali_kapilari(self):
        commands = [kapi.Command(("first",)), kapi.Command(("second",))]
        runner = mock.Mock(side_effect=[mock.Mock(returncode=1), mock.Mock(returncode=0)])
        with tempfile.TemporaryDirectory() as directory:
            result = kapi.run_commands(
                "test", commands, runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 1)
        self.assertEqual(runner.call_count, 1)

    def test_sinif_bayragi_tekrarlaninca_desenler_birikir(self):
        """Faz 187: `--sinif A --sinif B` yalnız B'yi koşuyordu (argparse ikinciyi
        birincinin yerine yazar); iki mimari testinin ikisi de koştu sanıldı."""
        with mock.patch.object(kapi, "test_commands", side_effect=ValueError("dur")) as commands:
            with contextlib.redirect_stdout(io.StringIO()):
                kapi.main(["test", "--proje", "Tracon.Core.UnitTests", "--sinif", "*A*", "--sinif", "*B*", "*C*"])

        self.assertEqual(commands.call_args.args[1], ["*A*", "*B*", "*C*"])

    def test_mtp_filtresi_filter_class_uretir(self):
        commands = kapi.test_commands("Tracon.Generators.UnitTests", ["*Capability*"], environ={})

        self.assertEqual(len(commands), 1)
        self.assertIn("--filter-class", commands[0].args)
        self.assertNotIn("--filter", commands[0].args)
        # Ayraçla DEĞİL parçayla karşılaştırılır: Windows'ta `pathlib` ters eğik
        # çizgi üretir, `endswith("/...")` orada HER ZAMAN False döner ve kapı
        # yalnız `windows-latest` ayağında kırmızı olur (üretim kodu doğruydu).
        parts = pathlib.PurePath(commands[0].args[0]).parts[-3:]
        self.assertEqual(
            parts, ("Tracon.Generators.UnitTests", "release", "Tracon.Generators.UnitTests"))

    def test_coklu_tfm_projesi_her_bacagi_ayri_kosar(self):
        # Faz 183: cok hedefli projenin ciktisi release/ DEGIL release_<tfm>/'dir.
        commands = kapi.test_commands("Tracon.Core.UnitTests", ["*Capability*"], environ={})

        self.assertEqual(
            [pathlib.PurePath(command.args[0]).parts[-2] for command in commands],
            ["release_net8.0", "release_net9.0", "release_net10.0"])

    def test_tfm_secimi_tek_bacagi_kosar(self):
        commands = kapi.test_commands("Tracon.Core.UnitTests", ["*X*"], "net8.0", environ={})

        self.assertEqual(len(commands), 1)
        self.assertEqual(
            pathlib.PurePath(commands[0].args[0]).parts[-3:],
            ("Tracon.Core.UnitTests", "release_net8.0", "Tracon.Core.UnitTests"))

    def test_derlenmeyen_tfm_istenirse_sessiz_gecmez(self):
        with self.assertRaises(ValueError):
            kapi.test_commands("Tracon.Core.UnitTests", ["*X*"], "net7.0", environ={})
        with self.assertRaises(ValueError):
            # Tek hedefli proje yalniz net10.0 derlenir; net8.0 istemek bayat
            # ya da var olmayan bir ikiliyi kosturmak olurdu.
            kapi.test_commands("Tracon.Generators.UnitTests", ["*X*"], "net8.0", environ={})

    def test_temsilci_kume_ve_tfm_listesi_props_dosyasindan_okunur(self):
        self.assertEqual(kapi.test_target_frameworks(environ={}), ("net8.0", "net9.0", "net10.0"))
        self.assertEqual(kapi.single_test_target_framework(), "net10.0")
        self.assertEqual(
            sorted(kapi.multi_target_test_projects()),
            sorted([
                "Tracon.Core.UnitTests",
                "Tracon.Sql.Shared.UnitTests",
                "Tracon.OpenAI.UnitTests",
                "Tracon.Anthropic.UnitTests",
                "Tracon.Google.UnitTests",
                "Tracon.Azure.UnitTests",
                "Tracon.Sqlite.IntegrationTests",
                "Tracon.Testing.Contracts.Xunit.UnitTests",
            ]))
        # Kume disindaki her ad gercek bir test projesidir - yazim hatasi
        # MSBuild'de sessizce "kosul yanlis" olur ve proje net10'da kalir.
        for project in kapi.multi_target_test_projects():
            csproj = ROOT / "tests" / project / f"{project}.csproj"
            self.assertTrue(csproj.exists(), project)
            # Denetim bulgusu (Faz 183): csproj govdesi props'tan SONRA okunur;
            # oraya yazilan tekil <TargetFramework> projeyi sessizce net10'a
            # indirir ve runtime nobetcisi net10'da yine gecer.
            self.assertNotIn("<TargetFramework", csproj.read_text(encoding="utf-8"), project)

    def test_tfm_listesi_ortam_degiskeniyle_daralir(self):
        # windows-latest bacagi MSBuild'e ayni adli ortam degiskenini verir;
        # betik de ayni ciktilari aramalidir.
        self.assertEqual(
            kapi.test_target_frameworks(environ={"TraconTestTargetFrameworks": "net10.0"}), ("net10.0",))
        self.assertEqual(
            [path.name for path in kapi.test_output_directories(
                "Tracon.Core.UnitTests", environ={"TraconTestTargetFrameworks": "net10.0"})],
            ["release_net10.0"])

    def test_bayat_tek_hedef_klasoru_secilmez(self):
        # Proje cok hedefli olduktan sonra eski release/ klasoru diskte kalir;
        # cozum DISKTEN degil BEYANDAN yapilir.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            (kok / "tests").mkdir()
            (kok / "tests" / "Directory.Build.props").write_text(
                (ROOT / "tests" / "Directory.Build.props").read_text(encoding="utf-8"), encoding="utf-8")
            (kok / "artifacts" / "bin" / "Tracon.Core.UnitTests" / "release").mkdir(parents=True)

            outputs = kapi.test_output_directories("Tracon.Core.UnitTests", root=kok, environ={})

        self.assertNotIn("release", [path.name for path in outputs])

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

            found, skipped = kapi.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertIn("sample.txt:1", found[0])
        self.assertEqual(skipped, 0)

    def test_bayat_dokuman_referansi_bulunur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "docs").mkdir()
            (root / "docs" / "VAR.md").write_text("", encoding="utf-8")
            (root / "src").mkdir()
            # Parcali yazilir: bu dosya `scripts/` altindadir ve kapinin KENDI
            # taramasina girer; duz yazilan bir fixture yolu kapiyi kendi test
            # kaynagi uzerinde kirmizi yapardi (`find_secrets` testindeki ayni
            # sebep, ayni yontem).
            missing = "docs/" + "YOK.md"
            present = "docs/" + "VAR.md"
            (root / "src" / "Thing.cs").write_text(
                f"// Bkz. {missing} ve {present}\n", encoding="utf-8")

            found = kapi.find_stale_doc_references(root)

        self.assertEqual(len(found), 1)
        self.assertIn(missing, found[0])

    def test_site_yolu_bayat_referans_sayilmaz(self):
        # `docs-site/src/content/docs/...` bir SITE yoludur, `docs/` agacina ait
        # degildir. Ayrilmazsa kapi 40+ yanlis pozitif uretir ve kapatilir.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "src").mkdir()
            (root / "src" / "Thing.cs").write_text(
                "/// <c>docs-site/src/content/docs/capabilities.md</c>\n", encoding="utf-8")

            self.assertEqual(kapi.find_stale_doc_references(root), [])

    def test_yer_tutucu_referansi_bayat_sayilmaz(self):
        # Arsivleme scripti ve faz sablonu ornek yol yazar; bunlar hicbir zaman
        # var olmaz ve bir kusur degildir.
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "scripts").mkdir()
            (root / "scripts" / "ornek.py").write_text(
                "# docs/X.md -> docs/arsiv/Y.md; sablon: docs/NN-BUYUK-HARFLI-AD.md\n",
                encoding="utf-8")

            self.assertEqual(kapi.find_stale_doc_references(root), [])

    def test_arsivlenen_faz_referansi_repoda_kalmaz(self):
        # SINIF KAPISI: bu repoda gercek olan tarama. Bir faz arsivlendiginde
        # `docs/NN-AD.md` -> `docs/arsiv/fazlar/NN-AD.md` olur; `docs/` disindaki
        # referanslar bugune kadar guncellenmiyordu (43 bayat referans olculdu).
        self.assertEqual(kapi.find_stale_doc_references(), [])

    def test_harita_eksigi_tam_test_kosumuna_duser(self):
        projects, full = kapi.affected_test_projects(["src/Unknown.Package/Thing.cs"])

        self.assertEqual(projects, ["Tracon.Generators.UnitTests"])
        self.assertTrue(full)

    def test_her_src_degisikligi_ornek_derleyen_projeyi_secer(self):
        # KACIS 9433efe4: src/Tracon.Core'a eklenen bir XML <example>
        # blogu Tracon.Generators.UnitTests'i DERLENMEZ hale getirdi;
        # ic dongu Core.UnitTests + AspNetCore.FunctionalTests seciyordu ve
        # gercekte dusen projeyi HIC kosmuyordu. ExampleExtractor
        # src/**/*.cs'in tamamini okur, yalniz Generators'i degil.
        projects, full = kapi.affected_test_projects(
            ["src/Tracon.Core/Builder/ITraconBuilder.cs"])

        self.assertIn("Tracon.Generators.UnitTests", projects)
        self.assertFalse(full)

    def test_paylasimli_saglayici_kaynagi_dort_saglayici_projesini_secer(self):
        # Faz 181: src/Tracon.Providers.Shared bir paket degildir; haritada
        # olmasa her degisiklik tam kosuma duserdi. Etki alani dort projedir.
        for path in ("src/Tracon.Providers.Shared/ModelProviderCore.cs",
                     "tests/Shared/Providers/ModelProviderCoreTests.cs"):
            projects, full = kapi.affected_test_projects([path])

            for project in kapi.PROVIDER_TEST_PROJECTS:
                self.assertIn(project, projects, path)
            self.assertFalse(full, path)

    def test_sql_saglayicisi_ortak_parite_kapilarini_secer(self):
        # Kayit paritesi (SqlProviderRegistrationParityTests), migration
        # paritesi ve SQL metni snapshot'i Tracon.Sql.Shared.UnitTests'te
        # yasar. Yalniz bir Use* dosyasina dokunan degisiklikte ic dongu
        # bugune kadar yalniz o saglayicinin IntegrationTests projesini
        # seciyordu; unutulan bir kayit satiri orada sessiz kalir.
        for package in ("Tracon.PostgreSql", "Tracon.SqlServer", "Tracon.Sqlite"):
            path = f"src/{package}/{package.replace('.', '')}BuilderExtensions.cs"
            projects, full = kapi.affected_test_projects([path])

            self.assertIn("Tracon.Sql.Shared.UnitTests", projects, path)
            self.assertIn(f"{package}.IntegrationTests", projects, path)
            self.assertFalse(full, path)

    def test_cekirdek_degisikligi_sql_kayit_kapisini_da_secer(self):
        # Kayit kapisinin yakaladigi iki vaka yalniz Core/Abstractions'ta
        # dogar: uc saglayicida birden unutulan yeni bir store sozlesmesi ve
        # yalniz AddTracon'a eklenen bir Auditing* dekoratoru. Ic dongu o
        # degisiklikte kapiyi kosmazsa bulgu ancak tam kosumda gorunur.
        for path in ("src/Tracon.Core/TraconServiceCollectionExtensions.Registration.Storage.cs",
                     "src/Tracon.Abstractions/Runs/IRunStore.cs"):
            projects, full = kapi.affected_test_projects([path])

            self.assertIn("Tracon.Sql.Shared.UnitTests", projects, path)
            self.assertFalse(full, path)

    def test_test_agaci_kok_dosyasi_tam_kosum_ister(self):
        # tests/Directory.Build.props HER test projesine ulasir. Faz 183'e
        # kadar bu yol HICBIR proje secmiyordu ve ic dongu sessizce bos kalirdi.
        projects, full = kapi.affected_test_projects(["tests/Directory.Build.props"])

        self.assertTrue(full)

    def test_tfm_nobetcisi_yalniz_temsilci_kumeyi_secer(self):
        projects, full = kapi.affected_test_projects(
            ["tests/Shared/TargetFramework/RuntimeMatchesTargetFrameworkTests.cs"])

        self.assertEqual(sorted(projects), sorted(kapi.multi_target_test_projects()))
        self.assertFalse(full)

    def test_cok_hedefli_projeye_ulasan_test_komutu_taninir(self):
        self.assertTrue(kapi.runs_multi_target_tests([kapi.full_solution_test_command()]))
        self.assertTrue(kapi.runs_multi_target_tests(
            [kapi.Command(("dotnet", "test", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))]))
        self.assertFalse(kapi.runs_multi_target_tests(
            [kapi.Command(("dotnet", "test", "tests/Tracon.Generators.UnitTests/Tracon.Generators.UnitTests.csproj"))]))
        self.assertFalse(kapi.runs_multi_target_tests([kapi.Command(("dotnet", "build", "Tracon.slnx"))]))

    def test_eksik_runtime_dotnet_root_altindan_bulunur(self):
        # Olculdu (Faz 183): test apphost'u runtime'i DOTNET_ROOT'tan cozer,
        # PATH'teki muxer'dan degil.
        with tempfile.TemporaryDirectory() as directory:
            shared = pathlib.Path(directory) / "shared" / "Microsoft.NETCore.App"
            for version in ("9.0.10", "10.0.0"):
                (shared / version).mkdir(parents=True)
            runner = mock.Mock()

            missing = kapi.missing_test_runtimes(
                ("net8.0", "net9.0", "net10.0"), environ={"DOTNET_ROOT": directory}, runner=runner)

        self.assertEqual(missing, ["net8.0"])
        runner.assert_not_called()

    def test_mimariye_ozel_dotnet_root_once_gelir(self):
        # Apphost DOTNET_ROOT_<ARCH>'i DOTNET_ROOT'tan ONCE okur.
        environ = {"DOTNET_ROOT": "/genel", "DOTNET_ROOT_ARM64": "/arm64"}

        self.assertEqual(kapi._apphost_dotnet_root(environ, "arm64"), "/arm64")
        self.assertEqual(kapi._apphost_dotnet_root(environ, "x86_64"), "/genel")
        self.assertEqual(kapi._apphost_dotnet_root({}, "arm64"), "")

    def test_eksik_runtime_global_kurulum_konumundan_bulunur(self):
        # Denetim bulgusu (Faz 183): DOTNET_ROOT yokken apphost PATH'teki
        # muxer'i DEGIL global kurulumu okur. PATH'e net8'li ozel bir SDK
        # konsa bile on kontrol global kokte net8 yoksa durdurmalidir.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            (kok / "etc").mkdir()
            (kok / "etc" / "install_location").write_text(str(kok / "x64") + "\n", encoding="utf-8")
            (kok / "etc" / "install_location_arm64").write_text(str(kok / "global") + "\n", encoding="utf-8")
            for version in ("9.0.10", "10.0.0"):
                (kok / "global" / "shared" / "Microsoft.NETCore.App" / version).mkdir(parents=True)
            runner = mock.Mock()

            missing = kapi.missing_test_runtimes(
                ("net8.0", "net10.0"), environ={}, runner=runner,
                system="Darwin", machine="arm64", install_location_dir=kok / "etc")

        self.assertEqual(missing, ["net8.0"])
        runner.assert_not_called()

    def test_global_kurulum_dosyasi_yoksa_varsayilan_kok_kullanilir(self):
        with tempfile.TemporaryDirectory() as directory:
            yok = pathlib.Path(directory) / "etc"

            self.assertEqual(kapi._global_dotnet_root("Linux", "x86_64", yok), "/usr/share/dotnet")
            self.assertEqual(kapi._global_dotnet_root("Darwin", "arm64", yok), "/usr/local/share/dotnet")
            self.assertIsNone(kapi._global_dotnet_root("Windows", "amd64", yok))

    def test_windowsta_list_runtimes_ciktisindan_bulunur(self):
        stdout = (
            "Microsoft.AspNetCore.App 8.0.31 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]\n"
            "Microsoft.NETCore.App 9.0.20 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n"
            "Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n")
        runner = mock.Mock(return_value=mock.Mock(returncode=0, stdout=stdout))

        with contextlib.redirect_stdout(io.StringIO()) as output:
            ok = kapi.require_test_runtimes(("net8.0", "net10.0"), environ={}, runner=runner, system="Windows")

        # ASP.NET Core 8 runtime'i NETCore 8 yerine SAYILMAZ.
        self.assertFalse(ok)
        self.assertIn("net8.0", output.getvalue())
        # Denetim bulgusu (Faz 183): ayrac ile DEGIL platformun kendi yol
        # bicimiyle karsilastirilir - windows-latest'te PurePath ters egik
        # cizgi uretir (test_mtp_filtresi_filter_class_uretir ile ayni sinif).
        self.assertIn(str(pathlib.PurePath("/usr/share/dotnet")), output.getvalue())

    def test_runtime_listesi_okunamazsa_engellemez(self):
        # dotnet yoksa asil komut zaten 127 ile durur; on kontrol ikinci bir
        # yaniltici hata uretmez. Global kok bulunamazsa da ayni kural.
        runner = mock.Mock(side_effect=FileNotFoundError("dotnet"))

        self.assertEqual(
            kapi.missing_test_runtimes(("net8.0",), environ={}, runner=runner, system="Windows"), [])
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(
                kapi.missing_test_runtimes(
                    ("net8.0",), environ={"DOTNET_ROOT": str(pathlib.Path(directory) / "yok")}, runner=runner),
                [])

    def test_diger_paylasimli_test_kaynagi_hala_tam_kosum_ister(self):
        projects, full = kapi.affected_test_projects(["tests/Shared/Infrastructure/ProcessRunner.cs"])

        self.assertTrue(full)

    def test_embedded_ornegi_kendi_test_projesini_secer(self):
        # KACIS a377106e: Faz 139 altinci genisleme noktasini ekledi,
        # samples/Tracon.Embedded geride kaldi. O gun bir samples/
        # degisikligi HICBIR test projesi secmiyordu; Embedded.Tests ise
        # ProjectReference ile tam o ornege bagli.
        projects, full = kapi.affected_test_projects(
            ["samples/Tracon.Embedded/Program.cs"])

        self.assertEqual(projects, ["Tracon.Embedded.Tests"])
        self.assertFalse(full)

    def test_yayin_ornekleri_sessiz_gecmez(self):
        # samples/Tracon.Samples.* cozumde DEGILDIR: hicbir kapanis
        # kosumu onlari kapsamaz, yalniz `kapi.py yayin`. Secim bos kalir
        # ama bu ARTIK sessiz degildir.
        with contextlib.redirect_stdout(io.StringIO()) as output:
            projects, full = kapi.affected_test_projects(
                ["samples/Tracon.Samples.CustomJobHandler/Program.cs"])

        self.assertEqual(projects, [])
        self.assertFalse(full)
        self.assertIn("kapi.py yayin", output.getvalue())

    def test_tam_kosum_komutu_trx_uretir(self):
        # ci.yml "Test et" adimlariyla AYNI kuyruk: dusen testin adi makine okunur olmadan
        # izole yeniden kosum yazilamaz.
        command = kapi.full_solution_test_command()

        self.assertEqual(command.args[-2:], ("--", "--report-trx"))

    def _trx(self, kok: pathlib.Path, proje: str, sonuclar: list[tuple[str, str]], cikti: str = "release") -> None:
        dizin = kok / "artifacts" / "bin" / proje / cikti / "TestResults"
        dizin.mkdir(parents=True, exist_ok=True)
        satirlar = "".join(
            f'<UnitTestResult testName="{ad}" outcome="{durum}" />' for ad, durum in sonuclar)
        (dizin / "r.trx").write_text(
            '<?xml version="1.0"?><TestRun xmlns='
            '"http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f"<Results>{satirlar}</Results></TestRun>",
            encoding="utf-8")

    def test_trx_yalniz_dusen_testi_verir(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._trx(kok, "Tracon.Ui.E2ETests", [
                ("Tracon.Ui.E2ETests.UiTests.Gecen", "Passed"),
                ("Tracon.Ui.E2ETests.UiTests.Dusen", "Failed"),
                ("Tracon.Ui.E2ETests.UiTests.Atlanan", "NotExecuted"),
            ])

            dusenler = kapi.failed_tests_from_trx(0, kok)

        self.assertEqual(
            dusenler, [kapi.FailedTest("Tracon.Ui.E2ETests", "release", "Tracon.Ui.E2ETests.UiTests.Dusen")])

    def test_coklu_tfm_trx_i_bacagiyla_bulunur(self):
        # Faz 183: yalniz release/ taransaydi net8.0 bacaginda dusen test
        # hic raporlanmaz, izole kosum da tetiklenmezdi.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._trx(kok, "Tracon.Core.UnitTests", [("N.S.Dusen", "Failed")], cikti="release_net8.0")
            self._trx(kok, "Tracon.Core.UnitTests", [("N.S.Dusen", "Passed")], cikti="release_net10.0")

            dusenler = kapi.failed_tests_from_trx(0, kok)

        self.assertEqual(dusenler, [kapi.FailedTest("Tracon.Core.UnitTests", "release_net8.0", "N.S.Dusen")])
        self.assertEqual(dusenler[0].label, "Tracon.Core.UnitTests (net8.0)")

    def test_bayat_trx_sayilmaz(self):
        # Onceki kosumun TRX'i bu kosumun dusen testi degildir.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._trx(kok, "Tracon.Core.UnitTests", [("X.Y.Eski", "Failed")])

            self.assertEqual(kapi.failed_tests_from_trx(time.time() + 60, kok), [])

    def test_izole_kosum_cikis_kodunu_degistirmez_ama_hukum_verir(self):
        # SINIF: "tam kosumda duser, izole gecer" YEDI kez kayda gecti ve her
        # seferinde ayirt etme elle yapildi (cogu kez TAM paketi ikinci kez
        # kosarak, ~9 dk). Kapi GEVSEMEZ: bu yalnizca bir sonraki adimi soyler.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            ikili = kok / "artifacts" / "bin" / "P" / "release"
            ikili.mkdir(parents=True)
            (ikili / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=0))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                hukumler = kapi.isolate_failed_tests(
                    [kapi.FailedTest("P", "release", "N.S.Dusen")], runner=runner, root=kok)

        self.assertEqual(hukumler, [("P", "N.S.Dusen", True)])
        self.assertIn("--filter-method", runner.call_args.args[0])
        self.assertIn("*Dusen*", runner.call_args.args[0])
        self.assertIn("izole GEÇTİ", output.getvalue())
        self.assertIn("TEKRAR koş", output.getvalue())

    def test_izole_kosum_dusen_bacagin_ikilisini_kosar(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            for cikti in ("release_net8.0", "release_net10.0"):
                ikili = kok / "artifacts" / "bin" / "P" / cikti
                ikili.mkdir(parents=True)
                (ikili / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                hukumler = kapi.isolate_failed_tests(
                    [kapi.FailedTest("P", "release_net8.0", "N.S.Dusen")], runner=runner, root=kok)

        self.assertEqual(pathlib.PurePath(runner.call_args.args[0][0]).parts[-2], "release_net8.0")
        self.assertEqual(hukumler, [("P (net8.0)", "N.S.Dusen", False)])
        self.assertIn("P (net8.0) · N.S.Dusen", output.getvalue())

    def test_izole_de_dusen_test_gercek_regresyondur(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            ikili = kok / "artifacts" / "bin" / "P" / "release"
            ikili.mkdir(parents=True)
            (ikili / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                hukumler = kapi.isolate_failed_tests(
                    [kapi.FailedTest("P", "release", "N.S.Dusen")], runner=runner, root=kok)

        self.assertEqual(hukumler, [("P", "N.S.Dusen", False)])
        self.assertIn("GERÇEK regresyon", output.getvalue())

    def test_frontend_degisikligi_ic_dongude_frontendi_acar(self):
        self.assertTrue(kapi.frontend_changed(["src/Tracon.UI/frontend/src/app.tsx"]))
        self.assertFalse(kapi.frontend_changed(["src/Tracon.Core/Thing.cs"]))

    def test_kapanis_komutlari_dort_net_kapisini_tasir(self):
        commands = kapi.closing_commands("abc123", site=False)
        rendered = [command.display for command in commands]

        self.assertIn("dotnet build Tracon.slnx -c Release", rendered)
        self.assertIn(
            "dotnet test Tracon.slnx -c Release --no-build -maxcpucount:2 -- --report-trx",
            rendered)
        # TraconSkipCleanWorkingTreeCheck (Faz 136): this pack validates the
        # packaging CONTRACT during iteration, not a release candidate - it must
        # not be blocked by the new dirty-tree gate the way `kapi.py yayin` is.
        self.assertIn(
            "dotnet pack Tracon.slnx -c Release --no-build -p:TraconSkipCleanWorkingTreeCheck=true",
            rendered)
        self.assertIn("dotnet format Tracon.slnx --verify-no-changes", rendered)

    def test_kapanis_performans_adimini_kosullu_ekler(self):
        with_it = [c.display for c in kapi.closing_commands("abc123", site=False, performance=True)]
        without_it = [c.display for c in kapi.closing_commands("abc123", site=False, performance=False)]

        self.assertIn("python3 scripts/kapi.py performans", with_it)
        self.assertNotIn("python3 scripts/kapi.py performans", without_it)

    def test_sicak_yol_degisikligi_performans_kapisini_tetikler(self):
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Core/Recording/RunEventWriter.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Sql.Shared/Stores/SqlRunStore.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["bench/Tracon.Benchmarks/Program.cs"]))
        self.assertFalse(kapi.performance_gate_triggered(["src/Tracon.Core/TraconOptions.cs"]))
        self.assertFalse(kapi.performance_gate_triggered([]))

    def test_tam_test_kosumu_kaynak_cekismesini_sinirlar(self):
        commands = kapi.closing_commands("abc123", site=False)
        rendered = [command.display for command in commands]

        self.assertIn(
            "dotnet test Tracon.slnx -c Release --no-build -maxcpucount:2 -- --report-trx",
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


class PerformansKapisiTestleri(unittest.TestCase):
    """`scripts/kapi.py performans` (Faz 116) - karşılaştırma ve taban çizgisi
    okuma mantığının SAF Python testleri. `dotnet run` gerektiren gerçek
    benchmark koşumu ve kasıtlı gerileme koşumu manuel kabul case'lerindedir
    (docs/manuel-test/36-GELISTIRME-KAPILARI.md)."""

    def test_artan_tahsis_kirmizi_olur_ve_metodu_bayti_adlandirir(self):
        baseline = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}
        current = {"A": {"allocatedBytes": 132, "meanNanoseconds": 1.0}}

        exit_code, messages = kapi.compare_allocations(baseline, current)

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("A" in message and "132" in message and "+32" in message for message in messages))

    def test_azalan_tahsis_kirmizi_olmaz_ama_guncelleme_uyarisi_verir(self):
        baseline = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}
        current = {"A": {"allocatedBytes": 80, "meanNanoseconds": 1.0}}

        exit_code, messages = kapi.compare_allocations(baseline, current)

        self.assertEqual(exit_code, 0)
        self.assertTrue(any("güncelle" in message for message in messages))

    def test_ayni_tahsis_gecer(self):
        baseline = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}
        current = {"A": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}

        exit_code, _messages = kapi.compare_allocations(baseline, current)

        self.assertEqual(exit_code, 0)

    def test_taban_cizgisinde_olmayan_benchmark_kirmizi_olur(self):
        exit_code, messages = kapi.compare_allocations({}, {"Yeni": {"allocatedBytes": 10, "meanNanoseconds": 1.0}})

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("Yeni" in message and "taban çizgisinde yok" in message for message in messages))

    def test_bu_kosumda_uretilmeyen_benchmark_kirmizi_olur(self):
        exit_code, messages = kapi.compare_allocations({"Eski": {"allocatedBytes": 10, "meanNanoseconds": 1.0}}, {})

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("Eski" in message and "üretilmedi" in message for message in messages))

    def test_eksik_taban_cizgisi_dosyasi_anlasilir_hata_verir(self):
        with tempfile.TemporaryDirectory() as directory:
            missing = pathlib.Path(directory) / "does-not-exist.json"

            with self.assertRaises(kapi.BaselineError):
                kapi.load_baseline(missing)

    def test_bozuk_taban_cizgisi_dosyasi_anlasilir_hata_verir(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "baseline.json"
            path.write_text("{ this is not valid json", encoding="utf-8")

            with self.assertRaises(kapi.BaselineError):
                kapi.load_baseline(path)

    def test_benchmarks_anahtari_eksik_taban_cizgisi_anlasilir_hata_verir(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "baseline.json"
            path.write_text('{"somethingElse": true}', encoding="utf-8")

            with self.assertRaises(kapi.BaselineError):
                kapi.load_baseline(path)

    def test_benchmarkdotnet_json_raporundan_tahsis_ve_sure_cikarilir(self):
        report = {
            "Benchmarks": [
                {
                    "FullName": "Tracon.Benchmarks.RunEventWriterBenchmarks.AppendEvent",
                    "Memory": {"BytesAllocatedPerOperation": 176},
                    "Statistics": {"Mean": 123.4},
                },
            ],
        }

        parsed = kapi.parse_benchmark_report(report)

        self.assertEqual(
            parsed["Tracon.Benchmarks.RunEventWriterBenchmarks.AppendEvent"],
            {"allocatedBytes": 176, "meanNanoseconds": 123.4})

    def test_birden_fazla_benchmark_sinifinin_raporu_birlesir(self):
        """BenchmarkDotNet HER benchmark SINIFI için ayrı bir *-report-full.json
        yazar, tüm koşum için tek dosya değil - ikisi de okunmalı."""
        with tempfile.TemporaryDirectory() as directory:
            report_dir = pathlib.Path(directory) / "results"
            report_dir.mkdir()
            (report_dir / "A-report-full.json").write_text(json.dumps({
                "Benchmarks": [{"FullName": "A.M", "Memory": {"BytesAllocatedPerOperation": 1}, "Statistics": {"Mean": 1.0}}],
            }), encoding="utf-8")
            (report_dir / "B-report-full.json").write_text(json.dumps({
                "Benchmarks": [{"FullName": "B.M", "Memory": {"BytesAllocatedPerOperation": 2}, "Statistics": {"Mean": 2.0}}],
            }), encoding="utf-8")

            found = kapi._benchmark_report_files(report_dir)

        self.assertEqual([path.name for path in found], ["A-report-full.json", "B-report-full.json"])

    def test_taban_cizgisi_yazma_ve_okuma_gidip_gelir(self):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / "baseline.json"
            benchmarks = {"A": {"allocatedBytes": 42, "meanNanoseconds": 3.5}}

            kapi.write_baseline(benchmarks, path)
            loaded = kapi.load_baseline(path)

        self.assertEqual(loaded["benchmarks"], benchmarks)

    def _write_report(self, report_dir: pathlib.Path, file_name: str, full_name: str, allocated_bytes: int) -> None:
        report_dir.mkdir(parents=True, exist_ok=True)
        (report_dir / file_name).write_text(json.dumps({
            "Benchmarks": [{
                "FullName": full_name,
                "Memory": {"BytesAllocatedPerOperation": allocated_bytes},
                "Statistics": {"Mean": 1.0},
            }],
        }), encoding="utf-8")

    def test_performans_kapisi_dotnet_run_basarisiz_olursa_onun_cikis_kodunu_doner(self):
        """Denetim bulgusu (Faz 116): orkestrasyon fonksiyonunun kendisi hiç
        test edilmiyordu, yalnız saf karşılaştırma mantığı. `runner` zaten
        enjekte edilebilir - `run_commands` testlerindeki desenin aynısı."""
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            kapi.write_baseline({"A": {"allocatedBytes": 1, "meanNanoseconds": 1.0}}, baseline_path)
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            exit_code = kapi.performance_gate(
                runner=runner,
                baseline_path=baseline_path,
                report_dir=pathlib.Path(directory) / "results")

        self.assertEqual(exit_code, 1)
        runner.assert_called_once()

    def test_performans_kapisi_rapor_uretilmezse_anlasilir_hata_verir(self):
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            kapi.write_baseline({"A": {"allocatedBytes": 1, "meanNanoseconds": 1.0}}, baseline_path)
            runner = mock.Mock(return_value=mock.Mock(returncode=0))

            exit_code = kapi.performance_gate(
                runner=runner,
                baseline_path=baseline_path,
                report_dir=pathlib.Path(directory) / "results")

        self.assertEqual(exit_code, 1)

    def test_performans_kapisi_ucdan_uca_karsilastirir_gercek_dotnet_calistirmadan(self):
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            report_dir = pathlib.Path(directory) / "results"
            kapi.write_baseline({"A.M": {"allocatedBytes": 100, "meanNanoseconds": 1.0}}, baseline_path)

            def fake_runner(*args, **kwargs):
                self._write_report(report_dir, "A-report-full.json", "A.M", 132)
                return mock.Mock(returncode=0)

            exit_code = kapi.performance_gate(
                runner=fake_runner, baseline_path=baseline_path, report_dir=report_dir)

        self.assertEqual(exit_code, 1)

    def test_performans_kapisi_guncelle_taban_cizgisini_yazar(self):
        with tempfile.TemporaryDirectory() as directory:
            baseline_path = pathlib.Path(directory) / "baseline.json"
            report_dir = pathlib.Path(directory) / "results"

            def fake_runner(*args, **kwargs):
                self._write_report(report_dir, "A-report-full.json", "A.M", 55)
                return mock.Mock(returncode=0)

            exit_code = kapi.performance_gate(
                update=True, runner=fake_runner, baseline_path=baseline_path, report_dir=report_dir)
            written = kapi.load_baseline(baseline_path)

        self.assertEqual(exit_code, 0)
        self.assertEqual(written["benchmarks"]["A.M"]["allocatedBytes"], 55)


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
            self._write_csproj(root, "Tracon.Core")
            self._write_csproj(root, "Tracon.Generators", "<PropertyGroup><IsPackable>false</IsPackable></PropertyGroup>")

            self.assertEqual(kapi.packable_project_ids(root), ["Tracon.Core"])

    def test_paket_profili_arac_icerik_meta_ve_kutuphaneyi_ayirir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Cli", "<PropertyGroup><PackAsTool>true</PackAsTool></PropertyGroup>")
            self._write_csproj(
                root, "Tracon.Templates",
                "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput><PackageType>Template</PackageType></PropertyGroup>")
            self._write_csproj(root, "Tracon", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")
            self._write_csproj(root, "Tracon.Core")

            self.assertEqual(kapi._package_profile(root, "Tracon.Cli"), "tool")
            self.assertEqual(kapi._package_profile(root, "Tracon.Templates"), "content")
            self.assertEqual(kapi._package_profile(root, "Tracon"), "meta")
            self.assertEqual(kapi._package_profile(root, "Tracon.Core"), "library")

    def test_kendi_surumunu_adlandirma_baska_paketi_karistirmiyor(self):
        """'Tracon.' önekiyle başlayan başka bir paketin dosyasını
        ("Tracon.Core...") kendi paketiymiş gibi almamalı."""
        self.assertTrue(kapi._names_own_version("Tracon", "Tracon.1.0.0-preview.1.nupkg"))
        self.assertFalse(kapi._names_own_version("Tracon", "Tracon.Core.1.0.0-preview.1.nupkg"))
        self.assertTrue(kapi._names_own_version("Tracon.Core", "Tracon.Core.1.0.0-preview.1.nupkg"))

    def test_surum_istenmisse_tam_dosya_adi_aranir(self):
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)
            (release_dir / "Tracon.Core.1.0.0-preview.1.nupkg").write_bytes(b"")

            found = kapi._resolve_nupkg(release_dir, "Tracon.Core", "1.0.0-preview.1")
            missing = kapi._resolve_nupkg(release_dir, "Tracon.Core", "1.0.0-preview.2")

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
            old = release_dir / "Tracon.Core.1.0.0-preview.1.nupkg"
            new = release_dir / "Tracon.Core.0.0.0-preview.0.400.nupkg"
            old.write_bytes(b"")
            new.write_bytes(b"")
            os.utime(old, (1, 1))
            os.utime(new, (2, 2))

            found = kapi._resolve_nupkg(release_dir, "Tracon.Core", None)

        self.assertEqual(found, new)

    def test_hedef_frameworkler_tekil_override_okur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Testing", "<PropertyGroup><TargetFrameworks>net10.0</TargetFrameworks></PropertyGroup>")
            self._write_csproj(root, "Tracon.Core")

            self.assertEqual(kapi._target_frameworks(root, "Tracon.Testing"), ("net10.0",))
            self.assertEqual(kapi._target_frameworks(root, "Tracon.Core"), ("net8.0", "net9.0", "net10.0"))

    @staticmethod
    def _write_fake_nupkg(path: pathlib.Path, *, content: bytes, psmdcp_guid: str = "0" * 32) -> None:
        """A minimal but REAL zip, shaped enough to exercise `_content_fingerprint`:
        one meaningful entry plus the two OPC entries NuGet regenerates with a
        random name on every real `dotnet pack` (see `_VOLATILE_OPC_ENTRY`)."""
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("lib/net10.0/Thing.dll", content)
            archive.writestr("_rels/.rels", f"<Relationships><Relationship Target=\"/package/services/metadata/core-properties/{psmdcp_guid}.psmdcp\" /></Relationships>")
            archive.writestr(f"package/services/metadata/core-properties/{psmdcp_guid}.psmdcp", "<coreProperties />")

    # Faz 136, 136.3: "bayat paket temizlenir" iddiası (eski `_clean_stale_packages`,
    # koşumdan ÖNCE aynı kimlikteki her şeyi sessizce siliyordu) yerini "farklı
    # içerikli aynı kimlik promote edilmez" iddiasına bırakır - staging'den
    # release_dir'e taşıma artık içerik parmak izi karşılaştırmasından
    # geçmeden olmaz (ham SHA-256 değil - bkz. `_content_fingerprint`).
    def test_yeni_paket_staging_dizininden_release_dizinine_tasinir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            name = "Tracon.Core.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / name, content=b"content")

            conflicts = kapi._promote_staged_packages(staging_dir, release_dir, [name])

            self.assertEqual(conflicts, [])
            self.assertTrue((release_dir / name).exists())
            self.assertFalse((staging_dir / name).exists())

    def test_ayni_icerik_parmak_izi_deterministik_no_op_olarak_gecer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            release_dir.mkdir()
            name = "Tracon.Core.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / name, content=b"same-content")
            self._write_fake_nupkg(release_dir / name, content=b"same-content")

            conflicts = kapi._promote_staged_packages(staging_dir, release_dir, [name])

            self.assertEqual(conflicts, [])

    def test_farkli_opc_rastgele_adi_tek_basina_konflikt_saymaz(self):
        """🚨 Ölçüldü (2026-09-03): NuGet, HER `dotnet pack` koşumunda
        `package/services/metadata/core-properties/<rastgele>.psmdcp` dosyasını
        YENİ bir GUID ile yazar ve `_rels/.rels` o adı taşır - aynı commit'i
        art arda iki kez paketlemek bile FARKLI ham SHA-256 üretir. Ham dosya
        hash'i karşılaştırılsaydı bu, MT-PKG-115'in kanıtlamak istediği
        senaryonun TAM TERSİNİ üretirdi: değişmeyen bir sürümün İKİNCİ koşumu
        her seferinde 'farklı artifact' diye reddedilirdi."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            release_dir.mkdir()
            name = "Tracon.Core.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / name, content=b"same-content", psmdcp_guid="a" * 32)
            self._write_fake_nupkg(release_dir / name, content=b"same-content", psmdcp_guid="b" * 32)
            self.assertNotEqual(kapi._sha256(staging_dir / name), kapi._sha256(release_dir / name))

            conflicts = kapi._promote_staged_packages(staging_dir, release_dir, [name])

            self.assertEqual(conflicts, [])

    def test_farkli_icerik_koşumu_durdurur_ve_mevcut_artifacti_korur(self):
        """Aynı kimlikte (id+sürüm) GERÇEKTEN farklı içerik: hiçbir dosya
        promote edilmez - konfilktsiz olan bile - ve mevcut release_dir olduğu
        gibi kalır."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            staging_dir, release_dir = root / "staging", root / "release"
            staging_dir.mkdir()
            release_dir.mkdir()
            conflicting = "Tracon.Core.1.0.0-preview.1.nupkg"
            clean = "Tracon.Abstractions.1.0.0-preview.1.nupkg"
            self._write_fake_nupkg(staging_dir / conflicting, content=b"new-content")
            self._write_fake_nupkg(release_dir / conflicting, content=b"old-content")
            self._write_fake_nupkg(staging_dir / clean, content=b"non-conflicting")

            conflicts = kapi._promote_staged_packages(staging_dir, release_dir, [conflicting, clean])

            with zipfile.ZipFile(release_dir / conflicting) as archive:
                untouched_content = archive.read("lib/net10.0/Thing.dll")

            self.assertEqual(conflicts, [conflicting])
            self.assertEqual(untouched_content, b"old-content")
            self.assertFalse((release_dir / clean).exists())
            self.assertTrue((staging_dir / clean).exists())

    def test_manifest_her_paket_icin_id_dosya_ve_sha256_tasir(self):
        with tempfile.TemporaryDirectory() as directory:
            release_dir = pathlib.Path(directory)

            manifest_path = kapi._write_manifest(
                release_dir,
                version="1.0.0-preview.1",
                commit="8b21cf9f301fbbbaee32268df838bc0130e45059",
                packages=[
                    {"id": "Tracon.Core", "file": "Tracon.Core.1.0.0-preview.1.nupkg", "sha256": "abc",
                     "symbolsFile": "Tracon.Core.1.0.0-preview.1.snupkg", "symbolsSha256": "def"},
                ],
            )
            manifest = json.loads(manifest_path.read_text(encoding="utf-8"))

        self.assertEqual(manifest["version"], "1.0.0-preview.1")
        self.assertEqual(manifest["dirty"], False)
        self.assertEqual(manifest["packages"][0]["id"], "Tracon.Core")
        self.assertEqual(manifest["packages"][0]["sha256"], "abc")

    def test_yayin_kirli_agacta_erken_reddeder_pack_denenmez(self):
        with mock.patch.object(kapi, "_git", return_value=[" M src/Directory.Build.props"]):
            with mock.patch("subprocess.run") as run:
                result = kapi.release_rehearsal(None)

        self.assertEqual(result, 1)
        run.assert_not_called()

    def test_yayin_git_bulunamazsa_taban_cozulemez_pack_denenmez(self):
        """Faz 136: git yoksa temizlik denetimi uyarıyla atlanır (MSBuild kapısı
        ikinci hattır). Faz 187: taban adımının ikinci hattı yoktur - sessiz
        atlama yerine kırmızı, pack hiç denenmez."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "src").mkdir()
            output = io.StringIO()

            with mock.patch.object(kapi, "_git", return_value=None):
                with mock.patch("subprocess.run", side_effect=OSError("git yok")) as run:
                    with contextlib.redirect_stdout(output):
                        result = kapi.release_rehearsal(None, root=root, release_dir=root / "artifacts" / "package" / "release")

        self.assertEqual(result, 1)
        self.assertIn("Taban çözülemedi", output.getvalue())
        self.assertFalse(any(call.args[0][0] == "dotnet" for call in run.call_args_list))

    # --- Faz 187: taban, izole restore ve kırıcı kapının sırası -------------

    @staticmethod
    def _breaking_changes_module():
        # kapi.py modülü `import breaking_changes` ile alır; testler aynı nesneyi
        # (sys.modules girdisi) yamalamalı, kopyasını değil.
        sys.path.insert(0, str(ROOT / "scripts"))
        return importlib.import_module("breaking_changes")

    def _rehearse(self, root: pathlib.Path, *, finish=None, restore=None, baseline="1.0.0-preview.2", not_run=None,
                  default_release_dir=False):
        bc = self._breaking_changes_module()
        pack_calls: list[list[str]] = []

        def fake_run(command, **kwargs):
            pack_calls.append(list(command))
            return mock.Mock(returncode=0)

        output = io.StringIO()
        resolve = mock.Mock(return_value=baseline) if isinstance(baseline, str) else mock.Mock(side_effect=baseline)
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(kapi, "_git", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "resolve_baseline", resolve))
            stack.enter_context(mock.patch.object(
                bc, "restore_baselines", restore or (lambda ids, version, work: work / "packages")))
            stack.enter_context(mock.patch.object(bc, "baseline_source_violations", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "validation_not_run", not_run or mock.Mock(return_value=[])))
            stack.enter_context(mock.patch("subprocess.run", side_effect=fake_run))
            finish_mock = stack.enter_context(mock.patch.object(
                kapi, "_finish_release_rehearsal", finish or mock.Mock(return_value=0)))
            stack.enter_context(contextlib.redirect_stdout(output))
            release_dir = None if default_release_dir else root / "artifacts" / "package" / "release"
            result = kapi.release_rehearsal(None, root=root, release_dir=release_dir)
        return result, pack_calls, finish_mock, output.getvalue()

    def _library_root(self, directory: str) -> pathlib.Path:
        root = pathlib.Path(directory)
        self._write_csproj(root, "Tracon.Core")
        self._write_csproj(root, "Tracon", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")
        return root

    def test_taban_cozulemezse_pack_denenmez(self):
        bc = self._breaking_changes_module()
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            result, pack_calls, finish, output = self._rehearse(
                root, baseline=bc.BaselineError("HEAD'den erişilen bir 'v*' etiketi yok"))

        self.assertEqual(result, 1)
        self.assertEqual(pack_calls, [])
        finish.assert_not_called()
        self.assertIn("❌ Taban çözülemedi: HEAD'den erişilen bir 'v*' etiketi yok", output)

    def test_restore_hatasi_paketi_adiyla_bildirir_pack_denenmez(self):
        bc = self._breaking_changes_module()

        def failing_restore(ids, version, work):
            raise bc.RestoreError(f"taban paketleri nuget.org'dan alınamadı (v{version}; {', '.join(ids)})")

        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            result, pack_calls, finish, output = self._rehearse(root, restore=failing_restore)

        self.assertEqual(result, 1)
        self.assertEqual(pack_calls, [])
        finish.assert_not_called()
        # Yalnız library paketinin tabanı istenir; meta paketin tabanı yoktur.
        self.assertIn("(v1.0.0-preview.2; Tracon.Core)", output)

    def test_pack_taban_ozelliklerini_ve_rapor_dizinini_alir(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            result, pack_calls, finish, output = self._rehearse(root)

            gate = finish.call_args.kwargs["breaking_gate"]
            arguments = pack_calls[0]
            self.assertEqual(result, 0)
            self.assertEqual(len(pack_calls), 1)
            self.assertIn("-p:TraconPackageBaselineVersion=1.0.0-preview.2", arguments)
            self.assertIn(f"-p:TraconApiCompatReportDir={gate.report_dir}", arguments)
            self.assertTrue(any(argument.startswith("-p:TraconPackageBaselineRoot=") for argument in arguments))
            self.assertEqual(gate.report_dir.parent, root / "artifacts" / "package" / "api-compat")
            self.assertEqual(gate.baseline, "1.0.0-preview.2")
            self.assertIn("Taban: v1.0.0-preview.2 (git describe)", output)
            self.assertIn("Taban paketleri izole cache'ten: 1 paket, kaynak api.nuget.org", output)

    def test_rapor_dizini_kosum_basina_benzersiz(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            _, _, first, _ = self._rehearse(root)
            _, _, second, _ = self._rehearse(root)

            self.assertNotEqual(first.call_args.kwargs["breaking_gate"].report_dir,
                                second.call_args.kwargs["breaking_gate"].report_dir)

    def test_istisnada_gecici_dizinler_silinir_promote_yok(self):
        seen: dict[str, pathlib.Path] = {}

        def exploding_finish(**kwargs):
            seen["report"] = kwargs["breaking_gate"].report_dir
            seen["staging"] = kwargs["staging_dir"]
            raise KeyboardInterrupt

        def restore(ids, version, work):
            seen["work"] = work
            return work / "packages"

        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            with self.assertRaises(KeyboardInterrupt):
                self._rehearse(root, finish=exploding_finish, restore=restore)

            self.assertFalse(seen["report"].exists())
            self.assertFalse(seen["staging"].exists())
            self.assertFalse(seen["work"].exists())
            self.assertFalse((root / "artifacts" / "package" / "release").exists())

    def test_prova_varsayilanda_surum_ve_commit_dizinine_yazar_release_dizinine_dokunmaz(self):
        """`release/` geliştirme feed'idir; oradaki eski sürümler provanın sample
        kapısını dakikalarca paketlemeden SONRA düşürüyordu (2026-09-24)."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)
            package_root = root / "artifacts" / "package"
            (package_root / "release").mkdir(parents=True)
            stale = package_root / "release" / "Tracon.Core.1.0.0-preview.2.82.nupkg"
            stale.write_bytes(b"eski")

            with mock.patch.object(kapi, "PACKAGE_RELEASE_DIR", package_root / "release"), \
                    mock.patch.object(kapi, "PACKAGE_REHEARSAL_ROOT", package_root / "yayin"), \
                    mock.patch.object(kapi, "_head_commit", return_value="0123456789abcdef0123"), \
                    mock.patch.object(kapi, "_resolve_package_set", return_value=({}, "1.0.0-preview.3")):
                result, _, finish, output = self._rehearse(root, default_release_dir=True)

            self.assertEqual(result, 0, output)
            self.assertEqual(finish.call_args.kwargs["release_dir"], package_root / "yayin" / "1.0.0-preview.3")
            self.assertEqual(finish.call_args.kwargs["staging_dir"].parent, package_root / "staging")
            self.assertEqual(stale.read_bytes(), b"eski")
            self.assertIn("Prova çıktısı:", output)

    def test_prova_dizini_surum_basinadir(self):
        """Faz 136 dizinin İÇİNDE geçerli kalır: aynı sürümün yeni commit'i aynı
        dizine düşer ve farklı içerik reddedilir (MT-PKG-116)."""
        root = pathlib.Path("/x")

        self.assertEqual(kapi.rehearsal_release_dir("1.0.0-preview.3", root), root / "1.0.0-preview.3")
        self.assertNotEqual(kapi.rehearsal_release_dir("1.0.0-preview.3", root),
                            kapi.rehearsal_release_dir("1.0.0-preview.2", root))

    def test_bayat_ilk_yayin_bayragi_pack_denenmez(self):
        bc = self._breaking_changes_module()
        with tempfile.TemporaryDirectory() as directory:
            root = self._library_root(directory)

            with mock.patch.object(bc, "stale_first_release_flags", return_value=["Tracon.Voice"]):
                result, pack_calls, _, output = self._rehearse(root)

        self.assertEqual(result, 1)
        self.assertEqual(pack_calls, [])
        self.assertIn("TraconPackageFirstRelease'i kaldırın: Tracon.Voice", output)

    @staticmethod
    def _write_valid_library_nupkg(staging_dir: pathlib.Path, project_id: str, version: str) -> None:
        """Enough of a real package to pass every metadata check that runs
        before the breaking-change gate, so the gate is what decides."""
        nuspec = (
            f'<package><metadata><id>{project_id}</id><license type="file">LICENSE.md</license>'
            "<requireLicenseAcceptance>true</requireLicenseAcceptance>"
            '<repository type="git" url="https://example.invalid" commit="abcdef1234" />'
            f"<releaseNotes>https://tracon.dev/changelog/{version}</releaseNotes></metadata></package>")
        with zipfile.ZipFile(staging_dir / f"{project_id}.{version}.nupkg", "w") as archive:
            for entry in ("icon.png", "README.md", "LICENSE.md"):
                archive.writestr(entry, "x")
            archive.writestr(f"{project_id}.nuspec", nuspec)
            for tfm in ("net8.0", "net9.0", "net10.0"):
                archive.writestr(f"lib/{tfm}/{project_id}.xml", "<doc />")
        (staging_dir / f"{project_id}.{version}.snupkg").write_bytes(b"")

    def _finish(self, root: pathlib.Path, gate_result: int):
        staging_dir = root / "staging"
        staging_dir.mkdir()
        release_dir = root / "release"
        self._write_valid_library_nupkg(staging_dir, "Tracon.Core", "1.0.0-preview.2.47")
        sys.path.insert(0, str(ROOT / "scripts"))
        samples = importlib.import_module("release_extension_samples")
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=root / "reports")
        output = io.StringIO()
        with mock.patch.object(kapi, "_check_breaking_changes", return_value=gate_result) as checked, \
                mock.patch.object(kapi, "_npm_dry_run", return_value=0), \
                mock.patch.object(samples, "verify", return_value=0), \
                contextlib.redirect_stdout(output):
            result = kapi._finish_release_rehearsal(
                root=root, release_dir=release_dir, staging_dir=staging_dir, project_ids=["Tracon.Core"],
                requested_version=None, commit="abcdef1234", breaking_gate=gate)
        checked.assert_called_once_with(root, gate, "1.0.0-preview.2.47")
        return result, release_dir, output.getvalue()

    def _finish_packages(self, root: pathlib.Path, packages: dict[str, tuple[str, dict[str, bytes]]]):
        """`_finish_release_rehearsal` over hand-made packages: id -> (nuspec
        dependency XML, extra root entries). The breaking gate is green."""
        staging_dir = root / "staging"
        staging_dir.mkdir()
        release_dir = root / "release"
        # Etiket biçiminde olmayan sürüm: CHANGELOG kapısı bu testlerin konusu değil.
        version = "1.0.0-preview.2.47"
        for project_id, (dependencies, extra) in packages.items():
            project_dir = root / "src" / project_id
            project_dir.mkdir(parents=True, exist_ok=True)
            (project_dir / f"{project_id}.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk" />\n', encoding="utf-8")
            self._write_valid_library_nupkg(staging_dir, project_id, version)
            nupkg = staging_dir / f"{project_id}.{version}.nupkg"
            with zipfile.ZipFile(nupkg) as archive:
                entries = {name: archive.read(name) for name in archive.namelist()}
            nuspec = entries[f"{project_id}.nuspec"].decode()
            entries[f"{project_id}.nuspec"] = nuspec.replace(
                "</metadata>", f"<dependencies>{dependencies}</dependencies></metadata>").encode()
            entries.update(extra)
            with zipfile.ZipFile(nupkg, "w") as archive:
                for name, data in entries.items():
                    archive.writestr(name, data)
        sys.path.insert(0, str(ROOT / "scripts"))
        samples = importlib.import_module("release_extension_samples")
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=root / "reports")
        output = io.StringIO()
        with mock.patch.object(kapi, "_check_breaking_changes", return_value=0), \
                mock.patch.object(kapi, "_npm_dry_run", return_value=0), \
                mock.patch.object(samples, "verify", return_value=0), \
                contextlib.redirect_stdout(output):
            result = kapi._finish_release_rehearsal(
                root=root, release_dir=release_dir, staging_dir=staging_dir, project_ids=sorted(packages),
                requested_version=None, commit="abcdef1234", breaking_gate=gate)
        return result, output.getvalue()

    HOSTING = '<dependency id="Microsoft.Agents.AI.Hosting" version="{0}" exclude="Build,Analyzers" />'

    def test_on_surum_ucuncu_taraf_bagimlilik_acik_alt_sinirsa_kirmizi(self):
        """A-59: `version="x"` = `>= x`; yayınlanmış preview.2 Hosting 1.22 ile
        uyarısız restore olup çalışma anında düştü."""
        with tempfile.TemporaryDirectory() as directory:
            result, output = self._finish_packages(pathlib.Path(directory), {
                "Tracon.AspNetCore": (self.HOSTING.format("1.20.0-preview.260831.1"), {})})

        self.assertEqual(result, 1, output)
        self.assertIn("tam aralık değil (A-59): Microsoft.Agents.AI.Hosting 1.20.0-preview.260831.1", output)

    def test_on_surum_ucuncu_taraf_bagimlilik_tam_araliksa_gecer(self):
        with tempfile.TemporaryDirectory() as directory:
            result, output = self._finish_packages(pathlib.Path(directory), {
                "Tracon.AspNetCore": (
                    self.HOSTING.format("[1.20.0-preview.260831.1]")
                    + self.HOSTING.replace("Hosting", "Hosting.A2A").format(
                        "[1.20.0-preview.260831.1, 1.20.0-preview.260831.1]"), {})})

        self.assertEqual(result, 0, output)

    def test_on_surum_farkli_iki_sinirli_aralik_tam_sayilmaz(self):
        self.assertIsNone(kapi.EXACT_RANGE_PATTERN.fullmatch("[1.0.0-preview.1, 1.0.0-preview.2]"))
        self.assertIsNone(kapi.EXACT_RANGE_PATTERN.fullmatch("[1.0.0-preview.1, )"))
        self.assertIsNotNone(kapi.EXACT_RANGE_PATTERN.fullmatch("[1.0.0-preview.1]"))

    def _ui_notice(self, root: pathlib.Path, text: bytes) -> None:
        source = root / kapi.THIRD_PARTY_NOTICE_PACKAGES["Tracon.UI"]
        source.parent.mkdir(parents=True, exist_ok=True)
        source.write_bytes(text)

    def test_ui_paketi_lisans_bildirimi_tasimiyorsa_kirmizi(self):
        """BL-058: gömülü React/TanStack paketlenir, MIT bildirimi kopyayla gider."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(root, {"Tracon.UI": ("", {})})

        self.assertEqual(result, 1, output)
        self.assertIn("THIRD-PARTY-NOTICES.txt paket kökünde yok (BL-058)", output)

    def test_ui_bildirimi_kaynaktan_farkliysa_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(
                root, {"Tracon.UI": ("", {"THIRD-PARTY-NOTICES.txt": b"eski\n"})})

        self.assertEqual(result, 1, output)
        self.assertIn("ile aynı değil", output)

    def test_ui_bildirimi_kaynakla_ayniysa_gecer(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(
                root, {"Tracon.UI": ("", {"THIRD-PARTY-NOTICES.txt": b"react\n"})})

        self.assertEqual(result, 0, output)

    def test_kirici_kapi_kirmiziyken_promote_yok(self):
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Core")

            result, release_dir, output = self._finish(root, gate_result=1)

            self.assertEqual(result, 1, output)
            self.assertFalse(release_dir.exists())

    def test_kirici_kapi_yesilken_promote_edilir(self):
        """Kontrol: sahte paket kapıdan önceki her denetimi geçer - yukarıdaki
        kırmızı sonucu kapı verir, metaveri denetimi değil."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._write_csproj(root, "Tracon.Core")

            result, release_dir, output = self._finish(root, gate_result=0)

            self.assertEqual(result, 0, output)
            self.assertTrue((release_dir / "Tracon.Core.1.0.0-preview.2.47.nupkg").exists())

    def test_surum_tabana_esitse_kapi_kirmizi(self):
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/yok"))
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            result = kapi._check_breaking_changes(ROOT, gate, "1.0.0-preview.2")

        self.assertEqual(result, 1)
        self.assertIn("tabandan (v1.0.0-preview.2) büyük değil", output.getvalue())


class TekDerlemeZinciriTestleri(unittest.TestCase):
    """Faz 191: `paketle` · `paket-dogrula` · `yayin --paket-dizini`.

    Sahte bir repo kökü iki paket taşır: `Tracon.Core` (library, taban
    doğrulaması var) ve `Tracon` (meta, `.snupkg` yok)."""

    COMMIT = "0123456789abcdef0123456789abcdef01234567"
    VERSION = "1.0.0-preview.2.47"
    BASELINE = "1.0.0-preview.2"

    @staticmethod
    def _bc():
        sys.path.insert(0, str(ROOT / "scripts"))
        return importlib.import_module("breaking_changes")

    def _root(self, directory: str) -> pathlib.Path:
        root = pathlib.Path(directory) / "repo"
        for project_id, body in (
                ("Tracon.Core", ""),
                ("Tracon", "<PropertyGroup><IncludeBuildOutput>false</IncludeBuildOutput></PropertyGroup>")):
            project = root / "src" / project_id
            project.mkdir(parents=True)
            (project / f"{project_id}.csproj").write_text(
                f'<Project Sdk="Microsoft.NET.Sdk">\n{body}\n</Project>\n', encoding="utf-8")
        return root

    def _write_package(self, directory: pathlib.Path, project_id: str, *, version: str | None = None,
                       commit: str | None = None) -> None:
        version = version or self.VERSION
        commit = commit or self.COMMIT
        library = project_id == "Tracon.Core"
        acceptance = "<requireLicenseAcceptance>true</requireLicenseAcceptance>"
        nuspec = (
            f'<package><metadata><id>{project_id}</id><license type="file">LICENSE.md</license>{acceptance}'
            f'<repository type="git" url="https://example.invalid" commit="{commit}" />'
            f"<releaseNotes>https://tracon.dev/changelog/{version}</releaseNotes></metadata></package>")
        with zipfile.ZipFile(directory / f"{project_id}.{version}.nupkg", "w") as archive:
            for entry in ("icon.png", "README.md", "LICENSE.md"):
                archive.writestr(entry, "x")
            archive.writestr(f"{project_id}.nuspec", nuspec)
            if library:
                for tfm in ("net8.0", "net9.0", "net10.0"):
                    archive.writestr(f"lib/{tfm}/{project_id}.xml", "<doc />")
        if library:
            with zipfile.ZipFile(directory / f"{project_id}.{version}.snupkg", "w") as archive:
                archive.writestr(f"lib/net10.0/{project_id}.pdb", "pdb")

    def _git(self, *, status=(), commit=None):
        head = commit or self.COMMIT

        def fake(*args):
            if args[0] == "status":
                return list(status)
            if args[:2] == ("rev-parse", "HEAD"):
                return [head]
            return []
        return fake

    def _pack(self, root: pathlib.Path, output: pathlib.Path, *, report: bool = True, status=(), not_run=()):
        """`pack_for_ci` with the dotnet pack replaced by a writer of fake packages."""
        bc = self._bc()
        calls: list[list[str]] = []

        def fake_run(command, **kwargs):
            calls.append(list(command))
            self._write_package(output, "Tracon.Core")
            self._write_package(output, "Tracon")
            if report:
                (output / "api-compat" / "Tracon.Core.xml").write_text("<Suppressions />", encoding="utf-8")
            return mock.Mock(returncode=0)

        text = io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(kapi, "_git", side_effect=self._git(status=status)))
            stack.enter_context(mock.patch.object(bc, "resolve_baseline", return_value=self.BASELINE))
            stack.enter_context(mock.patch.object(bc, "stale_first_release_flags", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "restore_baselines", lambda ids, version, work: work / "packages"))
            stack.enter_context(mock.patch.object(bc, "baseline_source_violations", return_value=[]))
            stack.enter_context(mock.patch.object(bc, "validation_not_run", return_value=list(not_run)))
            stack.enter_context(mock.patch("subprocess.run", side_effect=fake_run))
            stack.enter_context(contextlib.redirect_stdout(text))
            result = kapi.pack_for_ci(output, root=root)
        return result, calls, text.getvalue()

    def _rehearse(self, root: pathlib.Path, package_dir: pathlib.Path, *, commit=None, version=None,
                  breaking=0, baseline=None):
        bc = self._bc()
        samples = importlib.import_module("release_extension_samples")
        release_dir = root / "artifacts" / "package" / "release"
        text = io.StringIO()
        with contextlib.ExitStack() as stack:
            stack.enter_context(mock.patch.object(kapi, "_git", side_effect=self._git(commit=commit)))
            stack.enter_context(mock.patch.object(bc, "resolve_baseline", return_value=baseline or self.BASELINE))
            not_run = stack.enter_context(mock.patch.object(bc, "validation_not_run", return_value=[]))
            check = stack.enter_context(mock.patch.object(bc, "check", return_value=breaking))
            run = stack.enter_context(mock.patch("subprocess.run"))
            stack.enter_context(mock.patch.object(kapi, "_npm_dry_run", return_value=0))
            stack.enter_context(mock.patch.object(samples, "verify", return_value=0))
            stack.enter_context(contextlib.redirect_stdout(text))
            result = kapi.rehearse_package_directory(package_dir, version, root=root, release_dir=release_dir)
        return result, release_dir, text.getvalue(), run, not_run, check

    def _packed(self, directory: str, **kwargs) -> tuple[pathlib.Path, pathlib.Path]:
        root = self._root(directory)
        output = pathlib.Path(directory) / "ci-paket"
        result, _, text = self._pack(root, output, **kwargs)
        self.assertEqual(result, 0, text)
        return root, output

    @staticmethod
    def _manifest(directory: pathlib.Path) -> dict:
        return json.loads((directory / "package-manifest.json").read_text(encoding="utf-8"))

    @staticmethod
    def _rewrite_manifest(directory: pathlib.Path, change) -> None:
        path = directory / "package-manifest.json"
        manifest = json.loads(path.read_text(encoding="utf-8"))
        change(manifest)
        path.write_text(json.dumps(manifest), encoding="utf-8")

    @staticmethod
    def _snapshot(directory: pathlib.Path) -> dict[str, str]:
        return {path.relative_to(directory).as_posix(): kapi._sha256(path)
                for path in directory.rglob("*") if path.is_file()}

    # --- paketle ---------------------------------------------------------------

    def test_paketle_no_build_ile_tek_pack_ve_manifest_yazar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-paket"

            result, calls, text = self._pack(root, output)
            manifest = self._manifest(output)

        self.assertEqual(result, 0, text)
        self.assertEqual(len(calls), 1)
        self.assertEqual(calls[0][:2], ["dotnet", "pack"])
        self.assertIn("--no-build", calls[0])
        self.assertIn(f"-p:TraconApiCompatReportDir={output / 'api-compat'}", calls[0])
        self.assertEqual(manifest["commit"], self.COMMIT)
        self.assertEqual(manifest["version"], self.VERSION)
        self.assertEqual(manifest["baseline"], self.BASELINE)
        self.assertEqual([entry["id"] for entry in manifest["packages"]], ["Tracon", "Tracon.Core"])
        self.assertIsNone(manifest["packages"][0]["symbolsFile"])
        self.assertEqual(manifest["apiCompat"], [{
            "id": "Tracon.Core", "report": "api-compat/Tracon.Core.xml",
            "sha256": manifest["apiCompat"][0]["sha256"], "validationRan": True}])

    def test_paketle_farksiz_pakette_rapor_yok_mesru_kayittir(self):
        """187.0 adım 6: farksız pakette SDK rapor yazmaz."""
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory, report=False)

            records = self._manifest(output)["apiCompat"]

        self.assertEqual(records, [{"id": "Tracon.Core", "report": None, "sha256": None, "validationRan": True}])

    def test_paketle_bypass_bayragi_tasimaz(self):
        """K-661: `paketle` iç araç listesine girmez."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            _, calls, _ = self._pack(root, pathlib.Path(directory) / "ci-paket")
        help_text = io.StringIO()
        with contextlib.redirect_stdout(help_text), self.assertRaises(SystemExit):
            kapi.main(["paketle", "--help"])

        for flag in ("TraconSkipCleanWorkingTreeCheck", "TraconAllowDirtyPack"):
            self.assertFalse(any(flag in argument for argument in calls[0]), flag)
            self.assertNotIn(flag, help_text.getvalue())
        self.assertNotIn("--izin", help_text.getvalue())

    def test_paketle_kirli_agacta_pack_denenmez(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-paket"

            result, calls, text = self._pack(root, output, status=["?? notlar.txt"])

            self.assertFalse(output.exists())
        self.assertEqual(result, 1)
        self.assertEqual(calls, [])
        self.assertIn("?? notlar.txt", text)

    def test_paketle_git_yoksa_reddeder(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            text = io.StringIO()
            with mock.patch.object(kapi, "_git", return_value=None), \
                    mock.patch("subprocess.run") as run, contextlib.redirect_stdout(text):
                result = kapi.pack_for_ci(pathlib.Path(directory) / "ci-paket", root=root)

        self.assertEqual(result, 1)
        run.assert_not_called()

    def test_paketle_dolu_dizini_reddeder_silmez(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-paket"
            output.mkdir()
            leftover = output / "Tracon.Core.1.0.0-preview.2.46.nupkg"
            leftover.write_bytes(b"yarim kalmis")

            result, calls, text = self._pack(root, output)

            self.assertTrue(leftover.exists())
            self.assertEqual(leftover.read_bytes(), b"yarim kalmis")
        self.assertEqual(result, 1)
        self.assertEqual(calls, [])
        self.assertIn("Çıktı dizini boş değil", text)

    def test_paketle_ikinci_kosum_ayni_dizine_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            before = self._snapshot(output)

            result, calls, _ = self._pack(root, output)

            self.assertEqual(self._snapshot(output), before)
        self.assertEqual(result, 1)
        self.assertEqual(calls, [])

    def test_paketle_dogrulama_kosmadiysa_manifest_yazilmaz(self):
        """Semaphore pack aşamasındadır; kırmızıysa yarım dizin manifest'siz kalır
        ve sonraki koşum onu reddeder."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._root(directory)
            output = pathlib.Path(directory) / "ci-paket"

            result, _, text = self._pack(root, output, not_run=["Tracon.Core: paket doğrulaması bu koşumda koşmadı"])

            self.assertFalse((output / "package-manifest.json").exists())
            self.assertEqual(kapi.package_directory_problems(output), ["package-manifest.json yok"])
        self.assertEqual(result, 1)
        self.assertIn("Paket doğrulaması bu koşumda koşmadı", text)

    # --- paket-dogrula ----------------------------------------------------------

    def test_paket_dogrula_yesil(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)

            self.assertEqual(kapi.package_directory_problems(output), [])

    def test_paket_dogrula_tek_bayt_farki_dosya_adiyla(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)
            with (output / f"Tracon.Core.{self.VERSION}.nupkg").open("ab") as handle:
                handle.write(b"\0")

            problems = kapi.package_directory_problems(output)

        self.assertEqual(problems, [f"SHA-256 farklı: Tracon.Core.{self.VERSION}.nupkg"])

    def test_paket_dogrula_eksik_ve_fazla_dosya(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)
            (output / f"Tracon.Core.{self.VERSION}.snupkg").unlink()
            (output / "Tracon.Extra.1.0.0.nupkg").write_bytes(b"x")
            (output / "api-compat" / "Tracon.Extra.xml").write_text("<x />", encoding="utf-8")

            problems = kapi.package_directory_problems(output)

        self.assertIn(f"eksik: Tracon.Core.{self.VERSION}.snupkg", problems)
        self.assertIn("manifest dışı dosya: Tracon.Extra.1.0.0.nupkg", problems)
        self.assertIn("manifest dışı dosya: api-compat/Tracon.Extra.xml", problems)

    def test_paket_dogrula_bos_yok_ve_bozuk_manifest(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            (base / "bos").mkdir()
            (base / "manifestsiz").mkdir()
            (base / "manifestsiz" / "Tracon.Core.1.nupkg").write_bytes(b"x")
            (base / "bozuk").mkdir()
            (base / "bozuk" / "package-manifest.json").write_text("{", encoding="utf-8")
            (base / "sema").mkdir()
            (base / "sema" / "package-manifest.json").write_text(
                json.dumps({"dirty": False, "packages": [{"id": "X", "file": "../X.nupkg", "sha256": "0" * 64}]}),
                encoding="utf-8")

            self.assertEqual(kapi.package_directory_problems(base / "yok"), [f"{base / 'yok'}: dizin yok"])
            self.assertEqual(kapi.package_directory_problems(base / "bos"), [f"{base / 'bos'}: dizin boş"])
            self.assertEqual(kapi.package_directory_problems(base / "manifestsiz"), ["package-manifest.json yok"])
            self.assertTrue(kapi.package_directory_problems(base / "bozuk")[0].startswith("package-manifest.json okunamadı"))
            self.assertIn("package-manifest.json: X geçersiz dosya/SHA-256 kaydı taşıyor",
                          kapi.package_directory_problems(base / "sema"))

    def test_paket_dogrula_komutu_cikis_kodu(self):
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory)
            text = io.StringIO()
            with contextlib.redirect_stdout(text):
                green = kapi.main(["paket-dogrula", str(output)])
                (output / f"Tracon.{self.VERSION}.nupkg").unlink()
                red = kapi.main(["paket-dogrula", str(output)])

        self.assertEqual((green, red), (0, 1))
        self.assertIn(f"eksik: Tracon.{self.VERSION}.nupkg", text.getvalue())

    # --- yayin --kuru --paket-dizini --------------------------------------------

    def test_paket_dizini_modu_pack_ve_semaphore_cagirmaz_ortak_yolu_kosar(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            with mock.patch.object(kapi, "_finish_release_rehearsal", return_value=0) as finish:
                result, _, text, run, not_run, _ = self._rehearse(root, output)

        self.assertEqual(result, 0, text)
        run.assert_not_called()
        not_run.assert_not_called()
        finish.assert_called_once()
        self.assertEqual(finish.call_args.kwargs["staging_dir"], output)
        self.assertEqual(finish.call_args.kwargs["breaking_gate"].report_dir, output / "api-compat")
        self.assertIsNotNone(finish.call_args.kwargs["input_manifest"])

    def test_paket_dizini_modu_yesil_girdi_degismez_manifest_esit(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            before = self._snapshot(output)

            result, release_dir, text, run, not_run, check = self._rehearse(root, output)

            self.assertEqual(result, 0, text)
            # Pack anı kanıtı ortak yolda da çağrılmaz (Faz 191.3).
            not_run.assert_not_called()
            self.assertEqual(self._snapshot(output), before)
            written = self._manifest(release_dir)
            source = self._manifest(output)
            for field in ("version", "commit", "baseline", "packages"):
                self.assertEqual(written[field], source[field], field)
            self.assertEqual(kapi.package_directory_problems(release_dir), [])
            self.assertEqual(check.call_args.kwargs["report_dir"], output / "api-compat")
        run.assert_not_called()
        self.assertIn("Manifest girdiyle aynı", text)

    def test_paket_dizini_modu_kirici_kapi_kirmiziyken_kopya_yok(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, release_dir, _, _, _, _ = self._rehearse(root, output, breaking=1)

            self.assertFalse(release_dir.exists())
        self.assertEqual(result, 1)

    def test_paket_dizini_modu_rapor_kaydi_yoksa_rapor_eksik(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            (output / "api-compat" / "Tracon.Core.xml").unlink()
            self._rewrite_manifest(output, lambda manifest: manifest.update(apiCompat=[]))

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("rapor eksik: Tracon.Core", text)

    def test_paket_dizini_modu_rapor_dosyasi_yoksa_kirmizi(self):
        """Kayıt rapor diyor ama `api-compat/` boş: 'kırıcı değişiklik yok' sayılmaz."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            (output / "api-compat" / "Tracon.Core.xml").unlink()

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("eksik: api-compat/Tracon.Core.xml", text)

    def test_paket_dizini_modu_dogrulama_kostu_kaydi_yoksa_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory, report=False)
            self._rewrite_manifest(output, lambda manifest: manifest["apiCompat"][0].update(validationRan=False))

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("rapor eksik: Tracon.Core (doğrulama koştu kaydı yok)", text)

    def test_paket_dizini_modu_baska_commit_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, release_dir, text, *_ = self._rehearse(root, output, commit="f" * 40)

            self.assertFalse(release_dir.exists())
        self.assertEqual(result, 1)
        self.assertIn("Paketler başka bir commit'ten", text)

    def test_nuspec_commit_head_degilse_kirmizi(self):
        """Manifest commit'i doğru ama paketin kendisi başka commit'i adlandırıyor."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            self._write_package(output, "Tracon.Core", commit="e" * 40)
            self._rewrite_manifest(output, lambda manifest: [
                entry.update(sha256=kapi._sha256(output / entry["file"]))
                for entry in manifest["packages"] if entry["id"] == "Tracon.Core"])

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn(f"repository commit '{'e' * 40}' HEAD değil", text)

    def test_paket_dizini_modu_baska_taban_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, _, text, *_ = self._rehearse(root, output, baseline="1.0.0-preview.1")

        self.assertEqual(result, 1)
        self.assertIn("başka bir tabana karşı doğrulandı", text)

    def test_paket_dizini_modu_iki_surum_hatti_reddedilir(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            self._write_package(output, "Tracon", version="1.0.0-preview.2.48")
            (output / f"Tracon.{self.VERSION}.nupkg").unlink()
            self._rewrite_manifest(output, lambda manifest: [
                entry.update(file=f"Tracon.1.0.0-preview.2.48.nupkg",
                             sha256=kapi._sha256(output / "Tracon.1.0.0-preview.2.48.nupkg"))
                for entry in manifest["packages"] if entry["id"] == "Tracon"])

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("tek bir sürüm hattında değil", text)

    def test_paket_dizini_modu_surum_beklenen_surum_olarak_denetlenir(self):
        """Açık Soru 4 = A: --surum ile --paket-dizini birlikte izinlidir."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, _, text, *_ = self._rehearse(root, output, version="1.0.0-preview.3")

        self.assertEqual(result, 1)
        self.assertIn("Paket üretilmedi", text)

    def test_paket_dizini_modu_release_dirde_girdi_disi_paket_reddedilir(self):
        """Açık Soru 5 = A: ret, dosya silinmez."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            old = release_dir / "Tracon.Core.1.0.0-preview.2.10.nupkg"
            old.write_bytes(b"eski")

            result, _, text, *_ = self._rehearse(root, output)

            self.assertTrue(old.exists())
            self.assertEqual(sorted(path.name for path in release_dir.iterdir()), [old.name])
        self.assertEqual(result, 1)
        self.assertIn("girdi dışı paket taşıyor (1): Tracon.Core.1.0.0-preview.2.10.nupkg", text)
        self.assertIn(f"rm -rf {release_dir}", text)

    def test_paket_dizini_modu_ayni_kimlik_farkli_icerik_korunur(self):
        """K-661 kopya yolunda da: aynı ad, farklı içerik -> hiçbir dosya kopyalanmaz."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            name = f"Tracon.Core.{self.VERSION}.nupkg"
            with zipfile.ZipFile(release_dir / name, "w") as archive:
                archive.writestr("lib/net10.0/Tracon.Core.dll", "baska")

            result, _, text, *_ = self._rehearse(root, output)

            self.assertEqual(sorted(path.name for path in release_dir.iterdir()), [name])
        self.assertEqual(result, 1)
        self.assertIn("FARKLI içerikli bir artifact zaten var", text)

    def test_release_dirde_kesik_paket_dosya_adiyla_cikis_1(self):
        """`_content_fingerprint` BadZipFile izi değil, dosya adı verir."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            name = f"Tracon.Core.{self.VERSION}.nupkg"
            (release_dir / name).write_bytes((output / name).read_bytes()[:40])

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn(f"❌ {name}: paket okunamadı", text)

    def test_ayni_parmak_izi_farkli_bayt_manifest_esitsizligi_verir(self):
        """Eski bir kopya aynı içeriği farklı OPC baytlarıyla taşır: no-op promote
        ham SHA-256'yı değiştirmez, manifest karşılaştırması yakalar."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            release_dir = root / "artifacts" / "package" / "release"
            release_dir.mkdir(parents=True)
            name = f"Tracon.Core.{self.VERSION}.nupkg"
            with zipfile.ZipFile(output / name) as source, zipfile.ZipFile(release_dir / name, "w") as target:
                for entry in source.namelist():
                    target.writestr(entry, source.read(entry))
                target.writestr("package/services/metadata/core-properties/" + "a" * 32 + ".psmdcp", "rastgele")

            result, _, text, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        self.assertIn("manifest'i girdi manifest'inden farklı: packages", text)

    def test_kopya_yarida_kesilirse_asil_adla_dosya_kalmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            source = base / "girdi"
            release_dir = base / "release"
            source.mkdir()
            name = "Tracon.Core.1.0.0.nupkg"
            TekDerlemeZinciriTestleri._zip(source / name)

            with mock.patch("os.replace", side_effect=KeyboardInterrupt), self.assertRaises(KeyboardInterrupt):
                kapi._promote_staged_packages(source, release_dir, [name], copy=True)

            self.assertEqual(list(release_dir.iterdir()), [])
            self.assertTrue((source / name).exists())

    def test_kopya_modu_girdiyi_tasimaz(self):
        with tempfile.TemporaryDirectory() as directory:
            base = pathlib.Path(directory)
            source = base / "girdi"
            release_dir = base / "release"
            source.mkdir()
            name = "Tracon.Core.1.0.0.nupkg"
            TekDerlemeZinciriTestleri._zip(source / name)

            conflicts = kapi._promote_staged_packages(source, release_dir, [name], copy=True)

            self.assertEqual(conflicts, [])
            self.assertEqual(kapi._sha256(source / name), kapi._sha256(release_dir / name))
            self.assertEqual([path.name for path in release_dir.iterdir()], [name])

    @staticmethod
    def _zip(path: pathlib.Path) -> None:
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("lib/net10.0/Tracon.Core.dll", "dll")

    def test_manifest_disi_paketi_girdi_dizininde_reddeder(self):
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)
            (output / "Tracon.Sneaky.1.0.0.nupkg").write_bytes(b"x")

            result, _, text, run, *_ = self._rehearse(root, output)

        self.assertEqual(result, 1)
        run.assert_not_called()
        self.assertIn("manifest dışı dosya: Tracon.Sneaky.1.0.0.nupkg", text)


# Bu iki sabit PARÇALI yazılır: tam metin kaynakta görünseydi taramanın kendi
# kapısını kırardı. Aynı teknik test_secret_taramasi_deger_satirini_bulur'da da
# kullanılıyor.
SECRET_LINE = 'var x = "Pass' + 'word=uydurma-bir-deger";'
MARKER = "SYNTHETIC" + "-CREDENTIAL"


class SentetikCredentialTestleri(unittest.TestCase):
    """Faz 166. Bir secret tarayıcısını doğrulamanın tek yolu ona sahte bir
    credential göstermektir; istisna GİZLENMEZ, İŞARETLENİR."""

    def test_isaretsiz_credential_yakalanir(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            (root / "src").mkdir()
            (root / "src" / "a.cs").write_text(SECRET_LINE, encoding="utf-8")

            found, skipped = kapi.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertEqual(skipped, 0)

    def test_isaretli_satir_atlanir_ve_sayilir(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            (root / "src").mkdir()
            (root / "src" / "a.cs").write_text(SECRET_LINE + "  // " + MARKER, encoding="utf-8")

            found, skipped = kapi.find_secrets(root)

        self.assertEqual(found, [])
        self.assertEqual(skipped, 1)

    def test_isaret_ayni_satirda_olmali(self):
        # 🚨 Bir üst satırdaki yorum yetmez: istisna, istisnayı taşıyan
        # SATIRDA görünmelidir, yoksa incelemede kaybolur.
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            (root / "src").mkdir()
            (root / "src" / "a.cs").write_text("// " + MARKER + "\n" + SECRET_LINE, encoding="utf-8")

            found, skipped = kapi.find_secrets(root)

        self.assertEqual(len(found), 1)
        self.assertEqual(skipped, 0)


class IkiKatmanliSecretTaramasiTestleri(unittest.TestCase):
    """2026-09-19. Kapı YEŞİL derken `docs/arsiv/fazlar/53-*.md` içinde gerçek bir
    `ApiKeyGenerator` çıktısı duruyordu. İki bağımsız boşluk vardı: desen ürünün
    kendi anahtar formatını tanımıyordu, ve kapsam gerçek koşum çıktısı taşıyan
    iki ağacı (`arsiv`, `manuel-test`) hiç yürümüyordu."""

    # Gerçek format: `ap_` + kiracı eki + `_` + base64url(32 bayt) = 43 karakter.
    URETILMIS_ANAHTAR = "ap_default_" + ("2zKqRNjJ6MVXcwtTxPKuXkouy85tReMOhGAgYr9MGzo")

    def _tara(self, relative_path, line):
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            path = root / relative_path
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(line + "\n", encoding="utf-8")
            return kapi.find_secrets(root)

    def test_uretilmis_anahtar_arsivde_yakalanir(self):
        # 🚨 Bu tam olarak kaçırılan vakadır. Arşivlenmiş faz kaydı GERÇEK koşum
        # çıktısı taşır - üretilmiş bir credential'ın yapışacağı tek yer orasıdır.
        found, _ = self._tara("docs/arsiv/fazlar/99-X.md", self.URETILMIS_ANAHTAR)

        self.assertEqual(len(found), 1)

    def test_uretilmis_anahtar_manuel_testte_yakalanir(self):
        found, _ = self._tara("docs/manuel-test/99-X.md", self.URETILMIS_ANAHTAR)

        self.assertEqual(len(found), 1)

    def test_yerel_kurulum_deyimi_arsivde_kapiyi_kirmaz(self):
        # Arşiv ve manuel test kaydında bir docker parolası kusur değil, tekrar
        # üretilebilirlik talimatıdır. Kapsamı tüm desenlere açmak 48 satırı
        # işaretletirdi ve istisna listesi kapının kendisini anlamsızlaştırırdı.
        found, skipped = self._tara("docs/arsiv/fazlar/99-Y.md", SECRET_LINE)

        self.assertEqual(found, [])
        self.assertEqual(skipped, 0)

    def test_yerel_kurulum_deyimi_kod_agacinda_yakalanir(self):
        found, _ = self._tara("src/b.cs", SECRET_LINE)

        self.assertEqual(len(found), 1)

    def test_snake_case_metin_anahtar_sanilmaz(self):
        # 🚨 `{43,}` yazmak snake_case İngilizce metni yakalar; ölçüldü, 70+
        # yanlış pozitif. Uzunluk TAM verilmelidir.
        found, _ = self._tara(
            "src/c.cs",
            "ap_on_total_source_code_size_for_JavaScript_files_in_the_TypeScript_x")

        self.assertEqual(found, [])

    def test_kisa_gorunum_oneki_anahtar_sanilmaz(self):
        # `KeyPrefix` ürünün kendi tasarladığı 12 karakterlik görüntü önekidir
        # ve kayıtlarda meşru olarak durur.
        found, _ = self._tara("docs/arsiv/fazlar/99-Y.md", 'keyPrefix":"ap_default_2"')

        self.assertEqual(found, [])

    def test_arsivdeki_isaretli_satir_atlanir_ve_sayilir(self):
        found, skipped = self._tara(
            "docs/arsiv/fazlar/99-X.md", self.URETILMIS_ANAHTAR + " <!-- " + MARKER + " -->")

        self.assertEqual(found, [])
        self.assertEqual(skipped, 1)


class KapasiteKomutuTestleri(unittest.TestCase):
    """Faz 166. Kapasite ölçümü bir KAPI DEĞİLDİR (K-738)."""

    def test_kapasite_komutu_kapanisa_eklenmez(self):
        # 🚨 Ağır bir yük koşumu standart kapanışa sızarsa her faz saatler
        # sürer ve kapılar koşulmaz hale gelir.
        commands = [c.display for c in kapi.closing_commands("abc123", site=True, performance=True)]

        self.assertFalse(any("kapasite" in command for command in commands))
        self.assertFalse(any("capacity" in command for command in commands))

    def test_kapasite_komutu_ic_donguye_de_eklenmez(self):
        commands = [c.display for c in kapi.inner_loop_commands(["src/Tracon.Core/Foo.cs"])]

        self.assertFalse(any("kapasite" in command for command in commands))

    def test_kapasite_exact_surum_ister(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            code = kapi.capacity_measurement("smoke", "*-*", pathlib.Path("/tmp/kapasite-test"))

        self.assertEqual(code, 1)
        self.assertIn("exact", output.getvalue())

    def test_kapasite_bilinmeyen_profili_reddeder(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            code = kapi.capacity_measurement("yok", "1.0.0", pathlib.Path("/tmp/kapasite-test"))

        self.assertEqual(code, 1)
        self.assertIn("profili yok", output.getvalue())


if __name__ == "__main__":
    unittest.main()
