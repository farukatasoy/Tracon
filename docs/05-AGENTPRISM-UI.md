# Faz 5 — AgentPrismUI

> **Durum:** 🔜 Sıradaki
> **Önkoşul:** [04-HTTP-API.md](04-HTTP-API.md) — tamamlandı
> **Sonraki:** [06-GOZLEMLENEBILIRLIK.md](06-GOZLEMLENEBILIRLIK.md)
> **Paket:** `AgentPrism.UI`

---

## Bu Faza Başlarken

Önce şunları bu sırayla okuyun:

1. [`MIMARI.md`](MIMARI.md) — bölüm 3 (dört değişmez kural), bölüm 6 (çalıştırma yolu), bölüm 7 (güvenlik)
2. [`KARARLAR.md`](KARARLAR.md) — kapatılmış tartışmaları yeniden açmayın
3. [`04-HTTP-API.md`](04-HTTP-API.md) — "Gerçekleşen Public API", "Plandan Sapmalar" ve "Faz 5'e Devreden Notlar"
4. [`../MEMORY.md`](../MEMORY.md) — önceki oturumların keşfettiği tuzaklar
5. Bu doküman

Arayüzü geliştirirken **çalışan bir arka uç** gerekir:

```bash
cd samples/AgentPrism.Api && dotnet run
# http://localhost:5080/agentprism/api/meta
```

Örnek uygulama API anahtarı olmadan da çalışır (ağ çağrısı yapmayan `EchoModelProvider`).

---

## Devraldığınız HTTP Sözleşmesi

Bu uçlar **tamamlandı ve testlidir** (78 fonksiyonel test). Faz 5 bunları değiştirmez, tüketir.

```
GET    {prefix}/api/meta                       [kimlik dogrulamasi YOK]
       -> { version, prefix, authentication:{allowRemoteAccess,requiresBearerToken,
            requiresAuthorizationPolicy}, storage:{persistent,agentDefinitionStore,runStore,sessionStore} }

GET    {prefix}/api/agents                     -> AgentDescriptor[]
GET    {prefix}/api/agents/{name}              -> { descriptor, definition|null, isEditable }
POST   {prefix}/api/agents                     <- AgentDefinitionRequest        -> 201 | 409
PUT    {prefix}/api/agents/{name}              <- AgentDefinitionRequest        -> 200 | 400 | 404 | 409
DELETE {prefix}/api/agents/{name}              -> 204 | 404 | 409
GET    {prefix}/api/agents/{name}/versions     -> AgentDefinition[]  (yeniden eskiye)
POST   {prefix}/api/agents/{name}/rollback     <- { version }        -> 200 | 404 | 409
POST   {prefix}/api/agents/{name}/run          <- { message, sessionId? }       -> SSE

GET    {prefix}/api/sessions?agentName=&skip=&take=   -> SessionRecord[]
GET    {prefix}/api/sessions/{id}              -> { id, agentName, tenantId, createdAt, updatedAt,
                                                     messages|null, state }
DELETE {prefix}/api/sessions/{id}              -> 204 | 404

GET    {prefix}/api/runs?agentName=&status=&sessionId=&startedAfter=&skip=&take=  -> RunRecord[]
GET    {prefix}/api/runs/{id}                  -> RunRecord | 404
GET    {prefix}/api/runs/{id}/events           -> SSE  (canli veya replay)

GET    {prefix}/api/tools                      -> ToolDescriptor[]
GET    {prefix}/api/models                     -> ModelProviderDescriptor[]
GET    {prefix}/api/stats?agentName=&startedAfter=&maxAgents=  -> RunStatistics

POST   {prefix}/v1/responses                   OpenAI Responses API uyumlu
POST   {prefix}/v1/chat/completions            OpenAI Chat Completions API uyumlu
POST   {prefix}/v1/conversations               -> { id: "conv_...", object, created_at, metadata }
GET    {prefix}/v1/conversations/{id}          DELETE · /items
```

### SSE olay adları — **kararlı sözleşme**

`{prefix}/api/runs/{id}/events`:

```
run.started · message.delta · message.completed
tool.invoking · tool.invoked · tool.failed
run.completed · run.failed
```

Her çerçeve `id:` alanı taşır (olayın sıra numarası). Bağlantı koparsa istemci
`Last-Event-ID: <son alinan id>` başlığıyla devam eder; sunucu **bir sonraki**
sıradan yayına başlar.

`{prefix}/api/agents/{name}/run`:

```
update  -> AgentResponseUpdate  (Microsoft.Extensions.AI serilestirmesi)
done    -> { sessionId }
error   -> { type, message }
```

> Bu uçta `Last-Event-ID` ile devam **desteklenmez**: canlı bir model çağrısı
> yeniden oynatılamaz. Devam yalnızca `/api/runs/{id}/events` üzerindedir.

### Davranış sözleşmeleri (mevcut testlerin zorladığı kurallar)

| Kural | Nerede doğrulanıyor |
|-------|--------------------|
| `/api/meta` token, loopback ve başarısız policy altında bile açıktır | `SecurityTests` |
| `/api/meta` sır **ve policy adı** döndürmez | `MetaEndpointTests` |
| Kodda tanımlı agent'ta POST/PUT/DELETE `409` döner | `AgentCrudTests` |
| `isEditable=false` ise yazma uçları çalışmaz | `AgentCrudTests` |
| Geri alma eski sürümü silmez, yeni sürüm üretir | `AgentCrudTests` |
| Enum'lar JSON'da **ad** olarak gelir (`"Completed"`, `"Code"`) | `AgentCrudTests` |
| `/api/*` hataları `application/problem+json` | `ProblemDetailsTests` |
| `/v1/*` hataları OpenAI biçimi (`{"error":{...}}`) | `ProblemDetailsTests` |
| SSE `X-Accel-Buffering: no` taşır | `StreamingTests` |
| `Last-Event-ID` ile tekrar gönderim olmaz | `StreamingTests` |
| Bearer token hiçbir yanıtta ve günlükte görünmez | `SecretLeakTests` |

---

## 🚨 Bilinen Tuzaklar

**1. Model kataloğu boş olabilir.** AgentPrism yerleşik model listesi taşımaz (K-032).
`/api/models` boş liste dönebilir; bu bir hata **değildir**. Arayüz kullanıcıyı
`AgentPrism:Providers:OpenAI:Models` ayarına yönlendirmelidir.

**2. `/api/stats` maliyet döndürmez.** `runs` tablosu model adı taşımaz; maliyet Faz 6'da
gelir. Maliyet sütunu göstermeyin veya "Faz 6" olarak işaretleyin.

**3. Sohbet geçmişi `null` olabilir.** `/api/sessions/{id}` içindeki `messages`, agent
katalogdan kalkmışsa veya MAF serileştirme biçimi değişmişse `null` gelir. Üstveri yine
döner — arayüz bu durumu ele almalıdır.

**4. `messages` biçimi MAF'ın `ChatMessage` dizisidir**, AgentPrism'e özel bir DTO değil
(kural K3). İçerikler polimorfiktir ve `$type` ayracı taşır; `TextContent`,
`FunctionCallContent`, `FunctionResultContent`, `UsageContent` ayrımı bu ayraçtan yapılır.

**5. Konuşma ile oturum aynı şeydir** (K-043). `/v1/conversations` uçları vardır ancak
konuşma listesi arayüzde `/api/sessions` üzerinden kurulur — aynı kimlik uzayıdır,
`/api/sessions/{id}` daha zengin bilgi döner (opak durum + üstveri).

**6. Canlı olay akışı yoklamayla çalışır.** Varsayılan aralık 250 ms
(`AgentPrismEndpointOptions.RunEventPollInterval`). Devam eden bir çalıştırmada akış
`: bekleniyor` yorum satırları gönderir — SSE istemcisi bunları yok saymalıdır.

**7. Prefix sabit değildir.** `MapAgentPrism` herhangi bir prefix'e bağlanabilir; arayüz
onu `/api/meta` yanıtındaki `prefix` alanından öğrenir. Mutlak varlık yolu kullanmayın.

**8. `MapAgentPrism`'in döndürdüğü builder yalnız korumalı grubu temsil eder** (K-042).
Arayüz middleware'ini eklerken meta ucunun açık kaldığını varsayabilirsiniz.

---

## Amaç

Hafif ama eksiksiz bir yönetim arayüzü. Gömülü, sıfır kurulum. Tüketici projede hiçbir JavaScript bağımlılığı oluşmaz.

Bu faz sonunda kabul senaryosu tamamlanır: paket kurulur, iki satır kod yazılır, tarayıcıda çalışan bir kontrol düzlemi açılır.

---

## Teknoloji Seçimi

| Katman | Seçim | Gerekçe |
|--------|-------|---------|
| Çatı | React 19 + TypeScript | Streaming, SSE ve karmaşık durum yönetiminde olgun ekosistem |
| Build | Vite 7 | Hızlı, çıktı küçük, statik varlık üretimi basit |
| Veri | TanStack Query | Sunucu durumu, önbellek, yeniden deneme |
| Yönlendirme | TanStack Router | Tip güvenli, base path desteği |
| Stil | Tailwind CSS | Küçük çıktı, tema değişkenleri kolay |
| Bileşen | Elle yazılır | Ağır UI kütüphanesi çıktıyı şişirir |

**Bütçe:** gzip sonrası **250 KB altı** JavaScript. Bu bir hedef değil, kapıdır — build bu sınırı aşarsa CI kırılır.

Blazor WebAssembly değerlendirildi ve elendi: ilk yükleme boyutu (~2 MB+) "lite arayüz" hedefiyle çelişiyor. Blazor Server elendi: kalıcı SignalR bağlantısı gerektirir ve bir kütüphane olarak tüketicinin barındırma modelini kısıtlar.

---

## Ekranlar

| Ekran | İşlev |
|-------|-------|
| **Agents** | Katalog listesi (kod/DB rozeti), tanım editörü, versiyon geçmişi, geri alma |
| **Playground** | Akışlı sohbet; tool çağrıları, argümanlar, sonuçlar ve reasoning adımları açılır kartlar hâlinde |
| **Sessions** | Oturum listesi, mesaj geçmişi, silme, ham JSON görünümü |
| **Runs** | Çalıştırma listesi + zaman çizelgesi; olay akışı adım adım |
| **Tools** | Kayıtlı tool'lar, JSON şemaları, kullanım istatistikleri |
| **Models** | Sağlayıcılar, modeller, bağlantı sağlık kontrolü |
| **Settings** | Kiracı, migration durumu, sürüm, yetenek matrisi |

### Agent editörü

Tool seçimi **çoktan seçmeli listedir**, serbest metin değildir. Liste `{prefix}/api/tools` uçundan gelir. Arayüzden tool kodu yazılamaz — tasarım kuralı K2.

### Playground

Akış `{prefix}/api/agents/{name}/run` uçundan SSE ile gelir. Olay tipleri doğrudan `RunEventType` ile eşleşir:

```
run.started      → çalıştırma başlığı
message.delta    → metin akışı
tool.invoking    → tool kartı açılır (argümanlar görünür)
tool.invoked     → tool kartı sonuçla dolar
run.completed    → token ve süre özeti
run.failed       → hata detayı
```

---

## Paketleme

```
src/AgentPrism.UI/
├── frontend/                    (Vite kaynağı, git'te tutulur)
│   ├── src/
│   ├── index.html
│   ├── package.json
│   ├── package-lock.json
│   ├── tsconfig.json
│   └── vite.config.ts
├── wwwroot/                     (Vite çıktısı, .gitignore'da)
├── AgentPrism.UI.Frontend.targets
├── AgentPrismUiMiddleware.cs
└── AgentPrism.UI.csproj
```

### Build zinciri

`AgentPrism.UI.Frontend.targets`:

1. `npm ci` (yalnız `package-lock.json` değiştiğinde — dosya damgası ile)
2. `npm run build` → `wwwroot/`
3. Çıktı `EmbeddedResource` olarak assembly'ye gömülür

`AgentPrismFrontendEnabled` özelliği (`AgentPrism.UI.csproj` içinde zaten tanımlı) bu adımı kontrol eder. Faz 5'te `true` yapılır.

**Node.js bulunmazsa:** önceden derlenmiş `wwwroot/` varsa build devam eder. `dotnet pack` sırasında varlık yoksa **hata verir** — içi boş bir arayüz paketi yayınlanamaz.

### Base path

`MapAgentPrism` herhangi bir prefix'e bağlanabilir. Bu yüzden Vite `base: './'` ile derlenir ve `index.html` sunulurken `<base href="...">` etiketi çalışma anında yazılır. Mutlak varlık yolu kullanılmaz.

### Sunum

`AgentPrismUiMiddleware`:

- Gömülü kaynakları okur, `ETag` ve `Cache-Control: immutable` ile sunar (hash'li dosya adları)
- `index.html` için `no-cache`
- Bilinmeyen yollarda SPA fallback → `index.html`
- `{prefix}/api/*` ve `{prefix}/v1/*` middleware tarafından **ele alınmaz**, endpoint'lere geçer

---

## Tema

Açık ve koyu tema. Varsayılan `prefers-color-scheme`. Kullanıcı seçimi `localStorage`'da tutulur. Tüm renkler CSS değişkeni üzerinden — tema değişimi tek sınıf değişikliğidir.

---

## Test Stratejisi

> **Bu faz `tests/AgentPrism.Ui.E2ETests` projesini oluşturur** (`Microsoft.Playwright` ile). Faz 0 yalnız `AgentPrism.Core.UnitTests`'i kurdu; test projeleri test edecekleri şeyle birlikte gelir.

`tests/AgentPrism.Ui.E2ETests` — Playwright.

| Test | Neyi doğrular |
|------|---------------|
| `UiLoadsTest` | `/agentprism` açılır, ana ekran render olur |
| `AgentListTest` | Kod ile tanımlı agent listede görünür |
| `PlaygroundStreamingTest` | Mesaj gönderilir, akış gelir, tool kartı açılır |
| `AgentCreateTest` | Arayüzden agent oluşturulur ve hemen çalıştırılır |
| `PrefixTest` | Farklı prefix'e (`/panel`) bağlandığında varlıklar yüklenir |
| `ThemeTest` | Koyu tema geçişi çalışır |

Frontend birim testleri: Vitest, yalnız saf mantık (biçimlendirme, olay birleştirme) için.

### Bundle boyutu kapısı

CI'da `npm run build` sonrası gzip boyutu ölçülür. 250 KB aşılırsa build kırılır.

---

## Bitiş Ölçütleri (DoD)

- [ ] `dotnet run` → `http://localhost:5080/agentprism` açılır
- [ ] Yedi ekranın hepsi çalışır
- [ ] Playground akışı token token gelir
- [ ] Tool çağrısı kartı argüman ve sonuç gösterir
- [ ] Arayüzden agent oluşturulur, kaydedilir, çalıştırılır
- [ ] Uygulama yeniden başlatılır, her şey yerinde durur
- [ ] Farklı prefix ile çalışır
- [ ] gzip JS < 250 KB
- [ ] Playwright testleri geçer

### Uçtan uca kabul senaryosu

```bash
cd samples/AgentPrism.Api
dotnet user-secrets set "AgentPrism:PostgreSql:ConnectionString" "<host>"
dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "<key>"
dotnet run
```

1. `http://localhost:5080/agentprism` açılır
2. Agents ekranında kod ile tanımlı `support` agent'ı görünür
3. Playground'da mesaj gönderilir, yanıt **akışlı** gelir, tool kartı görünür
4. Arayüzden yeni agent tanımlanır, kaydedilir, hemen çalıştırılır
5. Uygulama durdurulur ve yeniden başlatılır — oturum, tanım ve geçmiş yerinde
6. Runs ekranında çalıştırma olay olay incelenir
7. `psql` ile `agentprism` şeması denetlenir; `public` şemasının değişmediği doğrulanır

---

## Riskler

| Risk | Önlem |
|------|-------|
| CI'da Node.js gerekliliği build zincirini karmaşıklaştırır | CI'da Node adımı Faz 0'da eklendi; `pack` varlık yoksa anlaşılır hata verir |
| Gömülü varlıklar assembly boyutunu büyütür | Bundle bütçesi CI kapısı; varlıklar Brotli sıkıştırılmış gömülür |
| Ters vekil arkasında SSE arabelleği | Faz 4'teki `X-Accel-Buffering: no`; dokümanda vekil ayarı |
| `npm ci` ağ hatası build'i kırar | `package-lock.json` sabit; CI'da npm önbelleği |
