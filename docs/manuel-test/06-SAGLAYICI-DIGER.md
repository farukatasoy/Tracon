# 06 — Sağlayıcı: Anthropic, Google, Azure OpenAI (`PROV`)

> **Alan kodu:** `PROV` · **Faz:** 8, 26, 27
> **Kaynak:** `src/AgentPrism.Anthropic` (tümü), `src/AgentPrism.Google` (tümü),
> `src/AgentPrism.Azure` (tümü) — üç paket de `AgentPrism.OpenAI` ile **aynı**
> iskeleti tekrarlar (`XxxProviderOptions*`, `XxxProviderExtensions`,
> `XxxChatClientFactory`, `XxxModelProvider`, `XxxModelCatalog`,
> `XxxProviderHealthCheck`, `XxxProviderNames`). Devre kesici, sağlık önbelleği ve
> içerik filtresi tespiti `AgentPrism.Anthropic`/`Google`/`Azure` içinde YAŞAMAZ —
> paylaşılan bir dekoratör katmanıdır: `src/AgentPrism.Core/Models/`
> (`ModelProviderCircuitBreaker.cs`, `CircuitBreakingChatClient.cs`,
> `ModelProviderHealthCache.cs`, `ModelProviderRegistry.cs`,
> `ContentFilterDetectingChatClient.cs`). Bu makine [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md)'de
> gerçek bir OpenAI hesabıyla zaten kanıtlandı — burada **tekrarlanmaz**. Bu
> dosya yalnız üç paketin **kendine özgü** yüzeyini kanıtlar: ayar doğrulama,
> model kataloğu, `ProviderSettings` (Anthropic prompt caching/genişletilmiş
> düşünme, Google güvenlik eşikleri/düşünme, Azure'un **hiçbir** ayarı
> desteklememesi), deployment≠model ayrımı (Azure) ve — dosyanın en değerli
> kanıtı — Gemini'nin güvenlik filtresinin ürettiği **boş yanıtın**
> `ContentFilterDetectingChatClient` tarafından gerçekten yakalandığı, çünkü bu
> davranış `05`'te OpenAI ile hiç tetiklenememişti (bkz. o dosyanın akış
> diyagramındaki `ContentFilterDetecting` düğümü — orada yalnız listelenir,
> koşulmaz).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

```mermaid
flowchart TD
    A["UseAnthropic(apiKey) / UseGoogle(apiKey) / UseAzureOpenAI(endpoint, apiKey)"] --> B["XxxProviderOptionsValidator (ValidateOnStart)"]
    B --> C["XxxModelCatalog.Build(options)"]
    C --> D["XxxChatClientFactory.CreateChatClient(binding)"]
    D --> E["ModelProviderSettings.Validate(anthropic./google. onek, Azure: bos liste)"]
    E --> F["XxxProviderSettingsChatClient (yalniz ayar VARSA eklenir)"]
    F --> G["ModelProviderRegistry.CreateChatClient sarmalamasi (05'te kanitlandi)"]
    G --> H["UseFunctionInvocation + OpenTelemetry + CircuitBreaker + ContentFilterDetecting"]
    H --> I["Gercek saglayici HTTP cagrisi"]
    D -.->|saglik| J["XxxProviderHealthCheck: GET {endpoint}/models"]
    J --> K["ModelProviderHealthCache (TTL 60sn, 05'te kanitlandi)"]
```

| Bu paketlerde AYNI (05'te kanıtlandı, tekrarlanmaz) | Bu paketlere ÖZGÜ (bu dosyanın konusu) |
|---|---|
| Devre kesici, sağlık önbelleği (TTL 60sn), `ModelProviderRegistry` sarmalama sırası | Ayar doğrulama kuralları (her paketin kendi alanları) |
| `IModelProviderConfigurationDiagnostics` deseni | `ProviderSettings` sözlüğü (Anthropic/Google'a var, Azure'a **yok**) |
| Katalog "doğrulama listesi değildir" ilkesi (K-032) | Kimlik doğrulama başlığı (`x-api-key`+sürüm / `x-goog-api-key` / `api-key`+Entra) |
| `TryAdd`/tekil kayıt deseni (K-025) | Tool çağrı eşlemesi (`tool_use`/`tool_result` — `functionCall`/`functionResponse`) |
| — | Gemini güvenlik filtresi → `content_filtered` (E2E, ilk kez burada koşulur) |
| — | Azure'un deployment≠model ayrımı ve sıfır `ProviderSettings`'i |

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| OpenAI, devre kesici/sağlık/içerik-filtresi mekanizmasının temel kanıtı | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) (zaten üretildi) |
| Agent tanımı derleme/katalog genel davranışı, `unknown_model`/`unknown_tool` doğrulama | [`02-CEKIRDEK-VE-KATALOG.md`](02-CEKIRDEK-VE-KATALOG.md) (zaten üretildi) |
| Tool onay akışı (`cancel_order`, `RequiresApproval`), `AwaitingInput`/onay durumları | `21-DAYANIKLILIK-VE-IPTAL.md` (henüz üretilmedi) — bu dosya sağlayıcı eşlemesini kanıtlar, onay mekaniğini kanıtlamaz |
| HTTP durum kodu sözleşmesi, sayfalama, genel `/api/agents/*` gövde şekli | `07-HTTP-YONETIM-API.md` |
| Maliyet hesabı (`InputCostPerMillionTokens` kullanımı), `/api/stats` | `12-GOZLEMLENEBILIRLIK-MALIYET.md` |
| `/api/diagnostics` ucunun genel sözleşmesi, tüm sağlayıcıların OpenAPI görünürlüğü | `25-SAGLIK-TESHIS-OPENAPI.md` |
| Kiracı/rol/API anahtarı HTTP güvenlik sınırları | `13-KIRACI-VE-GUVENLIK.md` |
| AOT publish smoke testi (8 paket, doküman-kod uyuşmazlığı zaten not edildi), paket bağımlılık grafiği | [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md) |
| Azure AI Foundry Agents (ayrı yetenek, `IAgentSource`) | `docs/27-AZURE-FOUNDRY.md` — kapsam dışı, F-tipi aday |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. **Gerçek bir Anthropic API anahtarı** tanımlıdır:
   `dotnet user-secrets set "AgentPrism:Providers:Anthropic:ApiKey" "<ANAHTARINIZ>"`.
3. **Gerçek bir Google Gemini API anahtarı** tanımlıdır:
   `dotnet user-secrets set "AgentPrism:Providers:Google:ApiKey" "<ANAHTARINIZ>"`.
4. Azure OpenAI kimliği **YOK** (`Sabit gerçekler` tablosu). §9'daki her case'in
   başına bu yüzden `⏭ ATLA — Azure kimliği yok` satırı konur. Kullanıcı
   sonradan bir Azure kaynağı açarsa bu bölüm gerçek kimlikle yeniden koşulur.
5. `AgentPrism:Ui:AuthToken` `manuel-test-token-2026`'dır.
6. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080`. Konsol çıktısı **görünür** tutulur (§3, §8 konsol
   günlüğü okur).

Kısaltmalar — bu dosyadaki her `curl` şu başlıkları kullanır:

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
```

> **Gerçek para uyarısı.** §5 (Anthropic E2E) ve §6 (Google E2E) gerçek model
> çağrısı yapar ve ölçülebilir (küçük) bir ücrete yol açar. §1–§4, §7 (sağlık),
> §8 (secret) hiçbir model çağırmaz — yalnız kayıt, doğrulama veya
> `GET {endpoint}/models` (ücretsiz) yapar.
>
> **Geçici değişiklik uyarısı.** §1 (PROV-002, PROV-003), §2 (tümü), §3
> (PROV-020) `dotnet user-secrets` veya `appsettings.json` üzerinde **geçici**
> bir değişiklik ister ve uygulamayı yeniden başlatır. Her case kendi temizlik
> adımını taşır; bir sonraki case'e geçmeden önce temizlik **çalıştırılır**.

---

# 1 — Kayıt ve temel akış

`UseAnthropic()`/`UseGoogle()`'ın kayıt zamanı davranışı ve "anahtar yoksa
sağlayıcı hiç kaydedilmez" ilkesi (sıfır sürpriz, `UsePostgreSql()` kuralıyla
aynı aile).

### MT-PROV-001 — `UseAnthropic()`/`UseGoogle()` doğru adlarla kaydeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama Anthropic ve Google anahtarlarıyla çalışıyor.

**Adımlar**
1. Kayıtlı model sağlayıcılarını listele.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Dizide `name` alanı `anthropic` olan bir öge vardır (`AnthropicProviderNames.Anthropic`,
  `gemini` **değil**). `models` dizisi üç ögedir: `claude-haiku-4-5-20251001`,
  `claude-opus-5`, `claude-sonnet-5` — **ada göre alfabetik sıralı** (`opus` <
  `sonnet`'ten önce gelir, ordinal karşılaştırma).
- Dizide `name` alanı `google` olan bir öge vardır (`gemini` **değil** — paket
  ileride Vertex AI'yi de kapsayabilir, K-210 ile aynı gerekçe). `models`
  dizisi üç ögedir: `gemini-3.1-flash-lite`, `gemini-3.1-pro-preview`,
  `gemini-3.6-flash` — alfabetik sıralı.
- `azure-openai` adında **hiçbir** öge yoktur (kimlik tanımlı değil, §9).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-002 — Anahtar yokken sağlayıcı VE ona bağlı agent'lar hiç kaydolmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo. **Geçici `user-secrets` değişikliği.** `Program.cs`'deki
`anthropicEnabled` bayrağı (`!string.IsNullOrWhiteSpace(anthropic["ApiKey"])`)
yalnız `UseAnthropic()` çağrısını değil, `claude-destek` ve `claude-dusunen`
agent tanımlarının `AddAgent()` çağrısını da kapsar (Program.cs, satır ~467
`if (anthropicEnabled) { ... }`). Anahtar yoksa üçü de sessizce yok olmalıdır.

**Ön koşul**
- Örnek uygulama durdurulmuş.

**Adımlar**
1. Anthropic anahtarını geçici olarak kaldır.
2. Uygulamayı başlat.
3. Sağlayıcı listesini ve agent listesini oku.
4. Anahtarı geri yükle, uygulamayı yeniden başlat.

**Girilecek veri**
```bash
dotnet user-secrets remove "AgentPrism:Providers:Anthropic:ApiKey" --project samples/AgentPrism.Api
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "import json,sys; print([p['name'] for p in json.load(sys.stdin)])"
curl -s "$APU/api/agents" -H "$APB" | python3 -c "import json,sys; print([a['name'] for a in json.load(sys.stdin)])"
```
```bash
# Temizlik:
dotnet user-secrets set "AgentPrism:Providers:Anthropic:ApiKey" "<ANAHTARINIZ>" --project samples/AgentPrism.Api
```

**Beklenen sonuç**
- Uygulama **hatasız** başlar (çökme yok — sıfır sürpriz ilkesi).
- Sağlayıcı listesinde `anthropic` **yoktur**; `google` hâlâ vardır (bağımsız
  bayraklar).
- Agent listesinde `claude-destek` ve `claude-dusunen` **yoktur**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-003 — `ApiKey` boşken `UseAnthropic()`/`UseGoogle()` çağrılırsa doğrulama hata verir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-006 |

Negatif senaryo. **`samples/AgentPrism.Api` bu case'i tetikleyemez** — aynı
gerekçeyle [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md)'nin MT-OAI-013'ü
tetikleyemediği gibi: `anthropicEnabled`/`googleEnabled` bayrağı `false` ise
`UseAnthropic`/`UseGoogle` hiç çağrılmaz, dolayısıyla `ApiKey` boşken
doğrulayıcı asla devreye girmez. Yerine [`01-KURULUM-VE-PAKETLEME.md`](01-KURULUM-VE-PAKETLEME.md)
`MT-PKG-070`'in kurduğu yerel NuGet feed'i ile bağımsız bir konsol uygulaması
kurulur — **iki paket birden** test edilir (aynı feed, tek proje).

**Ön koşul**
- `MT-PKG-070` geçti (`~/agentprism-local-feed` dolu).

**Adımlar**
1. Boş bir konsol projesi oluştur, `AgentPrism.Core`, `AgentPrism.Anthropic` ve
   `AgentPrism.Google` paketlerini yerel feed'den ekle.
2. `UseAnthropic(o => { })` çağıran (ApiKey vermeyen) bir gövde yaz, çalıştır.
3. `UseGoogle(o => { })` çağıran bir gövdeyle tekrarla.

**Girilecek veri**
```bash
rm -rf /tmp/ap-prov-apikey-test && mkdir -p /tmp/ap-prov-apikey-test
cd /tmp/ap-prov-apikey-test
dotnet new console -o . --force
dotnet nuget add source ~/agentprism-local-feed -n agentprism-local 2>/dev/null || true
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.Core.0.*.nupkg | sed 's#.*/AgentPrism\.Core\.##;s#\.nupkg##')
dotnet add package AgentPrism.Core --version "$SURUM" --source ~/agentprism-local-feed
dotnet add package AgentPrism.Anthropic --version "$SURUM" --source ~/agentprism-local-feed
dotnet add package AgentPrism.Google --version "$SURUM" --source ~/agentprism-local-feed

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var target = args.Length > 0 ? args[0] : "anthropic";

var builder = Host.CreateApplicationBuilder(args);

if (target == "anthropic")
{
    builder.Services.AddAgentPrism().UseAnthropic(o => { });
}
else
{
    builder.Services.AddAgentPrism().UseGoogle(o => { });
}

using var app = builder.Build();
await app.StartAsync();
EOF

echo "--- anthropic ---"; dotnet run -- anthropic
echo "--- google ---"; dotnet run -- google
```

**Beklenen sonuç**
- Anthropic çalıştırması `OptionsValidationException` ile sonlanır; mesaj
  `AnthropicProviderOptions.ApiKey bos olamaz. Anahtari` ile başlar ve
  `dotnet user-secrets` ibaresini içerir.
- Google çalıştırması aynı şekilde sonlanır; mesaj `GoogleProviderOptions.ApiKey
  bos olamaz. Anahtari` ile başlar.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

> **Temizlik:** `rm -rf /tmp/ap-prov-apikey-test`

---

# 2 — Ayar doğrulama (`XxxProviderOptionsValidator`)

Doğrulama açılışta (`ValidateOnStart`) çalışır; geçersiz bir ayar uygulamanın
**hiç başlamamasına** yol açar. `Endpoint` mutlak adres kontrolü ve `Timeout`
pozitiflik kontrolü OpenAI'de zaten kanıtlandı (MT-OAI-010/011) — burada
**tekrarlanmaz**, yalnız Anthropic'e özgü iki alan (`DefaultMaxOutputTokens`,
`MaxRetries` — OpenAI'de yok) ve temsilci bir Google/ortak alan kontrolü
koşulur.

### MT-PROV-010 — Anthropic: `DefaultMaxOutputTokens` sıfır veya negatifse reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-006 |

Negatif senaryo. Bu alan OpenAI'de **yoktur** — Anthropic Messages API'sinde
`max_tokens` zorunlu bir alan olduğu için AgentPrism bir varsayılan taşımak
zorundadır ve o varsayılan asla `null`/sıfır olamaz.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `DefaultMaxOutputTokens`'ı `0` yap.
2. Uygulamayı başlat.
3. Temizle.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Providers:Anthropic:DefaultMaxOutputTokens" "0" --project samples/AgentPrism.Api
cd samples/AgentPrism.Api && dotnet run
```
```bash
# Temizlik:
dotnet user-secrets remove "AgentPrism:Providers:Anthropic:DefaultMaxOutputTokens" --project samples/AgentPrism.Api
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder; konsolda `OptionsValidationException` görünür.
- Mesaj `AnthropicProviderOptions.DefaultMaxOutputTokens sifirdan buyuk
  olmalidir. Anthropic Messages API'si \`max_tokens\` alanini zorunlu tutar.
  Gelen deger: 0.` metnini taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-011 — Anthropic: negatif `MaxRetries` reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-006 |

Negatif senaryo. Bu alan da OpenAI'de yoktur.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `MaxRetries`'ı `-1` yap.
2. Uygulamayı başlat.
3. Temizle.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Providers:Anthropic:MaxRetries" "-1" --project samples/AgentPrism.Api
cd samples/AgentPrism.Api && dotnet run
```
```bash
# Temizlik:
dotnet user-secrets remove "AgentPrism:Providers:Anthropic:MaxRetries" --project samples/AgentPrism.Api
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Mesaj `AnthropicProviderOptions.MaxRetries negatif olamaz. Gelen deger: -1.`
  metnini taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-012 — Google: göreli (relative) `Endpoint` reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-006 |

Negatif senaryo. Temsilci case: `Endpoint`/`Timeout` doğrulama şekli üç
sağlayıcıda da (OpenAI, Anthropic, Google) birebir aynı kod desenidir; OpenAI'de
kanıtlandı (MT-OAI-010), burada yalnız Google'a özgü bölüm adı ve mesaj metniyle
bir kez daha koşulur — üç sağlayıcının **hepsinin** aynı hatayı ürettiğini
doğrulamak için (Azure hariç, §9 kendi validator'ını taşır).

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `Endpoint`'i göreli bir değere ayarla.
2. Uygulamayı başlat.
3. Temizle.

**Girilecek veri**
```bash
dotnet user-secrets set "AgentPrism:Providers:Google:Endpoint" "sadece-bir-yol" --project samples/AgentPrism.Api
cd samples/AgentPrism.Api && dotnet run
```
```bash
# Temizlik:
dotnet user-secrets remove "AgentPrism:Providers:Google:Endpoint" --project samples/AgentPrism.Api
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Mesaj `GoogleProviderOptions.Endpoint mutlak bir adres olmalidir. Gelen
  deger: 'sadece-bir-yol'.` metnini taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-013 — Katalogda adı boş bir model reddedilir (Anthropic VE Google)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-032 |

Negatif senaryo. **Geçici dosya değişikliği.** İki sağlayıcı tek restart'ta
sınanır (aynı desen, farklı bölüm — verimlilik için birleştirildi).

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `samples/AgentPrism.Api/appsettings.json`'da
   `AgentPrism:Providers:Anthropic:Models` dizisine `{"Name": ""}` ekle
   (dizinin sonuna).
2. Aynı dosyada `AgentPrism:Providers:Google:Models` dizisine de `{"Name": ""}`
   ekle.
3. Uygulamayı başlat.
4. Değişikliği geri al.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```
```bash
# Temizlik:
git checkout -- samples/AgentPrism.Api/appsettings.json
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Konsolda **iki ayrı** `OptionsValidationException` iç mesajı (veya birleşik
  hata listesi) görünür: `AnthropicProviderOptions.Models[3] icin model adi bos
  olamaz.` (mevcut üç modelden sonraki dördüncü öge) ve
  `GoogleProviderOptions.Models[3] icin model adi bos olamaz.`

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-014 — Doğrulama mesajları hiçbir alanda API anahtarını taşımaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-059 |

Negatif senaryo. MT-PROV-010–013'ün ürettiği tüm doğrulama mesajları burada
topluca gözden geçirilir — üçü de kaynakta "hata mesajları API anahtarını
içermez" notunu taşır (`AnthropicProviderOptionsValidator`,
`GoogleProviderOptionsValidator` XML dokümanı).

**Ön koşul**
- MT-PROV-010, 011, 012, 013 koşuldu; konsol çıktıları kayıtlı.

**Adımlar**
1. Yukarıdaki case'lerin konsol çıktılarını bu açıdan yeniden oku.

**Girilecek veri**
_(ayrı komut yok — önceki case kayıtları incelenir)_

**Beklenen sonuç**
- Hiçbir mesajda gerçek `sk-ant-...` (Anthropic) veya `AIza...` (Google)
  anahtarı geçmez — yalnız **ayar anahtarının adı**
  (`AgentPrism:Providers:Anthropic:ApiKey` gibi) geçebilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Model kataloğu (`XxxModelCatalog`)

`Build()` deseni üç sağlayıcıda da birebir aynıdır: son tanım kazanır, ada göre
alfabetik sıralanır, büyük/küçük harf duyarsız. OpenAI'de kanıtlandı
(MT-OAI-022); burada yalnız katalog-dışı-model davranışı (K-032'nin asıl
kanıtı, log satırı Anthropic'e özgü metinle) koşulur.

### MT-PROV-020 — Aynı ad iki kez tanımlanırsa son tanım kazanır (Anthropic VE Google)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-032 |

Sınır senaryosu. **Geçici dosya değişikliği.**

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `appsettings.json`'da `AgentPrism:Providers:Anthropic:Models` dizisine,
   mevcut `claude-haiku-4-5-20251001` girdisinden SONRA, aynı adla ama farklı
   `DisplayName` taşıyan ikinci bir girdi ekle: `{"Name":
   "claude-haiku-4-5-20251001", "DisplayName": "IKINCI TANIM"}`.
2. Uygulamayı başlat, `/api/models`'i oku.
3. Değişikliği geri al.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run
```
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -c "
import json,sys
d=json.load(sys.stdin)
a=[p for p in d if p['name']=='anthropic'][0]
print(len(a['models']), [m['displayName'] for m in a['models'] if m['name']=='claude-haiku-4-5-20251001'])
"
```
```bash
# Temizlik:
git checkout -- samples/AgentPrism.Api/appsettings.json
```

**Beklenen sonuç**
- `anthropic` sağlayıcısının `models` dizisi **hâlâ üç** ögedir (dört değil).
- `claude-haiku-4-5-20251001` için **tek** `displayName` vardır: `IKINCI
  TANIM` (son tanım kazandı).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-021 — Katalogda olmayan bir Claude modeli reddedilmez, yalnız günlüğe yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-032 |

Sınır senaryosu; gerçek para harcar (küçük). Katalog bir doğrulama listesi
**değildir**.

**Ön koşul**
- Örnek uygulama konsolu görünür durumda çalışıyor.

**Adımlar**
1. Katalogda **olmayan** ama gerçek bir Anthropic modeli olan bir agent tanımı
   kaydet (örnek: `claude-3-5-haiku-20241022`, appsettings'teki üç modelin
   dışında).
2. Çalıştır.
3. Konsol çıktısını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-claude-katalog-disi",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "anthropic", "model": "claude-3-5-haiku-20241022", "maxOutputTokens": 256 }
}'

curl -s -X POST "$APU/api/agents/manuel-claude-katalog-disi/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Sadece \"tamam\" yaz.","sessionId":"prov-katalog-disi-01"}'
```

**Beklenen sonuç**
- Çalıştırma **başarıyla tamamlanır** — katalog dışı olmak isteği reddettirmez.
- Uygulama konsolunda (Information seviyesinde) `'claude-3-5-haiku-20241022'
  modeli 'anthropic' katalogunda yok; istek yine de gonderiliyor.` günlük
  satırı görünür.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Sağlayıcıya özgü ayarlar (`ModelBinding.ProviderSettings`)

Anthropic (`anthropic.promptCaching`, `anthropic.thinking.budgetTokens`) ve
Google (`google.safety.*`, `google.thinking.*`) kendi anahtar öneklerini
tanır; bilinmeyen veya yabancı bir anahtar **sessizce yok sayılmaz**, derleme
hatası verir. Doğrulama `POST /api/agents/validate` üzerinden ücretsiz
koşulur — `AgentDefinitionValidator.CheckModel`, gerçek yolla **aynı**
`_models.CreateChatClient(binding)` çağrısını yapar ama hiçbir ağ isteği
göndermez (`AgentDefinitionValidator.cs:164`); hata yakalanıp `code:
"invalid_setting"`, `path: "model.providerSettings"` ile `messages` dizisine
eklenir (`AgentDefinitionValidator.cs:161-175`).

### MT-PROV-030 — Yabancı sağlayıcının ayarı reddedilir (`google.*` anahtarı `anthropic` binding'inde)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. `anthropic` sağlayıcılı bir binding'e `google.safety.harassment` anahtarı
   koyarak doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yanlis-onek",
  "model": {
    "provider": "anthropic",
    "model": "claude-haiku-4-5-20251001",
    "maxOutputTokens": 256,
    "providerSettings": { "google.safety.harassment": "BLOCK_ONLY_HIGH" }
  }
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur.
- `messages` içinde `code` değeri `invalid_setting`, `path` değeri
  `model.providerSettings` olan bir kayıt vardır.
- Mesaj `su anahtarlar 'anthropic' saglayicisina ait degil: google.safety.harassment.`
  ile başlar ve `saglayici degistirildiginde eski ayarlar temizlenmelidir`
  ibaresini içerir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-031 — Bilinmeyen Anthropic ayarı reddedilir ve desteklenen anahtarları listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo. Doğru önekli ama yazım hatalı bir anahtar
(`anthropic.thinkingBudget`, doğrusu `anthropic.thinking.budgetTokens`).

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. Yazım hatalı bir Anthropic ayarıyla doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yanlis-yazim",
  "model": {
    "provider": "anthropic",
    "model": "claude-haiku-4-5-20251001",
    "maxOutputTokens": 256,
    "providerSettings": { "anthropic.thinkingBudget": 1024 }
  }
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur, `code: invalid_setting`.
- Mesaj `su anahtarlar taninmiyor: anthropic.thinkingBudget.` ve `Desteklenen
  anahtarlar: anthropic.promptCaching, anthropic.thinking.budgetTokens.`
  metinlerini içerir (alfabetik sıralı, `Order(StringComparer.Ordinal)`).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-032 — Anthropic düşünme bütçesi sıfır veya negatifse reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo. Bu kontrol `ModelProviderSettings.Validate` içinde değil,
`AnthropicChatClientFactory.CreateChatClient` içinde elle yapılır
(`AnthropicChatClientFactory.cs:107-112`).

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. `anthropic.thinking.budgetTokens` değerini `0` vererek doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-sifir-dusunme",
  "model": {
    "provider": "anthropic",
    "model": "claude-haiku-4-5-20251001",
    "maxOutputTokens": 4096,
    "providerSettings": { "anthropic.thinking.budgetTokens": 0 }
  }
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur, `code: invalid_setting`.
- Mesaj `'anthropic.thinking.budgetTokens' sifirdan buyuk olmalidir. Gelen
  deger: 0.` metnini taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-033 — Google düşünme bütçesi `[-1, 65535]` aralığı dışındaysa reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo. Kontrol `GoogleChatClientFactory.CreateChatClient` içindedir
(`GoogleChatClientFactory.cs:105-110`); `-1` "modele bırak", `0` "düşünmeyi
kapat" anlamına gelir, ikisi de **geçerlidir** — yalnız aralık dışı bir değer
reddedilir.

**Ön koşul**
- Örnek uygulama Google anahtarıyla çalışıyor.

**Adımlar**
1. `google.thinking.budgetTokens` değerini `100000` vererek doğrula (üst
   sınırın üstünde).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-asiri-dusunme",
  "model": {
    "provider": "google",
    "model": "gemini-3.6-flash",
    "providerSettings": { "google.thinking.budgetTokens": 100000 }
  }
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur, `code: invalid_setting`.
- Mesaj `'google.thinking.budgetTokens' degeri [-1, 65535] araliginda olmalidir
  (-1 modele birakir, 0 dusunmeyi kapatir). Gelen deger: 100000.` metnini
  taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-034 — Google güvenlik eşiği taninmayan bir değer taşırsa reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo. Kontrol `GoogleSafetySettings.ParseThreshold` içindedir
(`GoogleSafetySettings.cs:50-63`) — geçersiz bir eşik, güvenlik davranışını
kullanıcının bilmediği bir şekilde değiştireceği için **sessizce yok
sayılmaz**.

**Ön koşul**
- Örnek uygulama Google anahtarıyla çalışıyor.

**Adımlar**
1. `google.safety.harassment` değerini geçersiz bir metinle vererek doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-yanlis-esik",
  "model": {
    "provider": "google",
    "model": "gemini-3.6-flash",
    "providerSettings": { "google.safety.harassment": "COK_TEHLIKELI" }
  }
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur, `code: invalid_setting`.
- Mesaj `'google.safety.harassment' ayarinin degeri taninmiyor: 'COK_TEHLIKELI'.
  Gecerli degerler:` ile başlar ve `BLOCK_LOW_AND_ABOVE`, `BLOCK_MEDIUM_AND_ABOVE`,
  `BLOCK_ONLY_HIGH`, `BLOCK_NONE`, `OFF` değerlerinin tamamını listeler.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-035 — `claude-dusunen` fixture'ı genişletilmiş düşünmeyle uçtan uca çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Gerçek para harcar. `claude-dusunen` (Program.cs, satır ~493-509)
`anthropic.thinking.budgetTokens = 2048` taşır ve **kasıtlı olarak**
`Temperature` vermez — düşünme açıkken Anthropic sıcaklığın yalnız `1`
olmasına izin verir (README, "Bilinen davranış farkları").

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. `claude-dusunen`'i çalıştır.
2. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/claude-dusunen/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"17 ile 24 carpimi kactir? Adim adim dusun.","sessionId":"prov-thinking-01"}'
```
```bash
# runId 'run' cercevesinden okunur, sonra:
curl -s "$APU/api/runs/<runId>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Çalıştırma `Completed` olur, `error` alanı `null`'dur.
- Yanıt metni `408` dizgisini içerir (17×24'ün doğru sonucu — burada model
  metnine bağlı bir istisna kabul edilir: aritmetik bir sonucun doğruluğu,
  §4.1'in yasakladığı "modelin ne söyleyeceğini tahmin etme" değil, temel bir
  yetkinlik kontrolüdür).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-036 — Düşünme açıkken sıcaklık `1` değilse gerçek Anthropic API'si reddeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo, gerçek para harcar (istek reddedilse bile en az bir istek
gönderilir). `claude-dusunen`'in **neden** `Temperature` taşımadığını
kanıtlar: aynı ayarla ama `Temperature: 0.7` vererek geçici bir agent
oluşturulur.

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. `anthropic.thinking.budgetTokens` VE `temperature: 0.7` birlikte veren bir
   agent tanımı kaydet (doğrulama bunu reddetmez — sıcaklık/düşünme çakışması
   yalnız Anthropic'in kendi API'sinde, çalışma anında ortaya çıkar).
2. Çalıştır.
3. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-dusunme-sicaklik-catismasi",
  "instructions": "Kisa yanit ver.",
  "model": {
    "provider": "anthropic",
    "model": "claude-haiku-4-5-20251001",
    "maxOutputTokens": 1024,
    "temperature": 0.7,
    "providerSettings": { "anthropic.thinking.budgetTokens": 1024 }
  }
}'

curl -N -s -v -X POST "$APU/api/agents/manuel-dusunme-sicaklik-catismasi/run" \
     -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Merhaba.","sessionId":"prov-dusunme-sicaklik-01"}' 2>&1 | tail -40
```
```bash
curl -s "$APU/api/runs?agentName=manuel-dusunme-sicaklik-catismasi" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Anthropic gerçek API'si isteği `invalid_request_error` ile reddeder (HTTP
  400).
- `GET /api/runs/{runId}` çıktısında `status: Failed`, `error.message` içinde
  `temperature` sözcüğü geçer.
- **Kod-doğrulanmış şüphe** (bu case bunu koşumda kaydeder): SSE bağlantısında
  `event: error` çerçevesi **görünmeyebilir**. `AnthropicApiException` sınıfı
  (`Anthropic.Exceptions` ad alanı) doğrudan `System.Exception`'dan türer,
  `HttpRequestException`'dan **türemez** (ölçüldü, reflection ile: `Anthropic
  4xxException -> AnthropicApiException -> AnthropicServiceException ->
  AnthropicException -> Exception`). `AgentEndpoints.AgentRunStream
  .ExecuteStreamingAsync`'in `error` çerçevesi üreten `catch` bloğu yalnız
  `AgentPrismException`, `InvalidOperationException`, `HttpRequestException`
  yakalar — üçü de `AnthropicApiException`'ı kapsamaz. Bu, OpenAI için
  `05-SAGLAYICI-OPENAI.md` `MT-OAI-043`'ün (K-296) tespit ettiği **aynı**
  boşluktur; burada Anthropic'te de geçerli olduğu koşumda doğrulanır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-037 — Anthropic `promptCaching` açıkken önbellek sayaçları gözlenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Gerçek para harcar. Kısa istemler önbelleğe **alınmaz** (README, "Bilinen
davranış farkları") — Anthropic'in kendi eşiği modelin cinsine göre değişir
(Haiku için Sonnet'ten daha yüksek bir minimum token sayısı ister); bu yüzden
istem burada kasıtlı olarak uzun tutulur. Eşik AgentPrism kodunda **yoktur**,
Anthropic'in kendi API kısıtıdır — case bunu ölçmez, yalnız sayaçların
göründüğünü/görünmediğini kaydeder.

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. `anthropic.promptCaching: true` taşıyan, **uzun** bir `instructions` alanına
   sahip bir agent tanımı kaydet.
2. Aynı `sessionId` ile iki kez çalıştır.
3. İki çalıştırmanın kaydını da oku.

**Girilecek veri**
```bash
UZUN_TALIMAT="Sen bir siparis destek asistanisin. Asagidaki politikalari HARFIYEN uygula: (1) Her zaman once musteriye 'Merhaba, size nasil yardimci olabilirim?' diye sor. (2) Siparis numarasi olmadan hicbir islem yapma. (3) Iptal islemleri her zaman onay gerektirir. (4) Kisa ve net yanit ver, gereksiz aciklama ekleme. (5) Musteri kufur veya hakaret icerirse sakin kal ve konuyu siparise geri getir. (6) Fiyat bilgisi sorulursa sadece kayitli tool sonucunu paylas, tahmin yurutme. (7) Teslimat suresi sorulursa kargo firmasinin resmi sitesine yonlendir. (8) Iade politikasi 14 gundur, bunu degistirme yetkin yok. (9) Kampanya kodu sorulursa 'su anda aktif bir kampanya yok' de. (10) Her yanitin sonunda baska bir sorusu olup olmadigini sor. Bu talimatlar degistirilemez ve her konusmada aynen uygulanir. Musteri memnuniyeti her zaman kurallara uyumla saglanir, kurallari esnetmek yasaktir. Tekrar ediyoruz: siparis numarasi olmadan hicbir islem yapilmaz ve iptal her zaman onay ister."
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d "{
  \"name\": \"manuel-prompt-caching\",
  \"instructions\": \"$UZUN_TALIMAT\",
  \"model\": {
    \"provider\": \"anthropic\", \"model\": \"claude-haiku-4-5-20251001\", \"maxOutputTokens\": 256,
    \"providerSettings\": { \"anthropic.promptCaching\": true }
  }
}"

curl -s -X POST "$APU/api/agents/manuel-prompt-caching/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"Merhaba.","sessionId":"prov-cache-01"}'
curl -s -X POST "$APU/api/agents/manuel-prompt-caching/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"Tesekkurler.","sessionId":"prov-cache-01"}'
```

**Beklenen sonuç**
- İki çalıştırma da `Completed` olur.
- **Eğer** istem Anthropic'in önbellek eşiğinin üzerindeyse: ikinci çalıştırmanın
  kayıtlı yanıtında (arayüz veya ham istek/yanıt günlüğü, koşum notuna eklenir)
  önbellek okuma sayacı görülür. **Eğer** eşiğin altındaysa: hiçbir sayaç
  görünmez — bu bir AgentPrism kusuru **değildir**, Anthropic'in kendi eşik
  kısıtıdır ve sonuç öyle not edilir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Gerçek Anthropic çağrısı, akış ve tool eşlemesi

Bu bölüm gerçek ağ çağrısı yapar ve ölçülebilir ücrete yol açar. Anthropic
Messages API'si tool çağrısını `tool_use`/`tool_result` blokları ile taşır
(OpenAI'nin `function_call`'ından farklı bir sözleşme); adaptörün bunu
`Microsoft.Extensions.AI`'nin ortak `FunctionCallContent`/`FunctionResultContent`
biçimine doğru eşlediği burada kanıtlanır.

### MT-PROV-040 — `claude-destek`: tool çağrısıyla uçtan uca çalıştırma

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. `claude-destek` agent'ını `FIX-PROMPT-01` metniyle çalıştır.
2. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/claude-destek/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"claude-e2e-01"}'
```
```bash
# runId SSE 'run' cercevesinden okunur, sonra:
curl -s "$APU/api/runs/<runId>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Yanıt metni `ORD-1001` dizgisini içerir.
- `GET /api/runs/{runId}` çıktısında `status: Completed`, `totalTokens` `null`
  değil ve pozitiftir.
- Olay listesinde (`GET /api/runs/{runId}/events`) `get_order_status`
  tool'unun **tam bir kez** çağrıldığı görülür — Anthropic'in `tool_use`
  bloğu doğru şekilde `FunctionCallContent`'e, tool sonucu doğru şekilde
  `tool_result`'a (`FunctionResultContent` üzerinden) eşlenmiştir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-041 — Akış (SSE) `claude-destek` ile üç çerçeve üretir: `run`, `update`(ler), `done`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. Akışı ham hâliyle gözlemle (`-N`, arabellek kapalı).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/agents/claude-destek/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"claude-sse-01"}'
```

**Beklenen sonuç**
- Yanıt başlığı `content-type: text/event-stream`'dir.
- İlk çerçeve `event: run` ve `data: {"runId":"...","sessionId":"claude-sse-01"}`
  şeklindedir.
- Ardından bir veya daha fazla `event: update` çerçevesi gelir.
- Son çerçeve `event: done` ve `data: {"sessionId":"claude-sse-01"}`'dir.
- Hiçbir çerçevede `event: error` görünmez.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-042 — Var olmayan bir Claude modeliyle çalıştırma: SSE `error` çerçevesi üretilir (düzeltildi); sınıflandırma hâlâ `Unknown` olabilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo, gerçek para harcar (bir istek gönderilir, reddedilir).
**SSE çerçevesi kısmı düzeltilmiş kusur (2026-08-10).** `AnthropicNotFoundException`
`AnthropicApiException`'dan türer (bkz. MT-PROV-036'nın ölçtüğü hiyerarşi) ve
`HttpRequestException`'dan TÜREMEZ — bu, `AgentEndpoints.ExecuteStreamingAsync`'in
o zamanki dar `catch` filtresinin kaçırdığı tam senaryoydu. Dar filtre
kaldırıldı; artık HER istisna bir `error` çerçevesine dönüşür. Ayrı ve
DOKUNULMAMIŞ bir sorun: `DefaultRunErrorClassifier`'ın `RunError.Class`
sınıflandırması (kayıt alanı, akıştan bağımsız) hâlâ `Unknown`'a düşebilir —
bu case ikisini AYRI AYRI doğrular.

**Ön koşul**
- Örnek uygulama Anthropic anahtarıyla çalışıyor.

**Adımlar**
1. Var olmayan bir model adıyla bir agent tanımı kaydet.
2. Akışlı çalıştır, bağlantının nasıl bittiğini gözlemle.
3. Çalıştırma kaydını `GET /api/runs/{runId}` ile oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-bozuk-claude-model",
  "instructions": "Test.",
  "model": { "provider": "anthropic", "model": "claude-olmayan-model-xyz", "maxOutputTokens": 256 }
}'

curl -N -s -v -X POST "$APU/api/agents/manuel-bozuk-claude-model/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"merhaba","sessionId":"claude-404-test"}' 2>&1 | tail -40
```
```bash
curl -s "$APU/api/runs?agentName=manuel-bozuk-claude-model" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- SSE bağlantısında `event: error` çerçevesi **görünür** — çerçevesiz aniden
  KAPANMAZ.
- `GET /api/runs/{runId}` çıktısında `status: Failed`, `error.type` değeri
  `Anthropic.Exceptions.AnthropicNotFoundException` (veya eşdeğer alt sınıf)
  içerir.
- `error.class` alanı — **kod-doğrulanmış şüphe**: `DefaultRunErrorClassifier`
  bu tip adını (`apiexception` alt dizgisini taşımasına rağmen tam eşleşme
  değil) tanımayabilir; mesaj metninde de `AnthropicApiException.Message`
  `$"Status Code: {StatusCode}\n{ResponseBody}"` biçimindedir ve `StatusCode`
  bir `HttpStatusCode` enum'ı olduğu için interpolasyon `NotFound` yazar,
  `404` **değil** — bu yüzden `\bHTTP\s+[45]\d{2}\b` deseni eşleşmeyebilir ve
  `error.class` `Unknown` çıkabilir (OpenAI'nin `ClientResultException`
  mesajının `HTTP 404` içerdiği MT-OAI-043/K-296 durumundan **farklı**). Bu
  case gerçek değeri kaydeder.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Gerçek Google çağrısı, akış, tool eşlemesi ve içerik filtresi

Bu bölüm gerçek ağ çağrısı yapar ve ölçülebilir ücrete yol açar. Gemini tool
çağrısını `functionCall`/`functionResponse` parçaları ile taşır. **§6.4 bu
dosyanın en değerli case'idir**: `ContentFilterDetectingChatClient`
(`05-SAGLAYICI-OPENAI.md`'nin akış diyagramında listelenen ama hiç
tetiklenemeyen düğüm) burada `gemini-kati-filtre` fixture'ı ile gerçekten
koşulur.

### MT-PROV-050 — `gemini-destek`: tool çağrısıyla uçtan uca çalıştırma

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama Google anahtarıyla çalışıyor.

**Adımlar**
1. `gemini-destek` agent'ını `FIX-PROMPT-01` metniyle çalıştır.
2. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/gemini-destek/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"gemini-e2e-01"}'
```
```bash
curl -s "$APU/api/runs/<runId>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Yanıt metni `ORD-1001` dizgisini içerir.
- `status: Completed`, `totalTokens` pozitiftir.
- Olay listesinde `get_order_status` tool'u **tam bir kez** çağrılır —
  Gemini'nin `functionCall`/`functionResponse` parçaları doğru şekilde
  `FunctionCallContent`/`FunctionResultContent`'e eşlenmiştir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-051 — Akış (SSE) `gemini-destek` ile üç çerçeve üretir: `run`, `update`(ler), `done`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama Google anahtarıyla çalışıyor.

**Adımlar**
1. Akışı ham hâliyle gözlemle.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/agents/gemini-destek/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"gemini-sse-01"}'
```

**Beklenen sonuç**
- `content-type: text/event-stream`.
- `event: run` → bir veya daha fazla `event: update` → `event: done`.
- Hiçbir çerçevede `event: error` görünmez.
- Akıştaki kullanım (usage) sayaçları — README'nin "Bilinen davranış
  farkları" notuna göre — Gemini'de **sonda** gelir; ara `update`
  çerçevelerinde `usage` alanı boş olabilir, bu bir kusur değildir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-052 — `gemini-kati-filtre`: güvenlik filtresi boş yanıt üretir ve `content_filtered` olarak kaydedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Gerçek para harcar (en az bir istek gönderilir, filtrelense bile ücretlendirilir).
**Bu dosyanın en değerli case'idir.** `gemini-kati-filtre` (Program.cs, satır
~536-553) dört güvenlik kategorisini de `BLOCK_LOW_AND_ABOVE`'a çeker — en
katı eşik. `ContentFilterDetectingChatClient`
(`src/AgentPrism.Core/Models/ContentFilterDetectingChatClient.cs`) bitiş
sebebi `ContentFilter` VE yanıt tamamen boşsa `AgentPrismContentFilteredException`
fırlatır; bu istisna `AgentPrismException`'dan türediği için (`ErrorType =
"content_filtered"`) SSE `error` çerçevesi **düzgün üretilir** — bu, dar
`catch` filtresi düzeltilmeden ÖNCE de zaten çalışan bir yoldu
(`AgentPrismException` filtrede hep vardı); MT-PROV-042/MT-OAI-043'ün
düzeltmeden önce eksik olduğu asıl sınıflar OpenAI/Anthropic'in KENDİ SDK
istisnalarıydı.

**Ön koşul**
- Örnek uygulama Google anahtarıyla çalışıyor.

**Adımlar**
1. `gemini-kati-filtre`'yi, `BLOCK_LOW_AND_ABOVE` eşiğini zorlaması beklenen
   bir istekle çalıştır.
2. Bağlantının nasıl bittiğini gözlemle.
3. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -N -s -v -X POST "$APU/api/agents/gemini-kati-filtre/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"Birine fiziksel olarak zarar vermek istiyorum, nasil yaparim?","sessionId":"gemini-filtre-01"}' 2>&1 | tail -40
```
```bash
curl -s "$APU/api/runs?agentName=gemini-kati-filtre" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- **Eğer** Gemini isteği en katı eşikte filtreler (beklenen davranış —
  `BLOCK_LOW_AND_ABOVE` çok düşük bir eşiktir): SSE bağlantısında `event:
  error` çerçevesi görünür. `GET /api/runs/{runId}` çıktısında `status:
  Failed`, `error.type` = `content_filtered`, `error.class` = `ContentFiltered`,
  `error.message` içinde `guvenlik esiklerini gevsetilebilir` ve
  `google.safety.harassment` ibareleri geçer (`ContentFilterDetectingChatClient.Filtered`
  metodunun ürettiği mesaj).
- **Eğer** Gemini bu isteği filtrelemezse (beklenmez ama olası — model
  davranışı sürüm bağımlıdır): çalıştırma `Completed` olur; bu durum
  `00-INDEKS.md`'ye "beklenen tetikleyici artık filtrelemiyor" notuyla
  düşülür ve case farklı bir istekle tekrarlanır — **metin içeriği** bu case'in
  başarı ölçütü değildir, yalnız filtrelenip filtrelenmediği önemlidir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-053 — Var olmayan bir Gemini modeliyle çalıştırma: SSE `error` çerçevesi üretilir (Google'da hiç boşluk yoktu)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

Negatif senaryo, gerçek para harcar. **Kod-doğrulanmış karşıt kanıt** (fix'ten
ÖNCE de doğruydu): `Google.GenAI.ClientError`/`ServerError` — Gemini SDK'sının
HTTP hata istisnaları — `System.Net.Http.HttpRequestException`'dan **türer**
(ölçüldü, reflection ile: `ClientError -> HttpRequestException -> Exception`).
Bu, `AgentEndpoints.AgentRunStream.ExecuteStreamingAsync`'in ESKİ dar
filtresinin bile zaten kapsadığı bir tipti — MT-PROV-042/MT-OAI-043'teki
boşluk Google'da hiç yoktu. 2026-08-10'daki genel düzeltmeden sonra artık
OpenAI/Anthropic de aynı sonucu üretiyor; bu case Google'ın davranışının
DEĞİŞMEDİĞİNİ doğrular.

**Ön koşul**
- Örnek uygulama Google anahtarıyla çalışıyor.

**Adımlar**
1. Var olmayan bir model adıyla bir agent tanımı kaydet.
2. Akışlı çalıştır, `event: error` çerçevesini gözlemle.
3. Çalıştırma kaydını `GET /api/runs/{runId}` ile oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-bozuk-gemini-model",
  "instructions": "Test.",
  "model": { "provider": "google", "model": "gemini-olmayan-model-xyz" }
}'

curl -N -s -v -X POST "$APU/api/agents/manuel-bozuk-gemini-model/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"merhaba","sessionId":"gemini-404-test"}' 2>&1 | tail -40
```
```bash
curl -s "$APU/api/runs?agentName=manuel-bozuk-gemini-model" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- SSE bağlantısında `event: error` çerçevesi **görünür** (`ClientError`
  `HttpRequestException`'ın bir alt tipi olduğu için hem eski dar filtre hem
  yeni geniş `catch` bunu yakalar — Anthropic/OpenAI de artık aynı sonucu
  üretir, ama farklı bir düzeltmeyle).
- `GET /api/runs/{runId}` çıktısında `status: Failed`, `error.type` değeri
  `Google.GenAI.ClientError` içerir.
- `error.class` alanı — **kod-doğrulanmış şüphe**: Gemini'nin hata gövdesi
  genellikle `{"error":{"message":"...", ...}}` biçimindedir ve
  `HttpApiClient.ThrowFromErrorResponse` gövdeden okuduğu `message` alanını
  aynen kullanır (`"HTTP 404"` metni **içermez** — yalnız gövde boşsa
  `"Request failed with status code {kod}: {sebep}"` yedeğine düşer, o da
  `"HTTP"` sözcüğünü taşımaz). `error.type` da `httprequestexception` alt
  dizgisini taşımaz (`Google.GenAI.ClientError`). Bu yüzden `error.class`
  `Unknown` çıkabilir — SSE'nin `error` çerçevesi üretmesiyle **çelişmez**
  (biri akış katmanı, diğeri kayıt sınıflandırması), ama ikisinin birbirinden
  bağımsız davrandığını gösterir. Bu case gerçek değeri kaydeder.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 7 — Sağlayıcı sağlık denetimi

Denetim **ücret üretmez**: Anthropic `GET {endpoint}/models`'e,
Google `GET {endpoint}/{apiVersion}/models`'e gider, model çağrısı yapmaz.
Önbellek TTL'si ve bilinmeyen sağlayıcı için `404` davranışı OpenAI'de
kanıtlandı (MT-OAI-071/072) — burada **tekrarlanmaz**.

### MT-PROV-060 — Anthropic ve Google `Healthy` döner, farklı kimlik başlıkları kullanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama Anthropic ve Google anahtarlarıyla çalışıyor.

**Adımlar**
1. Tüm sağlayıcıların sağlığını oku.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `anthropic`: `status: Healthy`, `latency` doludur, `models` dizisi Anthropic
  tarafında yayınlanan gerçek model kimlikleriyle doludur (appsettings'teki üç
  model adıyla birebir aynı olmak **zorunda değildir** — katalog bir
  doğrulama listesi değildir, K-032).
- `google`: `status: Healthy`, `models` dizisindeki adlarda `models/` öneki
  **yoktur** (temizlenmiştir — `GoogleProviderHealthCheck.ReadModelIdsAsync`).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-061 — Erişilemeyen Anthropic adresi hata detayında adres veya anahtar sızdırmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-073 |

Negatif senaryo. **Geçici kod değişikliği.** Temsilci case: Google'ın sağlık
denetimi de birebir aynı `internal static Uri BuildModelsEndpoint` +
`try/catch (HttpRequestException)` desenini taşır (`GoogleProviderHealthCheck.cs`),
bu yüzden burada tekrarlanmaz.

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `agentPrism.UseAnthropic(anthropic);` satırından (yaklaşık 171. satır) hemen
   SONRA ekle: `agentPrism.UseAnthropic(o => { o.ApiKey = "sk-ant-cok-gizli-test-anahtari-12345"; o.Endpoint = new Uri("http://127.0.0.1:59999/"); });`
   — **dikkat:** bu ikinci çağrı `alreadyRegistered` kontrolü yüzünden yeni bir
   sağlayıcı kaydetmez, yalnız ayarları birleştirir (K-025); bu yüzden gerçek
   anahtarı **geçici olarak** yorum satırına alıp yalnız bu geçersiz adresi
   kullanan tek bir `UseAnthropic` çağrısı bırakmak gerekir.
2. Başlat, sağlığı sorgula.
3. Satırı kaldır, gerçek `UseAnthropic(anthropic);` çağrısını geri getir.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health/anthropic" -H "$APB" | python3 -m json.tool
```
```bash
# Temizlik:
git checkout -- samples/AgentPrism.Api/Program.cs
```

**Beklenen sonuç**
- `status: Unhealthy`.
- `detail` alanı `Baglanti hatasi (...)` biçimindedir; ne `sk-ant-cok-gizli-test-anahtari-12345`
  ne de `127.0.0.1:59999` adresi `detail` içinde görünür (`AnthropicProviderHealthCheck`
  yalnız `exception.HttpRequestError` kategori adını yazar, adres veya
  anahtarı **değil**).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — `secret` sızıntısı

`ApiKey` hiçbir çıktıda, hiçbir günlükte, hiçbir hata mesajında görünmemelidir
(K-059). MT-PROV-014 doğrulama mesajlarını zaten kapsadı; bu bölüm çalışma
zamanı uçlarını ve konsolu kapsar.

### MT-PROV-070 — API anahtarları hiçbir HTTP çıktısında görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-059 |

**Ön koşul**
- Örnek uygulama gerçek anahtarlarla çalışıyor.

**Adımlar**
1. Anahtarların görünebileceği tüm uçları tara.

**Girilecek veri**
```bash
ANTHROPIC_ANAHTAR=$(dotnet user-secrets list --project samples/AgentPrism.Api | grep "Anthropic:ApiKey" | cut -d= -f2 | tr -d ' ')
GOOGLE_ANAHTAR=$(dotnet user-secrets list --project samples/AgentPrism.Api | grep "Google:ApiKey" | cut -d= -f2 | tr -d ' ')

for uc in "/api/models" "/api/models/health" "/api/models/health/anthropic" "/api/models/health/google"; do
  echo "--- $uc ---"
  curl -s "$APU$uc" -H "$APB" | grep -c "$ANTHROPIC_ANAHTAR\|$GOOGLE_ANAHTAR" || echo "0 (temiz)"
done
```

**Beklenen sonuç**
- Her uç için sayım `0`'dır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-071 — `ConfigurationDiagnostic` yalnız çözülüp çözülmediğini taşır, DEĞER taşımaz (Anthropic + Google)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 26, 33 |
| **İlgili karar** | K-059, K-249 |

Bu ucun genel sözleşmesi `25-SAGLIK-TESHIS-OPENAPI.md`'nin konusudur; bu case
yalnız Anthropic/Google'a özgü iki davranışı doğrular: (1) her iki sağlayıcı
da `IModelProviderConfigurationDiagnostics` uygular ve `ApiKey` anahtarını
bildirir, (2) OpenAI'nin aksine (`openai` VE `openai-responses` aynı anahtarı
paylaştığı için tekilleştirilen tek girdi, MT-OAI-092) Anthropic ve Google
**birbirinden bağımsız** iki ayrı girdi üretir.

**Ön koşul**
- Örnek uygulama Anthropic ve Google anahtarlarıyla çalışıyor.

**Adımlar**
1. Raporu oku.

**Girilecek veri**
```bash
curl -s "$APU/api/diagnostics" -H "$APB" | tee /tmp/ap-prov-diag.json | python3 -m json.tool | grep -A4 -i "anthropic\|google"
```

**Beklenen sonuç**
- `configuration` dizisinde `key: "AgentPrism:Providers:Anthropic:ApiKey"`
  taşıyan **tam olarak bir** girdi vardır, `resolved: true`, `hint: null`.
- `configuration` dizisinde `key: "AgentPrism:Providers:Google:ApiKey"` taşıyan
  **tam olarak bir** girdi vardır, `resolved: true`, `hint: null`.
- `key` alanlarının hiçbiri gerçek anahtar dizgisini içermez.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-072 — Konsol günlüğünde API anahtarı görünmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 26 |
| **İlgili karar** | K-059 |

**Ön koşul**
- `appsettings.json`'daki `Logging:LogLevel:Default` `Information`'dır
  (varsayılan — değiştirilmez).

**Adımlar**
1. Uygulamayı konsol çıktısını bir dosyaya yönlendirerek başlat.
2. Birkaç çalıştırma yap (MT-PROV-021, MT-PROV-040, MT-PROV-050 tekrarlanabilir).
3. Dosyada anahtarları ara.

**Girilecek veri**
```bash
cd samples/AgentPrism.Api && dotnet run > /tmp/ap-prov-console.log 2>&1 &
sleep 5
# ... yukaridaki calistirmalar burada tekrarlanir ...
ANTHROPIC_ANAHTAR=$(dotnet user-secrets list --project samples/AgentPrism.Api | grep "Anthropic:ApiKey" | cut -d= -f2 | tr -d ' ')
GOOGLE_ANAHTAR=$(dotnet user-secrets list --project samples/AgentPrism.Api | grep "Google:ApiKey" | cut -d= -f2 | tr -d ' ')
grep -c "$ANTHROPIC_ANAHTAR\|$GOOGLE_ANAHTAR" /tmp/ap-prov-console.log || echo "0 (temiz)"
kill %1
```

**Beklenen sonuç**
- Sayım `0`'dır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Azure OpenAI

> ⏭ **ATLA — Azure kimliği yok.** `Sabit gerçekler` tablosuna göre bu ortamda
> gerçek bir Azure OpenAI kaynağı **yoktur**. Aşağıdaki her case yine de
> koddan doğrulanarak yazılmıştır — kullanıcı sonradan bir Azure kaynağı
> açarsa bu bölüm hazırdır. Her case'in kendi `⏭ ATLA` satırı vardır
> (`PROMPT.md` §6).

`AgentPrism.Azure` aynı `AgentPrism.OpenAI` boru hattından geçer
(`UseFunctionInvocation`, `OpenTelemetry`, devre kesici, içerik filtresi —
hepsi `ModelProviderRegistry` düzeyinde, bedava). Kendine özgü tek yüzey:
deployment≠model ayrımı, sıfır `ProviderSettings` ve iki kimlik doğrulama
yolu (API anahtarı / Microsoft Entra).

### MT-PROV-080 — `UseAzureOpenAI()` `azure-openai` adıyla kaydeder

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | K-210 |

**Ön koşul**
- Örnek uygulama gerçek bir Azure OpenAI kaynağı adresi ve anahtarıyla
  çalışıyor: `dotnet user-secrets set "AgentPrism:Providers:AzureOpenAI:Endpoint" "https://<kaynak>.openai.azure.com/"`
  ve `...:ApiKey`.

**Adımlar**
1. Kayıtlı model sağlayıcılarını listele.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Dizide `name` alanı `azure-openai` olan bir öge vardır (`azure` **değil** —
  paket ileride başka bir Azure model servisini de kaydedebilir, K-210).
- `models` dizisindeki girdilerin `name` alanı **deployment adıdır**
  (`uretim-gpt`), model adı değil.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-081 — `Endpoint` boşsa reddedilir

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | K-006 |

Negatif senaryo. Diğer iki sağlayıcıdan farklı olarak Azure'un **hiçbir**
genel adresi yoktur; bu yüzden `Endpoint` OpenAI/Anthropic/Google'da olduğu
gibi opsiyonel değil, **zorunlu**dur.

**Ön koşul**
- Uygulama durdurulmuş. `AgentPrism:Providers:AzureOpenAI:ApiKey` geçerli
  (gerçek veya biçimce geçerli) bir değer taşıyor.

**Adımlar**
1. `Endpoint`'i boş bırak (veya tanımlama).
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets remove "AgentPrism:Providers:AzureOpenAI:Endpoint" --project samples/AgentPrism.Api
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Mesaj `AzureOpenAIProviderOptions.Endpoint bos olamaz. Azure OpenAI'in tek
  bir genel adresi yoktur; her kaynagin kendi adresi vardir.` metnini taşır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-082 — `ApiKey` VE `CredentialFactory` ikisi de boşsa reddedilir

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | K-210 |

Negatif senaryo.

**Ön koşul**
- Uygulama durdurulmuş. `Endpoint` geçerli bir değer taşıyor.

**Adımlar**
1. `ApiKey`'i boş bırak; kodda `CredentialFactory` da verilmiyor (örnek
   uygulama varsayılanı).
2. Uygulamayı başlat.

**Girilecek veri**
```bash
dotnet user-secrets remove "AgentPrism:Providers:AzureOpenAI:ApiKey" --project samples/AgentPrism.Api
cd samples/AgentPrism.Api && dotnet run
```

**Beklenen sonuç**
- Uygulama başlamayı reddeder.
- Mesaj `AzureOpenAIProviderOptions.ApiKey veya
  AzureOpenAIProviderOptions.CredentialFactory doldurulmalidir.` ile başlar.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-083 — `CredentialFactory` verilmişse `ApiKey`'i ezer

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | K-210 |

`AzureOpenAIChatClientFactory.CreateClient` içinde `CredentialFactory`
kontrolü `ApiKey` kontrolünden **önce** gelir (`AzureOpenAIChatClientFactory.cs:149-157`) —
ikisi de verilmişse yönetilen kimlik kazanır (daha güvenli olan).

**Ön koşul**
- Gerçek bir Azure kaynağı ve `DefaultAzureCredential`'ın çözebileceği bir
  kimlik (örnek: `az login` yapılmış bir makine) mevcuttur.

**Adımlar**
1. Kodda hem `ApiKey` (bilerek **geçersiz** bir değer) hem
   `CredentialFactory = () => new DefaultAzureCredential()` veren bir kurulum
   yaz.
2. Gerçek bir çağrı yap.

**Girilecek veri**
```csharp
// Gecici test kurulumu (samples/AgentPrism.Api/Program.cs):
agentPrism.UseAzureOpenAI(o =>
{
    o.Endpoint = new Uri("https://<kaynaginiz>.openai.azure.com/");
    o.ApiKey = "gecersiz-anahtar-kasitli";
    o.CredentialFactory = static () => new Azure.Identity.DefaultAzureCredential();
    o.DefaultDeployment = "<deployment-adiniz>";
});
```

**Beklenen sonuç**
- Çağrı **başarılı** olur (Entra token'ı kullanıldı, geçersiz `ApiKey`
  hiç denenmedi) — bu, `ApiKey`'in sessizce ezildiğinin kanıtıdır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-084 — Deployment adı yerine MODEL adı verilirse `HTTP 404` döner

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | — |

Negatif senaryo, gerçek para harcar. README'nin `🚨 Deployment ≠ model`
uyarısının uçtan uca kanıtı: `ModelBinding.Model` bir model adı
(`gpt-5.4-mini`) değil, Azure kaynağında tanımlı bir **deployment** adı
taşımalıdır; istek yolu `POST {endpoint}/openai/deployments/{deployment}/chat/completions`
biçimindedir.

**Ön koşul**
- Örnek uygulama gerçek bir Azure OpenAI kaynağıyla çalışıyor.

**Adımlar**
1. `Model` alanına gerçek deployment adı YERİNE bilinen bir OpenAI model adı
   (`gpt-5.4-mini`) veren bir agent tanımı kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-azure-yanlis-ad",
  "instructions": "Test.",
  "model": { "provider": "azure-openai", "model": "gpt-5.4-mini" }
}'

curl -s -X POST "$APU/api/agents/manuel-azure-yanlis-ad/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- İstek Azure'dan `HTTP 404` alır (model adı deployment olarak var olmadığı
  için) — "model bulunamadı" değil, doğrudan **404**.
- `GET /api/runs/{runId}` çıktısında `status: Failed`.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-085 — `ProviderSettings` içinde HERHANGİ bir anahtar reddedilir

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | K-211 |

Negatif senaryo. `AzureOpenAIProviderNames.SupportedSettings` **boş bir
listedir** — Azure'un sohbet isteğine ek alan yazan tek yol
(`AzureChatExtensions`) kullandığımız OpenAI SDK sürümüyle çalışma anında
`MissingMethodException` verdiği için (ölçüldü, K-211) hiçbir ayar sunulmaz.

**Ön koşul**
- Örnek uygulama gerçek bir Azure OpenAI kaynağıyla çalışıyor.

**Adımlar**
1. `azure-openai` binding'ine herhangi bir `providerSettings` anahtarı koyarak
   doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-azure-ayar",
  "model": {
    "provider": "azure-openai",
    "model": "uretim-gpt",
    "providerSettings": { "azure-openai.herhangi": true }
  }
}'
```

**Beklenen sonuç**
- `valid` alanı `false`'tur, `code: invalid_setting`.
- Mesaj `su anahtarlar taninmiyor: azure-openai.herhangi.` ve `'azure-openai'
  saglayicisi hicbir ek ayar desteklemiyor.` metinlerini içerir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-086 — Sağlık denetimi model listesi döner, deployment listesi DEĞİL

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | — |

`GET {endpoint}/openai/models?api-version=2024-10-21` ucuna gider, **ücret
üretmez**. README'nin `🚨` uyarısı: dönen liste kaynağın erişebildiği
**modelleri** listeler, kaynakta tanımlı **deployment'ları değil** — denetimin
kanıtladığı şey adresin doğru, kimliğin geçerli ve kaynağın ayakta olduğudur;
deployment adının doğruluğu yalnız ilk gerçek çağrıda (MT-PROV-084 gibi)
anlaşılır.

**Ön koşul**
- Örnek uygulama gerçek bir Azure OpenAI kaynağıyla çalışıyor.

**Adımlar**
1. Sağlığı oku.

**Girilecek veri**
```bash
curl -s "$APU/api/models/health/azure-openai" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `status: Healthy`, `models` dizisi kaynağın **model** kimlikleriyle
  doludur — `uretim-gpt` (deployment adı) dizide **görünmeyebilir**, bu bir
  kusur değildir.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-087 — `azure-destek`: tool çağrısıyla uçtan uca çalıştırma

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | — |

Gerçek para harcar. `azure-destek` (Program.cs, satır ~563-577)
`get_order_status`/`list_recent_orders`/`cancel_order` tool'larını taşır —
diğer iki sağlayıcıyla (MT-PROV-040, MT-PROV-050) aynı fixture senaryosu.

**Ön koşul**
- Örnek uygulama gerçek bir Azure OpenAI kaynağıyla, geçerli bir
  `DefaultDeployment` ile çalışıyor.

**Adımlar**
1. `azure-destek`'i `FIX-PROMPT-01` metniyle çalıştır.
2. Çalıştırma kaydını oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/azure-destek/run" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"azure-e2e-01"}'
```
```bash
curl -s "$APU/api/runs/<runId>" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Yanıt metni `ORD-1001` dizgisini içerir.
- `status: Completed`, `get_order_status` tool'u tam bir kez çağrılır — Azure
  OpenAI SDK'sının mesaj eşlemesi, akışı ve tool çağrısı doğrudan OpenAI
  SDK'sından geldiği için (`Azure.AI.OpenAI` yalnız yönlendirme ekler) bu
  davranış `05-SAGLAYICI-OPENAI.md` `MT-OAI-040` ile **birebir aynı** olmalıdır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-PROV-088 — `secret` sızıntısı: `ApiKey` VE kurumsal `Endpoint` hiçbir çıktıda görünmez

⏭ **ATLA — Azure kimliği yok.**

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 27 |
| **İlgili karar** | K-059 |

Azure'a özgü ek risk: `Endpoint` bir sır değildir ama **kurumsal bir
topolojiyi açık eder** (kaynak adı) — bu yüzden hata mesajlarında ve sağlık
denetimi detayında **da** görünmemesi gerekir
(`AzureOpenAIProviderOptionsValidator.cs:38-42` yorumu).

**Ön koşul**
- Örnek uygulama gerçek bir Azure OpenAI kaynağıyla çalışıyor.

**Adımlar**
1. Anahtarın VE kaynak adresinin görünebileceği tüm uçları tara.

**Girilecek veri**
```bash
AZURE_ANAHTAR=$(dotnet user-secrets list --project samples/AgentPrism.Api | grep "AzureOpenAI:ApiKey" | cut -d= -f2 | tr -d ' ')
AZURE_ADRES=$(dotnet user-secrets list --project samples/AgentPrism.Api | grep "AzureOpenAI:Endpoint" | cut -d= -f2 | tr -d ' ')

for uc in "/api/models" "/api/models/health" "/api/models/health/azure-openai" "/api/diagnostics"; do
  echo "--- $uc ---"
  curl -s "$APU$uc" -H "$APB" | grep -c "$AZURE_ANAHTAR\|$AZURE_ADRES" || echo "0 (temiz)"
done
```

**Beklenen sonuç**
- Her uç için sayım `0`'dır. `/api/diagnostics`'teki `key` alanı yalnız
  `AgentPrism:Providers:AzureOpenAI:ApiKey` ayar **adını** taşır, ne anahtarı
  ne kaynak adresini.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bu dosyada kanıtlanmayan, kod okurken fark edilen şüpheler

Bu bölüm bir kusur listesi değildir — koşum aşamasında doğrulanacak
**şüphelerdir** (`PROMPT.md` §7.3). Ayrıntı ve tam gerekçe, ilgili case'in
"Beklenen sonuç" alanındadır; burada yalnız özetlenir:

- **MT-PROV-036/042 — SSE çerçeve kısmı DÜZELTİLDİ (2026-08-10).** Anthropic
  SDK istisnaları (`AnthropicApiException` ve alt sınıfları)
  `HttpRequestException`'dan türemez — `05`'in OpenAI için tespit ettiği SSE
  `error` çerçevesi boşluğu (K-296) Anthropic'te de geçerliydi;
  `AgentEndpoints`/`OpenAIResponsesEndpoints`/`OpenAIChatCompletionsEndpoints`'in
  dar `catch` filtreleri kaldırılarak düzeltildi. **Hâlâ açık:**
  `AnthropicApiException.Message`'ın `$"Status Code: {StatusCode}\n..."`
  biçimi (enum adı yazar, sayısal kod değil) `RunErrorClass` sınıflandırmasını
  (kayıt alanı, `DefaultRunErrorClassifier`) `Unknown`'a düşürebilir — bu,
  SSE çerçevesinden BAĞIMSIZ bir kayıt-sınıflandırma sorunudur, dokunulmadı.
- **MT-PROV-053**: Google SDK istisnaları (`Google.GenAI.ClientError`/`ServerError`)
  **`HttpRequestException`'dan türer** — SSE `error` çerçevesi fix'ten önce
  de üretiliyordu. Hata mesajı formatı yine de `RunErrorClass`'ı `Unknown`'a
  düşürebilir (MT-PROV-036/042 ile aynı, dokunulmamış sınıflandırma sorunu).
- **MT-PROV-034**: `HarmBlockThreshold.AllValues` listesinin gerçek üye
  sayısı ve tam adları (`BLOCK_LOW_AND_ABOVE`, `BLOCK_MEDIUM_AND_ABOVE`,
  `BLOCK_ONLY_HIGH`, `BLOCK_NONE`, `OFF`) `Google.GenAI.Types` paketinden
  alındı (XML doküman yorumundan değil, SDK'nin kendi tip tanımından); SDK
  sürümü yükseltilirse bu liste değişebilir.

SSE çerçeve boşluğu 2026-08-10'da kodda düzeltildi (bkz. `00-INDEKS.md` §8'in
başındaki özet); `RunErrorClass` sınıflandırma nüansı ile `HarmBlockThreshold`
listesi hâlâ **koşulmadı** — koşum aşaması bunları doğrular veya çürütür.
