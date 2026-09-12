# 05 — Sağlayıcı: OpenAI (`OAI`)

> **Alan kodu:** `OAI` · **Faz:** 3, 8
> **Kaynak:** `src/Tracon.OpenAI` (tümü — `OpenAIProviderOptions*`,
> `OpenAIProviderExtensions`, `OpenAICompatibleProviderExtensions`,
> `OpenAIChatClientFactory`, `OpenAIModelProvider`, `OpenAIModelCatalog`,
> `OpenAIProviderHealthCheck`, `OpenAINamedChatClientFactoryCache`). Devre kesici
> ve sağlık önbelleği `Tracon.OpenAI`'de YAŞAMAZ — paylaşılan bir dekoratör
> katmanıdır: `src/Tracon.Core/Models/ModelProviderCircuitBreaker.cs` ·
> `CircuitBreakingChatClient.cs` · `ModelProviderHealthCache.cs` ·
> `ModelProviderRegistry.cs`. [`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md)'ün
> sınır tablosu bu makinenin ("devre kesici, sağlık, akış") kanıtını bu dosyaya
> bırakır — burada gerçek bir OpenAI hesabı üzerinden kanıtlanır; mekanizma
> `IModelProvider` uygulayan HER paket için bedavadır (gelecekte Anthropic,
> Google, Azure).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/05-SAGLAYICI-OPENAI.md`](kosumlar/2026-08-13/05-SAGLAYICI-OPENAI.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

`UseOpenAI()` ve `UseOpenAICompatible()` çağrıldığında kurulan zincir: ayar
doğrulama → model kataloğu → ham `IChatClient` → ortak boru hattı (tool döngüsü,
telemetri, ek çözme, devre kesici, içerik filtresi tespiti). Gerçek bir OpenAI
hesabı olmadan bu dosyanın büyük çoğunluğu **anlamsızdır** — model çağırmayan
kısımlar (kayıt, doğrulama, katalog) `echo` sağlayıcısıyla da doğrulanabilir ama
E2E, akış, sağlık ve devre kesici bölümleri gerçek ağ çağrısı ister.

```mermaid
flowchart TD
    A["UseOpenAI(apiKey) / UseOpenAICompatible(ad, ...)"] --> B["OpenAIProviderOptionsValidator"]
    B --> C["OpenAIModelCatalog.Build(options)"]
    C --> D["OpenAIModelProvider x1 veya x2 (ChatCompletions + Responses)"]
    D --> E["ModelProviderRegistry.CreateChatClient"]
    E --> F["ContentGuard (varsa)"]
    F --> G["UseFunctionInvocation + UseOpenTelemetry"]
    G --> H["AttachmentResolving (varsa)"]
    H --> I["ModelProviderCircuitBreaker.Wrap"]
    I --> J["ContentFilterDetecting"]
    J --> K["Gercek OpenAI HTTP cagrisi"]
    D -.-> L["OpenAIProviderHealthCheck: GET {endpoint}/models"]
    L --> M["ModelProviderHealthCache (TTL 60sn)"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Anthropic, Google, Azure OpenAI sağlayıcıları (aynı devre kesici/sağlık makinesini paylaşırlar) | [`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md) |
| Agent tanımı derleme/katalog genel davranışı, `unknown_model`/`unknown_tool` doğrulama, `ReasoningEffort` reddi | [`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md) (zaten üretildi — burada tekrarlanmaz) |
| HTTP durum kodu sözleşmesi, sayfalama, genel `/api/agents/*` gövde şekli | [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) |
| `/v1/chat/completions`, `/v1/responses` (OpenAI uyumlu dış yüzey) | [`08-OPENAI-UYUMLU-UCLAR.md`](08-OPENAI-UYUMLU-UCLAR.md) |
| `/api/diagnostics` ucunun genel sözleşmesi, `TraconHealthCheck`, tüm sağlayıcıların OpenAPI görünürlüğü | [`25-SAGLIK-TESHIS-OPENAPI.md`](25-SAGLIK-TESHIS-OPENAPI.md) |
| Maliyet hesabı (`InputCostPerMillionTokens` kullanımı), `/api/stats` | [`12-GOZLEMLENEBILIRLIK-MALIYET.md`](12-GOZLEMLENEBILIRLIK-MALIYET.md) |
| Kiracı/rol/API anahtarı HTTP güvenlik sınırları | [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md) |
| AOT publish smoke testi, paket bağımlılık grafiği | [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. PostgreSQL container'ı (`ap-pg`) çalışır ve `Tracon:PostgreSql:ConnectionString`
   tanımlıdır. Bu dosya kalıcılık davranışını sınamaz ama §4'teki "Responses API +
   kalıcılık çatışması yok" kanıtı PostgreSQL'in **açık** olmasını gerektirir.
3. **Gerçek bir OpenAI API anahtarı** tanımlıdır:
   `dotnet user-secrets set "Tracon:Providers:OpenAI:ApiKey" "<ANAHTARINIZ>"`.
   Anahtar yoksa `Program.cs`'deki `openAiEnabled` bayrağı `false` kalır,
   `UseOpenAI` hiç çağrılmaz ve sağlayıcı adı `echo` olur — bu dosyadaki
   case'lerin **hiçbiri** o durumda geçerli değildir (MT-OAI-001 hariç, o da
   yalnız kayıt sayısını sayar).
4. **Gerçek bir OpenRouter API anahtarı** tanımlıdır (§6 için):
   `dotnet user-secrets set "Tracon:Providers:OpenAICompatible:openrouter:ApiKey" "<OPENROUTER_ANAHTARINIZ>"`.
5. `Tracon:Ui:AuthToken` `manuel-test-token-2026`'dır.
6. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080`

Kısaltmalar — bu dosyadaki her `curl` şu başlıkları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
```

> **Gerçek para uyarısı.** Bu dosyadaki E2E case'leri (§5, §6.5, §8) gerçek
> OpenAI/OpenRouter çağrısı yapar ve ölçülebilir (küçük) bir ücrete yol açar.
> Sağlık denetimi (§7) ve kayıt/doğrulama (§1–§3) hiçbir model çağırmaz —
> yalnız `GET {endpoint}/models` veya hiçbir ağ çağrısı yapmaz.

---

# 1 — Kayıt ve temel akış

`UseOpenAI()`'nin kayıt zamanı davranışı: iki sağlayıcı, tek istemci, tekrarlı
çağrının çoğaltmaması.

### MT-OAI-001 — `UseOpenAI()` tek çağrıyla iki sağlayıcı kaydeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama gerçek OpenAI anahtarıyla çalışıyor.

**Adımlar**
1. Kayıtlı model sağlayıcılarını listele.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Dizi **en az** iki öge taşır: `name` alanı `openai` olan ve `name` alanı
  `openai-responses` olan.
- İkisinin `models` listesi **aynıdır** (appsettings.json'daki üç model:
  `gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra`) — tek `OpenAIProviderOptions`
  örneği ikisini de besler.

---

### MT-OAI-002 — Bilinmeyen sağlayıcı adıyla çalıştırma anlaşılır hata verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

Negatif senaryo. Doğrulama ucu değil, gerçek çalıştırma yolu sınanır
(`ModelProviderRegistry.CreateChatClient`) — `unknown_model` doğrulama hatası
zaten [`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md)'de kanıtlandı; bu
case aynı hatanın **çalıştırma anında** da aynı mesajla çıktığını doğrular.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `provider` alanı kayıtlı olmayan bir agent tanımı kaydet (doğrulama atlanır,
   `POST /api/agents` doğrudan yazar).
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yok-boyle-saglayici",
  "instructions": "Test.",
  "model": { "provider": "yok-boyle-bir-saglayici", "model": "x" }
}'

curl -s -X POST "$APU/api/agents/manuel-yok-boyle-saglayici/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"merhaba"}'
```

**Beklenen sonuç (düzeltildi — koşum kanıtı, doc kusuru)**
- Kayıt (`POST /api/agents`) başarılıdır — sağlayıcı varlığı kayıt anında
  denetlenmez, yalnız çalıştırma anında.
- Bilinmeyen sağlayıcı **derleme (compile) zamanında** yakalanır, bu yüzden
  akış hiç başlamaz: `POST .../run` senkron `HTTP 400`,
  `Content-Type: application/problem+json` döner (SSE `error` çerçevesi değil
  — MT-CORE-024'teki "derleme hatası" ile aynı mekanizma).
- `detail` alanı `'yok-boyle-bir-saglayici' adinda bir model saglayicisi
  kayitli degil. Kayitli saglayicilar: ` dizgisini içerir, ardından virgülle
  ayrılmış gerçek liste gelir (`openai, openai-responses, openrouter,
  anthropic, google`).
- `type` alanı standart ProblemDetails RFC 9110 bağlantısıdır
  (`.../section-15.5.1`), `TraconException` DEĞİLDİR — orijinal beklenti
  SSE akışı varsayıyordu, kodda böyle değil.

---

### MT-OAI-003 — Model adı boş ve `DefaultModel` tanımsızsa anlaşılır hata verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `model.model` alanı boş bir agent tanımı kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-model-adi-yok",
  "instructions": "Test.",
  "model": { "provider": "openai", "model": "" }
}'

curl -s -X POST "$APU/api/agents/manuel-model-adi-yok/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"merhaba"}'
```

**Beklenen sonuç (düzeltildi — koşum kanıtı, doc kusuru)**
- `POST /api/agents` kaydı kendisi `HTTP 400` ile reddedilir:
  `detail: "'model.provider' ve 'model.model' alanlari zorunludur."` — agent
  hiç kaydedilmez, `/run` adımına ulaşılmaz.
- `OpenAIChatClientFactory`'nin çalıştırma-anı mesajı (`Model adi bos ve
  varsayilan model tanimli degil.`) bu API yolundan **erişilemez** — kayıt
  katmanındaki doğrulama ondan önce devreye girer. Ürün kusuru değil: boş
  model adını daha erken, daha güvenli bir noktada reddetmek beklenenden
  daha sıkı bir davranış.

---

### MT-OAI-004 — İkinci `UseOpenAI()` çağrısı sağlayıcıları çoğaltmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

Negatif/sınır senaryosu. **Geçici kod değişikliği** — adım 4'te geri alınır.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `samples/Tracon.Api/Program.cs`'de `tracon.UseOpenAI(openAi);`
   satırından (yaklaşık 135. satır) hemen SONRA şu satırı geçici olarak ekle:
   `tracon.UseOpenAI(openAi);` (aynı çağrı, ikinci kez).
2. Uygulamayı başlat.
3. `/api/models`'i çağır.
4. Değişikliği geri al: `git checkout -- samples/Tracon.Api/Program.cs`

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print([x['name'] for x in d])"
```

**Beklenen sonuç**
- Uygulama başlar, **çökmez**.
- Liste `openai` ve `openai-responses`'ı **yalnızca birer kez** içerir —
  `ModelProviderRegistry`'nin "aynı ad birden çok kez kaydedilmiş" hatası
  fırlatılmaz, çünkü `OpenAIProviderExtensions.UseOpenAI` ikinci çağrıda
  `alreadyRegistered` bayrağıyla sağlayıcı ekleme adımını atlar.

### MT-OAI-010 — Göreli (relative) `Endpoint` reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3, 8 |
| **İlgili karar** | K-006 |

Negatif senaryo.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `Endpoint`'i göreli bir değere ayarla.
2. Uygulamayı başlat.
3. Temizle.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:Endpoint" "sadece-bir-yol"
cd samples/Tracon.Api && dotnet run
```
```bash
# Temizlik:
dotnet user-secrets remove "Tracon:Providers:OpenAI:Endpoint"
```

**Beklenen sonuç**
- Uygulama başlamayı **reddeder**; konsolda `OptionsValidationException` görünür.
- Mesaj `OpenAIProviderOptions.Endpoint mutlak bir adres olmalidir. Gelen
  deger: 'sadece-bir-yol'.` metnini taşır.

---

### MT-OAI-011 — Sıfır veya negatif `Timeout` reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-006 |

Negatif senaryo.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `Timeout`'u `00:00:00` yap.
2. Uygulamayı başlat.
3. Temizle.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:Providers:OpenAI:Timeout" "00:00:00"
cd samples/Tracon.Api && dotnet run
```
```bash
# Temizlik:
dotnet user-secrets remove "Tracon:Providers:OpenAI:Timeout"
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Mesaj `OpenAIProviderOptions.Timeout sifirdan buyuk olmalidir. Gelen deger:
  00:00:00.` metnini taşır.

---

### MT-OAI-012 — Katalogda adı boş bir model reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-032 |

Negatif senaryo. `appsettings.json`'daki `Models` dizisine adsız bir girdi
eklemek gerektiği için **geçici dosya değişikliği** ister.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `samples/Tracon.Api/appsettings.json`'da `Tracon:Providers:OpenAI:Models`
   dizisine `{"Name": ""}` ekle (dizinin sonuna, virgülle ayırarak).
2. Uygulamayı başlat.
3. Değişikliği geri al: `git checkout -- samples/Tracon.Api/appsettings.json`

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Mesaj `OpenAIProviderOptions.Models[3] icin model adi bos olamaz.` metnini
  taşır (dizin `3` — mevcut üç modelden sonraki dördüncü öge, sıfır tabanlı).

---

### MT-OAI-013 — `ApiKey` boşken `UseOpenAI()` çağrılırsa doğrulama hata verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-006 |

Negatif senaryo. **`samples/Tracon.Api` bu case'i tetikleyemez**: `Program.cs`
`openAiEnabled` bayrağı `false` ise `UseOpenAI` hiç çağrılmaz (bkz. Program.cs,
satır ~130), dolayısıyla `ApiKey` boşken doğrulayıcı asla devreye girmez. Bu,
gerçek bir MAF/Tracon davranışıdır, kod değiştirilmez — yerine [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md)
`MT-PKG-070`'in kurduğu yerel NuGet feed'i ile bağımsız bir konsol uygulaması
kurulur.

**Ön koşul**
- `MT-PKG-070` geçti (`~/tracon-local-feed` dolu).

**Adımlar**
1. Boş bir konsol projesi oluştur, `Tracon.Core` ve `Tracon.OpenAI`
   paketlerini yerel feed'den ekle.
2. `UseOpenAI(o => { })` çağıran (ApiKey vermeyen) bir gövde yaz.
3. Çalıştır.

**Girilecek veri**
```bash
rm -rf /tmp/ap-oai-apikey-test && mkdir -p /tmp/ap-oai-apikey-test
cd /tmp/ap-oai-apikey-test
dotnet new console -o . --force
dotnet nuget add source ~/tracon-local-feed -n tracon-local 2>/dev/null || true
SURUM=$(ls ~/tracon-local-feed/Tracon.OpenAI.0.*.nupkg | sed 's#.*/Tracon\.OpenAI\.##;s#\.nupkg##')
dotnet add package Tracon.Core --version "$SURUM" --source ~/tracon-local-feed
dotnet add package Tracon.OpenAI --version "$SURUM" --source ~/tracon-local-feed

cat > Program.cs <<'EOF'
using Tracon;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddTracon().UseOpenAI(o => { });
using var app = builder.Build();
await app.StartAsync();
EOF

dotnet run
```

**Beklenen sonuç**
- Süreç `OptionsValidationException` ile sonlanır.
- Mesaj `OpenAIProviderOptions.ApiKey bos olamaz. Anahtari` ile başlar ve
  `dotnet user-secrets` ibaresini içerir.

### MT-OAI-020 — Katalog yalnız yapılandırmadan gelir ve `/api/models`'te görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-032 |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Katalogu oku.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `openai` sağlayıcısının `models` dizisi **tam olarak** üç öge taşır, adları
  `gpt-5.4-mini`, `gpt-5.6-luna`, `gpt-5.6-terra` ve **ada göre alfabetik
  sıralı**.
- Her modelin `supportsStructuredOutput` alanı `true`'dur
  (`appsettings.json`'daki ayarla eşleşir).
- Hiçbir modelin `contextWindowTokens`/fiyat alanı doldurulmamıştır (koşulmadı
  — bu dosya fiyat alanlarını **atlar**, bkz. [`12-GOZLEMLENEBILIRLIK-MALIYET.md`](12-GOZLEMLENEBILIRLIK-MALIYET.md)).

---

### MT-OAI-021 — Katalogda olmayan bir model adı reddedilmez, yalnız günlüğe yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-032 |

Sınır senaryosu: katalog bir doğrulama listesi **değildir**.

**Ön koşul**
- Örnek uygulama konsolu görünür durumda çalışıyor (arka planda değil).

**Adımlar**
1. Katalogda **olmayan** ama gerçek bir OpenAI modeli olan bir agent tanımı
   kaydet (örnek: `gpt-4o-mini`, appsettings'teki üç modelin dışında).
2. Çalıştır.
3. Konsol çıktısını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-katalog-disi-model",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-4o-mini" }
}'

curl -s -X POST "$APU/api/agents/manuel-katalog-disi-model/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"Merhaba, kim oldugunu soylemene gerek yok, sadece \"tamam\" yaz."}'
```

**Beklenen sonuç**
- Çalıştırma **başarıyla tamamlanır** — gerçek bir OpenAI yanıtı üretir; katalog
  dışı olmak isteği reddettirmez.
- Uygulama konsolunda (Information seviyesinde) `'gpt-4o-mini' modeli 'openai'
  katalogunda yok; istek yine de gonderiliyor.` günlük satırı görünür.

---

### MT-OAI-022 — Aynı ad iki kez tanımlanırsa son tanım kazanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-032 |

Sınır senaryosu. **Geçici dosya değişikliği**.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `samples/Tracon.Api/appsettings.json`'da `Models` dizisine, mevcut
   `gpt-5.4-mini` girdisinden SONRA, aynı adla ama farklı `DisplayName` taşıyan
   ikinci bir girdi ekle: `{"Name": "gpt-5.4-mini", "DisplayName": "IKINCI TANIM"}`.
2. Uygulamayı başlat, `/api/models`'i oku.
3. Değişikliği geri al.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
d=json.load(sys.stdin)
oa=[p for p in d if p['name']=='openai'][0]
print(len(oa['models']), [m['displayName'] for m in oa['models'] if m['name']=='gpt-5.4-mini'])
"
```
```bash
# Temizlik:
git checkout -- samples/Tracon.Api/appsettings.json
```

**Beklenen sonuç**
- `gpt-5.4-mini` **tam olarak bir kez** görünür (dört değil, üç toplam model).
- Görünen kaydın `displayName`'i `IKINCI TANIM`'dır — `OpenAIModelCatalog.Build`
  bir `Dictionary` üzerinde son yazanın kazandığı bir birleştirme yapar.

### MT-OAI-030 — `openai-responses` yüzeyi PostgreSQL kalıcılığıyla çatışmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-030 |

Bu case, Faz 3'ün S2 sapmasının hâlâ geçerli olduğunu doğrular: sapma
kaldırılırsa (`AsIChatClientWithStoredOutputDisabled` yerine düz
`AsIChatClient(defaultModelId)` kullanılırsa) bu case **kaldı** vermelidir.

**Ön koşul**
- PostgreSQL container'ı çalışıyor, `Tracon:PostgreSql:ConnectionString`
  tanımlı, `samples/Tracon.Api/Program.cs`'de `UsePostgreSql` açık.

**Adımlar**
1. `openai-responses` yüzeyini kullanan bir agent tanımı kaydet.
2. Bir oturum kimliğiyle iki ardışık mesaj gönder (aynı oturumda konuşma
   geçmişinin çalıştığını da kanıtlar).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-responses-yuzeyi",
  "instructions": "Kisa yanit ver. Kullanicinin ilk mesajindaki sayiyi hatirla.",
  "model": { "provider": "openai-responses", "model": "gpt-5.4-mini" }
}'

curl -s -X POST "$APU/api/agents/manuel-responses-yuzeyi/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Benim sansli sayim 47. Sadece \"tamam\" yaz.","sessionId":"resp-yuzeyi-test"}'

curl -s -X POST "$APU/api/agents/manuel-responses-yuzeyi/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Sansli sayim neydi?","sessionId":"resp-yuzeyi-test"}'
```

**Beklenen sonuç**
- İki çalıştırma da SSE `error` çerçevesi üretmeden `done` ile tamamlanır —
  özellikle `InvalidOperationException: Only ConversationId or
  ChatHistoryProvider may be used, but not both.` hatası **görülmez**.
- İkinci yanıt `47` dizgisini içerir — geçmiş Tracon'in PostgreSQL
  kalıcılığında (OpenAI'ın kendi konuşma kimliğinde değil) tutulmuştur.

---

### MT-OAI-031 — İki yüzey de `/api/models`'te bağımsız görünür ve aynı katalogu taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Katalogu oku, iki yüzeyi karşılaştır.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
d=json.load(sys.stdin)
byname={p['name']: p for p in d}
print(byname['openai']['models'] == byname['openai-responses']['models'])
"
```

**Beklenen sonuç**
- Çıktı `True`'dur — `UseOpenAI` her iki sağlayıcıyı da **aynı**
  `OpenAIModelCatalog.Build(options)` çağrısının sonucuyla kurar.

### MT-OAI-040 — Tool çağrısı ile uçtan uca çalıştırma

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama gerçek OpenAI anahtarıyla çalışıyor.

**Adımlar**
1. `support` agent'ını (fixture `FIX-PROMPT-01`) çalıştır.
2. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"oai-e2e-01"}'
```
```bash
# runId SSE 'run' cercevesinden okunur, sonra:
curl -s "$APU/api/runs/<runId>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Yanıt metni `ORD-1001` dizgisini içerir.
- `GET /api/runs/{runId}` çıktısında `status` = `Completed`, `totalTokens`
  `null` değil ve pozitiftir.
- Aynı run'ın olay listesinde (`GET /api/runs/{runId}/events` veya arayüz)
  `get_order_status` tool'unun **tam bir kez** çağrıldığı görülür.

---

### MT-OAI-041 — Akış (SSE) üç çerçeve üretir: `run`, `update`(ler), `done`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 3, 4 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Akışı ham hâliyle gözlemle (`-N`, arabellek kapalı).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"oai-sse-01"}'
```

**Beklenen sonuç**
- Yanıt başlığı `content-type: text/event-stream`'dir.
- İlk çerçeve `event: run` ve `data: {"runId":"...","sessionId":"oai-sse-01"}`
  şeklindedir (alan adları camelCase).
- Ardından bir veya daha fazla `event: update` çerçevesi gelir.
- Son çerçeve `event: done` ve `data: {"sessionId":"oai-sse-01"}`'dir.
- Hiçbir çerçevede `event: error` görünmez.

---

### MT-OAI-042 — `Idempotency-Key` başlığı akışsız (tek JSON) yanıt üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 3, 43 |
| **İlgili karar** | K-288 |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Idempotency-Key` başlığıyla çalıştır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\nCT: %{content_type}\n" \
     -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: manuel-oai-042-$(date +%s)" \
     -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"oai-idem-01"}'
```

**Beklenen sonuç**
- `HTTP: 200`, `CT: application/json` (SSE **değil**).
- Gövde tek bir JSON nesnesidir, `text` alanı doludur.

---

### MT-OAI-043 — Gerçek OpenAI 404 (`model_not_found`): akışlı yanıt `error` çerçevesi üretir (düzeltildi)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3, 45 |
| **İlgili karar** | K-296 |

Negatif senaryo. **Düzeltilmiş kusur (2026-08-10).** `K-296` bu tam durumu
ölçmüştü (`DefaultRunErrorClassifier`'ın `ClientResultException`'ı eksik
olduğu ve düzeltildiği kayıt) — ama o düzeltme yalnız kaydedilen
`RunError.Class` alanına yapılmıştı.
`AgentEndpoints.AgentRunStream.ExecuteStreamingAsync`'in SSE `error` çerçevesi
üreten `catch` bloğu OpenAI SDK'sının gerçek istisna tipini
(`System.ClientModel.ClientResultException`) **listelemiyordu** — bu case
tam olarak bu boşluğu kayda geçirmek için yazılmıştı. Kod okumasıyla
doğrulandı ve düzeltildi: dar `when` filtresi kaldırıldı, `catch (Exception ex)`
`OperationCanceledException`'dan SONRA HER istisnayı yakalar. Bu case artık
düzeltilmiş davranışı doğrular.

**Ön koşul**
- Örnek uygulama gerçek OpenAI anahtarıyla çalışıyor.

**Adımlar**
1. Var olmayan bir model adıyla bir agent tanımı kaydet (K-296'nın ölçtüğü
   aynı dizgi).
2. Akışlı çalıştır, bağlantının nasıl bittiğini gözlemle.
3. Çalıştırma kaydını `GET /api/runs/{runId}` ile oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-bozuk-model",
  "instructions": "Test.",
  "model": { "provider": "openai", "model": "gpt-olmayan-model-xyz" }
}'

curl -N -s -v -X POST "$APU/api/agents/manuel-bozuk-model/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"merhaba","sessionId":"oai-404-test"}' 2>&1 | tail -40
```
```bash
# runId 'run' cercevesinden (varsa) veya /api/runs listesinden okunur:
curl -s "$APU/api/runs?agentName=manuel-bozuk-model" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- İlk `curl`: `run` çerçevesi gelir (çalıştırma başlamıştır), ardından
  **`event: error`** çerçevesi gelir — bağlantı çerçevesiz aniden KAPANMAZ.
- İkinci `curl` (`GET /api/runs`): `manuel-bozuk-model` için bir kayıt
  `status: Failed` ile görünür — `RunRecordingAgent.CompleteAsync` akış
  istisnasını `AgentRunStream`'in yakalamasından ÖNCE, kendi `try/catch`'inde
  yakalayıp kaydeder ve sonra yeniden fırlatır (K-294).
- Bu kaydın `error.type` alanı `System.ClientModel.ClientResultException`
  (veya OpenAI SDK'sının o anki tam tip adı) içerir, `error.message` alanı
  `model_not_found` ya da `404` dizgisini içerir.

### MT-OAI-050 — `openrouter` adlandırılmış sağlayıcı olarak görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | K-069 |

**Ön koşul**
- Gerçek OpenRouter API anahtarı tanımlı (Koşmadan önce, adım 4).

**Adımlar**
1. Katalogu oku.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
d=json.load(sys.stdin)
print([p['name'] for p in d])
"
```

**Beklenen sonuç**
- Liste `openrouter`'ı içerir. `openrouter-responses` **içermez** (Responses
  yüzeyi varsayılan kapalı — bkz. MT-OAI-056).
- `openrouter`'ın `models` dizisi `appsettings.json`'daki tek girdiyi taşır:
  `openai/gpt-5.4-mini`.

---

### MT-OAI-051 — Sağlayıcı adı doğrulaması: rezerve ad, geçersiz desen, 33. karakter

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | K-072 |

Negatif senaryo, üç alt deneme. Her denemede **geçici kod değişikliği** —
`tracon.UseOpenAICompatible("openrouter", openRouter);` satırından
(yaklaşık 160. satır) hemen ÖNCE tek satır eklenir, uygulama başlatılır, konsol
okunur, satır kaldırılır. Üçü de C# derleme zamanında değil, **çalışma
zamanında** (`ArgumentException`, açılış sırasında) patlar.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar (deneme 1 — rezerve ad)**
1. Ekle: `tracon.UseOpenAICompatible("openai", o => o.Endpoint = new Uri("http://localhost:9"));`
2. `dotnet run`, konsolu oku, satırı kaldır.

**Adımlar (deneme 2 — geçersiz desen, büyük harf)**
1. Ekle: `tracon.UseOpenAICompatible("OpenRouter2", o => o.Endpoint = new Uri("http://localhost:9"));`
2. `dotnet run`, konsolu oku, satırı kaldır.

**Adımlar (deneme 3 — 33 karakter, sınır aşımı)**
1. Ekle: `tracon.UseOpenAICompatible("a0000000000000000000000000000000x", o => o.Endpoint = new Uri("http://localhost:9"));`
   (**düzeltildi 2026-08-13**: dokümanın önceki örneği `a234567890123456789012345678901x`
   `len()` ile ölçülünce fiilen 32 karakterdi — sınırda ve geçerli, 33 değil.
   Yukarıdaki dizgi gerçek 33 karakterdir.)
2. `dotnet run`, konsolu oku, satırı kaldır.

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run
```
```bash
# Her denemeden sonra temizlik:
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Deneme 1: `ArgumentException` — mesaj `'openai' saglayici adi rezervedir.`
  ile başlar.
- Deneme 2: `ArgumentException` — mesaj `'OpenRouter2' gecerli bir saglayici
  adi degil.` ile başlar (büyük harf `IsLowerAlphaNumeric` tarafından
  reddedilir).
- Deneme 3: `ArgumentException` — mesaj `'...' gecerli bir saglayici adi
  degil.` ile başlar (33 karakter, sınır 32).
- Üçünde de süreç sıfırdan farklı bir çıkış koduyla sonlanır; `/health` hiçbir
  zaman yanıt vermez.

---

### MT-OAI-052 — Endpoint verilmeyen adlandırılmış sağlayıcı doğrulama hatası verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

Negatif senaryo. **Geçici kod değişikliği.**

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `tracon.UseOpenAICompatible("openrouter", openRouter);` satırından
   (yaklaşık 160. satır) hemen SONRA şu satırı ekle:
   `tracon.UseOpenAICompatible("test-eksik-endpoint", o => o.ApiKey = "sk-test");`
2. `dotnet run`, konsolu oku.
3. Satırı kaldır.

**Girilecek veri**
```bash
cd samples/Tracon.Api && dotnet run
```
```bash
# Temizlik:
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder; `OptionsValidationException`.
- Mesaj `OpenAIProviderOptions.Endpoint uyumlu saglayicilar icin zorunludur.`
  ile başlar.

---

### MT-OAI-053 — Anahtarsız yerel sağlayıcı (Ollama) — koşullu

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | S5 (Faz 8 sapması) |

Bu makinede Ollama kurulu olmayabilir (bkz. `00-INDEKS.md` §2 ortam notu).
Önce kontrol edilir; yoksa **atlanır**, koddaki mekanizma zaten
`OpenAICompatibleProviderExtensionsTests`/`FakeOpenAiCompatibleServer` ile
birim/fonksiyonel test edilmiştir (Faz 8 DoD, sapma S5).

**Ön koşul**
- —

**Adımlar**
1. Ollama'nın çalışıp çalışmadığını denetle.
2. Çalışıyorsa: `Program.cs`'deki (satır ~239-246) yorumlanmış iki satırı aç,
   uygulamayı başlat, `openrouter-destek` benzeri bir agent'la çağır.
3. Değişikliği geri al.

**Girilecek veri**
```bash
curl -s -m 2 http://localhost:11434/api/tags && echo "OLLAMA_VAR" || echo "OLLAMA_YOK — case atlanir"
```
```bash
# Ollama VARSA:
# Program.cs satir ~239-246'daki tracon.UseOpenAICompatible("ollama", ...) blogunu ac.
cd samples/Tracon.Api && dotnet run
curl -s "$APU/api/models" -H "$APB" | python3 -c "import json,sys; print([p['name'] for p in json.load(sys.stdin)])"
```
```bash
# Temizlik:
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Ollama yoksa: case `⏭ Atlandı` işaretlenir, gerekçe "Ollama bu makinede
  kurulu değil" olarak not düşülür.
- Ollama varsa: `ollama` adı `/api/models` listesinde görünür, `ApiKey`
  vermeden kayıt başarılıdır (yer tutucu anahtar mekanizması, K-069 civarı).

---

### MT-OAI-054 — Responses yüzeyi varsayılan olarak KAYDEDİLMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | K-069 |

**Ön koşul**
- Gerçek OpenRouter API anahtarı tanımlı.

**Adımlar**
1. Katalogda `openrouter-responses` aranır.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
d=json.load(sys.stdin)
print('openrouter-responses' in [p['name'] for p in d])
"
```

**Beklenen sonuç**
- Çıktı `False`'tur.

---

### MT-OAI-055 — `EnableResponsesSurface = true` ikinci bir sağlayıcı kaydeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

**Geçici kod değişikliği.**

**Ön koşul**
- Uygulama durdurulmuş, gerçek OpenRouter API anahtarı tanımlı.

**Adımlar**
1. `tracon.UseOpenAICompatible("openrouter", openRouter);` satırını
   (yaklaşık 160. satır) geçici olarak şuna değiştir:
   `tracon.UseOpenAICompatible("openrouter", o => { OpenAIProviderExtensions.Bind(openRouter, o); o.EnableResponsesSurface = true; });`
   (not: `OpenAIProviderExtensions.Bind` `internal`'dır; bu satır derlenmezse
   yerine `openRouter.Bind(o)`'yu manuel alan kopyalamayla değiştirip yalnız
   `o.EnableResponsesSurface = true;` eklemek yeterlidir — asıl kanıtlanan şey
   bayrağın etkisidir, `Bind`'in kendisi değil.)
2. Başlat, katalogu oku.
3. Değişikliği geri al.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
print('openrouter-responses' in [p['name'] for p in json.load(sys.stdin)])
"
```
```bash
# Temizlik:
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- Çıktı `True`'dur — `openrouter-responses` adıyla ikinci bir sağlayıcı
  görünür.

---

### MT-OAI-056 — Gerçek OpenRouter çağrısı: tool kullanımı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

**Ön koşul**
- Gerçek OpenRouter API anahtarı tanımlı.

**Adımlar**
1. `openrouter-destek` agent'ını (fixture) çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/openrouter-destek/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"ORD-1002 siparisim nerede?","sessionId":"oai-openrouter-01"}'
```

**Beklenen sonuç**
- Yanıt `ORD-1002` dizgisini içerir.
- Çalıştırma kaydında `get_order_status` tool'u tam bir kez çağrılır.
- `HTTP 402` (yetersiz kredi) **görülmez** — `openrouter-destek` tanımı zaten
  `MaxOutputTokens: 512` taşır (S6 sapmasının düzeltmesi, bkz. MT-OAI-057).

---

### MT-OAI-057 — `MaxOutputTokens` verilmezse gerçek OpenRouter hesabı `HTTP 402` üretebilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

Negatif senaryo — Faz 8'in ölçtüğü S6 sapmasının tekrar üretilmesi. **Bu
Tracon'in hatası değildir**: OpenRouter'ın kredi kontrolü `max_tokens`'i
"en kötü durum" maliyeti sayar; MAF/OpenAI istemcisi `MaxOutputTokens`
verilmezse varsayılan olarak `max_tokens=65536` gönderir.

**Ön koşul**
- Gerçek OpenRouter API anahtarı tanımlı, hesap bakiyesi düşük/orta.

**Adımlar**
1. `MaxOutputTokens` **vermeyen** bir agent tanımı `openrouter` sağlayıcısıyla
   kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-openrouter-token-siniri-yok",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openrouter", "model": "openai/gpt-5.4-mini" }
}'

curl -s -X POST "$APU/api/agents/manuel-openrouter-token-siniri-yok/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"Merhaba"}'
```

**Beklenen sonuç**
- **Hesap bakiyesine bağlıdır** — bu yüzden değişmez bir metin değil, bir
  DAVRANIŞ beklenir: **ya** çalıştırma normal tamamlanır (bakiye yeterli),
  **ya da** SSE `error` çerçevesinde `402` veya `insufficient credits`
  dizgisi görünür. İkinci durum bir kusur değildir; koşum kaydına
  hangisinin gözlemlendiği ve hesap bakiyesinin (yaklaşık) durumu not
  düşülür.

---

### MT-OAI-058 — İki adlandırılmış sağlayıcı farklı adreslere bağlanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

**Ön koşul**
- Gerçek OpenRouter API anahtarı tanımlı.

**Adımlar**
1. `openai` ve `openrouter`'ın sağlık denetimini karşılaştır.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health/openai" -H "$APB" | python3 -m json.tool
curl -s "$APU/api/models/health/openrouter" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- İki yanıtın `models` dizisi **birbirinden farklıdır** — `openai` gerçek
  OpenAI hesabının model listesini, `openrouter` OpenRouter'ın (yaklaşık 200
  modele kadar kırpılmış) kendi listesini döner. Bu, iki sağlayıcının farklı
  `Endpoint`'e (`https://api.openai.com/v1/` vs
  `https://openrouter.ai/api/v1`) gerçekten bağlandığının ağ-seviyesi
  kanıtıdır.
- `openai`'ın modeller listesi `appsettings.json`'daki üç modelle
  **sınırlı değildir** — gerçek hesaptaki TÜM modelleri taşır (katalog bir
  doğrulama listesi olmadığı gibi, sağlık denetimi de katalogdan değil
  doğrudan `GET {endpoint}/models`'ten okur).

### MT-OAI-070 — Tüm sağlayıcılar `Healthy` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama gerçek OpenAI (+ isteğe bağlı OpenRouter) anahtarıyla çalışıyor.

**Adımlar**
1. Tüm sağlayıcıların sağlığını oku.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `openai` ve `openai-responses` `status: Healthy` döner, `latency` alanı
  doludur (`00:00:0X.XXXXXXX` biçiminde) ve `models` dizisi doludur.
- Kayıtlıysa `openrouter` de `Healthy`'dir.

---

### MT-OAI-071 — Bilinmeyen sağlayıcı adıyla sağlık sorgusu `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Kayıtlı olmayan bir sağlayıcının sağlığını sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/models/health/hic-boyle-bir-saglayici" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`.
- Gövde bir `ProblemDetails`'tir, `title` `Saglayici bulunamadi`, `detail`
  `'hic-boyle-bir-saglayici' adinda kayitli bir model saglayicisi yok.`
  metnini taşır.

---

### MT-OAI-072 — Önbellek TTL'si (60 sn) çalışır; `refresh=true` onu atlar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Aynı sağlayıcıyı arka arkaya iki kez sorgula (`refresh` yok) — ikincisi
   önbellekten gelmelidir (belirgin şekilde daha düşük `latency`, hatta `0`'a
   yakın gecikme).
2. `refresh=true` ile üçüncü kez sorgula — yeniden ağa gitmelidir.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health/openai" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['latency'])"
curl -s "$APU/api/models/health/openai" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['latency'])"
curl -s "$APU/api/models/health/openai?refresh=true" -H "$APB" | python3 -c "import json,sys; print(json.load(sys.stdin)['latency'])"
```

**Beklenen sonuç**
- İkinci çağrının `latency` değeri **birinciyle aynıdır** (önbellekten dönen
  aynı `ModelProviderHealth` nesnesi, `latency` yeniden ölçülmez).
- Üçüncü çağrının `latency` değeri **farklıdır** (yeniden ölçüldü —
  `refresh=true` önbelleği atladı).

---

### MT-OAI-073 — Erişilemeyen sağlayıcının hata detayında adres veya anahtar sızmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | K-073 |

Negatif senaryo. **Geçici kod değişikliği.**

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `tracon.UseOpenAICompatible("openrouter", openRouter);` satırından
   (yaklaşık 160. satır) hemen SONRA ekle:
   `tracon.UseOpenAICompatible("kapali-port-testi", o => { o.Endpoint = new Uri("http://127.0.0.1:59999/v1"); o.ApiKey = "sk-cok-gizli-test-anahtari-12345"; });`
2. Başlat, sağlığı sorgula.
3. Satırı kaldır.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health/kapali-port-testi" -H "$APB" | python3 -m json.tool
```
```bash
# Temizlik:
git checkout -- samples/Tracon.Api/Program.cs
```

**Beklenen sonuç**
- `status: Unhealthy`.
- `detail` alanı `Baglanti hatasi (` ile başlar ve bir `HttpRequestError`
  kategori adı içerir (örnek: `ConnectionRefused`).
- `detail` alanı `127.0.0.1`, `59999` VEYA `sk-cok-gizli-test-anahtari-12345`
  dizgilerinin **hiçbirini** içermez.

---

### MT-OAI-074 — `/api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

**Ön koşul**
- Uygulama az önce yeniden başlatıldı (henüz hiçbir sağlık denetimi
  tetiklenmedi).

**Adımlar**
1. Uygulamayı yeniden başlat.
2. Hiçbir sağlık ucuna gitmeden `/api/models`'i oku.
3. Sağlık ucunu bir kez çağır.
4. `/api/models`'i tekrar oku.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
print([(p['name'], p.get('status')) for p in json.load(sys.stdin)])
"
curl -s "$APU/api/models/health/openai" -H "$APB" > /dev/null
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
print([(p['name'], p.get('status')) for p in json.load(sys.stdin)])
"
```

**Beklenen sonuç**
- İlk okumada `openai`'ın `status` alanı `Unknown` veya boş/`0`'dır — henüz
  hiç denetim yapılmadı.
- İkinci okumada `openai`'ın `status` alanı `Healthy`'dir — `/api/models/health`
  çağrısı sonucu önbelleğe yazmıştır ve `/api/models` onu `TryPeek` ile okur.
- İki `/api/models` çağrısının ikisi de **hızlıdır** (< 1 sn) — ikinci çağrı
  bile sağlayıcıya ağ çağrısı yapmaz, doğrudan önbellekten okur.

### MT-OAI-080 — Ardışık gerçek hatalar devreyi açar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | K-070 |

Negatif senaryo. Eşik koşum hızını artırmak için `2`'ye düşürülür.

**Ön koşul**
- Örnek uygulama gerçek OpenAI anahtarıyla çalışıyor.

**Adımlar**
1. Eşiği `2`'ye, mola süresini `20` saniyeye düşür, uygulamayı yeniden başlat.
2. Bozuk model adına sahip bir agent tanımı kaydet (MT-OAI-043'teki tanımı
   yeniden kullan, hâlâ kayıtlıysa atla).
3. İki kez arka arkaya çalıştır (ikisi de gerçek `404` ile başarısız olur).
4. Üçüncü kez çalıştır.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:CircuitBreaker:FailureThreshold" "2"
dotnet user-secrets set "Tracon:CircuitBreaker:BreakDuration" "00:00:20"
cd samples/Tracon.Api && dotnet run
```
```bash
for i in 1 2 3; do
  echo "--- deneme $i ---"
  curl -N -s -X POST "$APU/api/agents/manuel-bozuk-model/run" -H "$APB" \
       -H "content-type: application/json" \
       -d '{"message":"merhaba","sessionId":"circuit-test-'"$i"'"}' 2>&1 | grep -E "^event:|^data:"
done
```
```bash
# Temizlik:
dotnet user-secrets remove "Tracon:CircuitBreaker:FailureThreshold"
dotnet user-secrets remove "Tracon:CircuitBreaker:BreakDuration"
```

**Beklenen sonuç**
- Deneme 1 ve 2: gerçek OpenAI'a gider (birkaç yüz ms–birkaç saniye sürer);
  başarısız olur (bkz. MT-OAI-043'ün gözlemi).
- Deneme 3: **anında** başarısız olur (network gecikmesi olmadan, < 100ms) —
  `event: error` çerçevesinde `type: TraconProviderUnavailableException`,
  `message` alanı `saglayicisi devre kesici tarafindan gecici olarak
  durduruldu (2 ardisik hata)` dizgisini içerir. `AgentRunStream`'in `catch`
  bloğu artık (2026-08-10'dan beri) HER istisnayı yakalar — bu istisna da
  MT-OAI-043'teki `ClientResultException` de aynı şekilde `error` çerçevesine
  dönüşür.

---

### MT-OAI-081 — Açık devre `/api/models/health`'te `Unhealthy` olarak yansır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

MT-OAI-080'in devamı — devre **hâlâ açık** durumda (mola süresi dolmadan).

**Ön koşul**
- MT-OAI-080 az önce tamamlandı, devre `openai` için açık.

**Adımlar**
1. Sağlığı `refresh=true` ile sorgula.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health/openai?refresh=true" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `status: Unhealthy` — ham `GET {endpoint}/models` denetimi (hâlâ) başarılı
  olsa bile, `ModelProviderHealthCache.ApplyCircuitBreakerOverlay` devrenin
  açık olduğunu görüp durumu ezer.
- `detail` alanı `Devre kesici acik.` ile başlar ve kalan saniyeyi içerir.

---

### MT-OAI-082 — Mola süresi dolunca yarı-açık tek deneme; başarılıysa devre kapanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | — |

MT-OAI-080'in devamı (`BreakDuration=00:00:20`).

**Ön koşul**
- MT-OAI-080 tamamlandı, en az 20 saniye geçti.

**Adımlar**
1. Geçerli bir modelle (`support` agent'ı) çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"circuit-halfopen-01"}'
```

**Beklenen sonuç**
- Çalıştırma **başarıyla** tamamlanır (gerçek OpenAI çağrısı yapılır — mola
  süresi dolduğu için `EnsureRequestAllowed` devreyi `HalfOpen`'a çevirip
  tek denemeye izin verir).
- Ardından `curl -s "$APU/api/models/health/openai" -H "$APB"` çağrısı
  `status: Healthy` döner (`RecordSuccess` devreyi `Closed`'a sıfırladı).

---

### MT-OAI-083 — Devre kesici kapatılırsa (`Enabled=false`) hatalar sayılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 8 |
| **İlgili karar** | K-070 |

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. Devre kesiciyi kapat, eşiği `1`'e düşür (etkisiz olduğunu kanıtlamak için).
2. Bozuk modelle **üç kez** arka arkaya çalıştır.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:CircuitBreaker:Enabled" "false"
dotnet user-secrets set "Tracon:CircuitBreaker:FailureThreshold" "1"
cd samples/Tracon.Api && dotnet run
```
```bash
for i in 1 2 3; do
  curl -N -s -X POST "$APU/api/agents/manuel-bozuk-model/run" -H "$APB" \
       -H "content-type: application/json" \
       -d '{"message":"merhaba","sessionId":"circuit-disabled-'"$i"'"}' 2>&1 | grep -E "^event:|^data:"
done
```
```bash
# Temizlik:
dotnet user-secrets remove "Tracon:CircuitBreaker:Enabled"
dotnet user-secrets remove "Tracon:CircuitBreaker:FailureThreshold"
```

**Beklenen sonuç**
- **Üçü de** gerçek OpenAI'a gider (`TraconProviderUnavailableException`
  hiçbir zaman görünmez) — `FailureThreshold=1` olmasına rağmen devre hiç
  açılmaz çünkü `IsEnabled` kontrolü `EnsureRequestAllowed`/`RecordFailure`'ı
  baştan devre dışı bırakır.
- `curl "$APU/api/models/health/openai"` her zaman ham denetim sonucunu
  gösterir, devre kesici katmanı hiç eklenmez.

---

### MT-OAI-084 — İçerik guard engellemesi devre kesici tarafından hata SAYILMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8, 48 |
| **İlgili karar** | K-322 |

Sınır senaryosu — hiç ağ çağrısı yapılmadan devre sayacının **artmadığını**
kanıtlar. Örnek uygulama `AddPatternContentGuard` ile `gizli-proje` kelimesini
zaten engeller (`Program.cs`, `DeniedTerms`).

**Ön koşul**
- Devre kesici varsayılan ayarlarda (Enabled=true, FailureThreshold=5).
- `support` agent'ının devresi şu an `Closed` (temiz durum — gerekirse önce
  başarılı bir çalıştırma yapılarak sıfırlanır).

**Adımlar**
1. `FIX-PROMPT-05` fixture'ıyla **beş kez** arka arkaya çalıştır (guard her
   seferinde engeller).
2. Ardından geçerli bir mesajla çalıştır.

**Girilecek veri**
```bash
for i in 1 2 3 4 5; do
  curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
       -H "content-type: application/json" \
       -d '{"message":"gizli-proje hakkinda bilgi ver","sessionId":"guard-circuit-'"$i"'"}' \
       -w "\nHTTP: %{http_code}\n"
done

curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"guard-circuit-son"}'
```

**Beklenen sonuç**
- Beş engelleme çağrısının hiçbiri gerçek OpenAI'a çıkmaz (`ContentGuard`
  boru hattında `CircuitBreaker.Wrap`'ten ÖNCE durur).
- Altıncı (geçerli) çağrı **normal şekilde başarıyla tamamlanır** — beş
  engellemenin devre kesiciyi açtığına dair hiçbir belirti yoktur
  (`TraconContentBlockedException` `CircuitBreakingChatClient`'ın özel
  `catch` bloğunda yakalanıp hata sayılmadan yeniden fırlatılır).

### MT-OAI-090 — API anahtarı hiçbir HTTP çıktısında görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3, 8 |
| **İlgili karar** | K-059 |

**Ön koşul**
- Örnek uygulama gerçek anahtarlarla çalışıyor.

**Adımlar**
1. Anahtarın görünebileceği tüm uçları tara.

**Girilecek veri**
```bash
ANAHTAR=$(dotnet user-secrets list --project samples/Tracon.Api | grep "OpenAI:ApiKey" | cut -d= -f2 | tr -d ' ')

for uc in "/api/models" "/api/models/health" "/api/models/health/openai" "/api/tools"; do
  echo "--- $uc ---"
  curl -s "$APU$uc" -H "$APB" | grep -c "$ANAHTAR" || echo "0 (temiz)"
done
```

**Beklenen sonuç**
- Her uç için sayım `0`'dır (veya `grep -c` çıkışı boş/0).

---

### MT-OAI-091 — API anahtarı doğrulama/hata mesajlarında görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3, 8 |
| **İlgili karar** | K-059 |

Negatif senaryo. MT-OAI-010–013 ve MT-OAI-043/052/073'ün ürettiği tüm hata
mesajları burada topluca gözden geçirilir: hiçbirinde gerçek `ApiKey` değeri
(ne OpenAI ne OpenRouter) geçmez — yalnız **ayar anahtarının adı**
(`Tracon:Providers:OpenAI:ApiKey` gibi) geçer. Bu case ayrı bir çağrı
yapmaz, önceki case'lerin koşum kayıtlarını bu açıdan yeniden okur.

**Ön koşul**
- MT-OAI-010, 011, 052, 073 koşuldu.

**Adımlar**
1. Yukarıdaki case'lerin kayıtlı konsol çıktılarını/`detail` alanlarını gözden
   geçir.

**Girilecek veri**
_(ayrı komut yok — önceki case kayıtları incelenir)_

**Beklenen sonuç**
- Hiçbir hata mesajında gerçek `sk-...` ile başlayan bir OpenAI anahtarı veya
  `sk-or-...` ile başlayan bir OpenRouter anahtarı geçmez.

---

### MT-OAI-092 — `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 8, 33 |
| **İlgili karar** | K-059, K-249 |

Bu ucun genel sözleşmesi ve kimlik doğrulaması
[`25-SAGLIK-TESHIS-OPENAPI.md`](25-SAGLIK-TESHIS-OPENAPI.md)'nin konusudur; bu
case yalnız **OpenAI'a özgü** iki davranışı doğrular: (1) `UseOpenAI()`
tarafından kaydedilen sağlayıcı bir `ConfigurationDiagnostic` bildirir, (2)
`UseOpenAICompatible()` tarafından kaydedilen sağlayıcı **bildirmez** (K-249).
`MapTracon`'in çağrı imzası (`Action<TraconEndpointOptions>`) kod-only'dir,
`IConfiguration`'dan bağlanmaz — ama örnek uygulama ucu zaten açık kaydeder
(`Program.cs`, satır ~711: `options.EnableDiagnosticsEndpoint = true;`), hiçbir
ayar değişikliği gerekmez.

**Ön koşul**
- Örnek uygulama çalışıyor. `manuel-test-token-2026` token'ının bu ucu
  açabilecek role sahip olup olmadığı [`13-KIRACI-VE-GUVENLIK.md`](13-KIRACI-VE-GUVENLIK.md)'de
  ayrıca test edilir; bu case yalnız `401`/`403` **almadığını**, aksi hâlde bu
  case'in `⏭ ATLA` işaretlenip 13 numaralı dosyaya not düşüleceğini belirtir.

**Adımlar**
1. Raporu oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/diagnostics" -H "$APB" | tee /tmp/ap-oai-diag.json
python3 -m json.tool < /tmp/ap-oai-diag.json | grep -A4 -i "openai"
```

**Beklenen sonuç**
- Rapordaki `configuration` dizisinde `key: "Tracon:Providers:OpenAI:ApiKey"`
  taşıyan **tam olarak bir** girdi vardır, `resolved: true`, `hint: null`
  (gerçek anahtar tanımlı olduğu için). Tek girdidir çünkü `openai` VE
  `openai-responses` aynı `OpenAIProviderOptions` örneğini, dolayısıyla aynı
  anahtarı bildirir — `TraconDiagnosticsCollector` bu anahtarı
  `seenConfigurationKeys` ile tekilleştirir, ikinci girdi eklenmez.
- `key` alanının değeri **gerçek anahtar dizgisini içermez**, yalnız ayar
  yolunun adını taşır.
- `configuration` dizisinde `openrouter`'a ait **hiçbir** girdi yoktur —
  `UseOpenAICompatible()` `configurationSectionKey: null` geçtiği için (K-249).

---

### MT-OAI-093 — Konsol günlüğünde API anahtarı görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 3 |
| **İlgili karar** | K-059 |

**Ön koşul**
- `appsettings.json`'daki `Logging:LogLevel:Default` `Information`'dır
  (varsayılan — değiştirilmez).

**Adımlar**
1. Uygulamayı konsol çıktısını bir dosyaya yönlendirerek başlat.
2. Birkaç çalıştırma yap (MT-OAI-040, MT-OAI-021 tekrarlanabilir).
3. Dosyada anahtarı ara.

**Girilecek veri**
```bash
ANAHTAR=$(dotnet user-secrets list --project samples/Tracon.Api | grep "OpenAI:ApiKey" | cut -d= -f2 | tr -d ' ')
cd samples/Tracon.Api && dotnet run > /tmp/ap-oai-log.txt 2>&1 &
sleep 5
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz."}' > /dev/null
sleep 2
grep -c "$ANAHTAR" /tmp/ap-oai-log.txt
kill %1
```

**Beklenen sonuç**
- `grep -c` çıktısı `0`'dır.
