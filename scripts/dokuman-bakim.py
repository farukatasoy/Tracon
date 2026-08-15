#!/usr/bin/env python3
"""Doküman bakımı — faz kapanışında çalıştırılır.

İki iş yapar:
  1. `docs/KARARLAR-INDEKS.md` dosyasını `docs/KARARLAR.md`'den yeniden üretir.
     İndeks elle yazılmaz; böylece bayatlayamaz.
  2. Sıcak yol dokümanlarının bütçesini denetler. Sıcak yol = her oturumda
     okunan dosyalar. Bunlar büyürse her oturum daha pahalı başlar.

Kullanım:
    python3 scripts/dokuman-bakim.py           # üret + denetle
    python3 scripts/dokuman-bakim.py --denetle # yalnız denetle (CI/kapı)

Bütçe aşılırsa çıkış kodu 1'dir.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Sicak yol: her oturumda (veya her fazda birden cok kez) okunan dosyalar.
# Bayt butcesi ~2.4 bayt/token varsayimiyla secildi.
#
# KARARLAR-INDEKS.md 25_000: Faz 25 kapanisinda (203 karar) ilk kez 24_000'i
# asti ve butce 1_000 bayt buyutuldu; yorum "bir sonraki asimda yapisal cozum"
# diyordu. Faz 27'de (213 karar) tekrar asildi ve yapisal cozum uygulandi:
# indeksten TARIH SUTUNU kaldirildi (-~3 KB). Tarih kaybolmadi -- KARARLAR.md'de
# duruyor ve indeks zaten oraya yollamak icin var. Indeksin isi "kalemi bul,
# satir numarasini al"dir; tarih o iste kullanilmaz. Karar K-214, "yeniden
# acilma kosulu": "bu kez butce buyutulmez, bolunme uygulanir".
# Faz 32 kapanisinda (246 karar) UCUNCU kez asildi; soz tutuldu. "Reddedilen
# Isler" bolumu (~1.7 KB, kucuk ve nadiren buyuyen bir liste) ayri bir dosyaya
# (KARARLAR-INDEKS-REDDEDILEN.md) tasindi; sicak yol artik yalniz "Kalici
# Kararlar" tablosunu tasir. O dosya BUTCE'ye girmez -- yalniz faz-planlama'da,
# yeni bir is onerilmeden once bir kez okunur, her oturumda degil.
# Faz 36 kapanisinda (261 karar) DORDUNCU kez asildi (25_533 B). K-214'un
# "bu kez butce buyutulmez, bolunme uygulanir" sozu yine tutuldu: butce
# BUYUTULMEDI. Basligin acikama metni (kararlar tablosunun DEGIL) sikistirildi
# -- 3 ornek komut 2'ye indi, tekrar eden cumleler birlestirildi (~950 B -> ~470 B).
# Tablonun kendisi (261 satir, ~24 KB) HIC DOKUNULMADI -- icerik kaybi yok.
# Bu, ayni kaynaktan (kisaltilamaz gorunen baslik metni) UCUNCU sikistirmadir;
# bir sonraki asimda gercek bolunme (faz araligina gore iki dosya) gerekir --
# baslik metninde artik sikistirilacak bosluk kalmadi.
# Faz 37 kapanisinda (266 karar, 25_311 B) BESINCI kez asildi. Soz tutuldu:
# GERCEK BOLUNME uygulandi. En eski ARSIV_ESIK kadar kalici karar
# `KARARLAR-INDEKS-ARSIV.md`'ye tasindi (o dosya BUTCE'ye girmez, REDDEDILEN
# ile ayni desen); sicak `KARARLAR-INDEKS.md` yalniz en YENI kararlari tasir.
# Esik sayimla (K-numarasiyla degil) yapilir: K-numaralari birkac yeniden
# acilan/silinen kalemde bosluklu olabilir, sayim her zaman kararlar
# tablosundaki gercek satir sayisini yansitir.
# Faz 58.0 (2026-08-15): esik 150 -> 115. Dosya 24_774 B'ye ulasmisti (226 B
# bosluk) ve bir sonraki faz kapanisi onu kesin asacakti. K-214'un sozu yine
# tutuldu -- butce BUYUTULMEDI, bolunme derinlestirildi. Yeni kural: butcenin
# EN AZ %15'i bos kalmali (58.0'in tum sicak yol dosyalarina koydugu hedef);
# esik bu orani saglayacak sekilde secilir, dosya butceye DAYANDIGINDA degil.
ARSIV_ESIK = 115

BUTCE = {
    "AGENTS.md": 12_000,
    "MEMORY.md": 8_000,
    "docs/KARARLAR-INDEKS.md": 25_000,
    "docs/MIMARI.md": 44_000,  # K-361 (Faz 53): API anahtarı katmanı bugunku mimarinin gercek buyumesi
    "README.md": 20_000,
}

# Alan hafiza dosyalari tek tek buyuyebilir ama biri digerlerini yutmamali.
HAFIZA_DOSYA_BUTCESI = 16_000


def _kararlar_kalemleri() -> tuple[list, list]:
    kaynak = (ROOT / "docs" / "KARARLAR.md").read_text(encoding="utf-8")
    satirlar = kaynak.split("\n")

    kalemler = []
    for no, s in enumerate(satirlar, 1):
        if not s.startswith("| **"):
            continue
        m = re.match(r"\|\s*\*\*(.+?)\*\*(.*?)\|\s*(\d{4}-\d{2}-\d{2})\s*\|", s)
        if m:
            baslik, kuyruk, tarih = m.group(1), m.group(2), m.group(3)
        else:
            m2 = re.match(r"\|\s*\*\*(.+?)\*\*", s)
            baslik, kuyruk, tarih = (m2.group(1) if m2 else s[:70]), "", "?"
        isaret = ""
        if "kullanıcı kararı" in baslik + kuyruk:
            isaret += "👤"
        if "yeniden açıldı" in s:
            isaret += "🔁"
        kalemler.append((no, baslik.strip(), tarih, isaret))

    reddedilen = [k for k in kalemler if not k[1].startswith("K-")]
    kalici = [k for k in kalemler if k[1].startswith("K-")]
    return reddedilen, kalici


def _kararlar_satiri(no: int, baslik: str, isaret: str) -> str:
    num = baslik.split("—")[0].strip()
    geri = baslik.split("—", 1)[1].strip() if "—" in baslik else baslik
    sonek = f" {isaret}" if isaret else ""
    # "L" onekiyle degil dogrudan satir numarasi: sed -n 'N,Np'ye kopyala-yapistir.
    return f"| {num} | {no} | {geri}{sonek} |"


def kararlar_indeksi_uret() -> str:
    """Sıcak yol dosyası: yalnız en YENİ kalıcı (K-NNN) kararlar (ARSIV_ESIK
    kadar). Daha eskisi `KARARLAR-INDEKS-ARSIV.md`'dedir (Karar K-214'ün
    "yeniden açılma koşulu" — bölünme; gerçek bölünme Faz 37'de uygulandı)."""
    _reddedilen, kalici = _kararlar_kalemleri()
    yeni = kalici[-ARSIV_ESIK:] if len(kalici) > ARSIV_ESIK else kalici

    ç = [
        "# KARARLAR — İndeks",
        "",
        "> **Üretilen, elle düzenlenmez.** Kaynak: `KARARLAR.md` ·"
        " üretim: `scripts/dokuman-bakim.py`",
        "",
        "Bul: `grep -n 'K-059\\|jsonb' docs/KARARLAR.md`; oku: `sed -n 'N,Np' docs/KARARLAR.md`."
        " Tarih yok (K-214). Reddedilenler:"
        " [`KARARLAR-INDEKS-REDDEDILEN.md`](KARARLAR-INDEKS-REDDEDILEN.md)."
        f" En eski {len(kalici) - len(yeni)} karar:"
        " [`KARARLAR-INDEKS-ARSIV.md`](KARARLAR-INDEKS-ARSIV.md)."
        " 👤 kullanıcı kararı · 🔁 yeniden açılmış.",
        "",
        "---",
        "",
        f"## En Yeni Kalıcı Kararlar ({len(yeni)} / {len(kalici)} kalem)",
        "",
        "| K | Satır | Karar |",
        "|---|---|---|",
    ]
    for no, baslik, _tarih, isaret in yeni:
        ç.append(_kararlar_satiri(no, baslik, isaret))

    ç.append("")
    return "\n".join(ç)


def kararlar_indeksi_arsiv_uret() -> str:
    """Sıcak yolda DEĞİLDİR — yalnız `KARARLAR-INDEKS.md`de bulunamayan eski
    bir K-NNN aranırken okunur. BUTCE'ye girmez (REDDEDILEN ile aynı desen,
    Karar K-214)."""
    _reddedilen, kalici = _kararlar_kalemleri()
    eski = kalici[:-ARSIV_ESIK] if len(kalici) > ARSIV_ESIK else []

    ç = [
        "# KARARLAR — İndeks Arşivi",
        "",
        "> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](KARARLAR.md).",
        "> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`",
        "",
        "En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme)."
        " Yeni kararlar için: [`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md).",
        "",
        f"## Arşivlenen Kararlar ({len(eski)} kalem)",
        "",
        "| K | Satır | Karar |",
        "|---|---|---|",
    ]
    for no, baslik, _tarih, isaret in eski:
        ç.append(_kararlar_satiri(no, baslik, isaret))

    ç.append("")
    return "\n".join(ç)


def kararlar_reddedilen_uret() -> str:
    """Sıcak yolda DEĞİLDİR — yalnız yeni bir iş önerilmeden önce, `faz-planlama`
    sırasında bir kez okunur. Bu yüzden `BUTCE`'ye girmez (Karar K-214)."""
    reddedilen, _kalici = _kararlar_kalemleri()

    ç = [
        "# KARARLAR — Reddedilen İşler",
        "",
        "> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](KARARLAR.md).",
        "> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`",
        "",
        "Daha önce kanıtla reddedilmiş işlerin kontrol listesi — **bunları yeniden önerme.**",
        "Kalıcı (K-NNN) kararlar için: [`KARARLAR-INDEKS.md`](KARARLAR-INDEKS.md).",
        "",
        "```bash",
        "sed -n '120,121p' docs/KARARLAR.md   # satır numarasıyla tam gerekçe",
        "```",
        "",
        f"## Reddedilen İşler ({len(reddedilen)} kalem)",
        "",
        "| KARARLAR.md satırı | Karar |",
        "|---|---|",
    ]
    for no, baslik, _tarih, isaret in reddedilen:
        sonek = f" {isaret}" if isaret else ""
        ç.append(f"| L{no} | {baslik}{sonek} |")

    ç.append("")
    return "\n".join(ç)


def denetle() -> int:
    hata = 0
    print("Sıcak yol doküman bütçesi")
    print(f"{'dosya':<28} {'bayt':>8} {'bütçe':>8}  {'~token':>7}")
    for yol, sinir in BUTCE.items():
        p = ROOT / yol
        if not p.exists():
            print(f"{yol:<28} {'YOK':>8}")
            continue
        n = len(p.read_bytes())
        durum = "ok" if n <= sinir else "AŞTI"
        if n > sinir:
            hata = 1
        print(f"{yol:<28} {n:>8} {sinir:>8}  {n * 10 // 24:>7}  {durum}")

    print("\nAlan hafıza dosyaları")
    hafiza = sorted((ROOT / "docs" / "hafiza").glob("*.md"))
    for p in hafiza:
        n = len(p.read_bytes())
        durum = "ok" if n <= HAFIZA_DOSYA_BUTCESI else "AŞTI — ikiye böl"
        if n > HAFIZA_DOSYA_BUTCESI:
            hata = 1
        print(f"  {p.name:<26} {n:>8} {HAFIZA_DOSYA_BUTCESI:>8}  {durum}")

    toplam = sum(len((ROOT / y).read_bytes()) for y in BUTCE if (ROOT / y).exists())
    print(f"\nOturum başı sıcak yol toplamı: {toplam} B (~{toplam * 10 // 24} token)")
    if hata:
        print("\n❌ Bütçe aşıldı. Notu alan dosyasına taşı veya geçmişi arşive al.")
    else:
        print("\n✅ Bütçeler içinde.")
    return hata


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--denetle", action="store_true", help="yalnız denetle, üretme")
    a = ap.parse_args()

    if not a.denetle:
        for hedef, uret in (
            (ROOT / "docs" / "KARARLAR-INDEKS.md", kararlar_indeksi_uret),
            (ROOT / "docs" / "KARARLAR-INDEKS-ARSIV.md", kararlar_indeksi_arsiv_uret),
            (ROOT / "docs" / "KARARLAR-INDEKS-REDDEDILEN.md", kararlar_reddedilen_uret),
        ):
            yeni = uret()
            eski = hedef.read_text(encoding="utf-8") if hedef.exists() else ""
            hedef.write_text(yeni, encoding="utf-8")
            print("değişmedi" if eski == yeni else "yeniden üretildi", f"→ {hedef.relative_to(ROOT)}")
        print()

    return denetle()


if __name__ == "__main__":
    sys.exit(main())
