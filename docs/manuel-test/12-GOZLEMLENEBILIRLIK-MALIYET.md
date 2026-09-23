# 12 — Gözlemlenebilirlik ve Maliyet (`OBS`)

> **Alan kodu:** `OBS` · **Faz:** 6 (iz/span), 20 (maliyet + gösterge paneli),
> 35 (maliyet/kota metrikleri — OTel enstrümanları),
> 68 (çalıştırma kimliği + token kırılımı ve cache fiyatı),
> 88 (görsel üretim ölçümü), 89 (tool çıktısı boyut sınırı),
> 113 (§ `IRunErrorClassifier` kompozisyonu ve `RunErrorFingerprint`),
> 119 (§ ham hata metni sızıntısının kapatılması — `SafeErrorText`),
> 132 (§ uygulanan fiyat snapshot'ı — birim fiyatlar, `runs.model_provider`,
> yeniden hesaplamanın `Unknown`'a daralması)
> **Kaynak:** `src/Tracon.UI/frontend/src/screens/dashboard.tsx` (tüm dosya) ·
> `components/charts.tsx` (`TimeSeriesChart`/`ModelBreakdownChart`/
> `StatusDistributionChart`) · `components/waterfall.tsx` (iz/span görselleştirme,
> `run-detail.tsx` üzerinden kullanılır — bkz. Sınır) · `lib/format.ts`
> (`money`/`count`/`percent`) · `lib/api.ts` (`api.stats/timeseries/toolUsage/
> modelsHealth/onlineEvaluationSummary`).
> Sunucu: `src/Tracon.AspNetCore/Endpoints/ObservabilityEndpoints.cs`
> (`/api/runs/{id}/trace`, `/tools`, `/api/tools/usage`) ·
> `Endpoints/CatalogEndpoints.cs` (`/api/stats`, `/api/stats/timeseries`,
> `/api/stats/errors`, `/api/stats/recalculate-costs`) ·
> `Endpoints/ModelHealthEndpoints.cs` · `src/Tracon.Core/Diagnostics/`
> (`RunTraceCollector.cs`, `TraconMetrics.cs`) ·
> `src/Tracon.Core/Quotas/QuotaUsageObserver.cs` (yalnız Faz 35'in
> ölçerleri — bkz. Sınır) · `src/Tracon.Core/Models/RunPricingResolver.cs` ·
> `src/Tracon.Core/Storage/InMemoryRunStore.cs` (`GetTimeSeriesAsync`,
> `GetToolUsageAsync`) · `src/Tracon.Abstractions/Diagnostics/SafeErrorText.cs`
> (Faz 119 — hata metni redaksiyon kuralı; 26 çağrı yeri `docs/119-HATA-METNI-SIZINTISI.md`'de).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../arsiv/manuel-test-kosum-2026-09/12-GOZLEMLENEBILIRLIK-MALIYET.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/12-GOZLEMLENEBILIRLIK-MALIYET.md

---

## Bu dosya neyi kanıtlar

Dashboard ekranının kendi widget'ları (üst şerit, zaman serisi, model/agent/
hata kırılımları, uyarılar), bir çalıştırmanın iz (span) ağacının Waterfall
görselleştirmesi ve **örnekleme** kuralı (Faz 6 — başarılı çalıştırmaların
yalnız bir kısmı, hatalıların tamamı), maliyet çözümleme sırası (katalog →
yapılandırma → bilinmeyen, K-032) ve bunun **hiçbir zaman sıfıra düşmediği**
(bilinmeyen fiyat `null`'dur, `0` değil), ve Faz 35'in **arayüzü olmayan**
iki OpenTelemetry enstrümanının (`tracon.run.cost` sayacı,
`tracon.quota.usage`/`.limit` gözlemlenen ölçerleri) `dotnet-counters`
ile gözlemlenmesi.

```mermaid
flowchart TD
    A["dashboard.tsx acilir"] --> B["4 bagimsiz sorgu: stats, timeseries, topStrip, health"]
    B --> C["Ust serit: bugun vs dun (Day kovasi x2)"]
    B --> D["Zaman serisi + durum dagilimi (araligi secilen kova)"]
    B --> E["Model kirilimi / En aktif agent'lar / Hata kirilimi (stats)"]
    B --> F["Uyarilar: fiyatsiz + saglıksız saglayici + bekleyen girdi"]
    G["run-detail.tsx: bitmis KOK calistirma"] --> H["GET .../trace"]
    H -->|"orneklendiyse 200"| I["Waterfall: span agaci"]
    H -->|"orneklenmediyse 404"| J["'SuccessSampleRatio' mesaji"]
    K["RunRecordingAgent.CompleteAsync"] --> L["RunPricingResolver: katalog -> yapilandirma -> Unknown"]
    L --> M["runs.cost_* sutunlari + tracon.run.cost sayaci"]
    N["QuotaUsageObserver (Faz 35, varsayilan KAPALI)"] -.->|"EnableQuotaUsageGauge=true"| O["tracon.quota.usage/.limit"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Trace panelinin run-detail'de HANGİ KOŞULDA render edildiği (bitmemiş run, alt çalıştırma) | [`11-ARAYUZ-RUN-SESSION-SSE.md`](11-ARAYUZ-RUN-SESSION-SSE.md) `MT-UIRUN-015`/`016` — zaten üretildi. Burada yalnız trace'in **kendi içeriği** (span alanları, örnekleme, hassas veri ayıklama) ölçülür. |
| Kota **kural motoru** (`QuotaEnforcer`, `QuotaGate`, `429` zorlaması, `PUT /api/quotas`) | `23-SAKLAMA-ARSIV-KOTA.md` (henüz üretilmedi, Faz 21) — burada yalnız Faz 35'in kota kuralını **gözlemleyen** iki OTel ölçeri test edilir, kuralın kendisi değil. |
| Sağlayıcı hata sınıflandırma kuralının (`DefaultRunErrorClassifier`) kendisi, devre kesici, sağlayıcı sağlığının derin mekaniği | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) · [`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md) — zaten üretildi. Burada yalnız dashboard'un bu verileri **nasıl gösterdiği** ölçülür. |
| Geri bildirim/puanlamanın kendi işlevi (thumbs up/down, judge tetikleme) | 🚨 Hiçbir dosyaya atanmamış — bkz. `00-INDEKS.md` §8. Burada yalnız Dashboard'daki **özet** panelin boş/dolu durumu ölçülür, puanlama işlemi ölçülmez. |
| Çevrimiçi değerlendirmenin (Faz 49) yargıç tanımı, eşik ayarı, örnekleme kuralı | `17-EVAL-VE-DENEYLER.md` (henüz üretilmedi) — burada yalnız Dashboard'daki özet panel ölçülür. |
| Ayarlar ekranındaki olası fiyat/kota panelleri | Böyle bir panel **yoktur** — grep ile doğrulandı (`dashboard.tsx`'in `configurePricing` bağlantısı yalnız genel `settings` rotasına gider, özel bir alt sayfa yoktur). Fiyat/kota yalnız `dotnet user-secrets`/`curl` ile yönetilir. |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama çalışır, `manuel-test-token-2026` ile giriş yapılmıştır.
3. `Tracon:Providers:OpenAI:ApiKey` tanımlıdır.
4. **Örnek uygulama HİÇBİR model fiyatı tanımlamaz** (`grep -rn "InputCostPerMillionTokens\|Pricing" samples/Tracon.Api/Program.cs` boş döner) — bu yüzden reset sonrası HER çalıştırma `PricingSource.Unknown`'dur. Bu bir kusur değil, dosyanın kendi başlangıç durumudur; § 8'deki case'ler fiyatı bilerek sonradan tanımlar.
5. `dotnet-counters` .NET global aracı kuruludur (§ 12 için):
   ```bash
   dotnet tool install --global dotnet-counters
   dotnet-counters ps   # Tracon.Api sürecinin PID'sini bulmak için
   ```
6. Bu dosyanın birçok case'i `dotnet user-secrets set/remove` ile uygulamayı
   YENİDEN BAŞLATMAYI gerektirir — her case bunu adım olarak yazar, atlanmaz.

---

# 1 — Üst şerit (`TopStrip`)

### MT-OBS-001 — Reset sonrası tüm Dashboard boş-durumları aynı anda görünür

Sınır durumu — hiç çalıştırma yokken.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- Reset yordamı uygulanmış, hiçbir çalıştırma yapılmamış.

**Adımlar**
1. "Dashboard" ekranını aç.

**Beklenen sonuç**
- Üst şeritte dört karo görünür: "Bugünkü Çalıştırma" `0`, "Hata Oranı" `—`
  (`errorRate`, `point.runs === 0` iken `null` döner, `percent(null) = '—'`),
  "Bugünkü Token" `0`, "Bugünkü Maliyet" `—`. Hiçbirinde `delta` rozeti YOK
  (`delta()` her iki taraf da tanımsızsa `null` döner).
- Zaman serisi grafiği ve durum dağılım çubuğu `charts.noRuns` boş-durumunu
  gösterir (`points.length === 0`).
- "Model Kırılımı" aynı boş-durumu gösterir.
- "En Aktif Agent'lar" `dashboard.noRunsInWindow`, "Hata Kırılımı"
  `dashboard.noErrorsInWindow` metnini gösterir.
- "Uyarılar" paneli `dashboard.allClear` metnini gösterir (üçü de sıfır).
- "Geri Bildirim" `feedback.noneYet`, "Çevrimiçi Değerlendirme"
  `onlineEval.noneYet` metnini gösterir.

---

### MT-OBS-002 — Fiyat tanımsızken "Bugünkü Maliyet" karosu `—` gösterir, `runsWithUnknownPricing` sıfır DEĞİLDİR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | K-032 |

**Ön koşul**
- `playground/support` ile `FIX-PROMPT-02` (`Merhaba`) gönderilmiş, tur
  tamamlanmış. Hiçbir `Tracon:Pricing:*` anahtarı tanımlı DEĞİL
  (varsayılan durum).

**Adımlar**
1. "Dashboard" ekranını aç, "Bugünkü Maliyet" karosunu oku.
2. `curl -s "http://localhost:5080/tracon/api/stats?maxAgents=10" -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool | grep -i unknownpricing`

**Beklenen sonuç**
- Adım 1: karo `—` gösterir (`today?.cost === null || undefined`) — `0` DEĞİL.
- Adım 2: `runsWithUnknownPricing` alanı `0`'dan BÜYÜKTÜR — sunucu bilinmeyen
  fiyatı ayrıca sayar, sıfır yazmaz (`CatalogEndpoints.cs` açıklaması).

---

### MT-OBS-003 — Fiyat tanımlandıktan sonra yeni bir çalıştırma "Bugünkü Maliyet" karosunda gerçek bir tutar üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | K-032 |

**Ön koşul**
```bash
cd samples/Tracon.Api
dotnet user-secrets set "Tracon:Pricing:openai:gpt-5.4-mini:Input" "0.15"
dotnet user-secrets set "Tracon:Pricing:openai:gpt-5.4-mini:Output" "0.60"
```
uygulama yeniden başlatılmış.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tur tamamlansın.
2. "Dashboard" ekranını aç, "Bugünkü Maliyet" karosunu oku.

**Beklenen sonuç**
- Karo artık `—` değil, sıfırdan büyük bir sayısal tutar gösterir
  (`money(today.cost, null)` — bkz. `MT-OBS-004` için para birimi eksikliği).

---

### MT-OBS-004 — 🚨 "Bugünkü Maliyet" karosu para birimini HİÇBİR ZAMAN göstermez; Model Kırılımı aynı veri için gösterir

`TopStrip`'in maliyet karosu `money(today.cost, null)` çağırır — para birimi
parametresi SABİT `null`'dır. Aynı sayfadaki `ModelBreakdownChart` ise
`stats.data.currency`'i gerçekten kullanır. Kod okumasıyla ölçüldü, aşağıda
gözlemsel olarak doğrulanır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Pricing:Currency" "USD"
```
uygulama yeniden başlatılmış; `MT-OBS-003`'ün fiyatlandırması hâlâ tanımlı.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tur tamamlansın.
2. "Dashboard"ta "Bugünkü Maliyet" karosunun metnini oku.
3. Aynı sayfada "Model Kırılımı" panelindeki maliyet sütununu oku.

**Beklenen sonuç**
- Adım 2: yalnız sayı görünür (örn. `0.0012`) — `USD` soneki YOKTUR.
- Adım 3: AYNI ekranda, aynı veri kümesinden gelen tutarın yanında `USD`
  soneki VARDIR (`money(model.totalCost, currency)`).
- İki panel de doğrudur (aynı sayısal değer), yalnız biri para birimini
  gösterir, diğeri göstermez — bu bir tutarsızlıktır, kusur olarak değil,
  koşumda doğrulanacak bir gözlem olarak işaretlenir.

---

### MT-OBS-005 — `delta()` hesaplaması: dünü sıfırken bugün de sıfırsa `%0`, dün sıfırken bugün pozitifse rozet HİÇ görünmez

Sınır durumu — sıfıra bölme kaçınması.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama bugün ilk kez başlatılmış olmalı (dün hiç çalıştırma yok,
  `points[0]` gerçek bir "dün" bucket'ı DEĞİL — `topStrip` sorgusu yalnız 2
  günlük kova ister ve dünkü bucket veritabanında hiç yoksa sıfır sayılır).

**Adımlar**
1. Reset uygula, uygulamayı başlat, HİÇBİR çalıştırma yapmadan Dashboard'ı aç
   ("Bugünkü Çalıştırma" `0`).
2. Bir çalıştırma yap (`FIX-PROMPT-02`), Dashboard'ı yenile.

**Beklenen sonuç**
- Adım 1: "Bugünkü Çalıştırma" `0`, delta rozeti YOK (`delta(0,0) = 0` olsa
  bile bu durumda hem `current` hem `previous` `0`'dır, kod `previous === 0
  ? (current === 0 ? 0 : null) : ...` dalına göre `0` döner — rozet `+0.0%`
  olarak GÖRÜNÜR, gizlenmez).
- Adım 2: "Bugünkü Çalıştırma" `1`, delta `previous === 0 && current !== 0`
  olduğu için `null` döner — rozet HİÇ görünmez (yüzde artış matematiksel
  olarak tanımsızdır, `+∞%` yerine hiçbir şey gösterilir).

### MT-OBS-006 — Aralık düğmeleri farklı kova boyutuyla istek atar; 30 gün özellikle günlük kovaya düşer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- En az birkaç çalıştırma var (önceki case'lerden kalanlar yeterli).

**Adımlar**
1. Dashboard'ı aç, DevTools ağ sekmesini temizle.
2. Sırayla `1h`, `24h`, `7d`, `30d` düğmelerine tıkla, her birinde giden
   `GET api/stats/timeseries?...` isteğinin `bucket` parametresine bak.

**Beklenen sonuç**
- `1h`/`24h`/`7d` → `bucket=Hour`.
- `30d` → `bucket=Day` (30 gün × saatlik kova 720 kova eder, sunucunun 500
  kova sınırını aşardı — `RANGE_CONFIG`'in kod yorumu bunu açıkça gerekçelendirir).
- Her tıklamada `from`/`to` seçilen pencereye göre YENİDEN hesaplanır (`to`
  her zaman "şimdi").

---

### MT-OBS-007 — Zaman serisi grafiğinde çalışma/başarısızlık çizgileri ve durum dağılım çubuğu doğru veriyi çizer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- En az bir başarılı (`support`) ve bir başarısız (`MT-UIRUN-011` benzeri,
  geçersiz model) çalıştırma bugünün penceresinde var.

**Adımlar**
1. Dashboard'ı `24h` aralığında aç.
2. `data-testid="timeseries-chart"` SVG'sini incele: mor (çalışma sayısı) ve
   kesikli gül rengi (başarısız sayısı) çizgileri.
3. Hemen altındaki `data-testid="status-distribution-chart"`'ı incele.

**Beklenen sonuç**
- Adım 2: iki çizgi de aynı ölçekte (`Math.max(1, ...runs)`); başarısız
  çizgisi kesikli (`strokeDasharray`) — renk körlüğünde bile ayırt edilir.
- Adım 3: her kovada YEŞİLİMSİ (tamamlanan = `runs - failedRuns`) ve KIRMIZI
  (başarısız) yığılmış iki segment görünür; toplam yükseklik o kovanın run
  sayısıyla orantılıdır.

### MT-OBS-008 — Model kırılımı run sayısına göre azalan sırada çubuklar çizer; fiyat tanımsızken tutar `—`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- `support` (openai/gpt-5.4-mini) ile birden çok, `claude-support` (Anthropic
  anahtarı varsa) ile bir çalıştırma var. Fiyat tanımlı DEĞİL (bu case için
  `MT-OBS-003`'ün fiyat ayarları GERİ ALINIR: `dotnet user-secrets remove
  "Tracon:Pricing:openai:gpt-5.4-mini:Input"` ve `:Output`, yeniden başlat).

**Adımlar**
1. Dashboard'ı aç, "Model Kırılımı" panelini incele.

**Beklenen sonuç**
- Çubuklar `totalRuns`'a göre azalan sırada (en çok çalışan model en üstte,
  en geniş çubuk).
- Her satırda model adı, run sayısı, token sayısı ve maliyet sütunu var;
  fiyat tanımsız olduğu için maliyet sütunu `—` gösterir (`model.totalCost
  === undefined` VEYA `null` — `money()` ikisini de `—`'ye çevirir).

---

### MT-OBS-009 — En aktif agent'lar listesinde başarısız run varsa kırmızı ek metin görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-destek`'te en az bir başarılı, `MT-OBS-007`'nin agent'ında en az
  bir başarısız çalıştırma var.

**Adımlar**
1. Dashboard'ı aç, "En Aktif Agent'lar" panelini incele.

**Beklenen sonuç**
- Yalnız başarısız run'ı OLAN agent satırında kırmızı
  `dashboard.agentFailed` metni (`failed: N`) görünür; hiç başarısız run'ı
  olmayan agent satırında bu ek metin YOKTUR.

### MT-OBS-010 — Hata sınıfı kırılımı, sınıf başına en sık kümenin örnek mesajını ve son görülme zamanını gösterir

Sınıflandırma kuralının kendisi [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md)'de
zaten üretildi; burada yalnız bu ekranın GÖSTERİMİ ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20, 44 |
| **İlgili karar** | — |

**Ön koşul**
- Geçersiz bir model adıyla en az iki başarısız çalıştırma var (aynı hata
  sınıfına düşecek şekilde — örn. ikisi de `var-olmayan-model-xyz`).

**Adımlar**
1. Dashboard'ı aç, "Hata Kırılımı" panelini incele.

**Beklenen sonuç**
- İlgili sınıfın satırında toplam run sayısı ve altında en sık kümenin
  (`topClusters[0]`) örnek mesajı (`title` tooltip'inde tam metin) ve göreli
  "son görülme" zamanı görünür.
- `dashboard.errorClass.<sınıf>` çevirisi mevcutsa okunabilir bir etiket
  gösterir; yoksa ham anahtar görünür (bu da bir eksik çeviri sinyalidir).

### MT-OBS-011 — Hiçbir koşul tetiklenmediğinde "Her şey yolunda" metni görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-003`'ün fiyat ayarları tanımlı (fiyatsız run YOK), tüm sağlayıcılar
  sağlıklı, hiçbir `AwaitingInput` run YOK. Yalnız `support` ile fiyatlı bir
  çalıştırma yeterlidir.

**Adımlar**
1. Dashboard'ı aç, "Uyarılar" panelini incele.

**Beklenen sonuç**
- `dashboard.allClear` metni tek başına görünür; hiçbir rozet YOK.

---

### MT-OBS-012 — Fiyatı tanımsız run varken sarı uyarı rozeti görünür; "Fiyatı yapılandır" bağlantısı yalnız Admin'e görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-002`'nin fiyatsız çalıştırması var (bu dosyanın tek bearer token'ı
  her zaman `canAdminister = true` taşır — rol ayrımının kendisi
  [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md)'nin konusudur).

**Adımlar**
1. Dashboard'ı aç, "Uyarılar" panelini incele.

**Beklenen sonuç**
- Sarı `dashboard.unpricedTitle` tooltip'li bir rozet, fiyatsız run sayısını
  gösterir.
- Rozetin yanında `dashboard.configurePricing →` bağlantısı görünür (bu
  bağlantı özel bir fiyat ekranına DEĞİL, genel `settings` rotasına gider —
  ayrı bir fiyat paneli yoktur).

---

### MT-OBS-013 — Bekleyen girdi run'ı varken mavi uyarı rozeti "Çalıştırmalar"a bağlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20, 16 |
| **İlgili karar** | — |

**Ön koşul**
- [`11-ARAYUZ-RUN-SESSION-SSE.md`](11-ARAYUZ-RUN-SESSION-SSE.md) `MT-UIRUN-012`'nin
  `summarize-and-approve` çalıştırması hâlâ `AwaitingInput` durumunda.

**Adımlar**
1. Dashboard'ı aç, "Uyarılar" panelini incele, mavi rozete tıkla.

**Beklenen sonuç**
- Mavi `dashboard.awaitingRuns` rozeti bekleyen run sayısını gösterir.
- Tıklanınca `runs` ekranına gider (filtre uygulanmadan — yalnız genel
  listeye yönlendirir, `AwaitingInput` filtresi otomatik seçilmez).

### MT-OBS-014 — Hiç puanlanmış run yokken "henüz yok" metni; çevrimiçi değerlendirme paneli 30 saniyede bir kendiliğinden yenilenir

Derinlemesine puanlama/yargıç testi bu dosyanın kapsamı DIŞINDADIR (bkz.
Sınır tablosu) — burada yalnız özet panelin varlığı ve yenileme davranışı
ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- Hiçbir run puanlanmamış, hiçbir yargıç çalıştırılmamış.

**Adımlar**
1. Dashboard'ı aç, "Geri Bildirim" ve "Çevrimiçi Değerlendirme" panellerini
   incele.
2. Ağ sekmesinde 35 saniye bekle, `GET api/evaluation/online` isteğinin kaç
   kez gittiğini say.

**Beklenen sonuç**
- Adım 1: ikisi de "henüz yok" metnini gösterir (`feedback.noneYet`,
  `onlineEval.noneYet`).
- Adım 2: EN AZ iki istek gider (`refetchInterval: 30_000`) — "Geri
  Bildirim" paneli AYNI davranışı göstermez (o, `stats` sorgusuna bağlıdır,
  kendi zamanlayıcısı YOKTUR).

### MT-OBS-015 — `SuccessSampleRatio = 0` iken: başarılı run'da trace KESİN YOK, başarısız run'da `AlwaysPersistFailures` sayesinde YİNE DE VAR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Observability:SuccessSampleRatio" "0"
```
uygulama yeniden başlatılmış.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder (başarılı), tamamlanınca
   çalıştırma sayfasında "İz" panelini incele.
2. Geçersiz bir modelle (`manuel-model-hata` gibi) başarısız bir çalıştırma
   üret, "İz" panelini incele.

**Beklenen sonuç**
- Adım 1: `GET .../trace` `404` döner; panel `runDetail.noSpans` boş-
  durumunu, `Tracon:Observability:SuccessSampleRatio` adını anarak
  gösterir. Oran `0` olduğu için bu SONUÇ GARANTİLİDİR (olasılıksal değil).
- Adım 2: aynı ayar altında bile trace VARDIR (`200`) — `AlwaysPersistFailures`
  (varsayılan `true`) örnekleme oranını GEÇERSİZ kılar.

---

### MT-OBS-016 — `SuccessSampleRatio = 1` iken başarılı bir run'da trace KESİN VAR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Observability:SuccessSampleRatio" "1"
```
uygulama yeniden başlatılmış.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tamamlanınca "İz" panelini
   incele.

**Beklenen sonuç**
- `200` döner; `Waterfall` bileşeni render edilir (span sayısı, `traceId`,
  toplam süre başlıkta görünür).

---

### MT-OBS-017 — Waterfall ebeveyn-çocuk yuvalamayı girintiyle gösterir; kök span en üstte

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 6, 12 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-016`'nın `SuccessSampleRatio=1` ayarı hâlâ etkin.

**Adımlar**
1. `playground/router` aç, `FIX-PROMPT-01` gönder, tamamlansın.
2. `router`'nin KÖK çalıştırmasının "İz" panelini aç (alt çalıştırmanın
   DEĞİL — bkz. `MT-OBS-021`).
3. Bir span satırına tıkla, açılan ayrıntı bölümünü incele.

**Beklenen sonuç**
- Adım 2: en az iki span görünür; `support` agent'ına ait çağrı span'i
  `router`'ninkine göre girintili (`depth * 10px`) satırda, aynı zaman
  eksenine göre konumlanmış bir çubukla görünür.
- Adım 3: açılan bölümde `kind`/`status` rozetleri, `spanId` (mono) ve
  öznitelik tablosu (veya `waterfall.noAttributes` metni) görünür.

---

### MT-OBS-018 — Sıfıra yakın süreli bir span bile en az %0,6 genişlikte GÖRÜNÜR kalır

Sınır durumu — kod, alt-milisaniyelik span'lerin görsel olarak kaybolmasını
önlemek için taban genişlik uygular.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-017`'nin iz verisi açık.

**Adımlar**
1. En kısa süreli span satırının sağındaki süre etiketini oku (`formatMs`).
2. O satırın çubuğunu (DevTools ile `style.width`) ölç.

**Beklenen sonuç**
- Süre `<1ms` gibi çok kısa bir etiket taşısa BİLE çubuk genişliği `%0`
  DEĞİLDİR — en az `%0.6` (`Math.max(clampPercent(...), 0.6)`), gözle
  görülür ince bir çizgi kalır.

---

### MT-OBS-019 — 🚨 Hassas öznitelikler varsayılanda ayıklanır; `RecordSensitiveData=true` ile aynı tür çağrıda görünür

`RunTraceCollector.IsSensitive`, anahtar adında (alt dizgi olarak, büyük/
küçük harf duyarsız) `"message"`/`"prompt"`/`"completion"` geçen HER
özniteliği varsayılanda siler — semantik bir izin listesi değil, düz bir
alt dizgi eşleşmesidir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-016`'nın `SuccessSampleRatio=1` ayarı hâlâ etkin.
- `dotnet user-secrets set "Tracon:Observability:RecordSensitiveData" "false"`
  (varsayılan zaten budur, açıkça yazmak bu case'i belgeler).

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tamamlansın; `runId`'yi not al.
2. `curl -s "http://localhost:5080/tracon/api/runs/<runId>/trace" -H "Authorization: Bearer manuel-test-token-2026" | python3 -c "import json,sys; d=json.load(sys.stdin); print([k for s in d['spans'] for k in s['attributes'] if 'message' in k.lower() or 'prompt' in k.lower() or 'completion' in k.lower()])"`
3. `dotnet user-secrets set "Tracon:Observability:RecordSensitiveData" "true"`,
   uygulamayı yeniden başlat, AYNI adımları tekrarla (yeni bir `runId` ile).

**Beklenen sonuç**
- Adım 2: liste BOŞTUR — `gen_ai.*.message` gibi hassas anahtarlar hiçbirinde
  yok.
- Adım 3: liste artık DOLUDUR (en azından bir `gen_ai.*.message` benzeri
  anahtar görünür) — bayrak açıkken aynı çağrı gerçek içeriği taşır.
- Adım 3'ten sonra bayrak `false`'a GERİ ALINIR (varsayılan davranış — sonraki
  case'ler bunu bekler).

---

### MT-OBS-020 — Alt çalıştırmanın trace ucu, "span yok" ile "hiç çalıştırma yok"u AYNI mesajla döner

Negatif senaryo — sunucu bu ikisini ayırmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-017`'nin `support` alt çalıştırmasının `runId`'si elde.

**Adımlar**
1. `curl -s -o /dev/null -w "%{http_code}\n" "http://localhost:5080/tracon/api/runs/<altRunId>/trace" -H "Authorization: Bearer manuel-test-token-2026"`
2. Aynı isteği rastgele, var olmayan bir GUID ile tekrarla.

**Beklenen sonuç**
- İkisi de `404` döner ve gövde metni BİREBİR AYNIDIR
  (`"Trace bulunamadi"` + `SuccessSampleRatio` açıklaması) — sunucu "bu
  çalıştırma hiç yok" ile "bu çalıştırmanın span'i yok"u ayırt etmez (alt
  çalıştırmanın trace'i her zaman köke aittir, bu yüzden 404 beklenen
  sonuçtur — bkz. [`11-ARAYUZ-RUN-SESSION-SSE.md`](11-ARAYUZ-RUN-SESSION-SSE.md)
  `MT-UIRUN-015`).

### MT-OBS-021 — Fiyat tanımsızken maliyet alanları `null`'dur, `0` DEĞİL

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | K-032 |

**Ön koşul**
- Hiçbir `Tracon:Pricing:*` anahtarı tanımlı DEĞİL. `manuel-bos`
  (`FIX-AGENT-02`) ile bir çalıştırma üret.

**Adımlar**
1. `curl -s "http://localhost:5080/tracon/api/runs/<runId>" -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool | grep -A4 '"cost"'`

**Beklenen sonuç**
- `cost.source = "Unknown"`, `cost.inputCost = null`, `cost.outputCost = null`
  — HİÇBİRİ `0` DEĞİLDİR (`0` "ücretsiz model" anlamına gelirdi, `null`
  "fiyat bilinmiyor" anlamına gelir).

**Doğrulama sorgusu**
```sql
SELECT input_cost, output_cost, pricing_source FROM tracon.runs WHERE id = '<runId>';
```

---

### MT-OBS-022 — Yalnız `Input` fiyatı tanımlanınca `outputCost` `null` kalır, `source = Configuration` olur

Sınır durumu — kısmi fiyatlandırma "Unknown"a düşmez.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets remove "Tracon:Pricing:openai:gpt-5.4-mini:Output"
dotnet user-secrets set "Tracon:Pricing:openai:gpt-5.4-mini:Input" "0.15"
```
(yalnız `Input` kalacak şekilde), uygulama yeniden başlatılmış.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tamamlansın.
2. `curl -s ".../api/runs/<runId>" -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool | grep -A4 '"cost"'`

**Beklenen sonuç**
- `cost.source = "Configuration"`, `cost.inputCost` sıfırdan büyük bir sayı,
  `cost.outputCost = null` — kısmi fiyatlandırma geçerli bir durumdur, tüm
  alanları `Unknown`'a düşürmez.

---

### MT-OBS-023 — Rezerve anahtar: `Pricing:Voice:...` bir "Voice" sağlayıcısı olarak ayrıştırılmaz

Sınır/negatif senaryo — `Voice` ve `Currency` fiyat bölümünde rezerve
adlardır.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Pricing:Voice:openai:gpt-5.4-mini:Input" "999"
```
(`openai`'nin GERÇEK `Pricing:openai:...` anahtarı KALDIRILMIŞ olmalı —
`MT-OBS-022`'den `dotnet user-secrets remove "Tracon:Pricing:openai:gpt-5.4-mini:Input"`),
uygulama yeniden başlatılmış.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tamamlansın.
2. Çalıştırmanın `cost.source` alanına bak.

**Beklenen sonuç (2026-09-16 turunda düzeltildi — bkz. koşum kaydı)**
- 🚨 Ön koşulun tam metniyle (`...:Input` `999`) uygulama **HİÇ AÇILMAZ**:
  `TraconOptionsValidator` her `Pricing:Voice:{provider}:{model}` girdisini
  bir `VoicePriceOverride` olarak bağlar ve `PerMillionCharacters`/`PerMinute`
  alanlarından biri dolu değilse `OptionsValidationException` ile başlangıçta
  durur (`TraconOptionsValidator.cs:302-313`) — `Input` bu ikisinden biri
  DEĞİLDİR. Orijinal beklenen sonuç ("run tamamlanır, `cost.source =
  Unknown`") bu validasyonla çelişiyordu; AGENTS.md kuralı gereği ("doküman
  ile kod çelişirse doküman yanlıştır") burada düzeltildi.
- Rezervasyonun KENDİSİ (Voice bölümünün sohbet fiyatına hiç karışmaması)
  YİNE DE doğrudur — yalnız GEÇERLİ bir Voice şekliyle (`PerMinute`/
  `PerMillionCharacters`) gösterilebilir: `Tracon:Pricing:Voice:openai:
  gpt-5.4-mini:PerMinute=999` ile uygulama normal açılır ve `support` ile
  yapılan bir çalıştırmanın `cost.source` alanı **`"Unknown"`** kalır —
  reserve bölüm sohbet fiyatlandırmasına hiç sızmaz.
- Sonuç: rezervasyon kuralı DOĞRU çalışıyor, yalnız spec'in seçtiği örnek
  anahtar (`Input`) bu kuralı sınamadan önce başka bir (daha katı) doğrulamaya
  takılıyor. Bu bir ürün kusuru değildir — "sessizce görmezden gel" yerine
  "yüksek sesle başlangıçta reddet" daha güvenli bir tasarımdır.

---

### MT-OBS-024 — K-154: aynı model adı iki sağlayıcıda farklı fiyatla tanımlıyken yeniden hesaplama alfabetik İLK sağlayıcıyı seçer

`runs` tablosu sağlayıcı sütunu taşımaz; bir model adı birden fazla
sağlayıcıda tanımlıysa yeniden hesaplama HANGİ sağlayıcıdan geldiğini
bilemez ve alfabetik ilkini kazandırır — bilinen ve kabul edilmiş bir
sınırlama (K-154).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | K-154 |

**Ön koşul**
- OpenRouter anahtarı tanımlı.
```bash
dotnet user-secrets remove "Tracon:Pricing:Voice:openai:gpt-5.4-mini:Input"
dotnet user-secrets set "Tracon:Pricing:openai:gpt-5.4-mini:Input" "1"
dotnet user-secrets set "Tracon:Pricing:openai:gpt-5.4-mini:Output" "1"
dotnet user-secrets set "Tracon:Pricing:openrouter:gpt-5.4-mini:Input" "5"
dotnet user-secrets set "Tracon:Pricing:openrouter:gpt-5.4-mini:Output" "5"
```
uygulama yeniden başlatılmış — AYNI model adı (`gpt-5.4-mini`) iki
sağlayıcıda ÇOK FARKLI fiyatlarla tanımlı.

**Adımlar**
1. `openrouter-support` agent'ı (OpenRouter üzerinden `gpt-5.4-mini` kullanır)
   ile `playground`'da bir çalıştırma yap, tamamlansın, `runId`'yi not al.
2. `curl -s -X POST ".../api/stats/recalculate-costs" -H "Authorization: Bearer manuel-test-token-2026"`.
3. Çalıştırmanın güncellenmiş `cost.inputCost` değerine bak.

**Beklenen sonuç**
- Adım 3: `inputCost` `openrouter`'ın fiyatı (`5`) DEĞİL, alfabetik olarak
  ÖNCE gelen `openai`'nin fiyatıyla (`1`) hesaplanmıştır — gerçek sağlayıcı
  OpenRouter olmasına rağmen. Bu, kusur DEĞİL, K-154'ün belgelediği bir
  sınırlamadır; case bunu koşumda somut sayılarla doğrular.

---

### MT-OBS-025 — `POST /api/stats/recalculate-costs` Admin ister, denetim izine yazar, sayaçları tutarlı döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-024`'ün karışık fiyatlandırma durumu hâlâ etkin; en az bir
  fiyatsız (`manuel-bos`) çalıştırma da var.

**Adımlar**
1. `curl -s -X POST ".../api/stats/recalculate-costs" -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool`.
2. `SELECT tenant_id, action, entity FROM tracon.audit_log WHERE action = 'stats.recalculate-costs' ORDER BY occurred_at DESC LIMIT 1;`

**Beklenen sonuç**
- Adım 1: `runsConsidered >= runsUpdated`, `runsStillUnknown` en az
  `manuel-bos`'un çalıştırma sayısı kadardır (fiyatsız kalanlar).
- Adım 2: denetim izine `stats.recalculate-costs` satırı düşmüştür.

### MT-OBS-026 — `from >= to` (eşitlik dahil) `400 "Aralik gecersiz"` döner

Negatif senaryo.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -i "http://localhost:5080/tracon/api/stats/timeseries?from=2026-08-10T00:00:00Z&to=2026-08-10T00:00:00Z" -H "Authorization: Bearer manuel-test-token-2026"`
   (`from` ve `to` BİREBİR AYNI).

**Beklenen sonuç**
- `400`, `"Aralik gecersiz"` — kod `>=` kontrolü yapar, yalnızca `from > to`
  DEĞİL, `from == to` da reddedilir.

---

### MT-OBS-027 — 30 günlük aralığı saatlik kovayla istemek `400 "Kova sayisi asildi"` döner, günlük kova önerir

Negatif senaryo — 500 kova sınırı.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -i "http://localhost:5080/tracon/api/stats/timeseries?from=$(date -u -v-31d +%Y-%m-%dT%H:%M:%SZ)&to=$(date -u +%Y-%m-%dT%H:%M:%SZ)&bucket=Hour" -H "Authorization: Bearer manuel-test-token-2026"`
   (macOS `date -v` sözdizimi; 31 gün × 24 saat = 744 kova, 500 sınırını aşar).

**Beklenen sonuç**
- `400`, `"Kova sayisi asildi"`; gövde önerilen kovayı adlandırır (`"Onerilen
  kova: day."` benzeri).

---

### MT-OBS-028 — Boş kovalar sıfır sayımlarla döner; hiçbir kova ATLANMAZ

Sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Adımlar**
1. `curl -s "http://localhost:5080/tracon/api/stats/timeseries?from=2020-01-01T00:00:00Z&to=2020-01-02T00:00:00Z&bucket=Hour" -H "Authorization: Bearer manuel-test-token-2026" | python3 -c "import json,sys; d=json.load(sys.stdin); print(len(d))"`
   (kesinlikle hiç çalıştırmanın olmadığı bir tarih aralığı).

**Beklenen sonuç**
- Tam `24` öge döner (24 saatlik kova), hepsi `runs: 0`, `cost: null` —
  boş bir aralık BOŞ bir liste DEĞİL, sıfırlanmış kovalarla dolu bir liste
  döndürür.

---

### MT-OBS-029 — Yalnızca süren run'ları içeren bir kova `averageDurationMs = null` döner, `runs > 0` olsa bile

Sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-04` (uzun, süren) ile bir çalıştırma BAŞLATILMIŞ, henüz
  BİTMEMİŞ olmalı (akış sürerken hemen sorguyu at).

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-04` gönder; HEMEN (akış bitmeden)
   `curl -s "http://localhost:5080/tracon/api/stats/timeseries?bucket=Hour" -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool | tail -20`.

**Beklenen sonuç**
- Son (güncel) kovada `runs >= 1` ama `averageDurationMs: null` — `CompletedAt`
  henüz yazılmadığı için bu run "settled" sayılmaz ve ortalamaya girmez.

### MT-OBS-030 — `maxTools=0` sunucuda `1`'e yükseltilir, `0` tool DEĞİL

Sınır durumu — `Math.Clamp(max, 1, 200)`, taban `1`'dir (`/api/stats`'in
`maxAgents` tabanı `0`'dan FARKLI).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- En az bir tool çağrısı yapılmış (`FIX-PROMPT-01`).

**Adımlar**
1. `curl -s "http://localhost:5080/tracon/api/tools/usage?maxTools=0" -H "Authorization: Bearer manuel-test-token-2026" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"`

**Beklenen sonuç**
- Sonuç `0` DEĞİL, `1`'dir (en az bir tool çağrısı varsa) — `maxTools=0`
  isteği sessizce `1`'e yuvarlanır.

---

### MT-OBS-031 — `startedAfter` filtresi run'ın BAŞLANGIÇ zamanına göre süzer, tool çağrısının kendi zamanına göre DEĞİL

Sınır durumu.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 6 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-PROMPT-01` ile `get_order_status` çağıran bir run bugün yapılmış.

**Adımlar**
1. `curl -s "http://localhost:5080/tracon/api/tools/usage?startedAfter=$(date -u -v+1H +%Y-%m-%dT%H:%M:%SZ)" -H "Authorization: Bearer manuel-test-token-2026"`
   (gelecekteki bir zaman — hiçbir run'ın `StartedAt`'i bunu geçmemiştir).

**Beklenen sonuç**
- Boş liste `[]` döner — filtre run'ın `StartedAt`'ine uygulanır; tool
  çağrısının kendi zaman damgası ayrıca değerlendirilmez.

### MT-OBS-032 — Dashboard sağlık verisini `refresh=true` OLMADAN çeker; 60 saniyelik önbellek payına düşer

Devre kesici ve sağlayıcı sağlığının derin mekaniği zaten
[`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md)/[`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md)'de
üretildi; burada yalnız Dashboard'un bu veriyi NASIL çektiği ölçülür.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 20 |
| **İlgili karar** | — |

**Adımlar**
1. Dashboard'ı aç, ağ sekmesinde `GET api/models/health` isteğinin sorgu
   dizgisine bak.

**Beklenen sonuç**
- İstek `refresh=true` PARAMETRESİ TAŞIMAZ (`api.modelsHealth(false)`) —
  Dashboard her açılışta canlı bir sağlık taraması TETİKLEMEZ, yalnız son
  önbelleklenmiş durumu okur.

### MT-OBS-033 — `tracon.run.cost` sayacı yalnız fiyatı BİLİNEN run'larda artar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 35 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-003`'ün fiyatlandırması tanımlı (`openai:gpt-5.4-mini` fiyatlı).
- `dotnet-counters ps` ile `Tracon.Api` sürecinin PID'si bulunmuş.

**Adımlar**
1. `dotnet-counters monitor -p <pid> --counters Tracon` çalıştır, ekranı
   açık bırak.
2. `playground/support` aç, `Merhaba` gönder, tamamlansın.
3. `dotnet-counters` ekranında `tracon.run.cost` satırının değerine bak.
4. `manuel-bos` (fiyatsız) ile bir çalıştırma daha yap, sayaç DEĞİŞİYOR mu
   gözlemle.

**Beklenen sonuç**
- Adım 3: sayaç sıfırdan büyük bir değere ARTAR, `currency`/`agent_name`/
  `model_id`/`tenant_id` etiketleriyle görünür.
- Adım 4: sayaç DEĞİŞMEZ — fiyatsız run `RecordCost` çağrısını hiç
  TETİKLEMEZ (`cost.Source == Unknown` iken metrik yayılmaz; sıfır yaymak
  gerçek harcamayı küçük gösterirdi).

---

### MT-OBS-034 — `tracon.run.cost` İPTAL edilen bir run'da da (fiyat biliniyorsa) artar

Sınır durumu — harcanan token'ın parası zaten harcanmıştır.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 35, 32 |
| **İlgili karar** | — |

**Ön koşul**
- `MT-OBS-033`'ün fiyatlandırması etkin; `dotnet-counters monitor` açık.

**Adımlar**
1. `playground/support` aç, `FIX-PROMPT-04` (uzun) gönder; akış sürerken
   çalıştırma sayfasına geç, "İptal Et"e tıkla, onayla (bkz.
   [`11-ARAYUZ-RUN-SESSION-SSE.md`](11-ARAYUZ-RUN-SESSION-SSE.md) `MT-UIRUN-022`).
2. İptal tamamlanınca `tracon.run.cost` sayacına bak.

**Beklenen sonuç (KOSUM-PLANI §2.1 ile düzeltildi — bkz. `kosumlar/2026-08-13/`)**
- OpenAI streaming protokolünde (`stream_options.include_usage=true`)
  `usage` nesnesi YALNIZ SON (terminal) SSE parçasında gelir — MAF bunu
  TEK bir `UsageContent` olarak, akışın en sonunda yüzeye çıkarır. Bu
  yüzden akış DOĞAL olarak bitmeden (`RunStatus.Completed`'a ulaşmadan)
  yapılan bir iptal, `usage`'ı HER ZAMAN `null` bulur — sayaç bu durumda
  ARTMAZ, `cost` de `null` kalır. "Harcanan token'ın parası zaten
  harcanmıştır" tasarım niyeti (`RunRecordingAgent.cs:825-826` yorumu)
  DOĞRUDUR ama yalnız `usage` GERÇEKTEN biliniyorsa uygulanabilir; OpenAI
  streaming'de akış bitmeden bu bilgi hiçbir zaman gelmez.

---

### MT-OBS-035 — 🚨 `tracon.quota.usage`/`.limit` VARSAYILANDA (kapalı bayrak) hiçbir ölçüm yaymaz

`EnableQuotaUsageGauge` varsayılanı `false`'tur (`RunCost` sayacının
AKSİNE, bu ek bir kaynak tüketimidir ve açıkça istenmelidir).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 35 |
| **İlgili karar** | — |

**Ön koşul**
- Hiçbir `Tracon:Observability:EnableQuotaUsageGauge` ayarı YAPILMAMIŞ
  (varsayılan `false`).
- `curl -i -X PUT "http://localhost:5080/tracon/api/quotas" -H "Authorization: Bearer manuel-test-token-2026" -H "Content-Type: application/json" -d '{"period":"Daily","maxRuns":1000}'`
  ile kiracı geneli bir kota kuralı tanımlanmış (kuralın KENDİSİ Faz 21'in
  konusudur — burada yalnız SCAFFOLD amaçlı kullanılır, bkz. Sınır tablosu).

**Adımlar**
1. `dotnet-counters monitor -p <pid> --counters Tracon` çalıştır.
2. `playground/support` ile birkaç çalıştırma yap (kota sayacını doldurmak
   için).
3. `tracon.quota.usage`/`tracon.quota.limit` satırlarını ara.

**Beklenen sonuç**
- İkisi de listede İSİM olarak GÖRÜNEBİLİR (enstrüman her zaman kayıtlıdır)
  ama HİÇBİR ölçüm/etiket YAYMAZ — `Snapshot()` bayrak kapalıyken boş liste
  döner. Arka plan tazeleyicisi başlangıçta hemen döner: zamanlayıcı kurulmaz,
  veritabanına hiç gidilmez.

---

### MT-OBS-036 — Bayrak açılınca aynı ölçerler kota kuralına karşılık gelen etiketli değerleri yayar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 35 |
| **İlgili karar** | — |

**Ön koşul**
```bash
dotnet user-secrets set "Tracon:Observability:EnableQuotaUsageGauge" "true"
dotnet user-secrets set "Tracon:Observability:QuotaUsageRefreshInterval" "00:00:05"
```
uygulama yeniden başlatılmış; `MT-OBS-035`'in kota kuralı hâlâ tanımlı ve en
az bir çalıştırma yapılmış olmalı.

**Adımlar**
1. `dotnet-counters monitor -p <pid> --counters Tracon` çalıştır, en az
   10 saniye bekle (arka plan tazelemesi için).
2. `tracon.quota.usage`/`tracon.quota.limit` satırlarını oku.
3. `playground/support` ile bir çalıştırma daha yap, 10 saniye bekle,
   `usage` satırını tekrar oku.

**Beklenen sonuç**
- Uygulama açıldıktan hemen sonraki İLK okuma boş olabilir: ölçer yalnız
  önbelleği okur ve ilk tazeleme henüz bitmemiş olabilir. Bu bir hata
  DEĞİLDİR.
- Adım 2'de ikisi de SIFIRDAN FARKLI değer(ler) taşır; etiketler arasında
  `quota_scope` (boş = kiracı geneli), `quota_period` (`Daily`),
  `quota_metric` (`Runs`) görünür.
- `usage`'ın değeri o ana kadarki run sayısına, `limit`'in değeri `1000`'e
  eşittir (`MT-OBS-035`'in tanımladığı kural).
- Adım 3'te `usage` bir artar. Değer en çok bir `QuotaUsageRefreshInterval`
  (burada 5 sn) gecikir; okuma veritabanını hiç beklemez.

---

### MT-OBS-037 — `IRunAttributionContext` kayıtlı DEĞİLKEN hiçbir davranış değişmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-478 |

**Ön koşul**
Uygulamada `IRunAttributionContext` kaydı YOK (örnek uygulamada
`DemoRunAttributionContext` kaydı geçici olarak kaldırılmış ya da hiçbir
`X-Demo-*` başlığı gönderilmiyor).

**Adımlar**
```bash
curl -s -X POST "$BASE/api/agents/summarizer/run" \
  -H "Authorization: Bearer $TOKEN" -H "$ROLE" -H "Content-Type: application/json" \
  -d '{"message":"merhaba"}' -N
curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/runs?take=1" | jq '.[0] | {userId, labels}'
```

**Beklenen sonuç**
- Çalıştırma `200` ile normal biter.
- `userId` ve `labels` **`null`** döner (boş nesne `{}` DEĞİL).

---

### MT-OBS-038 — 🚨 İstek gövdesindeki `userId` YOK SAYILIR (sahteleştirme reddi)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-478 |

**Ön koşul**
`IRunAttributionContext` kimlik hattına bağlı (örnek uygulamada
`DemoRunAttributionContext`; kullanıcı `X-Demo-User` başlığından gelir).

**Adımlar**
```bash
curl -s -X POST "$BASE/api/agents/summarizer/run" \
  -H "Authorization: Bearer $TOKEN" -H "$ROLE" -H "Content-Type: application/json" \
  -H "X-Demo-User: ada" -H "X-Demo-Labels: team=payments,ticket=OPS-1" \
  -d '{"message":"Özetle: Tracon çalıştırmaları kaydeder.","userId":"ATTACKER"}' -N
curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/runs?take=1" | jq '.[0] | {userId, labels}'
```

**Beklenen sonuç**
- `userId` **`"ada"`** — sunucunun çözdüğü kimlik.
- `userId` hiçbir koşulda `"ATTACKER"` OLMAZ; gövdedeki alan bağlanmaz.
- `labels` `{"team":"payments","ticket":"OPS-1"}` döner.

---

### MT-OBS-039 — Kullanıcı ve etiket kırılımı `/api/stats` içinde döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-485 |

**Ön koşul**
`MT-OBS-038` koşulmuş; ayrıca `X-Demo-User: grace` + `X-Demo-Labels: team=billing`
ile bir, `X-Demo-User: ada` + `X-Demo-Labels: team=payments` ile bir çalıştırma
daha yapılmış (toplam üç atıflı çalıştırma).

**Adımlar**
```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/stats" \
  | jq '{byUser: [.byUser[] | {userId, totalRuns}], byLabel: [.byLabel[] | {key, value, totalRuns}]}'
```

**Beklenen sonuç**
- `byUser` iki satır: `ada` → 2 çalıştırma, `grace` → 1 çalıştırma.
- `byLabel` üç satır: `team=payments` → 2, `team=billing` → 1, `ticket=OPS-1` → 1.
- 🚨 `byLabel` satırlarının toplamı (4) `totalRuns`'tan **BÜYÜKTÜR** — iki etiket
  taşıyan çalıştırma iki satıra girer. Bu bir kusur değil, etiket kümesinin
  çalıştırmaları bölümlemiyor olmasının sonucudur.
- Atıfsız eski çalıştırmalar `totalRuns` içinde kalır ama `byUser`'da GÖRÜNMEZ.

---

### MT-OBS-040 — Çalıştırma listesi kullanıcıya ve etikete göre süzülür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | — |

**Ön koşul**
`MT-OBS-039` koşulmuş.

**Adımlar**
```bash
for q in "?userId=ada" "?userId=grace" "?label=team:payments" "?label=team:billing" "?label=team"; do
  echo -n "$q -> "
  curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/runs$q" | jq 'length'
done
```

**Beklenen sonuç**
- `?userId=ada` → `2` · `?userId=grace` → `1`
- `?label=team:payments` → `2` · `?label=team:billing` → `1`
- `?label=team` (değersiz, yalnız anahtar) → `3` — anahtarın HER değerini eşler.

---

### MT-OBS-041 — 🚨 Etiket sınırı aşımı `400` verir; çalıştırma HİÇ başlamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-480 |

**Ön koşul**
`IRunAttributionContext` kimlik hattına bağlı. Önce `/api/stats` çağrılıp
`totalRuns` not edilir.

**Adımlar**
```bash
curl -s -w "\nHTTP %{http_code}\n" -X POST "$BASE/api/agents/summarizer/run" \
  -H "Authorization: Bearer $TOKEN" -H "$ROLE" -H "Content-Type: application/json" \
  -H "X-Demo-User: ada" -H "X-Demo-Labels: a=1,b=2,c=3,d=4,e=5,f=6,g=7,h=8,i=9" \
  -d '{"message":"merhaba"}'
curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/stats" | jq '.totalRuns'
```

**Beklenen sonuç**
- `HTTP 400`; `title` = `"Invalid run attribution"`.
- `detail` sınırı SAYIYLA söyler: `"The run carries 9 labels; at most 8 are allowed."`
- 🚨 `totalRuns` **DEĞİŞMEZ** — reddedilen istek hiçbir satır açmaz. Dokuzuncu
  etiket sessizce KIRPILMAZ; kırpılmış bir etiket kümesi raporu okuyana eksiksiz
  bir ölçüm gibi görünürdü.

---

### MT-OBS-042 — Prompt cache isabetinde maliyet cache oranıyla hesaplanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-483 |

**Ön koşul**
Gerçek bir OpenAI anahtarı tanımlı ve cache oranı YAPILANDIRILMIŞ:
```bash
export "Tracon__Pricing__Currency=USD"
export "Tracon__Pricing__openai__gpt-5.4-mini__Input=0.25"
export "Tracon__Pricing__openai__gpt-5.4-mini__Output=2"
export "Tracon__Pricing__openai__gpt-5.4-mini__CachedInput=0.025"
```
Uygulama bu ayarlarla yeniden başlatılmış olmalı.

**Adımlar**
1. **AYNI** uzun ön ekli (>1024 token; ~1500 kelimelik tekrar eden metin) bir
   mesajı `summarizer`'a arka arkaya **iki kez** gönder.
2. İki çalıştırmanın `usage` ve `cost` alanlarını oku:
```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/runs?take=2" \
  | jq '.[] | {input: .usage.inputTokens, cached: .usage.cachedInputTokens,
               inputCost: .cost.inputCost, cachedCost: .cost.cachedInputCost, source: .cost.source}'
```

**Beklenen sonuç**
- Birinci çalıştırma: `cached` = `0`, `inputCost` = `input × 0.25 / 1e6`.
- İkinci çalıştırma: `cached` **> 0**; `inputCost` = `(input − cached) × 0.25 / 1e6`
  ve `cachedCost` = `cached × 0.025 / 1e6`.
- İkinci çalıştırmanın TOPLAM maliyeti birincinin maliyetinden **belirgin biçimde
  düşüktür**.
- 🚨 Hesap ÇIKARMALIdır: `cachedInputTokens` `inputTokens`'ın **içinde** sayılır;
  tam fiyatlı girdiye eklenmez.

---

### MT-OBS-043 — Cache fiyatı TANIMSIZKEN maliyet eskisiyle aynı kalır, `Unknown`'a DÜŞMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-483 |

**Ön koşul**
`MT-OBS-042` ile aynı, ama `CachedInput` ayarı **VERİLMEDEN** (yalnız
`Input`/`Output`) yeniden başlatılmış.

**Adımlar**
`MT-OBS-042`'nin adımlarını tekrarla (aynı uzun mesaj, iki kez).

**Beklenen sonuç**
- `cachedInputTokens` yine sağlayıcının bildirdiği değeri taşır (**> 0**).
- `inputCost` girdinin **TAMAMI** tam fiyattan hesaplanır — cache oranı yokken
  hiçbir çıkarma yapılmaz; değer bu faz ÖNCESİYLE birebir aynıdır.
- `cachedInputCost` **`null`**.
- 🚨 `cost.source` **`Unknown` DEĞİL** (`Catalog` veya `Configuration`). Eksik
  cache oranı ile eksik MODEL fiyatı iki ayrı arızadır ve karıştırılmaz.

---

### MT-OBS-044 — Sağlayıcının bildirmediği sayaç `null` kalır, `0` OLMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | K-482 |

**Ön koşul**
Herhangi bir OpenAI çalıştırması yapılmış.

**Adımlar**
```bash
curl -s -H "Authorization: Bearer $TOKEN" -H "$ROLE" "$BASE/api/runs?take=1" | jq '.[0].usage'
```

**Beklenen sonuç**
- `cachedInputTokens` ve `reasoningTokens` sayısal değer taşır (sağlayıcı bunları
  bildirir; `0` da geçerli bir ÖLÇÜMdür).
- `audioInputTokens` ve `audioOutputTokens` **`null`** — sağlayıcı bunları hiç
  bildirmez.
- 🚨 Ayrım taşıyıcıdır: `0` "ölçüldü, yoktu" der; `null` "hiç ölçülmedi" der.
  İkisini ayıramayan bir rapor, susan her sağlayıcı için kendinden emin bir
  %0 cache isabet oranı gösterir.

---

### MT-OBS-045 — 👤 Gösterge panelinde token kırılım çubuğu ve listede kullanıcı süzgeci

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 68 |
| **İlgili karar** | — |

**Ön koşul**
`MT-OBS-042` koşulmuş (cache isabetli en az iki çalıştırma var).

**Adımlar**
1. Konsolda **Gösterge Paneli**'ni aç; "Token kırılımı" panelini bul.
2. **Çalıştırmalar** ekranına geç; "Kullanıcı kimliği" kutusuna `ada` yaz.
3. Bir çalıştırmanın ayrıntısına gir.

**Beklenen sonuç**
- 👤 Kırılım çubuğu dört dilime kadar gösterir (cache isabeti · girdi · akıl
  yürütme · çıktı); her dilim altındaki açıklamada adı ve token sayısıyla yazılır.
- 👤 Çubuğun altındaki not, cache ve akıl yürütmenin girdi/çıktı toplamlarının
  **içinden** ayrıldığını söyler.
- 👤 Kullanıcı süzgeci listeyi daraltır; ayrıntı sayfasında "çalıştıran `ada`"
  ve etiket rozetleri görünür.
- 👤 Dil `tr`'ye çevrildiğinde tüm bu metinler Türkçe gelir.

---

### MT-OBS-046 — Görsel fiyatı yalnız açık yapılandırmadan hesaplanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | K-032 |

**Ön koşul**
- `MT-MM-095` için gerçek görsel üretim çalışır.
- Önce `Tracon:Pricing:Images:<provider>:<model>` bölümü tamamen yoktur.

**Adımlar**
1. Bir görsel üret ve tool çağrısının `usage` kaydını oku.
2. Uygulamayı şu açık fiyatla yeniden başlat:
```bash
dotnet user-secrets set "Tracon:Pricing:Images:openai:<model>:PerImage" "0.04"
dotnet user-secrets set "Tracon:Pricing:Images:openai:<model>:SizeMultipliers:1024x1024" "2"
```
3. Aynı boyutta bir görsel daha üret ve iki `usage` kaydını karşılaştır.

**Beklenen sonuç**
- İlk çağrıda ölçülen `quantity` gerçek görsel sayısıdır, fakat `cost` **`null`**'dur;
  `0` değildir ve `isEstimated` ile fiyat uydurulmaz.
- İkinci çağrıda `unit=images`, `quantity=1`, `cost=0.08` ve yapılandırılmış
  para birimi döner. Boyut çarpanı yalnız per-image fiyatına uygulanır.

### MT-OBS-047 — Token fiyatı ile görsel başı fiyat birlikte yapılandırılamaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 88 |
| **İlgili karar** | — |

**Adımlar**
```bash
dotnet user-secrets set "Tracon:Pricing:Images:openai:<model>:PerImage" "0.04"
dotnet user-secrets set "Tracon:Pricing:Images:openai:<model>:OutputCostPerMillionTokens" "10"
cd samples/Tracon.Api && dotnet run -c Release
```

**Beklenen sonuç**
- Uygulama başlangıçta options validation hatasıyla durur. Hata, aynı image
  modelinde `PerImage` ve `OutputCostPerMillionTokens` değerlerinin birlikte
  olamayacağını açıkça söyler.
- İki değerden biri kaldırılmadan endpoint bağlı olmaz ve hiçbir görsel çağrısı
  para harcamaz.

---

### MT-OBS-048 — Sınır konmuş bir tool çıktısı kırpılır ve zarfa sarılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 89 |
| **İlgili karar** | — |

**Ön koşul**
Uygulama küçük bir kurulum varsayılanıyla başlatılmış. Sınır
`TruncatingAIFunction.MinimumEnvelopeBytes`'ın (bugün 57) altında **olamaz** —
düşük bir değer uygulamayı options validation hatasıyla başlatmaz:
```bash
export Tracon__Tools__DefaultMaxOutputBytes=100
cd samples/Tracon.Api && dotnet run -c Release --urls http://localhost:5081
```

**Adımlar**
1. `support` agent'ına, `get_order_status`'un doğal çıktısını 100 baytın üstüne
   çıkaracak kadar uzun bir sipariş numarasıyla bir mesaj gönder (kısa bir
   numarayla — ör. `ORD-1` — doğal çıktı 100 bayttan küçüktür ve kırpma HİÇ
   tetiklenmez):
```bash
curl -s -X POST "$BASE/api/agents/support/run" -H "$APB" -H "Content-Type: application/json" \
  -d '{"message":"What is the status of order ORD-0000000000000000000000000000000000000000000000000000-LONG?"}'
```
2. Dönen `RunId`'yi al, `run_events`'i oku:
```bash
curl -s "$BASE/api/runs/$RUN_ID/events" -H "$APB" \
  | jq '.[] | select(.type=="ToolOutputTruncated")'
```

**Beklenen sonuç**
- `functionResult` içeriği geçerli bir JSON zarfıdır: `{"truncated":true,"omittedBytes":N,"content":"..."}`.
- Zarfın toplam UTF-8 bayt boyutu **100'ü aşmaz**.
- `run_events`'te bir `ToolOutputTruncated` satırı vardır; `toolName` `"get_order_status"`,
  `toolCallId` gerçek çağrının kimliğiyle eşleşir, `text` alanı atlanan bayt
  sayısını ve sınırı okunabilir biçimde taşır (`"N byte(s) omitted (limit 100)"`),
  `payload` `{"maxOutputBytes":100,"omittedBytes":N}` biçimindedir.
- `ToolOutputTruncated` olayının sırası `ToolInvoking` ile `ToolInvoked` arasındadır.

---

### MT-OBS-049 — Sınır konmadığında çıktı dokunulmadan geçer (K1)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 89 |
| **İlgili karar** | — |

**Ön koşul**
Uygulama **varsayılan** yapılandırmayla (herhangi bir `MaxOutputBytes`
ayarlanmadan) çalışıyor.

**Adımlar**
1. MT-OBS-048'in 1. adımını tekrarla.
2. `run_events`'i oku.

**Beklenen sonuç**
- `run_events` içinde hiçbir `ToolOutputTruncated` satırı yoktur.
- `ToolInvoked` olayının `payload`'ı tool'un ham metnidir (bir zarf **DEĞİLDİR**) —
  `get_order_status` için `"Order ORD-1 has shipped. Estimated delivery: 2 days."`
  biçiminde, `truncated`/`omittedBytes`/`content` alanları yoktur.

---

### MT-OBS-050 — İç span'ler `tracon.run` kök span'inin çocuğu olmaya devam eder (Faz 107)

Faz 107 `RunRecordingAgent`'ı `partial` dosyalara ayırdı; span'i açan
`PrepareRun` ve kapatan `CompleteAsync` bu ayrımdan etkilenen metotlar
arasındaydı (dosya taşıdı, gövde değişmedi). Bu case ayrıştırmanın
kök/çocuk span hiyerarşisini bozmadığını kanıtlar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 107 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Api` ayakta (bkz. `11-ARAYUZ-RUN-SESSION-SSE.md` MT-UIRUN-050).

**Adımlar**
1. `POST .../support/run` ile gerçek bir çalıştırma yap.
2. `GET .../runs/{id}/trace` ile span ağacını oku.

**Beklenen sonuç**
- `tracon.run` kök span'i vardır; `invoke_agent`/`chat` gibi iç span'ler
  onun **çocuğudur**, kardeşi değil.
- **Otomatik koşuldu (2026-08-26):** bu hiyerarşi
  `ObservabilityTests.Inner_spans_become_children_of_root_span`
  (`tests/Tracon.Core.UnitTests/Diagnostics/ObservabilityTests.cs`) ve
  `RunRecordingAgentOutcomeMatrixTests` (span durum etiketi, dört sonuç için)
  ile otomatik koşuldu — tam koşumda 1967/1967 testin parçası olarak geçti.
  `PrepareRun`/`CreateScope`/`RunCoreAsync`/`RunCoreStreamingAsync`, span ve
  `scope` yaşam döngüsünü kuran dört metottur; Faz 107 bunları ya hiç
  taşımadı (girdi metotları ana dosyada kaldı) ya da birebir taşıdı
  (`PrepareRun`/`CreateScope` → `RunRecordingAgent.Lifecycle.cs`).
- **Kapanış (2026-08-26, F-164):** gerçek kusur kapatıldı. Kök neden config veya
  DI değildi. `RunTraceCollector`, tamponu `Activity.Parent` zincirinin
  tepesindeki span ile arıyordu; gerçek ASP.NET yolunda bu span run root değil,
  HTTP server span'idir. Collector artık zincirde kayıtlı en yakın ancestor
  tamponunu buluyor. `RunTraceEndToEndTests.Successful_run_is_persisted_when_sample_ratio_is_one`
  gerçek DI + HTTP + SSE + trace endpoint zincirini ve `tracon.run` span'ini
  doğrular. `SuccessSampleRatio=1` ile sample tekrarında trace endpoint `200`
  dönmelidir.

---

### MT-OBS-051 — Kompozisyonla yazılmış `IRunErrorClassifier`'ın KENDİ kuralı yerleşiği geçersiz kılar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 113 |
| **İlgili karar** | K1 |

**Ön koşul**
- Kayıtlı bir `IRunErrorClassifier`: `DefaultRunErrorClassifier`'ı doğrudan
  `new()` ile kurup (DI gerekmez, sıfır bağımlılıklıdır) kompozisyonla sarar —
  `RunError.Type == TraconProviderUnavailableException.ProviderUnavailableErrorType`
  ise kendi kuralını uygular (yerleşiğin `ProviderUnavailable` atadığı sınıfı
  bilerek başka bir sınıfa çevirir), aksi hâlde `builtIn.Classify(runError)`'a düşer.
- Birincili VE yedeği İKİSİ de sürekli bağlantı reddiyle düşen bir agent
  (zincir kesin tükenir → gerçek bir `TraconProviderUnavailableException`).

**Adımlar**
1. Agent'a bir mesaj gönder (zincir tükenir).
2. `GET /api/runs/{runId}` ile `error.class`'ı oku.

**Beklenen sonuç (2026-08-26'da ölçüldü)**
- `run.error.type` = `"provider_unavailable"` (yerleşiğin kendisi bunu
  `ProviderUnavailable`'a eşlerdi) ama `run.error.class` tüketicinin KENDİ
  kuralının sonucudur — yerleşik ATLANMIŞTIR. `run.error.fingerprint` DOLUDUR
  (`RunErrorFingerprint.Compute` ile üretilmiştir).

---

### MT-OBS-052 — Aynı hata iki kez üretilince aynı `fingerprint` altında kümelenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 113 |
| **İlgili karar** | — |

**Ön koşul**
- MT-OBS-051'in aynı kurulumu.

**Adımlar**
1. MT-OBS-051'in AYNI çağrısını tekrar gönder.
2. İki `run`'ın `error.fingerprint` alanlarını karşılaştır.

**Beklenen sonuç (2026-08-26'da ölçüldü)**
- İki `run`'ın `fingerprint`'i **birebir aynıdır** (ölçülen değer:
  `b8e7c79d8be5af55023bbb6ebef993579ac38fca417e4739aedd85c4be541fa4`) —
  kompozisyonla üretilen parmak izi de yerleşiğin kullandığı
  `RunErrorFingerprint.Compute`'u çağırdığı için aynı kümeye düşer.

---

### MT-OBS-053 — 🚨 Tüketici sınıflandırıcısı exception atarsa run DURMAZ; sınıf yerleşikten gelir, hata loglanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 113 |
| **İlgili karar** | K1 |

**Ön koşul**
- MT-OBS-051'in kurulumu, ama sınıflandırıcı HER çağrıda bilerek
  `InvalidOperationException` fırlatacak şekilde değiştirilmiş.

**Adımlar**
1. Agent'a bir mesaj gönder.
2. `GET /api/runs/{runId}` ile `run.status`'u ve `error.class`'ı oku.
3. Sunucu loglarını `"registered IRunErrorClassifier threw"` için tara. 👤 insan gerekir (log gözü).

**Beklenen sonuç (2026-08-26'da ölçüldü)**
- Adım 2: `run.status` = `"Failed"` — run kayıtsız veya asılı KALMAZ, terminal
  durumuna ulaşır. `error.class`, tüketicinin (bozuk) kuralı değil, YERLEŞİK
  `DefaultRunErrorClassifier`'ın bu hata için verdiği karardır.
- Adım 3: `_logger.LogError` çağrısı loglarda görünür:
  `"The registered IRunErrorClassifier threw while classifying a run error;
  falling back to the built-in classifier."` ve altında gerçek istisna
  (`InvalidOperationException`) durur.

---

### MT-OBS-054 — 🚨 Geçersiz sağlayıcı kimlik bilgisiyle çalışan bir `run`, ham sağlayıcı metnini `error.message`'a yazmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 119 |
| **İlgili karar** | K-640 |

**Ön koşul**
- `samples/Tracon.Api` — `openai`/`anthropic`/`google` sağlayıcılarından
  birine **geçersiz** bir API anahtarı ver (`dotnet user-secrets set
  "Tracon:Providers:OpenAI:ApiKey" "sk-gecersiz"`).

**Adımlar**
1. O sağlayıcıya bağlı bir agent'ı çalıştır (`POST /api/agents/{ad}/run`).
2. `GET /api/runs/{runId}` ile `error.message`'ı ve `error.type`'ı oku.
3. Sunucu konsol logunu aynı isteğin zaman aralığında tara.

**Beklenen sonuç**
- Adım 2: `error.message` ham sağlayıcı metnini (401 gövdesi, anahtar öneki,
  sağlayıcının kendi hata cümlesi) **taşımaz**. Sözleşme zaten
  `ProviderFailureNormalizer` ile sabit `"The model provider request failed."`
  metnine iner (`error.type` = `upstream_error`) — bu Faz 119'dan önce de
  doğruydu, bu case regresyon olmadığını doğrular.
- Adım 3: Tam sağlayıcı hatası (`ex.ToString()`) `Tracon.ModelProvider`
  kategorisiyle loglanmıştır.

### MT-OBS-055 — 🚨 Kuyruklu (`respond-async`) bir `run`'ın oturum açma hatası, `jobs.error_message`'a ham metin sızdırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 119 |
| **İlgili karar** | K-640 |

**Ön koşul**
- `samples/Tracon.Api`, kalıcı bir SQL sağlayıcısı (`UsePostgreSql`/
  `UseSqlite`) ile çalışıyor — `jobs` tablosunu okumak için gerekli.

**Adımlar**
1. İşin İÇİNDE bir oturum açma hatası üret.
   🚨 **Uzun `sessionId` BU İŞİ YAPMAZ — üç sağlayıcıda da ölçüldü
   (2026-09-19).** PostgreSQL ve SQLite'ta `sessions.id` ile
   `runs.session_id` `text`tir (sınırsız); SQL Server'da ikisi de
   `nvarchar(200)`, yani **eşit genişlikte**, ve run satırı oturumdan ÖNCE
   yazılır — her aşırı uzun değer `503` ile kuyruğa girmeden durur ve
   `jobs` satırı hiç oluşmaz. Tetikleyici yeniden tasarlanmalıdır:
   iş çalışırken oturum deposunu **yabancı** bir istisnayla düşüren bir yol
   gerekir (agent silmek yetmez — o Tracon'un kendi `TraconException`'ını
   üretir ve doğru olarak redakte EDİLMEZ).
2. `GET /api/jobs/{jobId}` (veya doğrudan `jobs` tablosu) ile `errorMessage`'ı oku.
3. Sunucu logunu `"Queued run"` + `"(ref:"` için tara.

**Beklenen sonuç**
- Adım 2: `errorMessage` yabancı exception'ın (ör. depo sürücüsünün SQL
  hatası) ham metnini taşımaz; `"{TypeName} failed. (ref: {kimlik})"` biçiminde,
  tip adı + korelasyon kimliği taşır.
- Adım 3: Aynı korelasyon kimliğiyle **tam** exception detayı (`ex.ToString()`)
  bulunur — `AgentRunJobHandler.FailQueuedRunAsync` bu tam detayı
  `_logger.LogError` ile aynı `correlationId` ile yazar.

### MT-OBS-056 — Hata döndüren bir webhook hedefi, gövdesini `webhook_deliveries.error`'a yazdırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 119 |
| **İlgili karar** | K-640 |

**Ön koşul**
- Bir webhook subscription kaydet, hedef adres iç ayrıntı (örn. bir
  `Authorization` başlığı yankısı) döndüren bir gövdeyle `500` yanıtlayan bir
  test endpoint'i olsun (örn. `webhook.site` yerine yerel bir `httpbin`/basit
  `nc` dinleyicisi).

**Adımlar**
1. Bir `run.completed` olayı tetikle.
2. Teslimat başarısız olduktan sonra `GET /api/webhooks/{id}/deliveries`
   ile `error` alanını oku.

**Beklenen sonuç**
- `error` yalnız `"HTTP 500"` biçimindedir — hedefin döndürdüğü gövde
  **hiçbir baytı** ile görünmez. Durum kodu teşhis için yeterlidir
  (119.3 Açık Soru #3, seçenek A).

### MT-OBS-057 — MCP `tools/call` üzerinden çalıştırılan bozuk bir agent, ham hata metnini `CallToolResult`'a yazmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 119 |
| **İlgili karar** | K-640 |

**Ön koşul**
- MCP sunucusu açık (`UseMcpServer`), MT-OBS-054'ün geçersiz anahtarlı
  agent'ı dışa açık.

**Adımlar**
1. MCP istemcisinden (`summarizer` veya eşdeğeri) `tools/call` gönder.
2. Dönen `CallToolResult`'ın metnini oku.

**Beklenen sonuç**
- Metin `"'{agent}' could not be run: {güvenli metin}"` biçimindedir; güvenli
  metin tip adı + `(ref: ...)` taşır, sağlayıcının ham hata cümlesini taşımaz.

### MT-OBS-058 — 👤 Mimari cırcır kapısı: yeni bir `catch (Exception` bloğunun `.Message`'ı taban çizgisi dışında kalırsa build kırılır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 119 |
| **İlgili karar** | K-640 |

**Ön koşul**
- Geliştirme ortamı; `src/` içine geçici bir dosya eklenecek.

**Adımlar**
1. `src/Tracon.Core/` altına, bir `catch (Exception exception)` bloğu
   içinde `context.Something = exception.Message;` yazan geçici bir dosya ekle.
2. `dotnet test tests/Tracon.Core.UnitTests -c Release --no-build
   --filter-method "*RawExceptionTextSite*"` çalıştır (derlenmiş ikili ile).
3. Geçici dosyayı sil.

**Beklenen sonuç (2026-08-27'de ölçüldü)**
- Adım 2: `RawExceptionTextSiteTests.Raw_exception_text_sites_match_the_baseline`
  **kırmızı** döner; hata mesajı yeni siteyi `<dosya>:<metot>` biçiminde adlandırır
  ve `TRACON_RAW_EXCEPTION_TEXT_REFRESH=1` ile nasıl kapatılacağını söyler.
  Gerçek çıktı: `"+ src/Tracon.Core/__RatchetProbeTemp.cs:Probe: new raw-exception-text
  site, not in the baseline"`. Dosya silinip yeniden koşulduğunda yeşile döndü.

---

### MT-OBS-059 — `modelProvider` ve birim fiyatlar taşınır, snapshot kalır (toplama girmez, yeniden hesaplama üzerine yazmaz)

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 132 |
| **İlgili karar** | K-483, K-650 |

**Ön koşul**
- `MT-OBS-003`'ün fiyatlandırması etkin (`openai:gpt-5.4-mini` Input `0.15`,
  Output `0.60`).
- `MT-MYU-002`'nin `birincil-kirik` agent'ıyla bir çalıştırma yapılmış (yedek devrede).
- `MT-OBS-021`'in fiyatsız (`manuel-bos`) çalıştırması var.

**Adımlar**
1. `playground/support` aç, `Merhaba` gönder, tamamlansın; `runId`'yi not al.
2. `curl -s ".../api/runs/<runId>" -H "Authorization: Bearer manuel-test-token-2026" | python3 -m json.tool | grep -A10 "modelId\|modelProvider\|cost"` — hem `support` hem `birincil-kirik` için.
3. `dotnet user-secrets set "Tracon:Pricing:openai:gpt-5.4-mini:Input" "999"`, yeniden başlat, `curl -X POST ".../api/stats/recalculate-costs"`.
4. Adım 2'yi `support`'un `runId`'si için tekrarla.

**Beklenen sonuç**
- Adım 2 (`support`): `modelId "gpt-5.4-mini"`, `modelProvider "openai"`,
  `cost.inputPricePerMillionTokens 0.15`, `outputPricePerMillionTokens 0.6`;
  `inputCost`/`outputCost` yalnız `usage.inputTokens`/`outputTokens` × oran /
  1 000 000'dır — birim fiyat alanları bu toplama KATILMAZ.
- Adım 2 (`birincil-kirik`): `modelProvider` birincilin DEĞİL, yedeğin
  sağlayıcısıdır (`openai`).
- Adım 3'ün yanıtı `runsConsidered`/`runsUpdated`/`runsStillUnknown`/
  `runsSkipped` taşır; `support`'un run'ı `runsSkipped`'e girer
  (`runsConsidered`'e SAYILMAZ), `manuel-bos`'unki `runsConsidered`'e girer.
- Adım 4: maliyet ve birim fiyat Adım 2 ile AYNIDIR — `999` yansımaz. Fiyat
  `dotnet user-secrets remove ...:Input` ile geri alınır.
- 👤 Arayüzde `support`'un run detayında "MODEL"in yanında "PROVIDER"
  (`openai`), altında "Input/Output/Cached input price" `<tutar> / 1M tokens`
  biçiminde görünür; Türkçe'de "SAĞLAYICI"/"Girdi/Çıktı/Önbellek girdi
  fiyatı". `manuel-bos`'ta birim fiyat karoları HİÇ görünmez.

---

### MT-OBS-060 — `run_scores` zaman aralığı sorgusu indeks kullanır, tam tablo taraması değil

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 154 |
| **İlgili karar** | — |

**Ön koşul**
- PostgreSQL sağlayıcısı, migration'lar uygulanmış.

**Adımlar**
1. `psql -c "\di+ *run_scores*"` — indeks listesini gör.
2. `psql -c "EXPLAIN SELECT * FROM tracon.run_scores WHERE tenant_id = 'default' AND created_at >= now() - interval '30 days';"`

**Beklenen sonuç**
- Adım 1: `run_scores_created_at_idx` (`tenant_id, created_at`) ve
  `run_scores_target_author_name_idx` (Faz 152) birlikte listelenir.
- Adım 2: plan `run_scores_created_at_idx` üzerinden bir Index Scan/Bitmap
  Index Scan gösterir — `Seq Scan on run_scores` **görünmez**.

---

### MT-OBS-061 — Sağlıklı bir `run` hiçbir kayıt kaybı saymaz

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 173 |
| **İlgili karar** | K-782 |

**Ön koşul**
- `samples/Tracon.Api` ayakta, PostgreSQL ayakta, migration'lar uygulanmış.
- `dotnet-counters ps` çıktısındaki **`Tracon.Api`** PID'i alınmış.
  🚨 `dotnet run`'ın kendi PID'i **değildir** — ona bağlanan oturum hiçbir
  ölçüm görmez ve boş bir CSV bırakır.

**Adımlar**
1. `dotnet-counters collect --process-id <PID> --counters Tracon --refresh-interval 1 --duration 00:00:00:30 --format csv --output healthy.csv`
2. Koşum sürerken: `curl -X POST http://localhost:5080/tracon/api/agents/support/run -H 'Content-Type: application/json' -d '{"message":"healthy run"}'`
3. `awk -F',' '$NF != 0' healthy.csv | grep -E "recording_failures|tracon.runs"`

**Beklenen sonuç**
- `tracon.runs[...;tracon.run.status=Completed]` bir kez `1` olur.
- `tracon.run.recording_failures` satırlarının hiçbiri sıfırdan farklı
  **değildir** (seri hiç görünmeyebilir veya sürekli `0` okur; ikisi de geçer).

---

### MT-OBS-062 — 🚨 `runs` yazılamazken `run` TAMAMLANIR ve kayıp `stage=start` ile sayılır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 173 |
| **İlgili karar** | K-782 |

**Ön koşul**
- MT-OBS-061'in kurulumu.
- 🚨 **Veritabanını tamamen durdurma.** `run` uca hiç ulaşmaz: akış
  başlamadan önceki okumalar (ek dosya sahipliği · deney ataması · parametre
  kapısı) **sesli** düşer ve uç `HTTP 500` döner — `RunEventWriter` kurulmaz
  bile. Ölçülen şey yazma yolu olduğu için yalnız **yazma** engellenir.

**Adımlar**
1. ```sql
   CREATE OR REPLACE FUNCTION tracon.block_runs() RETURNS trigger AS $$
   BEGIN RAISE EXCEPTION 'runs is not writable'; END;
   $$ LANGUAGE plpgsql;
   CREATE TRIGGER block_run_insert BEFORE INSERT ON tracon.runs
     FOR EACH ROW EXECUTE FUNCTION tracon.block_runs();
   ```
2. MT-OBS-061 Adım 1 gibi bir `collect` oturumu başlat.
3. `curl -X POST .../agents/support/run -d '{"message":"blocked run"}'`
4. `awk -F',' '$NF != 0' blocked.csv | grep recording_failures`
5. `DROP TRIGGER block_run_insert ON tracon.runs;`

**Beklenen sonuç**
- Adım 3: `HTTP 200`; akış `event: done` ile **normal biter**; istemci
  yanıtın tamamını alır. Kayıt kaybı ürünü kesmez.
- Adım 4: `tracon.run.recording_failures[tracon.recording.stage=start;tracon.tenant.id=default]`
  **bir kez** `1` olur.
- Aynı koşumda `tracon.recording.stage=input` de bir kez `1` olur — `runs`
  satırı hiç yazılmadığı için `run_inputs` yabancı anahtarı da düşer. **İki
  ayrı aşama, iki ayrı seri**: tek sayaç ayrımı kaybetmez.
- `tracon.runs[...;status=Completed]` yine `1` olur — `run` tamamlandı.
- Log'da: `Tracon run recording was disabled (the run record could not be opened). Run <id> continues normally.`
- Adım 5'ten sonra `SELECT count(*) FROM tracon.runs` yalnız **sağlıklı**
  koşumları sayar; engelli koşumun satırı yoktur.

---

### MT-OBS-063 — Hata fırlatan bir `IRunEventSink` `stage=sink` ile sayılır ve `store` kaydı eksiksiz kalır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 173 |
| **İlgili karar** | K-782 |

**Ön koşul**
- `OnEventAsync`'inde koşulsuz `throw` eden bir `IRunEventSink` kayıtlı.
- Veritabanı **sağlam**.

**Adımlar**
1. Bir `run` koş, `collect` oturumu açıkken.
2. `awk -F',' '$NF != 0' sink.csv | grep recording_failures`
3. `GET /tracon/api/runs/{id}/events`

**Beklenen sonuç**
- Sayaç `tracon.recording.stage=sink` ile **bir kez** artar — `run` kaç olay
  yazarsa yazsın, sink ilk hatadan sonra o `run` için düşürülür.
- `stage=start`/`event`/`completion` **artmaz**: `store` etkilenmedi.
- Adım 3: olay akışı **eksiksizdir**.
- Log'da düşen sink'in **tipi** adlandırılır (etikette değil, log satırında).

---

### MT-OBS-064 — `run_inputs` yazılamazken `run` tamamlanır, yalnız replay ölür

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 173 |
| **İlgili karar** | K-782 |

**Ön koşul**
- `Tracon:RunRecording:RecordRunInput=true` (varsayılan).
- `tracon.run_inputs` üzerine MT-OBS-062'deki gibi bir INSERT engeli.
  `tracon.runs` **serbesttir**.

**Adımlar**
1. Bir `run` koş, `collect` oturumu açıkken.
2. `awk -F',' '$NF != 0' input.csv | grep recording_failures`
3. `POST /tracon/api/runs/{id}/replay`

**Beklenen sonuç**
- Sayaç yalnız `tracon.recording.stage=input` ile artar; `start` **artmaz**.
- `run` tamamlanır ve `GET /tracon/api/runs/{id}` kaydı **eksiksiz** döner —
  kayıp yalnız girdi tarafındadır.
- Adım 3 replay başarısız olur (girdi yok).

---

### MT-OBS-065 — `stage` etiketi kapalı kümenin dışına çıkamaz

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 173 |
| **İlgili karar** | K-782 |

**Ön koşul**
- Depo kökü.

**Adımlar**
1. `grep -n "Disable(" src/Tracon.Core/Recording/RunEventWriter.cs`
2. `grep -rn "RecordRunRecordingFailure(" src/`

**Beklenen sonuç**
- Adım 1: her `Disable` çağrısının ikinci argümanı bir
  `RunRecordingStages.*` sabitidir — **hiçbiri serbest metin değildir**.
- Adım 2: her çağrı yerinde `stage` argümanı yine bir `RunRecordingStages.*`
  sabitidir.
- `RunRecordingStages.All` altı değer taşır ve
  `RunRecordingFailureMetricTests.The_stage_set_is_closed_and_its_values_are_metric_safe`
  bu kümeyi **rakamla** kilitler.

---
