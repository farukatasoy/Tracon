#!/usr/bin/env python3
"""Doküman bakımı — faz kapanışında çalıştırılır.

Üç iş yapar:
  1. DÖRT dosyayı yeniden üretir: `docs/KARARLAR-INDEKS.md`,
     `docs/arsiv/KARARLAR-INDEKS-ARSIV.md`, `docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md`
     (üçü `docs/KARARLAR.md`'den) ve `docs/YOL-HARITASI.md` (faz dokümanlarının
     `> **Durum:**` satırından). Hiçbiri elle yazılmaz; böylece bayatlayamazlar.
  2. Doküman erişim katmanlarının bütçesini denetler: başlangıç bağlamı,
     gerektiğinde sorgulanan referanslar ve ledger.
  3. Kapanmış fazın `docs/` kökünde kalmadığını ve tüm yerel bağlantıların
     çözüldüğünü doğrular.

Kullanım:
    python3 scripts/dokuman-bakim.py               # üret + denetle
    python3 scripts/dokuman-bakim.py --denetle     # yalnız denetle (CI/kapı)
    python3 scripts/dokuman-bakim.py --projeksiyon # bayt/faz + kalan faz tahmini

    # Faz kapanışında: kullanıcıya dönük yüzey değişti mi, site güncellendi mi?
    python3 scripts/dokuman-bakim.py --site-denetle --taban <faz öncesi commit>

Bütçe aşılırsa çıkış kodu 1'dir.
"""
from __future__ import annotations

import argparse
import datetime
import os
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent

# Doküman erişim katmanları. Başlangıç bağlamı her oturumda okunur. Sorgu
# bağlamı yalnız ilgili alana girildiğinde, ledger ise yalnız tarihçe/karar
# aranırken okunur. Bayt bütçesi ~2.4 bayt/token varsayımıyla seçildi.
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

BASLANGIC_BUTCESI = {
    "AGENTS.md": 12_000,
    "MEMORY.md": 8_000,
}

SORGU_BUTCESI = {
    "docs/KARARLAR-INDEKS.md": 25_000,
    "docs/MIMARI.md": 44_000,  # K-361 (Faz 53) · Faz 77: §7 ayrildi, 41_043 -> 24_278
    # Faz 77 (K-524): §7 "Guvenlik Modeli" 17 KB'a ulasmisti -- dosyanin %42'si ve
    # sonraki en buyuk bolumun iki kati; her guvenlik fazi ona ekliyordu. Ayri dosyaya
    # alindi. Yeni dosyanin siniri ILK KEZ konuyor (K-214'un "buyutulmez" sozu var
    # olan bir siniri korur), bu yuzden OLCULEN boyuta %15 bosluk eklendi -- 58.4'un
    # kalibrasyon kurali. MIMARI.md'nin siniri DUSURULMEDI: bolunme zaten %45 bosluk
    # birakti ve bugunku mimarinin buyumesine yer birakmak istiyoruz.
    "docs/MIMARI-GUVENLIK.md": 21_000,  # olculen 17_421
    # Faz 90 (K-6xx): alan yonlendirme tablosu `MEMORY.md`den ayrildi. Tablo ALAN
    # SAYISIYLA buyur (20 -> 27 dosya), `MEMORY.md`nin tuzak listesi OGRENILEN
    # DERSLE; iki egri tek butcede sikisip dosyayi %1 bosluga dusurmustu.
    # BASLANGIC degil SORGU: `faz-baslangic` Adim 1 bunu okumaz, Adim 3 okur.
    # Sinir ILK KEZ konuyor -> olculene %15 bosluk eklendi (58.4 kalibrasyonu).
    "docs/hafiza/00-INDEKS.md": 4_100,  # olculen 3_424
    "README.md": 20_000,
}

YONETIM_BUTCESI = {
    # --- Faz 58.4'te eklendi ---------------------------------------------
    # Bunlar her oturumda BASTAN SONA okunmaz ama her planlama/kapanis
    # turunda buyurler ve hicbir freni yoktu. Sinirlar 2026-08-16'da OLCULEN
    # gercek boyuta %15 bosluk eklenerek kondu, tahminle degil.
    #
    # KARARLAR.md: ilk deger 465_000 idi ve Faz 58'in KENDI kararlari
    # (K-411..K-414, +5.663 B) eklenmeden ONCE olculmustu -- kapanista %14'e
    # dustu. Bu bir buyume olayi degil, kalibrasyon hatasiydi: sinir fazin
    # SONUNDAKI boyuta gore konur. K-214'un "butce buyutulmez" sozu var olan
    # bir sinirin asilmasi icindir; burada sinir ilk kez konuyor.
    "docs/KARARLAR.md": 475_000,           # olculen 398_967 (faz kapanisindan sonra)
    "docs/ADAYLAR.md": 80_000,  # olculen 67_195
}

# Birleştirilmiş görünüm yalnız büyüme projeksiyonu içindir. Raporlama bu
# kümeleri birbiriyle toplamaz; ledger boyutu başlangıç maliyeti değildir.
BUTCE = BASLANGIC_BUTCESI | SORGU_BUTCESI | YONETIM_BUTCESI

# Alan hafiza dosyalari tek tek buyuyebilir ama biri digerlerini yutmamali.
HAFIZA_DOSYA_BUTCESI = 16_000

# --- Dizin butceleri (Faz 58.4) ------------------------------------------
# Tek dosya freni yetmez: hangi dosyanin buyudugunden BAGIMSIZ bir ust sinir
# gerekir. Sinir "oturum maliyetini" olcer, disk boyutunu degil -- bu yuzden
# ARSIV ve KOSUM KAYITLARI HARIC tutulur (kullanici karari, 2026-08-16):
#
#   docs/arsiv/                 -> yalniz grep'lenir, hicbir oturum bastan okumaz
#   docs/manuel-test/kosumlar/  -> bir kosumun kaydi; spec degil
#   docs/kesif/                 -> bir aday-kesfi turunun kaydi; spec degil (K-426)
#
# Gerekce: bu ikisi sayilsaydi ARSIVLEMEK sayaci degistirmezdi ve disiplinin
# istedigi davranis (sicak yoldan cikarma) odullendirilmezdi. Boyle bir sinir
# yalnizca SILMEYE zorlar -- AGENTS.md'nin "icerik silinmez, tasinir" kuralinin
# tam tersi. Haric tutunca arsive tasimak sayaci GERCEKTEN dusurur.
HARIC = ("docs/arsiv", "docs/manuel-test/kosumlar", "docs/kesif")

DIZIN_BUTCESI = {
    # (yol, ozyinelemeli mi) -> sinir.  Olculen deger 2026-08-16.
    ("docs/manuel-test", False): 1_950_000,  # olculen 1_646_886 (yalniz spec)
    ("docs", True): 5_000_000,               # olculen 4_206_267 (haric'ler dusuldu)
}

# Bir butcenin en az bu kadari bos kalmali; asagisi "DAR" olarak isaretlenir
# (hata degil, erken uyari). Faz 58.0'in tum sicak yol dosyalarina koydugu hedef.
BOSLUK_ORANI = 0.15

# --- docs-site senkron denetimi ------------------------------------------
# `docs/` Turkce gelistirme gunlugudur; `docs-site/` Ingilizce URUN
# dokumantasyonudur ve yayinlanir. Faz 59 siteyi yayinladi fakat onu dogru
# tutan bir mekanizma yoktu: hicbir skill `docs-site`'a deginmiyordu. Site
# bayatlarsa kusur KULLANICIYA gorunur -- kod dogru olsa bile.
#
# Faz 80: eskiden kural KUME halinde "herhangi bir site sayfasi degisti mi"ye
# bakiyordu -- tetiklenen kural sayisindan BAGIMSIZ. `Workflows/` degistirip
# yalniz `packages.md`yi duzenlemek de yesil doenuyordu. Artik her kural KENDI
# hedefine karsi denetlenir (`_kural_eslesmesi`). Dizin hedefi ("concepts/")
# BILEREK genistir -- Abstractions/Core degisiminin hangi kavram sayfasina
# dusecegi onceden bilinemez; kalan kurallar TAM dosya eslesmesi ister.
SITE_KURALLARI: tuple[tuple[str, str, tuple[str, ...], str], ...] = (
    ("http-api", r"^src/AgentPrism\.AspNetCore/(Endpoints|OpenAICompat|A2A|McpServer)/",
     ("http-api.md",), "HTTP yuzeyi degisti"),
    ("guvenlik-kiraci", r"^src/AgentPrism\.AspNetCore/(Security|Tenancy)/",
     ("getting-started/security.md", "concepts/governance.md"), "guvenlik/kiraci sinirlari degisti"),
    ("arayuz", r"^src/AgentPrism\.UI/frontend/src/(screens|components)/",
     ("ui.md",), "ekran veya bilesen degisti (ekran goruntusu de gerekebilir)"),
    # capabilities.md kurali, genel Abstractions|Core kuralindan ONCE yazilir --
    # `buildTransitive/` ikisini de tetikler, ikisi de ayri satir olarak raporlanir.
    ("buildtransitive", r"^src/AgentPrism\.Core/buildTransitive/",
     ("capabilities.md",), "tuketicinin gordugu MSBuild yuzeyi degisti"),
    ("cekirdek-kavram", r"^src/AgentPrism\.(Abstractions|Core)/",
     ("concepts/",), "cekirdek kavram yuzeyi degisti"),
    ("workflow", r"^src/AgentPrism\.Workflows/",
     ("concepts/workflows.md",), "workflow yurutmesi degisti"),
    ("kalicilik", r"^src/AgentPrism\.(PostgreSql|SqlServer|Sqlite|Sql\.Shared)/",
     ("getting-started/persistence.md",), "kalicilik katmani degisti"),
    ("model-saglayici", r"^src/AgentPrism\.(OpenAI|Anthropic|Google|Azure|Voice)/",
     ("getting-started/first-agent.md",), "model saglayicisi degisti"),
    ("proje-sablonu", r"^src/AgentPrism\.Templates/",
     ("getting-started/index.md",), "proje sablonu degisti"),
    ("paket-tanimi", r"^src/AgentPrism[^/]*/[^/]*\.csproj$",
     ("packages.md",), "paket tanimi degisti"),
    ("paket-readme", r"^src/AgentPrism[^/]*/README\.md$",
     ("packages.md",), "paket README'si degisti"),
)


def _kural_eslesmesi(degisen: list[str]) -> list[tuple[str, tuple[str, ...], str, bool]]:
    """Her TETIKLENEN SITE_KURALLARI kurali icin (ad, hedefler, tetikleyen dosya,
    karsilandi mi). Saf fonksiyon -- git veya dosya sistemi cagrisi yapmaz, testi
    dogrudan bir dosya yolu listesiyle kosar.

    Dizin hedefi ("concepts/") herhangi bir alt sayfanin degismesiyle karsilanir;
    kalan hedefler TAM dosya adiyla eslesir ve listedeki hedeflerden HERHANGI
    BIRININ degismesi yeterlidir (guvenlik-kiraci kurali iki alternatif sayfa
    tasir)."""
    degisen_kume = set(degisen)
    sonuc: list[tuple[str, tuple[str, ...], str, bool]] = []
    for ad, desen, hedefler, _neden in SITE_KURALLARI:
        vuran = [y for y in degisen if re.search(desen, y)]
        if not vuran:
            continue
        if hedefler == ("concepts/",):
            karsilandi = any(
                y.startswith("docs-site/src/content/docs/concepts/") for y in degisen
            )
        else:
            tam_hedefler = {f"docs-site/src/content/docs/{h}" for h in hedefler}
            karsilandi = bool(tam_hedefler & degisen_kume)
        sonuc.append((ad, hedefler, vuran[0], karsilandi))
    return sonuc


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


def _kararlar_tablosu() -> tuple[int, list[tuple[int, str]]]:
    """§2 karar tablosunun satırları: (satır no, ham satır). Başlangıç `|---|`
    ayıracından sonra, bölümü kapatan `---` veya `## ` başlığına kadar okur.
    Boş satırlar DA döner — tabloyu kesen boş satır bir kusurdur."""
    satirlar = (ROOT / "docs" / "KARARLAR.md").read_text(encoding="utf-8").split("\n")
    bas = next(i for i, s in enumerate(satirlar) if s.startswith("## 2. "))
    ayirac = next(i for i in range(bas, len(satirlar)) if satirlar[i].startswith("|---"))
    govde = []
    for i in range(ayirac + 1, len(satirlar)):
        s = satirlar[i]
        if s.startswith("## ") or s.strip() == "---":
            break
        govde.append((i + 1, s))
    # Bolumu kapatan `---`'den ONCEKI bos satir mesrudur; tabloyu kesmez.
    while govde and not govde[-1][1].strip():
        govde.pop()
    return ayirac + 1, govde


def kararlar_denetle() -> tuple[int, list[str]]:
    """§2 karar tablosunun YAPISAL bütünlüğü: yinelenen numara · tabloyu kesen
    boş satır · sıra dışı numara.

    Faz 77 ve Faz 78 aynı tabandan yazıldı ve İKİSİ de K-535 ile K-536'yı aldı;
    hiçbir kapı görmedi (2026-08-21 keşif turu, kanal 2). Sebep: indeks üreteci
    (`_kararlar_kalemleri`) satır satır regex okur ve tablo YAPISINA hiç bakmaz
    -- bu yüzden hem yinelenen numarayı hem tabloyu kesen boş satırı sessizce
    geçiriyordu. Boş satır Markdown'da tabloyu ORADA bitirir: sonraki kararlar
    başlıksız ikinci bir tabloya düşer ve sitede/önizlemede satır olarak
    okunmaz. İki vaka vardı (398 ve 584); Faz 77 denetimi yalnız birini gördü."""
    bulgular = []
    _bas, govde = _kararlar_tablosu()

    bos = [no for no, s in govde if not s.strip()]
    for no in bos:
        bulgular.append(f"KARARLAR.md:{no} tabloyu kesen BOŞ satır — tablo orada biter")

    numaralar = []
    for no, s in govde:
        m = re.match(r"\|\s*\*\*K-(\d+)", s)
        if m:
            numaralar.append((int(m.group(1)), no))

    gorulen: dict[int, int] = {}
    for n, no in numaralar:
        if n in gorulen:
            bulgular.append(
                f"KARARLAR.md:{no} K-{n} YİNELENEN numara (ilki satır {gorulen[n]})")
        else:
            gorulen[n] = no

    for (a, _), (b, no) in zip(numaralar, numaralar[1:]):
        if b < a:
            bulgular.append(f"KARARLAR.md:{no} K-{b} sıra dışı — önceki K-{a}")

    return len(numaralar), bulgular


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
        " [`arsiv/KARARLAR-INDEKS-REDDEDILEN.md`](arsiv/KARARLAR-INDEKS-REDDEDILEN.md)."
        f" En eski {len(kalici) - len(yeni)} karar:"
        " [`arsiv/KARARLAR-INDEKS-ARSIV.md`](arsiv/KARARLAR-INDEKS-ARSIV.md)."
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
        "> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).",
        "> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`",
        "",
        "En eski kalıcı kararlar — sıcak yolun dışında (Karar K-214, gerçek bölünme)."
        " Yeni kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).",
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
        "> **Üretilen dosya. Elle düzenleme.** Kaynak: [`KARARLAR.md`](../KARARLAR.md).",
        "> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`",
        "",
        "Daha önce kanıtla reddedilmiş işlerin kontrol listesi — **bunları yeniden önerme.**",
        "Kalıcı (K-NNN) kararlar için: [`KARARLAR-INDEKS.md`](../KARARLAR-INDEKS.md).",
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


def _dizin_boyutu(yol: str, ozyinelemeli: bool, haric_uygula: bool = True) -> int:
    """Bir dizindeki .md dosyalarinin toplam bayti.

    `haric_uygula` False verilirse HARIC dusulmez -- denetim disi kalan
    yigini RAPORLAMAK icin gerekir (kendini dusurmesin diye).
    """
    kok = ROOT / yol
    if not kok.exists():
        return 0
    haric = tuple((ROOT / h).resolve() for h in HARIC) if haric_uygula else ()
    desen = kok.rglob("*.md") if ozyinelemeli else kok.glob("*.md")
    toplam = 0
    for p in desen:
        r = p.resolve()
        if any(r == h or h in r.parents for h in haric):
            continue
        toplam += len(p.read_bytes())
    return toplam


def _satir(ad: str, n: int, sinir: int, genislik: int = 30) -> tuple[str, bool]:
    """Bicimlenmis satir ve 'butce asildi mi' bayragi."""
    asti = n > sinir
    bosluk = (sinir - n) / sinir if sinir else 0
    if asti:
        durum = "AŞTI"
    elif bosluk < BOSLUK_ORANI:
        durum = f"DAR (%{bosluk * 100:.0f} boş)"
    else:
        durum = f"ok (%{bosluk * 100:.0f} boş)"
    return f"{ad:<{genislik}} {n:>9} {sinir:>9}  {n * 10 // 24:>8}  {durum}", asti


def yol_haritasi_uret() -> str:
    """Faz yol haritasi -- `docs/NN-*.md` dosyalarindan URETILIR, elle yazilmaz.

    Faz 58: README'deki tablo Faz 21-56'yi tek satirda ozetliyordu ve
    "Faz dokumanlari (00-32)" gibi elle bakilan sayilar bayatliyordu. Kaynak
    artik fazin KENDI dokumanindaki `> **Durum:**` satiridir; bir faz kapandiginda
    yol haritasi kendiliginden dogrulanir (K-214'un indeks deseni).
    """
    kisalt = {
        "Tamamlandı": "✅ Tamamlandı",
        "Tamam": "✅ Tamamlandı",
        "Beklemede": "⏸ Beklemede",
        "Planlandı": "📋 Planlandı",
    }

    # Faz 77: kapanmis fazlar `docs/arsiv/fazlar/` altina tasindi --
    # sicak yol sayacindan cikmalari icin (HARIC listesi arsivi dusuyor). Yol
    # haritasi IKI konumu da tarar; baglanti dosyanin GERCEK yerini gosterir,
    # yoksa 60 faz sessizce listeden duserdi.
    kaynaklar = sorted(
        (ROOT / "docs").glob("[0-9][0-9]-*.md"),
        key=lambda q: q.name,
    ) + sorted(
        (ROOT / "docs" / "arsiv" / "fazlar").glob("[0-9][0-9]-*.md"),
        key=lambda q: q.name,
    )
    satirlar = []
    for p in sorted(kaynaklar, key=lambda q: q.name):
        metin = p.read_text(encoding="utf-8")
        mb = re.search(r"^#\s+(.*)$", metin, re.M)
        md = re.search(r"^>\s*\*\*Durum:\*\*\s*(.*)$", metin, re.M)

        baslik = mb.group(1).strip() if mb else p.stem
        baslik = re.sub(r"^Faz\s+\d+\s*[—-]\s*", "", baslik)

        ham = (md.group(1) if md else "?").strip()
        ham = ham.replace("*", "").replace("✅", "").replace("⏸", "").replace("📋", "").strip()
        durum = next((v for k, v in kisalt.items() if ham.startswith(k)), ham or "?")

        no = p.name[:2].lstrip("0") or "0"
        yol = p.relative_to(ROOT / "docs").as_posix()
        satirlar.append(f"| [{no}]({yol}) | {baslik} | {durum} |")

    return "\n".join(
        [
            "# Faz Yol Haritası",
            "",
            "> **Üretilen dosya. Elle düzenleme.** Kaynak: her fazın kendi",
            "> dokümanındaki `> **Durum:**` satırı. Açık fazlar `docs/` kökünde,",
            "> kapanmış fazlar `docs/arsiv/fazlar/` altında yaşar.",
            "> Yeniden üretmek için: `python3 scripts/dokuman-bakim.py`",
            "",
            "Bir fazın durumu yanlış görünüyorsa **o fazın dokümanını** düzelt;",
            "bu dosyayı düzeltmek bir sonraki üretimde geri alınır.",
            "",
            f"## Fazlar ({len(satirlar)} kalem)",
            "",
            "| Faz | Konu | Durum |",
            "|-----|------|-------|",
            *satirlar,
            "",
            "Seçilmemiş adaylar: [`ADAYLAR.md`](ADAYLAR.md). Fazların hangi dalgada,"
            " hangi gerekçeyle sıralandığı (Faz 8–56, kapandı):"
            " [`arsiv/IKINCI-FAZ-YOL-HARITASI.md`](arsiv/IKINCI-FAZ-YOL-HARITASI.md) ·"
            " [`arsiv/UCUNCU-FAZ-YOL-HARITASI.md`](arsiv/UCUNCU-FAZ-YOL-HARITASI.md).",
            "",
        ]
    )


def kapanmis_faz_bulgulari(kok: pathlib.Path = ROOT) -> list[str]:
    """Kök `docs/` yalnız canlı fazları taşımalıdır.

    `Tamamlandı` durumundaki bir faz kökte kalırsa, başlangıçta bakılacak dosya
    sayısı gereksiz büyür ve kapanmış plan güncel bağlam gibi görünür. Taşınan
    dosyanın bağlantıları `kirik_baglantilar()` tarafından ayrıca doğrulanır.
    """
    bulgular = []
    docs = kok / "docs"
    for p in sorted(docs.glob("[0-9][0-9]-*.md")):
        metin = p.read_text(encoding="utf-8")
        durum = re.search(r"^>\s*\*\*Durum:\*\*\s*(.*)$", metin, re.M)
        if durum and re.search(r"(?:✅\s*)?Tamamlandı", durum.group(1)):
            bulgular.append(
                f"{p.relative_to(kok).as_posix()}: kapanmış faz `docs/arsiv/fazlar/` altında olmalı")
    return bulgular


def _git(*args: str) -> list[str] | None:
    """Cikis satirlari, ya da `None` -- komut basarisiz olduysa (git yok, repo
    disi, vb). `None` ile bos listeyi AYIRT ETMEK zorunludur: ayirt edilmezse
    bir git hatasi "degisiklik yok"e donusur ve kapi SESSIZCE gecer (Faz 80,
    kullanici karari -- bir kapinin en kotu hali sessiz gecistir)."""
    r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    if r.returncode != 0:
        return None
    return [s for s in r.stdout.splitlines() if s.strip()]


def _degisen_dosyalar(taban: str | None) -> list[str] | None:
    """Fazin dokundugu her dosya: taban..HEAD + calisma agaci + izlenmeyenler,
    ya da `None` -- alttaki `git` cagrilarindan biri basarisiz olduysa.

    Izlenmeyenler dahildir cunku yeni bir site sayfasi HENUZ commit edilmemis
    olabilir; onu gormezsek denetim yanlis yere kirmizi verir."""
    yollar: set[str] = set()
    if taban:
        d = _git("diff", "--name-only", f"{taban}...HEAD")
        if d is None:
            return None
        yollar.update(d)
    d2 = _git("diff", "--name-only", "HEAD")
    if d2 is None:
        return None
    yollar.update(d2)
    d3 = _git("status", "--porcelain")
    if d3 is None:
        return None
    yollar.update(s[3:].strip('"') for s in d3 if s.startswith("??"))
    return sorted(yollar)


def site_denetle(taban: str | None, gerekce_yazildi: bool) -> int:
    degisen = _degisen_dosyalar(taban)
    if degisen is None:
        print("❌ `git` çağrısı başarısız oldu; docs-site senkronu denetlenemedi.")
        return 1
    if not degisen:
        print("docs-site senkronu: değişiklik yok (taban verilmedi mi?).")
        return 0

    eslesme = _kural_eslesmesi(degisen)
    neden = {ad: n for ad, _desen, _hedefler, n in SITE_KURALLARI}

    print(f"docs-site senkronu — {len(degisen)} değişen dosya, {len(eslesme)} kural tetiklendi")
    if not eslesme:
        print("  Kullanıcıya dönük yüzey değişmedi. Site güncellemesi gerekmiyor.")
        return 0

    karsilanmadi = [e for e in eslesme if not e[3]]
    for ad, hedefler, tetikleyen, ok in eslesme:
        durum = "✅" if ok else "❌"
        print(f"  {durum} {ad:<18} hedef: {' · '.join(hedefler):<38} "
              f"{neden[ad]:<40} ör: {tetikleyen}")

    if not karsilanmadi:
        print("\n✅ Tetiklenen her kuralın hedefi değişenler arasında.")
        return 0

    if gerekce_yazildi:
        print(f"\n⚠️  {len(karsilanmadi)} kural karşılanmadı; gerekçe faz dokümanına "
              "yazıldı (--site-gerekce-yazildi):")
        for ad, hedefler, tetikleyen, _ in karsilanmadi:
            print(f"   - {ad}: hedef {' · '.join(hedefler)} ({tetikleyen} tetikledi)")
        return 0

    print(f"\n❌ {len(karsilanmadi)} kural karşılanmadı:")
    for ad, hedefler, tetikleyen, _ in karsilanmadi:
        print(f"   - {ad}: hedef {' · '.join(hedefler)} değişmedi ({tetikleyen} tetikledi)")
    print("   Ya hedef sayfayı güncelle ya gerekçesini faz dokümanına yazıp")
    print("   --site-gerekce-yazildi ile geç. Sessizce atlama.")
    return 1


# --- Kirik baglanti denetimi (Faz 77, genisletildi Faz 80) ---------------
# Faz 77 arsivlemesi ayni tuzagi IKI kez uretti: bir blok `docs/X.md`'den
# `docs/arsiv/Y.md`'ye tasindiginda blogun ICINDEKI goreli linkler hâlâ
# `docs/`'a goredir ve arsiv dizininden cozulmez. Ilk seferinde 17, ikinci
# seferinde 3 baglanti kirildi. Elle fark edilmesi guvenilmez -- denetim
# artik her kosumda bunu sayar.
#
# Faz 80: eskiden site-mutlak baglanti (`/reference/x/`) ve `.mdx` sayfalari
# TAMAMEN denetim disiydi. Naif "dosya yolu = slug" varsayimi 7355 baglantinin
# 6916'sini kirik gosterdi -- `api/` ve `http-api/` URETILEN sayfalari
# frontmatter `slug:` ile yeniden adlandirdigi icin. Dogru harita kurulunca
# (frontmatter `slug:` ONCELIKLI) elle yazilan sayfalardan cikan baglantilarin
# hicbiri kirik cikmadi (olculdu, 2026-08-21).
LINK = re.compile(r"\]\(([^)\s]+?)(#[^)\s]*)?\)")

# Uretilen ya da yer tutucu tasiyan yollar: kaynak olarak denetim disi.
BAGLANTI_HARIC = (
    "docs-site/src/content/docs/api/",       # npm run generate uretir
    "docs-site/src/content/docs/http-api/",  # npm run generate uretir
    "docfx/",                                # uretilen ara ciktilar
)

# Site-mutlak (`/...`) bir baglanti bu dizinlerin ALTINA dusuyorsa hedef olarak
# da denetim disidir: `api/`, `http-api/` ve `openapi/` `build` isinde henuz
# URETILMEMISTIR -- ayri bir `site` isi `npm run build` (-> `prebuild` ->
# `generate`) icinde uretir ve ucu commit EDILMEZ (`.gitignore`). Bagimsiz
# denetim bunu OLCTU: `http-api.md`deki `/openapi/agentprism.json` baglantisi,
# `openapi` bu listede olmadan, HER temiz `build` checkout'unda kalici yanlis
# pozitif uretirdi -- kaynak sayfa hic degismese bile. Kok dizinin kendisi de
# (`/http-api/`, `/api/`) ayni sebeple denetim disidir -- URL duzeyinde ayirt
# edilemez.
SITE_URETILEN_HEDEF = ("api", "http-api", "openapi")


def _slug_hesapla(rel: str, frontmatter_slug: str | None) -> str:
    """Bir docs-site sayfasinin Starlight slug'i: frontmatter `slug:` varsa o
    kullanilir (K-onceligi), yoksa dosya yolundan turetilir -- `x/index.md` ->
    `x`, kok `index.md`/`index.mdx` -> `""`. Saf fonksiyon; dosya sistemi veya
    git istemez, testi dogrudan bir yol dizesiyle kosar."""
    if frontmatter_slug:
        return frontmatter_slug
    if rel in ("index.md", "index.mdx"):
        return ""
    if rel.endswith("/index.md") or rel.endswith("/index.mdx"):
        return rel.rsplit("/", 1)[0]
    return rel.rsplit(".", 1)[0]


_FRONTMATTER_SLUG = re.compile(r'^slug:\s*(.+?)\s*$', re.M)


def _site_slug_haritasi(kok: pathlib.Path) -> dict[str, pathlib.Path]:
    """Elle yazilan docs-site sayfalarinin slug -> dosya haritasi. `api/` ve
    `http-api/` URETILEN oldugu icin haric tutulur -- `build` isinde henuz
    yoklardir ve dahil edilirlerse fantom hedefler uretirler."""
    docs = kok / "docs-site" / "src" / "content" / "docs"
    harita: dict[str, pathlib.Path] = {}
    if not docs.exists():
        return harita
    for p in list(docs.rglob("*.md")) + list(docs.rglob("*.mdx")):
        rel = p.relative_to(docs).as_posix()
        if rel.split("/", 1)[0] in SITE_URETILEN_HEDEF:
            continue
        try:
            metin = p.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        m = _FRONTMATTER_SLUG.search(metin)
        fm_slug = m.group(1).strip("'\"") if m else None
        harita[_slug_hesapla(rel, fm_slug)] = p
    return harita


# Faz 90: `LINK` ham metni tarar ve KOD BLOGUNU ayirt etmez. Bir dokuman bir
# markdown ORNEGI gosterdiginde (damitilmis faz kaydinin sablonu gibi) ornekteki
# `](...)` gercek bir baglanti sanilir ve kapi kalici yanlis pozitif uretir --
# olculdu: `docs/90-DOKUMAN-DAMITMA-POLITIKASI.md -> ../../ADAYLAR.md`. Var olan
# `"sablonu.md" in rel` istisnasi ayni sinifin dosya bazli, kaba cozumuydu;
# bu islev sorunu kaynaginda kapatir ve istisnayi gereksizlestirmez (sablon
# dosyalari fence DISINDA da yer tutucu tasir).
# Fence YALNIZ sutun 0'da taninir. CommonMark 3 bosluga kadar girinti kabul
# eder, ama bir LISTE OGESI icindeki kod blogunun kapanis fence'i de girintili
# olur (`docs/manuel-test/30-YEREL-REFERANS.md:451` -> "   ```") ve acilis
# fence'i "3. ```bash" oldugu icin bu islevce hic acilmamistir. Girintiliyi
# saysaydik o kapanis satiri YENI bir blok ACAR ve dosyanin geri kalanindaki
# baglantilar denetimden SESSIZCE duserdi -- yanlis negatif, kapinin yalan
# soylemesi. Sutun 0 kurali bu sinifi kapatir: girintili fence'in ICI taranir
# (yanlis pozitif olabilir, guvenli yon), hicbir sey denetim disi kalmaz.
# Olculdu 2026-08-23: `^ {0,3}` ile 2 dosya dengesiz, sutun 0 ile 1 (o da
# zaten denetim disi olan `sablonu.md`).
_FENCE = re.compile(r"^(`{3,}|~{3,})(.*)$")


def _kod_bloklarini_soy(metin: str) -> str:
    """Fenced kod bloklarinin ICINI bosaltir; satir sayisini KORUR ki bulgu
    mesajlari ve olasi satir referanslari kaymasin. Kapanis fence'i acilisla
    AYNI karakterden ve EN AZ o uzunlukta olmali (CommonMark); bu yuzden
    ```markdown blogunun icindeki `> ```bash` satiri onu kapatmaz -- alintili
    fence satir basinda degildir ve regex sutun 0 ister.
    Saf fonksiyon; testi dogrudan bir dizeyle kosar."""
    cikti: list[str] = []
    acik: str | None = None
    for satir in metin.split("\n"):
        m = _FENCE.match(satir)
        if acik is None:
            if m:
                acik = m.group(1)
                cikti.append("")
                continue
            cikti.append(satir)
        else:
            # Kapanis: ayni karakter, en az ayni uzunluk, arkasinda bilgi dizesi yok.
            if m and m.group(1)[0] == acik[0] and len(m.group(1)) >= len(acik) \
                    and not m.group(2).strip():
                acik = None
            cikti.append("")
    return "\n".join(cikti)


def kirik_baglantilar(kok: pathlib.Path = ROOT) -> list[str]:
    bulunan: list[str] = []
    slug_harita = _site_slug_haritasi(kok)
    for dp, dns, fns in os.walk(kok):
        dns[:] = [d for d in dns if d not in
                  {".git", "node_modules", "artifacts", "bin", "obj", "dist", ".vs"}]
        for fn_ in fns:
            if not (fn_.endswith(".md") or fn_.endswith(".mdx")):
                continue
            p2 = pathlib.Path(dp) / fn_
            rel = p2.relative_to(kok).as_posix()
            if rel.startswith(BAGLANTI_HARIC) or "sablonu.md" in rel:
                continue          # sablonlar `<N>-<AD>.md` gibi yer tutucu tasir
            try:
                metin = p2.read_text(encoding="utf-8")
            except (UnicodeDecodeError, OSError):
                continue
            for m in LINK.finditer(_kod_bloklarini_soy(metin)):
                h = m.group(1).rstrip("\\")
                if "<" in h or "[" in h:
                    continue
                if re.match(r"^[a-z]+:", h) or h.startswith("#"):
                    continue      # dis adres ya da aynı sayfa capasi
                if h.startswith("/"):
                    govde = h[1:].rstrip("/")
                    if not govde or govde.split("/", 1)[0] not in SITE_URETILEN_HEDEF:
                        if re.search(r"\.\w+$", govde):
                            # `llms.txt`, `openapi/agentprism.json` gibi dosya
                            # hedefleri sayfa degil `public/` varligidir.
                            if not (kok / "docs-site" / "public" / govde).exists():
                                bulunan.append(f"{rel} -> {h}")
                        elif govde not in slug_harita:
                            bulunan.append(f"{rel} -> {h}")
                    continue
                if not (p2.parent / h).exists():
                    bulunan.append(f"{rel} -> {h}")
    return bulunan


def _dar_mi(n: int, sinir: int) -> bool:
    return n <= sinir and (sinir - n) / sinir < BOSLUK_ORANI


# --- Buyume projeksiyonu (Faz 77) ----------------------------------------
# Bir sinir ancak ASILDIGINDA fark ediliyordu: DAR bandi "az kaldi" der ama
# "ne kadar kaldi" demez. Faz 76 kapanisinda docs/**.md %1 bostu ve bir
# sonraki faz onu kesin asacakti -- bunu kimse onceden soylemedi. Bu islev
# gecmis faz commit'lerinden bayt/faz turetir ve KALAN FAZ sayisini basar.

def _faz_commitleri(n: int) -> list[str]:
    """Konusu `phase <sayi>` olan son n commit, eskiden yeniye."""
    ham = _git("log", "--format=%H\t%s", "-400")
    bulunan = [s.split("\t", 1)[0] for s in ham if re.match(r"^\S+\tphase \d+", s)]
    return list(reversed(bulunan[:n]))


def _commit_boyutu(commit: str, hedef: str, dizin: bool, ozyinelemeli: bool) -> int:
    """Bir kalemin o commit'teki bayti. Dizinse HARIC dusulur."""
    if not dizin:
        satir = _git("ls-tree", "-l", commit, hedef)
        return int(satir[0].split()[3]) if satir and satir[0].split()[3] != "-" else 0
    toplam = 0
    for s in _git("ls-tree", "-r", "-l", commit, hedef + "/"):
        parca = s.split(None, 4)
        if len(parca) < 5 or parca[3] == "-":
            continue
        yol = parca[4].strip()
        if not yol.endswith(".md"):
            continue
        if any(yol == h or yol.startswith(h + "/") for h in HARIC):
            continue
        if not ozyinelemeli and "/" in yol[len(hedef) + 1:]:
            continue
        toplam += int(parca[3])
    return toplam


def projeksiyon(faz_sayisi: int = 6) -> int:
    commitler = _faz_commitleri(faz_sayisi)
    if len(commitler) < 2:
        print("Projeksiyon: yeterli `phase N` commit'i bulunamadı.")
        return 0

    kalemler: list[tuple[str, str, bool, bool, int]] = [
        (y, y, False, False, s) for y, s in BUTCE.items()
    ] + [
        (f"{y}/{'**' if oz else '*'}.md", y, True, oz, s)
        for (y, oz), s in DIZIN_BUTCESI.items()
    ]

    print(f"Büyüme projeksiyonu — son {len(commitler)} faz commit'i")
    print(f"{'kalem':<30} {'bayt/faz':>9} {'boşluk':>9}  kalan faz")
    uyari = []
    for ad, hedef, dizin, oz, sinir in kalemler:
        ilk = _commit_boyutu(commitler[0], hedef, dizin, oz)
        son = _commit_boyutu(commitler[-1], hedef, dizin, oz)
        if not ilk or not son:
            continue
        hiz = (son - ilk) / (len(commitler) - 1)
        p2 = ROOT / hedef
        simdi = _dizin_boyutu(hedef, oz) if dizin else (len(p2.read_bytes()) if p2.exists() else 0)
        bosluk = sinir - simdi
        if hiz <= 0:
            print(f"{ad:<30} {int(hiz):>9} {bosluk:>9}  büyümüyor")
            continue
        kalan = int(bosluk / hiz)
        isaret = "  🚨" if kalan <= 2 else ("  ⚠️" if kalan <= 6 else "")
        print(f"{ad:<30} {int(hiz):>9} {bosluk:>9}  ~{kalan} faz{isaret}")
        if kalan <= 6:
            uyari.append((ad, kalan))

    if uyari:
        print("\n⚠️  Yakında aşacak:")
        for ad, k in sorted(uyari, key=lambda x: x[1]):
            print(f"   {ad} — ~{k} faz")
        print("   İçeriği SİLME; alan dosyasına veya docs/arsiv/'e taşı.")
    else:
        print("\n✅ Hiçbir kalem altı fazdan yakın değil.")
    return 0


_URETILEN = (
    ("docs/KARARLAR-INDEKS.md", lambda: kararlar_indeksi_uret()),
    ("docs/arsiv/KARARLAR-INDEKS-ARSIV.md", lambda: kararlar_indeksi_arsiv_uret()),
    ("docs/arsiv/KARARLAR-INDEKS-REDDEDILEN.md", lambda: kararlar_reddedilen_uret()),
    ("docs/YOL-HARITASI.md", lambda: yol_haritasi_uret()),
)


# --- Faz dokumani damitmasi (Faz 90) -------------------------------------
# Kapanmis bir faz dokumaninin ~%62'si PLANDIR ve kapanista olur: `Planlanan
# Public API`yi `Gerceklesen` gecersizler, `Bu Faza Baslarken` bir OTURUM
# TALIMATIDIR, `NN.x` is kalemleri planin govdesidir ve sonuc koddadir.
# Damitma bunlari DUSURUR, tam metin git gecmisinde kalir ve `tam_metin_denetle`
# her kosumda cozulebilirligini kanitlar.
#
# `Gerceklesen Public API` ve `Dosya Listesi` de duser: kod TEK KAYNAKTIR
# (`PublicAPI.*.txt`), dokumandaki kopya bayatlar ve AKTIF OLARAK yaniltir;
# `git show --stat <sha>` gercek listeyi birebir uretir.

DAMITMA_ISARETI = "### ⚗️ Damıtılmış kayıt"

_FAZ_DUS = {
    "Bu Faza Başlarken", "Planlanan Public API", "Planlanan Dosya Listesi",
    "Gerçekleşen Public API", "Dosya Listesi", "Riskler", "Testler",
    "Açık Sorular", "Hata Modları ve Testler", "Manuel Kabul Case'leri",
    # olculen takma adlar (Faz 90, 91 dosya tarandi)
    "Riskler — kapanış durumu", "Oluşturulan / Değişen Dosyalar",
    "Planlanan Dosya Listesi (gerçekleşen)", "Dosya Listesi (gerçekleşen)",
}
_FAZ_KAL = {
    "Plandan Sapmalar", "🚨 Plandan Sapmalar", "Bu Fazda Verilen Kararlar",
    "Bu Fazda Verilecek Kararlar", "Denetim Bulguları", "Sonraki Faza Devir Notu",
}
_FAZ_DOD_ADI = "Bitiş Ölçütleri"
_FAZ_AMAC = {"Amaç", "Amaç ve Sonuç"}
_IS_KALEMI = re.compile(r"^\d+\.\d+\b")
AMAC_SINIRI = 400

# Takma ad kurallari -- olculdu (Faz 90): 98 taninmayan H2 adi var ve cogu tek
# bir fazda geciyor. Duz liste bayatlar; desen kalicidir. Yalniz KODDAN ya da
# git'ten BIREBIR yeniden uretilebilen bolumler duser:
#   * imza anlik goruntusu -- `maf-api-kesfi` skill'i reflection'la yeniden uretir
#   * dosya listesi        -- `git show --stat <sha>` birebir verir
#   * planlanan uc listesi -- `docs/openapi/agentprism.json` uretilir
# Geri kalan HER SEY korunur (varsayilan KAL + RAPORLA).
_DUS_DESENLERI = (
    re.compile(r"^(Doğrulanmış|Kullanılan)\s+.*\b(API|İmza)", re.I),
    re.compile(r"^(Gerçekleşen|Oluşturulan|Üretilen|Planlanan)\s+.*Dosya", re.I),
    re.compile(r"^Yeni HTTP Uçları$", re.I),
)


def _duser_mu(ad: str, yalin: str) -> bool:
    if _IS_KALEMI.match(ad) or ad in _FAZ_DUS or yalin in _FAZ_DUS:
        return True
    return any(d.match(ad) or d.match(yalin) for d in _DUS_DESENLERI)


def _plan_dodu_mu(govde: str) -> bool:
    """Isaretlenmemis kutu tasiyan ve HIC yesili olmayan DoD, planin kopyasidir:
    kapanista guncellenmemistir. Yalniz ayni dosyada BASKA bir DoD varsa duser
    (o zaman sonucu digeri tasir). Tek DoD ise KORUNUR -- isaretsiz kutu
    gercek bilgidir (`24-SQLITE.md`: "AOT olculmedi")."""
    return govde.count("- [ ]") > 0 and govde.count("✅") == 0 and "- [x]" not in govde


def _faz_bolumleri(metin: str) -> tuple[str, list[tuple[str, str]]]:
    """(ilk `## ` oncesi baslik blogu, [(H2 adi, govde)]).

    Govde H2 satirini ICERIR; boylece korunan bir bolum bire bir geri yazilir.
    Saf fonksiyon -- git veya dosya sistemi istemez."""
    satirlar = metin.split("\n")
    idx = [i for i, s in enumerate(satirlar) if s.startswith("## ")]
    if not idx:
        return metin, []
    bas = "\n".join(satirlar[: idx[0]])
    bolumler = []
    for a, b in zip(idx, idx[1:] + [len(satirlar)]):
        bolumler.append((satirlar[a][3:].strip(), "\n".join(satirlar[a:b]).rstrip("\n")))
    return bas, bolumler


def _amac_sikis(govde: str, sinir: int = AMAC_SINIRI) -> str:
    """`Amaç` bolumunu CUMLE SINIRINDA kirpar. Karakter sinirinda kesmek
    yarim cumle birakir; damitma okunabilirligi bozmamalidir."""
    satirlar = govde.split("\n")
    bas, govde_sat = satirlar[0], [s for s in satirlar[1:] if s.strip()]
    duz = " ".join(" ".join(govde_sat).split())
    if len(duz.encode()) <= sinir:
        return f"{bas}\n\n{duz}" if duz else bas
    parcalar = re.split(r"(?<=[.!?])\s+", duz)
    tut: list[str] = []
    for c in parcalar:
        aday = " ".join(tut + [c])
        if tut and len(aday.encode()) > sinir:
            break
        tut.append(c)
    return f"{bas}\n\n{' '.join(tut)}"


def _damitma_blogu(tam_sha: str, yol: str, bugun: str) -> str:
    """Kaydin basina konan aciklama + tam metne goturen komutlar.
    Komut satirlarinda `](` deseni YOKTUR -- `kirik_baglantilar()` yanlis
    pozitif uretmesin diye kod blogu icinde ve duz metin olarak yazilir."""
    return (
        f"> {DAMITMA_ISARETI}\n"
        "> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**\n"
        "> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve\n"
        "> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**\n"
        ">\n"
        "> Tam metin — kopyala, çalıştır:\n"
        ">\n"
        "> ```bash\n"
        f"> git show {tam_sha}:{yol}\n"
        "> ```\n"
        ">\n"
        f"> Damıtıldı {bugun} · `scripts/dokuman-bakim.py faz-damit`\n"
    )


def _faz_damit_metni(metin: str, *, tam_sha: str, yol: str,
                     bugun: str) -> tuple[str, list[str]]:
    """(damitilmis metin, uyarilar). Saf fonksiyon; SHA disaridan verilir.

    Taninmayan bir H2 **DUSURULMEZ** -- korunur ve uyari uretir. Toplu bir
    gecişte sessiz kayip yasaktir: olculdu (Faz 90), 103 taninmayan ad /
    4.078 satir var ve iclerinde `## Açık Kalan`, `## 🚨 Ölçülen MAF
    Davranışları` gibi kalici degerli bolumler bulunuyor."""
    if DAMITMA_ISARETI in metin:
        return metin, []                      # idempotent
    bas, bolumler = _faz_bolumleri(metin)
    if not bolumler:
        return metin, [f"{yol}: `## ` bölümü yok, dokunulmadı"]

    uyari: list[str] = []
    tutulan: list[str] = []
    dod_sayisi = sum(1 for ad, _ in bolumler if _FAZ_DOD_ADI in ad)
    for ad, govde in bolumler:
        yalin = re.sub(r"\s*\(.*?\)\s*$", "", ad).strip()
        if _duser_mu(ad, yalin):
            continue
        if _FAZ_DOD_ADI in ad:
            # DoD OZETLENMEZ: isaretsiz kutular gercek bilgidir (olculdu --
            # 11 fazda tek DoD isaretsiz kutu tasiyor ve o kutu "AOT olculmedi"
            # gibi kapanmamis bir isi kaydediyor). Yalniz AYNI dosyada baska
            # bir DoD varken planin isaretsiz kopyasi duser (5 dosya).
            if dod_sayisi > 1 and _plan_dodu_mu(govde):
                continue
            tutulan.append(govde)
            continue
        if ad in _FAZ_AMAC or yalin in _FAZ_AMAC:
            tutulan.append(_amac_sikis(govde))
            continue
        tutulan.append(govde)
        if ad not in _FAZ_KAL and yalin not in _FAZ_KAL:
            uyari.append(f"{yol}: tanınmayan bölüm KORUNDU — `## {ad}`")

    bas = bas.rstrip("\n")
    govde = "\n\n".join(tutulan)
    return f"{bas}\n\n{_damitma_blogu(tam_sha, yol, bugun)}\n---\n\n{govde}\n", uyari


# --- Alt komutlar (Faz 90) -----------------------------------------------
# `add_subparsers(dest=..., required=False)`: bayraksiz cagri ve `--denetle`
# davranis DEGISTIRMEZ. CI sozlesmesi (`ci.yml:131`) aynen calisir.

def _calisma_agaci_temiz(yollar: list[str]) -> str | None:
    """Hedefler icin `git status --porcelain` bos mu. Bos degilse HATA MESAJI
    doner ve hicbir dosyaya dokunulmaz: commit edilmemis bir duzenleme
    damitmada yok olurdu ve git gecmisinde de olmadigi icin GERI GETIRILEMEZDI."""
    ciktı = _git("status", "--porcelain", "--", *yollar)
    if ciktı is None:
        return "git çağrısı başarısız — damıtma çalışma ağacının temiz olduğunu kanıtlayamıyor"
    if ciktı:
        return "çalışma ağacı temiz değil; önce commit et:\n  " + "\n  ".join(ciktı[:10])
    return None


def _faz_dosyalari(secim: list[str]) -> list[pathlib.Path]:
    kaynak = ROOT / "docs" / "arsiv" / "fazlar"
    hepsi = sorted(kaynak.glob("[0-9][0-9]-*.md"))
    if not secim:
        return hepsi
    return [p for p in hepsi if p.name.split("-", 1)[0] in {f"{int(x):02d}" for x in secim}]


def komut_faz_damit(a: argparse.Namespace) -> int:
    """Kapanmis faz dokumanlarini damitilmis kayda indirger."""
    dosyalar = _faz_dosyalari(a.fazlar)
    if not dosyalar:
        print("Eşleşen faz dokümanı yok."); return 1
    rel = [p.relative_to(ROOT).as_posix() for p in dosyalar]
    if not a.kuru:
        hata = _calisma_agaci_temiz(rel)
        if hata:
            print(f"❌ {hata}"); return 1

    bugun = datetime.date.today().isoformat()
    yazilan = 0; toplam_o = toplam_y = 0; uyarilar: list[str] = []
    for p2 in dosyalar:
        yol = p2.relative_to(ROOT).as_posix()
        metin = p2.read_text(encoding="utf-8")
        sha_satir = _git("log", "-1", "--format=%h", "--", yol)
        if not sha_satir:
            uyarilar.append(f"{yol}: tam metin commit'i bulunamadı — ATLANDI"); continue
        yeni, u = _faz_damit_metni(metin, tam_sha=sha_satir[0], yol=yol, bugun=bugun)
        uyarilar += u
        toplam_o += len(metin.encode()); toplam_y += len(yeni.encode())
        if yeni == metin:
            continue
        if not a.kuru:
            p2.write_text(yeni, encoding="utf-8")
        yazilan += 1
        if a.ayrintili:
            print(f"  {yol}  {len(metin.encode()):>7} → {len(yeni.encode()):>6} B")

    print(f"\n{'(kuru) ' if a.kuru else ''}{yazilan}/{len(dosyalar)} dosya · "
          f"{toplam_o:,} → {toplam_y:,} B  (-%{100 - 100 * toplam_y // max(toplam_o, 1)})")
    if uyarilar:
        print(f"\n{len(uyarilar)} uyarı — tanınmayan bölümler KORUNDU (düşürülmedi):")
        for u in uyarilar[:20]:
            print(f"  {u}")
        if len(uyarilar) > 20:
            print(f"  … +{len(uyarilar) - 20}")
    return 0


# --- Kosum kaydi damitmasi (Faz 90) --------------------------------------
# Bir kosum kaydi case basina ~14 satirlik blok tasir ama tasidigi bilgi cogu
# zaman iki satirdir: `Gercek sonuc` + `Durum: ☑ Gecti`. Kosum bittiginde bir
# GECEN case'in ortam ciktisi degerini kaybeder.
#
# ANCAK asimetrik: gecmeyen case'in blogu `kusur-giderme`nin girdisidir ve
# AYNEN korunur. Dahasi olculdu (Faz 90): GECEN 1.061 case'in 254'u ⚠️/🚨/
# "duzeltme"/"kusur"/"HATA-" isareti tasiyor -- ornegin MT-RET-001 "Gecti"
# oldugu halde IKI dokuman duzeltmesi kaydediyor. Duz "gecti -> tek satir"
# kurali bu 383 KB'lik icerigi yok ederdi.

KOSUM_ISARETI = "### ⚗️ Damıtılmış koşum kaydı"

# Bilerek dar tutuldu: "dokuman" ve "eksik" gibi genel kelimeler OLCULDU ve
# yanlis tetikliyordu (`MT-PKG-022 — Her pakette ... XML dokumani var` bir
# dokuman KUSURU degil, dokumana DAIR bir case'tir).
_KOSUM_IZ = ("⚠️", "🚨", "HATA-", "düzelt", "Düzelt", "DÜZELT", "kusur", "Kusur", "KUSUR")
_KOSUM_CASE = re.compile(r"^## (MT-[A-Z]+-\d+)\s*(?:—\s*(.*))?$")
_KOSUM_GECTI = re.compile(r"☑\s*Geçti")


def _kosum_damit_metni(metin: str) -> tuple[str, dict[str, int]]:
    """(damitilmis metin, sayac). Saf fonksiyon.

    Daraltilan: YALNIZ `☑ Geçti` olan VE hicbir eylem isareti tasimayan case.
    Korunan: gecmeyen her case + isaret tasiyan her case, BIRE BIR."""
    if KOSUM_ISARETI in metin:
        return metin, {"daraltilan": 0, "korunan": 0}
    satirlar = metin.split("\n")
    idx = [i for i, x in enumerate(satirlar) if _KOSUM_CASE.match(x)]
    if not idx:
        return metin, {"daraltilan": 0, "korunan": 0}

    bas = "\n".join(satirlar[: idx[0]]).rstrip("\n")
    daralt: list[tuple[str, str]] = []
    koru: list[str] = []
    for a, b in zip(idx, idx[1:] + [len(satirlar)]):
        blok = "\n".join(satirlar[a:b]).rstrip("\n")
        m = _KOSUM_CASE.match(satirlar[a])
        kimlik, baslik = m.group(1), (m.group(2) or "").strip()
        if _KOSUM_GECTI.search(blok) and not any(x in blok for x in _KOSUM_IZ):
            daralt.append((kimlik, baslik))
        else:
            koru.append(blok)

    parcalar = [bas, "", (
        f"> {KOSUM_ISARETI}\n"
        "> Geçen ve **hiçbir düzeltme/kusur işareti taşımayan** case'lerin\n"
        "> `Gerçek sonuç` blokları düştü — bir koşumun ortam çıktısı, koşum\n"
        "> bittiği anda değerini kaybeder. **Geçmeyen** ve **işaret taşıyan**\n"
        "> her case'in bloğu AYNEN durur. Tam metin:\n"
        "> `git log --follow -- <bu dosya>`\n"), "---", ""]
    if daralt:
        parcalar += [f"## Temiz geçen case'ler ({len(daralt)})", "",
                     "| Case | Durum | Başlık |", "|---|---|---|"]
        parcalar += [f"| {k} | ☑ | {b} |" for k, b in daralt]
        parcalar.append("")
    if koru:
        parcalar += [f"## Ayrıntı taşıyan case'ler ({len(koru)})", ""]
        parcalar.append("\n\n".join(koru))
    return "\n".join(parcalar).rstrip("\n") + "\n", {
        "daraltilan": len(daralt), "korunan": len(koru)}


def komut_kosum_damit(a: argparse.Namespace) -> int:
    """Koşum kaydı dizinlerini asimetrik damıtır."""
    dizinler = [pathlib.Path(d) if pathlib.Path(d).is_absolute() else ROOT / d
                for d in a.dizinler]
    dosyalar = sorted(f for d in dizinler for f in d.glob("*.md"))
    if not dosyalar:
        print("Eşleşen koşum kaydı yok."); return 1
    rel = [f.relative_to(ROOT).as_posix() for f in dosyalar]
    if not a.kuru:
        hata = _calisma_agaci_temiz(rel)
        if hata:
            print(f"❌ {hata}"); return 1

    o = y = 0; d_top = k_top = 0
    for f in dosyalar:
        metin = f.read_text(encoding="utf-8")
        yeni, sayac = _kosum_damit_metni(metin)
        o += len(metin.encode()); y += len(yeni.encode())
        d_top += sayac["daraltilan"]; k_top += sayac["korunan"]
        if yeni != metin and not a.kuru:
            f.write_text(yeni, encoding="utf-8")
    print(f"{'(kuru) ' if a.kuru else ''}{len(dosyalar)} dosya · "
          f"{d_top} case daraltıldı · {k_top} case AYNEN korundu")
    print(f"{o:,} → {y:,} B  (-%{100 - 100 * y // max(o, 1)})")
    return 0


# --- Faz 90 kapilari -----------------------------------------------------
# Ucu de `denetle()`ye katilir, boylece `.github/workflows/ci.yml` DEGISMEDEN
# CI'da kosarlar. Ucu de SADECE OKUR -- `--denetle`nin "yazmaz" sozu korunur.

def tazelik_denetle(kok: pathlib.Path = ROOT) -> list[str]:
    """Uretilen DORT dosya kaynagiyla ayni mi. Bellekte yeniden uretip diskle
    karsilastirir; farkliysa bulgu. HICBIR SEY YAZMAZ.

    Bosluk (Faz 90'da olculdu): CI yalniz `--denetle` kosuyordu, URETIM modunu
    hic kosmuyordu. `YOL-HARITASI.md` veya karar indeksleri bayat commit
    edilebilir ve HICBIR kapi bunu soylemezdi -- "tek kaynak, elle yazilmaz"
    diyen dosyanin bayat olmasi tam da kapinin yalan soylemesidir.
    `docs-site/scripts/build-agent-map.mjs --check` (ci.yml:118) ayni deseni
    agent haritasi icin zaten uyguluyordu; dokuman uretecinin esdegeri yoktu."""
    bulunan: list[str] = []
    for rel, uret in _URETILEN:
        hedef = kok / rel
        try:
            diskteki = hedef.read_text(encoding="utf-8") if hedef.exists() else ""
        except OSError:
            continue
        if diskteki != uret():
            bulunan.append(f"{rel} — kaynakla ayni degil; `python3 scripts/dokuman-bakim.py` calistir")
    return bulunan


_GECMIS_BASLIK = re.compile(r"^#{2,3}\s*(K-\d+)(?:\s*/\s*(K-\d+))?", re.M)
_KARAR_NO = re.compile(r"^\| \*\*(K-\d+)")


def gecmis_isaretci_denetle(kok: pathlib.Path = ROOT) -> list[str]:
    """`KARARLAR-GECMISI.md`'ye yollayan her karar satiri icin o karara ait
    `### K-NNN` basligi GECMISI'de GERCEKTEN var mi.

    Karar numarasi satirin KENDI `| **K-NNN` onekinden okunur, isaretci
    METNINDEN degil: isaretci ifadesi standart DEGILDIR -- olculdu (Faz 90),
    en az bes farkli yazim kullaniliyor ("Tam gerekce:", "Olcumun tamami:",
    "Tam anlati:", "Ayrinti:", "Olcumler ve birlestirmenin neden dustugu:").
    Metne bagli bir denetim bunlarin cogunu SESSIZCE kacirirdi.

    `kirik_baglantilar()` bunu goremez: baglanti DOSYA duzeyindedir, dosya
    vardir, capa hic denetlenmez. Olculdu: 18 sarkan isaretci -- okuyucu var
    olmayan bir gerekceye yollaniyordu."""
    kararlar = kok / "docs" / "KARARLAR.md"
    gecmis = kok / "docs" / "arsiv" / "KARARLAR-GECMISI.md"
    if not kararlar.exists() or not gecmis.exists():
        return []
    var: set[str] = set()
    for m in _GECMIS_BASLIK.finditer(gecmis.read_text(encoding="utf-8")):
        var.add(m.group(1))
        if m.group(2):
            var.add(m.group(2))
    eksik: list[str] = []
    for satir in kararlar.read_text(encoding="utf-8").splitlines():
        # CIPLAK atif ("Kural `...GECMISI.md` satir 1622'de yazilidir") bir
        # "tam gerekce orada" sozu degildir; yalniz BAGLANTI verilen satir
        # okuyucuyu oraya yollar ve yalniz o satir capa borcludur.
        if "](arsiv/KARARLAR-GECMISI.md)" not in satir:
            continue
        m = _KARAR_NO.match(satir)
        if m and m.group(1) not in var:
            eksik.append(m.group(1))
    return [f"docs/KARARLAR.md -> arsiv/KARARLAR-GECMISI.md#{k} (baslik yok)"
            for k in sorted(set(eksik), key=lambda k: int(k[2:]))]


_TAM_METIN = re.compile(r"git show ([0-9a-f]{7,40}):(\S+\.md)")


def tam_metin_denetle(kok: pathlib.Path = ROOT) -> list[str]:
    """Damitilmis her kayittaki `git show <sha>:<yol>` gercekten cozuluyor mu.

    Damitma tam metni SILMEZ, git gecmisine birakir. Git gecmisine guvenmek
    ancak bir kapi onu HER kosumda kanitliyorsa mesrudur: `filter-branch`,
    agresif `gc` veya sig bir klon SHA'yi gecersizleyebilir. `fetch-depth: 0`
    (ci.yml:35) MinVer yuzunden zaten zorunludur -- beklenmedik bir sigorta."""
    bulunan: list[str] = []
    kaynak = kok / "docs" / "arsiv" / "fazlar"
    if not kaynak.exists():
        return []
    for dosya in sorted(kaynak.glob("*.md")):
        try:
            metin = dosya.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        for sha, yol in set(_TAM_METIN.findall(metin)):
            if _git("cat-file", "-e", f"{sha}:{yol}") is None:
                bulunan.append(f"{dosya.relative_to(kok).as_posix()} -> {sha}:{yol} çözülmüyor")
    return bulunan


def denetle() -> int:
    hata = 0
    dar = 0
    print(f"{'dosya':<30} {'bayt':>9} {'bütçe':>9}  {'~token':>8}")
    for baslik, butce in (
        ("Başlangıç bağlamı — her oturum", BASLANGIC_BUTCESI),
        ("Sorgu bağlamı — gerektiğinde", SORGU_BUTCESI),
        ("Yönetim ledger'ı — yalnız aramada", YONETIM_BUTCESI),
    ):
        print(f"\n{baslik}")
        for yol, sinir in butce.items():
            p = ROOT / yol
            if not p.exists():
                print(f"{yol:<30} {'YOK':>9}")
                continue
            s, asti = _satir(yol, len(p.read_bytes()), sinir)
            hata |= asti
            dar += int(not asti and _dar_mi(len(p.read_bytes()), sinir))
            print(s)

    # Faz 77: bu dongu DAR bandini uygulamiyordu -- yalniz `n > butce`
    # bakiyordu, bu yuzden 16000/16000 bile "ok" yaziyordu. BES dosya ayni
    # anda duvara dayanmisti ve rapor bunu hic soylemedi. Artik digerleriyle
    # ayni `_satir()` yardimcisini kullanir.
    print("\nAlan hafızası — yalnız ilgili alan")
    for p in sorted((ROOT / "docs" / "hafiza").glob("*.md")):
        if p.name == "00-INDEKS.md":
            continue   # alan dosyasi degil, yonlendirme; SORGU_BUTCESI'nde
                       # ayrica olculur -- burada da sayilsa cift raporlanirdi.
        s, asti = _satir(f"  {p.name}", len(p.read_bytes()), HAFIZA_DOSYA_BUTCESI, genislik=30)
        if asti:
            hata = 1
            s += " — ikiye böl"
        print(s)
        dar += int(not asti and _dar_mi(len(p.read_bytes()), HAFIZA_DOSYA_BUTCESI))

    print(f"\nCanlı geliştirme dokümanları (hariç: {', '.join(HARIC)})")
    print(f"{'dizin':<30} {'bayt':>9} {'bütçe':>9}  {'~token':>8}")
    for (yol, ozyinelemeli), sinir in DIZIN_BUTCESI.items():
        ad = f"{yol}/{'**' if ozyinelemeli else '*'}.md"
        n = _dizin_boyutu(yol, ozyinelemeli)
        s, asti = _satir(ad, n, sinir)
        hata |= asti
        dar += int(not asti and _dar_mi(n, sinir))
        print(s)

    haric_toplam = sum(_dizin_boyutu(h, True, haric_uygula=False) for h in HARIC)
    print(f"  (denetim dışı arşiv + koşum kaydı: {haric_toplam} B — sınırı etkilemez)")

    faz_bulgulari = kapanmis_faz_bulgulari()
    print(f"\nKök faz yaşam döngüsü: "
          f"{'❌ ' + str(len(faz_bulgulari)) + ' bulgu' if faz_bulgulari else '✅ temiz'}")
    for s in faz_bulgulari:
        print(f"  {s}")
    hata |= int(bool(faz_bulgulari))

    sayi, karar_bulgulari = kararlar_denetle()
    print(f"\nKarar defteri (§2, {sayi} kalem): "
          f"{'❌ ' + str(len(karar_bulgulari)) + ' bulgu' if karar_bulgulari else '✅ temiz'}")
    for s in karar_bulgulari:
        print(f"  {s}")
    hata |= int(bool(karar_bulgulari))

    kirik = kirik_baglantilar()
    print(f"\nKırık bağlantı: {len(kirik)}")
    for s in kirik[:10]:
        print(f"  {s}")
    if len(kirik) > 10:
        print(f"  … +{len(kirik) - 10}")
    # Faz 80, bağımsız denetim: bu sayaç eskiden `hata`'ya hiç katılmıyordu --
    # `--denetle` kırık bağlantı sayısından BAĞIMSIZ olarak çıkış kodu 0
    # veriyordu. Tam da bu fazın düzelttiği "kapı sessizce yanıltıyor" kusur
    # sınıfının kendisiydi; CI'ya bağlanmadan önce yakalandı.
    hata |= int(bool(kirik))

    for ad, bulgular in (
        ("Üretilen dosya tazeliği", tazelik_denetle()),
        ("Karar gerekçesi işaretçisi", gecmis_isaretci_denetle()),
        ("Damıtılmış kayıt tam metni", tam_metin_denetle()),
    ):
        print(f"\n{ad}: {'✅ temiz' if not bulgular else f'{len(bulgular)} bulgu'}")
        for b in bulgular[:10]:
            print(f"  {b}")
        if len(bulgular) > 10:
            print(f"  … +{len(bulgular) - 10}")
        hata |= int(bool(bulgular))

    baslangic_toplami = sum(
        len((ROOT / y).read_bytes()) for y in BASLANGIC_BUTCESI if (ROOT / y).exists())
    sorgu_toplami = sum(
        len((ROOT / y).read_bytes()) for y in SORGU_BUTCESI if (ROOT / y).exists())
    ledger_toplami = sum(
        len((ROOT / y).read_bytes()) for y in YONETIM_BUTCESI if (ROOT / y).exists())
    print(f"\nBaşlangıç bağlamı: {baslangic_toplami} B (~{baslangic_toplami * 10 // 24} token)")
    print(f"Sorgu bağlamı: {sorgu_toplami} B (~{sorgu_toplami * 10 // 24} token) — başlangıçta okunmaz")
    print(f"Yönetim ledger'ı: {ledger_toplami} B (~{ledger_toplami * 10 // 24} token) — başlangıçta okunmaz")
    # Faz 77: ozet eskiden DAR'i hic saymiyordu ve YEDI kalem darken
    # "✅ Bütçeler içinde" yaziyordu. Sikisma bu yuzden sessizce birikti.
    if hata:
        print("\n❌ Denetim kırmızı. Bütçe aşıldıysa içeriği SİLME — alan dosyasına\n   veya docs/arsiv/'e taşı. Karar defteri bulgusu varsa numarayı/satırı düzelt.")
    elif dar:
        print(f"\n⚠️  Bütçeler içinde ama {dar} kalem DAR (%{BOSLUK_ORANI * 100:.0f}'ten az boşluk).")
        print("   Bunlar bir sonraki fazda aşabilir — şimdi taşı, aşınca değil.")
        print("   Kalan faz tahmini için: python3 scripts/dokuman-bakim.py --projeksiyon")
    else:
        print("\n✅ Bütçeler içinde.")
    return int(hata)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--denetle", action="store_true", help="yalnız denetle, üretme")
    ap.add_argument("--projeksiyon", action="store_true",
                    help="geçmiş faz commit'lerinden bayt/faz türetip kalan fazı bas")
    ap.add_argument("--site-denetle", action="store_true",
                    help="docs-site senkronunu denetle (faz kapanışı)")
    ap.add_argument("--taban", help="fazın başladığı commit; site denetimi bu aralığa bakar")
    ap.add_argument("--site-gerekce-yazildi", action="store_true",
                    help="site güncellemesi gerekmiyor; gerekçe faz dokümanına yazıldı")

    # `required=False` (varsayilan): alt komut verilmezse ESKI akis aynen kosar.
    alt = ap.add_subparsers(dest="komut")
    fd = alt.add_parser("faz-damit", help="kapanmış faz dokümanını damıtılmış kayda indirge")
    fd.add_argument("fazlar", nargs="*", help="faz numaraları; boşsa tümü")
    fd.add_argument("--kuru", action="store_true", help="yazma, yalnız ne olacağını bas")
    fd.add_argument("--ayrintili", action="store_true", help="dosya dosya boyut bas")
    fd.set_defaults(_calistir=komut_faz_damit)

    kd = alt.add_parser("kosum-damit", help="koşum kaydını asimetrik damıt")
    kd.add_argument("dizinler", nargs="+", help="koşum dizini/dizinleri")
    kd.add_argument("--kuru", action="store_true", help="yazma, yalnız ne olacağını bas")
    kd.set_defaults(_calistir=komut_kosum_damit)

    a = ap.parse_args()
    if getattr(a, "_calistir", None):
        return a._calistir(a)

    if a.site_denetle:
        return site_denetle(a.taban, a.site_gerekce_yazildi)

    if a.projeksiyon:
        return projeksiyon()

    if not a.denetle:
        for rel, uret in _URETILEN:
            hedef = ROOT / rel
            yeni = uret()
            eski = hedef.read_text(encoding="utf-8") if hedef.exists() else ""
            hedef.write_text(yeni, encoding="utf-8")
            print("değişmedi" if eski == yeni else "yeniden üretildi", f"→ {hedef.relative_to(ROOT)}")
        print()

    return denetle()


if __name__ == "__main__":
    sys.exit(main())
