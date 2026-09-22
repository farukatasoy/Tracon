#!/usr/bin/env python3
"""Manuel kabul turunun tazelik ölçümü — tur AÇILMADAN önce koşulur.

`docs/manuel-test/` spec'i turdan bağımsızdır ve her faz ona case ekler. Bayat
olan spec değil, **koşum kaydıdır**: iki tur arasında yüzlerce case eklenir,
onlarca case'in metni değişir ve metni hiç değişmeyen case'lerin ALTINDAKİ KOD
değişir. Üçüncüsü en tehlikelisidir — case yanıltıcı biçimde eski hâliyle durur.

Bu betik o üç sınıfı ayırır ve turu körlemesine numara sırasıyla koşmak yerine
risk sırasına dizer:

  yeni     — taban turdan sonra eklendi, hiç koşulmadı
  değişti  — metni değişti, taban turun sonucu geçersiz
  sessiz   — metni aynı; riski aile kaynağına giren commit sayısıdır

Ölçüm ayrıca `CapabilityEntryPoints` kuralıyla (K-509) `src/*/PublicAPI.*.txt`
dosyalarını tarar ve manuel-test setinde hiç anılmayan giriş noktalarını verir.

Kullanım:
    python3 scripts/manuel-test-tazelik.py --taban <onceki turun son commit'i>
    python3 scripts/manuel-test-tazelik.py --taban 12fb6477 --kuru
    python3 scripts/manuel-test-tazelik.py --taban 12fb6477 --kosum docs/manuel-test/kosumlar/2026-09-16

Çözülemeyen her kaynak yolu RAPORLANIR. Sessiz geçmek ölçümü sessizce yanlış
yapar: `00-INDEKS.md` §7'nin `Kaynak` sütunu bayatlarsa kod kayması olduğundan
küçük görünür ve aile risk sırasında hak ettiği yerin altına düşer.
"""
from __future__ import annotations

import argparse
import dataclasses
import hashlib
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
MT = ROOT / "docs" / "manuel-test"
INDEKS = MT / "00-INDEKS.md"
KOSUMLAR = MT / "kosumlar"

# Bağımlılık zinciri (`00-INDEKS.md` §7 "Önerilen koşum sırası"). Bu beşi risk
# sırası EZEMEZ: zincir kırılırsa sonraki hiçbir aile anlamlı sonuç vermez.
ZINCIR = ("01", "02", "03", "05", "07")

# Kod kaymasının risk skoruna katkısı buradan sonra doymuş sayılır. 85 commit
# ile 200 commit arasındaki fark aileyi daha fazla riskli yapmaz; ikisi de
# "baştan oku" demektir.
KAYMA_TAVANI = 40

# Oturum bütçesi (`manuel-test-kosumu` SKILL.md §3). Arayüz aileleri Playwright
# ile koşar ve her case bir anlık görüntü + konsol kontrolü ister; bu yüzden
# oturum başına case sayısı yarıya iner.
ARAYUZ_AILELERI = ("09", "10", "11")
CASE_PER_OTURUM_ARAYUZ = 18
CASE_PER_OTURUM_DIGER = 30

# Aynı anda koşan şerit sayısı. Zincir bunun DIŞINDADIR: kapı ailelerini tek
# şerit sırayla koşar, paralel şeritler ancak zincir yeşilken açılır.
SERIT_SAYISI = 4

# Arayüz ailelerinin `Kaynak` sütunu frontend yollarını GÖRELİ yazar
# (`screens/run*.tsx`). Kökü budur.
FRONTEND = "src/Tracon.UI/frontend/src"

# Kaynak sütunu ayar anahtarı da yazar (`AgentGraph.MaxDuration`). Nokta tek
# başına yol işareti değildir; belirteç ya eğik çizgi ya da bilinen bir kaynak
# uzantısı taşımalıdır.
KAYNAK_UZANTILARI = (
    ".cs", ".ts", ".tsx", ".mjs", ".js", ".py", ".json", ".sql", ".css",
    ".props", ".targets", ".csproj", ".slnf", ".md", ".yml", ".svg",
)

CASE_BASLIK = re.compile(r"^#{2,4} +`?(MT-[A-Z0-9]+-\d+)")
CASE_TABLO = re.compile(r"^\|\s*\d+\s*\|\s*`?(MT-[A-Z0-9]+-\d+)`?")
AILE_SATIRI = re.compile(
    r"^\| (?P<no>\d+) \| \[`(?P<dosya>[^`]+)`\][^|]*\| `(?P<kod>[A-Z]+)` \|"
    r"(?P<fazlar>[^|]*)\|(?P<kaynak>[^|]*)\| \*\*(?P<hedef>\d+)\*\* \|")

# `CapabilityEntryPoints.cs` ile AYNI kural (K-509). İkisi ayrışırsa bu betik
# kapsanmış bir giriş noktasını eksik sanır.
KAYIT_ALICILARI = frozenset({
    "Tracon.ITraconBuilder",
    "Microsoft.Extensions.DependencyInjection.IServiceCollection",
    "Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder",
    "Microsoft.Extensions.Hosting.IHostApplicationBuilder",
    "Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
})
BUILDER_UYESI = re.compile(r"^Tracon\.ITraconBuilder\.(?P<ad>[A-Za-z0-9_]+)")
UZANTI_METODU = re.compile(
    r"^static [A-Za-z0-9_.]+\.(?P<ad>(?:Add|Use|Map)[A-Za-z0-9_]*)"
    r"(?:<[^(]*>)?\(this (?P<alici>[A-Za-z0-9_.]+)")


# --------------------------------------------------------------------------
# Case imzası
# --------------------------------------------------------------------------

def _kosum_kaydini_dus(satirlar: list[str]) -> list[str]:
    """`Gerçek sonuç` ve `Durum:` bloklarını atar.

    K-414 spec ile koşum kaydını ayırdı ve bu blokları spec dosyalarından
    SİLDİ. Atılmazlarsa taban turdaki HER case "değişti" görünür — ölçüldü:
    1096 sahte değişiklik, gerçek sayı 267.
    """
    temiz: list[str] = []
    atla = False
    for satir in satirlar:
        kirpik = satir.strip()
        if kirpik.startswith("**Gerçek sonuç**"):
            atla = True
            continue
        if kirpik.startswith("**Durum:**"):
            atla = False
            continue
        if atla:
            continue
        temiz.append(satir)
    return temiz


# Tarihsel kimlik yeniden adlandırmaları. Bir case'in yalnız ADLARI değiştiyse
# kanıt değeri değişmemiştir: aynı davranışı yeni adla sınar. Normalize
# edilmezse tek bir rename bütün seti "değişti" kovasına atar ve ölçüm
# kullanılamaz hâle gelir — ölçüldü: Faz 162'nin ürün adı 1096 sahte değişiklik,
# agent/workflow adları 98 sahte değişiklik üretiyordu.
#
# 🚨 Buraya yalnız KANITLANMIŞ bir rename girer: eski ad `src/` ve `samples/`
# içinde artık yaşamamalı ve eşleme 1:1 olmalıdır. Anlam değiştiren bir
# düzenlemeyi buraya yazmak, gerçek bir değişikliği "sessiz" kovasına saklar.
YENIDEN_ADLANDIRMALAR = {
    # Faz 162 — ürün adı
    "agentprism": "tracon",
    "agent prism": "tracon",
    # Faz 162 — örnek uygulamanın agent ve workflow adları
    "arastirmaci": "researcher",
    "yonlendirici": "router",
    "ozetleyici": "summarizer",
    "cevirmen": "translator",
    "openrouter-destek": "openrouter-support",
    "claude-destek": "claude-support",
    "claude-dusunen": "claude-thinking",
    "gemini-destek": "gemini-support",
    "gemini-kati-filtre": "gemini-strict-filter",
    "azure-destek": "azure-support",
    "sesli-asistan": "voice-assistant",
    "bilgi-asistani": "knowledge-assistant",
    "ozetle-ve-cevir": "summarize-and-translate",
    "ozetle-ve-onayla": "summarize-and-approve",
}

# Uzun olan önce: kısa bir ad uzun bir adın parçası olsaydı önce o eşleşirdi.
_RENAME_DESEN = re.compile("|".join(
    re.escape(k) for k in sorted(YENIDEN_ADLANDIRMALAR, key=len, reverse=True)))


def _imza(satirlar: list[str]) -> str:
    """Case gövdesinin normalize edilmiş hash'i."""
    metin = "\n".join(_kosum_kaydini_dus(satirlar)).lower()
    metin = _RENAME_DESEN.sub(lambda m: YENIDEN_ADLANDIRMALAR[m.group(0)], metin)
    return hashlib.sha1(re.sub(r"\s+", " ", metin).strip().encode()).hexdigest()[:12]


def case_imzalari(metin: str) -> dict[str, str]:
    """Dosyadaki her case'in imzası.

    İki biçim vardır: aile 01–30 case'i BAŞLIK yazar, aile 31–36 TABLO SATIRI.
    İkincisi `manuel_test_sayim_kaymasi` kapısına Faz 167'ye kadar görünmezdi.
    """
    satirlar = metin.split("\n")
    imzalar: dict[str, str] = {}

    basliklar = [i for i, s in enumerate(satirlar) if CASE_BASLIK.match(s)]
    if basliklar:
        sinirlar = basliklar + [len(satirlar)]
        for k in range(len(sinirlar) - 1):
            ad = CASE_BASLIK.match(satirlar[sinirlar[k]]).group(1)
            imzalar[ad] = _imza(satirlar[sinirlar[k]:sinirlar[k + 1]])

    for satir in satirlar:
        eslesme = CASE_TABLO.match(satir)
        if eslesme:
            imzalar[eslesme.group(1)] = _imza([satir])

    return imzalar


# --------------------------------------------------------------------------
# Git
# --------------------------------------------------------------------------

def _git(*argumanlar: str) -> subprocess.CompletedProcess[str]:
    return subprocess.run(["git", *argumanlar], cwd=ROOT,
                          capture_output=True, text=True)


def _tabanda_oku(taban: str, yol: str) -> str | None:
    sonuc = _git("show", f"{taban}:{yol}")
    return sonuc.stdout if sonuc.returncode == 0 else None


def commit_sayisi(taban: str, yollar: list[str]) -> int:
    """`taban..HEAD` arasında bu yollara dokunan commit sayısı.

    Boş yol listesi 0 döner, tüm commit'ler DEĞİL: yolu çözülemeyen bir aile
    aksi hâlde turun tamamını kendi kaymasıymış gibi gösterir.
    """
    if not yollar:
        return 0
    sonuc = _git("rev-list", "--count", f"{taban}..HEAD", "--", *yollar)
    return int(sonuc.stdout.strip() or 0) if sonuc.returncode == 0 else 0


def toplam_commit(taban: str) -> int:
    sonuc = _git("rev-list", "--count", f"{taban}..HEAD")
    return int(sonuc.stdout.strip() or 0) if sonuc.returncode == 0 else 0


def _tekil_temel_ad(ad: str) -> str | None:
    """Çıplak bir dosya adını repo genelinde TEK eşleşmesi varsa çözer.

    Sütun kardeş dosyayı çıplak yazar ama kardeş her zaman aynı dizinde
    değildir: `17-EVAL`'in hücresi `screens/eval*.tsx` yazdıktan sonra
    `feedback-control.tsx` yazar ve o dosya `components/` altındadır.

    🚨 Çoklu eşleşmede çözmez. Yanlış dosyayı seçmek kod kaymasını sessizce
    yanlış ölçer; çözülememek en azından §5'te görünür.
    """
    if "/" in ad or "*" in ad:
        return None
    eslesmeler = [s for s in _git("ls-files", f"*/{ad}").stdout.split("\n") if s]
    return eslesmeler[0] if len(eslesmeler) == 1 else None


def _yol_var_mi(yol: str) -> bool:
    if (ROOT / yol).exists():
        return True
    if "*" not in yol:
        return False
    return bool(_git("ls-files", "--", yol).stdout.strip())


# --------------------------------------------------------------------------
# Kaynak yolu çözümü
# --------------------------------------------------------------------------

def _paket_kisaltmalari() -> dict[str, str]:
    """`Core` → `src/Tracon.Core`. Kaynak sütunu paket adını kısaltarak yazar."""
    harita: dict[str, str] = {}
    for dizin in sorted((ROOT / "src").glob("Tracon*")):
        if not dizin.is_dir():
            continue
        harita[dizin.name] = f"src/{dizin.name}"
        harita[dizin.name.removeprefix("Tracon.")] = f"src/{dizin.name}"
    return harita


PAKETLER = _paket_kisaltmalari()


def _brace_ac(belirtec: str) -> list[str]:
    """`Dizin/{A,B}.cs` → `Dizin/A.cs`, `Dizin/B.cs`."""
    eslesme = re.search(r"\{([^{}]*)\}", belirtec)
    if not eslesme:
        return [belirtec]
    acilmis: list[str] = []
    for parca in eslesme.group(1).split(","):
        acilmis.extend(_brace_ac(
            belirtec[:eslesme.start()] + parca.strip() + belirtec[eslesme.end():]))
    return acilmis


def _paket_koku(yol: str) -> str | None:
    parcalar = yol.split("/")
    return "/".join(parcalar[:2]) if len(parcalar) >= 2 and parcalar[0] == "src" else None


def _tek_yol_coz(belirtec: str, son_kok: str | None,
                 son_dizin: str | None) -> str | None:
    """Bir kaynak belirtecini repo yoluna çevirir; çözemezse `None`."""
    belirtec = belirtec.strip().strip(",").rstrip("/")
    if not belirtec:
        return None

    adaylar = [belirtec, f"src/{belirtec}"]

    ilk, _, kalan = belirtec.partition("/")
    if ilk in PAKETLER:
        adaylar.append(f"{PAKETLER[ilk]}/{kalan}" if kalan else PAKETLER[ilk])

    adaylar.append(f"{FRONTEND}/{belirtec}")
    # Göreli devam: sütun bir dosyayı tam yazar, kardeşlerini çıplak yazar
    # (`Endpoints/ApiKeyEndpoints.cs` · `AuditEndpoints.cs`). Dizin önce
    # denenir; paket kökü daha geniştir ve yanlış eşleşme üretebilir.
    if son_dizin:
        adaylar.append(f"{son_dizin}/{belirtec}")
    if son_kok:
        adaylar.append(f"{son_kok}/{belirtec}")

    for aday in adaylar:
        if _yol_var_mi(aday):
            return aday

    return _tekil_temel_ad(belirtec)


def kaynak_yollari(hucre: str) -> tuple[list[str], list[str]]:
    """`Kaynak` sütununu (çözülen yollar, çözülemeyen belirteçler) olarak verir.

    Sütun üç kısayol kullanır ve üçü de sırayla denenir: paket kısaltması
    (`Core/Runs/...`), frontend göreliliği (`screens/run*.tsx`) ve aynı hücrede
    daha önce geçen paketin altındaki göreli dizin (`src/Tracon.Core` yazıldıktan
    sonra `Compilation/`).
    """
    cozulen: list[str] = []
    cozulemeyen: list[str] = []
    son_kok: str | None = None
    son_dizin: str | None = None

    for ham in re.findall(r"`([^`]+)`", hucre):
        # Parantez içi açıklama yoldan sayılmaz: "(yalnız CancelRunAsync)".
        temiz = re.sub(r"\([^)]*\)", "", ham).strip()
        for belirtec in _brace_ac(temiz):
            belirtec = belirtec.strip()
            # HTTP rotası (`/api/mcp-servers/*`) kaynak yolu değildir ve bir
            # ölçüm boşluğu da değildir; raporu kirletmeden elenir.
            if belirtec.startswith("/"):
                continue
            if "/" not in belirtec and not belirtec.endswith(KAYNAK_UZANTILARI):
                continue
            yol = _tek_yol_coz(belirtec, son_kok, son_dizin)
            if yol is None:
                cozulemeyen.append(belirtec)
                continue
            cozulen.append(yol)
            son_kok = _paket_koku(yol) or son_kok
            son_dizin = str(pathlib.PurePosixPath(yol).parent)

    return sorted(set(cozulen)), cozulemeyen


# --------------------------------------------------------------------------
# Aile ölçümü
# --------------------------------------------------------------------------

@dataclasses.dataclass
class Aile:
    no: str
    dosya: str
    kod: str
    fazlar: str
    yeni: list[str]
    degisti: list[str]
    sessiz: list[str]
    kod_kaymasi: int
    yol_sayisi: int
    cozulemeyen: list[str]
    yeni_dosya: bool

    @property
    def toplam(self) -> int:
        return len(self.yeni) + len(self.degisti) + len(self.sessiz)

    @property
    def risk(self) -> int:
        return len(self.yeni) + len(self.degisti) + min(self.kod_kaymasi, KAYMA_TAVANI)

    @property
    def oturum(self) -> int:
        """Bu ailenin kaç koşum oturumu tuttuğu (yukarı yuvarlanır)."""
        bolen = (CASE_PER_OTURUM_ARAYUZ if self.no in ARAYUZ_AILELERI
                 else CASE_PER_OTURUM_DIGER)
        return max(1, -(-self.toplam // bolen))


def aileleri_olc(taban: str) -> list[Aile]:
    aileler: list[Aile] = []

    for satir in INDEKS.read_text(encoding="utf-8").split("\n"):
        eslesme = AILE_SATIRI.match(satir)
        if not eslesme:
            continue

        dosya = eslesme.group("dosya")
        yol = f"docs/manuel-test/{dosya}"
        bugun = (MT / dosya)
        if not bugun.exists():
            continue

        simdi = case_imzalari(bugun.read_text(encoding="utf-8"))
        onceki_metin = _tabanda_oku(taban, yol)
        onceki = case_imzalari(onceki_metin) if onceki_metin else {}

        cozulen, cozulemeyen = kaynak_yollari(eslesme.group("kaynak"))

        aileler.append(Aile(
            no=eslesme.group("no"),
            dosya=dosya,
            kod=eslesme.group("kod"),
            fazlar=eslesme.group("fazlar").strip(),
            yeni=sorted(a for a in simdi if a not in onceki),
            degisti=sorted(a for a in simdi
                           if a in onceki and simdi[a] != onceki[a]),
            sessiz=sorted(a for a in simdi
                          if a in onceki and simdi[a] == onceki[a]),
            kod_kaymasi=commit_sayisi(taban, cozulen),
            yol_sayisi=len(cozulen),
            cozulemeyen=cozulemeyen,
            yeni_dosya=onceki_metin is None,
        ))

    return aileler


def serit_dagilimi(aileler: list[Aile]) -> list[list[Aile]]:
    """Zincir dışındaki aileleri şeritlere böler — en dolu şeride en az yük.

    Açgözlü paketleme: aileler risk sırasında gezilir ve her biri o an en az
    oturum taşıyan şeride düşer. Aile bölünmez; şerit izolasyonu dosya
    düzeyindedir (SKILL.md §1.3: "Bir dosya TEK şeride aittir").
    """
    seritler: list[list[Aile]] = [[] for _ in range(SERIT_SAYISI)]
    for aile in sorted((a for a in aileler if a.no not in ZINCIR),
                       key=lambda a: (-a.risk, a.no)):
        en_bos = min(seritler, key=lambda s: sum(x.oturum for x in s))
        en_bos.append(aile)
    return seritler


def kosum_sirasi(aileler: list[Aile]) -> list[Aile]:
    """Zincir önce ve kendi sırasında; kalan aileler risk sırasında."""
    sirali = {a.no: a for a in aileler}
    once = [sirali[no] for no in ZINCIR if no in sirali]
    kalan = sorted((a for a in aileler if a.no not in ZINCIR),
                   key=lambda a: (-a.risk, a.no))
    return once + kalan


# --------------------------------------------------------------------------
# Kapsama boşluğu
# --------------------------------------------------------------------------

def giris_noktalari() -> dict[str, set[str]]:
    """Public API'nin kayıt giriş noktaları — tüketicinin bir yeteneği açtığı üye."""
    bulunan: dict[str, set[str]] = {}
    for dosya in sorted((ROOT / "src").glob("*/PublicAPI.*.txt")):
        paket = dosya.parent.name
        for satir in dosya.read_text(encoding="utf-8").split("\n"):
            uye = BUILDER_UYESI.match(satir)
            ad = uye.group("ad") if uye else None
            if ad is None:
                uzanti = UZANTI_METODU.match(satir)
                if uzanti and uzanti.group("alici") in KAYIT_ALICILARI:
                    ad = uzanti.group("ad")
            if ad:
                bulunan.setdefault(ad, set()).add(paket)
    return bulunan


def kapsanmayanlar() -> dict[str, set[str]]:
    noktalar = giris_noktalari()
    metin = "\n".join(p.read_text(encoding="utf-8")
                      for p in sorted(MT.glob("[0-3]*.md")))
    return {ad: paketler for ad, paketler in sorted(noktalar.items())
            if not re.search(rf"\b{re.escape(ad)}\b", metin)}


# --------------------------------------------------------------------------
# Rapor
# --------------------------------------------------------------------------

def rapor(taban: str, baslik: str, aileler: list[Aile]) -> str:
    yeni = sum(len(a.yeni) for a in aileler)
    degisti = sum(len(a.degisti) for a in aileler)
    sessiz = sum(len(a.sessiz) for a in aileler)
    toplam = yeni + degisti + sessiz
    araya_giren = toplam_commit(taban)
    sira = kosum_sirasi(aileler)
    bosluklar = kapsanmayanlar()
    noktalar = giris_noktalari()

    s: list[str] = []
    s.append("# Koşum planı — tazelik ölçümü")
    s.append("")
    s.append("> **Üretilir, elle yazılmaz.** Kaynak:")
    s.append("> `python3 scripts/manuel-test-tazelik.py --taban <commit>`.")
    s.append(">")
    s.append(f"> **Taban:** `{taban}` · **Ölçülen:** `{baslik}` · "
             f"**Araya giren commit:** {araya_giren}")
    s.append("")
    s.append("## 1. Kovalar")
    s.append("")
    s.append("| Kova | Case | Pay | Anlamı |")
    s.append("|---|---|---|---|")
    s.append(f"| **yeni** | {yeni} | %{yeni * 100 // toplam} | "
             "Taban turdan sonra eklendi; hiç koşulmadı |")
    s.append(f"| **değişti** | {degisti} | %{degisti * 100 // toplam} | "
             "Metni değişti; taban turun sonucu geçersiz |")
    s.append(f"| **sessiz** | {sessiz} | %{sessiz * 100 // toplam} | "
             "Metni aynı; riski aile kaynağına giren commit sayısıdır |")
    s.append(f"| **toplam** | {toplam} | | {len(aileler)} aile |")
    s.append("")
    s.append("🚨 **`sessiz` kovası güvenli demek değildir.** Metni değişmeyen bir")
    s.append("case, altındaki kod değiştiyse yanıltıcıdır — koşulmadan bayat olduğu")
    s.append("bilinemez. `kod kayması` sütunu o riski ölçer.")
    s.append("")
    s.append("## 2. Aile ölçümü")
    s.append("")
    s.append("| # | Aile | case | yeni | değişti | sessiz | kod kayması | risk |")
    s.append("|---|---|---|---|---|---|---|---|")
    for a in sorted(aileler, key=lambda x: x.no):
        isaret = " 🆕" if a.yeni_dosya else ""
        s.append(f"| {a.no} | `{a.dosya}`{isaret} | {a.toplam} | {len(a.yeni)} | "
                 f"{len(a.degisti)} | {len(a.sessiz)} | {a.kod_kaymasi} | {a.risk} |")
    s.append("")
    s.append("🆕 = taban turda bu dosya yoktu.")
    s.append("")
    s.append("## 3. Koşum sırası")
    s.append("")
    s.append(f"**Zincir kapıdır.** `{' → '.join(ZINCIR)}` kırılırsa sonraki hiçbir")
    s.append("aile anlamlı sonuç vermez; bu beşi risk sırası **ezemez**. Kalan")
    s.append(f"aileler risk sırasındadır (`yeni + değişti + min(kod kayması, {KAYMA_TAVANI})`).")
    s.append("")
    s.append("| Sıra | Aile | risk | oturum | Not |")
    s.append("|---|---|---|---|---|")
    for i, a in enumerate(sira, 1):
        not_ = "🔗 zincir" if a.no in ZINCIR else ""
        s.append(f"| {i} | `{a.dosya}` | {a.risk} | {a.oturum} | {not_} |")
    s.append("")
    s.append(f"**Toplam {sum(a.oturum for a in sira)} oturum.** Tahmin "
             f"`manuel-test-kosumu` §3 bütçesindendir: arayüz ailesi "
             f"({' · '.join(ARAYUZ_AILELERI)}) oturum başına "
             f"{CASE_PER_OTURUM_ARAYUZ}, diğerleri {CASE_PER_OTURUM_DIGER} case.")
    s.append("")
    s.append("## 3.1 Şerit dağılımı")
    s.append("")
    zincir = [a for a in sira if a.no in ZINCIR]
    s.append(f"**Faz A — zincir, TEK şerit, sırayla.** "
             f"{' → '.join(a.no for a in zincir)} "
             f"({sum(a.oturum for a in zincir)} oturum). Paralel şeritler ancak "
             "bu beşi yeşil bitince açılır; kapı kırıksa sonraki hiçbir ailenin "
             "sonucu okunmaz.")
    s.append("")
    s.append(f"**Faz B — kalan {len(sira) - len(zincir)} aile, "
             f"{SERIT_SAYISI} şerit.** Açgözlü paketleme; aile bölünmez "
             "(SKILL.md §1.3: bir dosya TEK şeride aittir).")
    s.append("")
    s.append("| Şerit | Port | Şema | Oturum | Aileler |")
    s.append("|---|---|---|---|---|")
    for n, serit in enumerate(serit_dagilimi(aileler), 1):
        adlar = " · ".join(a.no for a in serit)
        s.append(f"| `ap-s{n}` | {5080 + n} | `mt_s{n}` | "
                 f"{sum(a.oturum for a in serit)} | {adlar} |")
    s.append("")
    s.append("## 4. Kapsama boşluğu")
    s.append("")
    s.append(f"`CapabilityEntryPoints` kuralıyla (K-509) ölçüldü: **{len(noktalar)}**")
    s.append(f"kayıt giriş noktasının **{len(bosluklar)}**'i manuel-test setinde hiç")
    s.append("anılmıyor.")
    s.append("")
    if bosluklar:
        s.append("| Giriş noktası | Paket |")
        s.append("|---|---|")
        for ad, paketler in bosluklar.items():
            s.append(f"| `{ad}` | {' · '.join(f'`{p}`' for p in sorted(paketler))} |")
        s.append("")
        s.append("⚠️ Her biri **elle doğrulanır**: bazısı başka kelimeyle kapsanmış")
        s.append("olabilir. Gerçekten kapsanmayan için alan dosyasına case yazılır.")
    else:
        s.append("Boşluk yok.")
    s.append("")
    s.append("## 5. Çözülemeyen kaynak yolları")
    s.append("")
    s.append("`00-INDEKS.md` §7 `Kaynak` sütunundan okunamayan belirteçler. Her biri")
    s.append("kod kaymasını **olduğundan küçük** gösterir ve aileyi risk sırasında")
    s.append("hak ettiği yerin altına düşürür.")
    s.append("")
    kirli = [a for a in aileler if a.cozulemeyen]
    if kirli:
        s.append("| Aile | Çözülen yol | Çözülemeyen |")
        s.append("|---|---|---|")
        for a in sorted(kirli, key=lambda x: x.no):
            liste = " · ".join(f"`{y}`" for y in a.cozulemeyen)
            s.append(f"| {a.no} | {a.yol_sayisi} | {liste} |")
    else:
        s.append("Tümü çözüldü.")
    s.append("")
    return "\n".join(s)


def main() -> int:
    ap = argparse.ArgumentParser(
        description="Manuel kabul turunun tazelik ölçümü.")
    ap.add_argument("--taban", required=True,
                    help="önceki tam turun son commit'i")
    ap.add_argument("--kosum",
                    help="koşum dizini; boşsa kosumlar/ altındaki en yenisi")
    ap.add_argument("--kuru", action="store_true",
                    help="yazma, yalnız bas")
    a = ap.parse_args()

    if _git("rev-parse", "--verify", f"{a.taban}^{{commit}}").returncode != 0:
        print(f"HATA: taban commit çözülemedi: {a.taban}", file=sys.stderr)
        return 1

    baslik = _git("rev-parse", "--short", "HEAD").stdout.strip()
    aileler = aileleri_olc(a.taban)
    if not aileler:
        print("HATA: 00-INDEKS.md içinden aile satırı okunamadı", file=sys.stderr)
        return 1

    metin = rapor(a.taban, baslik, aileler)

    if a.kuru:
        print(metin)
        return 0

    if a.kosum:
        dizin = pathlib.Path(a.kosum)
        if not dizin.is_absolute():
            dizin = ROOT / dizin
    else:
        # `kosumlar/` turlar arasinda hic var olmayabilir (kapanista arsive
        # tasinir); yoklugu ham traceback degil, ayni temiz hata olmalidir.
        adaylar = (sorted(d for d in KOSUMLAR.iterdir() if d.is_dir())
                   if KOSUMLAR.exists() else [])
        if not adaylar:
            print(f"HATA: koşum dizini yok: {KOSUMLAR}", file=sys.stderr)
            return 1
        dizin = adaylar[-1]

    dizin.mkdir(parents=True, exist_ok=True)
    hedef = dizin / "00-KOSUM-PLANI.md"
    hedef.write_text(metin, encoding="utf-8")
    print(f"yazıldı: {hedef.relative_to(ROOT)}")

    cozulemeyen = sum(len(x.cozulemeyen) for x in aileler)
    if cozulemeyen:
        print(f"uyarı: {cozulemeyen} kaynak yolu çözülemedi — §5")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
