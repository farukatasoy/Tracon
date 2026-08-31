# 27 — Model Yedek Zinciri ve Ön Uçuş Denetimi (`MYU`)

> **Alan kodu:** `MYU` · **Faz:** 62 tam kapsam · 81 (§ yanıt önbelleği ve eşzamanlı tool çağrısı) · 113 (§ `IProviderRetryClassifier` genişleme noktası) · 124 (§ yedeklemenin tool defteri)
>
> **Kaynak:**
> `src/AgentPrism.Abstractions/Agents/ModelBinding.cs` (`Fallbacks`, `ResponseCache`,
> `AllowConcurrentToolCalls`), `ModelFallback.cs`, `ResponseCacheSettings.cs` ·
> `src/AgentPrism.Abstractions/Models/ContextWindowEstimate.cs` ·
> `src/AgentPrism.Abstractions/Options/AgentPrismPreflightOptions.cs`,
> `AgentPrismModelConcurrencyOptions.cs` ·
> `src/AgentPrism.Core/Models/FallbackChatClient.cs`, `ProviderConcurrencyLimiter.cs`,
> `ContextWindowEstimator.cs`, `AgentPrismResponseCachingChatClient.cs`,
> `ModelProviderRegistry.cs` (yedek/eşzamanlılık/önbellek sarmalayıcıları ve
> `AllowConcurrentInvocation` bağlaması) · `src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs`
> (yalnız `BuildContextWindowStrategy`) ·
> `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs` (yalnız `EstimateAsync`,
> `/run`'daki `PreflightGate.CheckAsync` çağrısı) ·
> `src/AgentPrism.AspNetCore/RateLimiting/PreflightGate.cs`.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** koşulduğunda `kosumlar/<tarih>/27-MODEL-YEDEK-VE-ON-UCUS.md`
> altına yazılır. Bu dosya **spesifikasyondur** ve her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

```mermaid
flowchart TD
    A["POST /api/agents/name/run"] --> B{"Birincil saglayici cagirilir"}
    B -- "basarili" --> C["Yanit birincilden"]
    B -- "devre acik / 5xx / 429 / baglanti hatasi" --> D["Fallbacks 0 cagirilir"]
    D -- "basarili" --> E["Yanit yedekten<br/>ModelFallbackUsed olayi + span etiketi<br/>run.model_id yedegi yazar"]
    D -- "de dusuyor" --> F["Zincir tukendi<br/>ILK hata firlatilir, mesaj denenen saglayicilari sayar"]
    B -- "401/403 veya iptal" --> G["ANINDA firlatilir<br/>yedek HIC denenmez"]

    H["POST /api/agents/name/estimate"] --> I["Sayim yapilir<br/>saglayiciya HIC istek gitmez"]

    J["POST /api/agents/name/run<br/>Preflight.Enabled=true"] --> K{"Tahmini istem<br/>izin verilen payi asiyor mu"}
    K -- "evet" --> L["400 - saglayiciya cagri YAPILMAZ"]
    K -- "hayir" --> B

    M["POST /api/agents/name/run<br/>ResponseCache.Enabled=true"] --> N{"Onbellek anahtari<br/>(kiraci+saglayici+tool kumesi) isabet mi"}
    N -- "isabet" --> O["Saglayiciya HIC gitmez<br/>usage: null (K-557) - tool cagrisi yine CALISIR"]
    N -- "iska" --> B

    style F fill:#7a4a1f,stroke:#3d250f,color:#ffffff
    style G fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style L fill:#7a4a1f,stroke:#3d250f,color:#ffffff
    style O fill:#1f4a7a,stroke:#0f253d,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Devre kesicinin kendi durum makinesi (`Closed`/`Open`/`HalfOpen`), `AgentPrismProviderUnavailableException` sözleşmesi | `08-SAGLAYICI-GENISLEMESI.md` (zaten üretildi) — burada yalnız devrenin AÇIK olması yedek zincirini TETİKLEYEN bir girdi olarak kullanılır, devrenin kendisi tekrar test edilmez |
| Model sağlık ucu, sağlayıcı kaydı, `ProviderSettings` sözleşmesi | `08`, `25-SAGLIK-TESHIS-OPENAPI.md` (zaten üretildi) |
| `RunStatistics`/`/api/stats` panosunun genel şekli, maliyet hesaplama kuralları | `12-GOZLEMLENEBILIRLIK-MALIYET.md` (zaten üretildi) — burada yalnız `ByModel` kırılımının yedek modeli GÖSTERDİĞİ doğrulanır |
| `ContextWindow` sıkıştırma stratejisinin kendi çalışma zamanı davranışı (turuncu/kırmızı eviction) | `13-BAGLAM-SIKISTIRMA-VE-BELLEK.md` (zaten üretildi) — burada yalnız `MaxContextWindowTokens`'ın TÜRETİLMESİ test edilir, sıkıştırmanın kendisi değil |
| Agent düzenleyicinin genel form davranışı (kaydet/doğrula/geri yükle) | `10-ARAYUZ-AGENT-PLAYGROUND.md` (zaten üretildi) — burada yalnız yedek listesi alanı EKLENİR |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama en az iki sağlayıcıyla çalışır: gerçek `OpenAI` (birincil) ve
   `OpenAICompatible("flaky", ...)` — kasıtlı olarak DİNLEMEYEN bir porta
   (`http://localhost:1/v1`) işaret eden ikinci bir kayıt. `flaky` §1'in
   "devre açık" senaryosunu **gerçek bir bağlantı hatasıyla** (retryable —
   `HttpRequestException` ailesi) tetiklemek için kullanılır; sahte bir 401
   kullanılmaz çünkü kimlik doğrulama hataları BİLEREK yedeği tetiklemez
   (K-449).
   ```bash
   dotnet user-secrets set "AgentPrism:Providers:OpenAI:ApiKey" "<gercek anahtar>"
   ```
   `flaky` kaydı ve iki agent (`birincil-kirik` → `flaky` birincil + gerçek
   `openai` yedek; `birincil-saglam` → gerçek `openai` birincil, `Fallbacks`
   boş) `samples/AgentPrism.Api/Program.cs`'e geçici olarak eklenir; koşum
   sonunda geri alınır.
3. `CircuitBreaker:FailureThreshold` bu tur için `1`'e indirilir (tek
   denemede devrenin açılmasını görmek için):
   ```bash
   dotnet user-secrets set "AgentPrism:CircuitBreaker:FailureThreshold" "1"
   ```
   Dosyanın sonunda geri alınır: `dotnet user-secrets remove "AgentPrism:CircuitBreaker:FailureThreshold"`.
4. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
```

> **Gerçek para uyarısı.** `MT-MYU-002`, `003`, `004`'ün başarılı yolu gerçek
> `openai`'ye bir çağrı yapar (kısa mesaj, ucuz). `MT-MYU-001`, `005`, `006`,
> `007` de gerçek bir çağrı yapar (kısa mesaj). `MT-MYU-008`/`009` model
> çağırmaz, yalnız derleme kontrolüdür. `MT-MYU-010`/`011`/`012` de kısa
> mesajlarla gerçek çağrı yapar — ama her birinin İKİNCİ (isabet eden) çağrısı
> modele HİÇ gitmez, yalnız İLK çağrı ücretlidir. `MT-MYU-013` model çağırmaz.

---

### MT-MYU-001 — `Fallbacks` boş: geçersiz anahtarla çalıştırma bugünkü hatayı birebir korur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | K1 |

**Ön koşul**
- `birincil-saglam` agent'ı (gerçek `openai`, `Fallbacks: []`).

**Adımlar**
1. `AgentPrism:CircuitBreaker:FailureThreshold=1` iken `openai`'ye geçersiz
   bir API anahtarıyla bir çağrı tetikle (devreyi aç).
2. Aynı agent'a ikinci bir çağrı yap.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/birincil-saglam/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-myu-001-a' \
  -d '{"message":"merhaba"}'
sleep 1
curl -s -o /dev/null -w '%{http_code}\n' -X POST "$APU/api/agents/birincil-saglam/run" \
  -H "$APB" -H 'content-type: application/json' -H 'Idempotency-Key: mt-myu-001-b' \
  -d '{"message":"merhaba tekrar"}'
```

**Beklenen sonuç**
- Adım 2: devre kesici zaten açık; `AgentPrismProviderUnavailableException`
  kaynaklı bir hata döner (Faz 8'in bugünkü davranışı — `Fallbacks` boş
  olduğu için `FallbackChatClient` hiç devrede değildir).

---

### MT-MYU-002 — 🚨 Birincilin devresi açıkken yedek devreye girer; `run` kaydı VE `ByModel` yedek modeli gösterir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | K-320, K-443, K-447 |

**Ön koşul**
- `birincil-kirik` agent'ı (`flaky` birincil, gerçek `openai` yedek).
- `flaky`'nin devresi kapalı (tur başında; MT-MYU-001'in etkisi farklı bir
  sağlayıcı adına ait, bunu etkilemez).

**Adımlar**
1. `birincil-kirik`'e bir mesaj gönder (bağlantı hatası → devre açılır → aynı
   çağrı içinde yedeğe düşer).
2. Yanıtın geldiği `runId`'yi al, `GET /api/runs/{runId}`.
3. `GET /api/runs/{runId}/events` — `ModelFallbackUsed` olayını ara.
4. `GET /api/stats?agentName=birincil-kirik`.

**Girilecek veri**
```bash
RESP=$(curl -s -X POST "$APU/api/agents/birincil-kirik/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-myu-002' \
  -d '{"message":"merhaba"}')
echo "$RESP"
RUN_ID=$(echo "$RESP" | python3 -c "import json,sys;print(json.load(sys.stdin)['runId'])")
curl -s "$APU/api/runs/$RUN_ID" -H "$APB"
curl -s "$APU/api/runs/$RUN_ID/events" -H "$APB"
curl -s "$APU/api/stats?agentName=birincil-kirik" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `200`, yanıt gerçek modelden gelir (istek başarıyla tamamlanır —
  kullanıcı devrenin açıldığını FARK ETMEZ).
- Adım 2: `run.modelId` **gerçek `openai` modelinin adını** taşır, `flaky`'nin
  yer tutucu modelini DEĞİL.
- Adım 3: bir `ModelFallbackUsed` olayı VARDIR; `text` alanı
  `openai/{model}` biçimindedir, `payload` birincil ve yedek ikilisini taşır.
- Adım 4: `byModel` kırılımında yedek modelin adı görünür; `flaky`'nin yer
  tutucu modeli GÖRÜNMEZ (hiç başarılı çağrı yapmadı).

---

### MT-MYU-003 — Maliyet raporu yedek modelin fiyatıyla hesaplanır, birincilin fiyatıyla DEĞİL

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | K-447 |

**Ön koşul**
- MT-MYU-002'nin çıktısı; `AgentPrism:Pricing:{flaky-adı}:{model}` ile
  `AgentPrism:Pricing:openai:{gercek model}` FARKLI birim fiyatlarla
  yapılandırılmış (tur başında).

**Adımlar**
1. `GET /api/runs/{runId}` ile MT-MYU-002'nin çalıştırmasının maliyetini oku.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/$RUN_ID" -H "$APB" | python3 -c "import json,sys;r=json.load(sys.stdin);print(r['cost'])"
```

**Beklenen sonuç**
- `cost.inputCost`/`outputCost`, `openai:{gercek model}` için yapılandırılan
  birim fiyatla tutarlıdır; `flaky` için yapılandırılan fiyatla DEĞİL.

---

### MT-MYU-004 — Zincirin tamamı düşerse hata mesajı denenen sağlayıcıları sayar; İLK hatayı yansıtır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | K-449 |

**Ön koşul**
- `ikisi-de-kirik` agent'ı (`flaky` birincil, ikinci bir `flaky2` — o da
  dinlemeyen bir porta işaret eder — yedek).

**Adımlar**
1. `ikisi-de-kirik`'e bir mesaj gönder.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents/ikisi-de-kirik/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-myu-004' \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- Hata döner (`errorType: provider_unavailable`); hata mesajı `flaky` VE
  `flaky2` adlarının İKİSİNİ de içerir ("tried: ...").
- Mesaj birincilin (`flaky`) hatasını yansıtır — ikinci halkanın (`flaky2`)
  kendi hata metnini DEĞİL (ikisi de bağlantı hatası olduğu için metinler
  aslında benzer olabilir; asıl kanıt sunucu loglarında/kodda "ilk hata"
  sırasının izlenmesidir, bkz. `FallbackChatClientTests.Exhausted_chain_throws_the_first_failure_not_the_last`
  birim testi).

---

### MT-MYU-005 — Ön uçuş kapalıyken (varsayılan) pencereden büyük bir istem sağlayıcıdan hata alır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | K1, K-446 |

**Ön koşul**
- `AgentPrism:Preflight:Enabled` ayarlanMAMIŞ (varsayılan `false`).
- `birincil-saglam` agent'ı.

**Adımlar**
1. Modelin gerçek bağlam penceresini aşacak kadar uzun bir istem gönder
   (`python3 -c "print('kelime ' * 200000)"`).

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents/birincil-saglam/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-myu-005' \
  -d "{\"message\":\"$(python3 -c "print('kelime ' * 200000)")\"}"
```

**Beklenen sonuç**
- İstek sağlayıcıya GİDER (loglarda gerçek bir HTTP çağrısı görünür); hata
  varsa sağlayıcının kendi `context_length_exceeded` benzeri hatasıdır,
  AgentPrism'in ürettiği bir `400` DEĞİLDİR.

---

### MT-MYU-006 — Ön uçuş açıkken aynı istem sağlayıcıya HİÇ gitmeden `400` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | K-446 |

**Ön koşul**
```bash
dotnet user-secrets set "AgentPrism:Preflight:Enabled" "true"
```
(Örnek uygulama yeniden başlatılır.)

**Adımlar**
1. MT-MYU-005'in AYNI uzun istemini gönder.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents/birincil-saglam/run" -H "$APB" \
  -H 'content-type: application/json' -H 'Idempotency-Key: mt-myu-006' \
  -d "{\"message\":\"$(python3 -c "print('kelime ' * 200000)")\"}"
```

**Beklenen sonuç**
- `400`; gövde `promptTokens`, `contextWindowTokens`, `allowedPromptTokens`
  alanlarını taşır ve `promptTokens > allowedPromptTokens`'tır.
- Sağlayıcı loglarında bu istek için YENİ bir HTTP çağrısı YOKTUR.

---

### MT-MYU-007 — `POST /estimate` sayı döner, sağlayıcıya HİÇ istek gitmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | — |

**Ön koşul**
- `birincil-saglam` agent'ı. `Preflight:Enabled` bu case için ÖNEMSİZ —
  uç her durumda çalışır.

**Adımlar**
1. Kısa bir mesajla tahmin iste.
2. Uzun (MT-MYU-005'in) mesajıyla tekrar iste.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/birincil-saglam/estimate" -H "$APB" \
  -H 'content-type: application/json' -d '{"message":"merhaba"}'
curl -s -X POST "$APU/api/agents/birincil-saglam/estimate" -H "$APB" \
  -H 'content-type: application/json' \
  -d "{\"message\":\"$(python3 -c "print('kelime ' * 200000)")\"}"
```

**Beklenen sonuç**
- Adım 1: `wouldBeRejected: false`, küçük bir `promptTokens`.
- Adım 2: `wouldBeRejected: true` (model üstverisi bir pencere veriyorsa) veya
  `contextWindowTokens: null` (vermiyorsa) — ikisinde de sağlayıcı loglarında
  YENİ bir çağrı YOKTUR.

---

### MT-MYU-008 — `MaxContextWindowTokens` boş, model üstverisi dolu: `ContextWindow` sıkıştırmalı agent kaydı GEÇER

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | — |

**Ön koşul**
- Kullanılacak model, sağlayıcının `Models` kataloğunda `ContextWindowTokens`
  DOLU bir kayıt olarak var (ör. `OpenAIModelCatalog`'un varsayılan girdisi).

**Adımlar**
1. `Compaction.Strategy = "ContextWindow"`, `MaxContextWindowTokens` GÖNDERİLMEDEN
   bir agent kaydet.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents" -H "$APB" -H 'content-type: application/json' \
  -d '{
    "name": "baglam-turetilen",
    "instructions": "Kisa yanit ver.",
    "model": {"provider": "openai", "model": "<katalogda pencereli bir model>"},
    "compaction": {"strategy": "ContextWindow"}
  }'
```

**Beklenen sonuç**
- `201`; derleme BAŞARILIDIR (`400` DEĞİL). Agent çalıştırılabilir durumdadır.

---

### MT-MYU-009 — İkisi de boş: anlaşılır hata hangi iki alandan birinin doldurulacağını söyler

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 62 |
| **İlgili karar** | — |

**Ön koşul**
- Kullanılacak model, sağlayıcının `Models` kataloğunda YOK (`ContextWindowTokens`
  bilinmiyor) — ör. rastgele bir model adı.

**Adımlar**
1. Aynı kaydı, katalogda OLMAYAN bir modelle dene.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents" -H "$APB" -H 'content-type: application/json' \
  -d '{
    "name": "baglam-eksik",
    "instructions": "Kisa yanit ver.",
    "model": {"provider": "openai", "model": "katalogda-olmayan-model-adi"},
    "compaction": {"strategy": "ContextWindow"}
  }'
```

**Beklenen sonuç**
- `400`; hata mesajı hem `MaxContextWindowTokens`'ı hem modelin katalogda
  bulunamadığını AÇIKÇA adlandırır (`AgentDefinitionCompiler`'ın
  `BuildContextWindowStrategy` mesajı).

---

### MT-MYU-010 — 🚨 Aynı istem iki kez sorulunca ikinci `run` modele ÇIKMAZ; ama önbellekteki tool çağrısı yine ÇALIŞIR

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 81 |
| **İlgili karar** | K-551, K-552, K-557 |

**Ön koşul**
- `cached-support` agent'ı (`samples/AgentPrism.Api`'de kalıcı olarak tanımlı;
  `ResponseCache.Enabled = true`, `Lifetime = 10 dk`). `AddDistributedMemoryCache()`
  zaten kayıtlıdır.

**Adımlar**
1. `cached-support`'a bir sipariş sorusu gönder (`orderId` her seferinde
   AYNI olsun ki tool sonucu da AYNI kalsın — K-557'nin dersi budur).
2. Aynı istemi TEKRAR gönder.
3. `GET /api/runs?agentName=cached-support&limit=2` ile son iki `run`'ı karşılaştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/cached-support/run" -H "$APB" \
  -H 'content-type: application/json' -d '{"message":"What is the status of order 77?"}'
curl -s -X POST "$APU/api/agents/cached-support/run" -H "$APB" \
  -H 'content-type: application/json' -d '{"message":"What is the status of order 77?"}'
curl -s "$APU/api/runs?agentName=cached-support&limit=2" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç (2026-08-22'de ölçüldü — gerçek OpenAI çağrısıyla)**
- Adım 1'in akışında bir `functionCall` (`get_order_status`, `orderId: "77"`),
  bir `functionResult` ve İKİ `usage` bloğu (turn başına bir tane) vardır.
- Adım 2'nin akışında AYNI `functionCall`/`functionResult` çifti YİNE vardır
  (isabet eden yanıt tool çağrısını taşımaya devam eder — 81.1) ama **hiçbir
  `usage` bloğu YOKTUR**.
- Adım 3: en yeni `run`'ın `"usage"` alanı **`None`/`null`**dur (K-557 —
  `0` değil `null`: "ölçülmedi", "sıfır harcandı" değil); bir önceki `run`'ın
  `usage.totalTokens`'ı gerçek bir sayıdır (ör. `244`).

---

### MT-MYU-011 — Farklı tool kümesine sahip iki agent aynı önbellek kaydını PAYLAŞMAZ

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 81 |
| **İlgili karar** | K-551 |

**Ön koşul**
- `cached-support` (`ToolNames: [get_order_status, list_recent_orders]`,
  `ResponseCache.Enabled = true`) ve aynı talimatı/modeli taşıyan ama
  **farklı** (veya boş) bir tool kümesine sahip geçici bir ikinci agent —
  `samples/AgentPrism.Api/Program.cs`'e geçici eklenir:
  ```csharp
  .AddAgent(new AgentDefinition
  {
      Name = "cached-support-notools",
      Instructions = "You are a support assistant. Answer briefly and clearly. " +
                     "Always use a tool for order questions.",
      Model = model with { ResponseCache = new ResponseCacheSettings { Enabled = true } },
  })
  ```

**Adımlar**
1. `cached-support`'a bir soru sor (tool çağrısı üretir, önbelleğe yazılır).
2. `cached-support-notools`'a AYNI soruyu sor (aynı talimat, aynı model, **tool
   YOK**).

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/cached-support/run" -H "$APB" \
  -H 'content-type: application/json' -d '{"message":"What is the status of order 88?"}'
curl -s -X POST "$APU/api/agents/cached-support-notools/run" -H "$APB" \
  -H 'content-type: application/json' -d '{"message":"What is the status of order 88?"}'
```

**Beklenen sonuç**
- İkinci agent'ın yanıtında `get_order_status` `functionCall`'ı GÖRÜNMEZ
  (agent'ın hiç ToolNames'i yok, model onu göremez) — ama daha önemlisi,
  ikinci çağrı GERÇEKTEN modele gider (isabet ETMEZ): akışında en az bir
  `usage` bloğu vardır. Eğer anahtar tool kümesini yok sayıyor olsaydı,
  ikinci agent birincinin (tool çağrısı taşıyan) kaydına düşer ve bu bir
  yetki sızıntısı olurdu.

---

### MT-MYU-012 — Başka kiracının önbelleklenmiş yanıtı GÖRÜNMEZ

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 81 |
| **İlgili karar** | K-551 |

**Ön koşul**
- Çok kiracılılık açık (`AgentPrism:Tenancy:Enabled=true`,
  `AllowHeaderResolution=true` — bkz. `13-KIRACI-VE-GUVENLIK.md`).
- `cached-support` her iki kiracıda da erişilebilir (kod tanımlı agent).

**Adımlar**
1. Kiracı `acme` başlığıyla bir soru sor.
2. Kiracı `beta` başlığıyla AYNI soruyu sor.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/cached-support/run" -H "$APB" \
  -H 'X-AgentPrism-Tenant: acme' -H 'content-type: application/json' \
  -d '{"message":"What is the status of order 99?"}'
curl -s -X POST "$APU/api/agents/cached-support/run" -H "$APB" \
  -H 'X-AgentPrism-Tenant: beta' -H 'content-type: application/json' \
  -d '{"message":"What is the status of order 99?"}'
```

**Beklenen sonuç**
- İkinci çağrı (kiracı `beta`) de GERÇEKTEN modele gider — akışında bir
  `usage` bloğu vardır. Kiracı `acme`'nin kaydı `beta`'ya SIZMAZ.

---

### MT-MYU-013 — `ResponseCache.Enabled` açıkken `IDistributedCache` kayıtlı değilse anlaşılır bir hata döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 81 |
| **İlgili karar** | K-556 |

**Ön koşul**
- `samples/AgentPrism.Api/Program.cs`'deki `builder.Services.AddDistributedMemoryCache();`
  satırı GEÇİCİ olarak yorum satırına alınır, uygulama yeniden başlatılır.

**Adımlar**
1. `ResponseCache.Enabled: true` taşıyan bir tanımı doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H 'content-type: application/json' \
  -d '{
    "name": "would-be-cached",
    "instructions": "hi",
    "model": {"provider": "openai", "model": "gpt-5.4-mini", "responseCache": {"enabled": true}}
  }' | python3 -m json.tool
```

**Beklenen sonuç**
- `messages` dizisinde tek bir kayıt: `"code": "invalid_setting"`,
  `"path": "model.providerSettings"`, `"message"` alanı `IDistributedCache`'i
  ve `AddDistributedMemoryCache()` örneğini AÇIKÇA adlandırır (planın
  öngördüğü `compilation_error` DEĞİL — K-556).
- Koşumdan sonra `AddDistributedMemoryCache()` satırının yorumu KALDIRILIR.

---

### MT-MYU-014 — 👤 `AllowConcurrentToolCalls` kapalıyken (varsayılan) davranış bugünküyle birebir aynıdır

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 81 |
| **İlgili karar** | K-553 |

**Ön koşul**
- `support` agent'ı (Faz 5'ten beri var, `AllowConcurrentToolCalls` hiç
  ayarlanmaz → varsayılan `false`).

**Adımlar**
1. `support`'a birden çok tool gerektirebilecek bir istem gönder (ör. birden
   fazla sipariş sorgusu).

**Beklenen sonuç**
- Kayıtlar, metrikler ve nihai yanıt Faz 80 öncesiyle birebir aynıdır — bu
  case yeni bir davranış KANITLAMAZ, yalnız regresyon yoktur der.

> **Not — gerçek eşzamanlılığın otomatikleştirilmiş kanıtı ayrıdır.** Üç
> bağımsız tool'un GERÇEKTEN çakıştığı (`Barrier` ile garanti edilen) ve
> her birinin kaydının/yetkilendirme kararının doğru çağrıya bağlandığı
> iddiası gerçek bir LLM ile YENİDEN ÜRETİLEMEZ (model üç tool'u aynı turda
> çağırıp çağırmayacağına kendi karar verir, bu davranış zorlanamaz).
> Bu iddia `tests/AgentPrism.AspNetCore.FunctionalTests/ConcurrentToolInvocationTests.cs`
> tarafından **her koşumda garantili** kanıtlanır (sahte model üç çağrıyı TEK
> bir turda üretir, `Barrier(3)` üç gövdenin GERÇEKTEN aynı anda çalıştığını
> zorlar). El ile koşulacak tek şey budur: bu üç testin GERÇEKTEN geçtiğini
> doğrulamak yeter, ayrı bir manuel case gerekmez.

---

### MT-MYU-015 — 🚨 `IProviderRetryClassifier` kayıtlı değilken (veya `Unknown` dönerken) yedek zincir bugünküyle birebir aynıdır — ve yedek GERÇEKTEN doğru modeli çağırır

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 113 |
| **İlgili karar** | K1 |

**Ön koşul**
- Birincili sürekli bağlantı reddiyle düşen (`http://localhost:1/v1`), modeli
  birincilden **FARKLI** bir gerçek `openai` modeline düşen geçici bir agent
  (`Fallbacks` alanında `ModelFallback.Model` birincininkinden farklı).
- Hiçbir `IProviderRetryClassifier` kayıtlı DEĞİL (varsayılan) VEYA kayıtlı
  olan her zaman `Unknown` dönüyor.

**Adımlar**
1. Agent'a bir mesaj gönder.
2. `GET /api/runs/{runId}` ile yanıtı ve `modelId`'yi doğrula.

**Beklenen sonuç (2026-08-26'da ölçüldü — gerçek OpenAI çağrısıyla, `samples/AgentPrism.Api`)**
- Bağlantı hatası (`Connection refused`) yedek zincire düşürür; yanıt
  **gerçek** yedek modelden gelir (`"Hi"`), birincinin yer tutucu modelinden DEĞİL.
- 🚨 Bu case bir regresyonu YAKALADI ve düzeltmeyi doğruladı: yedek çağrısı
  **kendi** `ModelFallback.Model` adını taşımalıdır — birincinin `ChatOptions.ModelId`'sini
  DEĞİL. Düzeltmeden önce ölçüldü: yedek gerçek `openai`'a **birincinin** yer
  tutucu model adıyla gitti ve sunucu `HTTP 404 (model_not_found)` döndürdü,
  zincir TAMAMEN tükendi ("All providers in the fallback chain failed").
  Kök sebep ve düzeltme: `docs/hafiza/model-boru-hatti.md` §
  "`ChatOptions.ModelId` yedek bağlıya sızar". Regresyon:
  `FallbackChatClientTests.Fallback_link_is_called_with_its_own_ModelId_not_the_primarys`.

---

### MT-MYU-016 — `IProviderRetryClassifier` `DoNotRetry` dönerse yedek zincire HİÇ geçilmez

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 113 |
| **İlgili karar** | K1 |

**Ön koşul**
- MT-MYU-015'in AYNI kurulumu (birincili sürekli bağlantı reddiyle düşen agent).
- Kayıtlı bir `IProviderRetryClassifier`, bağlantı reddi hatalarında
  `ProviderRetryDecision.DoNotRetry` döndürüyor.

**Adımlar**
1. Aynı agent'a bir mesaj gönder.

**Beklenen sonuç (2026-08-26'da ölçüldü)**
- Hata **ANINDA** yüzeye çıkar (`errorType: upstream_error`,
  `"The model provider request failed."`); yedek bağ HİÇ denenmez — yerleşik
  kuralın "bağlantı hatası retry'a girer" kararı tüketicinin `DoNotRetry`
  kararıyla geçersiz kılınır.

---

### MT-MYU-017 — 👤 Yedeğe geçerken tamamlanmış bir tool çağrısı tekrar ÇALIŞMAZ

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 124 |
| **İlgili karar** | K-1 |

**Ön koşul** — bu case gerçek bir `openai` ile **elle** yeniden üretilemez:
zincirin, bir tool zaten çalıştıktan **sonra ve tam o turda** kesintiye
uğraması gerekir — gerçek bir sağlayıcının tam o anda çökmesi zorlanamaz
(`http://localhost:1/v1` gibi baştan ölü bir bağlantı bunu SAĞLAMAZ: o
kurulumda birincil hiçbir zaman bir tool çağırmaz, kesinti ilk çağrıda olur).

**Bu iddianın otomatikleştirilmiş kanıtı ayrıdır.** Aynı MT-MYU-014'ün
eşzamanlı tool çağrısı için yaptığı gerekçe burada da geçerlidir:
`tests/AgentPrism.AspNetCore.FunctionalTests/FallbackToolSideEffectTests.cs`
sahte bir sağlayıcı çifti kullanır (biri tool'u çağırıp SONRA düşer, öbürü
AYNI tool çağrısını tekrar sorar) ve gerçek `[AgentPrismTool]` gövdesinin
kaç kez çalıştığını sayar — bu, her koşumda **garantili** ve deterministik
tekrarlanır; gerçek bir LLM'in aynı turda aynı tool'u tekrar sorup
sormayacağı zorlanamaz. El ile koşulacak tek şey, bu üç testin
(`A_side_effecting_tool_runs_once_even_though_the_fallback_asks_for_it_again`,
`Falling_back_once_does_not_leave_the_compiled_agents_options_permanently_wrapped`,
`Two_concurrent_tool_calls_both_answered_correctly_when_the_fallback_repeats_them`)
GERÇEKTEN geçtiğini doğrulamaktır:

```bash
./artifacts/bin/AgentPrism.AspNetCore.FunctionalTests/release/AgentPrism.AspNetCore.FunctionalTests \
  --filter-method "*FallbackToolSideEffect*"
```

**Beklenen sonuç**
- Üç testin üçü de yeşildir. İlki tool gövdesinin **bir kez** çalıştığını ve
  `ModelFallbackUsed` olayının gerçekten üretildiğini kanıtlar (yedeğin
  hiç tetiklenmediği bir sahte-pozitiften ayırmak için); ikincisi önbelleklenmiş
  (`CompiledAgentCache`) agent'ın aynı sunucuda yapılan sonraki, yedeksiz bir
  çalıştırmada kalıcı olarak sarmalanmadığını; üçüncüsü `AllowConcurrentToolCalls`
  açıkken iki eşzamanlı çağrının defterini bozmadan ikisinin de doğru
  cevaplandığını doğrular.

---

## Koşumdan sonra

1. `dotnet user-secrets remove "AgentPrism:CircuitBreaker:FailureThreshold"`
2. `dotnet user-secrets remove "AgentPrism:Preflight:Enabled"`
3. `samples/AgentPrism.Api/Program.cs`'e eklenen `flaky`/`flaky2` kayıtları ve
   `birincil-kirik`/`ikisi-de-kirik`/`baglam-turetilen`/`baglam-eksik`
   agent'ları geri alınır.
4. MT-MYU-011'in geçici `cached-support-notools` agent'ı geri alınır.
   `cached-support` ve `AddDistributedMemoryCache()` KALICIDIR, geri alınmaz.
5. MT-MYU-013'ün yorumladığı `AddDistributedMemoryCache()` satırı geri açılır.
6. MT-MYU-015/016'nın geçici `IProviderRetryClassifier` kaydı, `retry-seam-demo`
   agent'ı ve `flaky-113` sağlayıcı kaydı `samples/AgentPrism.Api/Program.cs`'ten
   geri alınır; `dotnet user-secrets remove "AgentPrism:Demo:RetryClassifierBlocksConnectionFailures"`.
6. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı tekrar uygulanır.
