#!/usr/bin/env python3
"""Public API yüzey envanteri — her public tipin dış kanıtı (Faz 182, F-259).

`PublicAPI.Unshipped.txt` bir üye listesidir; bir tipin NEDEN public olduğunu
söylemez. Bu betik her public TİPİ (üye değil) dört sınıftan birine koyar:

  tüketici   — tüketici onu görür: site sayfası, sample, şablon çıktısı, paket
               README'si, XML `<example>`/`<code>` bloğu, kayıt giriş noktası
               (K-509) veya sevk edilen OpenAPI şeması
  seam       — genişleme noktası: public arayüz (§15 seam envanteri, bugün
               `SeamContractDocumentationTests`'in taradığı küme), abstract sınıf
               veya `Tracon.Testing.Contracts.Xunit` sözleşme paketi
  gerekçeli  — kanıtı yok ama bilinçli olarak public; gerekçesi
               `scripts/public-yuzey-gerekceleri.tsv` dosyasındadır
  kanıtsız   — hiçbiri; varsayılan kaderi `internal`'dır

Bir tip kalırsa, public imzasında geçen her tip de kalmak ZORUNDADIR — aksi
hâlde derleyici `CS0050`/`CS0051`/`CS0053` verir. Bu yüzden kanıt tek tipte
durmaz: kök tiplerden imza kapanışı (`imza:`) hesaplanır ve kapanışa giren tip
kökünün sınıfını alır. Kalan bir tipin XML dokümanı `<exception cref>` ile bir
istisna adlandırıyorsa o istisna da sözleşmenin parçasıdır (`istisna:`) —
tüketici onu yakalar, seam'i uygulayan onu fırlatır; derleyici bunu zorlamaz.
GA freeze turu (UR-003) aynı betiği koşar: `kanıtsız` sütunu o turun iş
listesidir.

Kullanım:
    python3 scripts/public-yuzey-envanteri.py                   # paket tablosu
    python3 scripts/public-yuzey-envanteri.py --liste kanıtsız  # adaylar
    python3 scripts/public-yuzey-envanteri.py --tip Tracon.RunRecord
    python3 scripts/public-yuzey-envanteri.py --json            # makine çıktısı
    python3 scripts/public-yuzey-envanteri.py --denetle         # kanıtsız > 0 → 1

Ölçüt tip ADIDIR, üye değil. Bir ad eşleşmesi yanlış pozitif üretebilir
(bir tipin adı başka bir tipin özellik adıyla aynıysa) — yanlış pozitif tipi
TUTAR, yani güvenli yöndedir. Yanlış negatifi (tüketicinin gerçekten
kullandığı tipi `internal` yapmak) paketlenmiş sample'lar yakalar:
`python3 scripts/kapi.py yayin --kuru`.
"""
from __future__ import annotations

import argparse
import dataclasses
import json
import pathlib
import re
import subprocess
import sys
from collections import deque

ROOT = pathlib.Path(__file__).resolve().parent.parent
GEREKCE_YOLU = pathlib.Path("scripts") / "public-yuzey-gerekceleri.tsv"
OPENAPI_YOLU = pathlib.Path("docs") / "openapi" / "tracon.json"

# Faz 182 Açık Soru 2 = B: sözleşme paketi bu dalgaya girmez. Sözleşme
# sınıfları tüketicinin TÜRETECEĞİ yüzeydir; tanım gereği "seam" sınıfındadır.
SOZLESME_PAKETLERI = frozenset({"Tracon.Testing.Contracts.Xunit"})

# Kayıt giriş noktası kuralı (K-509) `kayit_giris_noktasi.py`'dedir;
# `manuel-test-tazelik.py` aynı modülü kullanır, C# kopyası
# `CapabilityEntryPoints.cs`'tir. Kayıt giriş noktası tüketicinin bir yeteneği
# açtığı üyedir; kapsama kapısı her birinin site haritasında ve örnekte
# göründüğünü zaten zorlar.
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from kayit_giris_noktasi import UZANTI_METODU, kayit_uzantisi_mi  # noqa: E402

# Bir Unshipped satırı tip bildirimi değilse ya imza `(` ya da tip ` -> ` taşır
# (`PublicSurfaceBaselineTests` ile aynı süzgeç).
UYE_ISARETI = re.compile(r"\(| -> ")
# Üye satırlarının başındaki değiştiriciler (`static`, `override` ...). `~`
# (nullable-oblivious işareti) boşluksuz yapışır: `~Tracon.X.Y`, `~override Tracon.X`.
DEGISTIRICI = re.compile(r"^~?(?:[a-z]+\s+)*")
GENERIK = re.compile(r"<[^<>]*>")
TRACON_ADI = re.compile(r"Tracon(?:\.[A-Za-z_][A-Za-z0-9_]*)+")
TANIMLAYICI = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")
BILDIRIM = re.compile(
    r"(?P<degistiriciler>(?:\b(?:public|internal|protected|private|static|sealed|abstract|"
    r"partial|readonly|ref|unsafe|new|file)\s+)*)"
    r"\b(?P<tur>class|interface|struct|enum|record\s+struct|record\s+class|record)\s+"
    r"(?P<ad>[A-Za-z_][A-Za-z0-9_]*)\b")
DELEGE = re.compile(
    r"(?P<degistiriciler>(?:\b(?:public|internal|protected|private|unsafe|new)\s+)*)"
    r"\bdelegate\s+[^;(]+?\s(?P<ad>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]*>)?\s*\(")
XML_ORNEK = re.compile(r"<(example|code)>(?P<govde>.*?)</\1>", re.DOTALL)
BELGE_ISTISNASI = re.compile(r'///.*?<exception\s+cref="(?:T:)?(?:[A-Za-z_][A-Za-z0-9_]*\.)*(?P<ad>[A-Za-z_][A-Za-z0-9_]*)"')
BAGLI_KAYNAK = re.compile(r'<Compile\s+Include="\.\./(?P<dizin>[^/"]+)/\*\*/\*\.cs"')

KORPUS_UZANTILARI = frozenset({".md", ".mdx", ".cs", ".razor", ".cshtml"})

SINIFLAR = ("tüketici", "seam", "gerekçeli", "kanıtsız")

Anahtar = tuple[str, str]  # (paket, tam ad) — aynı tam ad birden çok pakette public olabilir


# --------------------------------------------------------------------------
# Veri modeli
# --------------------------------------------------------------------------

@dataclasses.dataclass
class Tip:
    paket: str
    ad: str                      # generic parametre listesi atılmış tam ad
    uyeler: list[str] = dataclasses.field(default_factory=list)
    tur: str = "?"               # interface · class · static · abstract · record · struct · enum · delegate
    kaynak: str | None = None    # bildirimin repo-göreli yolu
    baslik_adlari: set[str] = dataclasses.field(default_factory=set)  # taban/kısıt tam adları
    belge_istisnalari: set[str] = dataclasses.field(default_factory=set)  # `<exception cref>` tam adları
    sinif: str = "kanıtsız"
    gerekce: str = ""            # ilk kanıt: `site:<yol>`, `imza:<tip>`, `istisna:<tip>`, `seam:arayuz` ...

    @property
    def anahtar(self) -> Anahtar:
        return (self.paket, self.ad)

    @property
    def basit(self) -> str:
        return self.ad.rsplit(".", 1)[-1]


@dataclasses.dataclass
class Korpus:
    """Tüketiciye açık metin: dosya → o dosyadaki tanımlayıcılar."""
    dosyalar: list[tuple[str, str, frozenset[str]]]  # (etiket, yol, tanımlayıcılar)
    metinler: dict[str, str]                        # yol → metin (öznitelik araması için)

    @classmethod
    def kur(cls, girdiler: list[tuple[str, str, str]]) -> "Korpus":
        return cls(
            [(etiket, yol, frozenset(TANIMLAYICI.findall(metin))) for etiket, yol, metin in girdiler],
            {yol: metin for _, yol, metin in girdiler},
        )

    def bul(self, ad: str) -> str | None:
        for etiket, yol, adlar in self.dosyalar:
            if ad in adlar:
                return f"{etiket}:{yol}"
        return None

    def oznitelik_bul(self, kisa_ad: str) -> str | None:
        """`[Kisa]`, `[Kisa(...)]`, `[A, Kisa]` — `Attribute` son eki olmadan kullanım."""
        desen = re.compile(rf"[\[,]\s*(?:[A-Za-z_][A-Za-z0-9_.]*\.)?{re.escape(kisa_ad)}\s*[\](,]")
        for etiket, yol, adlar in self.dosyalar:
            if kisa_ad in adlar and desen.search(self.metinler[yol]):
                return f"{etiket}:{yol}"
        return None


# --------------------------------------------------------------------------
# Ayrıştırma
# --------------------------------------------------------------------------

def _imzadan_once(govde: str) -> str:
    kes = len(govde)
    for isaret in ("(", " -> ", " = "):
        konum = govde.find(isaret)
        if konum >= 0:
            kes = min(kes, konum)
    return govde[:kes]


def _yol(satir: str) -> str:
    """Bir satırın sahip-yolu: değiştiricisiz, generic parametresiz, imzadan önceki kısım."""
    yol = _imzadan_once(DEGISTIRICI.sub("", satir))
    while True:
        sade = GENERIK.sub("", yol)
        if sade == yol:
            return yol
        yol = sade


def _en_uzun_onek(yol: str, adlar) -> str | None:
    parcalar = yol.split(".")
    for uzunluk in range(len(parcalar), 0, -1):
        aday = ".".join(parcalar[:uzunluk])
        if aday in adlar:
            return aday
    return None


def unshipped_ayristir(paket: str, metin: str) -> dict[str, Tip]:
    """Bir paketin Unshipped metninden tipler ve üyeleri (anahtar: tam ad)."""
    tipler: dict[str, Tip] = {}
    uyeler: list[str] = []
    for satir in metin.splitlines():
        satir = satir.strip()
        if not satir or satir.startswith("#"):
            continue
        if UYE_ISARETI.search(satir):
            uyeler.append(satir)
            continue
        ad = _yol(satir)
        tipler[ad] = Tip(paket, ad)
    for satir in uyeler:
        sahip = _en_uzun_onek(_yol(satir), tipler)
        if sahip is not None:
            tipler[sahip].uyeler.append(satir)
    return tipler


def ad_indeksi(tipler: dict[Anahtar, Tip]) -> dict[str, list[Anahtar]]:
    indeks: dict[str, list[Anahtar]] = {}
    for anahtar in sorted(tipler):
        indeks.setdefault(anahtar[1], []).append(anahtar)
    return indeks


def istisna_referanslari(tip: Tip, indeks: dict[str, list[Anahtar]]) -> set[Anahtar]:
    """Tipin XML dokümanının `<exception cref>` ile adlandırdığı public istisnalar."""
    return {anahtar for ad in tip.belge_istisnalari - {tip.ad} for anahtar in indeks.get(ad, [])}


def imza_referanslari(tip: Tip, indeks: dict[str, list[Anahtar]]) -> set[Anahtar]:
    """Tipin public üyelerinin imzasında, taban listesinde ve kapsayıcısında geçen tipler."""
    adlar: set[str] = set()
    for satir in tip.uyeler:
        govde = DEGISTIRICI.sub("", satir)
        # Sahip yolu tipin kendisini adlandırır; referans imzadan başlar.
        for ad in TRACON_ADI.findall(govde[len(_imzadan_once(govde)):]):
            hedef = _en_uzun_onek(ad, indeks)
            if hedef is not None:
                adlar.add(hedef)
    adlar |= tip.baslik_adlari
    # İç içe public tip, kapsayıcısı public değilse dışarıdan görünmez.
    kapsayici = tip.ad.rsplit(".", 1)[0]
    if kapsayici in indeks:
        adlar.add(kapsayici)
    adlar.discard(tip.ad)
    return {anahtar for ad in adlar for anahtar in indeks[ad]}


def bildirimleri_tara(tipler: dict[Anahtar, Tip], kaynaklar: dict[str, list[tuple[str, str]]]) -> None:
    """Tür, kaynak dosya ve bildirim başlığındaki (taban, kısıt) tip adlarını doldurur.

    `kaynaklar`: paket → [(repo-göreli yol, metin)]; bağlı kaynak ağaçları
    (`*.Shared`) derlendikleri her paketin listesindedir. Başlık, adın bittiği
    yerden derinlik sıfırdaki ilk `{` veya `;` karakterine kadardır.

    `<exception cref>` dosya düzeyinde toplanır: dosyada bildirilen her public
    tip dosyanın bütün belgelenmiş istisnalarını alır. Üye düzeyinde eşlemek
    bildirim sınırlarını ayrıştırmayı gerektirirdi; dosya çoğunlukla tek tip
    ve yardımcı kayıtlarını taşıdığı için yaklaşım güvenli yönde (tutan) hata yapar.
    """
    basit_adlar: dict[str, set[str]] = {}
    for _, ad in tipler:
        basit_adlar.setdefault(ad.rsplit(".", 1)[-1], set()).add(ad)

    for paket, dosyalar in kaynaklar.items():
        paket_tipleri = {tip.basit: tip for tip in tipler.values() if tip.paket == paket}
        if not paket_tipleri:
            continue
        for yol, metin in dosyalar:
            istisnalar = {hedef for eslesme in BELGE_ISTISNASI.finditer(metin)
                          for hedef in basit_adlar.get(eslesme.group("ad"), set())}
            for eslesme in BILDIRIM.finditer(metin):
                tip = paket_tipleri.get(eslesme.group("ad"))
                degistiriciler = eslesme.group("degistiriciler")
                if tip is None or "public" not in degistiriciler:
                    continue
                tur = re.sub(r"\s+", " ", eslesme.group("tur"))
                if tur == "class" and "static" in degistiriciler:
                    tur = "static"
                elif tur == "class" and "abstract" in degistiriciler:
                    tur = "abstract"
                elif tur.startswith("record"):
                    tur = "record"
                if tip.tur in ("?", "class", "record"):
                    tip.tur = tur
                tip.kaynak = tip.kaynak or yol
                tip.belge_istisnalari |= istisnalar
                for kimlik in TANIMLAYICI.findall(_baslik(metin, eslesme.end())):
                    tip.baslik_adlari |= basit_adlar.get(kimlik, set())
            for eslesme in DELEGE.finditer(metin):
                tip = paket_tipleri.get(eslesme.group("ad"))
                if tip is not None and "public" in eslesme.group("degistiriciler"):
                    tip.tur = "delegate"
                    tip.kaynak = tip.kaynak or yol


def _baslik(metin: str, baslangic: int) -> str:
    derinlik = 0
    son = min(len(metin), baslangic + 4000)
    for konum in range(baslangic, son):
        karakter = metin[konum]
        if karakter in "(<[":
            derinlik += 1
        elif karakter in ")>]":
            derinlik -= 1
        elif karakter in "{;" and derinlik <= 0:
            return metin[baslangic:konum]
    return metin[baslangic:son]


def xml_ornekleri(metin: str) -> str:
    """`///` yorumlarındaki `<example>` ve `<code>` blokları — sevk edilen kullanım örneği."""
    yorum = "\n".join(
        satir.strip()[3:] for satir in metin.splitlines() if satir.strip().startswith("///"))
    return "\n".join(eslesme.group("govde") for eslesme in XML_ORNEK.finditer(yorum))


def gerekceleri_oku(metin: str) -> dict[str, str]:
    """`<tam ad>\\t<gerekçe>` satırları. Gerekçesiz satır hatadır."""
    gerekceler: dict[str, str] = {}
    for numara, satir in enumerate(metin.splitlines(), start=1):
        if not satir.strip() or satir.startswith("#"):
            continue
        ad, _, gerekce = satir.partition("\t")
        if not gerekce.strip():
            raise ValueError(f"{GEREKCE_YOLU}:{numara}: '{ad.strip()}' gerekçesiz")
        gerekceler[ad.strip()] = gerekce.strip()
    return gerekceler


# --------------------------------------------------------------------------
# Sınıflandırma
# --------------------------------------------------------------------------

def siniflandir(
    tipler: dict[Anahtar, Tip],
    korpus: Korpus,
    openapi_semalari: set[str],
    gerekceler: dict[str, str],
) -> None:
    """Kökleri bulur, imza kapanışını yayar. Öncelik: tüketici > seam > gerekçeli."""
    kokler: dict[str, list[tuple[Anahtar, str]]] = {sinif: [] for sinif in SINIFLAR[:3]}
    for tip in tipler.values():
        tip.sinif, tip.gerekce = "kanıtsız", ""
        kanit = _tuketici_kaniti(tip, korpus, openapi_semalari)
        if kanit:
            kokler["tüketici"].append((tip.anahtar, kanit))
            continue
        kanit = _seam_kaniti(tip)
        if kanit:
            kokler["seam"].append((tip.anahtar, kanit))
            continue
        if tip.ad in gerekceler:
            kokler["gerekçeli"].append((tip.anahtar, f"gerekçe:{gerekceler[tip.ad]}"))

    indeks = ad_indeksi(tipler)
    atanmis: set[Anahtar] = set()
    for sinif in SINIFLAR[:3]:
        kuyruk: deque[Anahtar] = deque()
        for anahtar, kanit in kokler[sinif]:
            if anahtar not in atanmis:
                atanmis.add(anahtar)
                tipler[anahtar].sinif, tipler[anahtar].gerekce = sinif, kanit
                kuyruk.append(anahtar)
        while kuyruk:
            kaynak = tipler[kuyruk.popleft()]
            for tur, hedefler in (("imza", imza_referanslari(kaynak, indeks)),
                                  ("istisna", istisna_referanslari(kaynak, indeks))):
                for hedef in sorted(hedefler):
                    if hedef not in atanmis:
                        atanmis.add(hedef)
                        tipler[hedef].sinif, tipler[hedef].gerekce = sinif, f"{tur}:{kaynak.ad}"
                        kuyruk.append(hedef)


def _tuketici_kaniti(tip: Tip, korpus: Korpus, openapi_semalari: set[str]) -> str | None:
    if tip.paket in SOZLESME_PAKETLERI:
        return None
    kanit = korpus.bul(tip.basit)
    if kanit:
        return kanit
    if tip.basit.endswith("Attribute") and len(tip.basit) > len("Attribute"):
        kanit = korpus.oznitelik_bul(tip.basit[: -len("Attribute")])
        if kanit:
            return kanit
    for satir in tip.uyeler:
        uzanti = UZANTI_METODU.match(satir)
        if uzanti is None:
            continue
        if kayit_uzantisi_mi(uzanti.group("ad"), uzanti.group("alici")):
            return f"giris-noktasi:{uzanti.group('ad')}"
        # Uzantı metodunu çağıran tüketici sınıfın ADINI yazmaz, metodun adını yazar.
        kanit = korpus.bul(uzanti.group("ad"))
        if kanit:
            return f"uzanti:{uzanti.group('ad')}@{kanit}"
    if tip.basit in openapi_semalari:
        # Sevk edilen OpenAPI belgesinin açıklamaları yalnız PUBLIC tipin XML
        # dokümanından gelir (XmlCommentGenerator; ölçüldü 2026-09-22: önbellekte
        # 604 tip, sıfırı internal). Şema tipini internal yapmak HTTP sözleşmesinin
        # dokümanını siler — HTTP yüzeyi bu envanterin kapsamı dışındadır.
        return f"http-sema:{OPENAPI_YOLU.as_posix()}"
    return None


def _seam_kaniti(tip: Tip) -> str | None:
    if tip.paket in SOZLESME_PAKETLERI:
        return "seam:sozlesme-paketi"
    if tip.tur == "interface":
        return "seam:arayuz"
    if tip.tur == "abstract":
        return "seam:abstract-sinif"
    return None


def bayat_gerekceler(tipler: dict[Anahtar, Tip], gerekceler: dict[str, str]) -> list[str]:
    """Artık public olmayan ya da kendi kanıtı olan tipe yazılmış gerekçe."""
    siniflar: dict[str, list[Tip]] = {}
    for tip in tipler.values():
        siniflar.setdefault(tip.ad, []).append(tip)
    sorunlar: list[str] = []
    for ad in sorted(gerekceler):
        eslesen = siniflar.get(ad)
        if not eslesen:
            sorunlar.append(f"{ad}: public değil (ya da yeniden adlandırıldı) — satırı sil")
        elif all(tip.sinif != "gerekçeli" for tip in eslesen):
            tip = eslesen[0]
            sorunlar.append(f"{ad}: artık '{tip.sinif}' ({tip.gerekce}) — gerekçe gereksiz, satırı sil")
    return sorunlar


# --------------------------------------------------------------------------
# Repo okuması
# --------------------------------------------------------------------------

def _git_dosyalari(root: pathlib.Path, *yollar: str) -> list[str]:
    cikti = subprocess.run(
        ["git", "ls-files", "--", *yollar], cwd=root, check=True, capture_output=True, text=True)
    return [satir for satir in cikti.stdout.splitlines() if satir]


def _oku(root: pathlib.Path, yol: str) -> str:
    return (root / yol).read_text(encoding="utf-8", errors="replace")


def korpus_topla(root: pathlib.Path) -> Korpus:
    girdiler: list[tuple[str, str, str]] = []

    def ekle(etiket: str, yollar: list[str]) -> None:
        for yol in yollar:
            if pathlib.PurePosixPath(yol).suffix in KORPUS_UZANTILARI and (root / yol).is_file():
                girdiler.append((etiket, yol, _oku(root, yol)))

    # `api/` ve `http-api/` üretilir ve izlenmez: her public tipi listeler, kanıt değildir.
    ekle("site", _git_dosyalari(root, "docs-site/src/content/docs"))
    ekle("sample", _git_dosyalari(root, "samples"))
    ekle("sablon", _git_dosyalari(root, "src/Tracon.Templates/content"))
    src = _git_dosyalari(root, "src")
    ekle("readme", ["README.md"] + [
        yol for yol in src if re.fullmatch(r"src/[^/]+/README\.md", yol) and ".Shared/" not in yol])
    for yol in src:
        if yol.endswith(".cs") and ".Shared/" not in yol:
            ornek = xml_ornekleri(_oku(root, yol))
            if ornek:
                girdiler.append(("ornek", yol, ornek))
    return Korpus.kur(girdiler)


def kaynaklari_topla(root: pathlib.Path, paket: str) -> list[tuple[str, str]]:
    """Paketin kendi `.cs` dosyaları ve csproj'un bağladığı `*.Shared` ağaçları."""
    dizinler = [f"src/{paket}"]
    csproj = root / "src" / paket / f"{paket}.csproj"
    if csproj.is_file():
        dizinler += [f"src/{eslesme.group('dizin')}"
                     for eslesme in BAGLI_KAYNAK.finditer(csproj.read_text(encoding="utf-8"))]
    return [(yol, _oku(root, yol)) for yol in _git_dosyalari(root, *dizinler) if yol.endswith(".cs")]


def envanter_olustur(root: pathlib.Path = ROOT) -> tuple[dict[Anahtar, Tip], dict[str, str]]:
    tipler: dict[Anahtar, Tip] = {}
    kaynaklar: dict[str, list[tuple[str, str]]] = {}
    for dosya in sorted((root / "src").glob("*/PublicAPI.Unshipped.txt")):
        paket = dosya.parent.name
        for tip in unshipped_ayristir(paket, dosya.read_text(encoding="utf-8")).values():
            tipler[tip.anahtar] = tip
        kaynaklar[paket] = kaynaklari_topla(root, paket)
    bildirimleri_tara(tipler, kaynaklar)

    semalar: set[str] = set()
    if (root / OPENAPI_YOLU).is_file():
        belge = json.loads((root / OPENAPI_YOLU).read_text(encoding="utf-8"))
        semalar = set(belge.get("components", {}).get("schemas", {}))

    gerekceler = gerekceleri_oku(_oku(root, GEREKCE_YOLU.as_posix())) if (root / GEREKCE_YOLU).is_file() else {}

    siniflandir(tipler, korpus_topla(root), semalar, gerekceler)
    return tipler, gerekceler


# --------------------------------------------------------------------------
# Rapor
# --------------------------------------------------------------------------

def paket_tablosu(tipler: dict[Anahtar, Tip]) -> list[tuple[str, dict[str, int]]]:
    sayim: dict[str, dict[str, int]] = {}
    for tip in tipler.values():
        satir = sayim.setdefault(tip.paket, {sinif: 0 for sinif in SINIFLAR})
        satir[tip.sinif] += 1
    return sorted(sayim.items())


def tablo_yaz(tipler: dict[Anahtar, Tip]) -> str:
    satirlar = [
        "| Paket | Toplam | Tüketici | Seam | Gerekçeli | Kanıtsız |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    toplam = {sinif: 0 for sinif in SINIFLAR}
    for paket, sayim in paket_tablosu(tipler):
        for sinif in SINIFLAR:
            toplam[sinif] += sayim[sinif]
        satirlar.append(
            f"| `{paket}` | {sum(sayim.values())} | "
            + " | ".join(str(sayim[sinif]) for sinif in SINIFLAR) + " |")
    satirlar.append(
        f"| **Toplam** | **{sum(toplam.values())}** | "
        + " | ".join(f"**{toplam[sinif]}**" for sinif in SINIFLAR) + " |")
    return "\n".join(satirlar)


def kanit_zinciri(tipler: dict[Anahtar, Tip], ad: str) -> list[str]:
    """Bir tipin neden kaldığını köke kadar izler (`imza:` ve `istisna:` adımları)."""
    indeks = ad_indeksi(tipler)
    satirlar: list[str] = []
    for anahtar in indeks.get(ad, []):
        adim, gorulen = tipler[anahtar], set()
        while adim.anahtar not in gorulen:
            gorulen.add(adim.anahtar)
            satirlar.append(f"{adim.ad} [{adim.paket}, {adim.tur}] → {adim.sinif}: {adim.gerekce}")
            tur, _, ust = adim.gerekce.partition(":")
            if tur not in ("imza", "istisna"):
                break
            adim = tipler[indeks[ust][0]]
        satirlar.append("")
    return satirlar


def main(argv: list[str] | None = None) -> int:
    ayristirici = argparse.ArgumentParser(description=(__doc__ or "").split("\n", 1)[0])
    ayristirici.add_argument("--liste", choices=SINIFLAR + ("hepsi",), help="bir sınıfın tiplerini listele")
    ayristirici.add_argument("--paket", help="yalnız bu paket")
    ayristirici.add_argument("--tip", help="bir tipin kanıt zincirini göster (tam ad)")
    ayristirici.add_argument("--json", action="store_true", help="tüm envanteri JSON olarak yaz")
    ayristirici.add_argument("--denetle", action="store_true",
                             help="kanıtsız tip veya bayat gerekçe varsa çıkış kodu 1")
    args = ayristirici.parse_args(argv)

    tipler, gerekceler = envanter_olustur()
    sorunlar = bayat_gerekceler(tipler, gerekceler)
    if args.paket:
        tipler = {anahtar: tip for anahtar, tip in tipler.items() if tip.paket == args.paket}
    sirali = sorted(tipler.values(), key=lambda tip: tip.anahtar)

    if args.json:
        print(json.dumps(
            [dataclasses.asdict(tip) | {"baslik_adlari": sorted(tip.baslik_adlari),
                                        "belge_istisnalari": sorted(tip.belge_istisnalari)}
             for tip in sirali],
            ensure_ascii=False, indent=1))
        return 0

    if args.tip:
        zincir = kanit_zinciri(tipler, args.tip)
        if not zincir:
            print(f"{args.tip}: envanterde yok (public değil ya da ad yanlış)", file=sys.stderr)
            return 2
        print("\n".join(zincir).rstrip())
        return 0

    if args.liste:
        for tip in sirali:
            if args.liste == "hepsi" or tip.sinif == args.liste:
                print("\t".join((tip.paket, tip.ad, tip.tur, tip.sinif, tip.gerekce, tip.kaynak or "-")))
        return 0

    print(tablo_yaz(tipler))
    for sorun in sorunlar:
        print(f"bayat gerekçe: {sorun}", file=sys.stderr)
    kanitsiz = sum(1 for tip in sirali if tip.sinif == "kanıtsız")
    if args.denetle and (kanitsiz or sorunlar):
        print(f"denetim: {kanitsiz} kanıtsız tip, {len(sorunlar)} bayat gerekçe", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
