# 22 — Guardrail ve Yapılandırılmış Çıktı (`GUARD`)

> **Alan kodu:** `GUARD` · **Faz:** 38, 48
> **Kaynak:** `src/AgentPrism.Abstractions/Agents/ResponseFormat.cs` ·
> `Agents/ModelBinding.cs` (`ResponseFormat` alanı) ·
> `Models/ModelDescriptor.cs` (`SupportsStructuredOutput` alanı) ·
> `src/AgentPrism.Abstractions/Guards/` (tümü: `IContentGuard`, `ContentGuardContext`,
> `ContentGuardResult`) · `AgentPrismException.cs`
> (`AgentPrismContentBlockedException`) · `Runs/RunEventType.cs` (`ContentMasked`/`ContentBlocked`) ·
> `Runs/RunErrorClass.cs` (`ContentBlocked`) ·
> `src/AgentPrism.Core/Guards/` (tümü) ·
> `src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs`
> (`BuildResponseFormat`/`CheckStructuredOutputCapability`/`FindModelDescriptor`) ·
> `src/AgentPrism.Core/Models/ModelProviderRegistry.cs` (boru hattı sırası) ·
> `src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs`
> (`content_blocked` → `422`/SSE `error`) ·
> `src/AgentPrism.UI/frontend/src/screens/agent-editor.tsx`, `agent-detail.tsx`,
> `models.tsx`, `run-detail.tsx`.
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** [`kosumlar/2026-08-13/22-GUARDRAIL-VE-YAPISAL-CIKTI.md`](kosumlar/2026-08-13/22-GUARDRAIL-VE-YAPISAL-CIKTI.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır.

---

## Bu dosya neyi kanıtlar

İki bağımsız genişleme yüzeyi, aynı katmanda (`IChatClient` boru hattı) yaşadıkları
için tek dosyada toplanır: **yapılandırılmış çıktı** (`ModelBinding.ResponseFormat`
→ `ChatOptions.ResponseFormat`, Faz 38) ve **içerik guardrail'leri**
(`IContentGuard` → `ContentGuardingChatClient`, Faz 48).

```mermaid
flowchart TD
    A["ContentFilterDetectingChatClient"] --> B["devre kesici"]
    B --> C["AttachmentResolvingChatClient"]
    C --> D["FunctionInvokingChatClient (tool dongusu)"]
    D --> E["OpenTelemetry"]
    E --> F["ContentGuardingChatClient — Faz 48"]
    F --> G["saglayicinin HAM istemcisi"]
    G -.->|"ChatOptions.ResponseFormat — Faz 38"| H["saglayici JSON semasina uyar"]
    F -.->|"giris engellendi"| X["aga HIC cikilmaz"]

    classDef guard fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
    class F,X guard
```

🚨 Guard tool çağrı döngüsünün **İÇİNDE**, devre kesicinin **DIŞINDA** durur
(K-320/K-321, Faz 48'in kendi planını düzelten ölçüm — bkz. dosyanın S1 notu).
Yapılandırılmış çıktı bu boru hattına dokunmaz; yalnız `IModelProvider`'ın döndürdüğü
ham istemciye `ChatOptions.ResponseFormat` alanı iletilir.

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| API anahtarı kapsamı / rol denetiminin genel davranışı | `13-KIRACI-VE-GUVENLIK.md` |
| SSE genel mekanizması (`Last-Event-ID`, yeniden bağlanma) | `11-ARAYUZ-RUN-SESSION-SSE.md` |
| `ProblemDetails` zarfının genel sözleşmesi | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Devre kesicinin sağlayıcı hatalarında AÇILMASI (genel davranış) | `05-SAGLAYICI-OPENAI.md` / `06-SAGLAYICI-DIGER.md` (zaten üretildi) — bu dosya yalnız guard'ın onu **tetikleMEdiğini** sınar |
| Azure AI Content Safety adaptörü | Kapsam dışı — henüz yok (48.6, aday kalem F-87+) |
| Tool **argümanı** denetimi (çağrıdan önce) | Kapsam dışı — F-61, henüz yok |
| `/v1/chat/completions`, `/v1/responses` uçlarındaki aynı SSE `error` boşluğu | `08-OPENAI-UYUMLU-UCLAR.md` (zaten üretildi, `MT-COMPAT-028`) |
| Playground'da SSE `error` çerçevesinin sessiz yutulması | `10-ARAYUZ-AGENT-PLAYGROUND.md` (zaten üretildi, `MT-UIAG-043`) |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama gerçek bir OpenAI anahtarıyla çalışır:
   `cd samples/AgentPrism.Api && dotnet run` → `http://localhost:5080`.
3. `AgentPrism:Ui:AuthToken` `manuel-test-token-2026`'dır.

   ```bash
   export APB="Authorization: Bearer manuel-test-token-2026"
   export APU="http://localhost:5080/agentprism"
   ```

4. 🚨 **Örnek uygulamanın guard'ı ZATEN AÇIKTIR ve KOD ile ayarlıdır**
   (`samples/AgentPrism.Api/Program.cs:119-122`):
   ```csharp
   .AddPatternContentGuard(options =>
   {
       options.MaskedPii = PiiPatterns.CreditCard | PiiPatterns.Email | PiiPatterns.ProviderApiKey;
       options.DeniedTerms.Add("gizli-proje");
   })
   ```
   Bu satır **kod** ile yazıldığı için `dotnet user-secrets` ile ezilemez
   (`AddPatternContentGuard`'ın kendi XML belgesi: "kodda yazılan değer
   `AgentPrism:ContentGuard:Pattern` bölümünü geçersiz kılar", K4). §6'daki
   bazı case'ler (TC kimlik numarası, iki guard önceliği, sıfır maliyet) bu
   yüzden örnek uygulamayı KULLANMAZ; kendi `AgentPrismTestHost` konsol
   projesini kurar (İzlek A/C karışımı, aşağıda her case kendi kurulumunu taşır).
5. §1–§2 (yapılandırılmış çıktı, derleme anı doğrulama) ağa **hiç çıkmaz** —
   geçersiz kombinasyonlar derleme anında (SAVE değil, RUN/derleme anında)
   reddedilir, model hiç çağrılmaz.

> **Gerçek para uyarısı.** §1 (5 case), §4 (3 case) ve §5–§7 (13 case) gerçek
> OpenAI çağrısı yapar; hepsi küçük ölçekli tek turlu istemlerdir. §2, §3
> (yalnız MT-GUARD-022 hariç) ve §8 hiçbir model çağırmaz.

---

# 1 — Yapılandırılmış çıktı: kip dönüşümü ve gerçek çalıştırma (Faz 38)

### MT-GUARD-001 — `JsonSchema` kip: yanıt şemaya uyan geçerli JSON'dur

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-208 (desen), K-034 (derleme anı doğrulama) |

Faz kapanışında gerçek `gpt-5.4-mini` ile doğrulanmış senaryonun tekrarı
(`docs/arsiv/fazlar/38-YAPILANDIRILMIS-CIKTI.md`, 2026-08-06 koşumu).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `JsonSchema` çıktı biçimli bir agent tanımı kaydet.
2. Agent'ı akışsız modda (`Idempotency-Key`) çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "fatura-okuyucu",
  "instructions": "Faturayi ozetle ve JSON dondur.",
  "model": {
    "provider": "openai", "model": "gpt-5.4-mini",
    "responseFormat": {
      "kind": "JsonSchema", "schemaName": "invoice",
      "schema": { "type": "object",
                  "properties": { "total": { "type": "number" }, "currency": { "type": "string" } },
                  "required": ["total", "currency"] }
    }
  }
}' | jq '{name, responseFormat: .model.responseFormat}'

curl -s -X POST "$APU/api/agents/fatura-okuyucu/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Toplam 1250 TL, para birimi Turk Lirasi."}' | jq '.response.text'
```

**Beklenen sonuç**
- `POST /api/agents` `201` döner; `model.responseFormat.kind` `"JsonSchema"` olarak kayıtlıdır.
- `run` yanıtındaki metin **geçerli bir JSON belgesidir** (`jq` hatasız ayrıştırır).
- Belge `total` (sayısal tip) ve `currency` (dizgi tip) alanlarını taşır (şemanın `required` listesi).
- Alan **değerleri** (örn. tam olarak `1250`) iddia edilmez — yalnız yapı ve tip.

---

### MT-GUARD-002 — `Json` kip: şema yok, yalnız "geçerli JSON" zorunluluğu

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Json` çıktı biçimli (şemasız) bir agent tanımla.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "json-serbest",
  "instructions": "Kullanicinin istedigi bilgiyi JSON olarak dondur.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "Json" } }
}'

curl -s -X POST "$APU/api/agents/json-serbest/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Bir sehir adi ve nufusunu JSON olarak dondur."}' | jq '.response.text'
```

**Beklenen sonuç**
- `201` döner (`schema` alanı gönderilmedi, `Kind=Json` tek başına geçerlidir).
- Yanıt metni `jq` ile hatasız ayrıştırılan geçerli bir JSON'dur.
- Alan adları/şekli **serbesttir** — MT-GUARD-001'in aksine hiçbir şema dayatılmaz.

---

### MT-GUARD-003 — `Text` kip: `null`'dan ayrı, kayıtlı bir tercih

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

`Text`, `null`'dan davranışsal olarak farklıdır: `null` "hiçbir şey söyleme",
`Text` "açıkça düz metin iste" demektir (38.1). İkisi arayüzde ayrı görünmelidir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Text` çıktı biçimiyle bir agent tanımla.
2. Arayüzde agent detay ekranını aç, sürüm karşılaştırma tablosuna bak.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "duz-metin",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "Text" } }
}' | jq '.model.responseFormat'
```
Sonra tarayıcıda: `http://localhost:5080/agentprism/agents/duz-metin`.

**Beklenen sonuç**
- `POST` yanıtı `{"kind":"Text"}` döner (`kind` **dizgi** olarak, sayısal değil —
  `AgentResponseFormatKind`'ın `JsonStringEnumConverter` özniteliği taşıdığının kanıtı).
- Agent detay ekranının versiyon geçmişi bölümünde bir alan diff'i varsa
  `responseFormat` satırı `Text` değerini gösterir.

---

### MT-GUARD-004 — Akışlı (varsayılan SSE) yolda `JsonSchema` kip çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

Faz 38'in Açık Soru 5'inin (akışlı yolda şema destekleniyor mu) doğrudan
tekrarı. `Idempotency-Key` **verilMEZ** — varsayılan SSE dalı kullanılır.

**Ön koşul**
- MT-GUARD-001 geçti (`fatura-okuyucu` var).

**Adımlar**
1. Aynı agent'ı `Idempotency-Key` olmadan çalıştır.
2. `update` çerçevelerindeki metin parçalarını birleştir.

**Girilecek veri**
```bash
curl -s -N -X POST "$APU/api/agents/fatura-okuyucu/run" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"message":"Toplam 980 TL, para birimi Turk Lirasi."}'
```

**Beklenen sonuç**
- `event: run` sonra bir dizi `event: update` çerçevesi gelir; `event: error` **görünmez**.
- `update` çerçevelerindeki metin delta'ları birleştirildiğinde geçerli, şemaya
  uyan bir JSON belgesi elde edilir (`total`, `currency` alanları).

---

### MT-GUARD-005 — `responseFormat` hiç verilmezse davranış değişmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K1 |

Negatif/regresyon kontrolü. `support` fixture agent'ı `responseFormat` alanına
hiç dokunmadan var olan bir tanımdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `support` agent'ını normal çalıştır.
2. Tanımı oku.

**Girilecek veri**
```bash
curl -s "$APU/api/agents/support" -H "$APB" | jq '.model.responseFormat'

curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Merhaba"}' | jq '.response.text'
```

**Beklenen sonuç**
- `.model.responseFormat` `null` döner (ya alan yok ya da JSON `null` — ikisi de kabul edilir), `500` **verilmez**.
- `run` normal serbest metin döner (JSON zorlaması yok).

### MT-GUARD-010 — `Kind=JsonSchema`, `Schema` boş: SAVE kabul eder, RUN reddeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-034 |

Negatif senaryo. Faz kapanışında gerçek koşumla doğrulanmış örnek.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Schema` alanı olmadan `JsonSchema` kipi seçilmiş bir tanım kaydet.
2. `SAVE` yanıtını oku.
3. Aynı agent'ı çalıştır.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "kirik",
  "instructions": "x",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "JsonSchema" } }
}' | head -5

curl -s -i -X POST "$APU/api/agents/kirik/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- Adım 2: `HTTP/1.1 201 Created`.
- Adım 3: `HTTP/1.1 400 Bad Request`; gövde `"title":"Agent derlenemedi"` ve
  detayda `kirik` adı ile `Schema` alan adını taşır (`"...JsonSchema cikti kipini
  secti ancak Schema vermedi."`).
- Model **hiç çağrılmaz** (derleme, dispatch'ten önce durur).

---

### MT-GUARD-011 — `Kind=Text` ama `Schema` dolu: RUN reddeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-034 |

Negatif senaryo. Çelişkili tanım: kullanıcı şema yazdığını sanır, ama `Text`
kipinde şema hiç gitmez — bu yüzden reddedilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Kind=Text` VE `Schema` dolu bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "celiskili-text",
  "instructions": "x",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "Text", "schema": {"type":"object","properties":{}} } }
}' | jq '.name'

curl -s -i -X POST "$APU/api/agents/celiskili-text/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- SAVE `201` döner.
- RUN `400`; detay `celiskili-text` adını, `'Text' cikti kipini secti ancak
  Schema da verdi` ifadesini taşır.

---

### MT-GUARD-012 — `Kind=Json` ama `Schema` dolu: aynı red deseni

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-034 |

Negatif senaryo. MT-GUARD-011'in ikinci kombinasyonu (kod aynı `if` bloğunu
`Kind == Json` için de çalıştırır).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Kind=Json` VE `Schema` dolu bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "celiskili-json",
  "instructions": "x",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "Json", "schema": {"type":"object","properties":{}} } }
}'

curl -s -i -X POST "$APU/api/agents/celiskili-json/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- SAVE `201`.
- RUN `400`; detay `'Json' cikti kipini secti ancak Schema da verdi` ifadesini taşır.

---

### MT-GUARD-013 — `Schema` bir JSON nesnesi değilse RUN reddeder

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

Negatif senaryo. Sağlayıcı bunu her hâlükârda reddederdi; hata derlemeye çekilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Schema` alanına bir JSON **dizisi** (nesne değil) veren bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "dizi-sema",
  "instructions": "x",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "JsonSchema", "schema": [1, 2, 3] } }
}'

curl -s -i -X POST "$APU/api/agents/dizi-sema/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- SAVE `201` (dizi de geçerli bir `JsonElement`'tir, SAVE anında tip denetimi yoktur).
- RUN `400`; detay `dizi-sema` adını ve `Schema alani bir JSON nesnesi olmalidir` ifadesini taşır.

---

### MT-GUARD-014 — Boş obje şema (`{}`) derlemeyi geçer, hiçbir alanı zorlamaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | Açık Soru 2 |

Sınır senaryosu. 38.2: "Şema **içeriği** doğrulanmaz... yalnız 'nesne mi'
denetlenir." `{"type":"object","properties":{}}` tam olarak
`agent-editor.tsx`'in şema kutusunun **varsayılan** değeridir
(`DEFAULT_SCHEMA_TEXT`) — kullanıcı JsonSchema kipini seçip kutuyu hiç
düzenlemezse bu boş şemayla kaydeder.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Boş obje şemasıyla bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "bos-sema",
  "instructions": "Kisa bir yanit uret.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini",
             "responseFormat": { "kind": "JsonSchema", "schemaName": "serbest",
                                  "schema": {"type":"object","properties":{}} } }
}'

curl -s -X POST "$APU/api/agents/bos-sema/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}' | jq '.response.text'
```

**Beklenen sonuç**
- SAVE `201`, RUN `400` **verilmez** — derleme geçer.
- Yanıt geçerli bir JSON nesnesidir ama içeriği (hangi alanları taşıdığı)
  **serbesttir** — şema hiçbir alanı zorunlu kılmaz. Bu, arayüzden JsonSchema
  kipini seçip şema kutusunu boş bırakan bir kullanıcının fiilen **hiçbir kısıt
  uygulamadığı** anlamına gelir; hata da almaz.

### MT-GUARD-020 — Desteklemeyen modelde `JsonSchema` kipi RUN'da reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-267 |

Negatif senaryo. 🚨 Anthropic anahtarının **geçerli olması gerekmez** —
denetim, sağlayıcıya hiç bağlanmadan derleme anında durur.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `provider: anthropic, model: claude-sonnet-5` ile `JsonSchema` kipi seçilmiş bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "claude-yapisiz-kip",
  "instructions": "x",
  "model": { "provider": "anthropic", "model": "claude-sonnet-5",
             "responseFormat": { "kind": "JsonSchema", "schemaName": "deneme",
                                  "schema": {"type":"object","properties":{"x":{"type":"string"}},"required":["x"]} } }
}'

curl -s -i -X POST "$APU/api/agents/claude-yapisiz-kip/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- SAVE `201`.
- RUN `400`; detay `anthropic/claude-sonnet-5` ve `yapilandirilmis cikti
  desteklemiyor` ifadesini taşır.
- Hiçbir Anthropic API çağrısı yapılmaz (istek derleme aşamasında durur).

---

### MT-GUARD-021 — Aynı model, `Json` kipinde de reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-267 |

Negatif senaryo. `CheckStructuredOutputCapability` hem `JsonSchema` hem `Json`
dalında çağrılır (`AgentDefinitionCompiler.cs:453-457`).

**Ön koşul**
- MT-GUARD-020 geçti.

**Adımlar**
1. Aynı modelle, `Json` kipi (şemasız) seçilmiş bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "claude-json-kip",
  "instructions": "x",
  "model": { "provider": "anthropic", "model": "claude-sonnet-5",
             "responseFormat": { "kind": "Json" } }
}'

curl -s -i -X POST "$APU/api/agents/claude-json-kip/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- RUN `400`, aynı `yapilandirilmis cikti desteklemiyor` deseni.

---

### MT-GUARD-022 — Aynı modelde `Text` kipi HER ZAMAN izinlidir (kontrol grubu)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-267 |

K-267'nin doğrudan pozitif kanıtı: `SupportsStructuredOutput=false`
`Text` kipini etkilemez (38.3'ün "Text için anlamsız" gerekçesi). 🚨 Bu case
**gerçek** bir Anthropic çağrısı yapar (küçük ölçekte, gerçek para).

**Ön koşul**
- MT-GUARD-020 geçti. Geçerli bir Anthropic API anahtarı tanımlı
  (`dotnet user-secrets set "AgentPrism:Providers:Anthropic:ApiKey" "<ANAHTAR>"`).

**Adımlar**
1. Aynı modelle, `Text` kipi (açıkça) seçilmiş bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "claude-duz-metin",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "anthropic", "model": "claude-sonnet-5",
             "responseFormat": { "kind": "Text" } }
}'

curl -s -i -X POST "$APU/api/agents/claude-duz-metin/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Merhaba, bir cumlede kendini tanit."}'
```

**Beklenen sonuç**
- RUN `400` **ALINMAZ** — `yapilandirilmis cikti desteklemiyor` mesajı görünmez.
- Gerçek bir Anthropic yanıtı döner (serbest metin).

---

### MT-GUARD-023 — Kataloğa kayıtlı olmayan modelde denetim ATLANIR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | K-032 |

Sınır senaryosu. `FindModelDescriptor` modeli kataloğunda **bulamazsa**
yetenek denetimi atlanır (bilinmeyen model reddedilmez — K-032). Bu case
istemli olarak var olmayan bir model adı kullanır; gerçek sağlayıcı çağrısı
ayrı ve **beklenen** bir hatayla (model bulunamadı) başarısız olur — bu
başarısızlık yapılandırılmış çıktı denetiminden **değildir**.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `provider: openai`, kataloğa hiç eklenmemiş bir model adıyla `JsonSchema` kipli bir tanım kaydet.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "bilinmeyen-model-kip",
  "instructions": "x",
  "model": { "provider": "openai", "model": "gpt-9-hic-boyle-bir-model-yok",
             "responseFormat": { "kind": "JsonSchema", "schemaName": "deneme",
                                  "schema": {"type":"object","properties":{"x":{"type":"string"}}} } }
}'

curl -s -i -X POST "$APU/api/agents/bilinmeyen-model-kip/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}'
```

**Beklenen sonuç**
- SAVE `201`.
- RUN'da `400` + `yapilandirilmis cikti desteklemiyor` mesajı **GÖRÜNMEZ**
  (derleme geçer, çünkü model kataloğunda bulunamadı → denetim atlandı).
- Bunun yerine sağlayıcıdan kaynaklanan **farklı** bir hata görülür (tipik
  olarak `502` — gerçek OpenAI çağrısı model adının geçersizliğinden başarısız
  olur). Kritik olan **hangi hata mesajı** değil, `yapilandirilmis cikti
  desteklemiyor` mesajının **hiç çıkmamasıdır**.

### MT-GUARD-030 — `responseFormat` ayarlanmamış bir tanım `500` vermez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | 38.5 |

Sınır senaryosu. `ResponseFormat` nullable'dır; bu alanı hiç bilmeyen (Faz
38'den önce yazılmış) bir kayıt seri hâlden çıkarken çökmemelidir. `support`
zaten böyle bir kayıttır (kod tanımlı, `responseFormat` hiç belirtilmemiş).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `support` tanımını oku.

**Girilecek veri**
```bash
curl -s -i "$APU/api/agents/support" -H "$APB" | tail -30
```

**Beklenen sonuç**
- `HTTP: 200`.
- `model.responseFormat` `null`'dur (veya alan gövdede hiç yoktur) — **ikisi de kabul edilir**.
- `500` **dönmez**.

---

### MT-GUARD-031 — "structured output" rozeti yalnız destekleyen modellerde görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `GET /api/models` yanıtını oku.
2. Arayüzde model listesi ekranını aç.

**Girilecek veri**
```bash
curl -s "$APU/api/models" -H "$APB" | jq '.[] | select(.provider=="openai" or .provider=="anthropic") | {provider, model: .name, supportsStructuredOutput}'
```
Tarayıcıda: `http://localhost:5080/agentprism/models`.

**Beklenen sonuç**
- `openai/gpt-5.4-mini` (ve `gpt-5.6-luna`/`gpt-5.6-terra`) `supportsStructuredOutput: true`.
- `anthropic/claude-sonnet-5` (ve kardeşleri) `supportsStructuredOutput: false` veya alan yok.
- Arayüzde yalnız `true` olan modellerin kartında **"structured output"** rozeti görünür.

---

### MT-GUARD-032 — Arayüz, API'nin izin verdiği bozuk JSON'u SAVE anında engeller

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 38 |
| **İlgili karar** | — |

Sınır senaryosu — MT-GUARD-010'un **tam tersi**: `curl` ile boş/bozuk şema
SAVE anında kabul edilir (doğrulama RUN'a ertelenir), ama arayüzün kendi
istemci-taraflı kontrolü (`agent-editor.tsx:269`, `schemaJsonValid`) söz
dizimi hatalı JSON'da Kaydet düğmesini **daha erken** devre dışı bırakır.
Bu, `curl` ile bu korumanın **atlatılabildiğini** de gösterir (zaten MT-GUARD-010
kanıtladı).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Arayüzde yeni bir agent oluşturma ekranını aç.
2. Yanıt biçimi olarak `JsonSchema` seç.
3. Şema kutusuna söz dizimi hatalı bir metin yaz (örn. `{ bozuk`).

**Girilecek veri**
- Tarayıcı: `http://localhost:5080/agentprism/agents/new`.
- Şema kutusuna: `{ bozuk`

**Beklenen sonuç**
- Şema kutusunun altında bir hata notu belirir.
- **Kaydet düğmesi devre dışı kalır** — istek hiç gönderilmez.
- Kutuyu geçerli JSON'a (örn. varsayılan `{"type":"object","properties":{}}`) döndürünce düğme yeniden etkinleşir.

### MT-GUARD-040 — Eşleşmeyen istem guard açıkken değişmeden geçer

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-323 |

Kontrol grubu. Guard'ın varlığı **her** isteği bozmamalıdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Hiçbir yasak desen içermeyen bir istem gönder.

**Girilecek veri**
```bash
RUN=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"siparisim nerede"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN/events" -H "$APB" | jq '[.[] | select(.type=="ContentMasked" or .type=="ContentBlocked")] | length'
```

**Beklenen sonuç**
- Run normal tamamlanır.
- `ContentMasked`/`ContentBlocked` olay sayısı **0**'dır.

---

### MT-GUARD-041 — GİRİŞ maskeleme: kredi kartı numarası modele gitmeden maskelenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-321, K-325 |

Faz kapanışında gerçek OpenAI ile doğrulanmış senaryonun tekrarı.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Geçerli Luhn kontrol basamaklı bir kart numarası içeren istem gönder.
2. Çalıştırma olaylarını oku.
3. 🚨 Olay yükünde/metninde kart numarasının **geçmediğini** doğrula.

**Girilecek veri**
```bash
RUN=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"kart numaram 4539578763621486, tekrar eder misin"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN/events" -H "$APB" | jq '.[] | select(.type=="ContentMasked")'
curl -s "$APU/api/runs/$RUN/events" -H "$APB" | grep -c "4539578763621486"
```

**Beklenen sonuç**
- Bir `ContentMasked` olayı görünür; `payload` alanı `"guard":"pattern"`,
  `"rule":"credit-card"`, `"direction":"Input"` taşır.
- `grep -c "4539578763621486"` çıktısı **`0`**'dır — kart numarasının kendisi
  hiçbir olayda geçmez.
- Modelin yanıtı kart numarasını **tekrarlamaz** (modele zaten maskelenmiş
  hâli gitmiştir).

---

### MT-GUARD-042 — ÇIKIŞ maskeleme: istemde yok ama modelin ürettiği e-posta maskelenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-321 |

Girişte HİÇ e-posta yok; bu, guard'ın çıkışı da ayrıca denetlediğinin kanıtıdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Modeli sahte bir kurumsal e-posta uydurmaya zorla.
2. Yanıtı oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"Ornek bir kurumsal iletisim adresi uydur ve SADECE onu yaz."}' | jq -r '.response.text'
```

**Beklenen sonuç**
- Yanıt metni **tam olarak** `[redacted]` içerir (`MaskReplacement`'ın
  varsayılan değeri — bu, model metni değil guard'ın kendi sabit çıktısıdır,
  bu yüzden değişmez olarak iddia edilebilir).
- Yanıt hiçbir `@` işaretli dizgi taşımaz.

---

### MT-GUARD-043 — Yasak sözcük GİRİŞTE, AKIŞSIZ dalda `422` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-324 |

Negatif senaryo. Akışsız dal `Idempotency-Key` başlığıyla seçilir (Faz 43).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `DeniedTerms` listesindeki `gizli-proje` sözcüğünü içeren bir istemi akışsız modda gönder.

**Girilecek veri**
```bash
curl -s -i -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"gizli-proje hakkinda bilgi ver"}'
```

**Beklenen sonuç**
- `HTTP/1.1 422 Unprocessable Entity`.
- Gövde `"errorType":"content_blocked"`, `"guard":"pattern"`, `"rule":"denied-term"`, `"direction":"Input"` taşır.
- Gövde `gizli-proje` dizgisini **taşımaz**.

---

### MT-GUARD-044 — Aynı yasak sözcük AKIŞLI (varsayılan SSE) dalda: `error` çerçevesi

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-324 (S4) |

Negatif senaryo. SSE başlıkları çalıştırma **başlamadan** gönderilir; `422`
fiziksel olarak imkânsızdır. Engelleme yerine `event: error` çerçevesi olarak
görünür.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Aynı istemi `Idempotency-Key` **olmadan** gönder.
2. SSE çerçevelerini oku.
3. `GET /api/runs/{id}` ile kalıcı kaydı doğrula.

**Girilecek veri**
```bash
curl -s -N -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" \
  -d '{"message":"gizli-proje hakkinda bilgi ver"}'
```
Yanıtta yakalanan `runId` ile:
```bash
curl -s "$APU/api/runs/$RUN" -H "$APB" | jq '{status, errorType: .error.type, errorClass: .error.class}'
```

**Beklenen sonuç**
- `HTTP/1.1 200 OK` (SSE başlıkları normal gönderilir).
- Akışta `event: run` sonra `event: error` gelir; `data` alanı
  `AgentPrismContentBlockedException` tipini taşır ama `gizli-proje` metnini **taşımaz**.
- `GET /api/runs/{id}` `status: "Failed"`, `errorType: "content_blocked"` döner —
  MT-GUARD-043 (akışsız) ile **aynı** kalıcı sonuç, yalnız HTTP taşıma katmanı farklıdır.

### MT-GUARD-050 — Geçersiz Luhn kontrol basamaklı 16 hane MASKELENMEZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | 48.4 |

Negatif senaryo. Bir sipariş numarası, kart deseniyle **yanlışlıkla**
eşleşmemelidir — bu, guard'ı kullanılamaz hâle getirirdi.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Kontrol basamağı **tutmayan** 16 haneli bir sayıyı "sipariş numarası" olarak gönder.

**Girilecek veri**
```bash
RUN=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"siparis numaram 1234567812345678, aynen tekrar et"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN/events" -H "$APB" | jq '[.[] | select(.type=="ContentMasked")] | length'
```

**Beklenen sonuç**
- `ContentMasked` olay sayısı **`0`**'dır (`1234567812345678` Luhn'a uymaz).
- Modelin yanıtı sayıyı **değişmeden** içerebilir (maskeleme uygulanmadı).

---

### MT-GUARD-052 — TC kimlik numarası: kontrol basamağı geçerliyse maskelenir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | 48.4 (S6 — onuncu basamak düzeltmesi) |

Örnek uygulama `TurkishNationalId` desenini AÇMAZ (yalnız `CreditCard\|Email\|
ProviderApiKey`); bu yüzden kendi `AgentPrismTestHost` konsol projesi gerekir.
🚨 Numaralar elle **hesaplandı ve Python ile doğrulandı**
(`CheckDigits.IsValidTurkishNationalId`'in tam algoritmasıyla): `12345678950`
geçerli, `12345678901` (sıradan görünen ama kontrol basamağı tutmayan) geçersizdir.

**Ön koşul**
- .NET SDK kurulu. Yerel NuGet feed hazır (`00-INDEKS.md` §2.3).

**Adımlar**
1. Bir konsol projesi kur, `AgentPrism.Testing` paketini ekle.
2. `TurkishNationalId` deseni açık bir guard ile bellek içi host kur.
3. Geçerli ve geçersiz numarayı ayrı ayrı çalıştır.

**Girilecek veri**
```bash
mkdir -p ~/agentprism-manuel/guard-testleri && cd ~/agentprism-manuel/guard-testleri
dotnet new console
SURUM=$(ls ~/agentprism-local-feed/AgentPrism.Testing.*.nupkg | sed 's#.*AgentPrism.Testing\.##;s#\.nupkg##')
dotnet add package AgentPrism.Testing --version "$SURUM"

cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await AgentPrismTestHost.StartAsync(o =>
{
    o.ModelProvider = provider;
    o.ConfigureAgentPrism = builder => builder
        .AddPatternContentGuard(opt => opt.MaskedPii = PiiPatterns.TurkishNationalId)
        .AddAgent(new AgentDefinition
        {
            Name = "kimlik-testi",
            Model = new ModelBinding { Provider = "fake", Model = "model-1" },
        });
});

var gecerli = await host.RunAsync("kimlik-testi", "kimlik numaram 12345678950, tekrar eder misin");
Console.WriteLine("GECERLI  (12345678950) -> durum=" + gecerli.Record.Status +
    " maskelenen=" + gecerli.Events.Count(e => e.Type == RunEventType.ContentMasked));

var gecersiz = await host.RunAsync("kimlik-testi", "kimlik numaram 12345678901, tekrar eder misin");
Console.WriteLine("GECERSIZ (12345678901) -> durum=" + gecersiz.Record.Status +
    " maskelenen=" + gecersiz.Events.Count(e => e.Type == RunEventType.ContentMasked));
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `GECERLI` satırı `maskelenen=1` yazar (kontrol basamakları algoritmayı geçer).
- `GECERSIZ` satırı `maskelenen=0` yazar (desen yapısal olarak eşleşse de —
  `\b[1-9][0-9]{10}\b` — kontrol basamağı doğrulaması reddeder).
- İkisi de `durum=Completed`'dir (maskeleme `Block` değildir, çalıştırma devam eder).

---

### MT-GUARD-053 — Sağlayıcı API anahtarı deseni (`sk-…`) maskelenir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | 48.4 |

`ProviderApiKey` deseni örnek uygulamada zaten açıktır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `sk-` önekli, gerçekçi uzunlukta sahte bir anahtar içeren istem gönder.

**Girilecek veri**
```bash
RUN=$(curl -s -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"yanlislikla anahtarimi yapistirdim: sk-th1sIsATestKeyN0tReal1234567890, silebilir misin"}' | jq -r '.runId')

curl -s "$APU/api/runs/$RUN/events" -H "$APB" | jq '.[] | select(.type=="ContentMasked")'
curl -s "$APU/api/runs/$RUN/events" -H "$APB" | grep -c "sk-th1sIsATestKeyN0tReal1234567890"
```

**Beklenen sonuç**
- Bir `ContentMasked` olayı `"rule":"provider-api-key"` (veya eşdeğer bir kural
  adı — gerçek adı koşumda kaydedilir), `"direction":"Input"` taşır.
- `grep -c` çıktısı **`0`**'dır — anahtarın kendisi hiçbir olayda geçmez.

---

### MT-GUARD-054 — 🚨 Patolojik e-posta deseni: zaman aşımı ile durur mu, hangi hata görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | 48.4 (Riskler tablosu) |

Sınır senaryosu — **şüpheli bulgu**. `EmailPattern`'in
(`@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)*\.[A-Za-z]{2,}"`)
`(?:\.[A-Za-z0-9\-]+)*` grubu, ardından gelen zorunlu `\.[A-Za-z]{2,}` ile
**klasik geri izleme (backtracking) patlaması** şekli taşır. `matchTimeoutMilliseconds:
1000` bunu sınırlamalıdır (DoD: "patolojik girdi kilitlemez"), ama uç noktanın
`catch` bloğu (`AgentEndpoints.cs`) yalnız `AgentPrismException`,
`InvalidOperationException`, `HttpRequestException` yakalar —
`RegexMatchTimeoutException` (`TimeoutException`'dan türer) **hiçbirine
uymaz**. Kod okumasıyla bu bir sızıntı gibi görünüyor; **koşulmadı**.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Çok sayıda tekrarlı `x.` bloğundan sonra geçersiz bir uzantıyla biten bir "e-posta benzeri" dizge gönder.
2. İsteğin ne kadar sürdüğünü ve hangi durum kodunun döndüğünü ölç.

**Girilecek veri**
```bash
LOCAL="a"
DOMAIN=$(python3 -c "print('x.' * 40, end='')")
curl -s -i -w "\nSURE: %{time_total}s\n" -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d "{\"message\":\"iletisim adresim ${LOCAL}@${DOMAIN}9 gecersiz uzanti\"}"
```
> Yeterince yavaş değilse (`SURE` 1 saniyenin çok altındaysa) `40` tekrarını
> `80`'e, sonra `160`'a çıkar.

**Beklenen sonuç (DoD'nin iddiası)**
- İstek **birkaç saniye içinde** biter (birkaç dakika değil) — zaman aşımı çalışır.

**Koşumda kaydedilecek asıl soru**
- Dönen HTTP durum kodu nedir? `400`/`422` (kontrollü) mü, `500`
  (yakalanmamış istisna) mü, yoksa bağlantı sıfırlanması mı?
- `GET /api/runs/{id}` (eğer bir `runId` yakalanabildiyse) çalıştırmayı nasıl
  sınıflandırıyor?
- Bu sonuç şüpheyi **doğrularsa** (yakalanmamış `500`), `00-INDEKS.md`'ye
  `Kusur, Önem: Orta-Yüksek` olarak eklenmelidir (veri kaybı yok, ama guard'ın
  "başarısız kapanır" sözleşmesi tutarsız bir hata yüzeyi üretiyor).

### MT-GUARD-060 — Denetim izinde engellenen metin YOK, kural adı VAR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-325, K-059 |

Negatif senaryo. `48.5`'in temel garantisi.

**Ön koşul**
- MT-GUARD-043 (veya benzer bir engelleme case'i) en az bir kez koşturuldu.

**Adımlar**
1. `content.blocked` aksiyonlu denetim kayıtlarını oku.
2. Engellenen metnin geçip geçmediğini ara.

**Girilecek veri**
```bash
curl -s "$APU/api/audit?action=content.blocked" -H "$APB" | jq '.[0]'
curl -s "$APU/api/audit?action=content.blocked" -H "$APB" | grep -c "gizli-proje"
```

**Beklenen sonuç**
- En az bir kayıt döner; `after` alanı `{"guard":"pattern","rule":"denied-term","direction":"Input","action":"Block"}` biçimindedir.
- `grep -c "gizli-proje"` çıktısı **`0`**'dır.
- Kayıt `entity` alanında `run:<runId>` taşır (hangi çalıştırma olduğu izlenebilir, içerik değil).

---

### MT-GUARD-061 — `RunErrorClass.ContentBlocked`, `ContentFiltered`'dan AYRIDIR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-326 |

"Model reddetti" (sağlayıcı filtresi) ile "bizim politikamız reddetti" (guard)
farklı kovalara düşmelidir — farklı eylemler gerektirirler.

**Ön koşul**
- MT-GUARD-043/044 koşturuldu.

**Adımlar**
1. Engellenen çalıştırmanın hata sınıfını oku.

**Girilecek veri**
```bash
curl -s "$APU/api/runs/$RUN" -H "$APB" | jq '{errorType: .error.type, errorClass: .error.class}'
```

**Beklenen sonuç**
- `errorType`: `"content_blocked"`.
- `errorClass`: `"ContentBlocked"` — `"ContentFiltered"` **DEĞİL** (o, sağlayıcının
  kendi filtresine ayrılmıştır, bkz. `06-SAGLAYICI-DIGER.md`).
- `/api/stats/errors` panosunda (varsa) bu iki sınıf **ayrı** satırlar olarak görünür.

---

### MT-GUARD-062 — Arka arkaya 10 engelleme devre kesiciyi AÇMAZ

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-322 |

Negatif senaryo. Guard devre kesicinin **dışındadır**; engelleme bir sağlayıcı
arızası sayılmaz.

**Ön koşul**
- Örnek uygulama çalışıyor. `openai` sağlayıcısı sağlıklı (`Closed`) durumda.

**Adımlar**
1. Aynı yasak sözcüğü arka arkaya 10 kez gönder.
2. Sağlayıcı sağlığını oku.
3. Normal (engellenmeyen) bir istek gönder.

**Girilecek veri**
```bash
for i in $(seq 1 10); do
  curl -s -o /dev/null -X POST "$APU/api/agents/support/run" \
    -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
    -d '{"message":"gizli-proje"}'
done

curl -s "$APU/api/models/health" -H "$APB" | jq '.[] | select(.provider=="openai") | {provider, state}'

curl -s -i -X POST "$APU/api/agents/support/run" \
  -H "$APB" -H "content-type: application/json" -H "Idempotency-Key: $(uuidgen)" \
  -d '{"message":"merhaba"}' | head -3
```

**Beklenen sonuç**
- Sağlık ucu `openai` için `state: "Closed"` (sağlıklı) döner — 10 engelleme
  **birikmedi**.
- Sonraki normal istek `200`/`201` ile başarıyla tamamlanır — sağlayıcı KAPANMAMIŞTIR.

---

### MT-GUARD-064 — 🚨 `GET /api/runs?errorType=...` filtresi SESSİZCE YOK SAYILIR

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | — |

Negatif senaryo — **şüpheli bulgu**. `docs/arsiv/fazlar/48-GUARDRAILS.md`'nin plan
aşamasındaki doğrulama komutu `GET /api/runs?errorType=content_blocked`
kullanıyordu, ama `RunEndpoints.cs:42-53`'teki `MapGet("/api/runs", ...)`
imzası yalnız `agentName`, `status`, `kind`, `sessionId`, `startedAfter`,
`includeChildren`, `parentRunId`, `rootRunId`, `skip`, `take` parametrelerini
bağlar — **`errorType` diye bir sorgu parametresi YOK**. ASP.NET Core minimal
API bağlanmamış bir sorgu parametresini sessizce yok sayar; bu yüzden bu
komut aslında **tüm** çalıştırmaları döndürüyor olmalıdır, yalnız
`content_blocked` olanları değil. (Faz kapanışının "gerçek koşum" bölümü bu
komutu tekrarlamadı — farklı bir doğrulama yoluna, tekil `GET /api/runs/{id}`
okumasına geçti; bu yüzden boşluk kapanışta hiç yakalanmamış olabilir.)

**Ön koşul**
- En az bir `content_blocked` (MT-GUARD-043) VE en az bir normal tamamlanmış
  (MT-GUARD-040) çalıştırma var.

**Adımlar**
1. `errorType=content_blocked` ile listele, sayıyı not al.
2. Aynı sorguyu `errorType` OLMADAN tekrarla, sayıyı karşılaştır.
3. Kontrol grubu: gerçek bağlı bir parametre olan `status=Failed` ile dene.

**Girilecek veri**
```bash
curl -s "$APU/api/runs?errorType=content_blocked" -H "$APB" | jq 'length'
curl -s "$APU/api/runs" -H "$APB" | jq 'length'
curl -s "$APU/api/runs?status=Failed" -H "$APB" | jq 'length'
```

**Beklenen sonuç (şüphenin doğrulanması)**
- İlk iki sayı **AYNIDIR** — `errorType` parametresi hiçbir filtreleme
  yapmadan tüm run'ları döndürür (bağlanmamış parametre sessizce yok sayılır).
- Üçüncü sayı (gerçek `status` filtresi) daha **KÜÇÜKTÜR** — endpoint'in
  KENDİSİ filtreleme yapabiliyor, yalnız `errorType` diye bir alan yok.
- Doğrularsa: bu ya bir **kusur** (plan/dokümantasyonun vaat ettiği bir
  filtre kod tarafında hiç uygulanmamış) ya da eski bir plan taslağının hiç
  gerçekleşmemiş kalıntısıdır — `00-INDEKS.md`'ye not düşülür.

### MT-GUARD-070 — Hiç guard kayıtlı değilken: sıfır maliyet, içerik DEĞİŞMEZ

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K1, K-323 |

K1'in ("sıfır sürpriz") çekirdek kanıtı: hiçbir guard eklenmediğinde
sarmalayıcı boru hattına hiç girmez.

**Ön koşul**
- MT-GUARD-052'nin proje kurulumu hazır.

**Adımlar**
1. `AddPatternContentGuard`/`AddContentGuard` **hiç çağrılmadan** bir host kur.
2. `ContentGuardPipeline.HasGuards` değerini oku.
3. Kredi kartı içeren bir istem çalıştır; modele giden metni oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/guard-testleri
cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;
using Microsoft.Extensions.DependencyInjection;

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await AgentPrismTestHost.StartAsync(o =>
{
    o.ModelProvider = provider;
    o.ConfigureAgentPrism = builder => builder.AddAgent(new AgentDefinition
    {
        Name = "guardsiz",
        Model = new ModelBinding { Provider = "fake", Model = "model-1" },
    });
    // DIKKAT: AddPatternContentGuard(...) veya AddContentGuard<T>() HIC cagrilmiyor.
});

var pipeline = host.Services.GetRequiredService<ContentGuardPipeline>();
Console.WriteLine("HasGuards: " + pipeline.HasGuards);

var sonuc = await host.RunAsync("guardsiz", "kart numaram 4539578763621486, tekrar eder misin");
Console.WriteLine("durum: " + sonuc.Record.Status);
Console.WriteLine("modelin gordugu metin: " + provider.Requests[0].Messages[^1].Text);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `HasGuards: False`.
- `durum: Completed`.
- "modelin gordugu metin" satırı `4539578763621486`'yı **DEĞİŞMEDEN** içerir
  — `[redacted]` **görünmez**. Hiçbir guard kayıtlı değilse denetim tamamen atlanır.

---

### MT-GUARD-071 — 🚨 Yalnız alakasız bir config anahtarı bile guard'ı DI'a kaydeder

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-323 — inceltilmiş sınır |

Sınır senaryosu — **şüpheli bulgu**. `AgentPrismServiceCollectionExtensions.cs:178-185`:
`patternSection.Exists()` DOĞRUYSA `PatternContentGuard` kaydedilir — bu
kontrol, bölümün **herhangi bir** alt anahtarının var olup olmadığına bakar,
`DeniedTerms`/`MaskedPii`'nin dolu olup olmadığına DEĞİL. Yani yalnızca
`MaskReplacement` gibi davranışsal olarak nötr bir anahtar ayarlamak bile
guard'ı DI konteynerine ekler (`HasGuards=true`), her ne kadar guard
hiçbir kuralı olmadığı için pratikte her zaman `Allow` dönsün. K1'in "kayıt
kapı" ilkesi ("hiç guard kayıtlı değilse maliyet sıfırdır") bu durumda
**incelmiş** olur: davranış aynı görünür ama pipeline artık boş değildir
(faz dokümanının kendi tahsis tablosunda "kayıtlı, hiç kural yok" satırı
736 B yerine 952 B/çağrı gösteriyordu).

**Ön koşul**
- .NET SDK kurulu. Yerel NuGet feed hazır.

**Adımlar**
1. `AgentPrism:ContentGuard:Pattern:MaskReplacement` DIŞINDA hiçbir anahtar
   içermeyen bir `IConfiguration` ile `AddAgentPrism(section)` çağır.
2. `ContentGuardPipeline.HasGuards` ve `PatternContentGuardOptions` içeriğini oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/guard-testleri
dotnet add package Microsoft.Extensions.Configuration

cat > Program.cs <<'EOF'
using AgentPrism;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var config = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AgentPrism:ContentGuard:Pattern:MaskReplacement"] = "***",
    })
    .Build();

var services = new ServiceCollection();
services.AddAgentPrism(config.GetSection("AgentPrism"));

var provider = services.BuildServiceProvider();
var pipeline = provider.GetRequiredService<ContentGuardPipeline>();
var options = provider.GetRequiredService<IOptionsMonitor<PatternContentGuardOptions>>().CurrentValue;

Console.WriteLine("HasGuards: " + pipeline.HasGuards);
Console.WriteLine("MaskedPii: " + options.MaskedPii);
Console.WriteLine("DeniedTerms sayisi: " + options.DeniedTerms.Count);
Console.WriteLine("MaskReplacement: " + options.MaskReplacement);
EOF

dotnet run -c Release
```

**Beklenen sonuç (şüphenin doğrulanması)**
- `HasGuards: True` — `AddPatternContentGuard()` HİÇ çağrılmadığı, `DeniedTerms`/
  `MaskedPii` HİÇ ayarlanmadığı hâlde guard kayıtlıdır.
- `MaskedPii: None`, `DeniedTerms sayisi: 0` — guard hiçbir kuralı yoktur ve
  pratikte hep `Allow` döner.
- `MaskReplacement: ***` — yalnız bu tek anahtar bağlandı.
- Doğrularsa: K1'in "kayıt = maliyet" denklemi tam doğru değildir; sıfır
  maliyet iddiası yalnız `ContentGuard` bölümü **hiç yoksa** geçerlidir, bölüm
  var ama boşsa değil.

---

### MT-GUARD-072 — İki guard'tan `Block` kazanır, KAYIT SIRASINDAN bağımsız

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | 48.3, Açık Soru 2 |

**Ön koşul**
- MT-GUARD-052'nin proje kurulumu hazır.

**Adımlar**
1. Biri her zaman `Mask`, diğeri her zaman `Block` döndüren iki özel guard tanımla.
2. Önce Mask'ı, sonra Block'u kaydederek çalıştır.
3. Kayıt sırasını TERSİNE çevirip tekrar çalıştır.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/guard-testleri
cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;

internal sealed class HepMaskele : IContentGuard
{
    public string Name => "hep-maskele";
    public ValueTask<ContentGuardResult> InspectAsync(ContentGuardContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ContentGuardResult.Mask("[TEST-MASKE]", "test-mask-kural"));
}

internal sealed class HepEngelle : IContentGuard
{
    public string Name => "hep-engelle";
    public ValueTask<ContentGuardResult> InspectAsync(ContentGuardContext context, CancellationToken cancellationToken = default)
        => ValueTask.FromResult(ContentGuardResult.Block("test-engel-kural", "her zaman engeller"));
}

async Task<string> DeneAsync(bool maskeOnce)
{
    var provider = new FakeModelProvider().EchoesUserMessage();

    await using var host = await AgentPrismTestHost.StartAsync(o =>
    {
        o.ModelProvider = provider;
        o.ConfigureAgentPrism = builder =>
        {
            if (maskeOnce)
            {
                builder.AddContentGuard<HepMaskele>().AddContentGuard<HepEngelle>();
            }
            else
            {
                builder.AddContentGuard<HepEngelle>().AddContentGuard<HepMaskele>();
            }

            builder.AddAgent(new AgentDefinition
            {
                Name = "iki-guard",
                Model = new ModelBinding { Provider = "fake", Model = "model-1" },
            });
        };
    });

    var sonuc = await host.RunAsync("iki-guard", "merhaba");

    return $"durum={sonuc.Record.Status} hataTipi={sonuc.Record.Error?.Type}";
}

Console.WriteLine("Mask-once Block:  " + await DeneAsync(maskeOnce: true));
Console.WriteLine("Block-once Mask:  " + await DeneAsync(maskeOnce: false));
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- İki satır da `durum=Failed hataTipi=content_blocked` yazar — **kayıt
  sırasından bağımsız** olarak `Block` kazanır.

---

### MT-GUARD-073 — Guard istisna atarsa çalıştırma BAŞARISIZ olur

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | Açık Soru 3 — K-089'un ruhu |

Negatif senaryo. "Gözlemlenebilirlik işlevselliği bozmaz" kuralı **guard için
geçerli değildir** — guard bir gözlem aracı değil bir kontroldür.

**Ön koşul**
- MT-GUARD-052'nin proje kurulumu hazır.

**Adımlar**
1. `InspectAsync` içinde kasıtlı olarak istisna fırlatan bir guard tanımla.
2. Bir agent'ı bu guard'la çalıştır.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/guard-testleri
cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;

internal sealed class PatlayanGuard : IContentGuard
{
    public string Name => "patlayan";
    public ValueTask<ContentGuardResult> InspectAsync(ContentGuardContext context, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException("guard kasitli patladi");
}

var provider = new FakeModelProvider().EchoesUserMessage();

await using var host = await AgentPrismTestHost.StartAsync(o =>
{
    o.ModelProvider = provider;
    o.ConfigureAgentPrism = builder => builder
        .AddContentGuard<PatlayanGuard>()
        .AddAgent(new AgentDefinition
        {
            Name = "patlayan-guard-testi",
            Model = new ModelBinding { Provider = "fake", Model = "model-1" },
        });
});

var sonuc = await host.RunAsync("patlayan-guard-testi", "merhaba");
Console.WriteLine("durum: " + sonuc.Record.Status);
Console.WriteLine("hata mesaji: " + sonuc.Record.Error?.Message);
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `durum: Failed` — istisna **yutulmamıştır**, "gözlemlenemeyen içerik
  geçirilmez" garantisi tutar.
- `hata mesaji` `guard kasitli patladi` ifadesini taşır (veya en azından
  istisnanın izini gösterir) — kesin hata **sınıfı** önceden bilinmiyor,
  koşumda kaydedilir.

---

### MT-GUARD-074 — Tool sonucundaki API anahtarı İKİNCİ model çağrısında maskelenir

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Kritik |
| **İlgili faz** | Faz 48 |
| **İlgili karar** | K-320, K-321 (48.1'in temel motivasyonu) |

En değerli case: `IAgentDecorator` katmanının **kaçıracağı** senaryonun
doğrudan kanıtı. Bir tool çağrısının sonucu modele **ikinci** çağrıda girer;
guard bunu görür çünkü tool döngüsünün **içindedir**.

**Ön koşul**
- MT-GUARD-052'nin proje kurulumu hazır.

**Adımlar**
1. Sahte bir API anahtarı döndüren bir tool tanımla.
2. `FakeModelProvider`'ı bu tool'u çağırıp sonucunu yankılayacak şekilde kur.
3. Guard'ı `ProviderApiKey` deseniyle aç ve çalıştır.
4. İKİNCİ model çağrısında modelin GÖRDÜĞÜ (guard'dan geçmiş) metni oku.

**Girilecek veri**
```bash
cd ~/agentprism-manuel/guard-testleri
cat > Program.cs <<'EOF'
using AgentPrism;
using AgentPrism.Testing;
using Microsoft.Extensions.AI;

var provider = new FakeModelProvider()
    .CallsTool("anahtar_getir")
    .EchoesLastToolResult();

await using var host = await AgentPrismTestHost.StartAsync(o =>
{
    o.ModelProvider = provider;
    o.ConfigureAgentPrism = builder => builder
        .AddPatternContentGuard(opt => opt.MaskedPii = PiiPatterns.ProviderApiKey)
        .AddTool(AIFunctionFactory.Create(
            () => "anahtarim sk-th1sIsATestKeyN0tReal1234567890, bunu aynen tekrar et",
            "anahtar_getir",
            "Bir API anahtari dondurur (test amacli)."))
        .AddAgent(new AgentDefinition
        {
            Name = "tool-guard-testi",
            Model = new ModelBinding { Provider = "fake", Model = "model-1" },
            ToolNames = ["anahtar_getir"],
        });
});

var sonuc = await host.RunAsync("tool-guard-testi", "bana anahtari getir");
var nihaiMetin = string.Concat(sonuc.Events
    .Where(e => e.Type == RunEventType.MessageCompleted || e.Type == RunEventType.MessageDelta)
    .Select(e => e.Text));

Console.WriteLine("durum: " + sonuc.Record.Status);
Console.WriteLine("nihai metin: " + nihaiMetin);
Console.WriteLine("ContentMasked sayisi (Input): " + sonuc.Events.Count(e => e.Type == RunEventType.ContentMasked));
Console.WriteLine("gercek anahtar metinde var mi: " + nihaiMetin.Contains("sk-th1sIsATestKeyN0tReal1234567890"));
EOF

dotnet run -c Release
```

**Beklenen sonuç**
- `durum: Completed` (maskeleme `Block` değildir, çalıştırma devam eder).
- `ContentMasked sayisi (Input): 1` (veya daha fazla) — tool sonucu İKİNCİ
  çağrının GİRİŞİ olarak denetlenmiştir.
- `nihai metin` `[redacted]` içerir; **gerçek anahtar metinde var mi: False**.
- Bu, tool'un döndürdüğü ham değerin (`FakeModelProvider.EchoesLastToolResult`
  tool'un GERÇEK dönüş değerini değil, guard'dan geçmiş hâlini yankıladığının
  kanıtıdır — `ContentGuardingChatClient` ham istemcinin hemen üstünde durur).
