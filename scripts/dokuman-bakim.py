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

    # Faz kapanışında: kullanıcıya dönük yüzey değişti mi, site güncellendi mi?
    python3 scripts/dokuman-bakim.py --site-denetle --taban <faz öncesi commit>

Bütçe aşılırsa çıkış kodu 1'dir.
"""
from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
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
# Kural: fazin dokundugu kaynak yolu kullaniciya donuk bir yuzeyse, site'nin
# ELLE yazilan sayfalarindan en az biri degismelidir. `api/` ve `http-api/`
# URETILIR (npm run generate) ve commit EDILMEZ; oradaki is kodda yasar
# (XML dokumani, .WithTags/.Produces ustverisi), bu yuzden site degisikligi
# sayilirken haric tutulurlar.
SITE_URETILEN = ("docs-site/src/content/docs/api/", "docs-site/src/content/docs/http-api/")

SITE_KURALLARI: tuple[tuple[str, tuple[str, ...], str], ...] = (
    (r"^src/AgentPrism\.AspNetCore/(Endpoints|OpenAICompat|A2A|McpServer)/",
     ("http-api.md",), "HTTP yuzeyi degisti"),
    (r"^src/AgentPrism\.AspNetCore/(Security|Tenancy)/",
     ("getting-started/security.md", "concepts/governance.md"), "guvenlik/kiraci sinirlari degisti"),
    (r"^src/AgentPrism\.UI/frontend/src/(screens|components)/",
     ("ui.md",), "ekran veya bilesen degisti (ekran goruntusu de gerekebilir)"),
    (r"^src/AgentPrism\.(Abstractions|Core)/",
     ("concepts/",), "cekirdek kavram yuzeyi degisti"),
    (r"^src/AgentPrism\.Workflows/",
     ("concepts/workflows.md",), "workflow yurutmesi degisti"),
    (r"^src/AgentPrism\.(PostgreSql|SqlServer|Sqlite|Sql\.Shared)/",
     ("getting-started/persistence.md",), "kalicilik katmani degisti"),
    (r"^src/AgentPrism\.(OpenAI|Anthropic|Google|Azure|Voice)/",
     ("getting-started/first-agent.md",), "model saglayicisi degisti"),
    (r"^src/AgentPrism\.Templates/",
     ("getting-started/index.md",), "proje sablonu degisti"),
    (r"^src/AgentPrism[^/]*/[^/]*\.csproj$",
     ("packages.md",), "paket tanimi degisti"),
    (r"^src/AgentPrism[^/]*/README\.md$",
     ("packages.md",), "paket README'si degisti"),
)


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

    # Faz 77: kapanmis fazlar (00-59) `docs/arsiv/fazlar/` altina tasindi --
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
        durum = next((v for k, v in kisalt.items() if ham.startswith(k)), ham[:40] or "?")

        no = p.name[:2].lstrip("0") or "0"
        yol = p.relative_to(ROOT / "docs").as_posix()
        satirlar.append(f"| [{no}]({yol}) | {baslik} | {durum} |")

    return "\n".join(
        [
            "# Faz Yol Haritası",
            "",
            "> **Üretilen dosya. Elle düzenleme.** Kaynak: her fazın kendi",
            "> `docs/NN-*.md` dosyasındaki `> **Durum:**` satırı.",
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


def _git(*args: str) -> list[str]:
    r = subprocess.run(["git", *args], cwd=ROOT, capture_output=True, text=True, check=False)
    if r.returncode != 0:
        return []
    return [s for s in r.stdout.splitlines() if s.strip()]


def _degisen_dosyalar(taban: str | None) -> list[str]:
    """Fazin dokundugu her dosya: taban..HEAD + calisma agaci + izlenmeyenler.

    Izlenmeyenler dahildir cunku yeni bir site sayfasi HENUZ commit edilmemis
    olabilir; onu gormezsek denetim yanlis yere kirmizi verir."""
    yollar: set[str] = set()
    if taban:
        yollar.update(_git("diff", "--name-only", f"{taban}...HEAD"))
    yollar.update(_git("diff", "--name-only", "HEAD"))
    yollar.update(s[3:].strip('"') for s in _git("status", "--porcelain") if s.startswith("??"))
    return sorted(yollar)


def site_denetle(taban: str | None, gerekce_yazildi: bool) -> int:
    degisen = _degisen_dosyalar(taban)
    if not degisen:
        print("docs-site senkronu: değişiklik yok (taban verilmedi mi?).")
        return 0

    tetiklenen = []
    for desen, sayfalar, neden in SITE_KURALLARI:
        vuran = [y for y in degisen if re.search(desen, y)]
        if vuran:
            tetiklenen.append((sayfalar, neden, vuran))

    site_degisti = [
        y for y in degisen
        if y.startswith("docs-site/src/content/docs/") and not y.startswith(SITE_URETILEN)
    ]

    print(f"docs-site senkronu — {len(degisen)} değişen dosya")
    if not tetiklenen:
        print("  Kullanıcıya dönük yüzey değişmedi. Site güncellemesi gerekmiyor.")
        return 0

    print(f"{'gözden geçirilecek sayfa':<40} {'neden':<45} örnek")
    for sayfalar, neden, vuran in tetiklenen:
        print(f"  {' · '.join(sayfalar):<38} {neden:<45} {vuran[0]}")

    if site_degisti:
        print(f"\n✅ Site {len(site_degisti)} sayfada değişti: {', '.join(site_degisti[:3])}")
        print("   Yine de yukarıdaki her satırın karşılığı yazıldı mı, göz at.")
        return 0

    if gerekce_yazildi:
        print("\n⚠️  Site değişmedi; gerekçe faz dokümanına yazıldı (--site-gerekce-yazildi).")
        return 0

    print("\n❌ Kullanıcıya dönük yüzey değişti fakat docs-site/ hiç değişmedi.")
    print("   Ya siteyi güncelle ya gerekçesini faz dokümanına yazıp")
    print("   --site-gerekce-yazildi ile geç. Sessizce atlama.")
    return 1


def denetle() -> int:
    hata = 0
    print("Sıcak yol doküman bütçesi")
    print(f"{'dosya':<30} {'bayt':>9} {'bütçe':>9}  {'~token':>8}")
    for yol, sinir in BUTCE.items():
        p = ROOT / yol
        if not p.exists():
            print(f"{yol:<30} {'YOK':>9}")
            continue
        s, asti = _satir(yol, len(p.read_bytes()), sinir)
        hata |= asti
        print(s)

    print("\nAlan hafıza dosyaları")
    for p in sorted((ROOT / "docs" / "hafiza").glob("*.md")):
        n = len(p.read_bytes())
        if n > HAFIZA_DOSYA_BUTCESI:
            hata = 1
            print(f"  {p.name:<28} {n:>9} {HAFIZA_DOSYA_BUTCESI:>9}  AŞTI — ikiye böl")
        else:
            print(f"  {p.name:<28} {n:>9} {HAFIZA_DOSYA_BUTCESI:>9}  ok")

    print(f"\nDizin bütçeleri (hariç: {', '.join(HARIC)})")
    print(f"{'dizin':<30} {'bayt':>9} {'bütçe':>9}  {'~token':>8}")
    for (yol, ozyinelemeli), sinir in DIZIN_BUTCESI.items():
        ad = f"{yol}/{'**' if ozyinelemeli else '*'}.md"
        s, asti = _satir(ad, _dizin_boyutu(yol, ozyinelemeli), sinir)
        hata |= asti
        print(s)

    haric_toplam = sum(_dizin_boyutu(h, True, haric_uygula=False) for h in HARIC)
    print(f"  (denetim dışı arşiv + koşum kaydı: {haric_toplam} B — sınırı etkilemez)")

    toplam = sum(len((ROOT / y).read_bytes()) for y in BUTCE if (ROOT / y).exists())
    print(f"\nOturum başı sıcak yol toplamı: {toplam} B (~{toplam * 10 // 24} token)")
    if hata:
        print("\n❌ Bütçe aşıldı. İçeriği SİLME — alan dosyasına veya docs/arsiv/'e taşı.")
    else:
        print("\n✅ Bütçeler içinde.")
    return int(hata)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--denetle", action="store_true", help="yalnız denetle, üretme")
    ap.add_argument("--site-denetle", action="store_true",
                    help="docs-site senkronunu denetle (faz kapanışı)")
    ap.add_argument("--taban", help="fazın başladığı commit; site denetimi bu aralığa bakar")
    ap.add_argument("--site-gerekce-yazildi", action="store_true",
                    help="site güncellemesi gerekmiyor; gerekçe faz dokümanına yazıldı")
    a = ap.parse_args()

    if a.site_denetle:
        return site_denetle(a.taban, a.site_gerekce_yazildi)

    if not a.denetle:
        for hedef, uret in (
            (ROOT / "docs" / "KARARLAR-INDEKS.md", kararlar_indeksi_uret),
            (ROOT / "docs" / "arsiv" / "KARARLAR-INDEKS-ARSIV.md", kararlar_indeksi_arsiv_uret),
            (ROOT / "docs" / "arsiv" / "KARARLAR-INDEKS-REDDEDILEN.md", kararlar_reddedilen_uret),
            (ROOT / "docs" / "YOL-HARITASI.md", yol_haritasi_uret),
        ):
            yeni = uret()
            eski = hedef.read_text(encoding="utf-8") if hedef.exists() else ""
            hedef.write_text(yeni, encoding="utf-8")
            print("değişmedi" if eski == yeni else "yeniden üretildi", f"→ {hedef.relative_to(ROOT)}")
        print()

    return denetle()


if __name__ == "__main__":
    sys.exit(main())
