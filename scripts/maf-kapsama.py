#!/usr/bin/env python3
"""MAF kapsama matrisinin (`docs/MAF-KAPSAMA.md`) ölçülen sütunlarını üretir ve denetler (Faz 224).

Matris iki tür sütun taşır. Yargı sütunları (Durum, kapsama, not) elle yazılır ve elle
kalır. Ölçülen olgular bu betikten gelir:

    temas    §5'in her satırındaki MAF jetonlarından hangileri `src/`, `tests/` ve
             `samples/` kaynağında geçiyor. Sonuç `scripts/maf-kapsama-temas.tsv`'dir;
             `kapi.py uret` onu yeniden yazar, `dokuman-bakim.py --denetle` tazeliğini
             denetler. Alan sayı değil KÜMEDİR: dosya yalnız bir jeton bir alana girip
             çıkınca değişir, ve o değişiklik diff'te satırın Durum'unu yeniden yargılatır.
    fark     İki `dump-api.sh` çıktısını TİP KAPSAMLI karşılaştırır. Düz `diff` bir üyenin
             bir tipten ötekine taşınmasını "değişmedi" sayar; burada iki tipte de görünür.
    denetle  Tazelik + `✗` cırcırı: §5'te `Takip` sütunu `✗` olan satır sayısı tabandan
             büyükse ya da küçükse (taban bayat) bulgu. `✗⁺` sayılmaz: yeni MAF tipi onunla
             girer (MAF-KAPSAMA §10 P-03).

Durum sütunu bilerek ölçülmez: genel jetonlar (`AIAgent`, `ChatOptions`) ve ad çakışmaları
(`AsIChatClient`) yüzünden doğrudan bir uyuşmazlık kapısı 2026-10-20'de 143 satırın 39'unda
yanlış alarm verirdi (kullanıcı kararı: üretilen tablo).

Kullanım:
    python3 scripts/maf-kapsama.py temas            # tabloyu stdout'a basar
    python3 scripts/maf-kapsama.py temas --yaz      # scripts/maf-kapsama-temas.tsv'yi yazar
    python3 scripts/maf-kapsama.py fark eski.txt yeni.txt
    python3 scripts/maf-kapsama.py denetle

Çıkış kodu: `fark` kaldırma ya da tip başlığı değişimi (taban sınıf, `[abstract]`,
`[sealed]`) varsa 1 ("kaldırma bir kusur işidir", `maf-api-kesfi`); eksik dump (`!!`) hatadır;
`denetle` bulgu varsa 1; aksi hâlde 0.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys
from dataclasses import dataclass, field

ROOT = pathlib.Path(__file__).resolve().parent.parent
MATRIS = ROOT / "docs" / "MAF-KAPSAMA.md"
TEMAS = ROOT / "scripts" / "maf-kapsama-temas.tsv"

# §5'te `Takip` sütunu `✗` olan satır sayısı. Yalnız aşağı iner: bir satır `✓` olunca
# `denetle` "taban bayat" der ve bu sayı o gün düşürülür (MAF-KAPSAMA §10 P-03).
CARPI_TABANI = 52

ALANLAR = ("src", "tests", "samples")
DURUMLAR = ("✅", "🟡", "❌", "⛔")
_BOLUM_BASI = "## 5. Yetenek matrisi"
_ATLANAN_DIZINLER = frozenset({"obj", "bin", "node_modules", "Generated"})

_TERS_TIRNAK = re.compile(r"`([^`]+)`")
_MAF_JETONU = re.compile(r"[A-Z][A-Za-z0-9_]*")
_ALT_BOLUM = re.compile(r"^###\s+(5\.\d+)\b")
_HUCRE_AYRACI = re.compile(r"(?<!\\)\|")

_KARAKTER = re.compile(r"'(?:\\(?:u[0-9A-Fa-f]{4}|U[0-9A-Fa-f]{8}|x[0-9A-Fa-f]{1,4}|.)|[^'\\\n])'")
_TANIMLAYICI = re.compile(r"[A-Za-z_][A-Za-z0-9_]*")

TSV_BASLIGI = """\
# MAF kapsama matrisinin (docs/MAF-KAPSAMA.md §5) ölçülen teması. ELLE DÜZENLEME.
# Üretilir: python3 scripts/maf-kapsama.py temas --yaz (kapi.py uret); tazeliği dokuman-bakim.py --denetle denetler.
# src/tests/samples: satırın MAF jetonlarından o alanın kaynağında geçenler (yorum ve dize hariç); "-" temas yok;
# "?" ölçülmedi: satırın yetenek hücresinde ters tırnaklı jeton yok.
# Bir alan değiştiyse satırın Durum sütununu matriste yeniden yargıla; Durum bilerek ölçülmez.
"""


@dataclass(frozen=True)
class Satir:
    """§5'in bir yetenek satırı: alt bölüm, iki elle yazılan sütun ve hücreden çıkan jetonlar."""

    bolum: str
    durum: str
    takip: str
    yetenek: str
    jetonlar: tuple[str, ...]


@dataclass
class Tip:
    """Dump'taki bir tip: başlığı (`base: …`, `[abstract]`, `[sealed]`) ve kendi üyeleri."""

    baslik: str
    uyeler: set[str] = field(default_factory=set)


@dataclass
class Fark:
    """İki dump arasındaki tip kapsamlı fark."""

    eklenen_tipler: list[str] = field(default_factory=list)
    kaldirilan_tipler: list[str] = field(default_factory=list)
    eklenen_uyeler: dict[str, list[str]] = field(default_factory=dict)
    kaldirilan_uyeler: dict[str, list[str]] = field(default_factory=dict)
    degisen_basliklar: dict[str, tuple[str, str]] = field(default_factory=dict)

    @property
    def kirici_olabilir(self) -> bool:
        """Kaldırma ya da başlık değişimi. Taban sınıf değişince miras üyeler de gider
        (dump `DeclaredOnly` döker, onları göstermez); `[sealed]` türetmeyi kapatır."""
        return bool(self.kaldirilan_tipler or self.kaldirilan_uyeler or self.degisen_basliklar)


def _hucreler(satir: str) -> list[str]:
    govde = satir.strip()
    if govde.startswith("|"):
        govde = govde[1:]
    if govde.endswith("|"):
        govde = govde[:-1]
    return [h.strip() for h in _HUCRE_AYRACI.split(govde)]


def hucre_jetonlari(hucre: str) -> tuple[str, ...]:
    """Ters tırnak içindeki, büyük harfle başlayan, en az üç harfli tanımlayıcılar; sıralı."""
    bulunan: set[str] = set()
    for aralik in _TERS_TIRNAK.findall(hucre):
        bulunan.update(j for j in _MAF_JETONU.findall(aralik) if len(j) >= 3)
    return tuple(sorted(bulunan))


def satirlari_oku(metin: str) -> list[Satir]:
    """§5'in yetenek satırlarını okur. Bölüm yoksa ya da satır çıkmazsa hata verir.

    Sessiz boş tablo bir kapının yalan söylemesidir: başlık yeniden adlandırılınca cırcır
    sıfır sayar ve "temiz" der."""
    satirlar = metin.splitlines()
    try:
        bas = next(i for i, s in enumerate(satirlar) if s.startswith(_BOLUM_BASI))
    except StopIteration:
        raise ValueError(f"§5 bulunamadı: '{_BOLUM_BASI}' başlığı matriste yok") from None

    sonuc: list[Satir] = []
    bolum = "5"
    for s in satirlar[bas + 1:]:
        if s.startswith("## "):
            break
        if eslesme := _ALT_BOLUM.match(s):
            bolum = eslesme.group(1)
            continue
        if not s.startswith("|"):
            continue
        hucreler = _hucreler(s)
        if len(hucreler) < 6 or hucreler[0] not in DURUMLAR:
            continue
        sonuc.append(Satir(bolum, hucreler[0], hucreler[5], hucreler[1], hucre_jetonlari(hucreler[1])))

    if not sonuc:
        raise ValueError("§5'te yetenek satırı yok (ilk hücresi ✅ 🟡 ❌ ⛔ olan tablo satırı)")
    return sonuc


def _dize_sonu(metin: str, i: int) -> int | None:
    """`i`'de bir dize sabiti başlıyorsa bittiği konum, başlamıyorsa `None`.

    Önek `$…` ve `@` her sırada olabilir. Ham dize açılıştaki tırnak sayısıyla kapanır;
    enterpolasyonlu dizenin sonu deliklerdeki iç içe dizeleri atlayarak bulunur."""
    n = len(metin)
    j, dolar, verbatim = i, 0, False
    while j < n and metin[j] in "$@":
        if metin[j] == "$":
            dolar += 1
        elif verbatim:
            return None
        else:
            verbatim = True
        j += 1
    if j >= n or metin[j] != '"':
        return None

    tirnak = len(metin[j:]) - len(metin[j:].lstrip('"'))
    if tirnak >= 3 and not verbatim:
        kapanis = metin.find('"' * tirnak, j + tirnak)
        return n if kapanis < 0 else kapanis + tirnak

    k = j + 1
    while k < n:
        c = metin[k]
        if verbatim and c == '"':
            if metin.startswith('""', k):
                k += 2
                continue
            return k + 1
        if not verbatim:
            if c == "\\":
                k += 2
                continue
            if c == '"':
                return k + 1
            if c == "\n":
                return k
        if dolar and c == "{":
            if metin.startswith("{{", k):
                k += 2
                continue
            k = _kod_sonu(metin, k + 1, kapanis="}")
            continue
        k += 1
    return n


def _kod_sonu(metin: str, i: int, kapanis: str) -> int:
    """Enterpolasyon deliğinin kodunu atlar; eşleşen `}`'den sonraki konum."""
    n, derinlik = len(metin), 0
    while i < n:
        c = metin[i]
        if c in '$@"' and (son := _dize_sonu(metin, i)) is not None:
            i = son
            continue
        if c == "'" and (eslesme := _KARAKTER.match(metin, i)):
            i = eslesme.end()
            continue
        if c == "{":
            derinlik += 1
        elif c == kapanis:
            if derinlik == 0:
                return i + 1
            derinlik -= 1
        i += 1
    return n


def _ayikla(metin: str) -> str:
    """Yorum, dize ve karakter sabitlerini boşlukla değiştirir; kod kalır.

    Tek soldan sağa geçiş gerekir: ayrı ayrı silmek "http://x"'i yorum sanar. Düzenli
    ifade de yetmez (Faz 224 denetimi): ham dizenin kapanışı tırnak sayısına,
    enterpolasyonlu dizenin sonu deliğindeki iç içe dizeye bağlıdır."""
    parcalar: list[str] = []
    i, n = 0, len(metin)
    while i < n:
        c = metin[i]
        if metin.startswith("//", i):
            satir_sonu = metin.find("\n", i)
            i = n if satir_sonu < 0 else satir_sonu
        elif metin.startswith("/*", i):
            son = metin.find("*/", i + 2)
            i = n if son < 0 else son + 2
        elif c in '$@"' and (dize_sonu := _dize_sonu(metin, i)) is not None:
            i = dize_sonu
        elif c == "'" and (eslesme := _KARAKTER.match(metin, i)):
            i = eslesme.end()
        else:
            parcalar.append(c)
            i += 1
            continue
        parcalar.append(" ")
    return "".join(parcalar)


def kaynak_jetonlari(metin: str) -> set[str]:
    """C# kaynağındaki tanımlayıcılar; yorum, dize ve karakter sabiti ayıklanır.

    Enterpolasyon deliğindeki kod dizeyle birlikte ayıklanır. Bu yüzden tablo bir teması
    yalnız eksik gösterebilir, fazla değil."""
    return set(_TANIMLAYICI.findall(_ayikla(metin)))


def _kaynak_dosyalari(kok: pathlib.Path, alan: str) -> list[pathlib.Path]:
    dizin = kok / alan
    if not dizin.is_dir():
        return []
    dosyalar = []
    for yol in dizin.rglob("*.cs"):
        parcalar = yol.relative_to(kok).parts
        if _ATLANAN_DIZINLER.intersection(parcalar) or yol.name.endswith(".g.cs"):
            continue
        dosyalar.append(yol)
    return sorted(dosyalar)


def jeton_indeksi(kok: pathlib.Path = ROOT) -> dict[str, set[str]]:
    """Alan başına kaynakta geçen tanımlayıcı kümesi. Okunamayan dosya hata verir, atlanmaz."""
    indeks: dict[str, set[str]] = {}
    for alan in ALANLAR:
        kume: set[str] = set()
        for yol in _kaynak_dosyalari(kok, alan):
            kume.update(kaynak_jetonlari(yol.read_text(encoding="utf-8")))
        indeks[alan] = kume
    return indeks


def temas_uret(kok: pathlib.Path = ROOT) -> str:
    """`scripts/maf-kapsama-temas.tsv`'nin içeriği; aynı girdi aynı baytı üretir."""
    satirlar = satirlari_oku((kok / "docs" / "MAF-KAPSAMA.md").read_text(encoding="utf-8"))
    indeks = jeton_indeksi(kok)
    cikti = [TSV_BASLIGI.rstrip("\n"), "\t".join(("bolum", "durum", "takip", "yetenek", *ALANLAR))]
    for s in satirlar:
        alanlar = [(" ".join(j for j in s.jetonlar if j in indeks[a]) or "-") if s.jetonlar else "?" for a in ALANLAR]
        yetenek = re.sub(r"\s+", " ", s.yetenek)
        cikti.append("\t".join((s.bolum, s.durum, s.takip, yetenek, *alanlar)))
    return "\n".join(cikti) + "\n"


def carpi_mi(takip: str) -> bool:
    """`✗` ve ekli biçimi (`✗ F-600`) sayılır; `✗⁺` sayılmaz."""
    return takip.startswith("✗") and not takip.startswith("✗⁺")


def carpi_say(satirlar: list[Satir]) -> int:
    return sum(1 for s in satirlar if carpi_mi(s.takip))


def carpi_denetle(kok: pathlib.Path = ROOT, taban: int = CARPI_TABANI) -> list[str]:
    """§5'teki `✗` sayısı tabana eşit değilse bir bulgu döner."""
    sayi = carpi_say(satirlari_oku((kok / "docs" / "MAF-KAPSAMA.md").read_text(encoding="utf-8")))
    if sayi > taban:
        return [f"docs/MAF-KAPSAMA.md §5: `✗` satır sayısı {sayi}, taban {taban} — arttı. "
                "Yeni MAF tipi `✗⁺` ile girer; `✗` yalnız azalır (§10 P-03)"]
    if sayi < taban:
        return [f"docs/MAF-KAPSAMA.md §5: `✗` satır sayısı {sayi}, taban {taban} — taban bayat. "
                f"scripts/maf-kapsama.py içinde CARPI_TABANI = {sayi} yap"]
    return []


def tazelik_denetle(kok: pathlib.Path = ROOT) -> list[str]:
    hedef = kok / "scripts" / "maf-kapsama-temas.tsv"
    diskteki = hedef.read_text(encoding="utf-8") if hedef.exists() else ""
    if diskteki != temas_uret(kok):
        return ["scripts/maf-kapsama-temas.tsv — kaynakla aynı değil; `python3 scripts/kapi.py uret` çalıştır"]
    return []


def dump_oku(metin: str) -> dict[str, Tip]:
    """`dump-api.sh` çıktısı: `>>> <TamAd>   base: …` tip satırıdır, dört boşlukla başlayan satır üyedir.

    `!!` satırı (yüklenemeyen ya da bulunamayan derleme) hatadır: eksik bir eski dump o
    derlemedeki her kaldırmayı "eklenen tip" gibi gösterirdi (Faz 224 denetimi)."""
    tipler: dict[str, Tip] = {}
    gecerli: Tip | None = None
    for satir in metin.splitlines():
        if satir.startswith("!!"):
            raise ValueError(f"dump eksik: {satir.strip()}")
        if satir.startswith(">>> "):
            ad, _, baslik = satir[4:].partition("   ")
            gecerli = tipler.setdefault(ad.strip(), Tip(" ".join(baslik.split())))
        elif satir.startswith("#"):
            gecerli = None
        elif gecerli is not None and satir.startswith("    ") and satir.strip():
            gecerli.uyeler.add(satir.strip())
    if not tipler:
        raise ValueError("dump'ta `>>>` tip satırı yok — dump-api.sh çıktısı mı?")
    return tipler


def fark(eski: dict[str, Tip], yeni: dict[str, Tip]) -> Fark:
    sonuc = Fark(
        eklenen_tipler=sorted(yeni.keys() - eski.keys()),
        kaldirilan_tipler=sorted(eski.keys() - yeni.keys()),
    )
    for ad in sorted(eski.keys() & yeni.keys()):
        if eski[ad].baslik != yeni[ad].baslik:
            sonuc.degisen_basliklar[ad] = (eski[ad].baslik, yeni[ad].baslik)
        if eklenen := sorted(yeni[ad].uyeler - eski[ad].uyeler):
            sonuc.eklenen_uyeler[ad] = eklenen
        if kaldirilan := sorted(eski[ad].uyeler - yeni[ad].uyeler):
            sonuc.kaldirilan_uyeler[ad] = kaldirilan
    return sonuc


def _fark_bas(f: Fark) -> None:
    print(f"Eklenen tip ({len(f.eklenen_tipler)}):")
    for tip in f.eklenen_tipler:
        print(f"  + {tip}")
    print(f"Kaldırılan tip ({len(f.kaldirilan_tipler)}):")
    for tip in f.kaldirilan_tipler:
        print(f"  - {tip}")
    print(f"Başlığı değişen tip ({len(f.degisen_basliklar)}):")
    for tip, (eski, yeni) in sorted(f.degisen_basliklar.items()):
        print(f"  ~ {tip}: {eski} → {yeni}")
    tipler = sorted(f.eklenen_uyeler.keys() | f.kaldirilan_uyeler.keys())
    print(f"Üye farkı ({len(tipler)} tip):")
    for tip in tipler:
        print(f"  {tip}")
        for uye in f.eklenen_uyeler.get(tip, []):
            print(f"    + {uye}")
        for uye in f.kaldirilan_uyeler.get(tip, []):
            print(f"    - {uye}")
    print(
        f"Özet: +tip {len(f.eklenen_tipler)} · -tip {len(f.kaldirilan_tipler)} · ~başlık {len(f.degisen_basliklar)} · "
        f"+üye {sum(map(len, f.eklenen_uyeler.values()))} · -üye {sum(map(len, f.kaldirilan_uyeler.values()))}"
    )
    if f.kirici_olabilir:
        print("❌ Kaldırma ya da başlık değişimi var: yükseltme bir kusur işidir (maf-api-kesfi § Sürüm yükseltirken)")


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    alt = parser.add_subparsers(dest="komut", required=True)
    temas = alt.add_parser("temas", help="§5 temas tablosunu üret")
    temas.add_argument("--yaz", action="store_true", help="scripts/maf-kapsama-temas.tsv'ye yaz")
    fark_komutu = alt.add_parser("fark", help="iki dump-api.sh çıktısını tip kapsamlı karşılaştır")
    fark_komutu.add_argument("eski", type=pathlib.Path)
    fark_komutu.add_argument("yeni", type=pathlib.Path)
    alt.add_parser("denetle", help="temas tablosunun tazeliği + ✗ cırcırı")
    args = parser.parse_args(argv)

    if args.komut == "temas":
        metin = temas_uret()
        if args.yaz:
            eski = TEMAS.read_text(encoding="utf-8") if TEMAS.exists() else ""
            TEMAS.write_text(metin, encoding="utf-8")
            print(f"{'değişmedi' if eski == metin else 'yeniden üretildi'} → {TEMAS.relative_to(ROOT)}")
        else:
            sys.stdout.write(metin)
        return 0

    if args.komut == "fark":
        sonuc = fark(dump_oku(args.eski.read_text(encoding="utf-8")), dump_oku(args.yeni.read_text(encoding="utf-8")))
        _fark_bas(sonuc)
        return 1 if sonuc.kirici_olabilir else 0

    bulgular = tazelik_denetle() + carpi_denetle()
    for b in bulgular:
        print(f"❌ {b}")
    if not bulgular:
        print("✅ temas tablosu taze · ✗ cırcırı tabanda")
    return 1 if bulgular else 0


if __name__ == "__main__":
    sys.exit(main())
