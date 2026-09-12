# Faz 68 — Çalıştırma Kimliği ve Token Kırılımı

> **Durum:** ✅ Tamamlandı (2026-08-19)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-111**, **F-112**
> **Önkoşul:** [Faz 20](20-MALIYET-VE-GOSTERGE-PANELI.md) — maliyet hesabı ve gösterge paneli · [Faz 41](41-KIRACI-YALITIMININ-ZORLANMASI.md) — kiracı yalıtımı sözleşmesi
> **Paketler:** `Tracon.Abstractions`, `Tracon.Core`, `Tracon.Sql.Shared`, `Tracon.PostgreSql`, `Tracon.SqlServer`, `Tracon.Sqlite`, `Tracon.AspNetCore`, `Tracon.UI`
> **Yeni paket:** Yok · **Migration:** **gerekli — üç set** (`runs` tablosuna sütunlar). Numara uygulama anında alınır (K-178)
> **Public API:** **büyüyor — dört `sealed record` birden.** `RunRecord`, `RunStartInfo`, `RunUsage`, `RunCost` ve iki istatistik tipi. `PublicAPI.Shipped.txt` bugün **boş** (ölçüldü: 16 satır, hepsi `#nullable enable`) — şimdi bedava, Faz 7'den sonra F-50 dışında en pahalı değişiklik
> **Site etkisi:** `concepts/runs.md`, `guides/observability.md`, `reference/configuration.md`, `concepts/governance.md`
> **Manuel test alanı:** [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](../../manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

> ### ⚗️ Damıtılmış kayıt
> Bu dosya fazın **planını** değil, fazın bıraktığı **kalıcı bilgiyi**
> taşır. Plan gövdesi, planlanan/gerçekleşen API, dosya listesi, risk ve
> açık soru bölümleri kapanışta düştü — **silinmedi, git geçmişindedir.**
>
> Tam metin — kopyala, çalıştır:
>
> ```bash
> git show 9c32242:docs/arsiv/fazlar/68-CALISTIRMA-KIMLIGI-VE-TOKEN-KIRILIMI.md
> ```
>
> Damıtıldı 2026-08-23 · `scripts/dokuman-bakim.py faz-damit`

---

## Amaç

Tracon bugün "hangi kiracı ne harcadı" sorusunu cevaplıyor. "Hangi **kullanıcı**" ve "hangi **iş**" sorularını cevaplayamıyor. Aynı şekilde token sayacı üç alandır; prompt caching'in kazancı ve reasoning token'ının payı görünmüyor. Bu faz ikisini birlikte kapatır — ikisi de aynı `runs` satırına yazılır ve ayrı planlanırsa aynı tabloya iki migration gider.

## Bitiş Ölçütleri (DoD)

Hepsi gerçek ölçümle karşılandı; kanıtlar örnek uygulamada **gerçek OpenAI
çağrılarıyla** ve gerçek PostgreSQL'e karşı alındı.

- [x] `IRunAttributionContext` kayıtlı değilken hiçbir davranış değişmez;
      `user_id` ve etiket `NULL` yazılır — `Nothing_changes_when_no_attribution_context_is_registered`
      + sözleşme testi `A_run_without_attribution_reads_back_as_null_not_as_an_empty_map`
- [x] İstek gövdesine konan `userId` **yok sayılır** — gerçek çağrı:
      `{"message":"...","userId":"ATTACKER"}` → kayıt `userId: "ada"`.
      Test: `A_userId_in_the_request_body_is_ignored` (+ tersi yönü de)
- [x] `GET /api/stats` `byUser`/`byLabel` kırılımını doğru toplamlarla döner
      (`groupBy` eklenmedi — K-485). Gerçek çıktı:
      `byUser: [('ada',2,108), ('grace',1,47)]`,
      `byLabel: [('team','payments',2), ('team','billing',1), ('ticket','OPS-1',1)]`
- [x] Cache token'ı bildiren bir sağlayıcıda maliyet, cache oranı uygulanarak
      hesaplanır — **gerçek OpenAI prompt cache isabeti** (aşağıdaki komut ve çıktı)
- [x] Cache token'ı bildirmeyen sağlayıcıda alan `null` kalır — **sıfır değil**.
      Gerçek çıktı aynı satırda ikisini birden gösteriyor:
      `cachedInputTokens: 0` (bildirildi) yanında `audioInputTokens: null` (bildirilmedi)
- [x] Etiket sınırı aşımı `400` verir — gerçek çıktı:
      `"The run carries 9 labels; at most 8 are allowed."`, `totalRuns` **değişmedi**
- [x] Metrik etiket kümesi değişmedi — `TelemetryTagTests` üç testle sabitliyor
- [x] Üç SQL sağlayıcısı + bellek içi sözleşme koşumları geçer —
      SQLite 570 · PostgreSQL 1114 (bellek içi dâhil) · SQL Server 556, hepsi yeşil
- [x] Dört doğrulama kapısı sıfır uyarı verir — build ✅ (0 uyarı, 0 hata) · test **4248/0** ✅ ·
      pack ✅ (0 hata, 0 uyarı) · format ✅
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [x] `secret` taraması boş döndü (eşleşmeler faz öncesinden gelen yer tutucu
      yorum satırları; gerçek değer yok)
- [x] Manuel kabul case'leri `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`
      içine eklendi (**MT-OBS-037…045**); 👤 işaretli olan biri dışında hepsi koşuldu
- [x] `faz-denetim` koşuldu; **dört 🔴 bulgu üretildi ve dördü de kapatıldı**
- [x] `docs-site/` güncellendi (`concepts/runs.md`, `concepts/governance.md`,
      `guides/observability.md`, `reference/configuration.md`)
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Ölçülen bundle payı

| | Faz öncesi | Faz sonrası | Fark |
|---|---:|---:|---:|
| `index-*.js.br` | 140.614 B | 145.822 B | +5.208 B |
| `index-*.css.br` | 5.490 B | 5.497 B | +7 B |
| **Toplam brotli** | **146.104 B** | **151.319 B** | **+5.215 B** |

Bütçe 250 KB gzip; derleme `172,2 KB gzipped (budget 250 KB)` raporladı. Yeni
bağımlılık **alınmadı** — kırılım çubuğu var olan bileşenlerle çizildi.

### Doğrulama komutları ve gerçek çıktılar

```bash
# Kullanıcı ve etiket kırılımı
curl -s "http://localhost:5080/tracon/api/stats" | jq '.byUser, .byLabel'
# byUser  : ada 2 run / 108 token · grace 1 run / 47 token
# byLabel : team=payments 2 · team=billing 1 · ticket=OPS-1 1
#           (toplam 4 > 3 atıflı run — etiket kümesi run'ları BÖLÜMLEMEZ)

# Süzgeçler
?userId=ada -> 2 · ?userId=grace -> 1
?label=team:payments -> 2 · ?label=team:billing -> 1 · ?label=team -> 3

# Cache token'ı gerçekten ayrı yazılmış mı
curl -s "http://localhost:5080/tracon/api/runs?take=1" | jq '.[0].usage'
# { inputTokens: 39, outputTokens: 20, totalTokens: 59,
#   cachedInputTokens: 0, reasoningTokens: 0,        <- sağlayıcı BİLDİRDİ (0 bir ölçümdür)
#   audioInputTokens: null, audioOutputTokens: null } <- sağlayıcı HİÇ bildirmedi
```

**Gerçek prompt cache isabeti** — aynı ~2560 token'lık ön ek iki kez, fiyatlar
`Input=0.25 / Output=2 / CachedInput=0.025`:

```
1. run (soğuk): input=2560 cached=0     -> inputCost=0.00064   cachedCost=0
2. run (sıcak): input=2560 cached=2304  -> inputCost=6.4e-05   cachedCost=5.76e-05
```

`(2560−2304) × 0.25/1e6 = 6,4e-05` ve `2304 × 0.025/1e6 = 5,76e-05` — çıkarmalı
hesap birebir tutuyor; ikinci `run` yaklaşık **3,9 kat ucuz**.

**Aynı senaryo, `CachedInput` ayarı KALDIRILARAK** (DoD "cache fiyatı tanımsız"):

```
input=2560 cached=2304 -> inputCost=0.00064  (girdinin TAMAMI tam fiyattan)
                          cachedInputCost=null
                          source=Configuration   <- `Unknown` DEĞİL
```

Faz öncesiyle birebir aynı değer; eksik cache oranı ile eksik model fiyatı iki
ayrı arıza olarak ayrık kaldı.

## Plandan Sapmalar

| # | Plan ne diyordu | Ne yapıldı | Gerekçe |
|---|---|---|---|
| 1 | `GET /api/stats?groupBy=user\|label` | `groupBy` **eklenmedi**; `byUser`/`byLabel` her zaman döner, `userId`/`label` ise **özetin tamamını** daraltır | K-485. `ByAgent`/`ByModel`/`ByVersion`/`ByErrorClass`'ın hiçbiri koşullu değil ve `SqlRunStore.GetStatisticsAsync` sonuç kümelerini **konuma göre** okuyor; kümeleri koşullu yapmak o okuyucuyu kırılgan hâle getirirdi |
| 2 | Plan `RunStartInfo` üretim noktalarını saymamıştı | **Beş** üretim noktası bulundu (`RunRecordingAgent`, `AgentEndpoints`, `ApprovalEndpoints`, `WorkflowRunner`, `InboundTriggerDispatcher`) ve her biri elle izlendi | `faz-uygulama` Adım 4. Kuyruğa alınan `run` yolu attribution'ı **kaybediyordu** → UPSERT'te `COALESCE` koruması (K-486) |
| 3 | Ses alanları "bedava" sayılıyordu | `InputAudioTokenCount`/`OutputAudioTokenCount` MEAI 10.8.3'te **`[Experimental]`** çıktı (`MEAI001` → hata) | K-484. Bastırma iki `return` deyimine daraltıldı ve `UsageBreakdown` içinde toplandı |
| 4 | Plan yalnız `RunUsage`/`RunCost`/`RunRecord`/istatistik tiplerini listeliyordu | `RunCost.Total()`/`RunTreeCost.Total()` ve `RunAttributionReader` **plan dışı** eklendi | Denetim 🔴#1/#2/#4'ün kök nedeni: elle yazılmış iki terimli maliyet toplamları ve iki ayrı attribution okuması. Tek bir doğruluk kaynağı sınıfın tamamını kapatır |
| 5 | Plan `RunStatistics`'e yalnız kırılım ekliyordu | `CachedInputTokens`/`ReasoningTokens`/`AudioInputTokens`/`AudioOutputTokens` toplamları da eklendi | Gösterge panelindeki kırılım çubuğu bu dört sayaç olmadan çizilemez; aynı `record`'a ikinci kez dokunmak Faz 7 sonrası kırıcı olurdu |
| 6 | Plan `CachedInputCost`'u yalnız `RunCost`'a koyuyordu | `RunTreeCost`'a da eklendi ve **her** maliyet toplamı tarandı (üç dialektte 24 nokta + bellek içi + çalışma anı) | Cache ücreti üçüncü bir terimdir: `runs.input_cost` cache'i zaten dışarıda bırakır, iki terim toplayan her sorgu **eksik** raporlar |

> **Kapsam dışı bırakıldı, gerekçesiyle:** Faz 64'ün veri konusu silme akışı
> `runs.user_id` üzerinden **eşleşmez**. `IDataSubjectResolver`'ın kendi
> dokümanı "subject id'yi Tracon'in satırlarında saklamak" alternatifini
> açıkça reddediyor; hangi `user_id`'nin hangi veri konusuna ait olduğunu yalnız
> tüketici bilir. Çözüm yolu `docs-site/concepts/governance.md`'ye yazıldı:
> resolver `GET /api/runs?userId={id}&includeChildren=true` ile `run` kimliklerini
> bulup `RunIds`'e koyar; `runs` satırı silindiğinde `user_id` de gider.
> `DataSubjectScope`'a `UserIds` alanı eklemek ayrı bir aday kalemidir.

## Bu Fazda Verilen Kararlar

| K | Konu |
|---|---|
| **K-478** | Çalıştırma kimliği `IRunAttributionContext`'ten gelir; istek **gövdesinden asla alınmaz** |
| **K-479** | Etiketler ayrı tabloya değil `runs.labels` JSON sütununa yazılır (açık soru 1) |
| **K-480** | Sınır aşımı **kırpılmaz, reddedilir**; gürültülü sınır HTTP'de (`400`), sessiz düşürme kayıt yolunda |
| **K-481** | Kullanıcı ve etiket **metrik etiketi olmaz**; kapı `TelemetryTagTests` |
| **K-482** | Kırılım toplamların **içinde** sayılır; bildirilmeyen sayaç `null` kalır, `0` olmaz |
| **K-483** | Fiyat **çıkarmalı**; tanımsız cache oranı `Unknown`'a düşürmez (açık soru 2/3) |
| **K-484** | `MEAI001` bastırması tek dosyada (`UsageBreakdown`) toplandı |
| **K-485** | `groupBy` eklenmedi; kırılımlar her zaman döner |
| **K-486** | Attribution UPSERT'te `COALESCE` ile **korunur**, üzerine yazılmaz |

## Denetim Bulguları

`faz-denetim` taze bağlamlı bağımsız bir denetçiyle koşuldu. **Dört 🔴 bulgu
üretildi ve dördü de kapatıldı.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🔴 | `cached_input_cost` **çalışma anındaki** toplamların hiçbirinde yoktu: kota muhasebesi, `tracon.run.cost` metriği, webhook özeti, workflow kotası. Maliyet tavanı olan bir kiracı tavanı **aşabilirdi** | **Düzeltildi.** `RunCost.Total()`/`RunTreeCost.Total()` eklendi ve elle yazılmış iki terimli her toplam ona bağlandı. Sınıf taraması `ModelRunJudge`, `OnlineEvalSummaryService` ve arayüzdeki `run-comparison.tsx`'i de yakaladı (denetçinin 🟢#10'u) |
| 2 | 🔴 | Aynı eksiklik **bellek içi** store'un zaman serisi ve deney sonuçlarındaydı; üç SQL dialektinde ise düzeltilmişti → **aynı sorgu store'a göre farklı yanıt** veriyordu | **Düzeltildi.** İkisi de `RunCost.Total()` kullanıyor. Kapı: `Every_cost_total_includes_the_cache_charge` — özet, zaman serisi, deney sonucu ve ağaç toplamını **tek testte** ve dört koşumda birden iddia eder |
| 3 | 🔴 | Katalogla fiyatlanan bir modele cache oranı **hiçbir yoldan verilemiyordu**: dört sağlayıcı uzantısı anahtarı okumuyordu ve katalog fiyatı `Tracon:Pricing`'i eziyor. `docs-site` bu anahtarın çalıştığını söylüyordu | **Düzeltildi.** `OpenAI`/`Anthropic`/`Google`/`Azure` uzantıları `CachedInputCostPerMillionTokens`'ı okuyor |
| 4 | 🔴 | Workflow yolu attribution'ı **doğrulamadan, dondurmadan, korumasız** okuyordu: tüketicinin implementasyonu fırlatırsa workflow `run`'ı ölürdü; 200 karakterden uzun `userId` SQL Server insert'ini patlatıp **workflow'un tüm kaydını sessizce kaybettirirdi** | **Düzeltildi.** Garantiler `RunAttributionReader`'a çıkarıldı; agent ve workflow yolu aynı uygulamayı paylaşıyor |
| 5 | 🟡 | Faz dokümanı hâlâ `?groupBy=user` diyordu | **Düzeltildi** — DoD, `endpoint` tablosu, manuel case ve doğrulama komutu gerçeğe hizalandı (K-485) |
| 6 | 🟡 | Cache oranı tanımlıyken sağlayıcı bildirmezse `CachedInputCost` `0` yazılıyordu — fazın kendi "sıfır bir iddiadır" kuralına aykırı | **Düzeltildi** + iki test (`A_defined_rate_produces_no_cache_charge_when_the_provider_reported_nothing`, `A_reported_zero_cache_count_does_produce_a_zero_charge`) |
| 7 | 🟡 | `ModelRunJudge` kendi `RunUsage`'ını dört alan olmadan kuruyordu | **Düzeltildi** — `UsageBreakdown` kullanıyor |
| 8 | 🟡 | Sözleşme testi cache ücretini zaman serisi ve deney sonucu için hiç sormuyordu (1–2'nin testten kaçma sebebi) | **Düzeltildi** — bulgu 2'nin kapısı |
| 9 | 🟢 | SQL Server'ın CI collation'ı `user_id` karşılaştırmasını harf duyarsız yapar | **Devredildi** — var olan desen (`agent_name`, `session_id` aynı durumda), bu fazın sapması değil |
| 10 | 🟢 | `run-comparison.tsx` maliyet toplamı | Bulgu 1'in sınıf taramasıyla birlikte **kapatıldı** |
| 11 | 🟢 | Yeni süzgeçlerde debounce yok | **Devredildi** — ölçülmedi, kapsam dışı iyileştirme |

**Denetçinin temiz bulduğu başlıklar:** test tiyatrosu · test seviyesi ·
imza-gövde kayması (dört token alanı beş toplama noktasında da eksiksiz) ·
public API kaydı · repo kuralları (dil sınırı, XML doküman, `TryAdd`, `secret`,
MAF sarmalama) · ürün yüzeyi. Ayrıca ölçtü: üç dialektin `runColumns` sırası
birebir aynı (0–51) ve `SqlRunStore.ReadRun`'ın sabit ordinal'leriyle eşleşiyor.

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler**

- `IRunAttributionContext` — `TryAdd` ile kayıtlı, varsayılan
  `DefaultRunAttributionContext` yalnız `AmbientRunAttributionScope`'u okur.
  🚨 Bir kayıt yolunda **doğrudan okuma**; `RunAttributionReader.Read(...)`
  kullan — istisna yutma, bütün-hâlinde düşürme ve dondurma orada.
- `RunCost.Total()` / `RunTreeCost.Total()` — 🚨 "bu `run` ne tuttu" sorusunun
  **tek** cevabı. `InputCost + OutputCost` elle toplanmaz; cache ücreti üçüncü
  terimdir ve unutulursa toplam sessizce eksik çıkar (denetimin birinci bulgusu
  tam olarak buydu).
- `RunLabels` — sınırların tek kaynağı. Yeni bir giriş noktası eklersen
  gürültülü reddi (`400` / `ArgumentException`) orada kur.
- `UsageBreakdown` — `UsageDetails`'in dört kırılım sayacına **tek** erişim
  noktası; `MEAI001` bastırması burada yaşar.

**Bilinen tuzaklar**

- 🚨 `runs` tablosuna sütun eklerken `runColumns` sırası üç dialektte de
  **sona** eklenir; `SqlRunStore.ReadRun` sabit ordinal okur. Bugünkü son
  ordinal **51**'dir.
- 🚨 `SelectRunStatistics` sonuç kümeleri **konuma göre** okunur. Bugün
  **sekiz** küme var (özet · agent · model · sürüm · hata sınıfı · küme ·
  kullanıcı · etiket). Yeni küme **sona** eklenir.
- 🚨 Bir maliyet toplamı eklediğinde `cached_input_cost`'u unutma — üç dialekt,
  bellek içi store, çalışma anı (kota/metrik/webhook) ve arayüz. Kapı:
  `Every_cost_total_includes_the_cache_charge`.
- 🚨 Tek `$` işaretli raw interpolated string'de `{{` kaçış **değildir**; SQL'e
  literal süslü parantez yazmak yerine `IS NOT NULL` guard'ı kullan.
- 🚨 Gösterge panelinde boş grafik metni artık **iki** panelde görünür;
  Playwright'ta `GetByText(...).First` zorunlu.

**Açık uçlar**

- `DataSubjectScope`'a `UserIds` alanı (yukarıdaki kapsam-dışı notu).
- SQL Server collation duyarsızlığı (denetim 🟢#9) — var olan desen, ayrı kalem.
- Süzgeç debounce'u (denetim 🟢#11).
