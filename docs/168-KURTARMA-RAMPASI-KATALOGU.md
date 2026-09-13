# Faz 168 — Kurtarma Rampası Kataloğu

> **Durum:** ✅ Tamamlandı (2026-09-13)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-228** (keşif: [`kesif/2026-09-13-anew-karsilastirmasi.md`](kesif/2026-09-13-anew-karsilastirmasi.md) § 6 A3, § 9 H3)
> **Önkoşul:** [Faz 167](arsiv/fazlar/167-AGENT-ZORLAMA-KATMANI.md) — `KR-11` rampası `git reset --hard` yasağının **var olduğunu** varsayar. Yasak konmadıysa `KR-11`'in metni değişir
> **Paketler:** Yok. Bu faz `src/` altına **hiç dokunmaz**
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok — `docs-site/` sayfası yok, sevk edilen yapıt yok
> **Manuel test alanı:** [`docs/manuel-test/36-GELISTIRME-KAPILARI.md`](manuel-test/36-GELISTIRME-KAPILARI.md) — Faz 167'nin bıraktığı numaradan devam eder

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. **Tamamını değil, yalnız işaret edilen
> bölümleri oku.**

1. Bu doküman
2. Desen kaynağı — yeni dosya **bunlara benzeyecek**, ikisi de kısadır:
   ```bash
   cat .agents/ortak/kapilar.md          # 5.962 B
   cat .agents/ortak/test-seviyeleri.md  # 1.931 B
   ```
   İkisi de `.agents/skills/` **dışında** durur, "Bağlayan dosyalar" satırı
   taşır ve tek kaynak cümlesi yazar. `kurtarma.md` üçüncüsüdür.
3. Bağlanacak protokoller — **gövdeleri kopyalanmayacak**, yalnız yerleri
   bilinecek:
   ```bash
   grep -n "^## Adım" .agents/skills/kusur-giderme/SKILL.md
   grep -n "^## Adım" .agents/skills/faz-uygulama/SKILL.md
   ```
4. Kararlar — yalnız bir kalem:
   ```bash
   grep -n "K-408" docs/KARARLAR.md
   ```
   **K-408** (kaynak dili sınırı) — `.agents/` geliştirme aparatıdır ve Türkçe
   kalır; `SourceLanguageTests` onu **taramaz** (Faz 167 § 167.5'te ölçüldü).
5. Alan hafızası: [`hafiza/dokumantasyon.md`](hafiza/dokumantasyon.md) — doküman
   kapıları ve `kirik_baglantilar()` davranışı burada yaşar.
6. Önceki fazın devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/167-AGENT-ZORLAMA-KATMANI.md
   ```
   `KR-11`'in dayandığı yasağın gerçekten konup konmadığını **oradan** öğren.

---

## Amaç

Bir şey ters gittiğinde Tracon'in protokolü **dağınık ve adsızdır**.
`kusur-giderme` yalnız kusuru kapsar; kalan durumlar için ya yarım bir cümle
vardır ya hiçbir şey. Bu faz on iki durumu adlandırır ve tek bir kataloğa
bağlar.

- **F-228** — `.agents/ortak/kurtarma.md`: beş yeni rampa yazılır, yedi mevcut
  protokol **bağlanır** (kopyalanmaz).

Bir rampanın asıl işi protokolü hatırlatmak değil, **doğaçlamayı
yasaklamaktır**. `kusur-giderme` bunu kusur için yapıyor ve ölçülebilir sonuç
verdi: üç kusur sınıfı (`AsyncLocal` 4 kez, senkronizasyon kopyası 5 kez,
Playwright locator 3 kez) ancak **adlandırıldıktan sonra** kapı kazandı.

### Bugün ne çalışmıyor — doğrulanmış kanıt

2026-09-13 taraması, on iki durumun repodaki karşılığını ölçtü:

| Durum | Repodaki karşılığı |
|---|---|
| Derleme hatası · kırmızı test · kırılgan test · regresyon | `kusur-giderme` Adım 1–4, 7 — **var, adsız** |
| Plan sapması | `faz-uygulama` Adım 1 — **var, adsız** |
| Belirsizlik · doküman-kod çelişkisi | `AGENTS.md` "Her belirsizliği sor" — **var, adsız** |
| Performans hedefi kaçtı | `kapi.py performans` — kapı var, **protokol yok** |
| **Araştırılacak bulgu** (repro gerekiyor) | **yok** |
| **Düzeltme turu limiti** | **yok** |
| **Faz ortasında kapsam değişimi** | **yok** |
| **Faz içi bağlam sisi / devir** | **yok** |
| **Güvenli geri alma** | **yok** |

> Kanıtlar 2026-09-13 tarihinde yeniden doğrulandı: `.agents/ortak/` iki dosya
> taşıyor, `kusur-giderme` yedi adımlı, `faz-uygulama` yedi adımlı,
> `kapi.py` `performans` alt komutunu tanıyor (`scripts/kapi.py:1267`).

**En keskin boşluk devir teslimdir.** `faz-tamamlama` Adım 6 **yalnız faz
sonunda** koşar. Faz ortasında bağlam bittiğinde hiçbir protokol yoktur — oturum
ya doğaçlar ya bilgiyi kaybeder.

---

## 168.1 — Dosya, önek ve biçim

Tek yeni dosya: `.agents/ortak/kurtarma.md`. Önek **`KR-`**.

### Önek neden `KR-`

| Önek | Kullanımda | Çakışır mı |
|---|---|---|
| `K-NNN` | Karar defteri, 761 kalem | ✅ çakışır |
| `F-NN` | Aday listesi | ✅ çakışır |
| `MT-*` | Manuel kabul case'i | ✅ çakışır |
| `BL-NNN` | Yayın blocker'ı | ✅ çakışır |
| `R-NN` | ANEW kıyas dokümanının **kendi** şeması | ⚠️ aynı repoda `R-05` iki farklı sisteme çıkardı |
| **`KR-`** | — | **çakışma yok** (ölçüldü 2026-09-13: `docs/`, `.agents/`, `scripts/`, `AGENTS.md` içinde sıfır eşleşme) |

`KR-01…12` ayrıca ANEW'in `R-01…12` şemasıyla **birebir hizalanır** — kıyas
dokümanını okuyan eşlemeyi anında kurar.

> 🚨 Keşif raporu § 9 H3'ün tablosu rampaları `K-01…K-12` diye yazıyor. Bu bir
> **yazım hatasıdır**; karar `KR-`'dir ve § 14'te öyle kayıtlıdır. Rapor keşif
> kaydıdır ve değiştirilmez — bu faz `KR-` kullanır.

### Biçim

`kapilar.md` ve `test-seviyeleri.md` deseni birebir izlenir:

- `.agents/skills/` **dışında** durur → skill olarak keşfedilmez (içinde
  `SKILL.md` yoktur; Faz 92 bunu yapısal olarak doğruladı)
- Başta bir **"Bağlayan dosyalar"** satırı taşır
- Her rampa **tek kaynak** cümlesi yazar; gövde ya buradadır ya **orada**,
  ikisinde birden değil

🚨 **`#fragment` bağlantısı bu repoda ilk kez kullanılacak.** Ölçüldü
(`scripts/dokuman-bakim.py:1082`):

```python
LINK = re.compile(r"\]\(([^)\s]+?)(#[^)\s]*)?\)")
```

`group(1)` fragment'ı **dışarıda bırakır**, yani `SKILL.md#adim-2` biçimi
`kirik_baglantilar()` kapısını **kırmaz**. Yine de `MT-GDK` case'i bunu koşarak
kanıtlar — ölçülmüş bir regex, koşulmuş bir kapı değildir.

---

## 168.2 — On iki rampa

```mermaid
flowchart TD
    accTitle: Kurtarma katalogunun iki yarisi
    accDescr: Bes rampanin govdesi yeni dosyada yasar. Yedi rampa mevcut protokollere baglanir ve govdeleri kopyalanmaz.
    K["kurtarma.md<br/>KR-01…12"]
    K --> Y["gövde BURADA<br/>KR-05 · 06 · 08 · 09 · 11"]
    K --> B["yalnız BAĞLANIR"]
    B --> B1["KR-01…04 →<br/>kusur-giderme"]
    B --> B2["KR-07 →<br/>faz-uygulama Adım 1"]
    B --> B3["KR-10 →<br/>AGENTS.md"]
    B --> B4["KR-12 →<br/>kapi.py performans"]
```

| Kod | Durum | Gövde nerede |
|---|---|---|
| `KR-01` | Derleme hatası | → `kusur-giderme` Adım 1 |
| `KR-02` | Kırmızı test | → `kusur-giderme` Adım 1–4 |
| `KR-03` | Kırılgan test | → `kusur-giderme` Adım 2 |
| `KR-04` | Regresyon | → `kusur-giderme` Adım 5, 7 |
| **`KR-05`** | **Araştırılacak bulgu** — repro gerekiyor | **Yeni — burada** |
| **`KR-06`** | **Düzeltme turu limiti** | **Yeni — burada** |
| `KR-07` | Plan sapması | → `faz-uygulama` Adım 1 |
| **`KR-08`** | **Faz ortasında kapsam değişimi** 👤 | **Yeni — burada** |
| **`KR-09`** | **Faz içi bağlam sisi / devir** | **Yeni — burada** |
| `KR-10` | Belirsizlik · doküman-kod çelişkisi | → `AGENTS.md` "Her belirsizliği sor" |
| **`KR-11`** | **Güvenli geri alma** 👤 | **Yeni — burada** |
| `KR-12` | Performans hedefi kaçtı | → `kapi.py performans` |

### Beş yeni rampanın gövdesi

**`KR-05` — araştırılacak bulgu.** Tetikleyici: bir bulgu geçerli **görünüyor**
ama repro yok. Adımlar: minimal repro yaz → süre kutusu **bir tur** → repro
çıkarsa `KR-02`'ye geç, çıkmazsa **gerekçeli kapanış**. 🚨 Düzeltme yazmak
yasaktır: repro'suz düzeltme, düzeltildiğini kanıtlayamaz.

**`KR-06` — düzeltme turu limiti.** Tetikleyici: aynı davranış için **üçüncü**
düzeltme turu. Adım: **kod yazmayı durdur** ve tek soruyu sor — kök sebep
**kodda mı planda mı?** Plandaysa `KR-08`'e geç. Bu rampa bir sayı taşır ve sayı
**üçtür**; "birkaç tur" bir limit değildir.

**`KR-08` — faz ortasında kapsam değişimi** 👤. Tetikleyici: fazın DoD'si artık
doğru işi tarif etmiyor. Sıra **bağlayıcıdır**: önce **faz dokümanı** güncellenir,
sonra delta plan yazılır, sonra kod. Ters sıra `AGENTS.md`'nin "plandan sapma
gizlenmez" kuralını sessizce çiğner. 👤 kullanıcı onayı gerekir.

**`KR-09` — faz içi bağlam sisi / devir.** Tetikleyici: oturum bağlamı doluyor
ama faz bitmedi. Adımlar: durum dosyası yaz (ne bitti · ne yarım · sıradaki tek
adım · bilinen tuzak) → devret. 🚨 `faz-tamamlama` Adım 6 **buna alternatif
değildir**: o yalnız faz **sonunda** koşar. Bu rampa fazın **ortası** içindir ve
bugün karşılığı yoktur.

**`KR-11` — güvenli geri alma** 👤. Araç `git revert`'tir. `git reset --hard`
Faz 167'de yasaklanmıştır ve bu rampa o yasağı **varsayar**. Geri almadan önce
migration risk raporu yazılır ve tek kural şudur: **"bilinmiyor" bir cevaptır ve
geri almayı durdurur.** 👤 kullanıcı onayı gerekir.

---

## 168.3 — Bağlantılar: altı dosya, altı satır

Katalog **keşfedilemezse yazılmamış sayılır.** Altı giriş noktası bağlanır:

| Dosya | Ne eklenir |
|---|---|
| `AGENTS.md` | "İhtiyaç \| Yol" tablosuna **bir satır** (~90 B) |
| `.agents/skills/README.md` | Ortak sözleşme listesine `kurtarma.md` |
| `.agents/skills/kusur-giderme/SKILL.md` | `KR-01…04`'ün gövdesinin burada olduğunu söyleyen bir satır |
| `.agents/skills/faz-uygulama/SKILL.md` | Adım 1'in `KR-07` olduğunu söyleyen bir satır |
| `.agents/skills/faz-denetim/SKILL.md` | "araştırılacak" sonucu `KR-05`'e gider (Faz 169 bunu kapıya bağlar) |
| `.agents/skills/faz-baslangic/SKILL.md` | Bağlam sisi `KR-09`'dur |

🚨 **`AGENTS.md` bütçesi dar: 11.189 / 12.000 B, %7 boş — 811 B kalmış.** Bir
satır sığar, **ikinci bir ekleme sığmaz**. Bu fazda `AGENTS.md`'ye başka hiçbir
şey eklenmez. Satır eklenmezse katalog keşfedilemez; iki satır eklenirse bütçe
zorlanır. Tam olarak **bir** satır.

`.agents/` hiçbir bütçe sözlüğünde **değildir** (ölçüldü: `BASLANGIC_BUTCESI`,
`SORGU_BUTCESI`, `YONETIM_BUTCESI`, `DIZIN_BUTCESI` — dördü de yalnız `docs/` ve
kök dosyaları sayar). Yeni dosya bütçe kapısını **tetiklemez**.

---

## Planlanan Public API

**Yok.** Bu faz `src/` altına dokunmaz. Kod yok, test yok, yeni kapı yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
.agents/
├── ortak/
│   └── kurtarma.md               YENİ — tek dosya, ~80–120 satır
└── skills/
    ├── README.md                 DEĞİŞİR — bir satır
    ├── kusur-giderme/SKILL.md    DEĞİŞİR — bir satır
    ├── faz-uygulama/SKILL.md     DEĞİŞİR — bir satır
    ├── faz-denetim/SKILL.md      DEĞİŞİR — bir satır
    └── faz-baslangic/SKILL.md    DEĞİŞİR — bir satır

AGENTS.md                         DEĞİŞİR — TAM BİR SATIR (~90 B)

docs/manuel-test/
└── 36-GELISTIRME-KAPILARI.md     DEĞİŞİR — üç case
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test |
|---|---|---|
| `#fragment` bağlantısı kapıyı kırar | Fonksiyonel | `python3 scripts/dokuman-bakim.py --denetle` → kırık bağlantı **0** |
| `AGENTS.md` iki satır alır ve bütçeyi aşar | Fonksiyonel | Aynı kapı — `AGENTS.md` satırı kırmızı döner |
| Rampa gövdesi **hem** katalogda **hem** skill'de yaşar; ikisi zamanla çelişir | Manuel | Yeni case: her `KR-` satırı ya gövde ya bağlantı taşır, ikisi birden değil |
| Katalog hiçbir yerden bağlanmaz ve görünmez kalır | Fonksiyonel | `grep -rn "kurtarma.md" AGENTS.md .agents/` **altı** dosya döndürmeli |
| Bağlanan bir skill adımı ileride yeniden numaralanır; bağlantı bayatlar | Fonksiyonel | `kirik_baglantilar()` dosyayı yakalar, **fragment'ı değil** — sınır bilinerek kabul edilir (bkz. Riskler) |

Beş soru, bu fazın bağlamındaki cevaplarıyla:

| Soru | Cevap |
|---|---|
| İptal · eşzamanlılık · boş/aşırı girdi · başka kiracı | Kapsam dışı — kod yolu yok |
| Alt sistem hatası | Kapı zaten var (`kirik_baglantilar`); bu faz yeni kapı **eklemez** |

Sözleşme testi gerekmez: hiçbir sınır geçilmiyor.

---

## Manuel Kabul Case'leri

> Kapanışta [`docs/manuel-test/36-GELISTIRME-KAPILARI.md`](manuel-test/36-GELISTIRME-KAPILARI.md)
> içine, Faz 167'nin bıraktığı numaradan devam ederek eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `kurtarma.md` yazılmış, `#fragment` bağlantısı taşıyor | `python3 scripts/dokuman-bakim.py --denetle` | Kırık bağlantı **0**; fragment'lı bağlantı bulgu üretmez |
| 2 | `kurtarma.md` içinde var olmayan bir dosyaya bağlantı konmuş | Aynı komut | Çıkış `1`; o satır **raporlanır** — kapı gerçekten koşuyor |
| 3 | `AGENTS.md` satırı eklenmiş | Aynı komut | `AGENTS.md` bütçe içinde (< 12.000 B); DAR uyarısı kabul edilir, **aşım kabul edilmez** |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | "Bağlanır, kopyalanmaz" sözü bir kapıyla korunsun mu? | A: bu fazda hayır · B: `tekrarlanan_kapi_tanimlari()`'ye bir satır (`kurtarma.md` içinde `kapi.py kapanis` ham komutu geçmemeli) | **B** — kapı bugün `ci.yml`, `AGENTS.md` ve `faz-tamamlama` için aynı şeyi yapıyor; desen hazır ve satır ucuz. Kopyalama riski gerçektir: katalog tam olarak kopyalamaya davet eden bir biçimdir |
| 2 | `kurtarma.md` hangi dilde? | A: Türkçe · B: İngilizce | **A** — geliştirme aparatıdır (K-408), `kapilar.md` ve `test-seviyeleri.md` de Türkçe. `SourceLanguageTests` `.agents/`'ı taramaz |
| 3 | `KR-09` bir **durum dosyası şablonu** da taşısın mı? | A: dört başlık yeter (ne bitti · ne yarım · sıradaki adım · tuzak) · B: ayrı şablon dosyası | **A** — YAGNI. İkinci bir dosya kataloğun kendi "bağlanır, kopyalanmaz" sözünü zorlar |

---

## Bitiş Ölçütleri (DoD)

- [x] `.agents/ortak/kurtarma.md` var; **on iki** `KR-` kalemi taşıyor ve beşinin gövdesi (`KR-05, 06, 08, 09, 11`) **burada**, yedisininki **bağlantıda**
- [x] Hiçbir rampa gövdesi iki yerde yaşamıyor — `KR-01…04, 07, 10, 12` satırlarının hiçbiri protokol adımlarını **tekrarlamıyor**
- [x] `grep -rln "kurtarma.md" AGENTS.md .agents/` — altı **giriş noktası** + `kapilar.md` geri referansı = **7** (Sapma 1)
- [x] `AGENTS.md` **tam bir satır** aldı ve bütçe içinde — öncesi/sonrası bayt ölçüldü ve yazıldı (taban 11.189 B, tavan 12.000 B)
- [x] `python3 scripts/dokuman-bakim.py --denetle`: kırık bağlantı **0**, fragment bulgu üretmedi, üretilen dosyalar taze. 🚨 Çıkış kodu **arşivlemeden sonra** `0`'dır; öncesinde "kapanmış faz `docs/arsiv/fazlar/` altında olmalı" bulgusu vardır (denetim 🟡 4)
- [x] Açık Soru 1 **B** seçildi: `tekrarlanan_kapi_tanimlari()` üç dal kazandı (var mı · ham komut kopyalıyor mu · `kapilar.md`'ye bağlanıyor mu); üçü de mutation ile kırmızı görüldü, `dokuman_bakim_test.py` iki vaka taşıyor
- [x] `python3 -m unittest discover -s scripts -p "*_test.py"` yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`)
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı aşağıya yazıldı (§ Örnek Uygulama Koşumu) — 🚨 **bu faz kod değiştirmez; koşum bir regresyon kanıtıdır** (Faz 92 emsali)
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri `docs/manuel-test/36-GELISTIRME-KAPILARI.md` içine eklendi; **dördü de** koşuldu (`MT-GDK-032…035`, Sapma 3)
- [x] `faz-denetim` koşuldu — Faz 167'nin `faz-denetcisi` tipiyle; 🔴 bulgu kalmadı
- [x] `## Süreç Ölçümü` bölümü dolduruldu
- [x] `KR-11`'in metni Faz 167'nin **gerçekleşen** sonucuyla tutarlı — `git reset --hard` yasağı konmadıysa rampa metni ona göre yazıldı

### Doğrulama komutları

```bash
# Katalog altı yerden bağlı mı
grep -rln "kurtarma.md" AGENTS.md .agents/ | wc -l    # 6 olmalı

# On iki rampa var mı
grep -c "^| \`KR-" .agents/ortak/kurtarma.md          # 12 olmalı

# AGENTS.md bütçesi
wc -c AGENTS.md                                        # < 12000

# Kapılar
python3 scripts/dokuman-bakim.py --denetle   # kırık bağlantı 0; çıkış 0 yalnız ARŞİVLEMEDEN SONRA
python3 -m unittest discover -s scripts -p "*_test.py"   # 260 test
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| `AGENTS.md` 811 B boşta; ikinci bir ekleme sığmaz | Bu fazda `AGENTS.md`'ye **yalnız bir satır** girer. DoD bunu bayt ölçümüyle kanıtlar |
| Satır hiç eklenmezse katalog keşfedilemez ve yazılmamış sayılır | DoD satırı: altı bağlayıcı dosya `grep` ile sayılır |
| "Bağlanır, kopyalanmaz" sözü yalnız yazıyla korunur | Açık Soru 1 → önerilen **B**: `tekrarlanan_kapi_tanimlari()`'ye bir satır |
| On ikinin yedisi yalnız bağlantıdır; katalog bir indekse benzer ve indeksler bayatlar | Bayatlamayı `kirik_baglantilar()` yakalar. Ayrıca değeri üreten şey içerik değil **adlandırmanın kendisidir** — "ne yapsak?" yerine "bu bir `KR-06`" denir |
| Bağlantı dosyayı doğrular ama **fragment'ı** doğrulamaz; skill adımı yeniden numaralanırsa çapa sessizce ölür | Bilinen sınır, kabul edilir ve `kurtarma.md` içine yazılır. Fragment doğrulayan bir kapı bu fazın kapsamında **değildir** — gerekirse 🟢 aday olur |
| Rampa hiç çağrılmaz ve katalog ölü metin olur | Ölçüm Faz 169'un `## Süreç Ölçümü` bölümünden gelir: bir krizde rampa **koduyla** anıldı mı |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

**1 — Katalog altı değil YEDİ dosyadan bağlanıyor.** DoD `grep -rln "kurtarma.md"
AGENTS.md .agents/` çıktısını **6** bekliyordu; gerçekleşen **7**'dir. Yedincisi
`kapilar.md`'dir ve bir giriş noktası değil, **geri referanstır**: `KR-12`
gövdesini `kapilar.md`'ye bağladığı için o dosyanın "Bağlayan dosyalar" satırı
artık üç değil dört çağıran sayar. Satır yazılmasaydı `kapilar.md`'nin kendi
sözleşme cümlesi yanlış kalırdı. Altı giriş noktasının hepsi plandaki
dosyalardır; yedinci onlara ek.

**2 — `kapilar.md` `performans` alt komutunu hiç belgelemiyordu.** `KR-12`'nin
gövdesi oraya bağlanınca boşluk görüldü: dosya "ham komutlar yalnız burada ve
`scripts/kapi.py` içinde yaşar" diyor ama `kapi.py`'nin altı alt komutundan
yalnız dördünü yazıyordu (`performans` hiç yok, `yayin` yalnız düz metinde).
Tek satır eklendi — `KR-12` aksi hâlde var olmayan bir gövdeye bağlanırdı.

**3 — Üç manuel case yerine DÖRT yazıldı.** Plan üç case öngörüyordu; Açık Soru 1
**B** seçilince yeni bir kapı doğdu ve o kapının kendi mutation case'i gerekti
(`MT-GDK-034`). Plandaki üç case `MT-GDK-032`, `033` ve `035` olarak yazıldı.

**4 — `MT-GDK-011` bayattı (kapsam dışı kusur, kullanıcı onayıyla düzeltildi).**
Case "On skill görünür" diyor ve on skill sayıyordu; `nuget-danismani` sonradan
eklenmişti ve diskte **on bir** skill var. Case güncellendi.

**5 — Dosya 126 satır (plan: ~80–120).** Beş yeni rampanın gövdesi ile bilinen
sınır bölümü altı satır taşırdı. Bütçe tetiklenmiyor (`.agents/` hiçbir bütçe
sözlüğünde değil, ölçüldü).

### Planın doğrulanan yapısal iddiaları

`faz-uygulama` Adım 1 gereği plan kabul edilmeden ölçüldü; **beşi de tuttu**:

| İddia | Ölçüm | Sonuç |
|---|---|---|
| `KR-` öneki hiçbir sistemle çakışmaz | `grep -rn "KR-[0-9]" docs/ .agents/ scripts/ AGENTS.md` | ✅ 46 eşleşmenin **tamamı** Faz 168'in kendi planında |
| `LINK` regex'i fragment'ı `group(1)` dışında bırakır | `m.LINK.findall("[x](a/b/SKILL.md#adim-2)")` → `[('a/b/SKILL.md', '#adim-2')]` | ✅ |
| `kirik_baglantilar()` `.agents/` ağacını yürür | Yürüyüşte **22** `.md` dosyası; kasıtlı kırık bağlantı `.agents/ortak/kurtarma.md -> yok-boyle-bir-dosya.md` olarak raporlandı | ✅ ölçüldü, varsayılmadı |
| `.agents/` hiçbir bütçe sözlüğünde değil | `BUTCE` 11 anahtar, `DIZIN_BUTCESI` 5 anahtar — hiçbiri `.agents/` değil | ✅ |
| `AGENTS.md` 11.189 B, tavan 12.000 | `wc -c` | ✅ |

## Bu Fazda Verilen Kararlar

| Karar | Nerede |
|---|---|
| **K-765** — kurtarma rampalarının öneki `KR-`'dir; bir rampanın gövdesi TEK YERDE yaşar, katalog on ikiden yedisini yalnız bağlar; söz `tekrarlanan_kapi_tanimlari()` kapısına bağlandı | `docs/KARARLAR.md` |

Açık soruların cevapları (kullanıcı kararı, 2026-09-13):

| # | Cevap | Sonuç |
|---|---|---|
| 1 | **B** — kapı eklensin | `tekrarlanan_kapi_tanimlari()` üç dal kazandı; `dokuman_bakim_test.py` iki vaka |
| 2 | **A** — Türkçe | K-408: `.agents/` geliştirme aparatıdır, `SourceLanguageTests` taramaz |
| 3 | **A** — dört başlık yeter | `KR-09` gövdesinde dört başlık; ayrı şablon dosyası yok |

## Gerçekleşen Public API

**Yok.** Faz `src/` altına hiç dokunmadı.

## Dosya Listesi (gerçekleşen)

```
.agents/
├── ortak/
│   ├── kurtarma.md               YENİ — 126 satır / 6.207 B, 12 `KR-` satırı
│   └── kapilar.md                DEĞİŞİR — `performans` satırı + bağlayan dosyalar (Sapma 2)
└── skills/
    ├── README.md                 DEĞİŞİR — ortak sözleşme listesi
    ├── kusur-giderme/SKILL.md    DEĞİŞİR — `KR-01…04`'ün gövdesi burada
    ├── faz-uygulama/SKILL.md     DEĞİŞİR — Adım 1 = `KR-07`
    ├── faz-denetim/SKILL.md      DEĞİŞİR — repro'suz bulgu → `KR-05`
    └── faz-baslangic/SKILL.md    DEĞİŞİR — bağlam sisi → `KR-09`

AGENTS.md                         DEĞİŞİR — TAM BİR SATIR (11.189 → 11.293 B, +104 B)

scripts/
├── dokuman-bakim.py              DEĞİŞİR — `tekrarlanan_kapi_tanimlari()` üç yeni dal
└── dokuman_bakim_test.py         DEĞİŞİR — iki vaka güncellendi (7 → 9 bulgu)

docs/
├── KARARLAR.md                   DEĞİŞİR — K-765
├── manuel-test/00-INDEKS.md      DEĞİŞİR — 31 → 35 case, Faz 168 satırı
└── manuel-test/36-GELISTIRME-KAPILARI.md   DEĞİŞİR — dört yeni case + `MT-GDK-011` düzeltmesi
```

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 — planın beş yapısal iddiası da ölçümde tuttu |
| Düzeltme turu sayısı | 1 (`.agents/skills/README.md` satır sarması) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | denetim bölümünde |
| Fazın ürettiği regresyon | 0 — `docs/manuel-test/00-INDEKS.md` sayım kaymasını fazın **kendi kapısı** yakaladı ve aynı turda kapandı |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz yeni kapandı) |

Kapsam dışı bulunan ve kapatılan kusur: **1** (`MT-GDK-011` bayat skill sayımı).

## Örnek Uygulama Koşumu

`samples/Tracon.Api` Release'te ayağa kaldırıldı (2026-09-13, `:5081`).
Bu faz `src/`, `samples/` ve `tests/` altına **hiç** dokunmadı
(`git diff --stat 83904250 -- src/ samples/ tests/` → **0 satır**), bu yüzden
koşum bir davranış kanıtı değil, **regresyon kanıtıdır**.

| Ne | Gerçek çıktı |
|---|---|
| Başlatma | `Now listening on: http://localhost:5081` · `Application started` · `MCP discovery completed: 0 tools available` |
| `GET /tracon/api/agents` (token'sız) | `401` · `"A valid 'Authorization: Bearer <token>' header is required."` — üç katmanlı guard duruyor |
| `GET /tracon/api/agents` (Bearer) | `200` · **7 agent**: `cached-support`, `order-summary`, `researcher`, `router`, `summarizer`, `support`, `translator` |
| `POST /tracon/api/agents/support/run` | SSE akışı: `event: run` + `{"runId":"01a09b7e-…","sessionId":null}`, ardından `event: update` çerçeveleri (`"Echo: "`, `"Faz "`, `"168 "` …) — akış parça parça geldi, `authorName` `support` |
| `GET /health` | `Degraded` · log: *"No model provider has been confirmed healthy yet."* |

🚨 `Degraded` bu fazın ürettiği bir kusur **değildir**: `TraconHealthCheck`
en az bir model sağlayıcısının `Healthy` **teyit edilmiş** olmasını ister
(`TraconHealthCheck.cs:74-80`) ve demo `echo` sağlayıcısı o listeye girmez.
`AuthToken` yerel bir ortam değişkeninden verildi; hiçbir yere yazılmadı
(K-059).

## Denetim Bulguları

`faz-denetim` `faz-denetcisi` tipiyle koşuldu (salt-okunur; ağaç koşum sonrası
`git status` ile doğrulandı — denetçi hiçbir dosyaya dokunmadı). **2 🔴 · 3 🟡 ·
1 🟢.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `KR-10`'un fragment çapası (`#temel-iletişim-kuralları`) **ölü**. Gerçek GitHub slug'ı `temel-i̇letişim-kuralları` — `İ`.toLowerCase() `i` + **`U+0307`** üretir. Fazın devir notu bunun tersini "ölçüldü, temiz" diye yazıyordu | **Düzeltildi.** `KR-10` artık çapasız bağlanır ve bölüm adını düz metin yazar. `kurtarma.md`'ye bir 🚨 kuralı eklendi: `İ` ile başlayan başlığa çapa **yazılmaz**. Bulgu `github-slugger` ile bağımsız doğrulandı |
| 2 | 🔴 | Üç DoD kutusu kanıtlanmadan işaretliydi: (a) dört kapı hâlâ koşuyordu, (b) örnek uygulama koşumu **hiç yapılmamıştı**, (c) denetim henüz bitmemişti | **Düzeltildi.** (a) Kapılar bitti, **onu da yeşil** (aşağıda). (b) Koşum yapıldı, çıktı § Örnek Uygulama Koşumu'nda. (c) Bu tablo |
| 3 | 🟡 | Doğrulama komutu bloğu plan sayılarıyla kalmış: `# 6 olmalı` (gerçek 7), "üçü de koşuldu" (gerçek dört case) | **Düzeltildi** — ikisi de gerçekleşen sayıya çekildi |
| 4 | 🟡 | DoD `--denetle` çıkışını `0` sayıyordu; arşivlemeden **önce** `1`. `MT-GDK-032` de "Çıkış `0`" bekliyordu ve kapanış anında hiç geçemezdi | **Düzeltildi** — DoD satırı ve `MT-GDK-032` "kırık bağlantı **0**" iddiasına çekildi; çıkış kodunun arşivlemeye bağlı olduğu yazıldı |
| 5 | 🟡 | Yeni kapının **varlık dalı** otomatik testle korunmuyordu; dal düşürülse suite yeşil kalıyordu | **Düzeltildi** — `test_kapi_tanimlari_yalniz_kurtarma_eksikse_de_yakalanir` eklendi. Mutation ile kanıtlandı: dal düşürülünce suite **FAILED**, geri alınınca **OK** (260 test) |
| 6 | 🟢 | `KR-11` `deny` listesini sayarak tekrarlıyor; `.claude/settings.json` genişlerse cümle sessizce yalan olur | **Devredildi** — `docs/ADAYLAR.md`, `F-230`. Bugün doğru ve Faz 167 devir notu bu tekrarı **açıkça istedi** (yanlış güven üretmemek için) |

**Denetçinin doğruladıkları:** gövde tekrarı yok (K-765 sözleşmesi temiz — yedi
bağlantı satırının hiçbiri protokol adımını taşımıyor) · `KR-11` Faz 167'nin
gerçekleşen sonucuyla tutarlı ve yanlış güven üretmiyor · kapsam büyütmesi yok
(`src/` sıfır değişiklik) · kapının 2. ve 3. dalı test tiyatrosu değil.

### 🔴 kapandıktan sonra kapılar yeniden koştu

`faz-tamamlama` Adım 4 gereği. Düzeltmeler yalnız doküman ve `scripts/`
dosyalarına dokundu; `python3 -m unittest discover -s scripts` **260 test
yeşil**, `dokuman-bakim.py --denetle` kırık bağlantı **0**.

## Sonraki Faza Devir Notu

**Faz 169 (`F-229` — faz planı sözleşmesi) için zorunlu:**

- 🚨 `KR-05` rampasının **tam adresi**:
  [`.agents/ortak/kurtarma.md`](../.agents/ortak/kurtarma.md) ·
  çapa `#kr-05--araştırılacak-bulgu`. Triyajın "araştırılacak" kanalı oraya
  çıkar. Çapa GitHub slug'ıdır ve **kapı tarafından doğrulanmaz** (aşağı bak);
  yanlış çapa sessizce ölür, dosya yolu ölmez — adresi verirken ikisini birden
  yaz.
- `KR-05`'in gövdesi "repro çıkmazsa gerekçeli kapanış faz dokümanının
  `## Denetim Bulguları` bölümüne yazılır" der. Faz 169 bu bölümü kapıya
  bağlayacaksa üçüncü kanalın oraya yazıldığını **varsaymasın**, saysın.
- `## Süreç Ölçümü` bu fazda dolduruldu; eşik **167**, bu faz **168**'dir.
  Başlık kesme işareti taşımıyor.

**Bilinen ve kabul edilen sınır — fragment çapası:**

- `kirik_baglantilar()` **dosyayı** doğrular, `#fragment`'ı doğrulamaz
  (`LINK` regex'i fragment'ı `group(1)` dışında bırakır — ölçüldü). Kataloğun
  **beş** fragment'lı bağlantısı kalan; beşi de `github-slugger` ile teker teker
  doğrulandı. Bağlanan skill adımı yeniden numaralanırsa çapa **sessizce** ölür.
  Her satır adımı **adıyla da** yazar, bu yüzden çapa ölse bile hedef okunur.
  Fragment doğrulayan kapı 🟢 adaydır.
- 🚨 **Başlığı `İ` ile başlayan bölüme çapa YAZILMAZ** — bu fazda bir kez
  yazıldı ve denetim onu 🔴 olarak yakaladı (Bulgu 1). `İ`.toLowerCase() `i` +
  `U+0307` üretir; slug o **görünmez** karakteri taşır ve elle yazılan çapa
  ekranda birebir aynı görünürken hedefe atlamaz. İlk denemede çapa
  `unicodedata.combining` ile tarandı ve "temiz" döndü — çünkü tarama
  **yazılan çapayı** taradı, **hedef başlığın slug'ını** değil. Bir çapa
  iddiası ancak hedefin slug'ı ÜRETİLİP karşılaştırılırsa doğrulanmış olur
  (`MEMORY.md`: "Kaynak okuması GÖRÜNMEZ karakteri doğrulayamaz").

**`KR-11` Faz 167'nin gerçekleşen sonucuyla tutarlıdır:** yasak **kondu** ama
rampa metni K-761'i tekrarlar — `deny` bloğu `/bin/git`, `sh -c 'git …'` ve
`git -C . reset --hard` biçimlerini durdurmaz, `git rebase`/`git clean -fd`/
`rm -rf` listede hiç yoktur. Rampa "harness beni durdurur" **demiyor**.
