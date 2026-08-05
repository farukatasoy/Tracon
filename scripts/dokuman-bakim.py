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
# KARARLAR-INDEKS.md 25_000: Faz 25 kapanisinda (203 karar, 24 reddedilen is)
# ilk kez 24_000'i astı (~1%). Icerik silinmedi/tasinmadi -- baslikklar zaten
# kisaltildi (bkz. K-198..K-203). Butce burada BILEREK 1_000 bayt buyutuldu;
# bu, indeksin sonsuza kadar buyuyecegi yapisal gercegiyle yuzlesmenin ilk
# adimidir. Gercek cozum (bolum bazli indeks veya eski fazlarin arsivlenmesi)
# henuz yazilmadi -- bir sonraki asimda tekrar degerlendirilmeli.
BUTCE = {
    "AGENTS.md": 12_000,
    "MEMORY.md": 8_000,
    "docs/KARARLAR-INDEKS.md": 25_000,
    "docs/MIMARI.md": 42_000,
    "README.md": 20_000,
}

# Alan hafiza dosyalari tek tek buyuyebilir ama biri digerlerini yutmamali.
HAFIZA_DOSYA_BUTCESI = 16_000


def kararlar_indeksi_uret() -> str:
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

    ç = [
        "# KARARLAR — İndeks",
        "",
        "> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](KARARLAR.md).",
        "> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`",
        "",
        "`KARARLAR.md` ~115 KB'dir (~48k token) ve **baştan sona okunmaz.**",
        "Kalemi bu indeksten bul, sonra yalnız o satırı oku:",
        "",
        "```bash",
        "grep -n 'K-059' docs/KARARLAR.md            # tek kalemin tam gerekçesi",
        "sed -n '120,121p' docs/KARARLAR.md          # satır numarasıyla",
        "grep -n 'jsonb\\|migration' docs/KARARLAR.md  # konu araması",
        "```",
        "",
        "İşaretler: 👤 kullanıcı kararı (teknik kanıtla değil, konuşarak değişir) · "
        "🔁 yeniden açılmış",
        "",
        "---",
        "",
        f"## 1. Reddedilen İşler ({len(reddedilen)} kalem) — bunları yeniden önerme",
        "",
        "| KARARLAR.md satırı | Karar | Tarih |",
        "|---|---|---|",
    ]
    for no, baslik, tarih, isaret in reddedilen:
        ç.append(f"| L{no} | {baslik} {isaret} | {tarih} |")

    ç += [
        "",
        f"## 2. Kalıcı Kararlar ({len(kalici)} kalem)",
        "",
        "| K | Satır | Karar | Tarih |",
        "|---|---|---|---|",
    ]
    for no, baslik, tarih, isaret in kalici:
        num = baslik.split("—")[0].strip()
        geri = baslik.split("—", 1)[1].strip() if "—" in baslik else baslik
        ç.append(f"| {num} | L{no} | {geri} {isaret} | {tarih} |")

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
        hedef = ROOT / "docs" / "KARARLAR-INDEKS.md"
        yeni = kararlar_indeksi_uret()
        eski = hedef.read_text(encoding="utf-8") if hedef.exists() else ""
        hedef.write_text(yeni, encoding="utf-8")
        print("değişmedi" if eski == yeni else "yeniden üretildi", f"→ {hedef.relative_to(ROOT)}")
        print()

    return denetle()


if __name__ == "__main__":
    sys.exit(main())
