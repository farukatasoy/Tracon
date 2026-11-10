#!/usr/bin/env python3
"""Tests for the single validation gate runner."""
from __future__ import annotations

import contextlib
import dataclasses
import datetime
import gzip
import importlib.util
import io
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import threading
import time
import types
import unittest
import xml.etree.ElementTree as ElementTree
import zipfile
from unittest import mock

ROOT = pathlib.Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("kapi", ROOT / "scripts" / "kapi.py")
kapi = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = kapi
spec.loader.exec_module(kapi)


def _write_src_build_props(root: pathlib.Path, frameworks: str = "net10.0") -> None:
    """Paketlerin varsayilan TFM listesi; `_target_frameworks` onu buradan okur."""
    props = root / "src" / "Directory.Build.props"
    props.parent.mkdir(parents=True, exist_ok=True)
    props.write_text(
        f"<Project><PropertyGroup><TargetFrameworks>{frameworks}</TargetFrameworks></PropertyGroup></Project>\n",
        encoding="utf-8")


def _ci_isleri(metin: str) -> dict[str, dict]:
    """`ci.yml` işleri: {ad: {"if": değer | None, "needs": [işler]}}. YAML kütüphanesi gerekmez.

    Blok biçimli `if: >-` değeri izleyen daha derin girintili satırlardan toplanır.
    """
    isler: dict[str, dict] = {}
    simdiki = None
    icinde = False
    blok_if = False
    for satir in metin.splitlines():
        if re.match(r"^jobs:\s*$", satir):
            icinde = True
            continue
        if not icinde:
            continue
        if blok_if:
            if not satir.strip() or re.match(r"^ {5,}", satir):
                isler[simdiki]["if"] = f"{isler[simdiki]['if']} {satir.strip()}".strip()
                continue
            blok_if = False
        is_satiri = re.match(r"^  ([A-Za-z0-9_-]+):\s*$", satir)
        if is_satiri:
            simdiki = is_satiri.group(1)
            isler[simdiki] = {"if": None, "needs": []}
            continue
        if simdiki is None:
            continue
        ozellik = re.match(r"^    (if|needs):\s*(.*?)\s*$", satir)
        if not ozellik:
            continue
        if ozellik.group(1) == "if":
            blok_if = re.fullmatch(r"[|>][+-]?", ozellik.group(2)) is not None
            isler[simdiki]["if"] = "" if blok_if else ozellik.group(2)
        else:
            isler[simdiki]["needs"] = [ad for ad in re.findall(r"[A-Za-z0-9_-]+", ozellik.group(2))]
    return isler


KANONIK_REPO_KOSULU = "github.repository == 'farukatasoy/Tracon'"
# Bu fonksiyonlardan biri iş düzeyindeki `if:`'te durursa atlanan bir bağımlılık işi atlatmaz.
DURUM_FONKSIYONU = re.compile(r"\b(?:always|failure|cancelled)\(\)")


def _korumasiz_isler(isler: dict[str, dict]) -> list[str]:
    """Özel depoda ve fork'larda koşabilecek işler, sebebiyle.

    Koşul YALNIZ `build`'de durur; diğer her iş ona (doğrudan ya da zincirle) `needs` ile
    bağlıdır ve atlanan bir bağımlılık onları da atlatır. Durum fonksiyonu bu zinciri deler:
    öyle bir iş depo koşulunu kendisi taşımalıdır.
    """
    sorunlar = []
    for ad, is_ in isler.items():
        if ad == "build":
            continue
        bekleyenler, ziyaret = [ad], set()
        while bekleyenler:
            simdiki = bekleyenler.pop()
            if simdiki in ziyaret:
                continue
            ziyaret.add(simdiki)
            bekleyenler.extend(isler[simdiki]["needs"])
        if "build" not in ziyaret:
            sorunlar.append(f"`{ad}` işi `build`'e bağlı değil; koşulsuz koşar")
        kosul = is_["if"] or ""
        # `||` ile bağlanan koşul korumayı deler: `always() || <koşul>` her depoda doğrudur.
        if DURUM_FONKSIYONU.search(kosul) and (KANONIK_REPO_KOSULU not in kosul or "||" in kosul):
            sorunlar.append(
                f"`{ad}` işinin `if:`'i durum fonksiyonu taşıyor; `needs` zinciri onu atlatmayabilir, "
                "depo koşulunu kendisi taşımalı")
    return sorunlar


# `ci.yml` işleri depo koşulunu `build` üzerinden miras alır (`_korumasiz_isler`). Zincire bağlı olmayan
# her iş dosyası -- haftalık fuzz, SDK uyum işi -- koşulu işin kendisinde taşımalıdır.
ZINCIRSIZ_DOSYA_DISI = {"ci.yml", "ayna.yml"}


def _zincirsiz_korumasiz_isler(isler: dict[str, dict]) -> list[str]:
    """Depo koşulunu kendisi taşımayan işler. `||` korumayı deler: her depoda doğrudur."""
    sorunlar = []
    for ad, is_ in isler.items():
        kosul = is_["if"] or ""
        if KANONIK_REPO_KOSULU not in kosul or "||" in kosul:
            sorunlar.append(f"`{ad}` işi kanonik depo koşulunu kendisi taşımıyor")
    return sorunlar



def _is_blogu(metin: str, ad: str) -> str:
    """`ad` işinin satırları: iki boşluk girintili iş başlığından sonraki iş başlığına kadar."""
    satirlar = metin.splitlines()
    baslangic = next(i for i, satir in enumerate(satirlar) if re.match(rf"^  {re.escape(ad)}:\s*$", satir))
    son = next(
        (i for i in range(baslangic + 1, len(satirlar)) if re.match(r"^  [A-Za-z0-9_-]+:\s*$", satirlar[i])),
        len(satirlar))
    return "\n".join(satirlar[baslangic:son])


def _matris_girdileri(blok: str) -> list[dict[str, str]]:
    """Bir işin `include:` girdileri: [{anahtar: değer}]. Yorum satırları ve tırnaklar atılır."""
    girdiler: list[dict[str, str]] = []
    icinde = False
    for satir in blok.splitlines():
        if re.match(r"^\s+include:\s*$", satir):
            icinde = True
            continue
        if not icinde or not satir.strip() or satir.lstrip().startswith("#"):
            continue
        if not re.match(r"^ {10,}", satir):
            break
        girdi = re.match(r"^\s*(-\s+)?([A-Za-z_][\w-]*):\s*(.*?)\s*$", satir)
        if not girdi:
            continue
        deger = girdi.group(3)
        deger = deger[1:-1] if len(deger) >= 2 and deger[0] == deger[-1] and deger[0] in "'\"" else deger
        if girdi.group(1):
            girdiler.append({})
        girdiler[-1][girdi.group(2)] = deger
    return girdiler


def _matris_bag_sorunlari(metin: str, varsayilan_imaj: dict[str, str]) -> list[str]:
    """Platform bacaklarının adı, ortam değişkeni ve imajı birbirine bağlı mı.

    Bir bacak yalnız bir ortam değişkeni setidir. Bağ kopunca (yazım hatası, silinmiş anahtar, yanlış imaj)
    bacak varsayılan imajla koşar ve adının söylediği sürümü sınamadan yeşil kalır.
    """
    sorunlar = []
    for is_adi in ("postgres-matrix", "sqlserver-matrix", "sqlite-floor", "culture-matrix"):
        blok = _is_blogu(metin, is_adi)
        girdiler = _matris_girdileri(blok)
        if not girdiler:
            sorunlar.append(f"`{is_adi}` işinin matrisi okunamadı")
            continue
        for anahtar in sorted(set(re.findall(r"\$\{\{\s*matrix\.([A-Za-z_][\w-]*)\s*\}\}", blok))):
            for girdi in girdiler:
                if anahtar not in girdi:
                    sorunlar.append(
                        f"`{is_adi}`: `matrix.{anahtar}` kullanılıyor ama {girdi} girdisinde `{anahtar}` yok")
        for girdi in girdiler:
            ad = girdi.get("name", "")
            imaj = girdi.get("image", "")
            if is_adi == "postgres-matrix":
                beklenen = int(re.match(r"\d+", ad).group())
                etkin = imaj or varsayilan_imaj["postgres"]
                bulunan = re.search(r"[:-]pg(\d+)(?:-|$)", etkin)
                if not bulunan or int(bulunan.group(1)) != beklenen:
                    sorunlar.append(f"`{is_adi}` hücresi `{ad}`: imaj `{etkin}` {beklenen} sürümünü söylemiyor")
            elif is_adi == "sqlserver-matrix":
                beklenen = int(ad[:4])
                etkin = imaj or varsayilan_imaj["sqlserver"]
                bulunan = re.search(r":(\d{4})(?:-|$)", etkin)
                if not bulunan or int(bulunan.group(1)) != beklenen:
                    sorunlar.append(f"`{is_adi}` hücresi `{ad}`: imaj `{etkin}` {beklenen} sürümünü söylemiyor")
            elif is_adi == "sqlite-floor" and not girdi.get("sqlite"):
                sorunlar.append(f"`{is_adi}`: `sqlite` sürümü boş; sürüm testi boş değişkende atlanır")
    return sorunlar


def _ci_adimlari(blok: str) -> list[str]:
    """Bir iş bloğunun adımları: altı boşluk girintili `- ` satırlarından bölünmüş metinler."""
    return re.split(r"(?m)^(?=      - )", blok)[1:]


def _ci_sessiz_yesil_sorunlari(metin: str) -> list[str]:
    """Kırmızı ya da hiç test koşmayan bir koşunun yeşil görünebileceği üç yer.

    1. `steps.restore` kullanan iş, kullanımdan ÖNCE `id: restore` adımını tanımlar. Tanımsızsa
       `steps.restore.outcome` boş kalır, her test adımı sessizce atlanır ve iş yeşil biter.
    2. TRX yüklemesi `if-no-files-found: error` taşır (test adımları atlanırsa iş kırmızı olur) ve
       `overwrite: true` taşır (altyapı yeniden denemesi ikinci yüklemede 409 almasın).
    3. `nuget-packages` ve `bin-*` yüklemeleri `overwrite` TAŞIMAZ: işi yeniden koşmak ikinci yüklemede
       409 ile kırılmalı; aksi halde önceki işlerin test ettiği baytlar sessizce değişir.
    """
    sorunlar = []
    for ad in _ci_isleri(metin):
        adimlar = _ci_adimlari(_is_blogu(metin, ad))
        ilk_kullanim = next(
            (i for i, adim in enumerate(adimlar) if re.search(r"(?m)^\s+if:.*steps\.restore", adim)), None)
        if ilk_kullanim is not None:
            tanim = next(
                (i for i, adim in enumerate(adimlar) if re.search(r"(?m)^\s+id:\s*restore\s*$", adim)), None)
            if tanim is None or tanim > ilk_kullanim:
                sorunlar.append(f"`{ad}`: `steps.restore` kullanılıyor ama öncesinde `id: restore` adımı yok")
        for adim in adimlar:
            if "actions/upload-artifact@" not in adim:
                continue
            artifact = re.search(r"(?m)^\s+name:\s*(.+?)\s*$", adim)
            yol = re.search(r"(?m)^\s+path:\s*(\S+)\s*$", adim)
            if yol and yol.group(1).endswith(".trx"):
                if not re.search(r"(?m)^\s+if-no-files-found:\s*error\s*$", adim):
                    sorunlar.append(f"`{ad}`: TRX yüklemesi `if-no-files-found: error` taşımıyor")
                if not re.search(r"(?m)^\s+overwrite:\s*true\s*$", adim):
                    sorunlar.append(f"`{ad}`: TRX yüklemesi `overwrite: true` taşımıyor")
            if artifact and (artifact.group(1) == "nuget-packages" or artifact.group(1).startswith("bin-")):
                if re.search(r"(?m)^\s+overwrite:", adim):
                    sorunlar.append(f"`{ad}`: `{artifact.group(1)}` yüklemesi `overwrite` taşıyor")
    return sorunlar


def _varsayilan_imajlar() -> dict[str, str]:
    """Fikstürlerin boş değişkende kullandığı imaj: bacağın boş `image:` değeri bu imajı söyler."""
    okunan = {}
    for ad, yol in (
        ("postgres", ROOT / "tests" / "Tracon.PostgreSql.IntegrationTests" / "Infrastructure" / "PostgresFixture.cs"),
        ("sqlserver", ROOT / "tests" / "Tracon.SqlServer.IntegrationTests" / "Infrastructure" / "SqlServerFixture.cs"),
    ):
        eslesme = re.search(r'const string DefaultImage = "([^"]+)";', yol.read_text(encoding="utf-8"))
        okunan[ad] = eslesme.group(1) if eslesme else ""
    return okunan


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
        """`--sinif A --sinif B` yalnız B'yi koşuyordu (argparse ikinciyi
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
        # Cok hedefli projenin ciktisi release/ DEGIL release_<tfm>/'dir. Paketler
        # bugun tek TFM hedefler; altyapi ikinci bir TFM'i ortam degiskeniyle sinar.
        self.assertEqual(
            [pathlib.PurePath(command.args[0]).parts[-2]
             for command in kapi.test_commands("Tracon.Core.UnitTests", ["*Capability*"], environ={})],
            ["release_net10.0"])
        self.assertEqual(
            [pathlib.PurePath(command.args[0]).parts[-2]
             for command in kapi.test_commands("Tracon.Core.UnitTests", ["*Capability*"], environ={"TraconTestTargetFrameworks": "net10.0;net11.0"})],
            ["release_net10.0", "release_net11.0"])

    def test_tfm_secimi_tek_bacagi_kosar(self):
        commands = kapi.test_commands("Tracon.Core.UnitTests", ["*X*"], "net11.0", environ={"TraconTestTargetFrameworks": "net10.0;net11.0"})

        self.assertEqual(len(commands), 1)
        self.assertEqual(
            pathlib.PurePath(commands[0].args[0]).parts[-3:],
            ("Tracon.Core.UnitTests", "release_net11.0", "Tracon.Core.UnitTests"))

    def test_derlenmeyen_tfm_istenirse_sessiz_gecmez(self):
        with self.assertRaises(ValueError):
            kapi.test_commands("Tracon.Core.UnitTests", ["*X*"], "net7.0", environ={})
        with self.assertRaises(ValueError):
            # Tek hedefli proje yalniz net10.0 derlenir; baska bir TFM istemek
            # bayat ya da var olmayan bir ikiliyi kosturmak olurdu.
            kapi.test_commands("Tracon.Generators.UnitTests", ["*X*"], "net11.0", environ={})

    def test_temsilci_kume_ve_tfm_listesi_props_dosyasindan_okunur(self):
        self.assertEqual(kapi.test_target_frameworks(environ={}), ("net10.0",))
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
            # Denetim bulgusu: csproj govdesi props'tan SONRA okunur;
            # oraya yazilan tekil <TargetFramework> projeyi sessizce net10'a
            # indirir ve runtime nobetcisi net10'da yine gecer.
            self.assertNotIn("<TargetFramework", csproj.read_text(encoding="utf-8"), project)

    def test_tfm_listesi_ortam_degiskeninden_okunur(self):
        # Ayni adli ortam degiskeni MSBuild'e baslangic ozelligi olarak gider;
        # betik de ayni ciktilari aramalidir.
        self.assertEqual(kapi.test_target_frameworks(environ={"TraconTestTargetFrameworks": "net10.0;net11.0"}), ("net10.0", "net11.0"))
        self.assertEqual(
            [path.name for path in kapi.test_output_directories("Tracon.Core.UnitTests", environ={"TraconTestTargetFrameworks": "net10.0;net11.0"})],
            ["release_net10.0", "release_net11.0"])

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
                "# docs" "/X.md -> docs" "/arsiv/Y.md; sablon: docs" "/NN-BUYUK-HARFLI-AD.md\n",
                encoding="utf-8")

            self.assertEqual(kapi.find_stale_doc_references(root), [])

    def test_arsivlenen_faz_referansi_repoda_kalmaz(self):
        # SINIF KAPISI: bu repoda gercek olan tarama. Bir dokuman silindiginde
        # (once arsive tasiniyordu) onu anan baska dosyalar bir donem
        # guncellenmiyordu (43 bayat referans olculdu).
        self.assertEqual(kapi.find_stale_doc_references(), [])

    def test_kanitsiz_public_tip_tarama_kapisini_kirar(self):
        # `public-yuzey-envanteri.py --denetle` hicbir kapida kosmuyordu; kuralin
        # cirkirligi iki kez sessizce gevsedi (iki kanitsiz istisna).
        runner = mock.Mock(side_effect=[
            mock.Mock(returncode=1, stdout="", stderr="denetim: 1 kanitsiz tip, 0 bayat gerekce\n"),
            mock.Mock(returncode=0, stdout="Tracon.Abstractions\tTracon.OrnekTip\tclass\tkanitsiz\n", stderr=""),
        ])
        with mock.patch.object(kapi.subprocess, "run", runner):
            found = kapi.public_surface_violations()

        self.assertTrue(any("Tracon.OrnekTip" in line for line in found), found)

    def test_temiz_envanter_tarama_kapisini_kirmaz(self):
        runner = mock.Mock(return_value=mock.Mock(returncode=0, stdout="", stderr=""))
        with mock.patch.object(kapi.subprocess, "run", runner):
            self.assertEqual(kapi.public_surface_violations(), [])

    def test_scan_kanitsiz_public_tipte_kirmizi_doner(self):
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(kapi, "public_surface_violations", return_value=["Tracon.OrnekTip: kanitsiz"]), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            result = kapi.scan(pathlib.Path(directory))

        self.assertEqual(result, 1)
        self.assertIn("Tracon.OrnekTip", output.getvalue())

    def test_public_yuzey_repoda_kanitsiz_tip_icermez(self):
        # SINIF KAPISI: bu repoda gercek olan denetim.
        self.assertEqual(kapi.public_surface_violations(), [])

    # Context7 surumlu kimligi `previousVersions` ilk etiketinden uretir. Etiket son yayindan
    # geride kalirsa ajanlar kurulu paketin degil eski bir surumun metnini okur.

    def _context7_koku(self, directory: str, config: object, changelog_text: str | None) -> pathlib.Path:
        root = pathlib.Path(directory)
        text = config if isinstance(config, str) else json.dumps(config)
        (root / "context7.json").write_text(text, encoding="utf-8")
        if changelog_text is not None:
            (root / "CHANGELOG.md").write_text(changelog_text, encoding="utf-8")
        return root

    _CHANGELOG = "## [Unreleased]\n\n### Added\n- devam eden\n\n## [1.0.0-preview.6] - 2026-10-27\n"

    def test_context7_etiketi_son_yayinla_ayniysa_temiz(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_koku(
                directory, {"previousVersions": [{"tag": "v1.0.0-preview.6"}]}, self._CHANGELOG)

            self.assertEqual(kapi.context7_version_drift(root), [])

    def test_context7_etiketi_dize_olarak_da_okunur(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_koku(directory, {"previousVersions": ["v1.0.0-preview.6"]}, self._CHANGELOG)

            self.assertEqual(kapi.context7_version_drift(root), [])

    def test_context7_etiketi_son_yayindan_geride_kalirsa_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_koku(
                directory, {"previousVersions": [{"tag": "v1.0.0-preview.5"}]}, self._CHANGELOG)

            found = kapi.context7_version_drift(root)

        self.assertEqual(len(found), 1, found)
        self.assertIn("v1.0.0-preview.5", found[0])
        self.assertIn("v1.0.0-preview.6", found[0])

    def test_context7_bos_surum_listesi_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_koku(directory, {"previousVersions": []}, self._CHANGELOG)

            self.assertEqual(len(kapi.context7_version_drift(root)), 1)

    def test_context7_bozuk_json_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_koku(directory, "{ bozuk", self._CHANGELOG)

            self.assertEqual(len(kapi.context7_version_drift(root)), 1)

    # Yukseltme kapisinin tabani son yayin olmali: kapi son yayinin veritabanini bugunun migration'larina tasir.

    def _taban_koku(self, directory: str, manifest: object, changelog_text: str | None) -> pathlib.Path:
        root = pathlib.Path(directory)
        target = root / kapi.UPGRADE_BASELINE_MANIFEST
        target.parent.mkdir(parents=True)
        target.write_text(manifest if isinstance(manifest, str) else json.dumps(manifest), encoding="utf-8")
        if changelog_text is not None:
            (root / "CHANGELOG.md").write_text(changelog_text, encoding="utf-8")
        return root

    def test_yukseltme_tabani_son_yayinla_ayniysa_temiz(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._taban_koku(directory, {"version": "1.0.0-preview.6"}, self._CHANGELOG)

            self.assertEqual(kapi.upgrade_baseline_drift(root), [])

    def test_yukseltme_tabani_son_yayindan_geride_kalirsa_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._taban_koku(directory, {"version": "1.0.0-preview.5"}, self._CHANGELOG)

            found = kapi.upgrade_baseline_drift(root)

        self.assertEqual(len(found), 1, found)
        self.assertIn("1.0.0-preview.5", found[0])
        self.assertIn("1.0.0-preview.6", found[0])

    def test_yukseltme_tabani_bozuk_ya_da_surumsuzse_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._taban_koku(directory, "{ bozuk", self._CHANGELOG)
            self.assertEqual(len(kapi.upgrade_baseline_drift(root)), 1)

        with tempfile.TemporaryDirectory() as directory:
            root = self._taban_koku(directory, {"tag": "v1"}, self._CHANGELOG)
            self.assertEqual(len(kapi.upgrade_baseline_drift(root)), 1)

    def test_yukseltme_tabani_tarihli_yayin_yoksa_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._taban_koku(directory, {"version": "1.0.0-preview.6"}, "## [Unreleased]\n")

            self.assertEqual(len(kapi.upgrade_baseline_drift(root)), 1)

    def test_yukseltme_tabani_repoda_son_yayindir(self):
        # SINIF KAPISI: bu repoda gercek olan denetim.
        self.assertEqual(kapi.upgrade_baseline_drift(), [])

    def test_context7_tarihli_yayin_yoksa_kirar(self):
        with tempfile.TemporaryDirectory() as directory:
            root = self._context7_koku(
                directory, {"previousVersions": [{"tag": "v1.0.0-preview.6"}]}, "## [Unreleased]\n")

            self.assertEqual(len(kapi.context7_version_drift(root)), 1)

    def test_context7_dosyasi_yoksa_kontrol_atlanir(self):
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(kapi.context7_version_drift(pathlib.Path(directory)), [])

    def test_scan_yalniz_context7_kaymasiyla_kirmiziya_doner(self):
        # Ötekiler boş: sonucu yalnız etiket ayırt eder. Geçici kökte migration manifesti
        # yoktur; taklit edilmezse `scan` her iki durumda da 1 döner ve test bir şey kanıtlamaz.
        bos = {
            "find_sync_copies": [], "find_secrets": ([], 0), "migration_anchor_not_in_history": None,
            "migration_integrity_violations": [], "find_stale_doc_references": [],
            "public_surface_violations": [],
        }
        for tag, beklenen in (("v1.0.0-preview.6", 0), ("v1.0.0-preview.5", 1)):
            with self.subTest(tag=tag), tempfile.TemporaryDirectory() as directory, \
                    contextlib.ExitStack() as stack, contextlib.redirect_stdout(io.StringIO()) as output:
                for ad, deger in bos.items():
                    stack.enter_context(mock.patch.object(kapi, ad, return_value=deger))
                root = self._context7_koku(directory, {"previousVersions": [{"tag": tag}]}, self._CHANGELOG)
                result = kapi.scan(root)

            self.assertEqual(result, beklenen, output.getvalue())
            self.assertEqual("v1.0.0-preview.5" in output.getvalue(), beklenen == 1)

    def test_context7_surumu_repoda_son_yayinla_ayni(self):
        # SINIF KAPISI: bu repoda gercek olan denetim.
        self.assertEqual(kapi.context7_version_drift(), [])

    def test_harita_eksigi_tam_test_kosumuna_duser(self):
        projects, full = kapi.affected_test_projects(["src/Unknown.Package/Thing.cs"])

        self.assertEqual(projects, ["Tracon.Generators.UnitTests"])
        self.assertTrue(full)

    def test_her_src_degisikligi_ornek_derleyen_projeyi_secer(self):
        # KACIS ef9c8ee1: src/Tracon.Core'a eklenen bir XML <example>
        # blogu Tracon.Generators.UnitTests'i DERLENMEZ hale getirdi;
        # ic dongu Core.UnitTests + AspNetCore.FunctionalTests seciyordu ve
        # gercekte dusen projeyi HIC kosmuyordu. ExampleExtractor
        # src/**/*.cs'in tamamini okur, yalniz Generators'i degil.
        projects, full = kapi.affected_test_projects(
            ["src/Tracon.Core/Builder/ITraconBuilder.cs"])

        self.assertIn("Tracon.Generators.UnitTests", projects)
        self.assertFalse(full)

    def test_paylasimli_saglayici_kaynagi_dort_saglayici_projesini_secer(self):
        # src/Tracon.Providers.Shared bir paket degildir; haritada
        # olmasa her degisiklik tam kosuma duserdi. Etki alani dort projedir.
        for path in ("src/Tracon.Providers.Shared/ModelProviderCore.cs",
                     "tests/Shared/Providers/ModelProviderCoreTests.cs"):
            projects, full = kapi.affected_test_projects([path])

            for project in kapi.PROVIDER_TEST_PROJECTS:
                self.assertIn(project, projects, path)
            self.assertFalse(full, path)

    def test_paketlere_derlenen_paylasimli_kaynak_seceneklerin_proje_kapisini_da_secer(self):
        # Tracon.PackageOptions.UnitTests her saglayici, Mcp, ses, is akisi ve SQL paketinin ayar bolumunu
        # gercek kapsayicidan yurutur. Tracon.Providers.Shared ve Tracon.Sql.Shared bu paketlere derlenen paylasimli
        # kaynaktir; haritada o proje yokken bir kayit ya da dogrulama degisikligi seceneklerin kapisini
        # yalniz tam kosumda calistirirdi.
        for path in ("src/Tracon.Providers.Shared/ModelProviderCore.cs",
                     "src/Tracon.Sql.Shared/Stores/SqlJobStore.cs"):
            projects, full = kapi.affected_test_projects([path])

            self.assertIn("Tracon.PackageOptions.UnitTests", projects, path)
            self.assertFalse(full, path)

    def test_yukseltme_kapisinin_paylasimli_kaynagi_dort_projeyi_secer_tam_kosuma_dusmez(self):
        for path in ("tests/Shared/Upgrade/UpgradeSeed.json", "tests/Shared/Upgrade/UpgradeProbes.cs"):
            projects, full = kapi.affected_test_projects([path])

            for project in kapi.UPGRADE_GATE_TEST_PROJECTS:
                self.assertIn(project, projects, path)
            self.assertFalse(full, path)

    def test_openai_ortak_kaynagi_iki_saglayici_projesini_secer(self):
        # src/Tracon.OpenAI.Shared paket degildir; OpenAI ve Azure
        # paketlerine derlenir. Haritada olmasa her degisiklik tam kosuma duserdi.
        projects, full = kapi.affected_test_projects(
            ["src/Tracon.OpenAI.Shared/OpenAIPipelineImageGenerator.cs"])

        self.assertIn("Tracon.OpenAI.UnitTests", projects)
        self.assertIn("Tracon.Azure.UnitTests", projects)
        self.assertFalse(full)

    def test_sozlesme_paketi_ve_sql_ortak_kaynagi_tam_kosuma_dusmez(self):
        # 2026-10-13: Tracon.Testing.Contracts.Xunit ve
        # Tracon.Sql.Shared haritada yoktu; iki faz da ic donguyu her
        # degisiklikte tam kosuma genisletti ve kosum UI'siz derlendigi icin
        # E2E'nin tamami dustu. Etki alani, paketi derleyen ya da
        # sozlesmeyi tureten test projeleridir.
        sql_tests = ("Tracon.PostgreSql.IntegrationTests",
                     "Tracon.SqlServer.IntegrationTests",
                     "Tracon.Sqlite.IntegrationTests")
        expectations = {
            "src/Tracon.Testing.Contracts.Xunit/Contracts/JobStoreContract.cs": (
                "Tracon.Testing.Contracts.Xunit.UnitTests", "Tracon.Core.UnitTests",
                *kapi.PROVIDER_TEST_PROJECTS, *sql_tests),
            "src/Tracon.Sql.Shared/Stores/SqlJobStore.cs": (
                "Tracon.Sql.Shared.UnitTests", *sql_tests),
        }
        for path, expected in expectations.items():
            projects, full = kapi.affected_test_projects([path])

            self.assertFalse(full, path)
            for project in expected:
                self.assertIn(project, projects, path)
                self.assertTrue((ROOT / "tests" / project / f"{project}.csproj").exists(), project)

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
        # tests/Directory.Build.props HER test projesine ulasir. Bir onceki degisikliğe
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
            [kapi.Command(("dotnet", "test", "--project", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))]))
        self.assertFalse(kapi.runs_multi_target_tests(
            [kapi.Command(("dotnet", "test", "--project", "tests/Tracon.Generators.UnitTests/Tracon.Generators.UnitTests.csproj"))]))
        self.assertTrue(kapi.runs_multi_target_tests(
            [kapi.Command(("dotnet", "test", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))]))
        self.assertFalse(kapi.runs_multi_target_tests(
            [kapi.Command(("dotnet", "test", "tests/Tracon.Generators.UnitTests/Tracon.Generators.UnitTests.csproj"))]))
        self.assertFalse(kapi.runs_multi_target_tests([kapi.Command(("dotnet", "build", "Tracon.slnx"))]))

    def test_eksik_runtime_dotnet_root_altindan_bulunur(self):
        # Olculdu: test apphost'u runtime'i DOTNET_ROOT'tan cozer,
        # PATH'teki muxer'dan degil.
        with tempfile.TemporaryDirectory() as directory:
            shared = pathlib.Path(directory) / "shared" / "Microsoft.NETCore.App"
            for version in ("9.0.10", "10.0.0"):
                (shared / version).mkdir(parents=True)
            runner = mock.Mock()

            missing = kapi.missing_test_runtimes(
                ("net10.0", "net11.0"), environ={"DOTNET_ROOT": directory}, runner=runner)

        self.assertEqual(missing, ["net11.0"])
        runner.assert_not_called()

    def test_mimariye_ozel_dotnet_root_once_gelir(self):
        # Apphost DOTNET_ROOT_<ARCH>'i DOTNET_ROOT'tan ONCE okur.
        environ = {"DOTNET_ROOT": "/genel", "DOTNET_ROOT_ARM64": "/arm64"}

        self.assertEqual(kapi._apphost_dotnet_root(environ, "arm64"), "/arm64")
        self.assertEqual(kapi._apphost_dotnet_root(environ, "x86_64"), "/genel")
        self.assertEqual(kapi._apphost_dotnet_root({}, "arm64"), "")

    def test_eksik_runtime_global_kurulum_konumundan_bulunur(self):
        # Denetim bulgusu: DOTNET_ROOT yokken apphost PATH'teki
        # muxer'i DEGIL global kurulumu okur. PATH'e net11'li ozel bir SDK
        # konsa bile on kontrol global kokte net11 yoksa durdurmalidir.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            (kok / "etc").mkdir()
            (kok / "etc" / "install_location").write_text(str(kok / "x64") + "\n", encoding="utf-8")
            (kok / "etc" / "install_location_arm64").write_text(str(kok / "global") + "\n", encoding="utf-8")
            for version in ("9.0.10", "10.0.0"):
                (kok / "global" / "shared" / "Microsoft.NETCore.App" / version).mkdir(parents=True)
            runner = mock.Mock()

            missing = kapi.missing_test_runtimes(
                ("net10.0", "net11.0"), environ={}, runner=runner,
                system="Darwin", machine="arm64", install_location_dir=kok / "etc")

        self.assertEqual(missing, ["net11.0"])
        runner.assert_not_called()

    def test_global_kurulum_dosyasi_yoksa_varsayilan_kok_kullanilir(self):
        with tempfile.TemporaryDirectory() as directory:
            yok = pathlib.Path(directory) / "etc"

            self.assertEqual(kapi._global_dotnet_root("Linux", "x86_64", yok), "/usr/share/dotnet")
            self.assertEqual(kapi._global_dotnet_root("Darwin", "arm64", yok), "/usr/local/share/dotnet")
            self.assertIsNone(kapi._global_dotnet_root("Windows", "amd64", yok))

    def test_windowsta_list_runtimes_ciktisindan_bulunur(self):
        stdout = (
            "Microsoft.AspNetCore.App 11.0.0 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]\n"
            "Microsoft.NETCore.App 9.0.20 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n"
            "Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n")
        runner = mock.Mock(return_value=mock.Mock(returncode=0, stdout=stdout))

        with contextlib.redirect_stdout(io.StringIO()) as output:
            ok = kapi.require_test_runtimes(("net10.0", "net11.0"), environ={}, runner=runner, system="Windows")

        # ASP.NET Core 11 runtime'i NETCore 11 yerine SAYILMAZ.
        self.assertFalse(ok)
        self.assertIn("net11.0", output.getvalue())
        # Denetim bulgusu: ayrac ile DEGIL platformun kendi yol
        # bicimiyle karsilastirilir - windows-latest'te PurePath ters egik
        # cizgi uretir (test_mtp_filtresi_filter_class_uretir ile ayni sinif).
        self.assertIn(str(pathlib.PurePath("/usr/share/dotnet")), output.getvalue())

    def test_runtime_listesi_okunamazsa_engellemez(self):
        # dotnet yoksa asil komut zaten 127 ile durur; on kontrol ikinci bir
        # yaniltici hata uretmez. Global kok bulunamazsa da ayni kural.
        runner = mock.Mock(side_effect=FileNotFoundError("dotnet"))

        self.assertEqual(
            kapi.missing_test_runtimes(("net11.0",), environ={}, runner=runner, system="Windows"), [])
        with tempfile.TemporaryDirectory() as directory:
            self.assertEqual(
                kapi.missing_test_runtimes(
                    ("net11.0",), environ={"DOTNET_ROOT": str(pathlib.Path(directory) / "yok")}, runner=runner),
                [])

    def test_diger_paylasimli_test_kaynagi_hala_tam_kosum_ister(self):
        projects, full = kapi.affected_test_projects(["tests/Shared/Infrastructure/ProcessRunner.cs"])

        self.assertTrue(full)

    def test_embedded_ornegi_kendi_test_projesini_secer(self):
        # KACIS 6e9cc8f6: altinci genisleme noktasini ekledi,
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

    def test_tam_kosum_komutu_mtp_bayraklarini_tasir(self):
        # `dotnet test` MTP kipindedir. `-maxcpucount` bu kipte HIC TEST
        # KOSTURMADAN cikis kodu 5 verir; `--` ayiricisi da artik yoktur.
        command = kapi.full_solution_test_command()
        args = command.args

        self.assertEqual(args[:4], ("dotnet", "test", "--solution", "Tracon.slnx"))
        self.assertNotIn("--", args)
        self.assertFalse([arg for arg in args if arg.startswith("-maxcpucount")])
        self.assertEqual(args[args.index("--max-parallel-test-modules") + 1], str(kapi.TEST_MAX_CPU_COUNT))
        self.assertIn("--report-trx", args)
        self.assertEqual(args[args.index("--results-directory") + 1], kapi.TEST_RESULTS_DIRECTORY)
        self.assertEqual(args[args.index("--results-directory-layout") + 1], "per-module")

    def test_ci_ve_kapi_ayni_trx_yolunu_ve_bayraklari_kullanir(self):
        # Kapi (kapi.py) ile CI (ci.yml) AYRI yazilmis iki komuttur ve bir tarafin
        # TRX yolunu degistirmek digerini sessizce bosaltir: CI yuklemesi artifact
        # bulamaz (`if-no-files-found: ignore`) ve kimse fark etmez.
        ci = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        test_lines = [
            line.strip() for line in ci.splitlines()
            if line.strip().startswith("run: dotnet test ") and "Tracon" in line
        ]

        # Test isleri modul basina bir adimdir (`--project`); cozum komutu yalniz kultur isinde kalir
        # (Docker'siz solution filter). Hepsi ayni TRX bayraklarini tasir ve derlemez (`--no-build`).
        self.assertGreaterEqual(len([line for line in test_lines if "--solution" in line]), 1, test_lines)
        projeler = {re.search(r"--project tests/([\w.]+)/", line).group(1) for line in test_lines if "--project" in line}
        self.assertGreaterEqual(len(projeler), 25, projeler)  # ayristirici bayatlarsa sessiz sifira dusmesin
        for line in test_lines:
            self.assertNotIn("-maxcpucount", line)
            self.assertIn(" --no-build", line)
            if "--solution" in line:
                # Cozum komutu modulleri tek tek kosar; tek projeli komutun paralel modulu yoktur.
                self.assertIn("--max-parallel-test-modules 1", line)
            self.assertIn("--report-trx", line)
            self.assertIn(f"--results-directory {kapi.TEST_RESULTS_DIRECTORY}", line)
            self.assertIn("--results-directory-layout per-module", line)
        self.assertIn(f"path: {kapi.TEST_RESULTS_DIRECTORY}/**/*.trx", ci)
        self.assertNotIn("TestResults/*.trx", ci)

    def test_ci_test_isleri_kirmizi_ya_da_bos_kosuyu_yesil_gostermez(self):
        metin = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")

        self.assertEqual(_ci_sessiz_yesil_sorunlari(metin), [])
        # Ayrıştırıcı gerçekten okuyor (sessizce sıfır iş görmesin): restore kullanan on üç iş, on dört iş restore
        # tanımlar, on dört TRX yüklemesi.
        kullananlar = [ad for ad in _ci_isleri(metin) if "steps.restore" in _is_blogu(metin, ad)]
        self.assertGreaterEqual(len(kullananlar), 13, kullananlar)
        self.assertGreaterEqual(metin.count("        id: restore\n"), 14)
        self.assertGreaterEqual(metin.count("path: artifacts/test-results/**/*.trx\n"), 14)

        def bozulmus(eski: str, yeni: str) -> list[str]:
            self.assertIn(eski, metin)
            return _ci_sessiz_yesil_sorunlari(metin.replace(eski, yeni, 1))

        restore = "      - name: Bagimliliklari geri yukle\n        id: restore\n"
        self.assertEqual(len(bozulmus(restore, "      - name: Bagimliliklari geri yukle\n")), 1)
        self.assertEqual(len(bozulmus(restore, "      - name: Bagimliliklari geri yukle\n        id: restor\n")), 1)
        trx_sonu = "          if-no-files-found: error\n          overwrite: true\n"
        self.assertEqual(len(bozulmus(trx_sonu, "          if-no-files-found: ignore\n          overwrite: true\n")), 1)
        self.assertEqual(len(bozulmus(trx_sonu, "          if-no-files-found: error\n")), 1)
        self.assertEqual(
            len(bozulmus("          name: nuget-packages\n", "          name: nuget-packages\n          overwrite: true\n")), 1)
        self.assertEqual(
            len(bozulmus("          name: bin-${{ matrix.os }}\n", "          name: bin-${{ matrix.os }}\n          overwrite: true\n")), 1)

    def _trx(self, kok: pathlib.Path, proje: str, sonuclar: list[tuple[str, str]], cikti: str = "release") -> None:
        # MTP 2 `--results-directory-layout per-module`: <proje>/<tfm>_<mimari>/*.trx.
        # `codeBase` testin kosan ikilisinin yolunu tasir; Windows'ta ters egik cizgiyle gelir.
        tfm = "net10.0" if cikti == "release" else cikti.removeprefix("release_")
        dizin = kok / kapi.TEST_RESULTS_DIRECTORY / proje / f"{tfm}_arm64"
        dizin.mkdir(parents=True, exist_ok=True)
        satirlar = "".join(
            f'<UnitTestResult testName="{ad}" outcome="{durum}" />' for ad, durum in sonuclar)
        (dizin / "r.trx").write_text(
            '<?xml version="1.0"?><TestRun xmlns='
            '"http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f"<Results>{satirlar}</Results>"
            f'<TestDefinitions><UnitTest><TestMethod codeBase="C:\\r\\artifacts\\bin\\{proje}\\{cikti}\\{proje}.dll"'
            ' className="C" name="M" /></UnitTest></TestDefinitions></TestRun>',
            encoding="utf-8")

    def test_eski_trx_yeri_artik_taranmaz(self):
        # TRX ikilinin yanindaki TestResults/'a degil `--results-directory`'ye
        # yazilir. Eski yoldaki bir dosya DUSEN TEST SAYILMAZ - yoksa taranan yer ile
        # yazilan yer ayrisir ve teshis sessizce bos kalir.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            eski = kok / "artifacts" / "bin" / "P" / "release" / "TestResults"
            eski.mkdir(parents=True)
            (eski / "r.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                '<UnitTestResult testName="N.S.Dusen" outcome="Failed" /></Results></TestRun>',
                encoding="utf-8")

            self.assertEqual(kapi.failed_tests_from_trx(0, kok), [])

    def test_trx_cikti_klasoru_codebase_ten_okunur_yoksa_tek_hedefli_release_varsayilir(self):
        # `codeBase` olmayan dal testsizdi. Cok hedefli bacak klasorunu TRX soyler;
        # `codeBase` yoksa tek hedefli projenin `release` klasoru varsayilir.
        def agac(metot: str):
            return ElementTree.ElementTree(ElementTree.fromstring(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
                f"<TestDefinitions><UnitTest>{metot}</UnitTest></TestDefinitions></TestRun>"))

        self.assertEqual(
            kapi._trx_output_directory(agac('<TestMethod codeBase="C:\\r\\bin\\P\\release_net10.0\\P.dll" name="M" />')),
            "release_net10.0")
        self.assertEqual(
            kapi._trx_output_directory(agac('<TestMethod codeBase="/r/bin/P/release/P.dll" name="M" />')),
            "release")
        self.assertEqual(kapi._trx_output_directory(agac('<TestMethod name="M" />')), "release")
        self.assertEqual(kapi._trx_output_directory(agac('<TestMethod codeBase="P.dll" name="M" />')), "release")

    def test_sifir_test_koşan_yesil_kosum_kirmizi_sayilir(self):
        # NEGATIF KONTROL: cikis kodu 0 "test kostu" demek degildir. TRX yoksa
        # ya da bossa kapi YESIL donmez.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(kapi.require_executed_tests(0, kok), kapi.ZERO_TESTS_EXIT_CODE)
            self.assertIn("koşulmuş test yok", output.getvalue())

            self._trx(kok, "P", [("N.S.Gecen", "Passed"), ("N.S.Dusen", "Failed")])
            self._trx(kok, "Q", [("N.S.Gecen", "Passed")], cikti="release_net10.0")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertEqual(kapi.require_executed_tests(0, kok), 0)
            self.assertIn("3 test, 2 modül", output.getvalue())

    def test_run_commands_cikis_kodu_sifir_ama_test_yoksa_durur(self):
        runner = mock.Mock(return_value=mock.Mock(returncode=0))
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(kapi, "executed_tests_from_trx", return_value=(0, 0)), \
                contextlib.redirect_stdout(io.StringIO()):
            result = kapi.run_commands(
                "test", [kapi.full_solution_test_command()], runner=runner,
                measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, kapi.ZERO_TESTS_EXIT_CODE)

    def test_run_commands_trx_istemeyen_komutu_sifir_test_diye_kirmaz(self):
        # `dotnet build` ya da tek proje komutu TRX uretmez; kapi onlara sayi sormaz.
        runner = mock.Mock(return_value=mock.Mock(returncode=0))
        with tempfile.TemporaryDirectory() as directory, \
                mock.patch.object(kapi, "executed_tests_from_trx", return_value=(0, 0)), \
                contextlib.redirect_stdout(io.StringIO()):
            result = kapi.run_commands(
                "test", [kapi.Command(("dotnet", "build", "Tracon.slnx")), kapi._dotnet_test_project("P")],
                runner=runner, measurement_path=pathlib.Path(directory) / "m.jsonl")

        self.assertEqual(result, 0)

    def test_contract_paketi_xunit_alt_sinirini_korur(self):
        # Iki alt paketin `PackageVersion`'i 4.x'e cikarsa paket
        # xunit.v3 3.x tuketicisini sessizce zorlar. Kapi nuspec'e bakar.
        proje = "Tracon.Testing.Contracts.Xunit"
        iyi = ('<dependency id="xunit.v3.extensibility.core" version="3.2.2" exclude="Build,Analyzers" />'
               '<dependency id="xunit.v3.assert" version="3.2.2" />')
        self.assertEqual(kapi.dependency_floor_errors(proje, iyi + iyi), [])
        self.assertEqual(kapi.dependency_floor_errors("Tracon.Core", ""), [])

        yukseltilmis = iyi.replace('"xunit.v3.assert" version="3.2.2"', '"xunit.v3.assert" version="4.0.2"')
        hatalar = kapi.dependency_floor_errors(proje, yukseltilmis)
        self.assertEqual(len(hatalar), 1)
        self.assertIn("xunit.v3.assert alt sınırı 4.0.2", hatalar[0])

        eksik = kapi.dependency_floor_errors(proje, '<dependency id="xunit.v3.assert" version="3.2.2" />')
        self.assertEqual(len(eksik), 1)
        self.assertIn("xunit.v3.extensibility.core bağımlılığı nuspec'te yok", eksik[0])

    def test_depodaki_xunit_alt_siniri_props_ile_ayni(self):
        props = (ROOT / "Directory.Packages.props").read_text(encoding="utf-8")
        for paket, alt_sinir in kapi.SHIPPED_DEPENDENCY_FLOORS["Tracon.Testing.Contracts.Xunit"].items():
            self.assertIn(f'<PackageVersion Include="{paket}" Version="{alt_sinir}" />', props)

    def test_tek_proje_komutu_project_bayragini_kullanir(self):
        self.assertEqual(
            kapi._dotnet_test_project("P").args,
            ("dotnet", "test", "--project", "tests/P/P.csproj", "-c", "Release", "--no-build"))

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
        # Yalniz release/ taransaydi net11.0 bacaginda dusen test
        # hic raporlanmaz, izole kosum da tetiklenmezdi.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._trx(kok, "Tracon.Core.UnitTests", [("N.S.Dusen", "Failed")], cikti="release_net11.0")
            self._trx(kok, "Tracon.Core.UnitTests", [("N.S.Dusen", "Passed")], cikti="release_net10.0")

            dusenler = kapi.failed_tests_from_trx(0, kok)

        self.assertEqual(dusenler, [kapi.FailedTest("Tracon.Core.UnitTests", "release_net11.0", "N.S.Dusen")])
        self.assertEqual(dusenler[0].label, "Tracon.Core.UnitTests (net11.0)")

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
        self.assertIn("yeniden koşturma", output.getvalue())
        self.assertIn("kök sebebi", output.getvalue())
        self.assertIn("ürün yarışı", output.getvalue())
        self.assertNotIn("TEKRAR koş", output.getvalue())

    def _sureli_trx(self, kok: pathlib.Path, proje: str, sonuclar: list[tuple[str, str]]) -> None:
        dizin = kok / kapi.TEST_RESULTS_DIRECTORY / proje / "net10.0_arm64"
        dizin.mkdir(parents=True)
        satirlar = "".join(
            f'<UnitTestResult testName="{ad}" duration="{sure}" outcome="Passed" />' for ad, sure in sonuclar)
        (dizin / "sonuc.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f"<Results>{satirlar}</Results></TestRun>",
            encoding="utf-8")

    def test_sure_kaydi_test_basina_milisaniye_yazar(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [("N.S.A", "00:00:01.5000000"), ("N.S.B", "00:01:00.0100000")])

            with contextlib.redirect_stdout(io.StringIO()):
                yol = kapi.record_test_durations(0.0, root=kok)

            with gzip.open(yol, "rt", encoding="utf-8") as dosya:
                kayit = json.load(dosya)

        self.assertEqual(kayit["modules"], {"P/net10.0_arm64": {"N.S.A": 1500, "N.S.B": 60010}})

    def test_sure_kaydi_ayni_adli_testin_en_uzununu_tutar(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [("N.S.A", "00:00:00.1000000"), ("N.S.A", "00:00:00.3000000")])

            with contextlib.redirect_stdout(io.StringIO()):
                yol = kapi.record_test_durations(0.0, root=kok)

            with gzip.open(yol, "rt", encoding="utf-8") as dosya:
                kayit = json.load(dosya)

        self.assertEqual(kayit["modules"]["P/net10.0_arm64"], {"N.S.A": 300})

    def test_sure_kaydi_trx_yoksa_dosya_yazmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)

            self.assertIsNone(kapi.record_test_durations(0.0, root=kok))
            self.assertFalse((kok / kapi.TEST_DURATIONS_DIRECTORY).exists())

    def test_sure_kaydi_cozulemeyen_sureyi_atlar(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [("N.S.A", "bozuk"), ("N.S.B", "00:00:00.0020000")])

            with contextlib.redirect_stdout(io.StringIO()):
                yol = kapi.record_test_durations(0.0, root=kok)

            with gzip.open(yol, "rt", encoding="utf-8") as dosya:
                kayit = json.load(dosya)

        self.assertEqual(kayit["modules"]["P/net10.0_arm64"], {"N.S.B": 2})

    def test_sure_kaydi_bozuk_trx_ile_kapiyi_kirmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            dizin = kok / kapi.TEST_RESULTS_DIRECTORY / "P" / "net10.0_arm64"
            dizin.mkdir(parents=True)
            (dizin / "sonuc.trx").write_text("<TestRun", encoding="utf-8")

            self.assertIsNone(kapi.record_test_durations(0.0, root=kok))

    def test_sure_kaydi_bos_trx_ile_kapiyi_kirmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [])

            with contextlib.redirect_stdout(io.StringIO()):
                yol = kapi.record_test_durations(0.0, root=kok)

            with gzip.open(yol, "rt", encoding="utf-8") as dosya:
                kayit = json.load(dosya)

        self.assertEqual(kayit["modules"], {"P/net10.0_arm64": {}})

    def test_sure_kaydi_yazilamazsa_uyarir_ve_kapiyi_kirmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [("N.S.A", "00:00:00.0010000")])

            with mock.patch.object(kapi.gzip, "open", side_effect=OSError("disk dolu")), \
                    contextlib.redirect_stdout(io.StringIO()) as output:
                self.assertIsNone(kapi.record_test_durations(0.0, root=kok))

        self.assertIn("yazılamadı", output.getvalue())

    def test_sure_kaydi_eski_dosyalari_siler(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [("N.S.A", "00:00:00.0010000")])
            kayitlar = kok / kapi.TEST_DURATIONS_DIRECTORY
            kayitlar.mkdir(parents=True)
            for sira in range(kapi.TEST_DURATIONS_KEEP + 3):
                (kayitlar / f"20000101T0000{sira:02d}Z.json.gz").write_bytes(b"")

            with contextlib.redirect_stdout(io.StringIO()):
                kapi.record_test_durations(0.0, root=kok)

            kalan = sorted(path.name for path in kayitlar.glob("*.json.gz"))

        self.assertEqual(len(kalan), kapi.TEST_DURATIONS_KEEP)
        self.assertNotIn("20000101T000000Z.json.gz", kalan)

    def _zamanli_trx(self, kok: pathlib.Path, proje: str, baslangic: str, bitis: str) -> None:
        dizin = kok / kapi.TEST_RESULTS_DIRECTORY / proje / "net10.0_arm64"
        dizin.mkdir(parents=True)
        (dizin / "sonuc.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<Times creation="{baslangic}" queuing="{baslangic}" start="{baslangic}" finish="{bitis}" />'
            '<Results><UnitTestResult testName="N.S.A" duration="00:00:01" outcome="Passed" /></Results></TestRun>',
            encoding="utf-8")

    def test_modul_suresi_siniri_asarsa_kapi_kirmizi(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._zamanli_trx(kok, "P", "2026-11-09T11:03:40.3574990Z", "2026-11-09T11:12:25.866393Z")

            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits={"P": 500})

        self.assertEqual(kod, kapi.MODULE_TIME_LIMIT_EXIT_CODE)
        self.assertIn("P/net10.0_arm64 526 s > 500 s", output.getvalue())
        self.assertIn("ölç", output.getvalue())

    def test_modul_suresi_sinirin_altindaysa_yesil_ve_basilir(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._zamanli_trx(kok, "P", "2026-11-09T11:00:00Z", "2026-11-09T11:03:59.9Z")

            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits={"P": 240})

        self.assertEqual(kod, 0)
        self.assertIn("P/net10.0_arm64: 240 s · sınır 240 s · kalan 0 s (%0)", output.getvalue())

    def test_siniri_olmayan_modul_kapiyi_kirmaz_ama_suresi_raporlanir(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._zamanli_trx(kok, "Q", "2026-11-09T11:00:00Z", "2026-11-09T13:00:00Z")

            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits={"P": 1})

        self.assertEqual(kod, 0)
        self.assertIn("Q/net10.0_arm64: 7200 s · sınır yok", output.getvalue())

    def test_suresi_okunamayan_trx_uyarir_ve_kapiyi_kirmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._sureli_trx(kok, "P", [("N.S.A", "00:00:01")])

            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits={"P": 1})

        self.assertEqual(kod, 0)
        self.assertIn("okunamadı", output.getvalue())

    def test_trx_zaman_damgasi_yedi_haneli_kesri_ve_saat_dilimini_okur(self):
        self.assertAlmostEqual(
            kapi._trx_instant("2026-11-09T11:00:01.1234567+01:00") - kapi._trx_instant("2026-11-09T10:00:00Z"),
            1.123456, places=5)
        self.assertIsNone(kapi._trx_instant("dun"))
        self.assertIsNone(kapi._trx_instant(None))

    def test_modul_sure_butceleri_yalniz_duser_ve_gercek_modullere_aittir(self):
        # Butceler yalniz duser; bu test bugunku degerleri sabitler ki bir
        # yukseltme ya da butcenin silinmesi gozden kacmasin (kapi.py yorumundaki kural).
        # Yeni giris eklemek bu tabloya da satir eklemektir; mevcut satir yalniz kucultulebilir.
        sabit = {
            "Tracon.SqlServer.IntegrationTests": 400,
            "Tracon.TwoNode.IntegrationTests": 540,
            "Tracon.Ui.E2ETests": 40,
            "Tracon.AspNetCore.FunctionalTests": 400,
            "Tracon.PostgreSql.IntegrationTests": 170,
            "Tracon.Package.Tests": 310,
            "Tracon.Core.UnitTests": 50,
            "Tracon.Workflows.UnitTests": 40,
            "Tracon.Generators.UnitTests": 30,
            "Tracon.Sqlite.IntegrationTests": 90,
        }
        butceler = kapi.TEST_MODULE_TIME_LIMITS_SECONDS

        for modul, sinir in sabit.items():
            with self.subTest(modul=modul):
                self.assertIn(modul, butceler, "butce silinemez")
                self.assertLessEqual(butceler[modul], sinir, "butce yukseltilemez")
        moduller = set(_slnx_test_modulleri())
        self.assertEqual(sorted(set(butceler) - moduller), [], "butce bir test modulune ait olmali")
        self.assertTrue(all(isinstance(sinir, int) and sinir > 0 for sinir in butceler.values()))

    def test_yesil_tam_kosum_sure_sinirini_denetler(self):
        komut = kapi.full_solution_test_command()
        with mock.patch.object(kapi, "require_executed_tests", return_value=0), \
                mock.patch.object(kapi, "enforce_module_time_limits", return_value=kapi.MODULE_TIME_LIMIT_EXIT_CODE) as sinir, \
                mock.patch.object(kapi, "record_test_durations"), \
                mock.patch.object(kapi, "isolate_failed_tests", return_value=[]), \
                mock.patch.object(kapi, "failed_tests_from_trx", return_value=[]), \
                mock.patch.object(kapi, "_record_measurement"), \
                contextlib.redirect_stdout(io.StringIO()):
            kod = kapi.run_commands("test", [komut], runner=mock.Mock(return_value=mock.Mock(returncode=0)))

        self.assertEqual(kod, kapi.MODULE_TIME_LIMIT_EXIT_CODE)
        sinir.assert_called_once()

    def _sure_sinirli_kosum(self, komut, *, cikis):
        with mock.patch.object(kapi, "require_executed_tests", return_value=0), \
                mock.patch.object(kapi, "enforce_module_time_limits", return_value=kapi.MODULE_TIME_LIMIT_EXIT_CODE) as sinir, \
                mock.patch.object(kapi, "record_test_durations"), \
                mock.patch.object(kapi, "isolate_failed_tests", return_value=[]), \
                mock.patch.object(kapi, "failed_tests_from_trx", return_value=[]), \
                mock.patch.object(kapi, "_record_measurement"), \
                contextlib.redirect_stdout(io.StringIO()):
            kod = kapi.run_commands("test", [komut], runner=mock.Mock(return_value=mock.Mock(returncode=cikis)))
        return kod, sinir

    def test_kirmizi_tam_kosum_cikis_kodunu_korur_ve_sure_sinirini_denetlemez(self):
        # Dusen bir test sure siniri kodu 9 ile ezilmemeli: kirmizinin sebebi test.
        kod, sinir = self._sure_sinirli_kosum(kapi.full_solution_test_command(), cikis=2)

        self.assertEqual(kod, 2)
        sinir.assert_not_called()

    def test_trx_istemeyen_dotnet_test_sure_sinirini_denetlemez(self):
        komut = kapi.Command(("dotnet", "test", "--project", "P.csproj"))
        self.assertFalse(kapi.reports_trx(komut))

        kod, sinir = self._sure_sinirli_kosum(komut, cikis=0)

        self.assertEqual(kod, 0)
        sinir.assert_not_called()

    def test_izole_kosum_dusen_bacagin_ikilisini_kosar(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            for cikti in ("release_net10.0", "release_net11.0"):
                ikili = kok / "artifacts" / "bin" / "P" / cikti
                ikili.mkdir(parents=True)
                (ikili / "P").write_text("", encoding="utf-8")
            runner = mock.Mock(return_value=mock.Mock(returncode=1))

            with contextlib.redirect_stdout(io.StringIO()) as output:
                hukumler = kapi.isolate_failed_tests(
                    [kapi.FailedTest("P", "release_net11.0", "N.S.Dusen")], runner=runner, root=kok)

        self.assertEqual(pathlib.PurePath(runner.call_args.args[0][0]).parts[-2], "release_net11.0")
        self.assertEqual(hukumler, [("P (net11.0)", "N.S.Dusen", False)])
        self.assertIn("P (net11.0) · N.S.Dusen", output.getvalue())

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

    def test_e2e_kosan_ic_dongu_arayuzlu_derler(self):
        # Haritası eksik bir yol iç döngüyü tam
        # koşuma genişletti ama derleme `-p:TraconFrontendEnabled=false` ile
        # kaldı; gömülü varlık olmadan E2E'nin tamamı düştü ve gerçek bir
        # regresyon o gürültünün içinde kayboldu. E2E'yi seçen her yol (tam
        # koşum ya da E2E projesinin kendisi) arayüzlü derlemelidir.
        for path in ("src/Unknown.Package/Thing.cs",
                     "tests/Shared/Infrastructure/ProcessRunner.cs",
                     "tests/Tracon.Ui.E2ETests/RunsPageTests.cs"):
            with contextlib.redirect_stdout(io.StringIO()):
                build = kapi.inner_loop_commands([path])[0]

            self.assertEqual(build.args[:2], ("dotnet", "build"), path)
            self.assertNotIn("-p:TraconFrontendEnabled=false", build.args, path)

    def test_e2e_kosmayan_ic_dongu_arayuzsuz_derler(self):
        build = kapi.inner_loop_commands(["src/Tracon.Core/Thing.cs"])[0]

        self.assertIn("-p:TraconFrontendEnabled=false", build.args)

    def test_ic_dongu_tabani_commit_edilen_isi_de_gorur(self):
        # Iş commit edildikten sonra `ic-dongu` HEAD'e karşı hiçbir şey
        # görmüyor, yalnız build koşup "yeşil" diyordu (iki derlenmeyen
        # <example> bu yüzden bir commit boyunca görünmedi). `--taban` aralığı verir.
        with mock.patch.object(kapi, "changed_paths", return_value=["src/Tracon.Core/Thing.cs"]) as paths, \
                contextlib.redirect_stdout(io.StringIO()):
            code = kapi.main(["--komutlari-bas", "ic-dongu", "--taban", "abc123"])

        self.assertEqual(code, 0)
        paths.assert_called_once_with("abc123")

    def test_ic_dongu_bos_degisiklikte_tabani_hatirlatir(self):
        with mock.patch.object(kapi, "changed_paths", return_value=[]), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            kapi.main(["--komutlari-bas", "ic-dongu"])

        self.assertIn("--taban", output.getvalue())

    def test_kapanis_komutlari_dort_net_kapisini_tasir(self):
        commands = kapi.closing_commands("abc123", site=False)
        rendered = [command.display for command in commands]

        self.assertIn("dotnet build Tracon.slnx -c Release", rendered)
        # Tam test koşumu tek `--solution Tracon.slnx` komutu değil, aşamalıdır (AsamaliTestKosumuTestleri).
        for asama in kapi.staged_test_commands():
            self.assertIn(asama.display, rendered)
        self.assertNotIn(kapi.full_solution_test_command().display, rendered)
        # TraconSkipCleanWorkingTreeCheck: this pack validates the
        # packaging CONTRACT during iteration, not a release candidate - it must
        # not be blocked by the new dirty-tree gate the way `kapi.py yayin` is.
        self.assertIn(
            "dotnet pack Tracon.slnx -c Release --no-build -p:TraconSkipCleanWorkingTreeCheck=true",
            rendered)
        self.assertIn("dotnet format Tracon.slnx --verify-no-changes", rendered)

    def test_kapanis_uretimle_baslar_ve_tam_testle_biter(self):
        # Ekim 2026: 42 kırmızı kapanışın 11'i bayat agent haritasında, 11'i
        # testlerden SONRA koşan bir adımda düştü. Üretim ilk, en pahalı adım son.
        rendered = [c.display for c in kapi.closing_commands("abc123", site=True, performance=True)]
        uretim = [c.display for c in kapi.generation_commands()]

        self.assertEqual(rendered[:len(uretim)], uretim)
        # Testler en sonda ve bitişik: aşamalı koşumun her komutu `dotnet test`, başka hiçbir adım değil.
        testler = [c.display for c in kapi.staged_test_commands()]
        self.assertEqual(rendered[-len(testler):], testler)
        self.assertEqual(sum(1 for c in rendered if c.startswith("dotnet test")), len(testler))
        test_sirasi = len(rendered) - len(testler)
        for ucuz in (
            "dotnet format Tracon.slnx --verify-no-changes",
            "dotnet pack Tracon.slnx -c Release --no-build -p:TraconSkipCleanWorkingTreeCheck=true",
            "(cd docs-site && npm run check)",
            "python3 scripts/kapi.py performans",
        ):
            self.assertLess(rendered.index(ucuz), test_sirasi, ucuz)
        # DocFX derlemenin yazdığı XML dokümanını okur: site kapısı derlemeden sonra.
        self.assertLess(rendered.index("dotnet build Tracon.slnx -c Release"),
                        rendered.index("(cd docs-site && npm run check)"))
        # Site (~35 sn, geç kırmızıların 6/11'i) format'tan (~2 dk) önce.
        self.assertLess(rendered.index("(cd docs-site && npm run check)"),
                        rendered.index("dotnet format Tracon.slnx --verify-no-changes"))

    def test_kapanis_restore_u_zorlar_ve_derlemeden_once_kosar(self):
        # önbellekli restore yeni yayımlanan güvenlik uyarısını görmez
        # (`NU1902`/`NU1903` yalnız soğuk ya da zorlanmış restore'da çıkar). Kapanış
        # sıfır uyarıyı ancak denetimi gerçekten koşturursa kanıtlar.
        rendered = [c.display for c in kapi.closing_commands("abc123", site=False)]

        self.assertIn("dotnet restore Tracon.slnx --force", rendered)
        self.assertLess(rendered.index("dotnet restore Tracon.slnx --force"),
                        rendered.index("dotnet build Tracon.slnx -c Release"))

    def test_kapanis_agent_haritasini_uretir_denetlemez(self):
        # Yerelde üretim denetimi gereksiz kılar; `--check` CI'da kalır (ci.yml).
        rendered = [c.display for c in kapi.closing_commands("abc123", site=False)]

        self.assertIn("node docs-site/scripts/build-agent-map.mjs", rendered)
        self.assertNotIn("node docs-site/scripts/build-agent-map.mjs --check", rendered)

    def test_public_komut_listeleri_ozel_betik_cagirmaz(self):
        # Public ağaç yalnız kendi dosyalarıyla koşar: sevk edilen kapı özel bir
        # betiği çağırırsa süzülmüş ağaçta ilk adımda düşer.
        for rendered in (
            [c.display for c in kapi.closing_commands("abc123", site=True, performance=True)],
            [c.display for c in kapi.generation_commands()],
        ):
            for command in rendered:
                self.assertNotIn(".agents", command)
            # Every script the public lists run exists in the public `scripts/` directory.
            for command in [*kapi.closing_commands("abc123", site=True, performance=True), *kapi.generation_commands()]:
                if command.args[0] == "python3" and command.args[1].endswith(".py"):
                    self.assertTrue((ROOT / command.args[1]).is_file(), command.args[1])

    def test_genisletme_adimlari_public_listeye_eklenir(self):
        ek = kapi.Extension(
            program="ozel/giris.py",
            generation=(kapi.Command(("echo", "uret")),),
            after_scan=lambda taban: (kapi.Command(("echo", taban)),),
        )

        rendered = [c.display for c in kapi.closing_commands("abc123", site=False, extension=ek)]

        self.assertEqual(rendered[0], "echo uret")
        self.assertEqual(rendered[rendered.index("python3 ozel/giris.py tarama") + 1], "echo abc123")
        self.assertNotIn("python3 scripts/kapi.py tarama", rendered)

    def test_tarama_kokleri_ozel_dizin_olmadan_calisir(self):
        # Süzülmüş ağaçta `docs/` yalnız iki sözleşme dizini taşır ve başka kök yoktur.
        with tempfile.TemporaryDirectory() as gecici:
            kok = pathlib.Path(gecici)
            (kok / "src").mkdir()
            (kok / "docs" / "sozlesme").mkdir(parents=True)
            (kok / "docs" / "sozlesme" / "hata-kodlari.txt").write_text("x", encoding="utf-8")

            self.assertEqual(kapi.find_sync_copies(kok), [])
            self.assertEqual(kapi.find_stale_doc_references(kok), [])
            self.assertEqual(kapi.find_secrets(kok), ([], 0))

    def test_ci_her_is_kanonik_repo_korumasina_baglidir(self):
        # Özel depoda ve fork'larda hiçbir iş dakika harcamamalı.
        isler = _ci_isleri((ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8"))

        self.assertEqual(isler["build"]["if"], KANONIK_REPO_KOSULU)
        self.assertEqual(_korumasiz_isler(isler), [])

    def test_ci_korumasi_zinciri_delen_isi_yakalar(self):
        metin = (
            "jobs:\n"
            "  build:\n"
            "    if: github.repository == 'farukatasoy/Tracon'\n"
            "  rapor:\n"
            "    needs: build\n"
            "    if: always()\n"
            "  ozet:\n"
            "    needs: [build]\n"
            "    if: >-\n"
            "      github.ref == 'refs/heads/main' &&\n"
            "      !cancelled()\n"
            "    runs-on: ubuntu-latest\n"
            "  guvenli:\n"
            "    needs: build\n"
            "    if: always() && github.repository == 'farukatasoy/Tracon'\n"
            "  basarisiz:\n"
            "    needs: build\n"
            "    if: failure()\n"
            "  delik:\n"
            "    needs: build\n"
            "    if: always() || github.repository == 'farukatasoy/Tracon'\n"
            "  kopuk:\n"
            "    runs-on: ubuntu-latest\n"
        )

        isler = _ci_isleri(metin)
        sorunlu = sorted({sorun.split("`")[1] for sorun in _korumasiz_isler(isler)})

        self.assertEqual(isler["ozet"]["if"], "github.ref == 'refs/heads/main' && !cancelled()")
        self.assertEqual(sorunlu, ["basarisiz", "delik", "kopuk", "ozet", "rapor"])

    def test_zincirsiz_is_akislarinin_her_isi_kanonik_repo_korumasini_kendisi_tasir(self):
        # Özel depoda ve fork'larda haftalık işler de dakika harcamamalı. `ayna.yml` özel depoda koşar ve
        # public ağaca girmez; `ci.yml` korumayı `build` üzerinden miras alır.
        dosyalar = sorted(
            p for p in (ROOT / ".github" / "workflows").glob("*.yml") if p.name not in ZINCIRSIZ_DOSYA_DISI)

        self.assertIn("sdk-conformance.yml", [p.name for p in dosyalar])
        self.assertIn("openapi-fuzz.yml", [p.name for p in dosyalar])
        for dosya in dosyalar:
            isler = _ci_isleri(dosya.read_text(encoding="utf-8"))
            with self.subTest(dosya=dosya.name):
                self.assertTrue(isler, "iş bulunamadı: ayrıştırıcı dosyayı okuyamadı")
                self.assertEqual(_zincirsiz_korumasiz_isler(isler), [])

    def test_zincirsiz_koruma_kontrolu_eksik_ve_delik_korumayi_yakalar(self):
        gercek = (ROOT / ".github" / "workflows" / "sdk-conformance.yml").read_text(encoding="utf-8")
        korumali = f"    if: {KANONIK_REPO_KOSULU}\n"
        self.assertIn(korumali, gercek)

        korumasiz = _ci_isleri(gercek.replace(korumali, ""))
        delik = _ci_isleri(gercek.replace(korumali, f"    if: always() || {KANONIK_REPO_KOSULU}\n"))

        self.assertEqual(_zincirsiz_korumasiz_isler(_ci_isleri(gercek)), [])
        self.assertEqual(len(_zincirsiz_korumasiz_isler(korumasiz)), 1)
        self.assertEqual(len(_zincirsiz_korumasiz_isler(delik)), 1)

    def test_platform_bacaklari_ad_ortam_degiskeni_ve_imaj_birbirine_bagli(self):
        metin = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        imajlar = _varsayilan_imajlar()

        self.assertTrue(all(imajlar.values()), imajlar)
        self.assertEqual(_matris_bag_sorunlari(metin, imajlar), [])
        # Ayrıştırıcı gerçekten okuyor: yedi PostgreSQL hücresi, iki SQL Server hücresi.
        self.assertEqual(
            [g["name"] for g in _matris_girdileri(_is_blogu(metin, "postgres-matrix"))],
            ["12", "13", "14", "15", "16", "17", "18, ICU tr-TR"])
        self.assertEqual(
            [g["name"] for g in _matris_girdileri(_is_blogu(metin, "sqlserver-matrix"))],
            ["2019", "2022, Turkish_CI_AS"])
        self.assertEqual([g["sqlite"] for g in _matris_girdileri(_is_blogu(metin, "sqlite-floor"))], ["3.44.0"])

    def test_platform_bacagi_bagi_kopunca_kirmizi(self):
        metin = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        imajlar = _varsayilan_imajlar()

        yazim = metin.replace("TRACON_TEST_POSTGRES_IMAGE: ${{ matrix.image }}", "TRACON_TEST_POSTGRES_IMAGE: ${{ matrix.imge }}")
        yanlis_imaj = metin.replace("image: pgvector/pgvector:pg13", "image: pgvector/pgvector:pg14")
        imajsiz = metin.replace("          - name: '13'\n            image: pgvector/pgvector:pg13\n", "          - name: '13'\n            image: ''\n")
        sql_yil = metin.replace("image: mcr.microsoft.com/mssql/server:2019-latest", "image: mcr.microsoft.com/mssql/server:2022-latest")
        bos_surum = metin.replace("sqlite: '3.44.0'", "sqlite: ''")
        for degisen in (yazim, yanlis_imaj, imajsiz, sql_yil, bos_surum):
            self.assertNotEqual(degisen, metin)

        self.assertTrue(any("matrix.imge" in s for s in _matris_bag_sorunlari(yazim, imajlar)))
        self.assertEqual(len(_matris_bag_sorunlari(yanlis_imaj, imajlar)), 1)
        self.assertIn("13", _matris_bag_sorunlari(yanlis_imaj, imajlar)[0])
        self.assertEqual(len(_matris_bag_sorunlari(imajsiz, imajlar)), 1)  # boş imaj = pg18, hücre 13
        self.assertEqual(len(_matris_bag_sorunlari(sql_yil, imajlar)), 1)
        self.assertEqual(len(_matris_bag_sorunlari(bos_surum, imajlar)), 1)

    def test_varsayilan_imaj_bacaklari_pr_de_kosar_ve_imaj_degiskeni_vermez(self):
        # PostgreSQL ve SQL Server tum-proje bacaginin disindadir; matrisler yalniz varsayilan olmayan surum
        # ve yerel ayarlari tasir ve PR'de kosmaz. Varsayilan imaj bu iki modulun PR'de de kosan tek yeridir.
        metin = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        isler = _ci_isleri(metin)

        for is_adi, degisken in (
            ("test-postgres", "TRACON_TEST_POSTGRES_IMAGE"),
            ("test-sqlserver", "TRACON_TEST_SQLSERVER_IMAGE"),
        ):
            self.assertIsNone(isler[is_adi]["if"], f"`{is_adi}` PR dahil her olayda koşmalı")
            self.assertNotIn(degisken, _is_blogu(metin, is_adi), f"`{is_adi}` fikstürün varsayılan imajını kullanır")
            self.assertNotIn("matrix.", _is_blogu(metin, is_adi))
        for is_adi in ("postgres-matrix", "sqlserver-matrix", "sqlite-floor", "culture-matrix"):
            self.assertEqual(isler[is_adi]["if"], "github.event_name != 'pull_request'", is_adi)

    def test_ci_her_test_projesi_kendi_isletim_sisteminde_tam_bir_kez_kosar(self):
        # Solution komutu her projeyi kendiliginden kosardi; modul basina adimlar yeni bir test projesini
        # sessizce disarida birakir. `test-*` isleri tum-proje bacaklaridir: ubuntu'dakiler Tracon.slnx'in,
        # windows'takiler Tracon.no-docker.slnf'in test projelerini TAM BIR KEZ kosar. Matrisler (veritabani
        # surumleri, kultur) ayri bacaklardir ve burada sayilmaz.
        metin = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        isler = _ci_isleri(metin)
        # WorkerHarness test degil, ikinci surecin konusudur.
        slnx = (ROOT / "Tracon.slnx").read_text(encoding="utf-8")
        ubuntu_beklenen = set(re.findall(r'Path="tests/([\w.]+)/[\w.]+\.csproj"', slnx)) - {"Tracon.WorkerHarness"}
        filtre = json.loads((ROOT / "Tracon.no-docker.slnf").read_text(encoding="utf-8"))
        windows_beklenen = {
            eslesme.group(1) for yol in filtre["solution"]["projects"]
            if (eslesme := re.match(r"tests/([\w.]+)/", yol))} - {"Tracon.WorkerHarness"}

        kosan: dict[str, dict[str, int]] = {"ubuntu-latest": {}, "windows-latest": {}}
        for ad in isler:
            if not ad.startswith("test-"):
                continue
            blok = _is_blogu(metin, ad)
            isletim = re.search(r"(?m)^    runs-on:\s*(\S+)", blok).group(1)
            for modul in re.findall(r"(?m)^\s+run: dotnet test --project tests/([\w.]+)/", blok):
                kosan[isletim][modul] = kosan[isletim].get(modul, 0) + 1

        self.assertGreaterEqual(len(ubuntu_beklenen), 25)
        self.assertEqual(sorted(kosan["ubuntu-latest"].items()), [(m, 1) for m in sorted(ubuntu_beklenen)])
        self.assertEqual(sorted(kosan["windows-latest"].items()), [(m, 1) for m in sorted(windows_beklenen)])

    def test_ci_is_grafi_tutarli_ve_iptal_kurallari_yerinde(self):
        metin = (ROOT / ".github" / "workflows" / "ci.yml").read_text(encoding="utf-8")
        isler = _ci_isleri(metin)

        for ad, is_ in isler.items():
            for bagimlilik in is_["needs"]:
                self.assertIn(bagimlilik, isler, f"`{ad}` var olmayan `{bagimlilik}` işini bekliyor")
        # Dongu yok: bagimliliksiz isten baslayarak her is siralanabilir.
        siralanan: set[str] = set()
        while len(siralanan) < len(isler):
            hazir = [ad for ad, is_ in isler.items() if ad not in siralanan and set(is_["needs"]) <= siralanan]
            self.assertTrue(hazir, f"bağımlılık döngüsü: {sorted(set(isler) - siralanan)}")
            siralanan.update(hazir)

        # Iptal: PR ve main'de eski kosum iptal edilir; etiket kosumu ASLA; elle tetiklemenin grubu `run_id`
        # icerir (olcum icin ardisik tetiklenen kosumlar birbirini iptal etmez).
        blok = re.search(r"(?m)^concurrency:\n((?:  .*\n)+)", metin)
        self.assertIsNotNone(blok, "ci.yml'de iş akışı düzeyinde `concurrency:` yok")
        grup = re.search(r"(?m)^  group: (.*)$", blok.group(1)).group(1)
        iptal = re.search(r"(?m)^  cancel-in-progress: (.*)$", blok.group(1)).group(1)
        self.assertIn("github.event_name == 'workflow_dispatch'", grup)
        self.assertIn("github.run_id", grup)
        self.assertIn("github.ref", grup)
        self.assertIn("!startsWith(github.ref, 'refs/tags/')", iptal)
        self.assertIn("github.event_name != 'workflow_dispatch'", iptal)
        # Kapilar belge degisikligi diye atlanmaz.
        self.assertNotIn("paths-ignore", metin)
        self.assertIn("PYTHONUTF8: 1", metin)

    def test_uret_alt_komutu_yalniz_ureticileri_kosar(self):
        with contextlib.redirect_stdout(io.StringIO()) as output:
            result = kapi.main(["--komutlari-bas", "uret"])

        self.assertEqual(result, 0)
        self.assertEqual(
            [line[2:] for line in output.getvalue().splitlines() if line.startswith("$ ")],
            [c.display for c in kapi.generation_commands()])

    def test_kapanis_performans_adimini_kosullu_ekler(self):
        with_it = [c.display for c in kapi.closing_commands("abc123", site=False, performance=True)]
        without_it = [c.display for c in kapi.closing_commands("abc123", site=False, performance=False)]

        self.assertIn("python3 scripts/kapi.py performans", with_it)
        self.assertNotIn("python3 scripts/kapi.py performans", without_it)

    def test_sicak_yol_degisikligi_performans_kapisini_tetikler(self):
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Core/Recording/RunEventWriter.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Core/Compilation/CompiledAgentCache.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Core/Diagnostics/ChatTelemetryLanes.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Sql.Shared/Stores/SqlRunStore.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Sqlite/Internal/SqliteDataSource.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["src/Tracon.Sqlite/Internal/SqliteDecimalFunctions.cs"]))
        self.assertTrue(kapi.performance_gate_triggered(["bench/Tracon.Benchmarks/Program.cs"]))
        self.assertFalse(kapi.performance_gate_triggered(["src/Tracon.Core/TraconOptions.cs"]))
        self.assertFalse(kapi.performance_gate_triggered([]))

    def test_tam_test_kosumu_kaynak_cekismesini_sinirlar(self):
        # Aynı anda en çok TEST_MAX_CPU_COUNT test modülü: hafif aşamalar o kadar modülü bir komutta koşar,
        # ağır modüller o kadar hatta ve hat başına tek modül koşar.
        asamalar = kapi.staged_test_commands()
        hafif = [komut for komut in asamalar if komut.group is None]
        agir = [komut for komut in asamalar if komut.group is not None]

        for komut in hafif:
            self.assertEqual(
                komut.args[komut.args.index("--max-parallel-test-modules") + 1], str(kapi.TEST_MAX_CPU_COUNT))
        for komut in agir:
            self.assertEqual(komut.args[komut.args.index("--max-parallel-test-modules") + 1], "1")
        self.assertEqual(len({komut.lane for komut in agir}), kapi.TEST_MAX_CPU_COUNT)

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
        """Denetim: `_git()` `subprocess.run`'ın FileNotFoundError'ını
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

    def test_okuma_gorunumu_dosyasi_da_git_tabanıyla_ayni_olmalidir(self):
        # `MigrationsViews/*.sql` sevk edilir ve açılışta sağlama toplamıyla
        # denetlenir; yerinde bir düzenleme CI'da değil, kullanıcının veritabanında
        # yakalanıyordu. Kapı yalnız `Migrations` ve `MigrationsKnowledge` dizinlerini
        # okuyordu.
        for directory_name in ("Migrations", "MigrationsKnowledge", "MigrationsViews"):
            with self.subTest(directory=directory_name), tempfile.TemporaryDirectory() as directory:
                root = pathlib.Path(directory)
                file = root / "src" / "Example" / directory_name / "0001_read_views.sql"
                file.parent.mkdir(parents=True)
                original = b"CREATE VIEW example_v1 AS SELECT 1;\n"
                file.write_bytes(original)
                relative = file.relative_to(root).as_posix()
                source = lambda commit, path: original if (commit, path) == ("abcdef1", relative) else None

                self.assertEqual(
                    kapi.migration_integrity_violations(
                        root, baseline_commit="abcdef1", source_commits={}, source_reader=source),
                    [])

                file.write_text("CREATE VIEW example_v1 AS SELECT 2;\n", encoding="utf-8")

                violations = kapi.migration_integrity_violations(
                    root, baseline_commit="abcdef1", source_commits={}, source_reader=source)

                self.assertEqual(len(violations), 1)
                self.assertIn(relative, violations[0])


def _slnx_test_modulleri(kok: pathlib.Path = ROOT) -> list[str]:
    """Tracon.slnx'in tuttuğu test modullerinin proje adları.

    Kural: `tests/` altındaki her proje bir test modülüdür, `tests/Directory.Build.props`'un xunit/MTP
    bağını `IsTestHarnessProject=true` ile reddedenler hariç (`Tracon.WorkerHarness`: iki süreçli
    kanıtın öznesi; test ana makinesi değildir ve `dotnet test` onu koşamaz)."""
    moduller = []
    for proje in ElementTree.parse(kok / "Tracon.slnx").getroot().iter("Project"):
        yol = pathlib.PurePosixPath(proje.get("Path", ""))
        if yol.parts[:1] != ("tests",):
            continue
        csproj = (kok / yol).read_text(encoding="utf-8")
        if re.search(r"<IsTestHarnessProject>\s*true\s*</IsTestHarnessProject>", csproj):
            continue
        moduller.append(yol.stem)
    return sorted(moduller)


def _slnf_projeleri(yol: pathlib.Path) -> list[str]:
    return list(json.loads(yol.read_text(encoding="utf-8"))["solution"]["projects"])


def _bolum_sorunlari(
    moduller: list[str], core: str, hafif: list[str], agir: dict[str, tuple[str, ...]],
) -> list[str]:
    """Her test modülü Core, hafif süzgeç ve ağır hatlar arasında TAM BİR yerde mi? Sorun listesi; boşsa tamam."""
    yerler: dict[str, list[str]] = {core: ["core"]}
    for ad in hafif:
        yerler.setdefault(ad, []).append("hafif süzgeç")
    for hat, adlar in agir.items():
        for ad in adlar:
            yerler.setdefault(ad, []).append(f"ağır hat {hat}")
    sorunlar = [f"{ad}: hiçbir aşamada yok" for ad in moduller if ad not in yerler]
    sorunlar += [f"{ad}: birden çok yerde ({', '.join(y)})" for ad, y in sorted(yerler.items()) if len(y) > 1]
    sorunlar += [f"{ad}: Tracon.slnx'te test modülü değil" for ad in sorted(yerler) if ad not in moduller]
    return sorunlar


class GrupKosucuTestleri(unittest.TestCase):
    """`Command.group` / `Command.lane`: bağımsız adımlar aynı anda koşar; çıktı, ölçüm ve karar liste sırasında kalır."""

    @staticmethod
    def _kos(komutlar, davranis, *, olcum_yamasi=None):
        """`davranis(ad)` -> (çıkış kodu, çıktı). Dönüş: (sonuç, ekran, başlama sırası, ölçüm satırları)."""
        basladi: list[str] = []
        kilit = threading.Lock()

        def runner(args, **kwargs):
            with kilit:
                basladi.append(args[0])
            kod, cikti = davranis(args[0])
            return mock.Mock(returncode=kod, stdout=cikti)

        with tempfile.TemporaryDirectory() as dizin, contextlib.redirect_stdout(io.StringIO()) as ekran:
            yol = pathlib.Path(dizin) / "m.jsonl"
            if olcum_yamasi is None:
                sonuc = kapi.run_commands("test", komutlar, runner=runner, measurement_path=yol)
            else:
                with mock.patch.object(kapi, "_record_measurement", side_effect=olcum_yamasi):
                    sonuc = kapi.run_commands("test", komutlar, runner=runner, measurement_path=yol)
            satirlar = [json.loads(s) for s in yol.read_text(encoding="utf-8").splitlines()] if yol.exists() else []
        return sonuc, ekran.getvalue(), basladi, satirlar

    @staticmethod
    def _grup(*adlar, grup="g"):
        return [kapi.Command((ad,), group=grup) for ad in adlar]

    def test_cikti_liste_sirasiyla_basilir_bitis_sirasiyla_degil(self):
        bitti = {ad: threading.Event() for ad in ("c1", "c2")}
        bitis_sirasi: list[str] = []

        def davranis(ad):
            if ad == "c0":
                for olay in bitti.values():
                    olay.wait(5)
            else:
                bitti[ad].set()
            bitis_sirasi.append(ad)
            return 0, f"CIKTI-{ad}\n"

        sonuc, ekran, _, _ = self._kos(self._grup("c0", "c1", "c2"), davranis)

        self.assertEqual(sonuc, 0)
        self.assertEqual(bitis_sirasi[-1], "c0", "c0 öbürlerini beklemedi: komutlar aynı anda koşmuyor")
        self.assertLess(ekran.index("CIKTI-c0"), ekran.index("CIKTI-c1"))
        self.assertLess(ekran.index("CIKTI-c1"), ekran.index("CIKTI-c2"))

    def test_gruptaki_komutlar_gercekten_ayri_thread_lerde_kosar(self):
        thread_ler: set[int] = set()

        def davranis(ad):
            thread_ler.add(threading.get_ident())
            return 0, ""

        self._kos(self._grup("c0", "c1", "c2"), davranis)

        self.assertNotIn(threading.get_ident(), thread_ler)

    def test_liste_sirasindaki_ilk_kirmizi_karar_verir_once_biten_degil(self):
        c1_bitti = threading.Event()

        def davranis(ad):
            if ad == "c0":
                c1_bitti.wait(5)
                return 3, ""
            if ad == "c1":
                c1_bitti.set()
                return 5, ""
            return 0, ""

        sonuc, ekran, _, satirlar = self._kos(self._grup("c0", "c1", "c2"), davranis)

        self.assertEqual(sonuc, 3)
        self.assertEqual([(s["command"], s["exit_code"]) for s in satirlar], [("c0", 3), ("c1", 5), ("c2", 0)])
        self.assertIn("Çıkış 5", ekran)  # kardeşin kırmızısı da görünür, kararı değiştirmez

    def test_kirmizi_adim_kardeslerini_yetim_birakmaz(self):
        serbest = threading.Event()
        bitenler: list[str] = []
        threading.Timer(0.3, serbest.set).start()

        def davranis(ad):
            if ad == "c0":
                return 1, ""
            serbest.wait(10)
            bitenler.append(ad)
            return 0, ""

        sonuc, _, basladi, satirlar = self._kos(self._grup("c0", "c1", "c2"), davranis)

        self.assertEqual(sonuc, 1)
        self.assertCountEqual(basladi, ["c0", "c1", "c2"])
        self.assertCountEqual(bitenler, ["c1", "c2"], "kırmızı adımdan sonra dönüldü ama kardeşler bitmemişti")
        self.assertEqual(len(satirlar), 3)

    def test_olcum_satirlari_ana_thread_ten_ve_liste_sirasiyla_yazilir(self):
        yazanlar: list[tuple[str, bool]] = []

        def kaydet(stage, command, started, exit_code, path, *, finished=None):
            yazanlar.append((command.args[0], threading.current_thread() is threading.main_thread()))

        def davranis(ad):
            time.sleep({"c0": 0.2, "c1": 0.0, "c2": 0.1}[ad])
            return 0, ""

        self._kos(self._grup("c0", "c1", "c2"), davranis, olcum_yamasi=kaydet)

        self.assertEqual(yazanlar, [("c0", True), ("c1", True), ("c2", True)])

    def test_olcum_satiri_komutun_kendi_suresini_tasir_siranin_bekleyisini_degil(self):
        def davranis(ad):
            time.sleep(0.4 if ad == "c0" else 0.0)
            return 0, ""

        _, _, _, satirlar = self._kos(self._grup("c0", "c1"), davranis)

        sureler = {s["command"]: s["duration_seconds"] for s in satirlar}
        self.assertGreaterEqual(sureler["c0"], 0.3)
        self.assertLess(sureler["c1"], 0.2)  # c1 c0'ın ardından basıldı ama kendi süresini yazar

    def test_hat_icindeki_komutlar_sirayla_hatlar_ayni_anda_kosar(self):
        kilit = threading.Lock()
        gunluk: list[str] = []
        b1_basladi = threading.Event()

        def davranis(ad):
            with kilit:
                gunluk.append(f"basla {ad}")
            if ad == "a1":
                b1_basladi.wait(5)  # a1 ancak b1 başlayınca biter: hatlar aynı anda koşuyor
            if ad == "b1":
                b1_basladi.set()
            with kilit:
                gunluk.append(f"bitti {ad}")
            return 0, ""

        komutlar = [
            kapi.Command(("a1",), group="g", lane="a"), kapi.Command(("a2",), group="g", lane="a"),
            kapi.Command(("b1",), group="g", lane="b"), kapi.Command(("b2",), group="g", lane="b"),
        ]
        sonuc, _, _, _ = self._kos(komutlar, davranis)

        self.assertEqual(sonuc, 0)
        self.assertLess(gunluk.index("basla b1"), gunluk.index("bitti a1"))
        self.assertLess(gunluk.index("bitti a1"), gunluk.index("basla a2"))
        self.assertLess(gunluk.index("bitti b1"), gunluk.index("basla b2"))

    def test_kirmizi_hat_sonraki_komutlari_baslatmaz_ama_her_hattin_ilki_baslar(self):
        a1_kaydedildi = threading.Event()

        def kaydet(stage, command, started, exit_code, path, *, finished=None):
            if command.args[0] == "a1":
                a1_kaydedildi.set()

        def davranis(ad):
            if ad == "a1":
                return 4, ""
            if ad == "b1":
                a1_kaydedildi.wait(5)  # a1 kırmızı sayıldıktan sonra biter
            return 0, ""

        komutlar = [
            kapi.Command(("a1",), group="g", lane="a"), kapi.Command(("b1",), group="g", lane="b"),
            kapi.Command(("b2",), group="g", lane="b"), kapi.Command(("a2",), group="g", lane="a"),
        ]
        sonuc, ekran, basladi, satirlar = self._kos(komutlar, davranis, olcum_yamasi=kaydet)

        self.assertEqual(sonuc, 4)
        self.assertCountEqual(basladi, ["a1", "b1"])
        self.assertEqual(ekran.count("atlandı"), 2)
        self.assertEqual(satirlar, [])  # yama satır yazmaz; ölçümün kendisi ayrı sınanır

    def test_uzun_suren_grup_sessiz_kalmaz_hangi_komutlar_surdugunu_yazar(self):
        def davranis(ad):
            time.sleep(0.4 if ad == "yavas" else 0.0)
            return 0, ""

        with mock.patch.object(kapi, "GROUP_HEARTBEAT_SECONDS", 0.05):
            sonuc, ekran, _, _ = self._kos(self._grup("yavas", "hizli"), davranis)

        self.assertEqual(sonuc, 0)
        self.assertRegex(ekran, r"… sürüyor \(\d+ s\): yavas")

    def test_komut_bulunamazsa_grupta_127_olur_ve_kimse_beklemede_kalmaz(self):
        def runner(args, **kwargs):
            if args[0] == "c1":
                raise FileNotFoundError(2, "No such file or directory", "c1")
            return mock.Mock(returncode=0, stdout="")

        with tempfile.TemporaryDirectory() as dizin, contextlib.redirect_stdout(io.StringIO()):
            sonuc = kapi.run_commands(
                "test", self._grup("c0", "c1"), runner=runner, measurement_path=pathlib.Path(dizin) / "m.jsonl")

        self.assertEqual(sonuc, 127)

    def test_kosucu_cokerse_grup_asili_kalmaz(self):
        def runner(args, **kwargs):
            if args[0] == "c1":
                raise ValueError("beklenmeyen")
            return mock.Mock(returncode=0, stdout="")

        with tempfile.TemporaryDirectory() as dizin, contextlib.redirect_stdout(io.StringIO()) as ekran:
            sonuc = kapi.run_commands(
                "test", self._grup("c0", "c1"), runner=runner, measurement_path=pathlib.Path(dizin) / "m.jsonl")

        self.assertEqual(sonuc, kapi.INTERNAL_ERROR_EXIT_CODE)
        self.assertIn("beklenmeyen", ekran.getvalue())

    def test_grup_komutun_gorunumunu_degistirmez(self):
        self.assertEqual(kapi.Command(("x", "y"), group="g", lane="a").display, kapi.Command(("x", "y")).display)

    def test_ayni_grup_adi_ancak_bitisikse_tek_blok_olur(self):
        komutlar = [
            *self._grup("g1", "g2"), kapi.Command(("duz",)), *self._grup("g3", "g4"),
            kapi.Command(("tek",), group="baska"),
        ]

        self.assertEqual(
            [[komut.args[0] for komut in blok] for blok in kapi._blocks(komutlar)],
            [["g1", "g2"], ["duz"], ["g3", "g4"], ["tek"]])

    def test_gruplanmamis_komutlar_eskisi_gibi_sirayla_ve_ilk_kirmizida_durur(self):
        sonuc, _, basladi, _ = self._kos(
            [kapi.Command((ad,)) for ad in ("s0", "s1", "s2")], lambda ad: (7 if ad == "s1" else 0, ""))

        self.assertEqual(sonuc, 7)
        self.assertEqual(basladi, ["s0", "s1"])

    def test_kuru_listede_grup_ve_hat_isaretleri_yorum_satiridir(self):
        komutlar = [
            kapi.Command(("once",)),
            kapi.Command(("a1",), group="g", lane="a"), kapi.Command(("b1",), group="g", lane="b"),
            kapi.Command(("a2",), group="g", lane="a"),
            kapi.Command(("sonra",)),
        ]
        runner = mock.Mock()
        with contextlib.redirect_stdout(io.StringIO()) as ekran:
            sonuc = kapi.run_commands("test", komutlar, runner=runner, dry_run=True)

        satirlar = ekran.getvalue().splitlines()
        self.assertEqual(sonuc, 0)
        runner.assert_not_called()
        self.assertEqual(
            [satir[2:] for satir in satirlar if satir.startswith("$ ")],
            [komut.display for komut in komutlar])
        self.assertTrue(any(satir.startswith("# paralel grup") for satir in satirlar))
        self.assertTrue(any(satir.startswith("# grup sonu") for satir in satirlar))
        # Hat işareti, listede hat değiştiği yerde yazılır (a1, b1, a2 -> a, b, a).
        self.assertEqual(
            [satir for satir in satirlar if satir.startswith("# hat ")], ["# hat a", "# hat b", "# hat a"])


class AsamaliTestKosumuTestleri(unittest.TestCase):
    """Kapanışın test koşumu: Core tek başına, hafif süzgeç, iki ağır hat; tek TRX denetimi; ilk kırmızıda durma."""

    def _kos(self, komutlar, kod_veren=lambda args: 0, *, sifir_test=0, sinir_kodu=0):
        cagrilar: list[tuple[tuple[str, ...], float]] = []
        kilit = threading.Lock()

        def runner(args, **kwargs):
            with kilit:
                cagrilar.append((tuple(args), time.time()))
            return mock.Mock(returncode=kod_veren(tuple(args)), stdout="")

        with mock.patch.object(kapi, "require_executed_tests", return_value=sifir_test) as zorunlu, \
                mock.patch.object(kapi, "enforce_module_time_limits", return_value=sinir_kodu) as sinir, \
                mock.patch.object(kapi, "record_test_durations") as sure, \
                mock.patch.object(kapi, "isolate_failed_tests", return_value=[]) as izole, \
                mock.patch.object(kapi, "failed_tests_from_trx", return_value=[]) as dusenler, \
                tempfile.TemporaryDirectory() as dizin, contextlib.redirect_stdout(io.StringIO()):
            yol = pathlib.Path(dizin) / "m.jsonl"
            sonuc = kapi.run_commands("test", komutlar, runner=runner, measurement_path=yol)
            satirlar = [json.loads(s) for s in yol.read_text(encoding="utf-8").splitlines()] if yol.exists() else []
        return types.SimpleNamespace(
            sonuc=sonuc, cagrilar=cagrilar, satirlar=satirlar, zorunlu=zorunlu, sinir=sinir, sure=sure,
            izole=izole, dusenler=dusenler)

    @staticmethod
    def _ad_iceren(*parcalar):
        return lambda args: 2 if any(parca in " ".join(args) for parca in parcalar) else 0

    # --- sıra ve yapı -------------------------------------------------------------

    def test_core_tek_basina_ilk_komuttur_sonra_hafif_sonra_agir(self):
        komutlar = kapi.staged_test_commands()

        self.assertEqual(
            komutlar[0].args[:4],
            ("dotnet", "test", "--project", "tests/Tracon.Core.UnitTests/Tracon.Core.UnitTests.csproj"))
        self.assertEqual(komutlar[1].args[:4], ("dotnet", "test", "--solution", "Tracon.fast.slnf"))
        self.assertIsNone(komutlar[0].group)
        self.assertIsNone(komutlar[1].group)
        self.assertTrue(all(komut.group == kapi.HEAVY_TEST_GROUP for komut in komutlar[2:]))
        self.assertEqual(len(komutlar), 2 + sum(len(adlar) for adlar in kapi.HEAVY_TEST_LANES.values()))

    def test_docker_modulleri_en_sonda_agir_hatlarda_kosar(self):
        komutlar = kapi.staged_test_commands()
        for modul in ("TwoNode", "SqlServer", "PostgreSql"):
            with self.subTest(modul=modul):
                yer = [i for i, komut in enumerate(komutlar) if f"Tracon.{modul}.IntegrationTests" in komut.display]
                self.assertEqual(len(yer), 1)
                self.assertGreaterEqual(yer[0], 2)
        self.assertNotIn("Tracon.fast.slnf", " ".join(" ".join(k.args) for k in komutlar[2:]))

    def test_sql_server_ve_two_node_ayni_hattadir_boylece_asla_ayni_anda_kosmaz(self):
        hat_of = {
            ad: hat for hat, adlar in kapi.HEAVY_TEST_LANES.items() for ad in adlar}

        self.assertEqual(hat_of["Tracon.SqlServer.IntegrationTests"], hat_of["Tracon.TwoNode.IntegrationTests"])
        komutlar = {k.display: k for k in kapi.staged_test_commands() if k.group}
        for komut in komutlar.values():
            proje = pathlib.PurePath(kapi._test_target(komut.args)).stem
            self.assertEqual(komut.lane, hat_of[proje], proje)

    def test_hat_sayisi_ayni_anda_kosan_modul_sayisidir(self):
        self.assertEqual(len(kapi.HEAVY_TEST_LANES), kapi.TEST_MAX_CPU_COUNT)

    def test_her_asama_trx_bayraklarini_tasir(self):
        for komut in kapi.staged_test_commands():
            with self.subTest(komut=komut.display):
                args = komut.args
                self.assertTrue(kapi.reports_trx(komut))
                self.assertNotIn("--", args)
                self.assertEqual(args[args.index("-c") + 1], "Release")
                self.assertIn("--no-build", args)
                self.assertEqual(args[args.index("--results-directory") + 1], kapi.TEST_RESULTS_DIRECTORY)
                self.assertEqual(args[args.index("--results-directory-layout") + 1], "per-module")

    def test_ic_dongu_tam_kosum_komutu_tek_cozum_komutu_olarak_kalir(self):
        with contextlib.redirect_stdout(io.StringIO()):
            komutlar = kapi.inner_loop_commands(["src/Unknown.Package/Thing.cs"])[1:]

        self.assertEqual([k.display for k in komutlar], [kapi.full_solution_test_command().display])

    def test_kapanis_testleri_listenin_sonunda_bitisiktir_ve_oncesinde_testsiz_adimlar_vardir(self):
        komutlar = kapi.closing_commands("abc123", site=True, performance=True)
        testler = kapi.staged_test_commands()

        self.assertEqual(komutlar[-len(testler):], testler)
        self.assertFalse(any(k.display.startswith("dotnet test") for k in komutlar[:-len(testler)]))

    # --- bölüm koruması -------------------------------------------------------------

    def test_slnf_ve_agir_liste_ve_core_her_test_modulunu_tam_bir_kez_kapsar(self):
        hafif = _slnf_projeleri(ROOT / kapi.FAST_TEST_SOLUTION_FILTER)

        self.assertTrue(all(yol.startswith("tests/") for yol in hafif), "hafif süzgeç yalnız test projesi tutar")
        for yol in hafif:
            self.assertTrue((ROOT / yol).is_file(), yol)
        self.assertEqual(
            json.loads((ROOT / kapi.FAST_TEST_SOLUTION_FILTER).read_text(encoding="utf-8"))["solution"]["path"],
            "Tracon.slnx")
        self.assertEqual(
            _bolum_sorunlari(
                _slnx_test_modulleri(), kapi.CORE_TEST_PROJECT,
                [pathlib.PurePosixPath(yol).stem for yol in hafif], kapi.HEAVY_TEST_LANES),
            [])

    def test_bolum_korumasi_hicbir_yerde_listelenmeyen_yeni_modulde_kirmizi_olur(self):
        moduller = ["Tracon.A.Tests", "Tracon.B.Tests", "Tracon.Yeni.Tests"]

        sorunlar = _bolum_sorunlari(moduller, "Tracon.A.Tests", ["Tracon.B.Tests"], {"a": ()})

        self.assertEqual(sorunlar, ["Tracon.Yeni.Tests: hiçbir aşamada yok"])

    def test_bolum_korumasi_iki_yerde_listelenen_ve_bilinmeyen_modulu_yakalar(self):
        moduller = ["Tracon.A.Tests", "Tracon.B.Tests"]

        sorunlar = _bolum_sorunlari(
            moduller, "Tracon.A.Tests", ["Tracon.B.Tests", "Tracon.Yazim.Tests"], {"a": ("Tracon.B.Tests",)})

        self.assertEqual(
            sorunlar,
            ["Tracon.B.Tests: birden çok yerde (hafif süzgeç, ağır hat a)",
             "Tracon.Yazim.Tests: Tracon.slnx'te test modülü değil"])

    def test_yardimci_proje_test_modulu_sayilmaz(self):
        moduller = _slnx_test_modulleri()

        self.assertNotIn("Tracon.WorkerHarness", moduller)
        self.assertIn(kapi.CORE_TEST_PROJECT, moduller)

    # --- koşum: tek TRX denetimi, ilk kırmızıda durma ------------------------------------

    def test_yesil_kosumda_trx_kancalari_bir_kez_ve_ilk_komuttan_once_baslayan_zamanla_calisir(self):
        sonuc = self._kos(kapi.staged_test_commands())

        self.assertEqual(sonuc.sonuc, 0)
        self.assertEqual(len(sonuc.cagrilar), len(kapi.staged_test_commands()))
        for kanca in (sonuc.zorunlu, sonuc.sinir, sonuc.sure):
            kanca.assert_called_once()
        since = sonuc.sure.call_args.args[0]
        self.assertEqual(sonuc.zorunlu.call_args.args[0], since)
        self.assertEqual(sonuc.sinir.call_args.args[0], since)
        self.assertLessEqual(since, min(zaman for _, zaman in sonuc.cagrilar))
        sonuc.izole.assert_not_called()
        self.assertEqual([s["exit_code"] for s in sonuc.satirlar], [0] * len(kapi.staged_test_commands()))

    def test_kirmizi_core_sonraki_asamalari_baslatmaz_ama_teshis_kancalari_calisir(self):
        sonuc = self._kos(kapi.staged_test_commands(), self._ad_iceren("Tracon.Core.UnitTests"))

        self.assertEqual(sonuc.sonuc, 2)
        self.assertEqual(len(sonuc.cagrilar), 1)
        self.assertIn("Tracon.Core.UnitTests", " ".join(sonuc.cagrilar[0][0]))
        sonuc.zorunlu.assert_not_called()
        sonuc.sinir.assert_not_called()
        sonuc.sure.assert_called_once()
        sonuc.izole.assert_called_once()
        # Ölçüm: yalnız Core satırı, kendi çıkış koduyla; ağır aşama için satır yok.
        self.assertEqual(len(sonuc.satirlar), 1)
        self.assertEqual(sonuc.satirlar[0]["exit_code"], 2)
        self.assertIn("Tracon.Core.UnitTests", sonuc.satirlar[0]["command"])

    def test_kirmizi_hafif_asama_agir_hatlari_baslatmaz(self):
        sonuc = self._kos(kapi.staged_test_commands(), self._ad_iceren("Tracon.fast.slnf"))

        self.assertEqual(sonuc.sonuc, 2)
        self.assertEqual(len(sonuc.cagrilar), 2)
        sonuc.sure.assert_called_once()
        sonuc.izole.assert_called_once()

    def test_kirmizi_agir_modul_hatti_durdurur_cikis_kodunu_korur_ve_sure_sinirini_ezmez(self):
        sonuc = self._kos(
            kapi.staged_test_commands(), self._ad_iceren("Tracon.SqlServer.IntegrationTests"), sinir_kodu=90)

        calisan = [" ".join(args) for args, _ in sonuc.cagrilar]
        self.assertEqual(sonuc.sonuc, 2)
        self.assertFalse(any("Tracon.Ui.E2ETests" in satir for satir in calisan), "kırmızı hattın sonraki komutu başladı")
        sonuc.sinir.assert_not_called()
        sonuc.sure.assert_called_once()
        sonuc.izole.assert_called_once()

    def test_asamalar_yesilken_modul_sure_siniri_cikis_kodu_verir_ve_tek_satir_yazar(self):
        sonuc = self._kos(kapi.staged_test_commands(), sinir_kodu=kapi.MODULE_TIME_LIMIT_EXIT_CODE)

        self.assertEqual(sonuc.sonuc, kapi.MODULE_TIME_LIMIT_EXIT_CODE)
        sonuc.sinir.assert_called_once()
        sonuc.sure.assert_called_once()
        kontrol = [s for s in sonuc.satirlar if s["command"] == kapi.TRX_CHECK.display]
        self.assertEqual([s["exit_code"] for s in kontrol], [kapi.MODULE_TIME_LIMIT_EXIT_CODE])
        self.assertEqual(
            [s["exit_code"] for s in sonuc.satirlar if s["command"] != kapi.TRX_CHECK.display],
            [0] * len(kapi.staged_test_commands()))

    def test_sifir_test_kosan_yesil_asamalar_kirmizi_sayilir(self):
        sonuc = self._kos(kapi.staged_test_commands(), sifir_test=kapi.ZERO_TESTS_EXIT_CODE)

        self.assertEqual(sonuc.sonuc, kapi.ZERO_TESTS_EXIT_CODE)
        sonuc.zorunlu.assert_called_once()
        sonuc.sinir.assert_not_called()

    def test_testsiz_adim_iki_test_kosumunu_ayirir_ve_her_kosum_kendi_kancalarini_calistirir(self):
        trx = kapi.full_solution_test_command()
        sonuc = self._kos([trx, kapi.Command(("dotnet", "build", "Tracon.slnx")), trx])

        self.assertEqual(sonuc.sonuc, 0)
        self.assertEqual(sonuc.zorunlu.call_count, 2)
        self.assertEqual(sonuc.sure.call_count, 2)

    def test_trx_istemeyen_grup_test_kancalarini_calistirmaz(self):
        sonuc = self._kos([kapi.Command((ad,), group="g") for ad in ("c0", "c1")])

        self.assertEqual(sonuc.sonuc, 0)
        sonuc.zorunlu.assert_not_called()
        sonuc.sure.assert_not_called()


class PerformansKapisiTestleri(unittest.TestCase):
    """`scripts/kapi.py performans` - karşılaştırma ve taban çizgisi
    okuma mantığının SAF Python testleri. `dotnet run` gerektiren gerçek
    benchmark koşumu ve kasıtlı gerileme koşumu manuel kabul case'lerindedir."""

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

    def test_olcum_uretmeyen_benchmark_anlasilir_kirmizi_olur(self):
        # 2026-10-11: BenchmarkDotNet aynı ada sahip ikinci bir .csproj bulunca
        # (.claude/worktrees altındaki agent worktree'leri) boilerplate'i
        # üretemedi; rapor `BytesAllocatedPerOperation: null` taşıdı ve kapı
        # anlamlı bir mesaj yerine TypeError ile çöktü.
        report = {"Benchmarks": [{"FullName": "A", "Statistics": None,
                                  "Memory": {"BytesAllocatedPerOperation": None}}]}
        current = kapi.parse_benchmark_report(report)

        exit_code, messages = kapi.compare_allocations({"A": {"allocatedBytes": 24, "meanNanoseconds": 1.0}}, current)

        self.assertEqual(exit_code, 1)
        self.assertTrue(any("A" in message and "ölçüm üretmedi" in message for message in messages))

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
        """Denetim bulgusu: orkestrasyon fonksiyonunun kendisi hiç
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
    """`scripts/kapi.py yayin` - saf Python mantığı. `dotnet
    pack`'e ihtiyaç duyan uçtan uca davranış `ReleaseArtifactTests.cs`'te ve bu
    fazın manuel kabul case'lerinde."""

    def _write_csproj(self, root: pathlib.Path, project_id: str, body: str = "") -> None:
        directory = root / "src" / project_id
        directory.mkdir(parents=True)
        (directory / f"{project_id}.csproj").write_text(
            f'<Project Sdk="Microsoft.NET.Sdk">\n{body}\n</Project>\n', encoding="utf-8")
        _write_src_build_props(root)

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
        """Denetim: eski bir 'en son yazılan' seçimi, ÖNCEKİ bir
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
            _write_src_build_props(root, "net10.0;net11.0")

            self.assertEqual(kapi._target_frameworks(root, "Tracon.Testing"), ("net10.0",))
            # Varsayilan props'tan OKUNUR, betikte tekrar yazilmaz.
            self.assertEqual(kapi._target_frameworks(root, "Tracon.Core"), ("net10.0", "net11.0"))

            (root / "src" / "Directory.Build.props").write_text("<Project />\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                kapi._target_frameworks(root, "Tracon.Core")

    @staticmethod
    def _write_fake_nupkg(path: pathlib.Path, *, content: bytes, psmdcp_guid: str = "0" * 32) -> None:
        """A minimal but REAL zip, shaped enough to exercise `_content_fingerprint`:
        one meaningful entry plus the two OPC entries NuGet regenerates with a
        random name on every real `dotnet pack` (see `_VOLATILE_OPC_ENTRY`)."""
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("lib/net10.0/Thing.dll", content)
            archive.writestr("_rels/.rels", f"<Relationships><Relationship Target=\"/package/services/metadata/core-properties/{psmdcp_guid}.psmdcp\" /></Relationships>")
            archive.writestr(f"package/services/metadata/core-properties/{psmdcp_guid}.psmdcp", "<coreProperties />")

    # "bayat paket temizlenir" iddiası (eski `_clean_stale_packages`,
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
        hash'i karşılaştırılsaydı bu, bir manuel case'in kanıtlamak istediği
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
                commit="691dfbe17061bd6932576e892f4eeffe044468df",
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

    def test_yayin_tarama_kirmiziysa_pack_denenmez(self):
        """Etiket koşusu `tarama`yı koşar ve yayınlanmış bir etiket geri alınamaz.
        Taramanın düşüreceği her şey (sürüm etiketi kayması dahil) etiketten ÖNCE,
        provada görünmeli; pack'e hiç girilmez."""
        with mock.patch.object(kapi, "_git", return_value=[]), \
                mock.patch.object(kapi, "scan", return_value=1) as scan, \
                mock.patch.object(kapi, "_pack_release", return_value=1) as pack:
            result = kapi.release_rehearsal(None)

        self.assertEqual(result, 1)
        scan.assert_called_once()
        pack.assert_not_called()

    def test_yayin_git_bulunamazsa_taban_cozulemez_pack_denenmez(self):
        """Git yoksa temizlik denetimi uyarıyla atlanır (MSBuild kapısı
        ikinci hattır). taban adımının ikinci hattı yoktur - sessiz
        atlama yerine kırmızı, pack hiç denenmez."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            (root / "src").mkdir()
            output = io.StringIO()

            with mock.patch.object(kapi, "_git", return_value=None), \
                    mock.patch.object(kapi, "scan", return_value=0):
                with mock.patch("subprocess.run", side_effect=OSError("git yok")) as run:
                    with contextlib.redirect_stdout(output):
                        result = kapi.release_rehearsal(None, root=root, release_dir=root / "artifacts" / "package" / "release")

        self.assertEqual(result, 1)
        self.assertIn("Taban çözülemedi", output.getvalue())
        self.assertFalse(any(call.args[0][0] == "dotnet" for call in run.call_args_list))

    # --- Taban, izole restore ve kırıcı kapının sırası -------------

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
            stack.enter_context(mock.patch.object(kapi, "scan", return_value=0))
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
        """Dizinin İÇİNDE geçerli kalır: aynı sürümün yeni commit'i aynı
        dizine düşer ve farklı içerik reddedilir."""
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
            archive.writestr(f"lib/net10.0/{project_id}.xml", "<doc />")
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
            _write_src_build_props(root)
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
        """`version="x"` = `>= x`; yayınlanmış preview.2 Hosting 1.22 ile
        uyarısız restore olup çalışma anında düştü."""
        with tempfile.TemporaryDirectory() as directory:
            result, output = self._finish_packages(pathlib.Path(directory), {
                "Tracon.AspNetCore": (self.HOSTING.format("1.20.0-preview.260831.1"), {})})

        self.assertEqual(result, 1, output)
        self.assertIn("tam aralık değil: Microsoft.Agents.AI.Hosting 1.20.0-preview.260831.1", output)

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
        """gömülü React/TanStack paketlenir, MIT bildirimi kopyayla gider."""
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            self._ui_notice(root, b"react\n")
            result, output = self._finish_packages(root, {"Tracon.UI": ("", {})})

        self.assertEqual(result, 1, output)
        self.assertIn("THIRD-PARTY-NOTICES.txt paket kökünde yok", output)

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

    def test_kapi_depoyu_yuzey_okuyucularina_verir(self):
        """The PublicAPI, `required` and contract readers need the repo whose tag is the baseline."""
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/yok"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes

        import sozlesme_kirilma

        with mock.patch.object(breaking_changes, "check", return_value=0) as checked, \
                mock.patch.object(sozlesme_kirilma, "check", return_value=0):
            result = kapi._check_breaking_changes(ROOT, gate, "1.0.0-preview.3")

        self.assertEqual(result, 0)
        self.assertEqual(checked.call_args.kwargs["repo"], ROOT)
        self.assertEqual(checked.call_args.kwargs["version"], "1.0.0-preview.3")

    def test_kapi_sozlesme_yuzeylerini_apicompat_kapisindan_sonra_okur(self):
        """The non-.NET surfaces are read after ApiCompat, with the same baseline, version and repo."""
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/yok"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes
        import sozlesme_kirilma

        order: list[str] = []
        with mock.patch.object(breaking_changes, "check", side_effect=lambda **_: order.append("apicompat") or 0), \
                mock.patch.object(sozlesme_kirilma, "check", side_effect=lambda **_: order.append("sozlesme") or 0) as surfaces:
            result = kapi._check_breaking_changes(ROOT, gate, "1.0.0-preview.3")

        self.assertEqual(result, 0)
        self.assertEqual(order, ["apicompat", "sozlesme"])
        self.assertEqual(surfaces.call_args.kwargs["repo"], ROOT)
        self.assertEqual(surfaces.call_args.kwargs["baseline"], "1.0.0-preview.2")
        self.assertEqual(surfaces.call_args.kwargs["version"], "1.0.0-preview.3")

    def test_apicompat_kirmizisi_sozlesme_okuyucusunu_calistirmaz(self):
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/yok"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes
        import sozlesme_kirilma

        with mock.patch.object(breaking_changes, "check", return_value=1), \
                mock.patch.object(sozlesme_kirilma, "check", return_value=0) as surfaces:
            result = kapi._check_breaking_changes(ROOT, gate, "1.0.0-preview.3")

        self.assertEqual(result, 1)
        surfaces.assert_not_called()

    def test_sozlesme_okuyucusunun_kirmizisi_provayi_kirmizi_yapar(self):
        gate = kapi.BreakingChangeGate(baseline="1.0.0-preview.2", report_dir=pathlib.Path("/yok"))
        sys.path.insert(0, str(ROOT / "scripts"))
        import breaking_changes
        import sozlesme_kirilma

        with mock.patch.object(breaking_changes, "check", return_value=0), \
                mock.patch.object(sozlesme_kirilma, "check", return_value=1):
            result = kapi._check_breaking_changes(ROOT, gate, "1.0.0-preview.3")

        self.assertEqual(result, 1)


class TekDerlemeZinciriTestleri(unittest.TestCase):
    """`paketle` · `paket-dogrula` · `yayin --paket-dizini`.

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
        _write_src_build_props(root)
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
                archive.writestr(f"lib/net10.0/{project_id}.xml", "<doc />")
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
            # The surfaces of the other kinds are read after ApiCompat; their own tests are in sozlesme_kirilma_test.py.
            stack.enter_context(mock.patch.object(importlib.import_module("sozlesme_kirilma"), "check", return_value=0))
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
        """Farksız pakette SDK rapor yazmaz."""
        with tempfile.TemporaryDirectory() as directory:
            _, output = self._packed(directory, report=False)

            records = self._manifest(output)["apiCompat"]

        self.assertEqual(records, [{"id": "Tracon.Core", "report": None, "sha256": None, "validationRan": True}])

    def test_paketle_bypass_bayragi_tasimaz(self):
        """`paketle` iç araç listesine girmez."""
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
            # Pack anı kanıtı ortak yolda da çağrılmaz.
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
        """--surum ile --paket-dizini birlikte izinlidir."""
        with tempfile.TemporaryDirectory() as directory:
            root, output = self._packed(directory)

            result, _, text, *_ = self._rehearse(root, output, version="1.0.0-preview.3")

        self.assertEqual(result, 1)
        self.assertIn("Paket üretilmedi", text)

    def test_paket_dizini_modu_release_dirde_girdi_disi_paket_reddedilir(self):
        """Ret, dosya silinmez."""
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
        """Kopya yolunda da: aynı ad, farklı içerik -> hiçbir dosya kopyalanmaz."""
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
    """Bir secret tarayıcısını doğrulamanın tek yolu ona sahte bir
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
    """2026-09-19. Kapı YEŞİL derken `arsiv` altında gerçek bir
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
        found, _ = self._tara("arsiv/fazlar/99-X.md", self.URETILMIS_ANAHTAR)

        self.assertEqual(len(found), 1)

    def test_uretilmis_anahtar_manuel_testte_yakalanir(self):
        found, _ = self._tara("manuel-test/99-X.md", self.URETILMIS_ANAHTAR)

        self.assertEqual(len(found), 1)

    def test_yerel_kurulum_deyimi_arsivde_kapiyi_kirmaz(self):
        # Arşiv ve manuel test kaydında bir docker parolası kusur değil, tekrar
        # üretilebilirlik talimatıdır. Kapsamı tüm desenlere açmak 48 satırı
        # işaretletirdi ve istisna listesi kapının kendisini anlamsızlaştırırdı.
        found, skipped = self._tara("arsiv/fazlar/99-Y.md", SECRET_LINE)

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
        found, _ = self._tara("arsiv/fazlar/99-Y.md", 'keyPrefix":"ap_default_2"')

        self.assertEqual(found, [])

    def test_arsivdeki_isaretli_satir_atlanir_ve_sayilir(self):
        found, skipped = self._tara(
            "arsiv/fazlar/99-X.md", self.URETILMIS_ANAHTAR + " <!-- " + MARKER + " -->")

        self.assertEqual(found, [])
        self.assertEqual(skipped, 1)


class MigrasyonTabaniGecmisteTestleri(unittest.TestCase):
    """Bir ayna yeniden yazılmış geçmiş taşır: manifestin tabanı orada yoktur."""

    def _depo(self, kok: pathlib.Path) -> str:
        env = {**os.environ, "GIT_AUTHOR_NAME": "t", "GIT_AUTHOR_EMAIL": "t@t",
               "GIT_COMMITTER_NAME": "t", "GIT_COMMITTER_EMAIL": "t@t"}
        for komut in (["init", "-q"], ["commit", "-q", "--allow-empty", "-m", "x"]):
            subprocess.run(["git", *komut], cwd=kok, check=True, capture_output=True, env=env)
        return subprocess.run(["git", "rev-parse", "HEAD"], cwd=kok, check=True,
                              capture_output=True, text=True).stdout.strip()

    def _manifest(self, kok: pathlib.Path, taban: str) -> None:
        (kok / "scripts").mkdir()
        (kok / kapi.APPLIED_MIGRATION_MANIFEST).write_text(
            json.dumps({"baselineCommit": taban, "sourceCommits": {}}), encoding="utf-8")

    def test_tabani_olmayan_tam_gecmis_dogrulanamadi_sayilir(self):
        with tempfile.TemporaryDirectory() as gecici:
            kok = pathlib.Path(gecici)
            self._depo(kok)
            self._manifest(kok, "0" * 40)

            self.assertEqual(kapi.migration_anchor_not_in_history(kok), "0" * 40)

    def test_tabani_olan_depo_dogrulanabilir(self):
        with tempfile.TemporaryDirectory() as gecici:
            kok = pathlib.Path(gecici)
            self._manifest(kok, self._depo(kok))

            self.assertIsNone(kapi.migration_anchor_not_in_history(kok))

    def test_okunamayan_manifest_dogrulanamadi_sayilmaz(self):
        with tempfile.TemporaryDirectory() as gecici:
            kok = pathlib.Path(gecici)
            self._depo(kok)

            self.assertIsNone(kapi.migration_anchor_not_in_history(kok))

    def test_git_olmayan_dizin_dogrulanamadi_sayilmaz(self):
        with tempfile.TemporaryDirectory() as gecici:
            kok = pathlib.Path(gecici)
            self._manifest(kok, "0" * 40)

            self.assertIsNone(kapi.migration_anchor_not_in_history(kok))


class KapasiteKomutuTestleri(unittest.TestCase):
    """Kapasite ölçümü bir KAPI DEĞİLDİR."""

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


class ModulSureButcesiTestleri(unittest.TestCase):
    """Her test modulu mutlak bir duvar suresi butcesi tasiyabilir; kapanista butcesi olan modul denetlenir."""

    @staticmethod
    def _trx(kok: pathlib.Path, proje: str, saniye: float, tfm: str = "net10.0_arm64") -> None:
        dizin = kok / kapi.TEST_RESULTS_DIRECTORY / proje / tfm
        dizin.mkdir(parents=True, exist_ok=True)
        (dizin / "sonuc.trx").write_text(
            '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
            f'<Times start="2026-11-09T11:00:00Z" finish="{_saat(saniye)}" />'
            '<Results><UnitTestResult testName="N.S.A" duration="00:00:01" outcome="Passed" /></Results></TestRun>',
            encoding="utf-8")

    def _kos(self, sureler: dict[str, float], limits: dict[str, int]) -> tuple[int, str]:
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            for proje, saniye in sureler.items():
                self._trx(kok, proje, saniye)
            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits=limits)
        return kod, output.getvalue()

    def test_rapor_her_modulu_suresi_ve_butce_payiyla_listeler_en_yavas_ustte(self):
        kod, metin = self._kos({"A": 100, "B": 300, "C": 50}, {"A": 200, "B": 400})

        self.assertEqual(kod, 0)
        satirlar = [satir.strip() for satir in metin.splitlines() if "net10.0_arm64" in satir]
        self.assertEqual(satirlar, [
            "B/net10.0_arm64: 300 s · sınır 400 s · kalan 100 s (%25)",
            "A/net10.0_arm64: 100 s · sınır 200 s · kalan 100 s (%50)",
            "C/net10.0_arm64: 50 s · sınır yok",
        ])

    def test_butcesi_olmayan_yavas_modul_kapiyi_kirmaz_butcesi_olan_asan_kirar(self):
        kod, metin = self._kos({"Yavas": 5000, "Sinirli": 61}, {"Sinirli": 60})

        self.assertEqual(kod, kapi.MODULE_TIME_LIMIT_EXIT_CODE)
        self.assertIn("Sinirli/net10.0_arm64: 61 s · sınır 60 s · AŞILDI 1 s", metin)
        self.assertIn("Sinirli/net10.0_arm64 61 s > 60 s", metin)
        self.assertNotIn("Yavas/net10.0_arm64 5000 s >", metin)

    def test_butce_sinirin_tam_ustunde_gecer_bir_saniye_ustunde_kirar(self):
        self.assertEqual(self._kos({"A": 60}, {"A": 60})[0], 0)
        self.assertEqual(self._kos({"A": 61}, {"A": 60})[0], kapi.MODULE_TIME_LIMIT_EXIT_CODE)

    def test_butce_modul_basinadir_her_tfm_bacagi_ayri_denetlenir(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._trx(kok, "A", 30, "net10.0_arm64")
            self._trx(kok, "A", 90, "net9.0_arm64")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits={"A": 60})

        self.assertEqual(kod, kapi.MODULE_TIME_LIMIT_EXIT_CODE)
        self.assertIn("A/net9.0_arm64 90 s > 60 s", output.getvalue())
        self.assertNotIn("A/net10.0_arm64 30 s >", output.getvalue())

    def test_suresi_okunamayan_butcesiz_modul_uyarir_kapiyi_kirmaz(self):
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            dizin = kok / kapi.TEST_RESULTS_DIRECTORY / "Q" / "net10.0_arm64"
            dizin.mkdir(parents=True)
            (dizin / "sonuc.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results/></TestRun>',
                encoding="utf-8")
            with contextlib.redirect_stdout(io.StringIO()) as output:
                kod = kapi.enforce_module_time_limits(0.0, root=kok, limits={})

        self.assertEqual(kod, 0)
        self.assertIn("Q/net10.0_arm64: TRX süresi okunamadı", output.getvalue())

    def test_gercek_butce_tablosu_gercek_modul_adlarini_kullanir_ve_kapanista_calisir(self):
        # Tablo tek yerdedir; kapanis test adimi onu varsayilan olarak okur.
        with tempfile.TemporaryDirectory() as directory:
            kok = pathlib.Path(directory)
            self._trx(kok, "Tracon.TwoNode.IntegrationTests", kapi.TEST_MODULE_TIME_LIMITS_SECONDS[
                "Tracon.TwoNode.IntegrationTests"] + 1)
            with contextlib.redirect_stdout(io.StringIO()):
                kod = kapi.enforce_module_time_limits(0.0, root=kok)

        self.assertEqual(kod, kapi.MODULE_TIME_LIMIT_EXIT_CODE)

    def test_sure_degiskenligi_kapanista_denetlenmez(self):
        # Varyans denetimi dedektordedir (`kararlilik`); kapanis yalniz mutlak butceyi bilir.
        komutlar = [komut.display for komut in kapi.closing_commands("abc123", site=True, performance=True)]

        self.assertFalse(any("kararlilik" in komut for komut in komutlar))
        self.assertFalse(any("kararlilik" in komut.display for komut in kapi.staged_test_commands()))
        self.assertFalse(any("kararlilik" in komut.display for komut in kapi.inner_loop_commands(["src/Tracon.Core/A.cs"])))


def _saat(saniye: float) -> str:
    """`2026-11-09T11:00:00Z`'den `saniye` sonrasi, TRX damgasi olarak."""
    ani = datetime.datetime(2026, 11, 9, 11, 0, 0, tzinfo=datetime.timezone.utc) + datetime.timedelta(seconds=saniye)
    return ani.strftime("%Y-%m-%dT%H:%M:%S.%f") + "Z"


def _trx_suresi(milisaniye: int) -> str:
    return f"{milisaniye // 3600000:02d}:{milisaniye // 60000 % 60:02d}:{(milisaniye % 60000) / 1000:010.7f}"


def _kosum_trx(
    dizin: pathlib.Path, testler: dict[str, tuple[int, str]], duvar_saniye: float = 10.0, ad: str = "result.trx",
) -> pathlib.Path:
    """Sentetik TRX: `testler` sirasi baslama sirasidir, her test 10 ms sonra baslar."""
    dizin.mkdir(parents=True, exist_ok=True)
    temel = datetime.datetime(2026, 11, 9, 11, 0, 0, tzinfo=datetime.timezone.utc)
    satirlar = []
    for sira, (test, (milisaniye, durum)) in enumerate(testler.items()):
        basladi = (temel + datetime.timedelta(milliseconds=10 * sira)).isoformat()
        satirlar.append(
            f'<UnitTestResult testName="{test}" duration="{_trx_suresi(milisaniye)}" startTime="{basladi}"'
            f' outcome="{durum}" />')
    yol = dizin / ad
    yol.write_text(
        '<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">'
        f'<Times start="{_saat(0)}" finish="{_saat(duvar_saniye)}" />'
        f"<Results>{''.join(satirlar)}</Results></TestRun>",
        encoding="utf-8")
    return yol


def _kosum(
    etiket: str = "M", sira: int = 1, *, baski: bool = False, cikis: int = 0, duvar: float | None = 10.0,
    testler: dict[str, tuple[int, str]] | None = None, baslama_sirasi: list[str] | None = None, sorun: str = "",
    tohum: int | None = None,
) -> "kapi.ModuleRun":
    testler = {"N.A": (100, "Passed"), "N.B": (50, "Passed")} if testler is None else testler
    return kapi.ModuleRun(
        etiket, sira, baski, tohum, cikis, (duvar or 0) + 1, duvar,
        {ad: sure for ad, (sure, _) in testler.items()},
        {ad: (durum,) for ad, (_, durum) in testler.items()},
        tuple(testler if baslama_sirasi is None else baslama_sirasi), sorun)


class KararlilikKarsilastirmaTestleri(unittest.TestCase):
    """Sayilar esikte: hemen icinde yesil, hemen disinda kirmizi. Sonuc kimligi ve cikis kodu onceligi."""

    # --- esikler ---------------------------------------------------------------------

    def test_esikler_yalniz_sikilasir(self):
        # Bugunku degerleri sabitler: bir gevsetme gozden kacmasin (kapi.py yorumundaki kural).
        self.assertLessEqual(kapi.STABILITY_TEST_FLOOR_MS, 200)
        self.assertLessEqual(kapi.STABILITY_TEST_MEDIAN_FACTOR, 1)
        self.assertLessEqual(kapi.STABILITY_MODULE_WALL_PERCENT, 15)
        self.assertLessEqual(kapi.STABILITY_MODULE_WALL_FLOOR_MS, 200)
        self.assertGreaterEqual(kapi.STABILITY_DEFAULT_REPEAT, 5)

    def test_cikis_kodlari_ayridir_ve_mtp_ile_kapi_kodlariyla_cakismaz(self):
        kodlar = {
            kapi.STABILITY_TIME_EXIT_CODE, kapi.STABILITY_RESULT_EXIT_CODE, kapi.STABILITY_USAGE_EXIT_CODE,
            kapi.MODULE_TIME_LIMIT_EXIT_CODE}

        self.assertEqual(len(kodlar), 4)
        self.assertTrue(all(kod > 12 for kod in (kapi.STABILITY_TIME_EXIT_CODE, kapi.STABILITY_RESULT_EXIT_CODE)))

    def test_modul_duvar_farki_tam_yuzde_15te_gecer_hemen_ustunde_kirmizi(self):
        icinde, yuzde = kapi.wall_spread_exceeded([100.0, 115.0, 108.0])
        disinda, _ = kapi.wall_spread_exceeded([100.0, 115.001])

        self.assertFalse(icinde)
        self.assertEqual(yuzde, 15.0)
        self.assertTrue(disinda)

    def test_modul_duvar_farki_kisa_modulde_200_ms_tabana_yaslanir(self):
        # 0,5 sn'lik modulde %15 = 75 ms: zamanlayici gurultusu. Taban 200 ms.
        self.assertFalse(kapi.wall_spread_exceeded([0.5, 0.7])[0])
        self.assertTrue(kapi.wall_spread_exceeded([0.5, 0.701])[0])
        self.assertFalse(kapi.wall_spread_exceeded([0.5, 0.58])[0], "%16 ama 80 ms: taban altinda")

    def test_modul_duvar_farki_uzun_modulde_yuzde_belirler_taban_degil(self):
        self.assertFalse(kapi.wall_spread_exceeded([400.0, 460.0])[0])
        self.assertTrue(kapi.wall_spread_exceeded([400.0, 460.1])[0])

    def test_test_suresi_farki_200_ms_tabaninda_gecer_hemen_ustunde_kirmizi(self):
        icinde = kapi.duration_spreads([_kosum(testler={"N.T": (10, "Passed")}), _kosum(sira=2, testler={"N.T": (210, "Passed")})])
        disinda = kapi.duration_spreads([_kosum(testler={"N.T": (10, "Passed")}), _kosum(sira=2, testler={"N.T": (211, "Passed")})])

        self.assertEqual((icinde[0].span_ms, icinde[0].allowed_ms, icinde[0].exceeded), (200, 200, False))
        self.assertTrue(disinda[0].exceeded)

    def test_test_suresi_farki_medyanin_bir_katinda_gecer_hemen_ustunde_kirmizi(self):
        def spreads(*sureler):
            return kapi.duration_spreads(
                [_kosum(sira=i + 1, testler={"N.T": (sure, "Passed")}) for i, sure in enumerate(sureler)])

        self.assertFalse(spreads(500, 1000, 1500)[0].exceeded, "aralik 1000 = medyan 1000")
        self.assertTrue(spreads(500, 1000, 1501)[0].exceeded)

    def test_test_suresi_yalniz_her_kosumda_olculen_testler_icin_karsilastirilir(self):
        kosumlar = [
            _kosum(testler={"N.A": (10, "Passed"), "N.Sadece1": (9999, "Passed")}),
            _kosum(sira=2, testler={"N.A": (20, "Passed")}),
        ]

        self.assertEqual([spread.name for spread in kapi.duration_spreads(kosumlar)], ["N.A"])

    # --- sure hukmu -------------------------------------------------------------------

    def test_tek_testin_suresi_kayarsa_modul_sure_kirmizisi_olur_ve_testi_adlandirir(self):
        kosumlar = [
            _kosum(sira=1, testler={"N.A": (100, "Passed"), "N.Yavas": (100, "Passed")}),
            _kosum(sira=2, testler={"N.A": (100, "Passed"), "N.Yavas": (900, "Passed")}),
            _kosum(sira=3, testler={"N.A": (100, "Passed"), "N.Yavas": (100, "Passed")}),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "süre")
        self.assertEqual(karsilastirma.result_problems, [])
        self.assertEqual(len(karsilastirma.time_problems), 1)
        self.assertIn("N.Yavas", karsilastirma.time_problems[0])
        self.assertIn("800 ms > 200 ms", karsilastirma.time_problems[0])
        self.assertEqual(karsilastirma.worst_test.name, "N.Yavas")
        self.assertEqual(kapi.stability_exit_code([karsilastirma]), kapi.STABILITY_TIME_EXIT_CODE)

    def test_modul_duvar_suresi_kayarsa_sure_kirmizisi_olur(self):
        kosumlar = [_kosum(sira=1, duvar=100.0), _kosum(sira=2, duvar=116.0), _kosum(sira=3, duvar=101.0)]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "süre")
        self.assertIn("modül duvar süresi", karsilastirma.time_problems[0])
        self.assertEqual(karsilastirma.wall_spread_percent, 16.0)

    def test_sureleri_sinir_icindeyse_yesil(self):
        kosumlar = [_kosum(sira=1, duvar=100.0), _kosum(sira=2, duvar=114.0), _kosum(sira=3, duvar=103.0)]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "yeşil")
        self.assertEqual(kapi.stability_exit_code([karsilastirma]), 0)

    def test_cpu_baskili_kosumun_suresi_raporlanir_ama_kirmizi_saymaz(self):
        kosumlar = [
            _kosum(sira=1, duvar=100.0), _kosum(sira=2, duvar=102.0), _kosum(sira=3, duvar=101.0),
            _kosum(sira=4, baski=True, duvar=300.0, testler={"N.A": (5000, "Passed"), "N.B": (50, "Passed")}),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "yeşil")
        self.assertEqual(karsilastirma.judged_runs, 3)
        self.assertEqual(karsilastirma.pressured_wall_seconds, (300.0,))
        self.assertEqual(karsilastirma.pressured_runs, 1)

    def test_baskisiz_temiz_kosum_ikiden_azsa_sure_karsilastirilmaz_ve_not_dusulur(self):
        kosumlar = [_kosum(sira=1), _kosum(sira=2, baski=True, duvar=500.0)]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "yeşil")
        self.assertIn("süre karşılaştırılamadı", karsilastirma.notes[0])

    # --- sonuc kimligi ------------------------------------------------------------------

    def test_bir_kosumda_eksik_test_sonuc_kirmizisidir_ve_testi_adlandirir(self):
        kosumlar = [
            _kosum(sira=1), _kosum(sira=2, testler={"N.A": (100, "Passed")}), _kosum(sira=3),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertEqual(len(karsilastirma.result_problems), 1)
        self.assertIn("N.B — sonuç değişti", karsilastirma.result_problems[0])
        self.assertIn("koşum 2: yok", karsilastirma.result_problems[0])

    def test_bir_kosumda_fazladan_test_sonuc_kirmizisidir(self):
        kosumlar = [
            _kosum(sira=1), _kosum(sira=2, testler={"N.A": (100, "Passed"), "N.B": (50, "Passed"), "N.Yeni": (1, "Passed")}),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertIn("N.Yeni", karsilastirma.result_problems[0])

    def test_sonuc_degisen_test_adlandirilir_ve_sonuc_sure_kirmizisini_ezer(self):
        kosumlar = [
            _kosum(sira=1, testler={"N.A": (100, "Passed"), "N.B": (50, "Passed")}),
            _kosum(sira=2, testler={"N.A": (100, "Passed"), "N.B": (900, "Failed")}),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertIn("N.B — sonuç değişti (koşum 1: Passed, koşum 2: Failed)", karsilastirma.result_problems[0])
        self.assertEqual(kapi.stability_exit_code([karsilastirma]), kapi.STABILITY_RESULT_EXIT_CODE)

    def test_her_kosumda_dusen_test_de_sonuc_kirmizisidir(self):
        kosumlar = [_kosum(sira=i, testler={"N.A": (100, "Failed"), "N.B": (50, "Passed")}, cikis=2) for i in (1, 2, 3)]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertEqual(karsilastirma.result_problems, ["N.A — her koşumda düştü (Failed)"])

    def test_atlanan_test_her_kosumda_atlaniyorsa_sonuc_aynidir(self):
        kosumlar = [_kosum(sira=i, testler={"N.A": (0, "NotExecuted"), "N.B": (50, "Passed")}) for i in (1, 2)]

        self.assertEqual(kapi.compare_module("M", kosumlar).verdict, "yeşil")

    def test_atlanan_test_bir_kosumda_kosarsa_sonuc_degismistir(self):
        kosumlar = [
            _kosum(sira=1, testler={"N.A": (0, "NotExecuted")}),
            _kosum(sira=2, testler={"N.A": (10, "Passed")}),
        ]

        self.assertEqual(kapi.compare_module("M", kosumlar).verdict, "sonuç")

    def test_cikis_kodu_sifir_degilse_ve_raporda_dusen_yoksa_sonuc_kirmizisi(self):
        kosumlar = [_kosum(sira=1), _kosum(sira=2, cikis=8)]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertIn("koşum 2: çıkış kodu 8", karsilastirma.result_problems[0])

    def test_raporu_olmayan_kosum_sonuc_kirmizisidir(self):
        kosumlar = [_kosum(sira=1), _kosum(sira=2, cikis=1, duvar=None, testler={}, sorun="TRX raporu yok ya da okunamadı (çıkış 1)")]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertIn("koşum 2: TRX raporu yok", karsilastirma.result_problems[0])

    def test_cpu_baskili_kosumda_sonuc_degisirse_kirmizi(self):
        kosumlar = [
            _kosum(sira=1), _kosum(sira=2),
            _kosum(sira=3, baski=True, testler={"N.A": (100, "Passed"), "N.B": (50, "Failed")}, cikis=2),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar)

        self.assertEqual(karsilastirma.verdict, "sonuç")
        self.assertIn("N.B", karsilastirma.result_problems[0])

    def test_ayni_adli_theory_satirlari_cokluk_olarak_karsilastirilir(self):
        ayni = _kosum(sira=1)
        ayni_iki_satir = dataclasses.replace(ayni, outcomes={"N.A": ("Passed", "Passed"), "N.B": ("Passed",)})
        eksik_satir = dataclasses.replace(ayni, index=2, outcomes={"N.A": ("Passed",), "N.B": ("Passed",)})

        self.assertEqual(kapi.compare_module("M", [ayni_iki_satir, eksik_satir]).verdict, "sonuç")

    # --- cikis kodu onceligi ----------------------------------------------------------------

    def test_sonuc_kirmizisi_sure_kirmizisindan_once_gelir_hicbiri_yoksa_sifir(self):
        yesil = kapi.compare_module("Y", [_kosum("Y", 1), _kosum("Y", 2)])
        sure = kapi.compare_module("S", [_kosum("S", 1, duvar=100.0), _kosum("S", 2, duvar=200.0)])
        sonuc = kapi.compare_module("R", [_kosum("R", 1), _kosum("R", 2, testler={"N.A": (1, "Failed")}, cikis=2)])

        self.assertEqual(kapi.stability_exit_code([yesil]), 0)
        self.assertEqual(kapi.stability_exit_code([yesil, sure]), kapi.STABILITY_TIME_EXIT_CODE)
        self.assertEqual(kapi.stability_exit_code([yesil, sonuc]), kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertEqual(kapi.stability_exit_code([sure, sonuc]), kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertEqual(kapi.stability_exit_code([sonuc, sure]), kapi.STABILITY_RESULT_EXIT_CODE)

    # --- siralama kaniti -----------------------------------------------------------------------

    def test_siralama_benzerligi_ayni_sira_bir_ters_sira_sifir(self):
        self.assertEqual(kapi.order_similarity(["a", "b", "c", "d"], ["a", "b", "c", "d"]), 1.0)
        self.assertEqual(kapi.order_similarity(["a", "b", "c", "d"], ["d", "c", "b", "a"]), 0.0)
        self.assertEqual(kapi.order_similarity(["a", "b", "c"], ["a", "c", "b"]), 0.5)
        self.assertIsNone(kapi.order_similarity(["a"], ["a"]))
        self.assertIsNone(kapi.order_similarity(["a", "b"], ["x", "y"]))

    def test_karistirilmis_modulde_baslama_sirasi_ayniysa_etkisiz_notu_dusulur(self):
        testler = {f"N.T{i:02d}": (10, "Passed") for i in range(30)}
        kosumlar = [_kosum(sira=1, testler=testler), _kosum(sira=2, testler=testler)]

        karistirilmis = kapi.compare_module("M", kosumlar, shuffled=True)
        karistirilmamis = kapi.compare_module("M", kosumlar, shuffled=False)

        self.assertTrue(any("görünür etkisi yok" in not_ for not_ in karistirilmis.notes))
        self.assertFalse(any("görünür etkisi yok" in not_ for not_ in karistirilmamis.notes))

    def test_baslama_sirasi_gercekten_degistiyse_etkisiz_notu_yok(self):
        adlar = [f"N.T{i:02d}" for i in range(30)]
        testler = {ad: (10, "Passed") for ad in adlar}
        kosumlar = [
            _kosum(sira=1, testler=testler, baslama_sirasi=adlar),
            _kosum(sira=2, testler=testler, baslama_sirasi=adlar[::-1]),
        ]

        karsilastirma = kapi.compare_module("M", kosumlar, shuffled=True)

        self.assertEqual(karsilastirma.order_similarity, [0.0])
        self.assertEqual(karsilastirma.notes, [])

    def test_kisa_modulde_siralama_notu_dusulmez(self):
        kosumlar = [_kosum(sira=1), _kosum(sira=2)]

        self.assertEqual(kapi.compare_module("M", kosumlar, shuffled=True).notes, [])

    # --- TRX okuma -----------------------------------------------------------------------------

    def test_trx_raporu_sure_sonuc_ve_baslama_sirasini_okur(self):
        with tempfile.TemporaryDirectory() as directory:
            dizin = pathlib.Path(directory)
            _kosum_trx(dizin, {"N.B": (1500, "Passed"), "N.A": (20, "Failed")}, duvar_saniye=12.5)

            rapor = kapi.read_module_report(dizin)

        self.assertEqual(rapor.wall_seconds, 12.5)
        self.assertEqual(rapor.durations_ms, {"N.B": 1500, "N.A": 20})
        self.assertEqual(rapor.outcomes, {"N.B": ("Passed",), "N.A": ("Failed",)})
        self.assertEqual(rapor.order, ("N.B", "N.A"))

    def test_trx_raporu_ayni_adli_satirlarda_en_uzun_sureyi_ve_tum_sonuclari_tutar(self):
        with tempfile.TemporaryDirectory() as directory:
            dizin = pathlib.Path(directory)
            (dizin / "result.trx").write_text(
                '<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results>'
                '<UnitTestResult testName="N.T" duration="00:00:00.1000000" outcome="Passed" />'
                '<UnitTestResult testName="N.T" duration="00:00:00.3000000" outcome="Failed" />'
                "</Results></TestRun>", encoding="utf-8")

            rapor = kapi.read_module_report(dizin)

        self.assertEqual(rapor.durations_ms, {"N.T": 300})
        self.assertEqual(rapor.outcomes, {"N.T": ("Failed", "Passed")})
        self.assertIsNone(rapor.wall_seconds)
        self.assertEqual(rapor.order, ())

    def test_trx_raporu_yoksa_ya_da_bozuksa_none(self):
        with tempfile.TemporaryDirectory() as directory:
            dizin = pathlib.Path(directory)
            self.assertIsNone(kapi.read_module_report(dizin))
            self.assertIsNone(kapi.read_module_report(dizin / "yok"))
            (dizin / "result.trx").write_text("<TestRun", encoding="utf-8")
            self.assertIsNone(kapi.read_module_report(dizin))


class _SahteBaskici:
    """`CpuBurner` yerine gecer: baslat/durdur olaylarini bir ortak olay listesine yazar."""

    def __init__(self, sayi: int, olaylar: list[str]) -> None:
        self.count = sayi
        self.olaylar = olaylar
        self.running = False

    def start(self) -> None:
        self.running = True
        self.olaylar.append("baski basladi")

    def stop(self) -> None:
        if self.running:
            self.olaylar.append("baski durdu")
        self.running = False

    def alive(self) -> int:
        return self.count if self.running else 0


class KararlilikKosumTestleri(unittest.TestCase):
    """`stability_check`: sentetik TRX yazan sahte kosucu ile uctan uca; hicbir dotnet cagrilmaz."""

    def setUp(self):
        self._gecici = tempfile.TemporaryDirectory()
        self.addCleanup(self._gecici.cleanup)
        self.kok = pathlib.Path(self._gecici.name)
        self.olaylar: list[str] = []
        self.cagrilar: list[list[str]] = []
        self._kilit = threading.Lock()

    CORE = kapi.StabilityUnit(
        "Tracon.Core.UnitTests", "release_net10.0", "core", None, pathlib.Path("/b/Tracon.Core.UnitTests"))
    HAFIF = kapi.StabilityUnit("Tracon.Voice.UnitTests", "release", "light", None, pathlib.Path("/b/Tracon.Voice.UnitTests"))
    AGIR = kapi.StabilityUnit(
        "Tracon.AspNetCore.FunctionalTests", "release", "heavy", "b", pathlib.Path("/b/Tracon.AspNetCore.FunctionalTests"))
    VARSAYILAN = {"N.A": (100, "Passed"), "N.B": (50, "Passed")}

    def _kosucu(self, davranis=None):
        """`davranis(proje, kosum_no)` -> None (varsayilan) | dict | (cikis, dict | None, duvar_s)."""
        def kos(args, **kwargs):
            dizin = pathlib.Path(args[args.index("--results-directory") + 1])
            proje = pathlib.Path(args[0]).name
            kosum_no = int(dizin.parent.name.removeprefix("run-"))
            with self._kilit:
                self.cagrilar.append(list(args))
                self.olaylar.append(f"{proje} koşum {kosum_no}")
            sonuc = davranis(proje, kosum_no) if davranis else None
            cikis, testler, duvar = 0, self.VARSAYILAN, 10.0
            if isinstance(sonuc, dict):
                testler = sonuc
            elif sonuc is not None:
                cikis, testler, duvar = sonuc
            if testler is not None:
                _kosum_trx(dizin, testler, duvar)
            return mock.Mock(returncode=cikis)
        return kos

    def _kos(self, units, repeat=5, davranis=None, **secenekler):
        fabrika = secenekler.pop("burner_factory", lambda sayi: _SahteBaskici(sayi, self.olaylar))
        rapor = self.kok / "rapor"
        with contextlib.redirect_stdout(io.StringIO()) as ekran:
            kod = kapi.stability_check(
                units, repeat, root=self.kok, runner=self._kosucu(davranis), burner_factory=fabrika,
                report_directory=rapor, measurement_path=self.kok / "olcum.jsonl", stamp="20261109T000000Z",
                cpu_count=8, **secenekler)
        return types.SimpleNamespace(kod=kod, ekran=ekran.getvalue(), rapor=rapor)

    def _rapor_json(self, sonuc):
        return json.loads((sonuc.rapor / "20261109T000000Z.json").read_text(encoding="utf-8"))

    # --- yesil yol, komut bicimi, sira ------------------------------------------------------------

    def test_ayni_sonuc_ve_sure_yesil_cikis_sifir_rapor_ve_olcum_satiri_yazar(self):
        sonuc = self._kos([self.CORE, self.HAFIF, self.AGIR])

        self.assertEqual(sonuc.kod, 0)
        self.assertEqual(len(self.cagrilar), 15)
        rapor = self._rapor_json(sonuc)
        self.assertEqual(rapor["exit_code"], 0)
        self.assertEqual(rapor["repeat"], 5)
        self.assertEqual(
            sorted(rapor["modules"]),
            ["Tracon.AspNetCore.FunctionalTests", "Tracon.Core.UnitTests (net10.0)", "Tracon.Voice.UnitTests"])
        modul = rapor["modules"]["Tracon.Voice.UnitTests"]
        self.assertEqual(modul["verdict"], "yeşil")
        self.assertEqual([kosum["index"] for kosum in modul["runs"]], [1, 2, 3, 4, 5])
        self.assertEqual(modul["runs"][0]["tests"], 2)
        self.assertEqual(
            rapor["thresholds"],
            {"test_floor_ms": 200, "test_median_factor": 1, "module_wall_percent": 15, "module_wall_floor_ms": 200})
        self.assertEqual(set(rapor), {"utc", "repeat", "shuffled", "base_seed", "pressure", "thresholds", "exit_code", "modules"})
        satir = json.loads((self.kok / "olcum.jsonl").read_text(encoding="utf-8").splitlines()[-1])
        self.assertEqual(
            (satir["stage"], satir["exit_code"], satir["command"]), ("kararlilik", 0, "kapi.py kararlilik --tekrar 5"))
        self.assertIn("Kararlı", sonuc.ekran)

    def test_komut_derlenmis_ikiliyi_trx_bayraklariyla_cagirir_ve_hicbir_seyi_derlemez(self):
        self._kos([self.HAFIF], repeat=2)

        for args in self.cagrilar:
            self.assertEqual(args[0], str(self.HAFIF.executable))
            self.assertIn("--report-trx", args)
            self.assertEqual(args[args.index("--report-trx-filename") + 1], kapi.STABILITY_TRX_NAME)
            self.assertEqual(args[args.index("--timeout") + 1], f"{kapi.STABILITY_MODULE_TIMEOUT_MINUTES}m")
            self.assertNotIn("dotnet", args[0])
            self.assertNotIn("build", args)
            self.assertNotIn("--seed", args)
        dizinler = {args[args.index("--results-directory") + 1] for args in self.cagrilar}
        self.assertEqual(len(dizinler), 2, "her koşum kendi dizinine yazar")
        self.assertTrue(all("20261109T000000Z" in dizin for dizin in dizinler))

    def test_asamalar_core_once_sonra_hafif_sonra_agir_ve_her_kosum_bitmeden_sonrakine_gecilmez(self):
        self._kos([self.AGIR, self.HAFIF, self.CORE], repeat=2)

        sira = [olay for olay in self.olaylar if "koşum" in olay]
        self.assertEqual(sira[:3], [
            "Tracon.Core.UnitTests koşum 1", "Tracon.Voice.UnitTests koşum 1", "Tracon.AspNetCore.FunctionalTests koşum 1"])
        self.assertEqual(sira[3:], [
            "Tracon.Core.UnitTests koşum 2", "Tracon.Voice.UnitTests koşum 2", "Tracon.AspNetCore.FunctionalTests koşum 2"])

    def test_kirmizi_modul_kosumu_durdurmaz_cunku_kararlilik_her_kosumun_bitmesini_ister(self):
        sonuc = self._kos([self.CORE, self.HAFIF], davranis=lambda proje, no: (2, {"N.A": (1, "Failed")}, 5.0)
                          if proje == "Tracon.Core.UnitTests" else None)

        self.assertEqual(len(self.cagrilar), 10)
        self.assertEqual(sonuc.kod, kapi.STABILITY_RESULT_EXIT_CODE)

    # --- sira karistirma ----------------------------------------------------------------------------

    def test_sira_karistir_her_kosuma_farkli_kayitli_tohum_verir(self):
        sonuc = self._kos([self.HAFIF], repeat=3, shuffle=True, base_seed=1000)

        tohumlar = sorted(int(args[args.index("--seed") + 1]) for args in self.cagrilar)
        self.assertEqual(tohumlar, [1000, 1001, 1002])
        rapor = self._rapor_json(sonuc)
        self.assertEqual(rapor["base_seed"], 1000)
        self.assertTrue(rapor["shuffled"])
        self.assertEqual([kosum["seed"] for kosum in rapor["modules"]["Tracon.Voice.UnitTests"]["runs"]], [1000, 1001, 1002])
        self.assertIn("tohum 1000", sonuc.ekran)

    def test_sira_karistirma_kapaliyken_tohum_verilmez_ve_raporlanmaz(self):
        sonuc = self._kos([self.HAFIF], repeat=2, base_seed=1000)

        self.assertFalse(any("--seed" in args for args in self.cagrilar))
        self.assertIsNone(self._rapor_json(sonuc)["base_seed"])

    def test_tohum_verilmezse_saatten_turetilir_ve_rapora_yazilir(self):
        sonuc = self._kos([self.HAFIF], repeat=2, shuffle=True)

        self.assertIsInstance(self._rapor_json(sonuc)["base_seed"], int)

    # --- sonuc ve sure hatalari --------------------------------------------------------------------

    def test_bir_kosumda_sonuc_degisirse_cikis_92_ve_testin_adi_ekranda(self):
        sonuc = self._kos(
            [self.HAFIF], davranis=lambda proje, no: {"N.A": (100, "Passed"), "N.B": (50, "Failed")} if no == 3 else None)

        self.assertEqual(sonuc.kod, kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("N.B — sonuç değişti", sonuc.ekran)
        self.assertEqual(self._rapor_json(sonuc)["exit_code"], 92)
        satir = json.loads((self.kok / "olcum.jsonl").read_text(encoding="utf-8").splitlines()[-1])
        self.assertEqual(satir["exit_code"], 92)

    def test_yalniz_sure_degisirse_cikis_91(self):
        sonuc = self._kos([self.HAFIF], davranis=lambda proje, no: (0, self.VARSAYILAN, 30.0) if no == 2 else None)

        self.assertEqual(sonuc.kod, kapi.STABILITY_TIME_EXIT_CODE)
        self.assertIn("modül duvar süresi", sonuc.ekran)

    def test_sonuc_ve_sure_birlikte_degisirse_sonuc_kodu_kazanir(self):
        def davranis(proje, no):
            if proje == "Tracon.Voice.UnitTests" and no == 2:
                return 0, self.VARSAYILAN, 30.0
            if proje == "Tracon.Core.UnitTests" and no == 3:
                return 2, {"N.A": (1, "Failed")}, 10.0
            return None

        self.assertEqual(self._kos([self.CORE, self.HAFIF], davranis=davranis).kod, kapi.STABILITY_RESULT_EXIT_CODE)

    def test_raporu_olmayan_kosum_kirmizidir_ve_kosum_cokmesi_digerlerini_engellemez(self):
        sonuc = self._kos([self.CORE, self.HAFIF], davranis=lambda proje, no: (1, None, 0) if (proje, no) == ("Tracon.Voice.UnitTests", 2) else None)

        self.assertEqual(sonuc.kod, kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("koşum 2: TRX raporu yok", sonuc.ekran)
        self.assertEqual(len(self.cagrilar), 10)

    def test_testsiz_rapor_kirmizidir(self):
        sonuc = self._kos([self.HAFIF], repeat=2, davranis=lambda proje, no: {})

        self.assertEqual(sonuc.kod, kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("raporda hiç test yok", sonuc.ekran)

    def test_zaman_asimi_ve_baslatma_hatasi_kosum_sorunu_olur_cokme_degil(self):
        def kos(args, **kwargs):
            raise subprocess.TimeoutExpired(args, 1)
        rapor = self.kok / "rapor"
        with contextlib.redirect_stdout(io.StringIO()) as ekran:
            kod = kapi.stability_check(
                [self.HAFIF], 2, root=self.kok, runner=kos, report_directory=rapor,
                measurement_path=self.kok / "olcum.jsonl", stamp="s",
                burner_factory=lambda sayi: _SahteBaskici(sayi, self.olaylar))

        self.assertEqual(kod, kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("dk içinde bitmedi", ekran.getvalue())

    def test_kosucu_beklenmedik_hata_verirse_sonuc_kirmizisi_olur_ve_kosum_kilitlenmez(self):
        def kos(args, **kwargs):
            raise RuntimeError("patladi")
        with contextlib.redirect_stdout(io.StringIO()) as ekran:
            kod = kapi.stability_check(
                [self.CORE, self.HAFIF], 2, root=self.kok, runner=kos, report_directory=self.kok / "r",
                measurement_path=self.kok / "olcum.jsonl", stamp="s",
                burner_factory=lambda sayi: _SahteBaskici(sayi, self.olaylar))

        self.assertEqual(kod, kapi.STABILITY_RESULT_EXIT_CODE)
        self.assertIn("koşucu çöktü", ekran.getvalue())

    # --- CPU baskisi ---------------------------------------------------------------------------------

    def test_baski_varsayilan_olarak_son_iki_kosumda_calisir_ve_bitince_durur(self):
        sonuc = self._kos([self.HAFIF], repeat=5, pressure=True)

        olaylar = self.olaylar
        self.assertEqual(olaylar.count("baski basladi"), 1)
        self.assertEqual(olaylar.count("baski durdu"), 1)
        baslangic = olaylar.index("baski basladi")
        self.assertEqual(olaylar[:baslangic], [f"Tracon.Voice.UnitTests koşum {no}" for no in (1, 2, 3)])
        self.assertEqual(olaylar[baslangic + 1:baslangic + 3], [f"Tracon.Voice.UnitTests koşum {no}" for no in (4, 5)])
        self.assertEqual(olaylar[-1], "baski durdu")
        rapor = self._rapor_json(sonuc)
        self.assertEqual(rapor["pressure"], {"runs": [4, 5], "burners": 4})
        kosumlar = rapor["modules"]["Tracon.Voice.UnitTests"]["runs"]
        self.assertEqual([kosum["pressured"] for kosum in kosumlar], [False, False, False, True, True])
        self.assertIn("(4 yük işlemi)", sonuc.ekran)

    def test_baski_kosumlari_elle_secilir_ve_aralarinda_baski_durur(self):
        self._kos([self.HAFIF], repeat=5, pressure=True, pressure_runs=[2, 4])

        self.assertEqual(self.olaylar.count("baski basladi"), 2)
        self.assertEqual(self.olaylar.count("baski durdu"), 2)
        self.assertEqual(
            [o for o in self.olaylar if o in ("baski basladi", "baski durdu")],
            ["baski basladi", "baski durdu", "baski basladi", "baski durdu"])

    def test_baski_istenmediyse_yuk_islemi_hic_baslamaz(self):
        self._kos([self.HAFIF], repeat=3)

        self.assertNotIn("baski basladi", self.olaylar)

    def test_gecersiz_baski_kosumu_ve_kisa_tekrar_hicbir_sey_kosmadan_reddedilir(self):
        self.assertEqual(self._kos([self.HAFIF], repeat=3, pressure=True, pressure_runs=[4]).kod, kapi.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self._kos([self.HAFIF], repeat=1).kod, kapi.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self._kos([], repeat=3).kod, kapi.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self.cagrilar, [])

    def test_hata_yolunda_yuk_islemleri_temizlenir(self):
        # Bir kosum ortasinda beklenmedik hata yukselirse baski `finally` ile durur.
        with mock.patch.object(kapi, "_execute_stability_run", side_effect=[[], RuntimeError("patladi")]):
            with self.assertRaises(RuntimeError), contextlib.redirect_stdout(io.StringIO()):
                kapi.stability_check(
                    [self.HAFIF], 5, pressure=True, pressure_runs=[2, 3], root=self.kok,
                    runner=self._kosucu(), burner_factory=lambda sayi: _SahteBaskici(sayi, self.olaylar),
                    report_directory=self.kok / "r", measurement_path=self.kok / "olcum.jsonl")

        self.assertEqual(self.olaylar[-2:], ["baski basladi", "baski durdu"])

    def test_baski_baslatilamazsa_kosum_baslamadan_cikis_2_ve_yuk_temizlenir(self):
        class Bozuk(_SahteBaskici):
            def start(self):
                super().start()
                raise OSError("fork basarisiz")

        sonuc = self._kos(
            [self.HAFIF], repeat=3, pressure=True, burner_factory=lambda sayi: Bozuk(sayi, self.olaylar))

        self.assertEqual(sonuc.kod, kapi.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self.olaylar[-1], "baski durdu")
        self.assertIn("CPU baskısı kurulamadı", sonuc.ekran)

    def test_baski_islemleri_baslar_baslamaz_olurse_kosum_baslamaz(self):
        class Olu(_SahteBaskici):
            def alive(self):
                return 0

        sonuc = self._kos([self.HAFIF], repeat=3, pressure=True, burner_factory=lambda sayi: Olu(sayi, self.olaylar))

        self.assertEqual(sonuc.kod, kapi.STABILITY_USAGE_EXIT_CODE)
        self.assertEqual(self.olaylar[-1], "baski durdu")

    def test_rapor_dizini_eski_raporlari_ve_trx_dizinlerini_budar(self):
        rapor = self.kok / "rapor"
        for i in range(kapi.STABILITY_REPORTS_KEEP + 3):
            (rapor).mkdir(exist_ok=True)
            (rapor / f"2020{i:04d}T000000Z.json").write_text("{}", encoding="utf-8")
            (rapor / f"2020{i:04d}T000000Z").mkdir()

        self._kos([self.HAFIF], repeat=2)

        raporlar = sorted(rapor.glob("*.json"))
        dizinler = sorted(p for p in rapor.iterdir() if p.is_dir())
        self.assertEqual(len(raporlar), kapi.STABILITY_REPORTS_KEEP)
        self.assertEqual(len(dizinler), kapi.STABILITY_TRX_DIRECTORIES_KEEP)
        self.assertTrue((rapor / "20261109T000000Z.json").is_file())
        self.assertTrue((rapor / "20261109T000000Z").is_dir())


class CpuYukIsleriTestleri(unittest.TestCase):
    """`CpuBurner`: yuk islemleri ne olursa olsun geride kalmaz."""

    @staticmethod
    def _sahte_popen(olusan: list[mock.Mock], *, ikinci_hata: bool = False):
        def popen(args, **kwargs):
            if ikinci_hata and len(olusan) == 1:
                raise OSError("fork basarisiz")
            surec = mock.Mock()
            surec.poll.return_value = None
            olusan.append(surec)
            return surec
        return popen

    def test_yuk_islemi_sayisi_cpu_sayisinin_yarisidir_en_az_bir(self):
        self.assertEqual(
            [kapi.stability_burner_count(n) for n in (1, 2, 3, 8, 10)], [1, 1, 1, 4, 5])

    def test_baslat_sayi_kadar_islem_acar_durdur_hepsini_sonlandirir_ve_ikinci_durdurma_zararsizdir(self):
        olusan: list[mock.Mock] = []
        baski = kapi.CpuBurner(3, popen=self._sahte_popen(olusan))

        baski.start()
        baski.start()
        self.assertEqual(len(olusan), 3)
        self.assertEqual(baski.alive(), 3)
        baski.stop()
        baski.stop()

        self.assertFalse(baski.running)
        for surec in olusan:
            surec.terminate.assert_called_once()
            surec.wait.assert_called_once()

    def test_with_blogunda_hata_olsa_da_islemler_durur(self):
        olusan: list[mock.Mock] = []

        with self.assertRaises(RuntimeError):
            with kapi.CpuBurner(2, popen=self._sahte_popen(olusan)):
                raise RuntimeError("test patladi")

        for surec in olusan:
            surec.terminate.assert_called_once()

    def test_baslatma_ortada_cokerse_acilmis_islemler_durdurulur(self):
        olusan: list[mock.Mock] = []
        baski = kapi.CpuBurner(3, popen=self._sahte_popen(olusan, ikinci_hata=True))

        with self.assertRaises(OSError):
            baski.start()

        self.assertEqual(len(olusan), 1)
        olusan[0].terminate.assert_called_once()
        self.assertFalse(baski.running)

    def test_sonlanmayan_islem_oldurulur(self):
        olusan: list[mock.Mock] = []
        baski = kapi.CpuBurner(1, popen=self._sahte_popen(olusan))
        baski.start()
        olusan[0].wait.side_effect = [subprocess.TimeoutExpired("x", 5), None]

        baski.stop()

        olusan[0].kill.assert_called_once()

    def test_gercek_yuk_islemi_baslar_calisir_ve_durdurulunca_biter(self):
        with kapi.CpuBurner(1, lifetime_seconds=60) as baski:
            surec = baski._processes[0]
            time.sleep(0.2)
            self.assertEqual(baski.alive(), 1)
            self.assertIsNone(surec.poll())

        self.assertIsNotNone(surec.poll())

    def test_yuk_islemi_omru_dolunca_kendiliginden_biter(self):
        baski = kapi.CpuBurner(1, lifetime_seconds=0.3)
        baski.start()
        surec = baski._processes[0]
        try:
            surec.wait(timeout=20)
        finally:
            baski.stop()

        self.assertEqual(surec.returncode, 0)


class KararlilikKomutuTestleri(unittest.TestCase):
    """Modul secimi, komut satiri ve calisma onkosullari."""

    def test_plan_her_test_modulunu_tam_bir_kez_kapsar(self):
        plan = kapi.stability_plan()
        hafif = [proje for proje, asama, _ in plan if asama == "light"]
        agir = {}
        for proje, asama, hat in plan:
            if asama == "heavy":
                agir.setdefault(hat, []).append(proje)

        self.assertEqual(
            _bolum_sorunlari(_slnx_test_modulleri(), kapi.CORE_TEST_PROJECT, hafif, {k: tuple(v) for k, v in agir.items()}), [])
        self.assertEqual(plan[0], (kapi.CORE_TEST_PROJECT, "core", None))

    def test_birimler_ikili_yolunu_derlenmis_cikti_dizininden_cozer(self):
        birimler = kapi.stability_units(["Tracon.Voice.UnitTests", "Tracon.Core.UnitTests"])

        self.assertEqual([birim.project for birim in birimler], ["Tracon.Core.UnitTests", "Tracon.Voice.UnitTests"])
        voice = birimler[1]
        self.assertEqual(voice.executable, ROOT / "artifacts" / "bin" / "Tracon.Voice.UnitTests" / "release" / "Tracon.Voice.UnitTests")
        self.assertEqual((voice.label, voice.stage, voice.lane), ("Tracon.Voice.UnitTests", "light", None))
        core = birimler[0]
        self.assertTrue(core.output.startswith("release_"), "Core çok hedefli: çıktı dizini TFM taşır")
        self.assertIn("(", core.label)

    def test_haric_listesi_modulu_cikarir_proje_listesi_sinirlar_bilinmeyen_ad_hata(self):
        hepsi = kapi.stability_units()
        haric = kapi.stability_units(exclude=["Tracon.Package.Tests", "Tracon.Ui.E2ETests"])
        sadece = kapi.stability_units(["Tracon.Sqlite.IntegrationTests"])

        self.assertEqual(len(hepsi) - len(haric), 2)
        self.assertNotIn("Tracon.Package.Tests", [birim.project for birim in haric])
        self.assertEqual([birim.project for birim in sadece], ["Tracon.Sqlite.IntegrationTests"])
        self.assertEqual(sadece[0].lane, "b")
        with self.assertRaises(ValueError) as hata:
            kapi.stability_units(["Tracon.Yok"])
        self.assertIn("Tracon.Yok", str(hata.exception))
        with self.assertRaises(ValueError):
            kapi.stability_units(exclude=["Tracon.Yazim"])

    def _args(self, **degisen):
        varsayilan = dict(
            tekrar=5, proje=None, haric=None, sira_karistir=False, baski=False, baski_kosumlari=None, tohum=None,
            cikti=None, komutlari_bas=False)
        varsayilan.update(degisen)
        return types.SimpleNamespace(**varsayilan)

    def test_derlenmis_ikili_yoksa_cikis_2_hicbir_sey_kosmadan_ve_derleme_onerilir(self):
        with mock.patch.object(kapi, "stability_check") as kontrol, \
                mock.patch.object(kapi, "stability_units", return_value=[
                    kapi.StabilityUnit("P", "release", "light", None, pathlib.Path("/yok/P"))]), \
                contextlib.redirect_stdout(io.StringIO()) as ekran:
            kod = kapi.stability_from_arguments(self._args())

        self.assertEqual(kod, kapi.STABILITY_USAGE_EXIT_CODE)
        kontrol.assert_not_called()
        self.assertIn("derlemez", ekran.getvalue())
        self.assertIn("dotnet build", ekran.getvalue())

    def test_tekrar_en_az_iki_ve_bilinmeyen_modul_reddedilir(self):
        with contextlib.redirect_stdout(io.StringIO()):
            self.assertEqual(kapi.stability_from_arguments(self._args(tekrar=1)), kapi.STABILITY_USAGE_EXIT_CODE)
            self.assertEqual(kapi.stability_from_arguments(self._args(proje=["Tracon.Yok"])), kapi.STABILITY_USAGE_EXIT_CODE)

    def test_hazir_ikililer_varsa_secenekler_stability_check_a_gecer(self):
        with tempfile.TemporaryDirectory() as dizin:
            ikili = pathlib.Path(dizin) / "P"
            ikili.write_text("", encoding="utf-8")
            birim = kapi.StabilityUnit("P", "release", "light", None, ikili)
            with mock.patch.object(kapi, "stability_check", return_value=0) as kontrol, \
                    mock.patch.object(kapi, "stability_units", return_value=[birim]):
                kod = kapi.stability_from_arguments(self._args(
                    tekrar=7, sira_karistir=True, baski_kosumlari=[2, 3], tohum=42, cikti=dizin))

        self.assertEqual(kod, 0)
        kontrol.assert_called_once_with(
            [birim], 7, shuffle=True, pressure=True, pressure_runs=[2, 3], base_seed=42,
            report_directory=pathlib.Path(dizin))

    def test_komutlari_bas_hicbir_sey_kosmadan_komutlari_listeler(self):
        with mock.patch.object(kapi, "stability_check") as kontrol, contextlib.redirect_stdout(io.StringIO()) as ekran:
            kod = kapi.stability_from_arguments(self._args(
                proje=["Tracon.Voice.UnitTests"], komutlari_bas=True, sira_karistir=True, baski=True))

        self.assertEqual(kod, 0)
        kontrol.assert_not_called()
        satirlar = ekran.getvalue().splitlines()
        self.assertTrue(any(satir.startswith("# CPU baskısı") for satir in satirlar))
        komut = next(satir for satir in satirlar if satir.startswith("$ "))
        self.assertIn("Tracon.Voice.UnitTests", komut)
        self.assertIn("--seed 1", komut)

    def test_komut_satiri_secenekleri_ayristirilir(self):
        with mock.patch.object(kapi, "stability_from_arguments", return_value=0) as komut:
            kod = kapi.main([
                "kararlilik", "--tekrar", "4", "--proje", "A", "B", "--proje", "C", "--haric", "D",
                "--sira-karistir", "--baski", "--baski-kosumlari", "2", "3", "--tohum", "9", "--cikti", "x"])

        self.assertEqual(kod, 0)
        args = komut.call_args.args[0]
        self.assertEqual(
            (args.tekrar, args.proje, args.haric, args.sira_karistir, args.baski, args.baski_kosumlari, args.tohum, args.cikti),
            (4, ["A", "B", "C"], ["D"], True, True, [2, 3], 9, "x"))

    def test_varsayilan_tekrar_bestir_ve_komut_ust_duzey_komutlari_bas_bayragini_taniyor(self):
        with mock.patch.object(kapi, "stability_from_arguments", return_value=0) as komut:
            kapi.main(["--komutlari-bas", "kararlilik"])

        args = komut.call_args.args[0]
        self.assertEqual((args.tekrar, args.komutlari_bas), (5, True))


class KararlilikIsAkisiTestleri(unittest.TestCase):
    """`kararlilik.yml`: public aynada haftalik koşum; ci.yml'in sözleşmelerini taşır."""

    WORKFLOWS = ROOT / ".github" / "workflows"

    def setUp(self):
        self.metin = (self.WORKFLOWS / "kararlilik.yml").read_text(encoding="utf-8")
        self.yorumsuz = "\n".join(satir for satir in self.metin.splitlines() if not satir.lstrip().startswith("#"))

    def test_haftalik_ve_elle_tetiklenir_pull_request_ve_push_yok(self):
        self.assertRegex(self.yorumsuz, r"(?m)^on:\n  schedule:\n    - cron: '\d+ \d+ \* \* \d'\n  workflow_dispatch:")
        self.assertNotIn("pull_request", self.yorumsuz)
        self.assertNotRegex(self.yorumsuz, r"(?m)^  push:")

    def test_is_kanonik_depo_korumasi_zaman_siniri_ve_yalniz_ubuntu_tasir(self):
        isler = _ci_isleri(self.metin)

        self.assertEqual(list(isler), ["stability"])
        self.assertEqual(_zincirsiz_korumasiz_isler(isler), [])
        self.assertRegex(self.yorumsuz, r"(?m)^    timeout-minutes: \d+$")
        self.assertIn("runs-on: ubuntu-latest", self.yorumsuz)
        self.assertNotIn("windows", self.yorumsuz.lower())
        self.assertNotIn("macos", self.yorumsuz.lower())

    def test_utf8_modu_ve_izinler_ci_yml_ile_aynidir(self):
        self.assertRegex(self.yorumsuz, r"(?m)^  PYTHONUTF8: 1$")
        self.assertRegex(self.yorumsuz, r"(?m)^permissions:\n  contents: read$")
        self.assertRegex(self.yorumsuz, r"(?m)^concurrency:\n  group: kararlilik\n  cancel-in-progress: false$")

    def test_her_eylem_ci_yml_deki_ayni_sha_ile_sabitlenmistir(self):
        ci = (self.WORKFLOWS / "ci.yml").read_text(encoding="utf-8")
        ci_sabitleri = set(re.findall(r"uses:\s*(actions/[\w-]+@[0-9a-f]{40})", ci))
        buradakiler = re.findall(r"uses:\s*(\S+)", self.yorumsuz)

        self.assertTrue(buradakiler)
        for eylem in buradakiler:
            with self.subTest(eylem=eylem):
                self.assertRegex(eylem, r"^actions/[\w-]+@[0-9a-f]{40}$")
                self.assertIn(eylem, ci_sabitleri)

    def test_bir_kez_derler_sonra_kararlilik_kosar_ve_rapor_her_durumda_yuklenir(self):
        adimlar = [satir.strip() for satir in self.yorumsuz.splitlines() if satir.strip().startswith("- name:")]
        derleme = self.yorumsuz.index("dotnet build Tracon.slnx -c Release --no-restore")
        kararlilik = self.yorumsuz.index("scripts/kapi.py kararlilik")

        self.assertEqual(self.yorumsuz.count("dotnet build"), 1)
        self.assertLess(derleme, kararlilik)
        self.assertIn("--tekrar 5 --sira-karistir --baski", self.yorumsuz)
        self.assertEqual(adimlar[-1], "- name: Upload the report")
        self.assertRegex(self.yorumsuz, r"- name: Upload the report\n        if: always\(\)")
        self.assertIn("path: artifacts/kararlilik/**", self.yorumsuz)

    def test_haric_listesi_gercek_modullerdir_ve_konteyner_paket_ve_tarayici_modullerini_kapsar(self):
        tek_satir = re.sub(r"\s+", " ", self.yorumsuz)
        komut = re.search(r"kararlilik --tekrar 5 .*?--haric ((?:Tracon\.[\w.]+ ?)+)", tek_satir)
        haric = komut.group(1).split()
        moduller = _slnx_test_modulleri()

        self.assertEqual([ad for ad in haric if ad not in moduller], [])
        self.assertEqual(len(set(haric)), len(haric))
        self.assertEqual(
            sorted(haric),
            sorted(["Tracon.SqlServer.IntegrationTests", "Tracon.PostgreSql.IntegrationTests",
                    "Tracon.TwoNode.IntegrationTests", "Tracon.Package.Tests", "Tracon.Ui.E2ETests"]))
        # Komut gercekten cozulur: hicbir bilinmeyen ad yok.
        self.assertTrue(kapi.stability_units(exclude=haric))

    def test_dosya_adi_uzak_durum_okumasinin_aradigi_workflow_adidir_ve_dosya_public_agactadir(self):
        self.assertEqual(kapi.STABILITY_WORKFLOW, "kararlilik.yml")
        self.assertTrue((self.WORKFLOWS / kapi.STABILITY_WORKFLOW).is_file())
        agac = [
            satir.strip() for satir in (ROOT / "scripts" / "kamu-agaci.txt").read_text(encoding="utf-8").splitlines()
            if satir.strip() and not satir.startswith("#")]
        self.assertIn(".github/", agac)

    def test_zincirsiz_is_akislari_testi_bu_dosyayi_da_kapsar(self):
        dosyalar = [p.name for p in self.WORKFLOWS.glob("*.yml") if p.name not in ZINCIRSIZ_DOSYA_DISI]

        self.assertIn("kararlilik.yml", dosyalar)

    def test_kararlilik_komutu_ci_nin_testlerini_ve_kapanisi_degistirmez(self):
        ci = (self.WORKFLOWS / "ci.yml").read_text(encoding="utf-8")

        self.assertNotIn("kararlilik", ci)


if __name__ == "__main__":
    unittest.main()
