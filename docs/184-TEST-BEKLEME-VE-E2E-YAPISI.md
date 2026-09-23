# Faz 184 — Test Bekleme ve E2E Yapısı

> **Durum:** ✅ Tamamlandı (2026-09-23)
> **Plan onayı:** farukatasoy, 2026-09-22 (beş fazlık tur onayı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-261**
> **Önkoşul:** Yok
> **Paketler:** yalnız `tests/` — sevk edilen paket değişmez
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyümüyor
> **Tüketici yüzeyi:** Yok · sevk edilen: Yok
> **Manuel test alanı:** `docs/manuel-test/09-ARAYUZ-GENEL.md` ve komşu arayüz aileleri — E2E bölünmesi bu ailelerin `➜ CI` işaretlerini etkileyebilir (Faz 180 şablonu)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Alan hafızası — bu fazın ana okuma kümesi:
   [`hafiza/test-paralellik-ve-zamanlama.md`](hafiza/test-paralellik-ve-zamanlama.md) ·
   [`hafiza/test-kosum-tuzaklari.md`](hafiza/test-kosum-tuzaklari.md) ·
   [`hafiza/test-yalitimi.md`](hafiza/test-yalitimi.md) — ölçülmüş altı zamanlama
   kusuru ve serileştirme kararlarının gerekçesi buradadır; bu faz o kararları
   **geri almadan önce** sebeplerini söker
3. Kusur protokolü: [`kusur-giderme` SKILL](../.agents/skills/kusur-giderme/SKILL.md)
   Adım 2 — kırılgan test ayrıştırma komutları bu fazda yoğun kullanılır
4. [`tests/Tracon.Ui.E2ETests/xunit.runner.json`](../tests/Tracon.Ui.E2ETests/xunit.runner.json)
   ve [`ci.yml:211`](../.github/workflows/ci.yml) — bugünkü serileştirme yüzeyi

---

## Amaç

Testler duvar saatine yaslanıyor: senkronizasyon yerine sabit `Task.Delay`,
istikrar yerine serileştirme. Bedel iki katlı — tam koşum ~557 sn (ölçüm:
`kapi.py` yorumu, 2026-09-04) ve sabit bekleme yavaş makinede yine kırılgan.
E2E tarafında 80 test tek 145 KB dosyada ve 7 web-first `Expect`'e karşı 95
navigation var: düşen test "timeout" der, "hangi element eksikti" demez.

- **F-261** — sabit beklemeleri koşul-beklemeye çevir; `UiTests.cs`'i ekran
  başına dosyalara böl; `WaitForAsync` kalıplarını web-first `Expect`'e taşı.
  Paralellik artırımı **ayrı ve ölçüm-kapılı** son adımdır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| `grep -rE "Task\.Delay\(" tests` | **121 satır**; senkronizasyon amaçlı sabit örnekler: [`DrainTests.cs:110`](../tests/Tracon.AspNetCore.FunctionalTests/DrainTests.cs) (200 ms), [`ToolGovernanceEndpointTests.cs:326`](../tests/Tracon.AspNetCore.FunctionalTests/ToolGovernanceEndpointTests.cs) (1 sn) |
| `tests/Tracon.Ui.E2ETests/UiTests.cs` (bu fazda silindi) | **144.937 bayt, 80 test tek dosyada**; `Expect(` yalnız **7** |
| [`xunit.runner.json`](../tests/Tracon.Ui.E2ETests/xunit.runner.json) | `maxParallelThreads: 4` + `conservative` |
| [`ci.yml:211`](../.github/workflows/ci.yml) | `-maxcpucount:1` — çözüm düzeyi serileştirme |
| [`kapi.py:695`](../scripts/kapi.py) | Tam koşum ölçümü 557 sn (TRX'li) |

> Kanıtlar 2026-09-22 tarihinde doğrulandı.

---

## 184.1 — Sabit beklemeden koşul-beklemeye

121 `Task.Delay` satırı üç sınıfa ayrılır ve sınıfı satırın yanına yazılır:

| Sınıf | Kader |
|---|---|
| Senkronizasyon (bir durumun oluşmasını bekliyor) | Koşul-bekleme: durumu yoklayan `WaitUntilAsync(koşul, zamanAşımı)` yardımcıları veya ürünün kendi sinyali (`SSE` olayı, store sorgusu, `TaskCompletionSource`) |
| Ürün davranışının parçası (ör. lease süresi dolmalı) | Kalır; yorumu sınıfını söyler — sayaç bunları ayırt eder |
| Gerçek zaman kısıtı taklidi | Test zamanı kısaltılabilir yapılandırmaya bağlanır (seçenek zaten varsa) |

Yardımcı `tests/Shared/` altına girer; her dönüşüm tek tek commit edilir ve
`kusur-giderme` Adım 2 komutlarıyla (paket koşumu + izole koşum) kırılganlık
ölçülür. Dönüşüm bir testi düşürürse bu **bilgidir** — sabit bekleme gerçek
bir yarışı gizliyordu; kusur ayrı kaydedilir, sessiz geçilmez.

## 184.2 — `UiTests.cs` bölünmesi

Ekran başına dosya (`Ui/DashboardTests.cs`, `Ui/PlaygroundTests.cs`, …);
paylaşılan akış `BrowserFixture` + yardımcılarda kalır. Bölme **davranış
değiştirmez** — `git log --follow` kaybını göze alıp içerik birebir taşınır.
`PlaywrightLocatorTests` kapısı (küçülen taban) bölünmeden etkilenmemeli.

## 184.3 — `WaitForAsync` → web-first `Expect`

95 navigation'lık akışta bekleme kalıpları `Expect(locator).ToBeVisibleAsync()`
ailesine çevrilir: düşüş mesajı "timeout" yerine eksik elementi söyler.
Dönüşüm ekran dosyası başına yapılır; her dosyada dönüşüm sonrası koşum kanıtı.

## 184.4 — Paralellik: yalnız ölçümle

Serileştirme kararları (`-maxcpucount:1`, `maxParallelThreads: 4`) ölçülmüş
altı zamanlama kusurunun sonucuydu — sebepler 184.1'de söküldükten **sonra**,
tek değişken oynatılarak ölçülür: önce E2E `maxParallelThreads`, sonra çözüm
düzeyi. Süre ve kırmızı sayısı kapanışa yazılır; hedef süre **yazılmaz**
(ölçülmemiş rakam yazılmaz kuralı).

---

## Planlanan Public API

Yok.

### HTTP `endpoint`'leri

Yok.

### Arayüz payı

Yok.

---

## Planlanan Dosya Listesi

```
tests/Shared/WaitUntil.cs                     (koşul-bekleme yardımcıları)
tests/Tracon.Ui.E2ETests/Ui/<Ekran>Tests.cs   (bölünmüş E2E)
tests/Tracon.Ui.E2ETests/UiTests.cs           (silinir — içerik taşınır)
tests/** (121 satırın dokunulan kısmı)
.github/workflows/ci.yml                      (yalnız 184.4 ölçümü gerektirirse)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Koşul-bekleme sonsuz döngüye girer | Birim (yardımcının kendisi) | `WaitUntilTests` — zaman aşımı ve iptal vakaları |
| Dönüşüm gizli yarışı açığa çıkarır (test düşmeye başlar) | Süreç | `kusur-giderme` Adım 2 ayrıştırması; kusur F-kaydı alır, geri sabit beklemeye dönülmez |
| E2E bölünmesi locator kapısını bozar | Repo kapısı | `PlaywrightLocatorTests` küçülen taban — bölünme sonrası aynı sayı |
| Paralellik artırımı gizli paylaşımı tetikler | Ölçüm | tek değişkenli koşum; kırmızı çıkarsa o adım geri alınır ve sebep hafızaya yazılır |
| `Expect` dönüşümü iddiayı zayıflatır (yanlış locator'a bakar) | Gözden geçirme | dönüşüm ekran başına ayrı commit; `faz-denetim` örneklem okur |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Faz bitti | Tam paket koşumu ×2 (arka arkaya) | İki koşumda da aynı sonuç; kırmızı yok |
| 2 | E2E bölündü | Bir E2E dosyasını tek başına koştur | Ekran dosyası bağımsız koşuyor; fixture paylaşımı çalışıyor |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | 121 satırın hepsi bu fazda mı? | A: hepsi sınıflandırılır, senkronizasyon sınıfının tamamı dönüştürülür · B: yalnız fonksiyonel testler, entegrasyon sonraya | **A** sınıflandırma + **B** dönüşüm — sınıflandırma ucuzdur ve tam yapılır; dönüşüm en kalabalık ve en kırılgan aileden başlar, faz sonunda kalan varsa sayısıyla devredilir |
| 2 | `WaitUntil` yardımcısı `Tracon.Testing`'e (sevk edilen) girer mi? | A: hayır, `tests/Shared` · B: evet, tüketici de ister | **A** — sevk edilen yüzey büyütmek Faz 182 ile ters yönde; B ancak dış talep kanıtıyla |

---

## Bitiş Ölçütleri (DoD)

- [x] 123 satırın sınıflandırması tamam (plan 121 diyordu; taban HEAD'de 123 ölçüldü); senkronizasyon sınıfının **63 satırının 63'ü** dönüştürüldü, kalan yok — tablo "Plandan Sapmalar" § 1'de. Kalan 50 + `WaitUntil`'in kendi satırı `// delay: <sınıf>` taşır ve `TestDelayClassificationTests` bunu zorlar
- [x] `UiTests.cs` silindi; 23 ekran sınıfı `tests/Tracon.Ui.E2ETests/Ui/` altında koşuyor. `PlaywrightLocatorTests` tabanı **bölünmeyle değişmedi** (tek dosya 127 = bölünmüş 127, düzeltilmiş tarayıcıyla ölçüldü). Tarayıcı kusurunun sakladığı borç ödendi: kayıtlı taban faz başındaki **119'dan 116'ya indi** — § 3, denetim 🔴 1
- [x] Dönüştürülen E2E dosyalarında `Expect` kullanımı (217 `WaitForAsync` → 0); düşüş mesajı örneği aşağıda, "Süreç Ölçümü" altında
- [x] Tam paket koşum süresi önce/sonra ölçüldü ve yazıldı — "Süreç Ölçümü" tablosu
- [ ] Dört doğrulama kapısı sıfır uyarı verir — "Kapanış Kapısı"
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — "Örnek Uygulama Koşumu"
- [ ] `secret` taraması boş döndü — `kapanis`'in ilk komutu
- [x] Manuel kabul case'leri eklendi ve koşuldu — `MT-GDK-052…055` (aile 36); 052 · 053 · 055 koşuldu, 054 kapanış koşumlarıyla
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — 1 🔴 · 2 🟡 · 2 🟢, hepsi düzeltildi ("Denetim Bulguları")

### Doğrulama komutları

```bash
grep -rE "Task\.Delay\(" tests --include="*.cs" | wc -l   # sınıflandırma yorumu taşımayan satır kalmamalı
python3 scripts/kapi.py ic-dongu
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Dönüşüm dalgası koşumu uzun süre kırmızı tutar | Ekran/dosya başına küçük adımlar; her adım kendi koşum kanıtıyla |
| Serileştirmeyi erken gevşetmek eski kusurları geri getirir | 184.4 en sona; tek değişken; hafızadaki altı vaka önce okunur |
| Bölünmüş E2E dosyaları fixture'ı yanlış paylaşır | `BrowserFixture` sözleşmesi bölünmeden önce yazılır; collection tanımı açık |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **Sayı ve sınıflar ölçüldü, plan ölçmemişti.** Taban `HEAD`'de `Task.Delay(`
   satırı **123**'tü (plan 121). Planın üç sınıfı yetmedi; satırlar sekiz
   sınıfa ayrıldı ve her kalan satır sınıfını `// delay: <sınıf>` ile taşır:

   | Sınıf | Satır | Dönüştürülen | Kalan |
   |---|---|---|---|
   | Senkronizasyon — özel yoklama döngüsü | 48 | 48 | 0 |
   | Senkronizasyon — durumu bekleyen sabit uyku | 15 | 15 | 0 |
   | `bound` — zamanlayıcıyla yarışan arıza sınırı | 6 | 5 | 1 |
   | `product` — geçen süre iddianın kendisi | 3 | 1 | 2 |
   | `simulated` — sahte, yavaş/asılı işi taklit eder | 43 | 4 | 39 |
   | `negative` — pozitif sinyali olmayan "olmadı" penceresi | 2 | 0 | 2 |
   | `retry` — altyapı çağrısının yeniden denemesi | 3 | 0 | 3 |
   | `fixture` — derlenen/analiz edilen örnek kaynak metni | 3 | 0 | 3 |
   | **Toplam** | **123** | **73** | **50** |

   Kalan 50'ye `WaitUntil`'in kendi `poll` satırı eklenir: bugün **51** satır,
   hepsi etiketli. Açık Soru 1'in önerisi "dönüşüm B: fonksiyonel testlerden
   başla, kalanı devret" idi; dönüşüm **tamamı** yapıldı, devreden satır yok.
2. **Planın iki örneği yanlış sınıftaydı.** `DrainTests.cs:110` senkronizasyon
   değil **negatif pencereydi** (işçi işi almamalı); pozitif kanıta çevrildi —
   işçi her tikte drain durumunu okur, test üç okumayı bekler.
   `ToolGovernanceEndpointTests.cs:326` ise `product`'tır: onayın aracın kendi
   200 ms sınırından uzun sürmesi iddianın **öncülüdür**; kaldı ve etiketlendi.
3. **🚨 `PlaywrightLocatorTests` kapısı kusurluydu; taban 119 → 127.** Tarayıcı
   her satırı ilk `//`'dan kesiyordu, string içinde de.
   `GetByPlaceholder("https://mcp.example.com/mcp")` kapanış parantezini
   kaybetti, parantez yürüyüşü dosya sonuna koştu ve tarayıcı `UiTests.cs`'in
   kalanında `GetByPlaceholder` saymayı **bıraktı**: sekiz çağrı, yedisi riskli,
   hiç sayılmadı. Kusuru bölünme gösterdi — aynı çağrılar tek dosyada 119,
   bölünmüş hâlde 126 sayıldı. Tarayıcı artık string ve karakter literallerini
   okur (normal, verbatim, ham, interpolasyonlu); dengesiz çağrı taramayı
   bitirmez, sayılır. Düzeltilmiş tarayıcıyla tek dosya **127**, bölünmüş hâl
   **127**: bölünme sayıyı değiştirmedi. Denetim büyüyen kayıtlı tabanı 🔴 saydı
   (taban yalnız küçülür); borç ödendi — tam placeholder metnini veren on bir
   `GetByPlaceholder` çağrısı `Exact = true` aldı. Kayıtlı taban **119 → 116**.
4. **Paylaşılan E2E düzeni taban sınıf değil `using static`.** Plan
   "`BrowserFixture` + yardımcılar" diyordu. Ortak taban sınıf denendi: `UiHost`
   `internal` olduğu için korumalı yardımcı imzaları derlenmez (CS0051).
   Oturum üst düzey `Infrastructure/Session.cs` oldu; iki veya daha çok sınıfın
   kullandığı yardımcılar `Infrastructure/UiTestHelpers.cs`'e geçti ve
   `using static` ile çağrılır — test gövdeleri **bayt bayt** aynı kaldı
   (yalnız görünürlük değiştiriciler farklı, satır karşılaştırmasıyla
   doğrulandı). Tek sınıfın kullandığı yardımcı o sınıfa gitti. Faz işaretçisi
   yorumları (`// --- Phase 16 …`) düştü; dosya adları gruplamayı taşır.
5. **184.3 plandan geniş yapıldı.** 217 `WaitForAsync` (plan "95 navigation"
   diyordu; o sayı `GotoAsync`'ti) `Expect`'e çevrildi, ek olarak **37 tek
   atımlık okuma** (`CountAsync`, `InputValueAsync`, `IsDisabledAsync`,
   `GetAttributeAsync`, `InnerTextAsync` + Shouldly) yeniden deneyen
   assertion'a döndü — F-122'nin ölçülmüş yarışı tam bu biçimdi. `Expect`'in
   kendi varsayılan sınırı 5 sn'dir; `BrowserFixture` onu eski beklemelerin
   30 sn'sine çıkarır ve `Expect` içindeki ≤30 sn açık sınırlar düştü (iki
   60 sn'lik ses sınırı kaldı).
6. **Planın "WaitForAsync hangi elementin eksik olduğunu söylemez" iddiası
   yarı yanlıştı.** Ölçüldü: eski mesaj da çağrı günlüğünde locator'ı yazar
   (`waiting for GetByRole(... "Dashbord") to be visible`). Kazanç başka
   yerdedir: `Expect` bir **assertion** olarak düşer ("Locator expected to be
   visible — element(s) not found") ve sayfanın o anki **aria snapshot**'ını
   ekler — beklenen başlığın yanında gerçekte ekranda olan (`heading
   "Dashboard"`) görünür. Örnek "Süreç Ölçümü" altında.
7. **Dönüşüm ve paralel koşum dokuz gizli yarışı açtı; hepsi düzeltildi.**
   Plan "dönüşüm bir testi düşürürse bu bilgidir" diyordu:
   - İptal kaydı `run` satırından **önce** yazılır; `ActiveCount == 1`'i
     bekleyip satırı okuyan dört Core testi yarıştı (net10 bacağında ölçüldü).
   - `SandboxedSkillScriptRunnerTests`: süreç geneli `ActivityListener`'ın
     `List`'i başka testin iş parçacığında büyüyordu ("Collection was
     modified").
   - `ChildAgentInvokerTests`, `ScopedToolTests`: `CancelAfter(20/50 ms)`
     gövde girmeden ateşleyebiliyordu.
   - Ses testi `commit`'e ses gelmeden basıyordu — altı fazda "yavaş makine"
     sanılan kırılganlığın kök sebebi (hafıza vakalar dosyası).
   - Tooltip testi rozet sayısını liste yüklenmeden okuyordu; tema testi aynı
     biçimde (henüz düşmemişti, önleyici).
8. **Canlı ses sınıflarının sınırı 60 sn'ye çıktı.** Üç sınıf hâlâ
   `Patience = 10 sn` taşıyordu (Faz 173 yalnız yaşam döngüsü sınıfını
   düzeltmişti); değer `LiveVoiceTests.Patience`'ta tek yerde.
9. **Yeni kapı: `TestDelayClassificationTests`.** Plan yalnız bir `grep`
   komutu veriyordu; etiket kuralı bir teste bağlandı — etiketsiz satır,
   bilinmeyen sınıf veya `WaitUntil` dışında `poll` kırmızı olur.
10. **Plan dışı kusur: `manuel-test-tazelik.py` sözleşme testlerini
    görmüyordu.** Envanter yalnız `tests/`'i tarıyordu; `MT-SEC-199`'un iki
    `➜ CI:` hedefi sevk edilen `src/Tracon.Testing.Contracts.Xunit`'te var
    olduğu hâlde bayat raporlanıyor, betik temiz ağaçta `1` dönüyordu.
11. **184.4 sonucu** — "Bu Fazda Verilen Kararlar" 3 ve 4.

## Bu Fazda Verilen Kararlar

Hepsi test altyapısıdır; `K-*` açılmadı (public API, güvenlik, veri veya
geri dönüşü pahalı sistem kararı yok).

1. **Bekleme tek yardımcıdadır.** `WaitUntil` (`tests/Shared/Waiting/`) her
   test projesine bağlıdır; sınır arızayı keser, varsayılan 30 sn. Açık Soru 2:
   **A** — sevk edilen `Tracon.Testing`'e girmedi.
2. **Negatif iddia önüne sabit uyku yerine pozitif sinyal.** Sinyal bulunamayan
   iki satır `negative` etiketiyle kaldı.
3. **E2E sınıfları paralel koşar** (`maxParallelThreads: 4`, `conservative`).
   Ölçüm "Süreç Ölçümü"nde; iki yarış düzeltildikten sonra 5/5 yeşil.
4. **Yerel kapı iki test projesini aynı anda koşar** (`kapi.py`
   `TEST_MAX_CPU_COUNT = 2`). Ölçüm aynı kod ve makinede: 1 işçi 863 · 879 sn,
   2 işçi 607 · 658 · 524 sn; beş koşum da 16.962 testte 0 kırmızı. Üç işçi
   Faz 100'de birbirini aç bırakıyordu; iki işçi, sabit beklemeler kalkınca
   yeşil kaldı. **CI 1'de kaldı** — runner donanımı ölçülmedi, `ci.yml`
   gerekçesini yazar. Geri dönüş kuralı: izole tekrarda geçen bir kaynak
   çekişmesi kırmızısı görülürse değer 1'e döner ve hafızaya yazılır.

## Gerçekleşen Public API

Yok. Faz yalnız `tests/`, `scripts/manuel-test-tazelik.py` ve dokümanlara
dokundu; sevk edilen hiçbir paket değişmedi.

## Dosya Listesi (gerçekleşen)

```
tests/Shared/Waiting/WaitUntil.cs                                   (yeni)
tests/Directory.Build.props                                         (bağlantı + global using)
tests/Tracon.Core.UnitTests/TestInfrastructure/WaitUntilTests.cs    (yeni, 9 test × 3 TFM)
tests/Tracon.Core.UnitTests/Architecture/TestDelayClassificationTests.cs  (yeni kapı, 6 test × 3 TFM)
tests/Tracon.Core.UnitTests/Architecture/PlaywrightLocatorTests.cs  (literal bilen tarayıcı, +2 test)
tests/Tracon.Core.UnitTests/Architecture/playwright-locator-baseline.txt (dosya başına, toplam 127)
tests/Tracon.Ui.E2ETests/UiTests.cs                                 (silindi)
tests/Tracon.Ui.E2ETests/Ui/<Ekran>Tests.cs                         (23 yeni sınıf)
tests/Tracon.Ui.E2ETests/Infrastructure/{Session,UiTestHelpers}.cs  (yeni)
tests/Tracon.Ui.E2ETests/Infrastructure/BrowserFixture.cs           (Expect varsayılan sınırı)
tests/Tracon.AspNetCore.FunctionalTests/Infrastructure/TraconTestHost.cs (WaitForRunStatusAsync, WaitForTerminalRunAsync)
tests/** — 74 dosyada dönüşüm, etiket veya yarış düzeltmesi
scripts/manuel-test-tazelik.py · scripts/manuel_test_tazelik_test.py
docs/manuel-test/36-GELISTIRME-KAPILARI.md (MT-GDK-052…055) · 00-INDEKS.md · 09 · 11 · 13 (sınıf adları)
docs/hafiza/{test-paralellik-ve-zamanlama,test-altyapisi,test-yalitimi,test-yalitimi-vakalari,kod-haritasi}.md
```

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (plan metni değişmedi; sapmalar yukarıda) |
| Düzeltme turu sayısı | 3 — dönüşümün açtığı Core yarışları (1), paralel E2E'nin açtığı iki yarış (2), denetim bulguları (3) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 0 / 0 |
| Fazın ürettiği regresyon | 0 — dönüşümün düşürdüğü dört Core testi ve paralel koşumun düşürdüğü iki E2E testi **önceden var olan** yarışlardı (§ 7) |
| Faz kapandıktan sonra bulunan kusur | ölçülmedi (faz yeni kapandı) |

**Tam paket koşumu** (`dotnet test Tracon.slnx -c Release --no-build -- --report-trx`,
`DOTNET_ROOT=~/.dotnet`, aynı makine, 2026-09-23):

| Koşum | Kod | `-maxcpucount` | Süre | Test | Kırmızı | E2E projesi |
|---|---|---|---|---|---|---|
| Taban | `515fe8c2` | 1 | **884 sn** | 16.911 | 0 | 1:33 |
| 184.1 + 184.2 | `78dbafd0` | 1 | 863 sn | 16.944 | 0 | 1:29 (seri) |
| + 184.3 + E2E paralel | `f210c797` | 1 | 879 sn | 16.962 | 0 | 0:35 |
| aynı | `f210c797` | 2 | **607 sn** | 16.962 | 0 | 0:38 |
| aynı | `f210c797` | 2 | 658 sn | 16.962 | 0 | 0:42 |
| aynı | `f210c797` | 2 | 524 sn | 16.962 | 0 | 0:36 |

Toplam süre E2E kazancını göstermez: koşumdan koşuma `Package.Tests` tek
başına 2:30–3:40 oynar. `-maxcpucount:2`'de `AspNetCore.FunctionalTests`
3:19 → 6:34 uzar (paylaşılan CPU) ama duvar saati kısalır.

**E2E projesi tek başına** (80 + 1 test): seri 93–99 sn; paralel ilk üç
koşum 42 · 36 · 79 sn (üçüncüsünde 2 kırmızı — § 7'deki ses ve tooltip
yarışları); düzeltmeden sonra beş koşum **48 · 37 · 35 · 44 · 40 sn, 0
kırmızı**.

**Düşüş mesajı** (kasıtlı yanlış başlık `"Dashbord"`, 3 sn sınır, geçici
sınıf, silindi):

```
WaitForAsync:  System.TimeoutException : Timeout 3000ms exceeded.
               Call log:
                 - waiting for GetByRole(AriaRole.Heading, new() { Name = "Dashbord" }) to be visible
Expect:        Microsoft.Playwright.PlaywrightException : Locator expected to be visible
               Error: element(s) not found
               Call log:
                 - Expect "ToBeVisibleAsync" with timeout 3000ms
                 - waiting for GetByRole(AriaRole.Heading, new() { Name = "Dashbord" })
               Aria snapshot:
                 …
                 - heading "Dashboard" [level=1]
```

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, `--no-launch-profile`, `ASPNETCORE_ENVIRONMENT=Staging`,
geçici SQLite ve atılabilir içerik anahtarı (2026-09-23):

- `GET /tracon/api/diagnostics` → `persistenceProvider: SQLite`,
  `canConnect: true`, `migrationsUpToDate: true`, 7 agent, 9 tool.
- `POST /tracon/api/agents/support/run` → SSE: `run` · 5 `update` · `done`;
  `GET /tracon/api/runs/{id}` → `Completed`.

Faz ürün koduna dokunmadı; koşum ürünün bu fazın test değişiklikleri
altında da ayağa kalktığını gösterir, yeni bir davranış göstermez.

## Denetim Bulguları

Denetçi `faz-denetcisi` (taze bağlam, salt-okunur), `git diff 515fe8c2...HEAD`
(o an `72d2f4de`), 2026-09-23. Kapıları koşmadı (salt-okunur kural).

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | DoD "`PlaywrightLocatorTests` tabanı değişmedi" karşılanmıyor: kayıtlı taban 119 → 127 büyüdü; taban yalnız küçülür | gerçek | **düzeltildi** — gizlenen borç ödendi, on bir tam-metin `GetByPlaceholder` `Exact = true` aldı; kayıtlı taban 116 (`2ad816e2`) |
| 2 | 🟡 | `WaitUntil` hiç dönmeyen bir probe'u kesemiyor — sınır ve token probe'a ulaşmıyor | gerçek | **düzeltildi** — probe `WaitAsync(kalan, token)` altında; iki yeni test (`50074b00`) |
| 3 | 🟡 | Engellenen append testinde pozitif sinyal (log) yanlış etkinin oluşacağı noktadan ÖNCE yazılıyor | gerçek, **denetçinin gördüğünden büyük** | **düzeltildi** — tur sayısı + soket kapanışı beklenir. Mutasyon (runner'ın `return`'ü silindi) testin **zaten** yeşil kaldığını gösterdi: yalnız `commentary`'ye bakıyordu, sızan append `thinking`'deydi. Artık her kanala bakar ve mutasyonda düşer (`44844f3c`). Aynı "log etkiden önce" biçimi geç tool testinde kısa `negative` pencereyle kapatıldı |
| 4 | 🟢 | `WorkflowTests` düğüm sayısı tek atımlık okuma | gerçek | **düzeltildi** — `Expect(nodes.Nth(2))` |
| 5 | 🟢 | Pozitif çapası olmayan iki negatif E2E iddiası (`AgentTests` düzenleme formu, `RoleTests` `/mcp`) | gerçek | **düzeltildi** — önce yüklenmiş ekran beklenir |

Denetçinin sağlam bulduğu dönüşümler: `DrainTests` okuma sayacı,
`RunContinuationTests` nöbetçi `run`'ı, kulvar testi, `OnlineEvalJobHandlerTests`,
`TraconDrainServiceTests`, `SubAgentTimeoutTests`, `McpDiscoverySingletonTests`,
`DatabaseUnavailableTests`, göç kilidi testleri, dört "iptal kaydı satırdan önce"
düzeltmesi; 51 etiketin tamamı (gizli senkronizasyon beklemesi yok); E2E
dönüşümünün semantiği (`UseInnerText`, `Detached` → `ToHaveCountAsync(0)`).

## Sonraki Faza Devir Notu

Faz 184 beş fazlık turun (180–184) sonuncusudur; kökte planlanmış faz yok.
Sıradaki iş `aday-kesfi` ile seçilir (yalnız kullanıcı isteğiyle).

- **Test yazarken:** duruma bekleyen her test `WaitUntil` kullanır; kalan
  `Task.Delay` bir `// delay: <sınıf>` etiketi taşımadan
  `TestDelayClassificationTests`'i geçemez. Konsol testi web-first `Expect`
  kullanır; varsayılan sınır 30 sn'dir (`BrowserFixture`), ≤30 sn açık sınır
  yazma. Yeni konsol testi ekranının dosyasına (`tests/Tracon.Ui.E2ETests/Ui/`)
  girer; iki sınıfın kullanacağı yardımcı `UiTestHelpers`'a.
- **🚨 Negatif iddia yazarken** pozitif sinyal, yanlış etkinin oluşabileceği
  son noktadan sonra gelmeli ve iddia her kanala bakmalı — mutasyonla doğrula.
- **🚨 Yerel kapı `-maxcpucount:2`, CI 1.** Kapanışta izole tekrarda geçen bir
  kaynak çekişmesi kırmızısı görülürse değer 1'e döner. CI'da 2'yi denemek
  ayrı bir ölçümdür (runner donanımı).
- **E2E sınıfları paralel** (dörder). Yeni bir E2E testi statik değişken
  durum paylaşmamalı; her test kendi `UiHost`'unu ve tarayıcı bağlamını açar.
- **Açık kalan:** `-maxcpucount` CI ölçümü; `EvalCommandTests`'in
  `CancelAfter(300 ms)` Ctrl+C taklidi (erken iptal de aynı çıkış kodunu
  verdiği için iddia gevşek, kırılgan değil) — ikisi de aday olabilir,
  bu fazda açılmadı.
