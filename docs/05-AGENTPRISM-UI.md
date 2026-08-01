# Faz 5 — AgentPrismUI

> **Durum:** Planlandı
> **Önkoşul:** [04-HTTP-API.md](04-HTTP-API.md)
> **Sonraki:** [06-GOZLEMLENEBILIRLIK.md](06-GOZLEMLENEBILIRLIK.md)
> **Paket:** `AgentPrism.UI`

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
dotnet user-secrets set "AgentPrism:ConnectionString" "<host>"
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
