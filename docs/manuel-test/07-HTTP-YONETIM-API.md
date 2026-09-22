# 07 — HTTP Yönetim API'si (`API`)

> **Alan kodu:** `API` · **Faz:** 4, 34, 43, 44
> **Kaynak:** `src/Tracon.AspNetCore/Endpoints/AgentEndpoints.cs` (CRUD, `validate`,
> `run`'ın idempotency dalı) · `RunEndpoints.cs` (yalnız `list`/`get`/`tree`/`events`/`input`
> — `cancel`/`feedback`/`compare`/`replay` **hariç**, bkz. Sınır tablosu) ·
> `SessionEndpoints.cs` (tümü) · `CatalogEndpoints.cs` (yalnız `/api/tools`, `/api/models`,
> `/api/stats`, `/api/stats/errors` — `/api/stats/timeseries` ve
> `/api/stats/recalculate-costs` **hariç**) · `MetaEndpoints.cs` (tümü) ·
> `Security/TraconEndpointFilter.cs` (yalnız varlık kanıtı — derinlik 13'te) ·
> `Idempotency/IdempotencyFilter.cs` (tümü) ·
> `TraconEndpointRouteBuilderExtensions.cs` (`MapTracon` giriş noktası).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/07-HTTP-YONETIM-API.md`](../arsiv/manuel-test-kosum-2026-09/07-HTTP-YONETIM-API.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/07-HTTP-YONETIM-API.md

---

## Bu dosya neyi kanıtlar

`app.MapTracon()`'in kurduğu yönetim API'sinin (`/api/*`) **genel HTTP sözleşmesi**:
durum kodu disiplini, `ProblemDetails` zarfı, agent tanımının CRUD/versiyon/rollback
yaşam döngüsü, sayfalama/filtreleme parametreleri, `Idempotency-Key` mekaniği (Faz 43),
`/api/agents/validate`'in davranışsal garantileri (Faz 34: her zaman `200`, yan
etkisiz) ve hata sınıflandırmasının (Faz 44) `/api/stats/errors` üzerinden HTTP'de
görünürlüğü. `/v1/*` (OpenAI uyumlu) uçları **kapsam dışıdır** — onlar `ProblemDetails`
bile kullanmaz (K-038), ayrı bir sözleşmedir.

```mermaid
flowchart TD
    A["app.MapTracon(prefix, configure)"] --> B["MetaGroup: /api/meta (AllowAnonymous)"]
    A --> C["idempotencyApp.Use: govde tamponlama (Idempotency-Key VARSA)"]
    A --> D["Korumali grup: TraconEndpointFilter (loopback + bearer/apikey)"]
    D --> E["RateLimitFilter (varsayilan KAPALI)"]
    E --> F["IdempotencyFilter (yalniz /api/agents/{name}/run)"]
    F --> G["RequireRole + RequireApiKeyScope (uc-basina)"]
    G --> H["Handler -> TypedResults.Ok / Created / NoContent / Problem"]
    H -.->|hata| I["ProblemDetails: type, title, status, detail"]
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Kiracı/rol/API anahtarı derinliği — loopback dışı erişim, kapsam reddi, kiracı-başlık çakışması | `13-KIRACI-VE-GUVENLIK.md` |
| `POST /api/runs/{id}/cancel` (iptal) | `21-DAYANIKLILIK-VE-IPTAL.md` |
| `POST /api/runs/{id}/feedback`, `GET .../compare/`, `POST .../replay` (skorlama, karşılaştırma, yeniden oynatma) | `17-EVAL-VE-DENEYLER.md` |
| `Prefer: respond-async` → `202` kuyruğa alma (Faz 46) | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` |
| SSE'nin arayüzde tüketimi, oturum mesaj geçmişi UI görünümü | `11-ARAYUZ-RUN-SESSION-SSE.md` |
| Sağlayıcıya özgü model/health/`providerSettings` davranışı | [`05-SAGLAYICI-OPENAI.md`](05-SAGLAYICI-OPENAI.md) · [`06-SAGLAYICI-DIGER.md`](06-SAGLAYICI-DIGER.md) (zaten üretildi) |
| Maliyet figürleri (`/api/stats`'in maliyet alanları, `/api/stats/timeseries`, `/api/stats/recalculate-costs`) | `12-GOZLEMLENEBILIRLIK-MALIYET.md` |
| `/v1/responses`, `/v1/chat/completions`, `/v1/conversations` (OpenAI uyumlu, `ProblemDetails` KULLANMAZ) | `08-OPENAI-UYUMLU-UCLAR.md` |
| `/api/diagnostics` genel sözleşmesi, OpenAPI belge üretimi | `25-SAGLIK-TESHIS-OPENAPI.md` |
| `callableAgentNames` çağrı grafiği döngü doğrulaması | `02-CEKIRDEK-VE-KATALOG.md` (zaten üretildi — `manuel-dongu` örneği) |
| `AgentDefinitionRequest` alan doğrulaması (`unknown_tool`/`unknown_model`), agent derleme genel davranışı | `02-CEKIRDEK-VE-KATALOG.md` (zaten üretildi) |
| Ek (attachment) yükleme/sahiplik uçları | `19-COK-MODLULUK-VE-SES.md` |
| MCP sunucusu erişilemezliğinde `validate`'in `inconclusive` bayrağı | `18-MCP-VE-A2A.md` |

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Örnek uygulama gerçek bir OpenAI anahtarıyla çalışır (`support`, `router`
   fixture agent'ları buna ihtiyaç duyar; `echo` sağlayıcısı yalnız anahtar
   YOKKEN devreye girer — bu dosyada anahtar açıktır, dolayısıyla `echo`
   **kullanılmaz**).
3. `Tracon:Ui:AuthToken` `manuel-test-token-2026`'dır.
4. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080`

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
```

> **Gerçek para uyarısı.** §3 (Idempotency, gerçek çalıştırma gerektirir), §4
> (hata sınıflandırma, bilinçli bir 404 tetikler) ve §6 (çalıştırma ağacı) küçük
> ölçüde gerçek OpenAI çağrısı yapar. Diğer tüm bölümler (CRUD, doğrulama,
> oturum/liste uçları, meta, güvenlik, `ProblemDetails`) hiçbir model çağırmaz.

---

# 1 — Agent CRUD yaşam döngüsü (Faz 4)

`POST`/`PUT`/`DELETE` yalnız veritabanı kökenli (`Database`) tanımlara uygulanır;
kodda tanımlı (`Code`) bir agent'a dokunma girişimi **her zaman `409`** döner —
ad çakışmasında kod kazandığı için (K-003), veritabanına yazılan aynı adlı bir
tanım hiçbir zaman çözülmezdi.

### MT-API-001 — `POST /api/agents` yeni bir tanım oluşturur, `201` ve `Location` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Yeni bir agent tanımı oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\nLocation: %{header_json}\n" \
     -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-crud-01",
  "instructions": "Kisa yanit ver.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 201`.
- `Location` başlığı `/tracon/api/agents/manuel-crud-01`'dir.
- Gövde tam `AgentDefinition`'ı taşır (`name`, `model`, vb.).

---

### MT-API-002 — Aynı ad ikinci kez `POST` edilirse `409` döner (veritabanı kökenli)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- MT-API-001 geçti (`manuel-crud-01` var).

**Adımlar**
1. Aynı adla tekrar oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "manuel-crud-01",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 409`.
- `title: "Agent name in use"`, `detail` `Use PUT to update it.` ile biter
  (ürün metni İngilizce'dir, K-228 — bu düzeltme koşumda yapıldı).

---

### MT-API-003 — Kodda tanımlı `support` adıyla `POST` edilirse `409` döner (farklı gerekçe metni)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | K-003 |

Negatif senaryo. MT-API-002 ile aynı durum kodu, **farklı** `detail` metni —
ayrım koddan kanıtlanır (`AgentEndpoints.CreateAgentAsync`, satır ~259-268).

**Ön koşul**
- Örnek uygulama çalışıyor (`support` kodda tanımlıdır).

**Adımlar**
1. `support` adıyla oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "support",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 409`.
- `title: "Agent name in use"` — MT-API-002 ile **aynı** `title`, farklı olan
  `detail` metnidir: `is defined in code and cannot be changed from the
  management API. Code wins name conflicts, so a definition written with the
  same name would never resolve.` (İngilizce, K-228; ürün `title`'ı bu iki
  case için de aynı çıktı — spec'in "farklı gerekçe metni" beklentisi yalnız
  `detail` için doğrudur, `title` için değil; koşumda düzeltildi).

---

### MT-API-004 — Adı boş bir tanım `400` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `name` alanını boş bırakarak oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Agent name empty"`, `detail: "'name' is required."`
  (İngilizce, K-228 — koşumda düzeltildi).

---

### MT-API-005 — `model.provider` veya `model.model` eksikse `400` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `model.model` alanını boş bırakarak oluşturmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "manuel-eksik-model",
  "model": { "provider": "openai", "model": "" }
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Model binding missing"`, `detail: "'model.provider'
  and 'model.model' are required."` (İngilizce, K-228 — koşumda düzeltildi).

---

### MT-API-006 — Var olmayan agent'ı okuma `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir adı oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/hic-boyle-bir-agent" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Agent not found"`, `detail: "There is no agent named
  'hic-boyle-bir-agent'."` (İngilizce, K-228 — koşumda düzeltildi).

---

### MT-API-007 — `PUT` yol ile gövdedeki ad uyuşmazsa `400` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo. Agent adı değiştirilemez — yeni ad için yeni tanım gerekir.

**Ön koşul**
- MT-API-001 geçti (`manuel-crud-01` var).

**Adımlar**
1. Yoldaki ad ile gövdedeki adı farklı vererek güncelle.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/manuel-crud-01" \
     -H "$APB" -H "content-type: application/json" -d '{
  "name": "baska-bir-ad",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Name mismatch"`, `detail` `An agent's name cannot be
  changed; create a new definition for a new name.` ile biter (İngilizce,
  K-228 — koşumda düzeltildi).

---

### MT-API-008 — Kodda tanımlı bir agent `PUT` ile güncellenemez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | K-003 |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `support`'u güncellemeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/support" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "support",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 409`, `title: "Code-defined agent cannot be modified"` (İngilizce,
  K-228 — koşumda düzeltildi).

---

### MT-API-009 — Var olmayan bir veritabanı tanımını `PUT` etmek `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo. `PUT` bir "upsert" **değildir** — önce var olmalıdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Hiç oluşturulmamış bir adı `PUT` ile "güncelle".

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/hic-olusturulmamis" \
     -H "$APB" -H "content-type: application/json" -d '{
  "name": "hic-olusturulmamis",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Agent not found"` (İngilizce, K-228 — koşumda
  düzeltildi).

---

### MT-API-010 — Başarılı `PUT` yeni bir versiyon üretir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- MT-API-001 geçti.

**Adımlar**
1. Versiyon sayısını (güncelleme öncesi) oku.
2. `manuel-crud-01`'i farklı bir `instructions` ile güncelle.
3. Versiyon sayısını tekrar oku.

**Girilecek veri**
```bash
curl -s "$APU/api/agents/manuel-crud-01/versions" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"

curl -s -X PUT "$APU/api/agents/manuel-crud-01" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "manuel-crud-01",
  "instructions": "Degistirilmis talimat.",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'

curl -s "$APU/api/agents/manuel-crud-01/versions" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- `HTTP: 200` (`PUT` yanıtı).
- İkinci sayım birincinden **bir fazladır** — versiyon geçmişi anlık
  değiştirilmez, yeni satır eklenir.
- `/api/agents/{name}/versions` listesi **yeniden eskiye** sıralıdır (en yeni
  versiyon ilk sırada).

---

### MT-API-011 — Kodda tanımlı bir agent silinemez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | K-003 |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `support`'u silmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/agents/support" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 409`, `title: "Code-defined agent cannot be modified"` (İngilizce,
  K-228 — koşumda düzeltildi).
- `GET /api/agents/support` hâlâ `200` döner (silinmedi).

---

### MT-API-012 — Var olmayan agent'ı silmek `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir adı sil.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/agents/hic-boyle-bir-agent" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`.

---

### MT-API-013 — Başarılı `DELETE` `204` döner, ardından `GET` `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- MT-API-001/010 geçti (`manuel-crud-01` var).

**Adımlar**
1. Sil.
2. Tekrar oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/agents/manuel-crud-01" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-crud-01" -H "$APB"
```

**Beklenen sonuç**
- Silme: `HTTP: 204`, gövde **boştur**.
- Ardından okuma: `HTTP: 404`.

---

### MT-API-014 — Var olmayan bir versiyon farkı istenirse `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Bir agent tanımı en az bir versiyonla var (`support` kodda tanımlıdır ama
  versiyon geçmişi taşımayabilir — yerine yeni oluşturulan bir tanım kullanılır).

**Adımlar**
1. Yeni bir agent oluştur (tek versiyon, numara `1`).
2. Var olmayan bir versiyon numarasıyla (`99`) fark iste.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-versiyon-testi",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'

curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents/manuel-versiyon-testi/versions/1/diff/99" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Version not found"`, `detail` içinde `99` sayısı geçer
  (İngilizce, K-228 — koşumda düzeltildi).

---

### MT-API-015 — Var olmayan bir versiyona geri dönmek `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- MT-API-014 geçti (`manuel-versiyon-testi` var, tek versiyon).

**Adımlar**
1. Var olmayan bir versiyon numarasına (`99`) geri dönmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/manuel-versiyon-testi/rollback" \
     -H "$APB" -H "content-type: application/json" -d '{"version": 99}'
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Rollback failed"` (İngilizce, K-228 — koşumda
  düzeltildi) — `AgentDefinitionStore.RollbackAsync`
  bir `TraconException` fırlatır, uç bunu `404`'e çevirir
  (`AgentEndpoints.cs:387-398`).

### MT-API-020 — Geçerli VE geçersiz tanımda da yanıt `200`'dür; `severity` ad olarak yazılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 34 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Geçerli bir tanımı doğrula.
2. Geçersiz (bilinmeyen tool) bir tanımı doğrula.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "manuel-gecerli",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" }
}'

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
     -H "content-type: application/json" -d '{
  "name": "manuel-gecersiz",
  "model": { "provider": "openai", "model": "gpt-5.4-mini" },
  "toolNames": ["hayali_tool"]
}'
```

**Beklenen sonuç**
- İki istek de `HTTP: 200` döner.
- Birinci gövdede `valid: true`, `messages: []`.
- İkinci gövdede `valid: false`, `messages` dizisinde `severity: "Error"`
  (dize/ad olarak — `1` gibi sayısal bir değer **değil**, `JsonStringEnumConverter`
  sözleşmesi, S8).

---

### MT-API-021 — `validate` yan etkisizdir: hiçbir agent veya çalıştırma satırı yazılmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 34 |
| **İlgili karar** | — |

`AgentDefinitionValidator.CheckModel` gerçek yolla **aynı**
`_models.CreateChatClient(binding)` çağrısını yapar ama ne veritabanına yazar
ne model çağırır — bu case bunu sayarak kanıtlar (faz dokümanının kendi ölçümü:
`GET /api/agents` ve `/api/runs` öncesi/sonrası aynı sayıyı döndü).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Agent ve run sayılarını öncesinde oku.
2. Aynı adla **on kez** doğrulama isteği gönder (kaydetme değil).
3. Sayıları tekrar oku.

**Girilecek veri**
```bash
oncesi_agent=$(curl -s "$APU/api/agents" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))")
oncesi_run=$(curl -s "$APU/api/runs" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))")

for i in $(seq 1 10); do
  curl -s -X POST "$APU/api/agents/validate" -H "$APB" -H "content-type: application/json" -d '{
    "name": "manuel-yan-etkisiz",
    "model": { "provider": "openai", "model": "gpt-5.4-mini" }
  }' > /dev/null
done

sonrasi_agent=$(curl -s "$APU/api/agents" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))")
sonrasi_run=$(curl -s "$APU/api/runs" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))")

echo "agent: $oncesi_agent -> $sonrasi_agent"
echo "run:   $oncesi_run -> $sonrasi_run"
```

**Beklenen sonuç**
- `agent:` öncesi ve sonrası **aynı** sayı (agent kaydedilmedi).
- `run:` öncesi ve sonrası **aynı** sayı (hiçbir model çağrılmadı, hiçbir
  `runs` satırı açılmadı).

---

### MT-API-022 — Bozuk JSON gövdesi gerçek bir HTTP hatası verir (`400`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 34 |
| **İlgili karar** | — |

Negatif senaryo. Bu, `validate`'in "her zaman `200`" kuralının **tek** istisnasıdır
— ayrıştırılamayan bir gövde, ağın kendi hatasıdır, doğrulama sonucu değil.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Geçersiz JSON gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/validate" -H "$APB" \
     -H "content-type: application/json" -d '{ gecersiz json burada'
```

**Beklenen sonuç**
- `HTTP: 400`.

### MT-API-030 — Aynı anahtar VE aynı gövdeyle ikinci istek yeniden çalışmaz, `Idempotency-Replayed: true` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 43 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Aynı `Idempotency-Key` ve aynı gövdeyle iki kez çalıştır.
2. `/api/runs` sayısını doğrula.

**Girilecek veri**
```bash
ANAHTAR="manuel-idem-$(date +%s)"

curl -s -w "\nHTTP: %{http_code}\nReplayed: %{header_json}\n" \
     -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $ANAHTAR" -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"api-idem-01"}'

curl -s -D - -o /tmp/ap-idem-2.json -w "\nHTTP: %{http_code}\n" \
     -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $ANAHTAR" -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"api-idem-01"}' | grep -i "idempotency-replayed\|HTTP:"

curl -s "$APU/api/runs?agentName=support&sessionId=api-idem-01" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- İlk yanıt `HTTP: 200`, `content-type: application/json` (akışsız — `Idempotency-Key`
  taşıyan istek her zaman akışsızdır), `Idempotency-Replayed` başlığı **yoktur**.
- İkinci yanıt `HTTP: 200`, `Idempotency-Replayed: true` başlığı **vardır**,
  gövde birinciyle **birebir aynıdır** (aynı `runId`).
- `sessionId: api-idem-01` için `/api/runs` sayısı **`1`**'dir — agent ikinci
  kez çalışmadı.

---

### MT-API-031 — Aynı anahtar, FARKLI gövdeyle kullanılırsa `422` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 43 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Bir anahtarla çalıştır.
2. **Aynı** anahtarla ama **farklı** bir gövdeyle tekrar çalıştır.

**Girilecek veri**
```bash
ANAHTAR="manuel-idem-catisma-$(date +%s)"

curl -s -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $ANAHTAR" -H "content-type: application/json" \
     -d '{"message":"Birinci mesaj.","sessionId":"api-idem-02"}' > /dev/null

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $ANAHTAR" -H "content-type: application/json" \
     -d '{"message":"IKINCI FARKLI mesaj.","sessionId":"api-idem-02"}'
```

**Beklenen sonuç**
- `HTTP: 422`, `title: "Idempotency-Key farkli bir istek icin kullanilmis"`.

---

### MT-API-032 — Akışlı istekte (`stream: true`) `Idempotency-Key` desteklenmez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 43 |
| **İlgili karar** | — |

Negatif senaryo. `AgentRunRequest`'in kendi gövdesinde `stream` alanı yoktur
(akış/akışsız seçimi `Idempotency-Key` başlığının **varlığıyla** yapılır,
`AgentEndpoints.RunAsync` satır ~492) — bu case filtrenin kendi gövde içi
`stream: true` denetimini (`IdempotencyFilter.InspectBodyAsync`) OpenAI uyumlu
`/v1/*` uçlarının gövde biçimiyle (`{"stream": true}`) tetikler; genel `run`
ucunda `message`/`stream` birlikte gönderilirse de aynı yol çalışır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Idempotency-Key` başlığı VE gövdede `stream: true` alanı taşıyan bir
   istek gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: manuel-idem-akis-$(date +%s)" -H "content-type: application/json" \
     -d '{"message":"Merhaba.","sessionId":"api-idem-03","stream":true}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Akisli istekte Idempotency-Key desteklenmiyor"`.

---

### MT-API-033 — 255 karakteri aşan `Idempotency-Key` reddedilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 43 |
| **İlgili karar** | — |

Negatif senaryo. Sınır durumu — `TraconIdempotencyOptions.MaxKeyLength`
varsayılanı `255`'tir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. 256 karakterlik bir anahtarla çalıştır.

**Girilecek veri**
```bash
UZUN_ANAHTAR=$(python3 -c "print('a' * 256)")
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $UZUN_ANAHTAR" -H "content-type: application/json" \
     -d '{"message":"Merhaba.","sessionId":"api-idem-04"}'
```

**Beklenen sonuç**
- `HTTP: 400`, `title: "Idempotency-Key cok uzun"`, `detail` içinde `255` ve
  `256` sayıları geçer.

---

### MT-API-034 — Aynı anahtarla eşzamanlı iki istek: ikincisi `409` alır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 43 |
| **İlgili karar** | — |

Negatif senaryo, hafif eşzamanlılık testi. `IdempotencyState.InProgress`
dalının kanıtı — iki istek **aynı anda** aynı anahtarla gönderilir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Aynı anahtarla iki isteği art arda, biri arka planda olacak şekilde başlat.
2. İkinci isteğin durum kodunu oku.

**Girilecek veri**
```bash
ANAHTAR="manuel-idem-eszamanli-$(date +%s)"
BODY='{"message":"Uzunca bir soru sor ve adim adim aciklayarak cevapla, en az uc paragraf yaz.","sessionId":"api-idem-05"}'

curl -s -o /tmp/ap-idem-c1.json -w "HTTP1: %{http_code}\n" \
     -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $ANAHTAR" -H "content-type: application/json" -d "$BODY" &
PID1=$!

sleep 0.05

curl -s -o /tmp/ap-idem-c2.json -w "HTTP2: %{http_code}\n" \
     -X POST "$APU/api/agents/support/run" -H "$APB" \
     -H "Idempotency-Key: $ANAHTAR" -H "content-type: application/json" -d "$BODY"

wait $PID1
```

**Beklenen sonuç**
- İki yanıttan biri `HTTP: 200` (gerçekten çalıştı), diğeri **muhtemelen**
  `HTTP: 409` (`title: "Istek zaten isleniyor"`) döner. Zamanlamaya bağlı
  olarak ikinci istek birinci bitmeden başlarsa `409` görülür; birinci çok
  hızlı biterse ikinci istek `Idempotency-Replayed: true` ile `200` de
  dönebilir — bu durumda case'in amacı (rezervasyon mekanizmasının varlığı)
  yine kanıtlanmış sayılır, `409` gözlenmediyse not düşülür.

### MT-API-040 — Gerçek bir sağlayıcı hatası `/api/stats/errors`'ta gruplanarak görünür

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 44 |
| **İlgili karar** | K-296 |

Küçük ölçüde gerçek para harcar (istek reddedilir ama gönderilir).

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir OpenAI modeliyle bir agent oluştur.
2. İki kez çalıştır (aynı hatayı iki kez üret).
3. `/api/stats/errors`'ı oku.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents" -H "$APB" -H "content-type: application/json" -d '{
  "name": "manuel-hata-sinifi-testi",
  "model": { "provider": "openai", "model": "gpt-olmayan-model-api-testi" }
}'

curl -s -X POST "$APU/api/agents/manuel-hata-sinifi-testi/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"1","sessionId":"api-hata-01"}' > /dev/null
curl -s -X POST "$APU/api/agents/manuel-hata-sinifi-testi/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"2","sessionId":"api-hata-02"}' > /dev/null

curl -s "$APU/api/stats/errors?agentName=manuel-hata-sinifi-testi" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Dizide **en az bir** giriş vardır; `class` alanı ad olarak yazılıdır
  (`ProviderError` gibi — K-296'nın ölçtüğü `ClientResultException` deseni
  eşleşiyorsa; eşleşmiyorsa `Unknown` görülebilir, bu durumda 06'daki ilgili
  şüphe notuna çapraz referans verilir).
- O sınıfın `count` alanı **en az `2`**'dir (iki hata da aynı sınıfa/parmak
  izine düştü).

---

### MT-API-041 — `/api/stats/errors` varsayılan aralığı son 24 saattir, `?hours=` ile değiştirilir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 44 |
| **İlgili karar** | — |

**Ön koşul**
- MT-API-040 geçti.

**Adımlar**
1. `hours` parametresiz sorgula (varsayılan 24 saat).
2. `hours=0.01` ile sorgula (36 saniye — MT-API-040'ın hatalarından öncesine
   düşmesi beklenir).

**Girilecek veri**
```bash
curl -s "$APU/api/stats/errors?agentName=manuel-hata-sinifi-testi" -H "$APB" | python3 -c "import json,sys; print(sum(e['totalRuns'] for e in json.load(sys.stdin)))"
curl -s "$APU/api/stats/errors?agentName=manuel-hata-sinifi-testi&hours=0.01" -H "$APB" | python3 -c "import json,sys; print(sum(e['totalRuns'] for e in json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Birinci sorgu (24 saat) en az `2` toplam sayar.
- İkinci sorgu (36 saniye, MT-API-040'ın hemen ardından koşulmadıysa) daha
  düşük veya `0` sayar — pencere daralınca eski hatalar dışarıda kalır.

---

### MT-API-042 — `/api/stats` genel sayaçları tutarlıdır (maliyet HARİÇ)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Bu case yalnız sayaç **tutarlılığını** sınar; maliyet alanları
[`12-GOZLEMLENEBILIRLIK-MALIYET.md`](12-GOZLEMLENEBILIRLIK-MALIYET.md)'in
konusudur ve burada değerlendirilmez.

**Ön koşul**
- MT-API-040 geçti (en az iki başarısız çalıştırma var).

**Adımlar**
1. Genel özeti oku.

**Girilecek veri**
```bash
curl -s "$APU/api/stats?agentName=manuel-hata-sinifi-testi" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Yanıt `totalRuns`, `completedRuns`, `failedRuns`, `canceledRuns`,
  `runningRuns`, `awaitingInputRuns` alanlarını taşır (`RunStatistics.cs`'te
  tanımlı altı sayaç — `RunStatistics.cs:46-66`).
- `failedRuns` **en az `2`**'dir (MT-API-040'ın iki başarısız çalıştırması).
- `byAgent` dizisinde `agentName: "manuel-hata-sinifi-testi"` girdisi vardır.
- **Kod-doğrulanmamış şüphe**: `totalRuns`'ın diğer beş sayacın toplamına eşit
  olup olmadığı (`awaitingInputRuns` dahil) kaynak okumasıyla doğrulanmadı —
  yalnız alan adları XML dokümanından çıkarıldı, toplama mantığı
  (`IRunStore.GetStatisticsAsync` uygulaması) okunmadı. Koşum bu eşitliği
  gerçek sayılarla sınar.

### MT-API-050 — `GET /api/sessions` sayfalama parametreleri `[1, 200]` aralığına kırpılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Sınır senaryosu.

**Ön koşul**
- Örnek uygulama çalışıyor. En az bir oturum var (önceki case'lerden).

**Adımlar**
1. `take=0` ile sorgula (alt sınırın altında).
2. `take=99999` ile sorgula (üst sınırın üstünde).
3. `skip=-5` ile sorgula (negatif).

**Girilecek veri**
```bash
curl -s "$APU/api/sessions?take=0" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions?take=99999" -H "$APB" | tail -1
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions?skip=-5" -H "$APB" | tail -1
```

**Beklenen sonuç**
- Üç istek de çökmez; `take=0` **en az `1`** kayıt döner (`Math.Clamp(0, 1, 200)`
  → `1`'e kırpılır).
- Hiçbir durumda `HTTP: 500` görülmez.

---

### MT-API-051 — Var olmayan oturum `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Var olmayan bir oturum kimliğini oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/hic-boyle-bir-oturum" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Session not found"` (İngilizce, K-228 — koşumda
  düzeltildi).

---

### MT-API-052 — Oturum silinir, tekrar okunduğunda `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- `api-idem-01` oturumu var (MT-API-030'dan).

**Adımlar**
1. Oturumu sil.
2. Tekrar oku.
3. Aynı oturumu ikinci kez silmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/sessions/api-idem-01" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/sessions/api-idem-01" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/sessions/api-idem-01" -H "$APB"
```

**Beklenen sonuç**
- Birinci silme: `HTTP: 204`.
- Okuma: `HTTP: 404`.
- İkinci silme: `HTTP: 404` (zaten silinmiş — idempotent bir "başarı" değil,
  gerçek bir "yok" durumu).

---

### MT-API-053 — Bellek içi depoda `POST /api/sessions/{id}/branch` `501` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Sınır senaryosu. PostgreSQL açıkken gerçek dallandırma (`201`) davranışı
[`03-KALICILIK-POSTGRESQL.md`](03-KALICILIK-POSTGRESQL.md)'in konusudur; bu
dosya varsayılan (bellek içi) kurulumda çalışır, bu yüzden `501` beklenir —
bu da K1 "sıfır sürpriz" ilkesinin bir kanıtıdır: yetenek çalışmaz ama
uygulama çökmez, açıkça "desteklenmiyor" der.

**Ön koşul**
- Örnek uygulama **bellek içi** depoyla çalışıyor (PostgreSQL/SQLite/SQL Server
  bağlı DEĞİL — bu dosyanın varsayılan kurulumu).

**Adımlar**
1. Herhangi bir oturumu dallandırmayı dene.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"api-branch-01"}' > /dev/null

curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/sessions/api-branch-01/branch" -H "$APB" \
     -H "content-type: application/json" -d '{"upToSequence": 1}'
```

**Beklenen sonuç**
- `HTTP: 501`, `title: "Branching not supported"` (İngilizce, K-228 —
  koşumda düzeltildi).

---

### MT-API-054 — Var olmayan bir oturumu dallandırmak `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo. Bellek içi kurulumda `501` `404`'ten **önce** kontrol edilir
mi yoksa sonra mı — bu case gerçek sırayı kaydeder.

**Ön koşul**
- Örnek uygulama bellek içi depoyla çalışıyor.

**Adımlar**
1. Var olmayan bir oturumu dallandırmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/sessions/hic-boyle-bir-oturum/branch" \
     -H "$APB" -H "content-type: application/json" -d '{"upToSequence": 1}'
```

**Beklenen sonuç**
- `HTTP: 501` (bellek içi depoda dallandırma zaten desteklenmediği için) VEYA
  `HTTP: 404` (oturum yoksa önce bu kontrol edilir) — koşum hangisinin
  gerçekleştiğini kaydeder; `ConversationBranchService.BranchAsync`'in dahili
  sırası bu dosyanın kaynak kapsamı dışındadır. **Koşum sonucu: `501`** —
  depo türü kontrolü oturum varlığından önce yapılıyor.

### MT-API-060 — `GET /api/runs` varsayılan olarak yalnız kök çalıştırmaları döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

`router` agent'ı `support`'u çağırır (Faz 12 deseni) — bu, gerçek bir
ebeveyn-çocuk `runs` çifti üretir.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `router`'yi bir sipariş sorusuyla çalıştır (alt çalıştırma açar).
2. Aynı oturum için `includeChildren` olmadan listele.
3. `includeChildren=true` ile tekrar listele.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/router/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"ORD-1001 siparisim nerede?","sessionId":"api-agac-01"}'

curl -s "$APU/api/runs?sessionId=api-agac-01" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
curl -s "$APU/api/runs?sessionId=api-agac-01&includeChildren=true" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- `includeChildren` olmadan **`1`** satır (yalnız kök — `router`).
- `includeChildren=true` ile **`2`** satır (kök + `support` alt çalıştırması).

---

### MT-API-061 — Var olmayan `runId` her uçta `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo, temsilci case: `get`, `tree`, `events` üçü de aynı
`NotFound(runId)` yardımcısını kullanır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Rastgele bir GUID ile üç ucu da sorgula.

**Girilecek veri**
```bash
GUID="00000000-0000-0000-0000-000000000000"
for uc in "" "/tree" "/events"; do
  echo "--- /api/runs/$GUID$uc ---"
  curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs/$GUID$uc" -H "$APB" | tail -3
done
```

**Beklenen sonuç**
- Üçü de `HTTP: 404` döner.

---

### MT-API-062 — `GET /api/runs/{id}/tree` kökten tam ağacı döner (alt çalıştırmadan sorulsa bile)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- MT-API-060 geçti.

**Adımlar**
1. Alt çalıştırmanın (`support`) `runId`'sini bul.
2. O `runId` ile `/tree` iste.

**Girilecek veri**
```bash
curl -s "$APU/api/runs?sessionId=api-agac-01&includeChildren=true" -H "$APB" | python3 -m json.tool
# support satirinin 'id' alani alinir, sonra:
curl -s "$APU/api/runs/<support-run-id>/tree" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
```

**Beklenen sonuç**
- Alt çalıştırmanın kimliğiyle sorgulanan `/tree` de **`2`** satır döner
  (kök + kendisi) — ağaç her zaman kökünden çekilir, sorulan satırdan değil.

---

### MT-API-063 — `Last-Event-ID` ile akış kaldığı sıradan devam eder, tekrar göndermez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Bir çalıştırma başlat, `runId`'yi al.
2. Tamamlanmış çalıştırmanın olaylarını `Last-Event-ID: 1` ile iste.

**Girilecek veri**
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"api-lastevent-01"}'
# runId 'run' cercevesinden okunur, sonra:
curl -sN -H "Last-Event-ID: 1" "$APU/api/runs/<runId>/events" -H "$APB" | head -20
```

**Beklenen sonuç**
- İlk gönderilen olayın `id:` alanı **`2`** veya daha büyüktür (`0` ve `1`
  tekrar gönderilmedi).

---

### MT-API-064 — Girdi kaydı kapalıyken `GET /api/runs/{id}/input` `404` döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 47 |
| **İlgili karar** | — |

Negatif senaryo. **Geçici `user-secrets` değişikliği.**

**Ön koşul**
- Uygulama durdurulmuş.

**Adımlar**
1. `Tracon:RunRecording:RecordRunInput`'ı `false` yap.
2. Uygulamayı başlat, bir çalıştırma yap.
3. Girdiyi oku.
4. Ayarı geri al.

**Girilecek veri**
```bash
dotnet user-secrets set "Tracon:RunRecording:RecordRunInput" "false" --project samples/Tracon.Api
cd samples/Tracon.Api && dotnet run
```
```bash
curl -s -X POST "$APU/api/agents/support/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Merhaba, sadece \"tamam\" yaz.","sessionId":"api-girdi-kapali-01"}'
# runId 'run' cercevesinden okunur, sonra:
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs/<runId>/input" -H "$APB"
```
```bash
# Temizlik:
dotnet user-secrets remove "Tracon:RunRecording:RecordRunInput" --project samples/Tracon.Api
```

**Beklenen sonuç**
- `HTTP: 404` — çalıştırmanın kendisi vardır (`GET /api/runs/{id}` `200`
  döner) ama girdisi yoktur.

---

### MT-API-065 — `GET /api/runs` sayfalama parametreleri `[1, 200]` aralığına kırpılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Sınır senaryosu. `/api/sessions` ile aynı desen (`Math.Clamp`), farklı uç.

**Ön koşul**
- Örnek uygulama çalışıyor. En az bir çalıştırma var.

**Adımlar**
1. `take=0` ile sorgula.
2. `take=99999` ile sorgula.

**Girilecek veri**
```bash
curl -s "$APU/api/runs?take=0" -H "$APB" | python3 -c "import json,sys; print(len(json.load(sys.stdin)))"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/runs?take=99999" -H "$APB" | tail -1
```

**Beklenen sonuç**
- `take=0` **en az `1`** kayıt döner.
- `take=99999` çökmez, `HTTP: 200` döner (kırpılmış üst sınırla).

### MT-API-070 — `GET /api/tools` kayıtlı tool'ları JSON şemalarıyla listeler

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Tool listesini oku.

**Girilecek veri**
```bash
curl -s "$APU/api/tools" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- Dizide `get_order_status`, `list_recent_orders`, `cancel_order` adlarını
  taşıyan girdiler vardır.
- Her girdi bir JSON şeması (`parameters` veya eşdeğer alan) taşır.
- Bu uç yalnız **okur**; hiçbir `POST`/`PUT` yolu yoktur — tool'lar yalnız
  kodda tanımlanır (güvenlik sınırı, AGENTS.md).

---

### MT-API-071 — `GET /api/models`'in `status` alanı önbellekten gelir, ağ çağrısı yapmaz

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4, 8 |
| **İlgili karar** | — |

`05-SAGLAYICI-OPENAI.md`/`06-SAGLAYICI-DIGER.md`'nin sağlık denetimi
uçlarından (`/api/models/health/*`, gerçek ağ çağrısı yapar) **farklı**
olarak, `/api/models`'in kendisi hiçbir ağ isteği göndermez.

**Ön koşul**
- Örnek uygulama çalışıyor, hiçbir `/api/models/health/*` sorgusu henüz
  yapılmadı (temiz reset sonrası).

**Adımlar**
1. Katalogu oku ve yanıt süresini ölç.

**Girilecek veri**
```bash
curl -s -w "\nSure: %{time_total}s\n" "$APU/api/models" -H "$APB" | tail -1
```

**Beklenen sonuç**
- Yanıt süresi milisaniyeler mertebesindedir (gerçek bir sağlayıcı ağ
  çağrısının onlarca-yüzlerce milisaniyesinden belirgin şekilde kısa).
- Sağlık denetimi hiç çalıştırılmadıysa her sağlayıcının `status` alanı
  `Unknown`'dur (önbellek boş).

### MT-API-080 — `/api/meta` kimlik doğrulamasız erişilebilir, sır içermez

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. `Authorization` başlığı **olmadan** meta ucunu oku.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/meta"
```

**Beklenen sonuç**
- `HTTP: 200` (token yokken diğer tüm uçlar `401` döner — bkz. MT-API-090).
- Gövde `version`, `prefix`, `authentication` (`allowRemoteAccess`,
  `requiresBearerToken`, `requiresAuthorizationPolicy`), `storage`
  (`persistent`, `agentDefinitionStore`, `runStore`, `sessionStore`,
  `jobStore`, `jobWorkerEnabled`), `roles` (`canRead`, `canOperate`,
  `canAdminister`) alanlarını taşır.
- Hiçbir alanda `ApiKey`, bağlantı dizesi veya agent adı **geçmez**.

### MT-API-090 — Token yokken korunan bir uç `401` döner, `WWW-Authenticate: Bearer` taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | **Kritik** |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Negatif senaryo.

**Ön koşul**
- Örnek uygulama `Tracon:Ui:AuthToken = manuel-test-token-2026` ile
  çalışıyor.

**Adımlar**
1. `Authorization` başlığı olmadan korunan bir ucu çağır.
2. Yanlış bir token ile çağır.

**Girilecek veri**
```bash
curl -s -D - -o /dev/null -w "\nHTTP: %{http_code}\n" "$APU/api/agents"
curl -s -D - -o /dev/null -w "\nHTTP: %{http_code}\n" "$APU/api/agents" -H "Authorization: Bearer yanlis-token"
```

**Beklenen sonuç**
- İki istek de `HTTP: 401` döner, `WWW-Authenticate: Bearer` başlığı taşır.
- `title: "Authentication failed"` (İngilizce, K-228 — koşumda düzeltildi),
  `detail` beklenen token hakkında **hiçbir bilgi vermez**.

---

### MT-API-091 — Doğru token ile korunan uç normal çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Bu case pozitif kontrol: MT-API-090'ın olumsuz sonucunun **yalnız** token
eksikliğinden geldiğini, ucun kendisinin bozuk olmadığını kanıtlar. Sabit
zamanlı karşılaştırma (`CryptographicOperations.FixedTimeEquals`) burada
gözlemlenemez (yalnız kodda doğrulanır) — zamanlama yan kanalı testi kapsam
dışıdır.

**Ön koşul**
- Örnek uygulama çalışıyor.

**Adımlar**
1. Doğru token ile aynı ucu çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/agents" -H "$APB" | tail -1
```

**Beklenen sonuç**
- `HTTP: 200`.

### MT-API-100 — Farklı hatalar aynı `ProblemDetails` zarfını taşır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 4 |
| **İlgili karar** | — |

Önceki case'lerin (MT-API-006 `404`, MT-API-004 `400`, MT-API-002 `409`)
ürettiği gövdeler burada tek tip zarf açısından topluca gözden geçirilir.

**Ön koşul**
- MT-API-002, 004, 006 koşuldu.

**Adımlar**
1. Üç farklı hata durumunu üret, `content-type` başlığını ve gövde şeklini
   karşılaştır.

**Girilecek veri**
```bash
for durum in \
  'GET /api/agents/hic-boyle-bir-agent' \
  'POST /api/agents' \
  ; do
  echo "--- $durum ---"
done

curl -s -D - -o /tmp/ap-pd-404.json "$APU/api/agents/hic-boyle-bir-agent" -H "$APB" | grep -i content-type
python3 -m json.tool < /tmp/ap-pd-404.json
```

**Beklenen sonuç**
- `content-type: application/problem+json` (RFC 7807).
- Gövde en az `type`, `title`, `status`, `detail` alanlarını taşır; `status`
  alanı HTTP durum koduyla **aynı** sayısal değeri taşır (örnek: `404`
  hatasında `status: 404`).
- `title` alanı kısa ve sabit bir kategori adıdır (`"Agent bulunamadi"` gibi);
  değişken veri (`detail` içindeki agent adı gibi) `title`'a **sızmaz**.
