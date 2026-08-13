# 08 — OpenAI Uyumlu Uçlar (`COMPAT`)

> **Alan kodu:** `COMPAT` · **Faz:** 50
> **Kaynak:** `src/AgentPrism.AspNetCore/OpenAICompat/` — `OpenAIChatCompletionsEndpoints.cs`
> (`/v1/chat/completions`), `OpenAIResponsesEndpoints.cs` (`/v1/responses`),
> `OpenAIConversationsEndpoints.cs` (`/v1/conversations*`), `OpenAICompatSupport.cs`
> (ortak yardımcılar), `AttachmentIngestion.cs` (gömülü `data:` URI → ek).
>
> 🚨 **00-INDEKS.md düzeltmesi.** Durum tablosundaki kaynak sütunu bu dosya için
> `src/AgentPrism.AspNetCore` (`v1/*`) yazıyordu. Böyle bir `v1/` klasörü **yoktur**
> — gerçek klasör adı `OpenAICompat/`'tır (`v1/*` yalnızca HTTP yol önekidir, dosya
> yolu değil). Ölçüldü: `find src/AgentPrism.AspNetCore -iname "*v1*"` boş sonuç
> döndü; asıl dosyalar `grep -rl "chat/completions" src/AgentPrism.AspNetCore`
> ile bulundu. 00-INDEKS.md §7 tablosu bu üretimle birlikte düzeltildi.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

---

## Bu dosya neyi kanıtlar

`app.MapAgentPrism()`'in kurduğu üç OpenAI-uyumlu uç: durumsuz `/v1/chat/completions`,
oturum destekli `/v1/responses`, konuşma yaşam döngüsü `/v1/conversations*`. Bu uçlar
yönetim API'sinden (`/api/*`, dosya 07) **ayrı bir sözleşmedir**: `ProblemDetails`
kullanmazlar, OpenAI'nin kendi `{"error":{"message":...}}` zarfını kullanırlar (K-038),
ve amaçları stok OpenAI SDK'larının (Python, Node, vb.) `base_url` değiştirerek
AgentPrism'e doğrudan bağlanabilmesidir.

```mermaid
flowchart TD
    A["POST /agentprism/v1/chat/completions"] --> B["model veya metadata.entity_id -> agent"]
    B --> C["agent.RunAsync(messages, session: null)"]
    C --> D["ChatCompletion govde — DURUMSUZ, oturum acilmaz"]

    E["POST /agentprism/v1/responses"] --> F["model veya metadata.entity_id -> agent"]
    F --> G["conversation / previous_response_id -> AgentSessionStore"]
    G --> H["OpenAIResponses.WriteResponse govde"]

    I["POST /agentprism/v1/conversations"] --> J["conv_ + AgentPrismId — KIMLIK REZERVASYONU"]
    J -.->|conversation alaninda kullanilir| G
    H -.->|conversation = oturum kimligi, K-043| J
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Yönetim API'si (`/api/*`) genel sözleşmesi, `ProblemDetails` | [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) (zaten üretildi) |
| Loopback/uzak erişim derinliği, API anahtarı kapsamları, rol politikası kombinasyonları, kiracı çakışması derinliği | `13-KIRACI-VE-GUVENLIK.md` — burada yalnız temsilci bearer-token case'leri var |
| Ek (attachment) yükleme/indirme/sahiplik uçlarının kendisi (`/api/attachments`) | `19-COK-MODLULUK-VE-SES.md` — burada yalnız `/v1/responses` içindeki gömülü `data:` URI alım (ingestion) yolu var |
| `AddPatternContentGuard`/`DeniedTerms` engelleme davranışının derinliği | `22-GUARDRAIL-VE-YAPISAL-CIKTI.md` |
| Tool onay akışının (`cancel_order`, human-in-the-loop) yönetim API'sindeki tam döngüsü | `21-DAYANIKLILIK-VE-IPTAL.md` — burada yalnız compat uçlarının onay bekleyen bir çalıştırmayı NASIL gösterdiği (ya da göstermediği) var |
| Sağlayıcıya özgü hata sınıflandırması, devre kesici derinliği | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) · [`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md) (zaten üretildi) |
| Çalıştırma kaydının (`/api/runs`) genel sözleşmesi | [`07-HTTP-YONETIM-API.md`](07-HTTP-YONETIM-API.md) — burada yalnız compat uçlarının da run kaydı ürettiğinin doğrulaması var |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama gerçek OpenAI **ve** Anthropic anahtarlarıyla çalışır
   (`support` OpenAI, `claude-destek` Anthropic ister; §1 ve §2'nin
   "sağlayıcı bağımsızlığı" case'leri ikisini de kullanır).
3. `AgentPrism:Ui:AuthToken` `manuel-test-token-2026`'dır ([`00-INDEKS.md`](00-INDEKS.md) §2.4).
4. Python'da stok `openai` paketi kurulu olmalıdır: `python3 -c "import openai; print(openai.__version__)"`.
   §4'teki case'ler bunu ister.
5. §1.16, §2.23, §3.6, §3.9, §3.13 (kiracı yalıtımı case'leri) için çok kiracılılık
   **geçici olarak** açılır — bu dosyaya özel, [`00-INDEKS.md`](00-INDEKS.md)'nin
   varsayılan secret listesinde YOKTUR:
   ```bash
   cd samples/AgentPrism.Api
   dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"
   dotnet user-secrets set "AgentPrism:Tenancy:AllowHeaderResolution" "true"
   ```
   Bu case'ler bitince kaldırın (`dotnet user-secrets remove ...` ikisi için) ve
   uygulamayı yeniden başlatın — geri kalan case'ler tek kiracılı varsayımla yazıldı.
6. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080`

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export TENANT_A="X-AgentPrism-Tenant: kiraci-alfa"
export TENANT_B="X-AgentPrism-Tenant: kiraci-beta"
```

> **Gerçek para uyarısı.** Bu dosyadaki neredeyse her case gerçek bir sağlayıcı
> çağrısı yapar (`support` = OpenAI, `claude-destek` = Anthropic) — yönetim
> API'sinin aksine burada model çağırmayan case sayısı azdır (yalnızca
> `/v1/conversations` grubunun bir kısmı ve saf doğrulama hataları). `İzlek B`
> her yerde kullanılır.

---

# 1 — `POST /v1/chat/completions` (durumsuz)

`support` agent'ı: OpenAI, `get_order_status`/`list_recent_orders`/`cancel_order`
tool'ları bağlı ([`00-INDEKS.md`](00-INDEKS.md) §3.1/§3.2).

### MT-COMPAT-001 — `model` alanından agent seçilir, mutlu yol

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-036 |

**Ön koşul**
- Örnek uygulama çalışıyor, OpenAI anahtarı tanımlı.

**Adımlar**
1. `support` agent'ına akışsız bir istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "Merhaba, kisa bir selam ver."}]
}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- Gövde `object: "chat.completion"`, `id` `chatcmpl-` önekiyle başlar.
- `choices[0].message.role = "assistant"`, `choices[0].finish_reason = "stop"`.
- `usage` alanı doludur (`prompt_tokens`, `completion_tokens`, `total_tokens`).

**Gerçek sonuç**
`HTTP: 200`. `id: "chatcmpl-..."`, `object: "chat.completion"`. `choices[0].message.role: "assistant"`, `finish_reason: "stop"`. `usage` dolu (`prompt_tokens:219, completion_tokens:8, total_tokens:227`).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-002 — `metadata.entity_id` ile agent seçilir (DevUI konvansiyonu)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `model` alanını **yanlış** bir değerle doldur, agent seçimini `metadata.entity_id`'ye bırak.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "gorunmez-model-adi",
  "metadata": {"entity_id": "support"},
  "messages": [{"role": "user", "content": "Merhaba."}]
}'
```

**Beklenen sonuç**
- `HTTP: 200` — `metadata.entity_id` `model`'e **öncelik** taşır (`OpenAICompatSupport.ReadAgentName`
  önce `metadata.entity_id`'ye bakar).
- `choices[0].message` doludur.

**Gerçek sonuç**
`HTTP: 200` — `metadata.entity_id` (`support`) `model`'e (`gorunmez-model-adi`) öncelik taşıdı, `choices[0].message` dolu.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-003 — Ne `model` ne `metadata.entity_id` verilirse `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `model` alanı olmadan istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "messages": [{"role": "user", "content": "Merhaba."}]
}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- Gövde `{"error":{"message":"...","type":"invalid_request_error"}}` biçimindedir
  (`ProblemDetails` **DEĞİL**).

**Gerçek sonuç**
`HTTP: 400`. Gövde `{"error":{"message":"...","type":"invalid_request_error"}}` biçiminde (`ProblemDetails` değil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-004 — Bilinmeyen agent `404` + `model_not_found`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir agent adıyla istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "hic-boyle-bir-agent",
  "messages": [{"role": "user", "content": "Merhaba."}]
}'
```

**Beklenen sonuç**
- `HTTP: 404`.
- `error.type = "model_not_found"`.
- `error.message` `'hic-boyle-bir-agent' adinda bir agent yok.` dizgisini içerir.

**Gerçek sonuç**
`HTTP: 404`, `error.type: "model_not_found"`, `error.message: "'hic-boyle-bir-agent' adinda bir agent yok."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-005 — `messages` boş dizi ise `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Boş `messages` dizisiyle istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": []
}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `error.message` `'messages' bos olamaz.` dizgisini içerir.
- Hiçbir sağlayıcı çağrısı yapılmaz (agent çözümlemesi mesaj doğrulamasından
  **önce** olur, ama bu doğrulama başarısız olduğu için `RunAsync` hiç çağrılmaz).

**Gerçek sonuç**
`HTTP: 400`, `error.message: "'messages' bos olamaz."`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-006 — Bozuk JSON gövdesi `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Sözdizimsel olarak bozuk bir gövde gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{ "model": "support", "messages": [ BOZUK'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `error.message` `Govde cozumlenemedi:` ile başlar.

**Gerçek sonuç**
`HTTP: 400`, `error.message` `"Govde cozumlenemedi:"` ile başlıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-007 — Parçalı içerik (`content` dizi biçimi) birleştirilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `content` alanını düz metin yerine OpenAI SDK'larının ürettiği parça dizisiyle gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": [{
    "role": "user",
    "content": [
      {"type": "text", "text": "ORD-1001 "},
      {"type": "text", "text": "siparisim nerede?"}
    ]
  }]
}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- Model `ORD-1001 siparisim nerede?` tam metnini görür (iki parça birleşti);
  yanıt `ORD-1001` dizgisini içerir ve `get_order_status` tool'u çağrılmıştır
  (MT-COMPAT-009'daki gibi `GET $APU/api/runs?agentName=support&take=1` ile doğrulanabilir).

**Gerçek sonuç**
`HTTP: 200`. Yanıt `ORD-1001 siparişiniz kargoya verilmiş...` — iki metin parçası birleşti, `get_order_status` çağrıldı. Çapraz doğrulama: `/api/runs?agentName=support&take=1` → `status: Completed`, `eventCount: 6` (tool çağrısı olayları dahil).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-008 — `developer` rolü `system`'e eşlenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

OpenAI'nin yeni SDK'ları `system` yerine `developer` rolünü kullanır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `developer` rollü bir mesajla istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": [
    {"role": "developer", "content": "Her zaman sadece \"tamam\" yaz."},
    {"role": "user", "content": "Merhaba, nasilsin?"}
  ]
}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- İstek reddedilmez (`developer` bilinmeyen bir rol olarak `user`'a düşmez —
  `ToChatRole` eşlemesinde açıkça `system`'e gider); yanıt `developer`
  talimatını izler biçimde kısadır.

**Gerçek sonuç**
`HTTP: 200`. Yanıt `"tamam"` — `developer` talimatı izlendi, istek reddedilmedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-009 — Tool çağrısı tel biçiminde görünmez, ama çalıştırma kaydına düşer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

Bu case dosyanın XML belgesindeki tasarım iddiasını doğrular: *"Tool cagrilari
yanitta gorunmez"* (`OpenAIChatCompletionsEndpoints` remarks). Katalog her
agent'ı `RunRecordingAgentDecorator` ile sarar (`src/AgentPrism.Core/Recording/`);
bu sarmalama giriş noktasından **bağımsızdır** — dolayısıyla `/v1/chat/completions`
üzerinden yapılan bir çağrı da yönetim API'sindeki `/api/runs` listesinde görünür.

**Ön koşul**
- Örnek uygulama çalışıyor.
- [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulandı (temiz `runs` tablosu
  değilse en azından en son satırı ayırt edebilecek bir zaman damgası tutulur).

**Adımlar**
1. Tool çağrısı gerektiren bir istek gönder.
2. Yanıt gövdesinde tool çağrısı ayrıntısı ara.
3. Yönetim API'sinden aynı çalıştırmayı çapraz doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "ORD-1001 siparisim nerede?"}]
}' | tee /tmp/ap-compat-009.json | python3 -m json.tool

curl -s "$APU/api/runs?agentName=support&take=1" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Adım 2: `/tmp/ap-compat-009.json` içinde `tool_calls`, `function_call` gibi
  bir alan **yoktur** — yalnızca `choices[0].message.content` düz metindir ve
  `ORD-1001` dizgisini içerir.
- Adım 3: `/api/runs` listesindeki en üstteki kayıt bu çağrıya aittir
  (`startedAt` yakın zamanlı), `status` `Completed`'dır ve `eventCount > 0`'dır.
  Olayların kendisi (`GET $APU/api/runs/{id}/events`, dosya 07'nin kapsamı)
  `get_order_status` çağrısını taşır — tool çağrısı **kayboldu değil, yalnızca
  tel biçiminden dışlandı**.

**Gerçek sonuç**
Adım 2: gövdede `tool_calls`/`function_call` alanı yok, yalnız `choices[0].message.content` düz metin ve `ORD-1001` içeriyor. Adım 3: `/api/runs` en üst kaydı `status: Completed`, `eventCount: 6` (tool çağrısı olayları tel biçiminden dışlandı ama kayıtlara düştü).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-010 — `usage` alanları `snake_case` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- MT-COMPAT-001 geçti.

**Adımlar**
1. Yanıt gövdesindeki `usage` alanının anahtar adlarını incele.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "Merhaba."}]
}' | python3 -c "import json,sys; print(list(json.load(sys.stdin)['usage'].keys()))"
```

**Beklenen sonuç**
- Çıktı tam olarak `['prompt_tokens', 'completion_tokens', 'total_tokens']`'dır
  (camelCase **DEĞİL** — `ChatUsage` kaydı `JsonPropertyName` ile açıkça
  `snake_case` yazıyor; AgentPrism'in geri kalan JSON yüzeyinin `camelCase`
  varsayılanından **bilinçli bir sapma**).

**Gerçek sonuç**
Çıktı tam olarak `['prompt_tokens', 'completion_tokens', 'total_tokens']` — snake_case.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-011 — Akışlı yanıt: ilk çerçeve `role` deltası, biten işaret `[DONE]`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `stream: true` ile istek gönder, ham SSE çıktısını dosyaya yaz.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "Kisaca merhaba de."}],
  "stream": true
}' | tee /tmp/ap-compat-011.txt
```

**Beklenen sonuç**
- `content-type: text/event-stream` (yanıt başlıklarını `-D -` ile ayrı görün).
- İlk `data:` çerçevesi `"delta":{"role":"assistant","content":null}` taşır
  (`ChatDelta(role, content)` — ilk çerçeve yalnız rolü bildirir).
- Sonraki çerçeveler `"content":"..."` parçalarıyla gelir.
- Son iki çerçeve sırasıyla `"finish_reason":"stop"` ve düz metin `data: [DONE]`
  satırıdır (SDK'ların beklediği sabit bitiş işareti).
- Her çerçevenin `object` alanı `"chat.completion.chunk"`dır (mutlu yoldaki
  `"chat.completion"`'dan **farklı**).

**Gerçek sonuç**
`content-type: text/event-stream`. İlk çerçeve `"delta":{"role":"assistant"}` (content alanı yok/null). Sonraki çerçeveler `"content":"..."` parçaları. Son iki çerçeve `"finish_reason":"stop"` ve `data: [DONE]`. Her çerçevenin `object` alanı `"chat.completion.chunk"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-012 — Akış durumsuzdur: hiçbir oturum/konuşma oluşmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

Kod belgesinin merkezi iddiası: *"Bu uc durumsuzdur"*. Bu case iddiayı hem
akışsız hem akışlı yolda ölçer — her ikisi de `session: null` ile `RunAsync`/
`RunStreamingAsync` çağırır.

**Ön koşul**
- [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulandı (`sessions` tablosu boş
  ya da mevcut satır sayısı biliniyor).

**Adımlar**
1. Mevcut oturum sayısını kaydet.
2. Akışsız bir `/v1/chat/completions` çağrısı yap.
3. Akışlı bir `/v1/chat/completions` çağrısı yap.
4. Oturum sayısını yeniden oku.

**Girilecek veri**
```bash
curl -s "$APU/api/sessions" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"

curl -s -X POST "$APU/v1/chat/completions" -H "$APB" -H "content-type: application/json" \
     -d '{"model":"support","messages":[{"role":"user","content":"Merhaba"}]}' > /dev/null

curl -s -X POST "$APU/v1/chat/completions" -H "$APB" -H "content-type: application/json" \
     -d '{"model":"support","messages":[{"role":"user","content":"Merhaba"}],"stream":true}' > /dev/null

curl -s "$APU/api/sessions" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Adım 1 ve adım 4'teki oturum sayısı **birebir aynıdır** — iki çağrı da
  (`GET /api/runs`'ta ayrı ayrı görünseler bile) `sessions` tablosuna hiçbir
  satır eklemez.

**Gerçek sonuç**
Adım 1 ve 4'te oturum sayısı **aynı** (`0 -> 0`) — akışsız ve akışlı çağrılar `sessions` tablosuna hiçbir satır eklemedi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-013 — `Idempotency-Key` tekrarı önbellekten döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 43, 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor. `AgentPrism:Idempotency:Enabled` varsayılan
  `true`'dur — ek yapılandırma gerekmez.

**Adımlar**
1. Aynı `Idempotency-Key` ile aynı gövdeyi iki kez gönder.

**Girilecek veri**
```bash
curl -s -D - -o /tmp/ap-compat-013-a.json -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -H "Idempotency-Key: manuel-idem-chat-01" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "Bir kere calis."}]
}' | grep -i "idempotency"

curl -s -D - -o /tmp/ap-compat-013-b.json -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -H "Idempotency-Key: manuel-idem-chat-01" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "Bir kere calis."}]
}' | grep -i "idempotency"

diff /tmp/ap-compat-013-a.json /tmp/ap-compat-013-b.json && echo "AYNI GOVDE"
```

**Beklenen sonuç**
- İlk yanıtta `Idempotency-Replayed` başlığı **yoktur**.
- İkinci yanıtta `Idempotency-Replayed` başlığı **vardır**.
- İki gövde birebir aynıdır (`AYNI GOVDE` yazdırılır) — model **ikinci kez
  çağrılmadı**, saklanan yanıt tekrarlandı.

**Gerçek sonuç**
İlk yanıtta `Idempotency-Replayed` başlığı yok. İkinci yanıtta `Idempotency-Replayed: true` var. İki gövde birebir aynı (`AYNI GOVDE` yazdırıldı).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-014 — Aynı `Idempotency-Key`, farklı gövde → `422`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 43, 50 |
| **İlgili karar** | — |

**Ön koşul**
- MT-COMPAT-013 geçti (`manuel-idem-chat-01` anahtarı bir gövdeyle tamamlanmış durumda).

**Adımlar**
1. Aynı anahtarı **farklı** bir gövdeyle yeniden kullan.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -H "Idempotency-Key: manuel-idem-chat-01" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "BASKA bir istek."}]
}'
```

**Beklenen sonuç**
- `HTTP: 422`.
- Gövde `title: "Idempotency-Key farkli bir istek icin kullanilmis"` başlığını
  taşır (`Results.Problem` — bu **tek** istisna: idempotency çakışması compat
  uçlarında bile `ProblemDetails` kullanır, çünkü filtre uç-bağımsızdır).

**Gerçek sonuç**
`HTTP: 422`, `title: "Idempotency-Key farkli bir istek icin kullanilmis"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-015 — `Idempotency-Key` + `stream: true` → `400`

Negatif senaryo / sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 43, 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Akışlı bir isteğe `Idempotency-Key` başlığı ekle.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -H "Idempotency-Key: manuel-idem-chat-stream-01" -d '{
  "model": "support",
  "messages": [{"role": "user", "content": "Merhaba"}],
  "stream": true
}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `title: "Akisli istekte Idempotency-Key desteklenmiyor"`.
- SSE akışı hiç **başlamaz** (yanıt `content-type: application/json`'dır,
  `text/event-stream` değil) — istemci yarım bir akışla karşılaşmaz.

**Gerçek sonuç**
`HTTP: 400`, `title: "Akisli istekte Idempotency-Key desteklenmiyor"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-016 — Aynı tel biçimi gerçek bir Anthropic agent'ıyla da çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50, 26 |
| **İlgili karar** | — |

`/v1/chat/completions` bir agent soyutlaması üzerine kuruludur; hangi
sağlayıcının arkada çalıştığı tel biçimini değiştirmemelidir.

**Ön koşul**
- Örnek uygulama çalışıyor, Anthropic anahtarı tanımlı.

**Adımlar**
1. `model` alanına OpenAI değil, Anthropic destekli bir agent yaz.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "claude-destek",
  "messages": [{"role": "user", "content": "Kisaca merhaba de."}]
}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- Gövde biçimi MT-COMPAT-001 ile **birebir aynı şemadadır**
  (`object: "chat.completion"`, `choices[0].message`, `usage`) — istemci
  hangi sağlayıcının çalıştığını gövde biçiminden anlayamaz.

**Gerçek sonuç**
`HTTP: 200`. Gövde MT-COMPAT-001 ile birebir aynı şemada (`object: "chat.completion"`, `choices[0].message`, `usage`) — sağlayıcı (Anthropic) gövde biçiminden anlaşılmıyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — `POST /v1/responses` (oturum destekli)

### MT-COMPAT-017 — `model` alanından agent seçilir, mutlu yol

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-036 |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `support` agent'ına akışsız bir Responses isteği gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "input": "Merhaba, kisa bir selam ver."
}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- Gövde MAF'ın `OpenAIResponses.WriteResponse` çıktısıdır (OpenAI Responses
  API'sinin `id`/`object: "response"`/`output`/`status` alanlarını taşır).

**Gerçek sonuç**
`HTTP: 200`. Gövde OpenAI Responses bicimindedir: `id: "resp_..."`, `object: "response"`, `output`, `status: "completed"`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-018 — `metadata.entity_id` ile agent seçilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `model` alanını atla, yalnız `metadata.entity_id` ver.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d '{
  "metadata": {"entity_id": "support"},
  "input": "Merhaba."
}'
```

**Beklenen sonuç**
- `HTTP: 200` — `model` hiç verilmediği için doğrudan `metadata.entity_id` kullanılır.

**Gerçek sonuç**
`HTTP: 200` - `model` hic verilmedi, dogrudan `metadata.entity_id` kullanildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-019 — Agent seçilmezse `400` + bilinen agent listesi

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

Bu davranış `/v1/chat/completions`'tan (MT-COMPAT-003) **farklıdır**: Responses
uç hata mesajına kayıtlı agent adlarının listesini ekler, Chat Completions eklemez.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Agent belirtmeden istek gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" \
     -d '{"input": "Merhaba."}' | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 400`.
- `error.message` `Kayitli agent'lar: ` dizgisini ve en az `support` adını içerir
  (`KnownAgentsAsync`).

**Gerçek sonuç**
`HTTP: 400`. `error.message` "Kayitli agent'lar: " dizgisini ve `support` dahil tum kayitli adlari iceriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-020 — Bilinmeyen agent `404` + bilinen agent listesi

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir agent adıyla istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "hic-boyle-bir-agent",
  "input": "Merhaba."
}'
```

**Beklenen sonuç**
- `HTTP: 404`, `error.type = "model_not_found"`.
- `error.message` hem `'hic-boyle-bir-agent' adinda bir agent yok.` hem
  `Kayitli agent'lar: ` metnini içerir.

**Gerçek sonuç**
`HTTP: 404`, `error.type: "model_not_found"`. `error.message` hem "'hic-boyle-bir-agent' adinda bir agent yok." hem "Kayitli agent'lar: " metnini iceriyor.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-021 — `conversation` alanı oturumu o kimlikle saklar (K-043)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-043 |

Bu case K-043'ün merkezi iddiasını doğrular: konuşma kimliği doğrudan oturum
kimliğidir — ikinci bir kimlik uzayı yoktur.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Sabit bir `conversation` kimliğiyle istek gönder.
2. Aynı kimliği yönetim API'sinde oturum olarak sorgula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-021",
  "input": "Merhaba."
}' | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])"

curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/manuel-conv-021" -H "$APB"
```

**Beklenen sonuç**
- İlk çağrının `id` alanı (yanıt kimliği) `manuel-conv-021`'den **farklıdır**
  (yanıt kimliği her zaman yeni üretilir; saklama kimliği `conversation`'dır).
- İkinci çağrı `HTTP: 200` döner ve `id: "manuel-conv-021"`, `agentName: "support"`
  taşıyan bir `SessionRecord` gösterir — ikinci bir depo **açılmadı**.

**Gerçek sonuç**
Ilk cagrinin id (resp_GSs7...) manuel-conv-021'den farkli. Ikinci cagri HTTP 200, id: manuel-conv-021, agentName: support tasiyan bir SessionRecord gosterdi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-022 — `previous_response_id` geçmişi zincirler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-036 |

`conversation` verilmediğinde saklama kimliği üretilen yanıt kimliğidir; bir
sonraki tur `previous_response_id` ile o kimliğe zincirlenir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `FIX-PROMPT-01`'i (`ORD-1001 siparisim nerede?`) `conversation` **vermeden** gönder, dönen `id`'yi sakla.
2. İkinci turda `previous_response_id`'yi ilk `id` yap, geçmişe atıfta bulunan bir soru sor.

**Girilecek veri**
```bash
RID1=$(curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "input": "ORD-1001 siparisim nerede?"
}' | python3 -c "import json,sys; print(json.load(sys.stdin)['id'])")

echo "Ilk yanit kimligi: $RID1"

curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d "{
  \"model\": \"support\",
  \"previous_response_id\": \"$RID1\",
  \"input\": \"Az once sordugum siparis numarasi neydi?\"
}" | python3 -m json.tool
```

**Beklenen sonuç**
- İkinci yanıt `ORD-1001` dizgisini içerir — model ilk turun geçmişini görür.
- `GET $APU/api/sessions/$RID1` bu kimlikte bir oturum kaydı gösterir (saklama
  kimliği `GetSessionStoreId` = `conversation ?? previous_response_id` kuralınca
  ilk yanıt kimliğidir).

**Gerçek sonuç**
Ikinci yanit Az once sordugunuz siparis numarasi ORD-1001 - model ilk turun gecmisini gordu. GET /api/sessions/RID1 bu kimlikte oturum kaydi gosterdi (saklama kimligi ilk yanit kimligi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-023 — Konuşma kimliğine çapraz kiracı erişimi `404` döner

Negatif senaryo — güvenlik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50, 41 |
| **İlgili karar** | — |

**Ön koşul**
- Çok kiracılılık açık ("Koşmadan önce" §5).
- `FIX-TENANT-01` (`kiraci-alfa`) bir konuşma oluşturmuş olmalı.

**Adımlar**
1. `kiraci-alfa` başlığıyla bir konuşma başlat.
2. `kiraci-beta` başlığıyla **aynı kimliğe** `/v1/responses` çağrısı yap.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "$TENANT_A" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-023",
  "input": "Bu benim gizli sohbetim."
}' > /dev/null

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" -H "$TENANT_B" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-023",
  "input": "Baska bir kiraciyim, bu sohbete katilmaya calisiyorum."
}'
```

**Beklenen sonuç**
- İkinci çağrı `HTTP: 404`, `error.type = "not_found_error"` döner.
- `kiraci-beta`'nın isteği `kiraci-alfa`'nın oturumuna **yeni bir tur eklemez**
  (`IsOwnedByTenantAsync` yükleme öncesi sahiplik denetimi yapar).

**Gerçek sonuç**
**KALDI - HATA-S2-005 (Orta).** Ikinci cagri (kiraci-beta, ayni conversation kimligi manuel-conv-023) beklenen HTTP 404/not_found_error yerine HTTP 200 dondu, gercek bir model yaniti uretti. Veri SIZINTISI YOK - dogrulandi: GET /api/sessions/manuel-conv-023 tenant-alfa basligiyla hala yalniz orijinal 2 mesaji gosteriyor (tenant-beta'nin mesaji ORAYA yazilmadi); tenant-beta basligiyla ayni ID icin TAMAMEN AYRI, bagimsiz bir SessionRecord (tenantId: kiraci-beta) sessizce OLUSTURULMUS. Kok neden: OpenAICompatSupport.IsOwnedByTenantAsync (OpenAICompatSupport.cs:103-113) store.GetAsync(sessionId) cagirir ve kendi kod yorumunda 'Bellek ici depo kiraci filtresi uygulamaz' (satir 100-101) diye ACIKLAR - ama bu VARSAYIM YANLIS: InMemorySessionStore._sessions sozlugu (TenantId, Id) BILESIK anahtarla tutuluyor (InMemorySessionStore.cs:20) ve GetAsync (satir 53-59) DAIMA ambient _tenantContext.TenantId ile sorguluyor - yani depo ZATEN kiraci-kapsamli. Sonuc: tenant-beta baglaminda store.GetAsync('manuel-conv-023') HICBIR ZAMAN tenant-alfa'nin kaydini GOREMEZ (farkli anahtar), record her zaman null donuyor, IsOwnedByTenantAsync'in 'record?.TenantId is null -> true (izin ver)' dali her zaman tetikleniyor - HTTP katmanindaki 404 reddi PRATIKTE HICBIR ZAMAN calismiyor (olu kod), yerine sessizce yeni bir oturum aciliyor. Veri gizliligi baska bir mekanizmayla (depo seviyesi kiraci ayrimi) korunuyor ama kodun kendi belgeledigi/iddia ettigi acik 404 reddi calismiyor.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-COMPAT-024 — Akışlı yanıt OpenAI olay adlarını kullanır, `[DONE]` YOKTUR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

🔍 **Ölçüldü (2026-08-09), `strings` ile decompile edilerek.**
`Microsoft.Agents.AI.Hosting.OpenAI.dll` (1.16.0-alpha.260730.1) içinde
`response.created`, `response.in_progress`, `response.output_item.added`,
`response.content_part.added`, `response.output_text.delta`,
`response.output_text.done`, `response.content_part.done`,
`response.output_item.done`, `response.completed` (hata yolunda
`response.failed`/`response.incomplete`/`response.cancelled`) dizgileri
bulundu; `[DONE]` dizgisi **bulunmadı**. Bu, `/v1/chat/completions`'ın
(MT-COMPAT-011) bitiş işaretinden **farklı bir sözleşmedir**.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `stream: true` ile istek gönder, ham SSE çerçevelerini kaydet.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "input": "Kisaca merhaba de.",
  "stream": true
}' | tee /tmp/ap-compat-024.txt
```

**Beklenen sonuç**
- İlk `event:` satırı `response.created`'dır.
- Akış `event: response.completed` ile biter.
- `data: [DONE]` satırı **hiçbir yerde geçmez**.
- Her çerçeve `event:` alanı taşır (MT-COMPAT-011'in `/v1/chat/completions`
  akışının aksine — o yalnız `data:` yazar, `event:` yazmaz).

**Gerçek sonuç**
Ilk event: satiri response.created. Akis event: response.completed ile bitti. data: [DONE] hicbir yerde gecmedi. Her cerceve event: alani tasiyor (12 event satiri).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-025 — Gömülü `data:` URI bir eke çevrilir, sohbet geçmişine gömülmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50, 14 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. 1×1 saydam PNG'nin `data:` URI'siyle bir görsel gönder.
2. Yanıttaki ek referansını bul, `/api/attachments/{id}` ile doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "input": [{
    "role": "user",
    "content": [
      {"type": "input_text", "text": "Bu resimde ne var, kisaca soyle."},
      {"type": "input_image", "image_url": "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII="}
    ]
  }],
  "conversation": "manuel-conv-025"
}' > /dev/null

curl -s "$APU/api/attachments?sessionId=manuel-conv-025" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `/api/attachments?sessionId=manuel-conv-025` listesinde `mediaType: "image/png"`
  taşıyan bir kayıt vardır.
- `GET $APU/api/sessions/manuel-conv-025` ile okunan sohbet geçmişinde ham
  base64 verisi **yoktur** — yalnız `/agentprism/api/attachments/{id}` biçiminde
  bir referans URI'si vardır (`AttachmentUriReference.Create`).

**Gerçek sonuç**
/api/attachments?sessionId=manuel-conv-025 listesinde mediaType: image/png tasiyan bir kayit var. Sohbet gecmisinde ham base64 verisi YOK; yalniz api/attachments/{id} bicimli bir referans var.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-026 — Tanınmayan ikili tür `400` ile reddedilir

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50, 14 |
| **İlgili karar** | — |

`AttachmentTypeGuard` istemcinin bildirdiği `Content-Type`'a değil, ilk
baytlardaki sihirli bayta bakar. Bilinen hiçbir imzayla eşleşmeyen, geçerli
UTF-8 da olmayan bir içerik reddedilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Tanınmayan bir ikili imza ile (`.exe` başlığı `MZ`) gömülü veri gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "input": [{
    "role": "user",
    "content": [
      {"type": "input_text", "text": "Bu dosyayi incele."},
      {"type": "input_file", "file_data": "data:application/octet-stream;base64,TVqQAAMAAAAEAAAA//8AAA=="}
    ]
  }]
}'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `error.message` `Dosya turu taninmadi.` ile başlar ve izin verilen türlerin
  listesini içerir.

**Gerçek sonuç**
**Dokuman duzeltmesi.** Senaryonun kendi base64 govdesi (TVpqdW5rZGF0YQ==) coder MZjunkdata metnine - bu gecerli ASCII/UTF-8'dir, AttachmentTypeGuard.LooksLikePlainText (AttachmentTypeGuard.cs:135-137) tarafindan BILEREK text/plain olarak kabul edilir (guard sadece taninan ikili imzalari VEYA gecerli UTF-8 metni kabul eder; ne biri ne digeri olan icerik reddedilir). Senaryo yazarinin niyeti gercek bir ikili/non-UTF8 govde test etmekti ama saglanan payload bunu karsilamiyordu. Govde gercek bir PE-benzeri ikili (4D 5A 90 00 03 00 00 00 04 00 00 00 FF FF 00 00, base64 TVqQAAMAAAAEAAAA//8AAA==) ile degistirildi ve DOGRU sekilde HTTP 400, 'Dosya turu taninmadi. Desteklenen turler: application/pdf, audio/*, image/gif, image/jpeg, image/png, image/webp, text/plain.' dondu - guard tasarlandigi gibi calisiyor, urun kusuru YOK.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-027 — Akışsız yolda sağlayıcı hatası `502` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- `azure-destek` agent'ı (Azure kimliği **yok** — bilinçli olarak kırık bir
  sağlayıcı hedefler).

**Adımlar**
1. Azure destekli agent'a akışsız bir istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "azure-destek",
  "input": "Merhaba."
}'
```

**Beklenen sonuç**
- `HTTP: 502`.
- `error.type = "upstream_error"`.
- Yanıt gövdesi `ProblemDetails` **DEĞİLDİR** — `AgentPrismException` (veya
  `InvalidOperationException`/`HttpRequestException`) `OpenAICompatSupport.Error`
  ile yakalanmıştır.

**Gerçek sonuç**
**KALDI - HATA-S2-003 (Yuksek).** Once azure-destek hic kayitli DEGILDI (bu ortamda Azure kimligi tanimsizdi -> agentPrism.AddAgent yalniz azureOpenAIEnabled true ise cagriliyor, Program.cs:556-578) - bu kismi doküman duzeltmesidir (asagida). Gecici sahte Azure Endpoint+ApiKey+DefaultDeployment ile agent kayitli hale getirilip GERCEK bir sağlayici hatasi (DNS cozulemedi) tetiklendi. Beklenen HTTP 502 + error.type: upstream_error (OpenAICompatSupport.Error), GERCEKLESEN: HTTP 500, govde ASP.NET Core'un GENEL ProblemDetails sayfasi ({"type":"...","title":"An error occurred while processing your request.","status":500}) - OpenAI hata zarfi DEGIL. Kok neden: OpenAIResponsesEndpoints.cs:213 akissiz yolun catch filtresi hala K-296 ONCESI dar listeyi tasiyor (catch (Exception ex) when (ex is AgentPrismException or InvalidOperationException or HttpRequestException)) - gercek saglayici istisnasi (System.AggregateException, DNS hatasi) bu filtreden GECMIYOR, yakalanmadan ASP.NET Core'un varsayilan isleyicisine sizip 500 ProblemDetails uretiyor. Kapsam: OpenAIChatCompletionsEndpoints.cs:145 (/v1/chat/completions akissiz yolu) AYNI dar filtreyi tasiyor - iki compat ucunun da akissiz yollari etkileniyor. K-296'nin duzeltmesi yalniz akisli varyantlari (ResponsesStream, ChatCompletionsStream) kapsamis, kardes akissiz yollari KACIRMIS.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-COMPAT-028 — Akışlı yolda sağlayıcı hatası `event: error` çerçevesi üretir (düzeltildi)

Negatif senaryo — düzeltilmiş kusur.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-296 |

**Düzeltilmiş kusur (2026-08-10).** `OpenAIResponsesEndpoints.ResponsesStream.ExecuteAsync`'in
hata `catch`'i tıpkı `OpenAIChatCompletionsEndpoints.ChatCompletionsStream` gibi
önceden yalnız `AgentPrismException`, `InvalidOperationException`,
`HttpRequestException` yakalıyordu. `05-SAGLAYICI-OPENAI.md` `MT-OAI-043`'ün
ölçtüğü boşluk (`System.ClientModel.ClientResultException`) ile
`06-SAGLAYICI-DIGER.md` `MT-PROV-036`'nın ölçtüğü boşluk
(`Anthropic.Exceptions.AnthropicApiException`) buradaki `ResponsesStream` için
de **bağımsız olarak** (üçüncü tekrar) geçerliydi. Dar filtre bu iki compat
ucundan da (`ResponsesStream` ve `ChatCompletionsStream`) kaldırıldı — artık
`OperationCanceledException` dışındaki HER istisna bir `error`/`response.failed`
çerçevesine dönüşür.

**Ön koşul**
- `azure-destek` agent'ı.

**Adımlar**
1. Azure destekli agent'a akışlı bir istek gönder, bağlantının nasıl kapandığını gözlemle.

**Girilecek veri**
```bash
curl -N -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -d '{
  "model": "azure-destek",
  "input": "Merhaba.",
  "stream": true
}'
```

**Beklenen sonuç**
- Başlıklar `200 OK` / `text/event-stream` olarak gönderilir; bağlantı
  çerçevesiz aniden KAPANMAZ — bir `event: response.failed`/`event: error`
  çerçevesi gelir.
- Çapraz doğrulama: `GET $APU/api/runs?agentName=azure-destek&take=1` bu
  çalıştırmayı `Failed` durumunda gösterir.
- `event: error`/`response.failed` GELMEZse (fix'in regresyonu): hemen
  `OpenAIResponsesEndpoints.cs`'in `ResponsesStream.ExecuteAsync`'indeki
  `catch` bloğu kontrol edilmelidir.

**Gerçek sonuç**
Basliklar 200/text/event-stream gonderildi, baglanti cercevesiz kapanmadi: event: error cercevesi geldi (DNS hatasi mesajiyla). Capraz dogrulama: /api/runs?agentName=azure-destek&take=1 bu calistirmayi Failed durumunda gosterdi. K-296 fix'i akisli yolda DOGRU calisiyor - regresyon yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-029 — Onay gerektiren bir tool çağrısı `/v1/responses` üzerinden nasıl görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50, 6 |
| **İlgili karar** | — |

`support` agent'ının `cancel_order` tool'u `RequiresApproval = true` taşır.
Yönetim API'si bunu bir onay kartıyla gösterir (dosya 21'in kapsamı); bu case
compat ucunun aynı durumu **ne döndürdüğünü** (boş içerik mi, hata mı, kısmi
metin mi) kayıt altına alır — kod okuması bir metin yanıtı varsaymadı.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `cancel_order` tetikleyen bir istek gönder.
2. Yanıt gövdesini incele.
3. Çalıştırma durumunu yönetim API'sinden doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-029",
  "input": "ORD-1001 siparisimi iptal et"
}' | python3 -m json.tool

curl -s "$APU/api/runs?agentName=support&sessionId=manuel-conv-029&take=1" -H "$APB" \
     | python3 -m json.tool
```

**Beklenen sonuç (koşum kaydeder, önceden varsayılmıyor)**
- Yanıt gövdesinin `status` alanı ve `output` içeriği **olduğu gibi** kaydedilir.
- `GET $APU/api/runs?...` çıktısındaki `status` alanı (`AwaitingInput` mu,
  `Completed` mi, başka bir şey mi) kaydedilir.
- Bu iki gözlem tutarsızsa (örnek: HTTP yanıtı "tamamlandı" gibi görünürken run
  kaydı "girdi bekliyor" diyorsa) bu bir kusur adayıdır ve
  `docs/UCUNCU-FAZ-ADAYLARI.md`'ye değil, doğrudan HATA şablonuyla kaydedilir
  (`PROMPT.md` §8 ayrımı: var olan davranış yanlışsa kusurdur).

**Gerçek sonuç**
**KALDI - HATA-S2-004 (Yuksek).** HTTP yaniti: status: completed, output: [] (BOS). Run kaydi da status: Completed (eventCount:3: run.started, message.completed BOS metinle, run.completed - hicbir run.awaiting_input yok). Ancak GET /api/sessions/manuel-conv-029 gercek durumu gosteriyor: mesaj gecmisinde bir toolApprovalRequest var (cancel_order, requiresConfirmation:true) VE state.stateBag._pendingApprovalRequests dizisinde bekleyen bir kayit var - tool GERCEKTEN onay bekliyor. Compat ucu (hem HTTP yaniti hem run kaydi) bu bekleyen onayi TAMAMEN gizliyor; ikisi de tutarli sekilde completed diyor ama gercek durum AwaitingInput'tur. cancel_order hicbir zaman calismadi (dogru - onay verilmedi) ama caller'in bunu /v1/responses uzerinden gormesinin hicbir yolu yok; yonetim API'sine (dosya 21) gitmeden sessizce takili kalir.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-COMPAT-030 — `Idempotency-Key` + `stream: true` `/v1/responses`'ta da `400`

Negatif senaryo / sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 43, 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Akışlı bir Responses isteğine `Idempotency-Key` ekle.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" -H "$APB" \
     -H "content-type: application/json" -H "Idempotency-Key: manuel-idem-resp-stream-01" -d '{
  "model": "support",
  "input": "Merhaba",
  "stream": true
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Akisli istekte Idempotency-Key desteklenmiyor"` —
  MT-COMPAT-015 ile aynı filtre, aynı davranış (filtre uç-bağımsızdır).

**Gerçek sonuç**
HTTP: 400, title: Akisli istekte Idempotency-Key desteklenmiyor - MT-COMPAT-015 ile ayni filtre, ayni davranis.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — `/v1/conversations*` (kimlik rezervasyonu ve yaşam döngüsü)

### MT-COMPAT-031 — Boş gövdeyle oluşturma: `conv_` önekli kimlik döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-043 |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Boş gövdeyle bir konuşma oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/conversations" -H "$APB" \
     -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `HTTP: 200`.
- `id` `conv_` öneki + 32 hane hexadecimal (`AgentPrismId.NewId().ToString("N")`)
  biçimindedir.
- `object: "conversation"`, `metadata: null` (boş gövdede metadata yoksayılır).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-032 — Metadata ile oluşturma: yalnız metin alanları geri döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Karışık türde alanlar taşıyan `metadata` gönder.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/conversations" -H "$APB" -H "content-type: application/json" -d '{
  "metadata": {
    "kaynak": "manuel-test",
    "sayi_alani": 42,
    "bool_alani": true
  }
}' | python3 -m json.tool
```

**Beklenen sonuç**
- `metadata` yalnız `{"kaynak": "manuel-test"}` içerir — `sayi_alani` ve
  `bool_alani` sessizce **düşer** (`ReadMetadata` yalnız `JsonValueKind.String`
  değerleri okur).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-033 — Bozuk gövde sessizce yutulmaz, `400` döner

Negatif senaryo / sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

Kodun kendi belgesi bu ayrımı bilinçli olarak vurgular: gövde `ContentLength`'e
güvenilmeden ham metin olarak okunur, çünkü parçalı (chunked) aktarımda bu
başlık gelmeyebilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Sözdizimsel olarak bozuk bir gövde gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/conversations" -H "$APB" \
     -H "content-type: application/json" -d '{ "metadata": BOZUK'
```

**Beklenen sonuç**
- `HTTP: 400`.
- `error.message` `Govde cozumlenemedi:` ile başlar.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-034 — Kullanılmamış konuşma `GET`'i `200` boş döner, `404` DEĞİL

Sınır durumu — gerçek OpenAI'den bilinçli davranış farkı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-043 |

**Ön koşul**
- `manuel-conv-034` kimliği **hiçbir yerde** kullanılmamış (rastgele, yeni bir
  UUID kullanmak çakışmayı önler).

**Adımlar**
1. Hiç kullanılmamış bir konuşma kimliğini sorgula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/v1/conversations/manuel-conv-034-$(uuidgen)" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200` (**404 değil** — kimlik rezervasyonu kavramının doğrudan sonucu:
  henüz kullanılmamış bir konuşma kimliği geçerlidir, oturum ilk `/v1/responses`
  çağrısında doğar).
- `object: "conversation"`, `created_at` şimdiki zamana yakın bir Unix damgasıdır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-035 — Kullanılmış konuşmanın `created_at`'i oturum oluşturma zamanını yansıtır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Bir konuşmayı `/v1/responses` ile kullan.
2. Aynı kimliği `GET /v1/conversations/{id}` ile sorgula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-035",
  "input": "Merhaba."
}' > /dev/null

curl -s "$APU/v1/conversations/manuel-conv-035" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `created_at` `SessionRecord.CreatedAt`'e eşittir (rezervasyon anındaki bir
  zaman değil — çünkü bu kimlik hiç `POST /v1/conversations` ile rezerve
  edilmedi, doğrudan `/v1/responses`'ta doğdu).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-036 — Konuşma `GET`'ine çapraz kiracı erişimi `404` döner

Negatif senaryo — güvenlik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50, 41 |
| **İlgili karar** | — |

**Ön koşul**
- Çok kiracılılık açık ("Koşmadan önce" §5).

**Adımlar**
1. `kiraci-alfa` ile bir konuşma kullan.
2. `kiraci-beta` ile aynı kimliği sorgula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "$TENANT_A" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-036",
  "input": "Alfa kiracisinin sohbeti."
}' > /dev/null

curl -s -w "\nHTTP: %{http_code}\n" "$APU/v1/conversations/manuel-conv-036" -H "$APB" -H "$TENANT_B"
```

**Beklenen sonuç**
- `HTTP: 404`, `error.type = "not_found_error"` — `kiraci-beta` `kiraci-alfa`'nın
  konuşmasının varlığını bile öğrenemez (kayıt var ama sahiplik uymuyor).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-037 — Silme, altındaki oturumu da siler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Bir konuşmayı kullan.
2. Sil.
3. Altındaki oturumun artık var olmadığını doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-037",
  "input": "Silinecek sohbet."
}' > /dev/null

curl -s -X DELETE "$APU/v1/conversations/manuel-conv-037" -H "$APB" | python3 -m json.tool

curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/manuel-conv-037" -H "$APB"
```

**Beklenen sonuç**
- `DELETE` yanıtı `{"id":"manuel-conv-037","object":"conversation.deleted","deleted":true}`.
- Ardından `GET /api/sessions/manuel-conv-037` `HTTP: 404` döner.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-038 — Kullanılmamış bir konuşmayı silmek hata değil, `deleted: false` döner

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- `manuel-conv-038-$(uuidgen)` hiç kullanılmamış.

**Adımlar**
1. Hiç kullanılmamış bir kimliği sil.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/v1/conversations/manuel-conv-038-deneme" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200` (404 **değil** — MT-COMPAT-034 ile tutarlı: kimlik her zaman
  geçerlidir).
- `deleted: false` (`record is not null && ...` ifadesi `record` `null` olduğu
  için kısa devre yapar, `manager.DeleteSessionAsync` hiç çağrılmaz).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-039 — Çapraz kiracı silme `404` döner, başka kiracının oturumunu silmez

Negatif senaryo — güvenlik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50, 41 |
| **İlgili karar** | — |

**Ön koşul**
- Çok kiracılılık açık ("Koşmadan önce" §5).

**Adımlar**
1. `kiraci-alfa` ile bir konuşma kullan.
2. `kiraci-beta` ile aynı kimliği silmeyi dene.
3. `kiraci-alfa`'nın oturumunun hâlâ var olduğunu doğrula.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "$TENANT_A" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-039",
  "input": "Alfa kiracisinin sohbeti."
}' > /dev/null

curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/v1/conversations/manuel-conv-039" -H "$APB" -H "$TENANT_B"

curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/manuel-conv-039" -H "$APB" -H "$TENANT_A"
```

**Beklenen sonuç**
- Adım 2: `HTTP: 404`.
- Adım 3: `HTTP: 200` — `kiraci-alfa`'nın oturumu hâlâ vardır, `kiraci-beta`'nın
  başarısız silme girişiminden **etkilenmedi**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-040 — Öge listesi mesaj/tool çağrısı/tool sonucu sırasını korur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Tool çağrısı tetikleyen bir konuşma başlat.
2. Ögeleri listele.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-040",
  "input": "ORD-1001 siparisim nerede?"
}' > /dev/null

curl -s "$APU/v1/conversations/manuel-conv-040/items" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `data` dizisi en az şu türleri **sırayla** içerir: `message` (kullanıcı
  girdisi), `function_call` (`name: "get_order_status"`, `arguments` JSON
  dizgisi), `function_call_output` (`output` alanı dolu), `message`
  (asistan yanıtı, `role: "assistant"`).
- `function_call` ve `function_call_output` ögelerinin `call_id` alanları
  **eşleşir**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-041 — `limit` parametresi kırpar, `has_more` kırpma olduğunda `true` döner (düzeltildi)

Sınır durumu — düzeltilmiş kusur; hâlâ eksik olan `after`/`before` imleç
desteği ayrı bir **yetenek** boşluğudur (aşağıda not edilir).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Düzeltilmiş kusur (2026-08-10).** `ListItemsAsync` kaynağı `HasMore: false`'u
önceden **sabit** yazıyordu (`ItemListResource` kurucusuna elle geçirilirdi) —
kırpma gerçek toplamdan az bir sonuç döndürse bile `has_more` hep `false`
görünüyordu. Kırpmadan ÖNCEki gerçek toplam artık ölçülüp `hasMore`'a
yazılıyor. **Hâlâ eksik olan (kusur değil, yetenek):** gerçek OpenAI'nin
`items.list` sözleşmesindeki `after`/`before` imleç parametreleri
desteklenmiyor — istemci `has_more: true` görse bile bir sonraki sayfayı
İSTEYEMEZ. Bu, kırpılmış ögelerin veri kaybı olmadan (yalnız görünürlük
sınırıyla) döndüğü anlamına gelir.

**Ön koşul**
- MT-COMPAT-040'ın `manuel-conv-040` konuşması en az 4 öge üretti.

**Adımlar**
1. `limit=2` ile ögeleri listele.

**Girilecek veri**
```bash
curl -s "$APU/v1/conversations/manuel-conv-040/items?limit=2" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `data` dizisi tam **2** öge taşır (ilk 2 — `items[..max]`).
- `has_more: true` — gerçek toplam öge sayısı (en az 4) 2'den fazla olduğu
  için. `false` dönerse fix'in regresyonudur — **Kusur, Önem: Orta**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-042 — Kullanılmamış konuşmada öge listesi boş dizi döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

**Ön koşul**
- Hiç kullanılmamış bir kimlik.

**Adımlar**
1. Kullanılmamış bir konuşmanın ögelerini listele.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/v1/conversations/manuel-conv-042-deneme/items" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200` (404 değil, MT-COMPAT-034 ile tutarlı).
- `data: []`, `first_id: null`, `last_id: null`, `has_more: false`.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-043 — Öge listesine çapraz kiracı erişimi `404` döner

Negatif senaryo — güvenlik.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50, 41 |
| **İlgili karar** | — |

**Ön koşul**
- Çok kiracılılık açık ("Koşmadan önce" §5).

**Adımlar**
1. `kiraci-alfa` ile bir konuşma kullan.
2. `kiraci-beta` ile aynı konuşmanın ögelerini listelemeyi dene.

**Girilecek veri**
```bash
curl -s -X POST "$APU/v1/responses" -H "$APB" -H "$TENANT_A" \
     -H "content-type: application/json" -d '{
  "model": "support",
  "conversation": "manuel-conv-043",
  "input": "Alfa kiracisinin gizli sohbeti."
}' > /dev/null

curl -s -w "\nHTTP: %{http_code}\n" "$APU/v1/conversations/manuel-conv-043/items" -H "$APB" -H "$TENANT_B"
```

**Beklenen sonuç**
- `HTTP: 404` — `kiraci-beta` `kiraci-alfa`'nın mesaj içeriğini **hiç göremez**.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-044 — `POST /v1/conversations/{id}/items` desteklenmez, düz 404 döner

Sınır durumu — gerçek OpenAI'den davranış farkı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | — |

`OpenAIConversationsEndpoints.Map` bu rotayı **hiç bağlamaz** — kodun kendi
belgesi bunu bilinçli bir tasarım kararı olarak açıklar (geçmişe doğrudan
mesaj yazmak agent bağlantısı gerektirir). Bu case gerçek istemcinin göreceği
HATA BİÇİMİNİ kaydeder: rota yoksa istek `OpenAICompatSupport.Error`'a hiç
ulaşmaz, ASP.NET Core'un **çıplak** yönlendirme 404'üne düşer.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olan bir konuşmaya doğrudan öge eklemeyi dene.

**Girilecek veri**
```bash
curl -s -D - -o /tmp/ap-compat-044.txt -X POST "$APU/v1/conversations/manuel-conv-034-deneme/items" \
     -H "$APB" -H "content-type: application/json" -d '{
  "role": "user",
  "content": [{"type": "input_text", "text": "Bunu ekle."}]
}'

cat /tmp/ap-compat-044.txt
```

**Beklenen sonuç**
- `HTTP: 404`.
- Gövde `{"error":{"message":...}}` biçiminde **DEĞİLDİR** — ASP.NET Core'un
  varsayılan boş 404 yanıtıdır (`content-type` bile `application/json`
  olmayabilir). Gerçek OpenAI istemcisi bu farkı `error.type` okuyarak değil,
  yalnız HTTP durum kodundan anlar — istemci kodu bu ayrımı hesaba katmalıdır.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Stok Python `openai` SDK ile uçtan uca (K-036 / K-043 doğrulaması)

Bu bölüm [`00-INDEKS.md`](00-INDEKS.md)'nin ortam tablosundaki *"Python — stok
`openai` istemcisi ile uyumluluk doğrulanır"* maddesini karşılar. K-036/K-043
kararları bu akışı `openai` **2.52.0** ile bir kez ölçtü (üretim oturumunda);
bu case'ler insan tarafından **yeniden** koşulur.

### MT-COMPAT-045 — `responses.create` + `responses.stream` + `previous_response_id` zinciri

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-036 |

**Ön koşul**
- `pip show openai` sürümü göstermeli. Örnek uygulama çalışıyor.

**Adımlar**
1. Python betiğini çalıştır.

**Girilecek veri**
```python
# /tmp/ap-compat-045.py
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:5080/agentprism/v1",
    api_key="manuel-test-token-2026",
)

resp = client.responses.create(model="support", input="ORD-1001 siparisim nerede?")
print("Yanit:", resp.output_text)
print("Kimlik:", resp.id)

print("--- Akis ---")
stream = client.responses.create(model="support", input="Kisaca merhaba de.", stream=True)
for event in stream:
    print(event.type)

resp2 = client.responses.create(
    model="support",
    input="Az once hangi siparis numarasini sordum?",
    previous_response_id=resp.id,
)
print("Zincirlenmis yanit:", resp2.output_text)
```
```bash
python3 /tmp/ap-compat-045.py
```

**Beklenen sonuç**
- İlk `print` `ORD-1001` dizgisini içerir.
- Akış bölümü `response.created` ile başlayan, `response.completed` ile biten
  bir olay adları dizisi yazdırır — istisna fırlatılmaz (resmi SDK MAF'ın
  ürettiği çerçeveleri sorunsuz çözümler).
- Zincirlenmiş yanıt `ORD-1001` dizgisini yeniden içerir (geçmiş korunmuş).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-046 — `conversations.create/retrieve/items.list/delete` zinciri

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-043 |

**Ön koşul**
- MT-COMPAT-045 ile aynı.

**Adımlar**
1. Python betiğini çalıştır.

**Girilecek veri**
```python
# /tmp/ap-compat-046.py
from openai import OpenAI

client = OpenAI(
    base_url="http://localhost:5080/agentprism/v1",
    api_key="manuel-test-token-2026",
)

conv = client.conversations.create(metadata={"kaynak": "manuel-test-046"})
print("Konusma kimligi:", conv.id)
assert conv.id.startswith("conv_")

client.responses.create(model="support", input="Merhaba.", conversation=conv.id)

got = client.conversations.retrieve(conv.id)
print("Olusturma zamani:", got.created_at)

items = client.conversations.items.list(conv.id)
print("Oge sayisi:", len(items.data))
for item in items.data:
    print(" -", item.type)

deleted = client.conversations.delete(conv.id)
print("Silindi mi:", deleted.deleted)
```
```bash
python3 /tmp/ap-compat-046.py
```

**Beklenen sonuç**
- `conv.id` `conv_` ile başlar.
- `items.data` en az bir `message` türü içerir.
- `deleted.deleted` `True`'dur.
- Hiçbir adımda SDK istisnası **fırlatılmaz** (resmi SDK'nın Conversations
  istemcisi tam uyumludur).

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-047 — `chat.completions.create` (akışlı/akışsız) + `NotFoundError` yakalama

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50 |
| **İlgili karar** | K-036 |

**Ön koşul**
- MT-COMPAT-045 ile aynı.

**Adımlar**
1. Python betiğini çalıştır.

**Girilecek veri**
```python
# /tmp/ap-compat-047.py
from openai import OpenAI, NotFoundError

client = OpenAI(
    base_url="http://localhost:5080/agentprism/v1",
    api_key="manuel-test-token-2026",
)

resp = client.chat.completions.create(
    model="support",
    messages=[{"role": "user", "content": "ORD-1001 siparisim nerede?"}],
)
print("Yanit:", resp.choices[0].message.content)

print("--- Akis ---")
stream = client.chat.completions.create(
    model="support",
    messages=[{"role": "user", "content": "Kisaca merhaba de."}],
    stream=True,
)
for chunk in stream:
    delta = chunk.choices[0].delta.content
    if delta:
        print(delta, end="", flush=True)
print()

try:
    client.chat.completions.create(
        model="hic-boyle-bir-agent",
        messages=[{"role": "user", "content": "x"}],
    )
    print("HATA: istisna beklenirdi")
except NotFoundError as e:
    print("NotFoundError yakalandi, status_code:", e.status_code)
```
```bash
python3 /tmp/ap-compat-047.py
```

**Beklenen sonuç**
- İlk yanıt `ORD-1001` içerir.
- Akış bölümü kesintisiz metin yazdırır, istisna fırlatmaz.
- `NotFoundError` yakalanır ve `status_code == 404`'tür — resmi SDK
  AgentPrism'in `model_not_found` zarfını kendi istisna hiyerarşisine
  doğru eşler.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Erişim denetimi (temsilci set)

Loopback/uzak erişim ve API anahtarı kapsamlarının derinlemesine matrisi
`13-KIRACI-VE-GUVENLIK.md`'nindir. Burada yalnız iki temsilci case var:
`v1/*` uçlarının da aynı `AgentPrismEndpointFilter`'dan geçtiğinin kanıtı.

### MT-COMPAT-048 — `Authorization` başlığı eksikse `401`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50, 9 |
| **İlgili karar** | — |

**Ön koşul**
- `AgentPrism:Ui:AuthToken` ayarlı (`manuel-test-token-2026`).

**Adımlar**
1. `Authorization` başlığı olmadan `/v1/responses` çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/responses" \
     -H "content-type: application/json" -d '{"model":"support","input":"Merhaba"}'
```

**Beklenen sonuç**
- `HTTP: 401`.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-COMPAT-049 — Yanlış bearer token `401`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 50, 9 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-TOKEN-02` (`yanlis-token`).

**Adımlar**
1. Geçersiz bir bearer token ile `/v1/chat/completions` çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/v1/chat/completions" \
     -H "Authorization: Bearer yanlis-token" -H "content-type: application/json" \
     -d '{"model":"support","messages":[{"role":"user","content":"Merhaba"}]}'
```

**Beklenen sonuç**
- `HTTP: 401`.
- Yanıt gövdesi boş ya da genel bir 401 sayfasıdır — `AgentPrismEndpointFilter`
  compat uçlarına da uygulanır, ayrı bir kimlik doğrulama yolu yoktur.

**Gerçek sonuç**
> _(koşum sırasında doldurulur)_

**Durum:** ☐ Beklemede · ☐ Geçti · ☐ Kaldı · ☐ Atlandı

---

## Bu dosyada kanıtlanmayan, kod okurken fark edilen şüpheler

Bu bölüm bir kusur listesi değildir — koşum aşamasında doğrulanacak
**şüphelerdir** (`PROMPT.md` §7.3).

- **MT-COMPAT-029**: `cancel_order` gibi onay gerektiren bir tool çağrısının
  `/v1/responses` üzerinden tam olarak nasıl serileştiği (`OpenAIResponses
  .WriteResponse`'un `ToolApprovalRequestContent`'i nasıl işlediği) MAF'ın
  kendi kaynağından **doğrulanmadı** — yalnız AgentPrism tarafındaki
  `agent.RunAsync` çağrısının hiçbir onay-özel dallanma taşımadığı görüldü.
  Olası sonuçlar: boş `output`, yarım metin veya MAF'ın kendi hata fırlatması.
  Koşum gerçek değeri kaydeder; tutarsızlık çıkarsa HATA şablonuyla kaydedilir.
- **MT-COMPAT-028 — DÜZELTİLDİ (2026-08-10).** `OpenAIResponsesEndpoints
  .ResponsesStream`'in dış `catch`'i artık dar bir tip listesiyle sınırlı
  değil; `OperationCanceledException` dışındaki HER istisna bir `error`/
  `response.failed` çerçevesine dönüşür. MAF'ın `WriteResponseStreamAsync`'inin
  KENDİ İÇİNDE ayrıca bir çevirme yapıp yapmadığı hâlâ decompile ile
  doğrulanmadı ama artık dış katman zaten her durumu yakaladığı için pratik
  sonucu değiştirmiyor.
- **MT-COMPAT-041 — `has_more` kısmı DÜZELTİLDİ (2026-08-10).** Kırpma
  gerçekleştiğinde artık `true` döner. Gerçek OpenAI'nin `items.list`
  sözleşmesindeki `after`/`before` imleç parametreleri hâlâ desteklenmiyor —
  bu bir kusur değil, ayrı bir **yetenek** boşluğudur (`limit`'in ötesinde
  sayfa isteme yolu yok); kapsam dışı bırakıldı.
