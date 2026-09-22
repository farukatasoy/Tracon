# Faz 180 — Manuel Set Devir Şablonu

> **Durum:** ✅ Tamamlandı (2026-09-22)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-165**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` ve `docs/manuel-test/` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok
> **Manuel test alanı:** setin kendisi — devir işareti tüm aile dosyalarına girebilir

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-166\|K-167\|K-738" docs/KARARLAR.md
   ```
   **K-166/K-167** (birim testleri gerçek hataları kaçırdı; kanıt örnek
   uygulamadan gelir), **K-738** (yük ölçümü rapordur, kapı değildir — devir
   sırasında süre eşiği yazılmaz)
3. [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md) —
   seviye seçim tablosunun tamamı; bu fazın ana aracıdır
4. [`docs/manuel-test/00-INDEKS.md`](../../manuel-test/00-INDEKS.md) — §1 (set
   yapısı) ve §tally (son koşumun sonucu); 36 aile dosyasını **okuma**
5. Alan hafızası: [`hafiza/test-altyapisi.md`](../../hafiza/test-altyapisi.md) ·
   [`hafiza/test-kosum-tuzaklari.md`](../../hafiza/test-kosum-tuzaklari.md)

---

## Amaç

Manuel kabul seti bugün tek regresyon ağıdır ve CI'da koşmaz: 2026-09-16
koşumu üç gün sürdü ve 43 kusur buldu — 4.199 otomatik testin hiçbirinin
görmediği kusurlar. Bu faz seti CI'a **toptan taşımaz** (adayın kendi engeli:
kuyruğu bitmez); bir case'in manuel setten otomatik teste **nasıl devredildiğini**
tek şablona bağlar ve şablonu ilk aile üzerinde uçtan uca kanıtlar.

- **F-165** — tekrar edilebilir devir şablonu + ilk aile dilimi; model-yanıtı
  ve insan-yargısı case'leri açıkça manuel kalır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `docs/manuel-test/` sayımı | **37 dosya, 1.872 benzersiz `MT-*` case** (aday gövdesindeki 1.632 sayısı bayatlamıştı — büyüme sürüyor) |
| [`00-INDEKS.md`](../../manuel-test/00-INDEKS.md) tally | Son tam koşum 2026-09-16→19: 1.813 ☑ · 26 ☐ · 19 ⏭ · 1 ☒; 43 kusur çıktı; yalnız **iki** tam koşum var |
| [`.github/workflows/ci.yml`](../../../.github/workflows/ci.yml) | `docs/manuel-test/` hiçbir job'da geçmiyor — set CI'a görünmez |
| [`scripts/manuel-test-tazelik.py`](../../../scripts/manuel-test-tazelik.py) | Tazelik kapısı var ama yalnız spec bayatlamasını ölçer; devir kavramı yok |

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 180.1 — Devir şablonu: bir case'in üç olası kaderi

Her `MT-*` case'i şablon uygulanınca üç sınıftan birine düşer ve sınıf spec
satırında görünür kılınır:

| Sınıf | İşaret | Kural |
|---|---|---|
| Devredildi | `➜ CI: <TestSınıfı.TestAdı>` | Davranış [test seviyeleri tablosuna](../../../.agents/ortak/test-seviyeleri.md) göre doğru seviyede otomatikleşti. Case spec'ten **silinmez** — koşum talimatı düşer, işaret kalır (tek kaynak: davranış tanımı spec'te, kanıt CI'da) |
| Manuel kalır | `👤 insan gerekir — <sebep>` | Model kalitesi, görsel yargı, fiziksel ortam (ses cihazı, gerçek sağlayıcı hesabı). Sebep tek cümle |
| Zaten kapsanıyor | `➜ CI: <mevcut test>` | Devir yeni test yazmaz; mevcut testi işaret eder. İlk dilimde beklenen en kalabalık sınıf budur |

Seviye kuralı plandan seçilir, uygulama oturumundan değil: sınır geçen davranış
(DI · HTTP · kiracı · akış · depo · paket) birim testine **devredilemez**.

## 180.2 — İşaretin makine tarafı

`scripts/manuel-test-tazelik.py` devir işaretini sayar ve rapora üç sütun ekler:
aile başına `devredildi / manuel / işaretsiz`. Kapı kuralı **cırcır değildir** —
bu faz yalnız saymayı kurar; "işaretsiz azalmalı" kuralı ilk dilimin ölçümü
görüldükten sonra ayrı kararla bağlanır. `➜ CI:` işaretinin gösterdiği test
sınıfının gerçekten var olduğu doğrulanır (bayat işaret = kırmızı).

## 180.3 — İlk aile dilimi

Bir aile uçtan uca devredilir ve şablonun gerçek maliyeti ölçülür: kaç case
devredildi, kaç yeni test yazıldı, kaç mevcut teste işaret verildi, kaç case
manuel kaldı. Bu sayılar kapanışta **ölçülür**, planda tahmin edilmez.

---

## Planlanan Public API

Yok — sevk edilen paketlere dokunulmaz.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
docs/manuel-test/<ilk-aile>.md        (devir işaretleri)
docs/manuel-test/00-INDEKS.md         (devir şablonu bölümü)
scripts/manuel-test-tazelik.py        (devir sayacı)
scripts/manuel_test_tazelik_test.py   (sayacın testleri)
tests/<ilgili proje>/                 (dilimin yeni testleri)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Devir işareti var olmayan bir test sınıfını gösteriyor | Script birimi | `manuel_test_tazelik_test.py` — bayat işaret vakası |
| Sınır davranışı birim teste devredilip test tiyatrosu oluşuyor | Denetim | `faz-denetim` — dilimin her devri seviye tablosuna karşı okunur |
| Devredilen case'in spec metni ile testin iddiası ayrışıyor | Script birimi | işaret + test adı eşlemesi raporda görünür; koşumda insan karşılaştırır |
| Sayaç aile dosyasındaki tablo biçim varyantlarını kaçırıyor | Script birimi | mevcut ayrıştırıcı testlerine varyant vakaları eklenir |

Beş soru bu fazda script yüzeyine uygulanır: iptal (yok — script kısa ömürlü) ·
eşzamanlılık (yok) · boş/aşırı girdi (boş aile dosyası, işaretsiz set) · başka
kiracı (yok) · alt sistem hatası (git yoksa açık hata).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Devir işaretli aile | `python3 scripts/manuel-test-tazelik.py` | Rapor aile için `devredildi/manuel/işaretsiz` sayılarını basar |
| 2 | İşaret bayat (test silindi) | Aynı komut | Kırmızı; bayat işaret adıyla listelenir |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | İlk aile hangisi? | A: `07-HTTP-YONETIM-API` · B: `02-CEKIRDEK-VE-KATALOG` | **A seçildi** (farukatasoy, 2026-09-22) |
| 2 | `➜ CI:` işaretinin tam sözdizimi | A: satır sonu eki · B: ayrı sütun | **İkisi de, TEK ev kuralıyla** — bkz. Plandan Sapmalar |

---

## Bitiş Ölçütleri (DoD)

- [x] Devir şablonu `00-INDEKS.md`'de: üç sınıf, işaret sözdizimi, seviye kuralı
- [x] İlk aile dilimi devredildi; dört sayı (devredilen/yeni test/mevcut teste işaret/manuel) belgeye yazıldı
- [x] `manuel-test-tazelik.py` devir sayacı ve bayat-işaret kırmızısı çalışıyor; testleri yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü
- [x] Manuel kabul case'leri bu fazın alanına eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı

### Doğrulama komutları

```bash
python3 scripts/manuel-test-tazelik.py
python3 -m unittest discover -s scripts -p "manuel_test_tazelik_test.py"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Kör devir test tiyatrosu üretir (adayın kendi uyarısı) | Seviye tablosu plana gömülü; `faz-denetim` dilimin her devrini seviyeye karşı okur |
| Çift kaynak: spec ile test ayrı yaşar | Case spec'ten silinmez, işaret tek yönlü gösterir; sayaç bayat işareti kırmızı yapar |
| Şablon ilk ailede işe yarar, ikincide yaramaz | Şablon bu fazda yalnız **bir** ailede kanıtlanır; genelleme sonraki dilim fazlarına kalır |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

| Plan | Gerçek | Gerekçe |
|---|---|---|
| Açık Soru 2: işaret "satır sonu eki" ya da "ayrı sütun" | **İkisi de, ama TEK ev kuralıyla**: başlık biçiminde `\| **Devir** \| … \|` satırı, tablo biçiminde `Devir` sütunu | İki biçim iki ev ister; ortak olan, işaretin gövde metnine karışmamasıdır. Gövdeye karışsaydı ön koşul metnindeki bir `👤` cümlesi sayıma girerdi — denetim bunu **ölçtü** (devredilmemiş dört ailede yedi sahte `manuel`) |
| 180.2: "`manuel-test-tazelik.py` devir işaretini sayar" | `--taban` **isteğe bağlı** oldu; bayraksız mod yalnız devir raporunu basar ve dosya yazmaz | Fazın kendi doğrulama komutu bayraksızdı. Ayrı bir betik case ayrıştırıcısını kopyalardı (K-411 sınıfı); zorunlu `--taban` ise tabana ihtiyaç duymayan bir ölçüme anlamsız bir argüman dayatırdı |
| Planda yok | **İşaret case imzasından düşürüldü** | Plan bunu öngörmemişti. Düşürülmeseydi bu fazın 43 işareti bir sonraki tazelik ölçümünde **43 sahte `değişti`** üretirdi — `YENIDEN_ADLANDIRMALAR`'ın kapattığı sınıfın aynısı. Ölçüldü: `0` sahte `değişti` |
| Planda yok | **Aile 07'de altı bayat case metni düzeltildi** | Dilim, spec metninin bugünkü kodla çeliştiği beş `title` alıntısı (K-228 öncesi Türkçe) ve kendi kayıtlı sonucuyla çelişen bir case başlığı (`MT-API-054`) buldu. İşaretlemek bunları görünür kıldı — şablonun beklenmedik ikinci faydası |
| Planda yok | **Bir betik kusuru düzeltildi** | `--kosum` repo DIŞINDA bir dizin alınca `relative_to` ham `ValueError` traceback'i atıyordu (dosya yazıldıktan SONRA). Betiğin kendi kabul kuralı (`MT-GDK-022`) ham traceback'i yasaklar |
| 180.3: "kaç yeni test yazıldı" | 13 yeni test metodu + **9 mevcut testin genişletilmesi** | Denetim, işaretlenen testlerin bir kısmının case'in yalnız durum kodunu sınadığını gösterdi. "Zaten kapsanıyor" demek için testin case'in **ayırt edici** iddiasını da sınaması gerekir; eksik iddialar eklendi |

## Bu Fazda Verilen Kararlar

Karar defterine giren yok. Üçü de yerel tercihtir ve burada yaşar:

1. **İşaretin tek evi `Devir` satırı/sütunudur.** Gövdede geçen bir `👤`
   cümlesi işaret değildir.
2. **`➜ CI:` ile `👤` bir aradaysa `➜ CI:` kazanır.** Kanıtın CI'da olduğu
   iddiası doğrulanabilir; "insan gerekir" doğrulanamaz.
3. **`işaretsiz` bir kapı değildir.** Cırcır kurmak, yargılanmamış case'leri
   gelişigüzel işaretlemeye davet ederdi (180.2'nin kendi kuralı).

## Gerçekleşen Public API

Yok — `src/` değişmedi. Yalnız `tests/`, `scripts/` ve `docs/`.

## Dosya Listesi (gerçekleşen)

```
docs/manuel-test/00-INDEKS.md               §9 devir şablonu · §7 aile 36 satırı
docs/manuel-test/07-HTTP-YONETIM-API.md     43 devir işareti · 6 bayat metin düzeltmesi
docs/manuel-test/36-GELISTIRME-KAPILARI.md  MT-GDK-049 · 050 · 051
scripts/manuel-test-tazelik.py              devir sayacı · tabansız mod · traceback düzeltmesi
scripts/manuel_test_tazelik_test.py         +13 test (43 toplam)

tests/Tracon.AspNetCore.FunctionalTests/
  StatsErrorEndpointTests.cs      YENİ — /api/stats/errors ve /api/stats sayaçları
  ListPagingClampTests.cs         YENİ — /api/sessions ve /api/runs sayfalama kırpması
  CatalogEndpointTests.cs         YENİ — /api/tools ve /api/models
  AgentCrudTests.cs               +2 test, +3 iddia
  AgentValidateEndpointTests.cs   +1 iddia (severity ad olarak)
  IdempotencyTests.cs             +4 iddia (title · detail · gövde eşitliği)
  JsonBindingProblemMiddlewareTests.cs  +1 test (bozuk JSON)
  MetaEndpointTests.cs            +1 iddia (roles bloğu)
  ProblemDetailsTests.cs          +1 iddia (type alanı)
  RunReplayEndpointTests.cs       +1 test (RecordRunInput=false)
  SecurityTests.cs                +1 iddia (detail token sızdırmaz)
  SessionEndpointTests.cs         +1 iddia · 1 yanıltıcı test adı düzeltildi
```

## Süreç Ölçümü

**Dilimin dört sayısı** (DoD):

| Ölçüm | Değer |
|---|---|
| Devredilen case | **42** / 43 |
| Yeni yazılan veya genişletilen teste işaret eden case | **12** |
| Dokunulmamış mevcut teste işaret eden case | **30** |
| Manuel kalan case | **1** (`MT-API-040`) |
| İşaretsiz kalan case | **0** |

Devredilen 42 case **51 benzersiz testi** gösterir; hepsi
`Tracon.AspNetCore.FunctionalTests` altındadır — HTTP sınırı geçen hiçbir
davranış birim testine devredilmedi. Yeni test metodu: **13**; genişletilen
mevcut test: **9**.

`MT-API-040` neden manuel kaldı: iddiası, **gerçek** bir sağlayıcı
istisnasının `ProviderError` sınıfına düşmesidir (K-296). Sahte sağlayıcı bunu
kanıtlayamaz — gruplamanın kendisi devredildi
(`StatsErrorEndpointTests.Repeated_provider_failures_are_grouped_under_one_error_class`),
sınıflandırma kalmadı. Kapanışta örnek uygulamayla **gözlendi**: gerçek OpenAI
`404`'ü `class: "ProviderError"`, `ClientResultException`, `totalRuns: 2`,
tek küme `count: 2` verdi.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 |
| Düzeltme turu sayısı | 1 (denetim sonrası) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 |
| Faz kapandıktan sonra bulunan kusur | — |

## Denetim Bulguları

Bağımsız denetçi (`faz-denetcisi`, taze bağlam) bir 🔴, beş 🟡 ve üç 🟢 buldu.

**🔴-1 — kapatıldı.** Beş case `➜ CI:` ile "kanıtı CI'da" ilan edilmişti; ama
BEKLENEN SONUÇ metinleri K-228 öncesi Türkçe `title` alıntıları taşıyordu ve
gösterilen testler yalnız durum kodunu sınıyordu. İşaret, **zaten yanlış olan**
bir case'i kanıtlı gösteriyordu. İki yönlü kapatıldı: metinler bugünkü
İngilizce başlıklara düzeltildi **ve** `IdempotencyTests` ile
`ProblemDetailsTests`'e `title`/`detail`/`type` iddiaları eklendi.

**🟡 beşi de kapatıldı:**

1. Yedi case'in kapsanmayan davranışsal iddiası — versiyon listesi sıralaması,
   reddedilen `DELETE` sonrası agent'ın durması, replay gövdesinin birebir
   aynılığı, ikinci `DELETE`'in `404`'ü, `meta`'nın `roles` bloğu, `401`
   `detail`'inin token sızdırmaması, `ProblemDetails` `type` alanı — hepsine
   iddia eklendi; `MT-API-100`'ün işareti `400` ve `403` testleriyle genişletildi.
2. `SessionEndpointTests.Missing_session_cannot_be_branched_returns_404` **501**
   iddia ediyordu → `Missing_session_branch_reports_not_supported_BEFORE_not_found`.
   `MT-API-054`'ün başlığı da kendi kayıtlı sonucuyla çelişiyordu; düzeltildi.
3. İmzadan düşürme yalnız başlık biçimini kapsıyordu → ayrıştırıcı `CaseBloku`
   ile yeniden yapılandırıldı; `Devir` **sütunu** da düşüyor (hücre boşaltılmaz,
   **düşürülür** — boş bırakmak satıra fazladan bir ayırıcı ekler ve imza yine ayrışırdı).
4. `👤` sayacı gövde metnini sayıyordu (yedi sahte `manuel`) ve kalın yazılmış
   bir işareti kaçırıyordu → tek ev kuralı ikisini birden kapattı.
5. DoD'nin istediği dört sayı → yukarıdaki Süreç Ölçümü tablosu.

**🟢 üçü aday listesine:** `test_envanteri`'nin dosya düzeyinde
`sınıf × metot` çarpımı (bilinçli, tek yönlü güvenli) · `ListPagingClampTests`
iki satırlık tohumla `200` tavanını gözlemleyemiyor · `--kosum`, `--taban`
yokken sessizce yok sayılıyor.

## Sonraki Faza Devir Notu

**Şablon bir ailede kanıtlandı, genelleme sonraki dilimlere kalıyor.**
Kalan 1.832 case işaretsizdir ve bu bir kusur değildir.

Sonraki dilimi alacak oturuma:

- **İşaretlemeden önce case'in BEKLENEN SONUÇ'unu testin gövdesiyle satır satır
  karşılaştır.** Bu fazın tek 🔴'ı ve beş 🟡'ından üçü buradan çıktı: durum kodu
  eşleşiyor diye "zaten kapsanıyor" demek, case'in ayırt edici iddiasını
  (`title` metni, sıralama, ikinci silme, gövde eşitliği) sessizce kanıtsız bırakır.
- **Tablo biçimli bir aile (31–36) ilk kez devredildiğinde** `Devir` sütununu
  başlık satırına ekle; sayaç sütunu adından bulur ve imzadan düşürür. Bu yol
  `manuel_test_tazelik_test.py`'de kilitlidir, ama gerçek bir aile üzerinde
  henüz koşulmadı.
- **İşaretlemek bayat spec metni bulur.** Aile 07'de altı tane çıktı. Bu bir yan
  ürün değil, dilimin ikinci değeridir — bulduğunu düzelt ve say.
- **"İşaretsiz azalmalı" kuralını bağlamak için yeterli ölçüm yok.** En az bir
  dilim daha gerekir; o zamana kadar sayı rapordur, kapı değildir (180.2).
