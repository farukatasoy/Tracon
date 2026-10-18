#!/usr/bin/env python3
"""MAF paket ailesinin NuGet'teki canlı envanterini Tracon'un taban çizgisiyle karşılaştırır.

Sürüm yükseltmesinde iki tam yüzey dump'ı (`maf-api-kesfi` § Sürüm yükseltirken) yalnız
Tracon'un REFERANS verdiği paketleri diff'ler. MAF yeni bir yeteneği çoğu zaman yeni bir
pakette, önce `alpha` veya `preview` olarak yayınlar; o paket ne referanslıdır ne dump'ta
görünür. Bu betik o boşluğu kapatır: ailenin NuGet'teki tüm paketlerini
`scripts/maf-paket-envanteri.tsv` ile karşılaştırır ve şunları bildirir:

    YENİ          Tabanda olmayan paket (hiç değerlendirilmedi)
    OLGUNLAŞTI    Olgunluğu yükseldi (alpha < preview < rc < ga)
    GERİLEDİ      Olgunluğu düştü
    GÖRÜNMÜYOR    Tabandaki paket aramada yok (kaldırıldı, unlist edildi ya da indeks gecikti)
    TUTARSIZ      `Directory.Packages.props` ile tabandaki `durum` birbirini tutmuyor

Ağdan etkilenir, bu yüzden CI'da koşmaz: her MAF yükseltmesinde elle koşulur.
Tabanın kendi biçimi ve `Directory.Packages.props` ile tutarlılığı ise ağ olmadan
`maf_paket_envanteri_test.py` içinde CI'da denetlenir.

Kullanım:
    python3 scripts/maf-paket-envanteri.py                 # canlı arama, yalnız farkları basar
    python3 scripts/maf-paket-envanteri.py --liste         # ailenin tamamını da basar
    python3 scripts/maf-paket-envanteri.py --guncelle      # tabanı canlı duruma getirir
    python3 scripts/maf-paket-envanteri.py --dosya x.json  # canlı arama yerine yerel JSON (curl çıktısı)

`--guncelle` olgunluğu günceller ve yeni paketi `degerlendirilmedi` olarak ekler. Yeni
satırın `durum` ve `not` alanını kararı veren kişi (ya da `aday-kesfi` turu) ELLE yazar.

Çıkış kodu 0: taban canlı durumla aynı. 1: fark var (liste basılır). 2: arama indirilemedi.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys
import urllib.error
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass

ROOT = pathlib.Path(__file__).resolve().parent.parent
BASELINE = ROOT / "scripts" / "maf-paket-envanteri.tsv"
PROPS = ROOT / "Directory.Packages.props"
SEARCH = "https://azuresearch-usnc.nuget.org/query"
PREFIXES = ("Microsoft.Agents.AI",)
PAGE_SIZE = 1000  # NuGet arama API'sinin üst sınırı

# Sıra olgunluğun artış yönüdür.
MATURITY_ORDER = {"alpha": 0, "preview": 1, "rc": 2, "ga": 3}

# `referans` durumları: paket Directory.Packages.props'ta vardır.
STATUS_REFERENCED = ("kullaniliyor", "referans-yalniz")
STATUSES = STATUS_REFERENCED + ("karar", "degerlendirilmedi")

HEADER = """\
# MAF paket ailesi taban çizgisi. `scripts/maf-paket-envanteri.py` okur ve yazar.
# Sütunlar (TAB): paket kimliği · olgunluk (ga|rc|preview|alpha) · durum · not
# durum:
#   kullaniliyor       Directory.Packages.props'ta referanslı VE src/ bir tipini çağırır
#   referans-yalniz    Directory.Packages.props'ta referanslı, src/ hiçbir tipini çağırmaz
#   karar              bilerek dışarıda; `not` karar veya aday numarasını ve gerekçeyi taşır
#   degerlendirilmedi  karar, aday veya keşif defterinde iz yok
# Güncelleme: python3 scripts/maf-paket-envanteri.py --guncelle, sonra yeni satırı ELLE doldur.
"""


@dataclass
class Row:
    """A baseline line: one package and what Tracon decided about it."""

    package: str
    maturity: str
    status: str
    note: str


@dataclass
class Finding:
    """A difference between the live family and the baseline."""

    kind: str
    package: str
    detail: str


def maturity_of(version: str) -> str:
    """`ga` for a release, otherwise the pre-release label (`alpha`, `preview`, `rc`).

    An unknown label counts as `preview`: a new package must never vanish from the report
    because its label is unfamiliar.
    """
    if "-" not in version:
        return "ga"
    # `rc5` ve `rc1` de `rc`'dir: etiket sürüm numarasına bitişik yazılabilir.
    label = re.match(r"[a-z]+", version.split("-", 1)[1].lower())
    return label.group() if label and label.group() in MATURITY_ORDER else "preview"


def read_baseline(path: pathlib.Path = BASELINE) -> list[Row]:
    """Parses the baseline; raises `ValueError` on any line that breaks the format."""
    rows: list[Row] = []
    seen: set[str] = set()

    for number, line in enumerate(path.read_text(encoding="utf-8").splitlines(), start=1):
        if not line.strip() or line.startswith("#"):
            continue

        cells = line.split("\t")
        if len(cells) != 4:
            raise ValueError(f"{path.name}:{number}: 4 sütun bekleniyordu, {len(cells)} bulundu")

        package, maturity, status, note = (cell.strip() for cell in cells)
        if maturity not in MATURITY_ORDER:
            raise ValueError(f"{path.name}:{number}: olgunluk '{maturity}' geçersiz ({'|'.join(MATURITY_ORDER)})")
        if status not in STATUSES:
            raise ValueError(f"{path.name}:{number}: durum '{status}' geçersiz ({'|'.join(STATUSES)})")
        if status == "karar" and not note:
            raise ValueError(f"{path.name}:{number}: 'karar' satırı gerekçesiz (not boş)")
        if package.lower() in seen:
            raise ValueError(f"{path.name}:{number}: '{package}' iki kez yazılmış")

        seen.add(package.lower())
        rows.append(Row(package, maturity, status, note))

    return rows


def write_baseline(rows: list[Row], path: pathlib.Path = BASELINE) -> None:
    """Writes the baseline sorted by package id."""
    lines = [f"{row.package}\t{row.maturity}\t{row.status}\t{row.note}" for row in
             sorted(rows, key=lambda row: row.package.lower())]
    path.write_text(HEADER + "\n".join(lines) + "\n", encoding="utf-8", newline="\n")


def referenced_packages(path: pathlib.Path = PROPS, prefixes: tuple[str, ...] = PREFIXES) -> set[str]:
    """Package ids of the family that `Directory.Packages.props` pins."""
    tree = ElementTree.parse(path)
    found = set()
    for element in tree.getroot().iter("PackageVersion"):
        include = element.get("Include", "")
        if include.lower().startswith(tuple(prefix.lower() for prefix in prefixes)):
            found.add(include)
    return found


def live_family(document: dict, prefixes: tuple[str, ...] = PREFIXES) -> dict[str, str]:
    """Maps package id to its newest version, from one NuGet search response."""
    lowered = tuple(prefix.lower() for prefix in prefixes)
    return {entry["id"]: entry["version"] for entry in document.get("data", [])
            if entry["id"].lower().startswith(lowered)}


def fetch_live(prefixes: tuple[str, ...] = PREFIXES, open_url=urllib.request.urlopen) -> dict:
    """Runs the NuGet search for every prefix, page by page, and merges the results."""
    merged: dict = {"data": [], "totalHits": 0}
    for prefix in prefixes:
        skip = 0
        while True:
            query = urllib.parse.urlencode(
                {"q": prefix, "prerelease": "true", "semVerLevel": "2.0.0", "take": PAGE_SIZE, "skip": skip})
            with open_url(f"{SEARCH}?{query}", timeout=60) as response:  # noqa: S310 - sabit https adres
                page = json.loads(response.read())
            merged["data"].extend(page.get("data", []))
            skip += PAGE_SIZE
            if skip >= page.get("totalHits", 0) or not page.get("data"):
                merged["totalHits"] += page.get("totalHits", 0)
                break
    return merged


def compare(live: dict[str, str], rows: list[Row], referenced: set[str]) -> list[Finding]:
    """Lists every difference between the live family, the baseline and the props file."""
    findings: list[Finding] = []
    by_id = {row.package.lower(): row for row in rows}
    live_ids = {package.lower(): package for package in live}
    referenced_ids = {package.lower() for package in referenced}

    for package, version in sorted(live.items()):
        row = by_id.get(package.lower())
        current = maturity_of(version)

        if row is None:
            findings.append(Finding("YENİ", package, f"{version} ({current}); tabanda yok"))
        elif MATURITY_ORDER[current] > MATURITY_ORDER[row.maturity]:
            findings.append(Finding("OLGUNLAŞTI", package, f"{row.maturity} → {current} ({version}); durum: {row.status}"))
        elif MATURITY_ORDER[current] < MATURITY_ORDER[row.maturity]:
            findings.append(Finding("GERİLEDİ", package, f"{row.maturity} → {current} ({version}); durum: {row.status}"))

    for row in rows:
        if row.package.lower() not in live_ids:
            findings.append(Finding(
                "GÖRÜNMÜYOR", row.package,
                "aramada yok; indeks gecikmesi olabilir, https://api.nuget.org/v3-flatcontainer/"
                f"{row.package.lower()}/index.json ile doğrula"))

    for package in sorted(referenced, key=str.lower):
        row = by_id.get(package.lower())
        if row is None:
            findings.append(Finding("TUTARSIZ", package, "Directory.Packages.props'ta referanslı ama tabanda yok"))
        elif row.status not in STATUS_REFERENCED:
            findings.append(Finding("TUTARSIZ", package, f"Directory.Packages.props'ta referanslı ama taban durumu '{row.status}'"))

    for row in rows:
        if row.status in STATUS_REFERENCED and row.package.lower() not in referenced_ids:
            findings.append(Finding("TUTARSIZ", row.package, f"taban durumu '{row.status}' ama Directory.Packages.props'ta yok"))

    return findings


def refresh(live: dict[str, str], rows: list[Row]) -> list[Row]:
    """Brings maturity up to date and appends new packages as `degerlendirilmedi`. Never removes a row."""
    by_id = {row.package.lower(): row for row in rows}
    refreshed = [Row(row.package, row.maturity, row.status, row.note) for row in rows]

    for row in refreshed:
        version = next((v for package, v in live.items() if package.lower() == row.package.lower()), None)
        if version is not None:
            row.maturity = maturity_of(version)

    for package, version in sorted(live.items()):
        if package.lower() not in by_id:
            refreshed.append(Row(package, maturity_of(version), "degerlendirilmedi", ""))

    return refreshed


def family_line(live: dict[str, str]) -> str:
    """The version line of the family: the core package's `major.minor`."""
    core = live.get("Microsoft.Agents.AI", "")
    return ".".join(core.split("-", 1)[0].split(".")[:2])


def print_list(live: dict[str, str], rows: list[Row]) -> None:
    """Prints every live package with its baseline status."""
    by_id = {row.package.lower(): row for row in rows}
    line = family_line(live)
    print(f"{'paket':52} {'sürüm':26} {'olgunluk':9} {'hat':8} durum")
    for package, version in sorted(live.items(), key=lambda item: item[0].lower()):
        row = by_id.get(package.lower())
        current_line = ".".join(version.split("-", 1)[0].split(".")[:2])
        old = "" if not line or current_line == line else f"eski ({current_line})"
        print(f"{package:52} {version:26} {maturity_of(version):9} {old:8} {row.status if row else '—'}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--dosya", help="Canlı arama yerine yerel bir NuGet arama JSON'u")
    parser.add_argument("--liste", action="store_true", help="Ailenin tamamını da bas")
    parser.add_argument("--guncelle", action="store_true", help="Tabanı canlı duruma getir")
    arguments = parser.parse_args()

    if arguments.dosya:
        document = json.loads(pathlib.Path(arguments.dosya).read_text(encoding="utf-8"))
    else:
        try:
            document = fetch_live()
        except urllib.error.URLError as error:
            # Sertifika doğrulaması ASLA kapatılmaz. python.org kurulumunun CA paketi yoksa
            # (macOS) arama curl ile indirilir ve --dosya ile verilir.
            print(f"❌ NuGet araması indirilemedi: {error.reason}", file=sys.stderr)
            print(f"   curl -s '{SEARCH}?q={PREFIXES[0]}&prerelease=true&semVerLevel=2.0.0&take=1000' "
                  "-o arama.json && python3 scripts/maf-paket-envanteri.py --dosya arama.json", file=sys.stderr)
            return 2

    live = live_family(document)
    total = document.get("totalHits", len(live))
    if total > len(document.get("data", [])):
        print(f"⚠️  Arama {total} sonuç bildirdi ama {len(document.get('data', []))} geldi; listede olmayan bir MAF "
              "paketi olabilir (--dosya kullanıyorsan take=1000 ile indir).", file=sys.stderr)

    rows = read_baseline(BASELINE)
    referenced = referenced_packages(PROPS)

    if arguments.guncelle:
        # Güncellemeden ÖNCEKİ farklar basılır: güncelleme `YENİ` ve `OLGUNLAŞTI` sinyalini
        # siler, ve o sinyal bu betiğin varlık sebebidir.
        applied = [finding for finding in compare(live, rows, referenced) if finding.kind in ("YENİ", "OLGUNLAŞTI", "GERİLEDİ")]
        write_baseline(refresh(live, rows), BASELINE)
        rows = read_baseline(BASELINE)
        print(f"Taban güncellendi: {len(rows)} paket, {len(applied)} değişiklik.")
        for finding in applied:
            print(f"  {finding.kind:11} {finding.package} — {finding.detail}")
        if any(finding.kind == "YENİ" for finding in applied):
            print("  → YENİ satırların durum ve not alanını ELLE yaz (şu an 'degerlendirilmedi').")

    if arguments.liste:
        print_list(live, rows)
        print()

    findings = compare(live, rows, referenced)
    counts = {name: sum(1 for version in live.values() if maturity_of(version) == name) for name in MATURITY_ORDER}

    if findings:
        print(f"❌ MAF paket ailesi tabandan ayrıştı: {len(findings)} fark")
        for finding in findings:
            print(f"  {finding.kind:11} {finding.package} — {finding.detail}")
        return 1

    summary = " · ".join(f"{name} {count}" for name, count in counts.items())
    print(f"✅ {len(live)} paket tabanla aynı (hat {family_line(live)}; {summary})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
