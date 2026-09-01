# Faz 132 — Uygulanan Fiyat Snapshot'ı ve Sağlayıcı Kimliği

> **Durum:** 📋 Planlandı (2026-09-01)
> **Kaynak:** [kesif/2026-09-01-tuketici-feature-talepleri.md](kesif/2026-09-01-tuketici-feature-talepleri.md) — **F-175**
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`, `.AspNetCore`, `.UI`
> **Yeni paket:** Yok · **Migration:** gerekli — üç set (`runs` tablosuna dört sütun) + üç `MigrationsViews` güncellemesi; numara uygulama anında alınır
> **Public API:** büyüyor (`RunCost` alanları, `RunRecord.ModelProvider`) **ve bir uç davranışı daralıyor**. `wc -l src/*/PublicAPI.Shipped.txt` → her dosya 1 satır; shipped giriş sıfır, bugün eklemek ucuz
> **Tüketici yüzeyi:** `docs-site/`: `guides/observability.md`, `reference/read-views.md`, `concepts/runs.md`, `http-api.md` · sevk edilen: `RunCost` ve `IRunPricingResolver` XML dokümanı, `runs_v1` sütun tablosu
> **Manuel test alanı:** [`docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md`](manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır.

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-483\|K-178\|K-631" docs/KARARLAR.md
   ```
   **K-483** (elle tekrarlanan toplama ifadesi kusur SINIFI üretir; `RunCost`
   `Total()` taşır), **K-178** (migration numaraları sağlayıcı başına
   bağımsızdır), **K-631** (fiyat çözümleyici boru hattı kurulumunda geç çözülür).
3. [Faz 111](arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md) — yalnız devir notu:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/111-OKUMA-SOZLESMESI-GORUNUMLERI.md
   ```
   `runs_v1` yayınlanmış bir okuma sözleşmesidir. **Sütun eklemek serbesttir**;
   sütun kaybetmek veya daraltmak yeni bir görünüm gerektirir.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/olcum-kota-ve-secenekler.md`](hafiza/olcum-kota-ve-secenekler.md)
   (K-483'ün vakası), [`hafiza/sql-saglayicilari.md`](hafiza/sql-saglayicilari.md)
   (üç lehçede elle yazılmış sorgu ve görünüm),
   [`hafiza/frontend.md`](hafiza/frontend.md) (maliyet ekranları ve sözlük).
5. Gerektiğinde: [`MIMARI.md`](MIMARI.md) — veri modeli bölümü.

---

## Amaç

Bu faz iki işi birlikte yapar: bir **çelişkiyi** kapatır ve bir **kanıt
boşluğunu** doldurur.

**Çelişki.** `RunCost`'un XML dokümanı şunu ilan ediyor: *"computed and written
once when the run ends (a price snapshot) — a later change to the price list
does not change past values."* Ama AgentPrism `POST /api/stats/recalculate-costs`
ucunu sevk ediyor ve o uç **bütün** `run` maliyetlerini güncel fiyatla yeniden
yazıyor. Üstelik bunu yanlış yapıyor: `run` satırında sağlayıcı saklanmadığı
için fiyatı yalnız model adıyla çözüyor. Beyan ile davranış çelişiyor.

**Kanıt boşluğu.** İki tarihsel `run` aynı sağlayıcı, aynı model ve aynı token
sayısına sahip olup farklı maliyet taşıyabilir. Yalnız toplam maliyetle bu
farkın nedeni açıklanamaz — hesaba giren birim fiyat hiçbir yerde durmuyor.

- **F-175** — uygulanan birim fiyatların `run` kaydına yazılması, `run`
  satırına sağlayıcı kimliğinin eklenmesi ve yeniden hesaplama ucunun
  fiyatlanmamış satırlara daraltılması.

### Sahiplik sınırı

AgentPrism kullanımı ölçer, teknik maliyeti hesaplar ve **uygulanan fiyatın
kaynağını** saklar. Tüketici sözleşmeyi, indirimi, vergiyi, markup'ı, fatura
ve muhasebe yuvarlamasını yönetir. Bu faz AgentPrism'i bir faturalama ürününe
dönüştürmez.

### Kapsam dışı — bilerek

| Kalem | Neden bu fazda değil |
|---|---|
| Katalog revizyonu / `SourceRevision` | Ne `ModelDescriptor` ne `AgentPrism:Pricing` bir revizyon kavramı taşıyor. Revizyon izleme, katalog sürümleme tasarımı ister — ayrı iş. Tüketici de "ilk değerli adım resolver sonucunun snapshot taşımasıdır" diyor |
| Tarih aralıklı (effective-dated) fiyat kataloğu | Aynı gerekçe |
| `tool_invocations`, ses ve görsel maliyetleri | `0015_tool_usage.sql` `pricing_source` sütununu **bilerek** dışarıda bıraktı; ses fiyatının tek kaynağı vardır. Bu faz model kullanımını kapsar |
| Dinamik routing policy | Bu fazın sağlayıcı alanı onun ön koşuludur; policy ayrı bir fazdır |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunSupportTypes.cs:88-91`](../src/AgentPrism.Abstractions/Runs/RunSupportTypes.cs) | `RunCost`'un XML'i "price snapshot … a later change to the price list does not change past values" diyor |
| [`RunCostRecalculationService.cs:7-13`](../src/AgentPrism.Core/Recording/RunCostRecalculationService.cs) | Aynı repoda: *"Every call is a **full** recalculation — there is no 'only unknown ones' filter"* ve *"the provider is not stored on historical rows, resolution is done by model name alone"* |
| [`CatalogEndpoints.cs:193`](../src/AgentPrism.AspNetCore/Endpoints/CatalogEndpoints.cs) | `POST /api/stats/recalculate-costs` sevk edilmiş bir uçtur |
| [`IRunStore.cs:276`](../src/AgentPrism.Abstractions/Runs/IRunStore.cs) | `UpdateRunCostAsync` — "Used only by the maintenance endpoint" |
| [`RunRecord.cs`](../src/AgentPrism.Abstractions/Runs/RunRecord.cs) | Alanlar arasında `ModelId` var, **sağlayıcı alanı yok** |
| [`RunRecordingAgent.Completion.cs:80`](../src/AgentPrism.Core/Recording/RunRecordingAgent.Completion.cs) | 🚨 `var modelProvider = fallbackUsed?.Provider ?? _modelProvider;` — gerçekten cevap veren sağlayıcı **burada biliniyor** |
| [`RunRecordingAgent.Completion.cs:86`](../src/AgentPrism.Core/Recording/RunRecordingAgent.Completion.cs) | `_pricingResolver?.Resolve(modelProvider, modelId, usage)` — sağlayıcı fiyatlamaya giriyor, ama `CompleteAsync` çağrısında **yazılmıyor** |
| [`0011_run_costs.sql`](../src/AgentPrism.PostgreSql/Migrations/0011_run_costs.sql) | `runs` tablosunda `input_cost`, `output_cost`, `cost_currency`, `pricing_source`; birim fiyat sütunu yok |
| [`0034_run_attribution.sql:40`](../src/AgentPrism.PostgreSql/Migrations/0034_run_attribution.sql) | `cached_input_cost` sonradan eklendi; sağlayıcı sütunu yine yok |
| [`0001_read_views.sql:5`](../src/AgentPrism.PostgreSql/MigrationsViews/0001_read_views.sql) | Kural yazılı: *"Adding a column is free"* — `runs_v1` büyüyebilir |
| [`ModelDescriptor.cs`](../src/AgentPrism.Abstractions/Models/ModelDescriptor.cs) | Fiyat alanları **milyon token başına**: `InputCostPerMillionTokens`, `OutputCostPerMillionTokens`, `CachedInputCostPerMillionTokens` |

> Kanıtlar 2026-09-01 tarihinde doğrulandı.

---

## 132.1 — Uygulanan birim fiyat `RunCost` içine girer

```csharp
public sealed record RunCost
{
    // mevcut: InputCost, OutputCost, CachedInputCost, Currency, Source, Total()

    public decimal? InputPricePerMillionTokens { get; init; }        // yeni
    public decimal? OutputPricePerMillionTokens { get; init; }       // yeni
    public decimal? CachedInputPricePerMillionTokens { get; init; }  // yeni
}
```

**Alan adı birimi taşır.** `InputUnitPrice` gibi bir ad, birimi okuyucunun
tahminine bırakırdı. Katalog zaten milyon token başına fiyatlıyor; ad bunu
tekrarlar ve dönüştürme hatası sınıfını kapatır.

🚨 **Bu üç alan bir toplama terimi DEĞİLDİR.** `RunCost.Total()` yalnız
`InputCost + OutputCost + CachedInputCost` toplar. Birim fiyatları toplama
sokmak K-483'ün vakasını tersine çevirir. Alanların XML dokümanı bunu açıkça
yazar ve `ReadViewCostTermTests` görünüm tarafında aynı ayrımı korur.

**Bilinmeyen fiyat `null`'dır, sıfır değildir.** `PricingSource.Unknown` olan
bir `run`'da üç alan da `null`'dır. Sıfır "bedava" demek olurdu; bu, mevcut
`PricingSource` dokümanının zaten yazdığı kuraldır.

**`Currency` bilinen her maliyet için zorunludur.** `Source` `Unknown`
değilse ve `Currency` boşsa bu bir kusurdur; `RunPricingResolver` bunu
doldurur, test bunu ölçer.

**`RunTreeCost` birim fiyat taşımaz.** Bir ağaç birden çok modeli kapsayabilir;
tek bir birim fiyat orada anlamsızdır. Ağaç toplamı toplam olarak kalır.

## 132.2 — `run` satırı sağlayıcıyı saklar

`RunRecord.ModelProvider` eklenir ve `runs.model_provider` sütununa yazılır.
Değer, `RunRecordingAgent.Completion.cs:80`'de **zaten hesaplanan**
`modelProvider`'dır — yani yedek zincirinden bir link cevap verdiyse onun
sağlayıcısı, yoksa birincil binding'inki. Cost, metrik ve `runs.model_id`
ile aynı kaynağı kullanır; ayrı bir yol açılmaz.

Eski satırlar `null` kalır. Yeniden hesaplama eski satırda model adıyla,
yeni satırda sağlayıcı+model ile çözer; bu fark `guides/observability.md`
sayfasında yazılır.

## 132.3 — Yeniden hesaplama daralır

`POST /api/stats/recalculate-costs` **yalnız `PricingSource.Unknown` taşıyan
satırları** yeniden fiyatlar. Fiyatlanmış bir `run` bir daha değişmez.

```mermaid
flowchart TD
    E["POST /api/stats/recalculate-costs"] --> Q["runs sorgusu"]
    Q --> F{"pricing_source"}
    F -->|"Unknown"| R["Resolve(provider, model, usage)"]
    F -->|"Catalog / Configuration"| S["ATLA — snapshot dokunulmaz"]
    R --> N{"fiyat bulundu mu?"}
    N -->|"evet"| U["UpdateRunCostAsync"]
    N -->|"hayır"| K["stillUnknown++"]
```

**Bu bir davranış değişikliğidir ve bilinçlidir.** Ucun meşru işi, bir modelin
fiyatı yapılandırılmadığı için boş kalan satırları doldurmaktır — eksik veriyi
onarmak. Fiyatlanmış satırı yeniden yazmak, snapshot'ın var olma sebebini yok
eder. Karar kapanışta `KARARLAR.md`'ye girer.

`RunCostRecalculationResult`'ın alan anlamları da güncellenir: `Considered`
artık "taranan `Unknown` satır" sayısıdır. XML dokümanı ve site sayfası bunu
yazar.

`UpdateRunCostAsync` **kaldırılmaz**: daraltılmış uç hâlâ ona ihtiyaç duyar ve
`IRunStore` yayınlanmış bir depolama sözleşmesidir.

## 132.4 — Kalıcılık ve görünüm

| Yüzey | Değişiklik |
|---|---|
| `runs` tablosu | Dört sütun: `model_provider text`, `input_price_per_mtok numeric(20,10)`, `output_price_per_mtok numeric(20,10)`, `cached_input_price_per_mtok numeric(20,10)` — üç sağlayıcı için üç migration |
| `runs_v1` görünümü | Aynı dört sütun eklenir. Kural gereği **serbesttir**; yeni görünüm sürümü gerekmez. `ReadViewColumnSetTests` güncellenir |
| `PostgresQueries` · `SqlServerQueries` · `SqliteQueries` | `runs` yazma/okuma sorguları üç lehçede ayrı ayrı |
| `InMemoryRunStore` | Aynı alanlar |
| `RunStoreContract` | Snapshot'ın değişmezliğini ölçen case |
| Arayüz | `run` detayında birim fiyat ve sağlayıcı; yeni metin `en.ts` **ve** `tr.ts` (K-228) |

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bölüme yazılır.

```csharp
// AgentPrism.Abstractions
public sealed record RunCost
{
    public decimal? InputPricePerMillionTokens { get; init; }
    public decimal? OutputPricePerMillionTokens { get; init; }
    public decimal? CachedInputPricePerMillionTokens { get; init; }
    // Total() DEĞİŞMEZ — birim fiyatlar toplama girmez.
}

public sealed record RunRecord
{
    public string? ModelProvider { get; init; }
}

public sealed record RunCostRecalculationResult
{
    // Alan adları korunur; Considered'ın ANLAMI daralır ve XML'de yazılır.
}
```

`IRunPricingResolver.Resolve` imzası **değişmez**. Dönen `RunCost` daha çok
alan taşır; çağıran kod değişmez.

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/stats/recalculate-costs` | Admin | **Daraldı** — yalnız `Unknown` fiyatlı `run`'ları fiyatlar |
| `GET` | `/api/runs/{id}` | Reader | Yanıt birim fiyatları ve sağlayıcıyı taşır |

Yeni uç yoktur. Sunucu yanıtları çevrilmez (K-232).

### Arayüz payı

`run` detayına birkaç satır. Bugünkü paket: `index-*.js.br` 148 807 B
(bütçe 250 KB gzip). Kapanışta yeniden ölçülür.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Runs/
├── RunSupportTypes.cs   (RunCost alanları + XML "toplama terimi değildir")
├── RunRecord.cs         (ModelProvider)
└── IRunStore.cs         (UpdateRunCostAsync XML'i daralan sözleşmeyi yazar)

src/AgentPrism.Core/
├── Models/RunPricingResolver.cs          (birim fiyatları sonuca koyar)
├── Recording/RunRecordingAgent.Completion.cs (modelProvider'ı yazıma geçirir)
├── Recording/RunEventWriter.cs           (CompleteAsync imzası)
└── Recording/RunCostRecalculationService.cs (Unknown filtresi)

src/AgentPrism.Sql.Shared/Stores/SqlRunStore.cs
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Internal/*Queries.cs
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/Migrations/NNNN_price_snapshot.sql
src/AgentPrism.{PostgreSql,SqlServer,Sqlite}/MigrationsViews/0001_read_views.sql
src/AgentPrism.AspNetCore/Endpoints/CatalogEndpoints.cs
src/AgentPrism.UI/frontend/src/screens/run-detail.tsx + locales/{en,tr}.ts
src/AgentPrism.Testing.Contracts.Xunit/Contracts/RunStoreContract.cs
docs-site/src/content/docs/guides/observability.md · reference/read-views.md · concepts/runs.md
```

🚨 **İmza değiştirmek ile gövdeyi kullanmak iki ayrı adımdır.** `RunCost`
üretilen ve okunan bir tiptir: `RunPricingResolver` onu üretir, `SqlRunStore`
ve `InMemoryRunStore` onu okur/yazar, arayüz onu gösterir. Yeni alan
`RunPricingResolver` dışında bir yerde doldurulmazsa hiçbir derleme hatası
oluşmaz — alan sessizce `null` kalır. Her yazma yolu tek tek izlenir.

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Fiyat listesi değişince eski `run` maliyeti değişir | Sözleşme (`RunStoreContract`) | dört koşumda birden |
| Yeniden hesaplama fiyatlanmış satıra dokunur | Fonksiyonel | `RunCostRecalculationScopeTests` |
| Birim fiyat `Total()` toplamına girer | Birim | `RunCostTotalTests` — K-483'ün sınıf koruması |
| Bilinmeyen fiyat `0` yazılır | Birim + Sözleşme | `RunPricingResolverTests` + `RunStoreContract` |
| `Currency` bilinen maliyette boş kalır | Birim | `RunPricingResolverTests` |
| Sağlayıcı yazılmaz; yedek link cevap verdiğinde yanlış sağlayıcı yazılır | Fonksiyonel | `RunProviderAttributionTests` — `ModelFallbackUsed` olan `run`'da |
| `numeric(20,10)` yuvarlaması birim fiyatı bozar | Sözleşme | `RunStoreContract` — üç SQL sağlayıcıda tam değer geri okunur |
| Bir lehçenin sorgusu güncellenmez | Sözleşme (`RunStoreContract`) | dört koşumda birden |
| `runs_v1` sütun kümesi kayar | Birim | mevcut `ReadViewColumnSetTests` |
| Görünümün maliyet toplamı birim fiyatı da toplar | Birim | mevcut `ReadViewCostTermTests` |
| Eski satırda `model_provider` `null` iken yeniden hesaplama çöker | Fonksiyonel | `RunCostRecalculationLegacyTests` |
| Başka kiracının `run` maliyeti yeniden hesaplanır | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| Yeni alanlar OpenAPI/TS istemcisine çıkmaz | E2E | mevcut OpenAPI tazelik kapısı |

Beş soru: **iptal** — yeniden hesaplama sayfa sayfa ilerler ve `CancellationToken`
her sayfada kontrol edilir; **eşzamanlılık** — aynı `run` üzerinde eşzamanlı
`UpdateRunCostAsync` çağrısı, `Unknown` filtresi sayesinde ikinci kez yazmaz;
**boş/aşırı girdi** — `usage` `null`, model `null`, çok büyük token sayısı;
**başka kiracı** — `UpdateRunCostAsync` beklenen kiracıyı zaten alır, filtre
onu korur; **alt sistem hatası** — fiyat çözümleyici hata verirse `run` devam
eder, maliyet `Unknown` kalır (gözlemlenebilirlik işlevi bozmaz).

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Fiyat yapılandırılmış | `run` yap, `GET /api/runs/{id}` | Yanıt birim fiyatları ve `modelProvider`'ı taşır |
| 2 | Case 1'in `run`'ı · fiyatı **değiştir** | `POST /api/stats/recalculate-costs`, sonra aynı `run`'ı oku | Maliyet ve birim fiyat **değişmez** |
| 3 | Fiyatı olmayan bir modelle `run` yap, sonra fiyatı ekle | `POST /api/stats/recalculate-costs` | Maliyet dolar; `pricing_source` `Unknown`'dan çıkar |
| 4 | Yedek zinciri olan agent, birincil hata verir | `run` yap | `modelProvider` **yedek** linkin sağlayıcısıdır; fiyat da onunla hesaplanmıştır |
| 5 | Fiyatı olmayan model | `run` yap | Birim fiyatlar `null`; **sıfır değil** |
| 6 | `EnableReadViews` açık | `SELECT * FROM runs_v1 LIMIT 1` | Dört yeni sütun görünür 👤 |
| 7 | Arayüz | `run` detayını aç | Birim fiyat ve sağlayıcı iki dilde doğru görünür 👤 |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `QuotedAt` (fiyatın çözüldüğü an) da saklansın mı? | A: hayır — `run`'ın `CompletedAt`'i zaten var · B: evet | **A.** Fiyat `run` bitişinde çözülür; ayrı bir zaman damgası aynı bilgiyi ikinci kez saklar |
| 2 | Daralan uç, atlanan satır sayısını yanıtta bildirsin mi? | A: evet — yeni `Skipped` alanı · B: hayır | **A.** Operatör "neden hiçbir şey değişmedi" sorusunu yanıtsız bırakmamalı; alan eklemek `RunCostRecalculationResult`'ta ucuzdur |
| 3 | Eski satırlar için `model_provider` geri doldurulsun mu? | A: hayır, `null` kalır · B: `run_events`'ten türetilmeye çalışılır | **A.** Türetme tahmindir; `null` dürüsttür ve `PricingSource` zaten kaynağı söyler |

---

## Bitiş Ölçütleri (DoD)

- [ ] `run` yanıtı uygulanan birim fiyatları ve `modelProvider`'ı taşır (case 1)
- [ ] Fiyat listesi değişse de fiyatlanmış `run` **değişmez** (case 2)
- [ ] Yeniden hesaplama yalnız `Unknown` satırları doldurur (case 3)
- [ ] Yedek link cevap verdiğinde sağlayıcı doğru yazılır (case 4)
- [ ] Bilinmeyen fiyat `null` kalır, `0` olmaz (case 5)
- [ ] `RunCost.Total()` birim fiyatları **toplamaz**; `RunCostTotalTests` bunu ölçer
- [ ] `runs_v1` dört yeni sütunu taşır; `ReadViewColumnSetTests` ve `ReadViewCostTermTests` yeşil
- [ ] `RunStoreContract` dört koşumun dördünde de yeşil
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`guides/observability.md`, `reference/read-views.md`, `concepts/runs.md`); `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı

### Doğrulama komutları

```bash
# Snapshot alanları
curl -s http://localhost:5081/agentprism/api/runs/<id> \
  | jq '{provider: .modelProvider, cost: .cost}'

# Daralan uç
curl -s -X POST http://localhost:5081/agentprism/api/stats/recalculate-costs | jq

# Görünüm sütunları
psql -c "SELECT model_provider, input_price_per_mtok FROM agentprism.runs_v1 LIMIT 1"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Birim fiyatlar `Total()`'a eklenir ve maliyet iki katına çıkar | K-483 aynı sınıfın vakasıdır; XML açıkça yazar, `RunCostTotalTests` ölçer, `faz-denetim` toplama ifadesini tarar |
| Uç daralınca bir tüketici "hiçbir şey değişmiyor" der | Yanıta `Skipped` alanı (Açık Soru 2) + `guides/observability.md` ve XML dokümanı davranışı yazar |
| Üç lehçenin biri güncellenmez | `RunStoreContract` dört koşumda birden çalışır; sessiz kalan lehçe orada düşer |
| `RunCost` yeni alanı yalnız bir yazma yolunda doldurulur | Derleme hatası vermez; faz uygulaması `grep -rn "new RunCost\|RunCost {" src/` çıktısını kontrol listesi yapar |
| `numeric(20,10)` birim fiyat için dar kalır | Katalog fiyatları milyon token başına ondalıklardır; mevcut maliyet sütunları aynı tipi kullanıyor. Uygulamada bir uç değerle ölçülür ve sonuç kapanışta yazılır |
| `runs_v1`'e sütun eklemek bir tüketicinin `SELECT *` sorgusunu bozar | Yayınlanmış kural sütun eklemeyi serbest bırakıyor; `reference/read-views.md` bunu zaten yazıyor |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. K-NNN numaraları burada alınır — yeniden hesaplama
> ucunun daralması bir karardır ve buraya yazılır.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
