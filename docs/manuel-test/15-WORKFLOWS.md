# 15 — Workflows: Yürütme, Kontrol Noktası, Graf ve Human-in-the-Loop (`WF`)

> **Alan kodu:** `WF` · **Faz:** 15, 16, 71, 87 (yalnız düğüm başına retry)
> **Kaynak:** `src/Tracon.Workflows/` (tümü: `TraconWorkflowOptions`,
> `TraconWorkflowsBuilderExtensions`, `WorkflowAgentBinding`, `Internal/*`) ·
> `src/Tracon.Abstractions/Workflows/` (tümü) ·
> `src/Tracon.Abstractions/Runs/RunKind.cs` ·
> `src/Tracon.Core/Workflows/WorkflowDefinitionValidator.cs` ·
> `src/Tracon.Core/Storage/InMemoryWorkflowStores.cs` ·
> `src/Tracon.Core/Audit/AuditingWorkflowDefinitionStore.cs` (yalnız denetim
> eylem adları) · `src/Tracon.PostgreSql/Migrations/0007_workflows.sql` ·
> `src/Tracon.PostgreSql/Stores/PostgresWorkflow*.cs` ·
> `src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs` ·
> `src/Tracon.AspNetCore/Contracts/WorkflowContracts.cs` ·
> `src/Tracon.UI/frontend/src/screens/workflows.tsx`,
> `workflow-editor.tsx`, `workflow-detail.tsx` ·
> `src/Tracon.UI/frontend/src/components/workflow-graph.tsx` ·
> `src/Tracon.UI/frontend/src/lib/workflow-graph.ts` ·
> `samples/Tracon.Api/Program.cs` (yalnız `AddWorkflow(...)` blokları:
> `summarize-and-translate`, `summarize-and-approve`; ve Faz 71'in `AddWorkflowFunction(...)`
> bloğu: `word-count`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

> **Koşum kaydı ayrıdır:** son tur (2026-09-16):
> [`../arsiv/manuel-test-kosum-2026-09/15-WORKFLOWS.md`](../arsiv/manuel-test-kosum-2026-09/15-WORKFLOWS.md)
> — `Gerçek sonuç` ve `Durum` orada. Bu dosya **spesifikasyondur** ve
> her koşumda yeniden kullanılır. 2026-08-13 turunun kaydı silindi (K-847);
> tam metin: git show 64c8a103:docs/manuel-test/kosumlar/2026-08-13/15-WORKFLOWS.md

---

## Bu dosya neyi kanıtlar

Faz 15, katalogdaki agent'ları beş hazır desenle (`Sequential`, `Concurrent`,
`Handoff`, `GroupChat`, `Magentic`) birbirine bağlayan bir yürütme motoru
getirdi; her çalıştırma bir `runs` satırıdır (`kind = 1`) ve kontrol
noktalarından sürdürülebilir. Faz 16 üçünü ekledi: **graf görselleştirme**
(derlenmiş workflow'dan çıkarılan, elle SVG çizilen bir graf), **human-in-the-
loop** (dış istek portu → `AwaitingInput` → `/respond` → yeni `runs` satırı) ve
**kalıcı executor kimliği** (K-127 — uygulama yeniden başlasa da kontrol
noktaları geçerli kalır).

```mermaid
flowchart TD
    A["PUT api/workflows/name"] --> B["WorkflowDefinitionValidator<br/>yapisal kural"]
    B -- "gecersiz" --> BX["400"]
    B -- "gecerli" --> C["IWorkflowDefinitionStore<br/>tenant basina kayit"]

    D["POST api/workflows/name/run"] --> E["WorkflowDefinitionCompiler<br/>agent adi -> katalog"]
    E -- "bilinmeyen agent" --> EX["SSE event: error<br/>KAYIT ANINDA degil CALISTIRMADA"]
    E -- "cozuldu" --> F["Microsoft Agent Framework<br/>InProcessExecution"]
    F --> G["runs satiri kind=Workflow<br/>alt agent'lar parent_run_id ile bagli"]
    F --> H["workflow_checkpoints<br/>json sutun, $type ilk ozellik"]
    F -- "dis istek portu" --> I["RunStatus.AwaitingInput<br/>RunAwaitingInput olayi"]
    I --> J["POST runs/id/respond"] --> K["checkpoint'ten SURDUR<br/>YENI runs satiri"]

    L["GET workflows/name/graph"] --> M["ReflectExecutors/Edges/Ports<br/>DERLENMIS workflow'dan"]

    style BX fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style EX fill:#7a1f1f,stroke:#3d0f0f,color:#ffffff
    style I fill:#7a4a1f,stroke:#3d250f,color:#ffffff
    style K fill:#1f6f4a,stroke:#0d3b27,color:#ffffff
```

## Sınır: bu dosya nerede biter

| Konu | Nerede |
|---|---|
| Genel HTTP zarfı (`ProblemDetails`, CRUD, idempotency) | `07-HTTP-YONETIM-API.md` (zaten üretildi) |
| Üç katmanlı erişim koruması, kiracı izolasyonu, API anahtarı kapsamları | `13-KIRACI-VE-GUVENLIK.md` (zaten üretildi) — burada TEKRARLANMAZ, yalnız §9'daki kapsam boşluğu bu dosyaya özgü olduğu için buradadır |
| `run-detail.tsx`'in `kind === 'Workflow'` satırını nasıl gösterdiği, `AwaitingInput` istatistik dalı, "Dallandır" | `11-ARAYUZ-RUN-SESSION-SSE.md` (zaten üretildi, `MT-UIRUN-012`/`013`/`044`) — burada TEKRARLANMAZ |
| SSE `error` çerçevesinin gerçek sağlayıcı istisnalarını (K-296) yakalayamaması | `05-SAGLAYICI-OPENAI.md` `MT-OAI-043` — bu dosyadaki workflow hataları `TraconException`'dır ve o boşluğa **girmez** (bkz. §8 notu) |
| İş kuyruğu (`Prefer: respond-async`) ile workflow çalıştırma | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (henüz üretilmedi) |
| Rol matrisi (`TraconPolicies.Reader`/`.Admin`) genel no-op durumu | `00-INDEKS.md` §8 ve `14-SKILL-VE-SCRIPT.md`'de zaten kaydedildi — burada tekrar açıklanmaz, yalnız §9'un öncülü olarak anılır |

> **Rol matrisi burada da NO-OP'tur, tekrar test edilmez.** `WorkflowEndpoints`
> her ucu `RequireRole(roles.Reader/Operator/Admin)` ile işaretler ama
> `TraconPolicies.*` örnek uygulamada kayıtlı değildir — statik bearer
> token tüm workflow uçlarına erişir. Bu, `14-SKILL-VE-SCRIPT.md`'nin zaten
> kaydettiği genel bulgunun bir tekrarıdır. **Farklı ve bu dosyaya özgü olan**,
> `WorkflowEndpoints`'in **hiçbir ucunda** `RequireApiKeyScope(...)` çağrısının
> bulunmaması — bu, rol no-op'undan bağımsız, API anahtarı KAPSAM sistemini de
> devre dışı bırakan ayrı bir bulgudur ve §9'da bir case ile ölçülür.

## Koşmadan önce

1. [`00-INDEKS.md`](00-INDEKS.md) §4 reset yordamı uygulanır.
2. Workflow tanım ve kontrol noktası depoları **her kalıcılık sağlayıcısında**
   (bellek içi dahil) aynı arayüzle çalışır (`IWorkflowDefinitionStore`/
   `IWorkflowCheckpointStore`, `UsePostgreSql()`/`UseSqlite()`/`UseSqlServer()`
   çağrılmadan bellek içi uygulamalarla kayıtlıdır). Bu dosyanın SQL
   doğrulama sorguları **PostgreSQL** varsayar (`$type` ayracı ve `json` sütun
   kanıtı yalnız PostgreSQL'de anlamlıdır — K-027); diğer sağlayıcılar
   `04-KALICILIK-DIGER.md`'nin işidir.
3. Örnek uygulama çalışır: `cd samples/Tracon.Api && dotnet run` →
   `http://localhost:5080/tracon`.
4. Örnek uygulama İKİ kodda tanımlı workflow taşır: `summarize-and-translate`
   (Sequential, insan girdisi istemez) ve `summarize-and-approve` (insan onayı
   ister, `RequestPort.Create<string, bool>`). Üçüncüsü yok; arayüzden
   tanımlanan workflow'lar bu dosyanın case'lerinde kurulur ve
   `FIX-WF-*` kimlikleriyle anılır.
5. `.UseWorkflows()` `Program.cs` satır ~103'te çağrılıdır — motor varsayılan
   olarak **açıktır**. §8'in bazı case'leri bunu geçici olarak kapatır; her
   birinin başında açıkça belirtilir.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/tracon"
export PG="docker exec -i ap-pg psql -U postgres -d tracon"
```

> **Gerçek para uyarısı.** §3, §4, §5, §6 gerçek OpenAI modeliyle (`gpt-5.4-mini`)
> çalışır ve her çalıştırma en az bir, Magentic'te (§6) birden fazla model
> çağrısı yapar — plan reddi yöneticiyi yeniden çalıştırır. §1, §2, §7 (yalnız
> `/graph` ucu — grafın **kendisi çalıştırma gerektirmez**), §8, §9 model
> çağırmaz.

---

## Bu dosyanın yerel fixture'ları

Bu veriler yalnız bu dosyaya özgüdür, `00-INDEKS.md`'ye girmez (`PROMPT.md` §4.2).
Katalogdaki agent'lar için bkz. `00-INDEKS.md` §3.1 (`summarizer`, `translator`
kullanılır — her ikisi de anahtar gerektirmez, OpenAI kullanır).

| Kimlik | Değer |
|---|---|
| `FIX-WF-01` | Ad `inceleme-zinciri` · `Sequential` · `agentNames: ["summarizer","translator"]` |
| `FIX-WF-02` | Ad `plan-onayli` · `Magentic` · `agentNames: ["translator"]` · `managerAgentName: "summarizer"` · `maxIterations: 2` · `requirePlanApproval: true` |
| `FIX-WF-03` | Ad `cift-gorus` · `Concurrent` · `agentNames: ["summarizer","translator"]` |
| `FIX-WF-MSG-01` | `"Tracon, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."` — özetleme/çeviri girdisi |

---

# 1 — Workflow Tanım CRUD ve Yapısal Doğrulama (Faz 15)

Doğrulama kuralları `WorkflowDefinitionValidator`'da tek bir yerde yaşar; hem
`PUT /api/workflows/{name}` (kayıt anında) hem workflow derleyicisi (çalıştırma
anında) aynı metodu çağırır. Agent'ların katalogda **gerçekten var olup
olmadığı** ise yalnız çalıştırma anında denetlenir — bu ayrım §1'in son
case'inde ölçülür.

### MT-WF-001 — `PUT /api/workflows/{name}` yeni bir Sequential tanım oluşturur (`200`)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-WF-01`'i oluştur.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/inceleme-zinciri" -H "$APB" \
     -H "content-type: application/json" -d '{
  "displayName": "Inceleme Zinciri",
  "description": "Ozetler, sonra cevirir.",
  "kind": "Sequential",
  "agentNames": ["summarizer", "translator"]
}'
```

**Beklenen sonuç**
- `HTTP: 200` — `SaveAsync` **her zaman** `TypedResults.Ok` döner, `Created`
  değil (`WorkflowEndpoints.cs:217-219`). Bu, Skill uçlarının `201`/`200`
  ayrımından **farklıdır**; workflow uçları böyle bir ayrım yapmaz.
- Gövdede `version: 1`, `tenantId: "default"`, `agentNames: ["summarizer","translator"]`.

---

### MT-WF-002 — Aynı adı tekrar `PUT` etmek günceller, `version` artar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-001 geçti.

**Adımlar**
1. Aynı ada, farklı `description` ile tekrar `PUT` gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/inceleme-zinciri" -H "$APB" \
     -H "content-type: application/json" -d '{
  "displayName": "Inceleme Zinciri",
  "description": "Ozetler, sonra cevirir (guncellendi).",
  "kind": "Sequential",
  "agentNames": ["summarizer", "translator"]
}'
```

**Beklenen sonuç**
- `HTTP: 200`, `version: 2`. Sürüm geçmişi **tutulmaz** (agent tanımlarının
  aksine) — yalnız son hâl saklanır (Faz 15 §15.4).

---

### MT-WF-003 — `GET /api/workflows/{name}` veritabanında saklı bir tanımı döner

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-001 geçti.

**Adımlar**
1. `GET /api/workflows/inceleme-zinciri` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/workflows/inceleme-zinciri" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200`, gövde MT-WF-002'nin sonucuyla birebir aynı.

---

### MT-WF-004 — Aynı uç, KODda tanımlı bir workflow için ayırt edici bir `404` döner (düzeltildi)

**Kısmen düzeltilmiş kusur (2026-08-10).** `WorkflowEndpoints.GetAsync`
`[FromServices] IWorkflowDefinitionStore store`'a **doğrudan** `store.GetAsync(...)`
çağırıyordu; kodda tanımlı bir workflow (`AddWorkflow(...)`) hiçbir zaman
`store`'a yazılmadığı için tekil `GET` ucu onu "yok" sayıyordu — liste ve
çalıştırma ile tutarsız bir 404. Tam birleştirme mimari olarak MÜMKÜN DEĞİL:
kod-tanımlı workflow'lar `CodeWorkflowRegistration`'da `Func<IServiceProvider,
Workflow>` olarak tutulur, hiçbir zaman bir `WorkflowDefinition` (düzenlenebilir
JSON tanım) ÜRETMEZ. Bunun yerine `GetAsync`'e `IWorkflowRunner?` eklendi:
`store`'da bulunamayan ama `runner.GetAsync` üzerinden var olduğu görülen bir
ad artık **ayırt edici** bir 404 mesajı ("Düzenlenebilir tanım yok — kodda
tanımlı") döner, generic "Workflow bulunamadi" değil.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulama varsayılan hâlde (hiçbir DB tanımı gerekmez).

**Adımlar**
1. Kodda tanımlı `summarize-and-translate` için listeyi çağır, varlığını doğrula.
2. Aynı ad için tekil `GET` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/workflows" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print([w['name'] for w in d])"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/summarize-and-translate" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `summarize-and-translate` listede, `origin: "Code"`.
- Adım 2: `HTTP: 404`, ama `title: "Duzenlenebilir tanim yok"` — generic
  `"Workflow bulunamadi"` DEĞİL. `detail` alanı workflow'un kodda tanımlı
  olduğunu ve düzenlenebilir bir `WorkflowDefinition` taşımadığını açıklar.
  Generic mesaj dönerse fix'in regresyonudur.

---

### MT-WF-005 — `GET /api/workflows` kod + veritabanı birleşik liste; isim çakışmasında KOD kazanır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-001 geçti (`inceleme-zinciri` DB'de var).

**Adımlar**
1. Kod-tanımlı adla (`summarize-and-translate`) çakışan bir DB tanımı `PUT` et.
2. Listeyi çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/summarize-and-translate" -H "$APB" \
     -H "content-type: application/json" -d '{
  "displayName": "Sahte DB Kaydi",
  "kind": "Concurrent",
  "agentNames": ["summarizer", "translator"]
}'
curl -s "$APU/api/workflows" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); e=[w for w in d if w['name']=='summarize-and-translate'][0]; print(e)"
```

**Beklenen sonuç**
- `PUT`: `HTTP: 200` — kayıt **kabul edilir**, doğrulama yalnız yapısaldır ve
  `Concurrent` + 2 agent geçerlidir.
- Liste: `summarize-and-translate` girdisi `origin: "Code"`, `kind: null`,
  `agentNames: []` gösterir — **DB kaydı GÖRÜNMEZ olur**
  (`WorkflowCatalog.ListAsync`, kod kayıtları `descriptors[name] = ...` ile
  DB'nin üzerine SONRADAN yazılır, `WorkflowCatalog.cs:66-67`). `POST
  .../run` da kod grafını çalıştırır (Sequential davranışı görülür,
  `Concurrent` değil) — bu adım koşulmaz, yalnız MT-WF-004'ün bulgusuyla
  birlikte not edilir.

---

### MT-WF-006 — `DELETE` veritabanı kaydını siler, sonraki `GET` `404` verir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-001 geçti.

**Adımlar**
1. `inceleme-zinciri`'yi sil.
2. Tekrar `GET` et.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/workflows/inceleme-zinciri" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/inceleme-zinciri" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `HTTP: 204`.
- Adım 2: `HTTP: 404`.

---

### MT-WF-007 — Var olmayan bir adı silmek → `404`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. Hiç var olmamış bir adı sil.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/workflows/hic-yok-boyle-workflow" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Workflow bulunamadi"`.

---

### MT-WF-008 — Kod-tanımlı bir adı silmeye çalışmak → `404`, çalışmaya devam eder

Sınır durumu — `DELETE` yalnız `store`'a bakar, kod kayıtlarının haberi bile
olmaz.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-005'in DB kaydı MT-WF-006/007'de temizlenmediyse önce temizlenir
  (`DELETE .../summarize-and-translate`, gövde farketmez zaten `store`'da kayıt yoktu).

**Adımlar**
1. `summarize-and-translate`'i sil (hiç DB kaydı olmadığı hâlde).
2. Listeyi tekrar çağır.
3. Çalıştır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/workflows/summarize-and-translate" -H "$APB"
curl -s "$APU/api/workflows" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print('summarize-and-translate' in [w['name'] for w in d])"
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"FIX-WF-MSG-01"}'
```

**Beklenen sonuç**
- Adım 1: `HTTP: 404` — silinecek DB kaydı yoktu.
- Adım 2: `True` — kod-tanımlı workflow listede olmaya devam eder.
- Adım 3: normal şekilde çalışır, `WorkflowOutput` üretir. Kod-tanımlı bir
  workflow HTTP üzerinden **hiçbir şekilde** kaldırılamaz; bu tasarım
  kararıdır (K2, dağıtımla gelen davranış veritabanı yazma yetkisiyle ele
  geçirilemez).

---

### MT-WF-009 — Boşluktan ibaret ad → `400` "name alanı zorunludur"

Negatif senaryo. Boş ad yol segmentinde gönderilemez (ASP.NET routing), ama
yalnız boşluklardan oluşan bir ad (`IsNullOrWhiteSpace`) yol segmentine
URL-encode edilerek geçebilir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. Ad yerine iki boşluk (`%20%20`) gönder.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/%20%20" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "Sequential",
  "agentNames": ["summarizer"]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "Workflow tanimin 'name' alani zorunludur."`
  (`WorkflowDefinitionValidator.cs:32-35`).

---

### MT-WF-010 — 🚨 `kind` alanı gövdede atlanırsa sessizce `Sequential`'a düşer

Şüpheli davranış. `WorkflowSaveRequest.Kind` `required` **değildir** ve
varsayılan değeri `WorkflowKind.Sequential` (enum `0`) — JSON gövdesinde
`"kind"` hiç yazılmazsa istemcinin "desen seçmedim" niyeti sessizce
`Sequential` olarak yorumlanır, `400` DEĞİL.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `kind` alanını hiç göndermeden `PUT` yap.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/kind-eksik" -H "$APB" \
     -H "content-type: application/json" -d '{
  "agentNames": ["summarizer"]
}'
```

**Beklenen sonuç**
- `HTTP: 200` (şüphe — doğrulanacak), gövdede `kind: "Sequential"`. Kullanıcı
  arayüz dışında (ör. bir betikle) `kind` göndermeyi unutursa hatasız ama
  YANLIŞ bir desen kaydolur.

---

### MT-WF-011 — `agentNames` boş → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/bos-katilimci" -H "$APB" \
     -H "content-type: application/json" -d '{"kind": "Sequential", "agentNames": []}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... hicbir agent icermiyor. 'agentNames' en az bir
  ad tasimalidir." metnini içerir (`WorkflowDefinitionValidator.cs:42-46`).

---

### MT-WF-012 — Aynı agent adı iki kez → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/tekrar-eden" -H "$APB" \
     -H "content-type: application/json" -d '{"kind": "Sequential", "agentNames": ["summarizer", "summarizer"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'summarizer' agent'i birden fazla kez geciyor ..."
  metnini içerir (`WorkflowDefinitionValidator.cs:61-66`).

---

### MT-WF-013 — `Concurrent` + tek agent → `400` (en az iki ister)

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/tek-concurrent" -H "$APB" \
     -H "content-type: application/json" -d '{"kind": "Concurrent", "agentNames": ["summarizer"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Concurrent' desenini kullaniyor ve en az iki
  agent ister; listede 1 ad var." metnini içerir
  (`WorkflowDefinitionValidator.cs:69-74`). Aynı kural `Handoff` ve
  `GroupChat` için de geçerlidir; bu case yalnız `Concurrent`'i temsil eder.

---

### MT-WF-014 — `Magentic` + boş `managerAgentName` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/yoneticisiz" -H "$APB" \
     -H "content-type: application/json" -d '{"kind": "Magentic", "agentNames": ["translator"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Magentic' desenini kullaniyor ve
  'managerAgentName' zorunludur ..." metnini içerir
  (`WorkflowDefinitionValidator.cs:78-82`).

---

### MT-WF-015 — `Magentic` + yönetici aynı zamanda katılımcı → `400`

Negatif senaryo — yönetici kendini yönlendiremez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/kendini-yoneten" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "Magentic",
  "agentNames": ["summarizer", "translator"],
  "managerAgentName": "summarizer"
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... hem yonetici hem katilimci olarak geciyor ..."
  metnini içerir (`WorkflowDefinitionValidator.cs:84-88`).

---

### MT-WF-016 — `GroupChat` + `managerAgentName` verilirse → `400` (bu alan yalnız `Magentic`'indir)

Negatif senaryo. `GroupChat`'in yöneticisi bir agent değil, kod tarafındaki
round-robin yöneticisidir (bkz. Faz 15 §15.2 "Plandan sapma").

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/groupchat-yanlis" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "GroupChat",
  "agentNames": ["summarizer", "translator"],
  "managerAgentName": "summarizer"
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'GroupChat' deseninde 'managerAgentName'
  kullanmaz ... sirayi kod tarafindaki round-robin yoneticisiyle dagitir."
  metnini içerir (`WorkflowDefinitionValidator.cs:94-99`).

---

### MT-WF-017 — `Sequential` + `handoffInstructions` verilirse → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/yersiz-devir" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "Sequential",
  "agentNames": ["summarizer", "translator"],
  "handoffInstructions": "Gerekince devret."
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Sequential' deseninde 'handoffInstructions'
  kullanmaz. Bu alan yalnizca 'Handoff' desenine aittir." metnini içerir
  (`WorkflowDefinitionValidator.cs:101-105`).

---

### MT-WF-018 — `Magentic` olmayan desende `requirePlanApproval: true` → `400`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | K-125 |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/yersiz-plan-onayi" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "Sequential",
  "agentNames": ["summarizer", "translator"],
  "requirePlanApproval": true
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Sequential' deseninde 'requirePlanApproval'
  kullanmaz. Plan onayi yalnizca 'Magentic' desenine aittir ..." metnini
  içerir (`WorkflowDefinitionValidator.cs:111-116`).

---

### MT-WF-019 — `maxIterations: 0` → `400`

Negatif senaryo, sınır değer.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/sifir-tur" -H "$APB" \
     -H "content-type: application/json" -d '{
  "kind": "GroupChat",
  "agentNames": ["summarizer", "translator"],
  "maxIterations": 0
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "'sifir-tur' workflow'unun 'maxIterations' degeri
  pozitif olmalidir."` (`WorkflowDefinitionValidator.cs:118-121`).

---

### MT-WF-020 — Var olmayan agent adı KAYITta kabul edilir, RUN'da SSE `error` verir

Sınır durumu — yapısal doğrulama (kayıt anında) ile katalog varlığı
doğrulaması (çalıştırma anında) **ayrı** denetimlerdir (`WorkflowDefinitionValidator`
dokümantasyonu, "Agent'larin katalogda gercekten var olup olmadigi derleme
aninda denetlenir").

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. Katalogda olmayan bir agent adıyla tanım kaydet.
2. Çalıştırmayı dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/hayali-agent" -H "$APB" \
     -H "content-type: application/json" -d '{"kind": "Sequential", "agentNames": ["yok-boyle-bir-agent"]}'
curl -N -s -X POST "$APU/api/workflows/hayali-agent/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç — DÜZELTİLDİ (koşum sırasında, kod okumasıyla).** Orijinal
metin "devamında `event: error` çerçevesi gelir" diyordu; bu yanlıştı —
düzeltilmiş hâli:
- Adım 1: `HTTP: 200` — yapısal olarak geçerli, kabul edilir.
- Adım 2: SSE akışı `event: run` çerçevesiyle başlar (bir `runId`
  üretilmiştir), ardından normal bir `event: event` çerçevesi gelir; gövdesi
  `"type":"RunFailed"` ve `"text"` alanında "... 'hayali-agent' workflow'u
  'yok-boyle-bir-agent' agent'ini kullaniyor ancak boyle bir agent katalogda
  yok. Once agent'i tanimlayin, sonra workflow'u kaydedin." metnini taşır,
  akış `event: done` ile normal biter — HTTP bağlantı düzeyinde bir hata
  YOKTUR. `WorkflowDefinitionCompiler`'ın attığı `TraconException`,
  `WorkflowRunner`'ın kendi içinde yakalanıp `RunEventType.RunFailed`
  (`WorkflowRunner.cs:1018`) tipli bir domain event'ine çevriliyor ve normal
  event akışının bir parçası olarak yayınlanıyor; `WorkflowEndpoints.WorkflowEventStream`'in
  ayrı `catch (TraconException ...)` bloğu (`event: error` üreten,
  `WorkflowEndpoints.cs:491-497`) YALNIZCA akışın kendisi (async enumerable)
  DIŞARI istisna fırlatırsa çalışır — bu case'in hata yolu oraya hiç
  uğramıyor.

### MT-WF-030 — Workflows ekranı: kod/veritabanı rozetleri ve katılımcı zinciri

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-001 geçti (`inceleme-zinciri` DB'de kayıtlı ve MT-WF-006/008'de
  silinmediyse — gerekirse yeniden `PUT` et).

**Adımlar**
1. Kabukta **Workflows** ekranını aç.

**Beklenen sonuç**
- `summarize-and-translate`, `summarize-and-approve` satırlarında **Kaynak** sütunu
  `workflows.originCode` ("code") rozeti taşır, **Desen** sütunu
  `workflows.codeGraph` ("code graph") rozeti gösterir (`kind == null`).
- `inceleme-zinciri` satırında **Desen** `Sequential` (accent tonlu rozet),
  **Kaynak** `workflows.originDatabase` ("database") rozeti taşır.
- **Agent'lar** sütunu `summarizer → translator` biçiminde ok ile ayrılmış
  zincir gösterir (`workflows.tsx:100`, `agentNames.join(' → ')`).

---

### MT-WF-031 — Editör: `Save` butonu ad/katılımcı boşken devre dışı, sunucuya istek gitmez

Sınır durumu — istemci tarafı engel.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. **Workflows → New workflow** aç.
2. Ağ sekmesini izlerken hiçbir alan doldurmadan `Save`'e bak.
3. Yalnızca ad gir, katılımcı seçme.

**Beklenen sonuç**
- Adım 2/3: `Save` düğmesi `disabled` — `blocked` koşulu `name.trim().length
  === 0 || agentNames.length === 0` sağlanır (`workflow-editor.tsx:136-140`).
  Ağ sekmesinde hiçbir `PUT` isteği görünmez.

---

### MT-WF-032 — Editör: desen değişince alan görünürlüğü ve maliyet uyarısı değişir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. **New workflow** aç, en az iki katılımcı seç.
2. Desen alanını sırayla `Handoff`, `GroupChat`, `Magentic` yap; her birinde
   ekranı gözlemle.

**Beklenen sonuç**
- `Handoff`: **Handoff instructions** metin alanı görünür; diğerlerinde yok.
- `GroupChat`/`Magentic`: **Max iterations** sayı alanı görünür (`Sequential`/
  `Concurrent`'te yok, `workflow-editor.tsx:274`).
- `Magentic`: **Manager agent** açılır listesi (seçili katılımcılar listede
  YOK — `available.filter(a => !agentNames.includes(a.name))`,
  `workflow-editor.tsx:249`) VE plan onayı checkbox'ı görünür; checkbox'ın
  yanında `workflowEditor.planApprovalCost` ("costs a manager turn") uyarı
  rozeti bulunur.

---

### MT-WF-033 — Editör: `Concurrent` desende tek katılımcı seçiliyken uyarı metni görünür

Sınır durumu.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. **New workflow**, desen `Concurrent` seç.
2. Yalnızca `summarizer`'yi katılımcı olarak ekle.

**Beklenen sonuç**
- `workflowEditor.needsTwo` metni ("{kind} needs at least two participants.")
  sarı uyarı olarak görünür; `Save` devre dışı kalır (`tooFew` koşulu,
  `workflow-editor.tsx:134,231-235`).

---

### MT-WF-034 — Detay ekranı: kod-tanımlı workflow'da `Edit` düğmesi hiç yok

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Workflows listesinden `summarize-and-translate`'e tıkla.

**Beklenen sonuç**
- Başlık yanında `Edit` düğmesi **görünmez** — `editable = descriptor.origin
  !== 'Code' && meta.roles.canAdminister` (`workflow-detail.tsx:187`),
  `origin === 'Code'` olduğu için ilk koşul yanlıştır.
- Grafik paneli ve çalıştırma paneli normal görünür.

---

### MT-WF-035 — Kod-tanımlı bir adın `/edit` URL'ine doğrudan gidilirse hata paneli

Sınır durumu — MT-WF-004'ün arayüzdeki yansıması. MT-WF-004'ün 404 mesajı
2026-08-10'da daha bilgilendirici hale geldi ama HTTP durumu hâlâ `404`'tür —
bu case'in gözlemlediği davranış (form gösterilmez, `ErrorNote` render edilir)
DEĞİŞMEDİ.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Tarayıcıda doğrudan `.../tracon/workflows/summarize-and-translate/edit` adresine git.

**Beklenen sonuç**
- Sayfa bir form GÖSTERMEZ; `existing` sorgusu (`api.workflow(name)`, tekil
  `GET`) `404` alır ve `existing.isError` dalı `ErrorNote` bileşenini render
  eder (`workflow-editor.tsx:129-131`). `ErrorNote`'un metni artık
  MT-WF-004'ün ayırt edici mesajını taşır ("Duzenlenebilir tanim yok").

### MT-WF-040 — `summarize-and-translate` çalıştırma: olay tipleri ve `runs` ağacı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- OpenAI anahtarı tanımlı.

**Adımlar**
1. Çalıştır, ilk SSE çerçevesindeki `runId`'yi not al.
2. Akış bitince `GET /api/runs/{runId}/tree` çağır.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Tracon, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."}'
```
```bash
curl -s "$APU/api/runs/<runId>/tree" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- SSE akışında en az bir kez şu tipler görülür (bu SIRAYLA, `WorkflowEventMapper`'ın
  garantisi): `WorkflowStarted`, `SuperStepStarted`, `ExecutorInvoked`,
  `ExecutorCompleted`, `SuperStepCompleted`, `WorkflowOutput`, `RunCompleted`.
  Hiçbir `AgentResponseEvent` "WorkflowOutput" olarak yanlış sınıflanmaz
  (`AgentResponseEvent`, `WorkflowOutputEvent`'ten türer — Faz 15 §"Ölçülen MAF
  Davranışları" madde 2 — dal sırası testle korunur, koşumda yalnız sonucun
  doğruluğu gözlemlenir).
- `/tree`: **3** satır — `depth=0 kind=Workflow name=summarize-and-translate`,
  altında `depth=1 kind=Agent name=summarizer`, `depth=1 kind=Agent
  name=translator` (Faz 12'nin `parent_run_id` mekanizması).

---

### MT-WF-041 — `runs.kind` / `workflow_name` veritabanı doğrulaması

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-040 geçti.

**Doğrulama sorgusu**
```sql
SELECT kind, workflow_name, agent_name, count(*)
FROM tracon.runs
WHERE workflow_name = 'summarize-and-translate' OR agent_name IN ('summarizer','translator')
GROUP BY kind, workflow_name, agent_name
ORDER BY kind;
```

**Beklenen sonuç**
- `kind=1` (Workflow) satırında `workflow_name='summarize-and-translate'`,
  `agent_name='summarize-and-translate'` (Faz 15 §15.3: "`AgentName` workflow
  satırlarında workflow'un adıdır").
- `kind=0` (Agent) satırlarında `workflow_name` **NULL**, `agent_name`
  sırasıyla `summarizer`/`translator`.

---

### MT-WF-042 — Kontrol noktası listesi ve `$type` ayracının ilk özellik olduğu kanıtı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | K-027 |

**Ön koşul**
- MT-WF-040 geçti, `runId` elde.

**Adımlar**
1. Kontrol noktalarını listele.
2. `workflow_checkpoints.state` sütununun HAM metnini oku.

**Girilecek veri**
```bash
curl -s "$APU/api/workflows/runs/<runId>/checkpoints" -H "$APB" | python3 -m json.tool
```

**Doğrulama sorgusu**
```sql
SELECT checkpoint_id, parent_id,
       pg_typeof(state) AS sutun_tipi,
       substring(state::text from 1 for 40) AS ilk_40_bayt
FROM tracon.workflow_checkpoints
WHERE run_id = '<runId>'
ORDER BY created_at;
```

**Beklenen sonuç — kısmen DÜZELTİLDİ (koşum sırasında, ölçümle).**
- HTTP listesi: en az 2 kayıt, `parentCheckpointId` zincirlenmiş (ilkinde
  `null`, sonrakilerde bir öncekinin `checkpointId`'si).
- SQL: `sutun_tipi = json` (`jsonb` DEĞİL — K-027).
- `ilk_40_bayt` iddiası YANLIŞTI: en üst seviye `state` nesnesi `{"$type":`
  ile DEĞİL, `{"stepNumber":0,"workflow":{"executors":{...` ile başlıyor —
  ilk özellik `stepNumber`'dır. `$type` işaretçisi state içinde gerçekten VAR
  (`state::text LIKE '%$type%'` → `true`) ama üst nesnede değil, executor
  kenarlarının (`edges`) polimorfik dizisinin İÇİNDE, çok daha derinde
  görünüyor (örnek: `..."[{"$type":0,"hasCondition":false,"kind":0,...`).
  K-027'nin asıl iddiası (sütun tipi `json`, `jsonb` DEĞİL — özellik SIRASI
  korunur) hâlâ geçerli ve doğrulandı; yalnız "$type ilk 40 bayttadır" alt
  iddiası yanlıştı, muhtemelen MAF'ın kontrol noktası şemasının farklı bir
  sürümüne dayanıyordu.

---

### MT-WF-043 — Kontrol noktalarının `run_id` ve `session_id` ile filtrelenebilirliği

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- `summarize-and-translate`'i FARKLI bir `sessionId` ile İKİNCİ kez çalıştır
  (`{"message":"...", "sessionId":"ikinci-oturum"}`), iki ayrı `runId` elde et.

**Doğrulama sorgusu**
```sql
SELECT run_id, session_id, count(*)
FROM tracon.workflow_checkpoints
WHERE session_id IN ('<ilk-session-id>', 'ikinci-oturum')
GROUP BY run_id, session_id;
```

**Beklenen sonuç**
- İki ayrı `session_id` için ayrı `run_id` grupları; bir oturumun kontrol
  noktaları diğerine SIZMAZ. `GET .../checkpoints` her `runId` için yalnız
  o çalıştırmaya ait noktaları döner (`ListByRunAsync`, Faz 15 §15.6'nın
  plandan sapan 5. metodu).

---

### MT-WF-044 — Aynı workflow iki kez çalıştırılınca executor kimlikleri SABİT kalır (K-127 kanıtı)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | K-127 |

**Adımlar**
1. `summarize-and-translate`'i iki kez, iki farklı `sessionId` ile çalıştır.
2. Her iki çalıştırmada `ExecutorInvoked` olaylarının `text` alanındaki
   `ozetleyici_<32-hex>` kimliğini karşılaştır.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Test A","sessionId":"kimlik-testi-1"}' | grep -o '"text":"ozetleyici_[a-f0-9]*"' | head -1
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Test B","sessionId":"kimlik-testi-2"}' | grep -o '"text":"ozetleyici_[a-f0-9]*"' | head -1
```

**Beklenen sonuç**
- İki komutun çıktısı **BİREBİR AYNI** kimliği taşır — `WorkflowAgentIdentity.Compute`
  `(workflowName, agentName)` çiftinden SHA-256 türetir, çalıştırmadan
  bağımsızdır (`WorkflowAgentIdentity.cs:75-87`). Uygulama bu iki çağrı
  arasında yeniden başlatılmasa da bu davranış zaten kanıtlanmış olur;
  gerçek "yeniden başlatma sonrası" senaryosu `dotnet run` durdurup tekrar
  başlatmayı gerektirir ve isteğe bağlıdır — koşum notuna eklenir.

### MT-WF-050 — `inceleme-zinciri` tanımla ve çalıştır, ağaç 3 satır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- `FIX-WF-01` kayıtlı (MT-WF-001'den kalmışsa yeniden `PUT` et).

**Adımlar**
1. Çalıştır, `runId`'yi not al.
2. `/tree` çağır.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/inceleme-zinciri/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Tracon, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."}'
```

**Beklenen sonuç**
- Kodda tanımlı ile AYNI davranış: `runs` ağacı 3 satır (1 workflow + 2 agent),
  `WorkflowOutput` üretilir. Arayüzden tanımlı workflow'un `AgentName`
  workflow adı olur (`inceleme-zinciri`).

---

### MT-WF-051 — Varsayılan `resume`: `checkpointId` verilmezse SON kontrol noktası kullanılır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-050 geçti, `runId` elde (çalıştırma TAMAMLANMIŞ olsa bile).

**Adımlar**
1. Boş gövdeyle `resume` çağır.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/resume" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- `event: run` çerçevesi **YENİ** bir `runId` bildirir (orijinalden farklı).
- `resume` **tamamlanmış bir çalıştırmayı bile kabul eder** —
  `PrepareResumeAsync` yalnızca `RequireWorkflowRunAsync` çağırır, `/respond`'un
  aksine `AwaitingInput` durumu ZORUNLU DEĞİLDİR (`WorkflowRunner.cs:216-231`).
  Faz 15'in kendi "Gerçek Kanıt" bölümü de tamamlanmış bir çalıştırmayı
  sürdürüp `WorkflowOutput` ürettiğini gösterir.

---

### MT-WF-052 — Belirli bir `checkpointId` ile erken bir noktadan `resume`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-050 geçti; MT-WF-042 tarzı bir sorguyla o çalıştırmanın **İLK**
  `checkpointId`'sini al.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/resume" -H "$APB" -H "content-type: application/json" \
     -d '{"checkpointId":"<ilk-checkpoint-id>"}'
```

**Beklenen sonuç**
- `HTTP` akışı başarıyla başlar; ilk kontrol noktası genelde ilk super-step
  öncesine denk geldiği için `summarizer`'nin YENİDEN çalıştığı gözlenebilir
  (`MessageDelta` olayları tekrar görülür) — bu Tracon'in değil, grafın
  o noktada kuyrukta bekleyen işin doğal sonucudur (Faz 16 §"Ölçülen MAF
  Davranışları" madde 7'nin aynısı, farklı bir bağlamda).

---

### MT-WF-053 — Tanım güncellendikten sonra ESKİ bir kontrol noktasından `resume` → uyumsuzluk hatası

Negatif senaryo — grafın yapısı değişti.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-050 geçti, `runId` ve bir `checkpointId` not alındı.

**Adımlar**
1. `inceleme-zinciri`'nin katılımcı listesini değiştir (üçüncü bir agent ekle,
   ör. `router`).
2. Eski `checkpointId` ile eski `runId`'yi sürdürmeyi dene.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/inceleme-zinciri" -H "$APB" -H "content-type: application/json" \
     -d '{"kind":"Sequential","agentNames":["summarizer","translator","router"]}'
curl -N -s -X POST "$APU/api/workflows/runs/<eski-runId>/resume" -H "$APB" -H "content-type: application/json" \
     -d '{"checkpointId":"<eski-checkpoint-id>"}'
```

**Beklenen sonuç — DÜZELTİLDİ (koşum sırasında, MT-WF-020 ile aynı gerekçeyle).**
Orijinal metin "SSE `event: error`" diyordu; gerçek davranış MT-WF-020'de
gözlenenin aynısı — çerçeve `event: event` içinde `type: "RunFailed"`, mesaj
içeriği doğru:
- `event: run` → `event: event` (`type: "RunFailed"`, `text`: "'inceleme-zinciri'
  workflow'u bu kontrol noktasindan surdurulemiyor: grafin yapisi kontrol
  noktasi yazildigi andakinden farkli. Workflow tanimi degistirildiyse yeni
  bir calistirma baslatin. Uygulama yeniden baslatildiysa eski kontrol
  noktalari kullanilamaz - executor kimlikleri surec belleginde uretilir.")
  → `event: done`.

### MT-WF-060 — Çalıştırma `AwaitingInput` ile kapanır, `Boolean` form kartı

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Çalıştır, akışın SONUNU izle.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-approve/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Tracon yayin oncesi manuel kabul testi yaziyoruz."}'
```

**Beklenen sonuç**
- Akışın SON olayı `RunAwaitingInput`'tur; hemen öncesinde tam olarak bir
  `WorkflowRequest` olayı gelir. `event: done` çerçevesi normal şekilde gelir
  (akış hatasız kapanır, yalnız iş bitmemiştir).
- `GET .../requests` ile alınan kayıtta: `form: "Boolean"`,
  `requestType: "System.String"`, `responseType: "System.Boolean"`,
  `portId: "yayin-onayi"`, `prompt` "Bu ozet yayinlansin mi?" ile BAŞLAR
  (devamı model özetidir, birebir eşleşme aranmaz).

---

### MT-WF-061 — `GET /requests` yalnız akış kapandıktan sonra çağrılır (arayüz kuralı)

Sınır durumu — akış devam ederken sorulursa da HTTP olarak çalışır, ama
arayüz bilerek beklemeyi tercih eder.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Workflow Detay ekranından `summarize-and-approve`'yı çalıştır (arayüzden,
   mesaj kutusuna `FIX-WF-MSG-01` yaz).
2. Akış sürerken (SpinnerIcon dönerken) "Waiting on you" panelinin
   görünmediğini doğrula.
3. Akış bitince aynı panelin belirdiğini doğrula.

**Beklenen sonuç**
- Adım 2: panel YOK — `pending` sorgusu `enabled: finished` ile korunur
  (`workflow-detail.tsx:64-68`), `finished = runId !== null && !streaming`.
- Adım 3: panel görünür, `Boolean` formuna göre "Yes"/"No" düğmeleri
  (`workflowDetail.yes`/`no`) render edilir.

---

### MT-WF-062 — `respond` onayla → yeni `runId`, çıktı BİREBİR `"Ozet yayinlandi."`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-060 geçti; `runId` ve `requestId` not alındı.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"<requestId>","approved":true}'
```

**Beklenen sonuç**
- `event: run` YENİ bir `runId` bildirir (orijinalden farklı — append-only,
  K-014).
- `WorkflowOutput` olayının `text` alanı **birebir** `"Ozet yayinlandi."`
  değerine eşittir (sabit koddan gelir, model üretmez).
- `GET /api/runs/<yeni-runId>` `status: "Completed"` döner.

---

### MT-WF-063 — `respond` reddet → çıktı BİREBİR `"Yayin iptal edildi; ozet arsivde birakildi."`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- `summarize-and-approve`'yı yeniden çalıştır, yeni `runId`/`requestId` al
  (MT-WF-062'nin `requestId`'si zaten tüketilmiştir).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"<requestId>","approved":false}'
```

**Beklenen sonuç**
- `WorkflowOutput.text` **birebir** `"Yayin iptal edildi; ozet arsivde
  birakildi."` değerine eşittir.

---

### MT-WF-064 — Yanlış `requestId` ile `respond` → SSE `error`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- Yeni bir `summarize-and-approve` çalıştırması, gerçek `runId` elde.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"uydurma-istek-kimligi","approved":true}'
```

**Beklenen sonuç**
- SSE `event: error`; `message` "'uydurma-istek-kimligi' kimlikli bekleyen bir
  istek '<runId>' calistirmasinda yok. Istek listesini GET
  /api/workflows/runs/{runId}/requests ile tazeleyin." metnini içerir
  (`WorkflowRunner.cs:263-265`).

---

### MT-WF-065 — `AwaitingInput` OLMAYAN bir çalıştırmaya `respond` → SSE `error`

Negatif senaryo — tamamlanmış (`summarize-and-translate`) bir çalıştırmayı yanıtlamayı
dene.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-040'ın tamamlanmış `runId`'si.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<tamamlanmis-runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"herhangi-bir-deger","approved":true}'
```

**Beklenen sonuç**
- SSE `event: error`; `message` "'<runId>' kimlikli calistirma insan girdisi
  beklemiyor (durum: Completed). Yalnizca 'AwaitingInput' durumundaki bir
  calistirma yanitlanabilir." metnini içerir (`WorkflowRunner.cs:248-253`).

---

### MT-WF-066 — Başka kiracının `AwaitingInput` çalıştırmasına `/requests` → `404`

Negatif senaryo — kiracı yalıtımı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- `X-Tracon-Tenant: kiraci-alfa` başlığıyla `summarize-and-approve`'yı çalıştır,
  `runId`'yi not al (bkz. `13-KIRACI-VE-GUVENLIK.md` MT-SEC-021 için header
  çözümlemesinin nasıl açıldığı).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-approve/run" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-alfa" -H "content-type: application/json" \
     -d '{"message":"Kiraci alfa testi."}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/runs/<runId>/requests" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- İkinci çağrı `HTTP: 404`, `title: "Calistirma bulunamadi"` —
  `RequireWorkflowRunAsync`'in kiracı eşleşmesi (`WorkflowRunner.cs:289-294`)
  "yetkisiz" bile demez, varlığı sızdırmaz.

### MT-WF-070 — `plan-onayli` tanımla ve çalıştır → `AwaitingInput`, form `PlanReview`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-WF-02`'yi oluştur.
2. Çalıştır.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/plan-onayli" -H "$APB" -H "content-type: application/json" -d '{
  "kind": "Magentic",
  "agentNames": ["translator"],
  "managerAgentName": "summarizer",
  "maxIterations": 2,
  "requirePlanApproval": true
}'
curl -N -s -X POST "$APU/api/workflows/plan-onayli/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Tracon, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir. Bunu Ingilizceye cevir."}'
```

**Beklenen sonuç**
- `PUT`: `HTTP: 200`, `requirePlanApproval: true`.
- `run`: akış `RunAwaitingInput` ile kapanır. `GET .../requests`:
  `form: "PlanReview"`, `requestType:
  "Microsoft.Agents.AI.Workflows.MagenticPlanReviewRequest"`, `prompt`
  BOŞ DEĞİL (planın kendisi — metnine karşı eşleşme aranmaz, yalnız
  varlığı ve `form` alanı değişmezdir).

---

### MT-WF-071 — Planı onayla → yönetici bitirir, katılımcı agent çalışır

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-070 geçti, `runId`/`requestId` elde.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"<requestId>","approved":true}'
```

**Beklenen sonuç**
- Yeni bir `runId` açılır; akışta en az bir `ExecutorInvoked`(`translator`) ve
  bir `WorkflowOutput` görülür. `WorkflowOutput.text` **boş değildir** (model
  üretimi — metnine eşleşme aranmaz).

---

### MT-WF-072 — Metinsiz ret (`text` boş) → SSE `error`

Negatif senaryo — arayüzde "Send back" düğmesi bu durumda zaten `disabled`
olur (`workflow-detail.tsx:411`, `text.trim().length === 0`); bu case
sunucu tarafını doğrudan `curl` ile, arayüzü baypas ederek sınar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- `plan-onayli`'yi yeniden çalıştır, yeni `runId`/`requestId` al.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"<requestId>","approved":false,"text":""}'
```

**Beklenen sonuç**
- SSE `event: error` — Microsoft Agent Framework, düzeltme metni olmayan bir
  ret isteğini işleyemez ve akış hata ile kapanır (Faz 16 §16.4: "Plan
  reddedilirken düzeltme metni zorunludur"). Tam istisna tipi/metni
  koşumda kaydedilir (kod tarafında bu senaryo elle üretilmedi, yalnız
  dokümante edildi — bu case onu ilk kez ölçer).

---

### MT-WF-073 — Düzeltme metniyle ret → yönetici YENİDEN planlar, ikinci bir `AwaitingInput`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- `plan-onayli`'yi yeniden çalıştır, yeni `runId`/`requestId` al.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"<requestId>","approved":false,"text":"Daha kisa bir plan kur, tek adimda cevir."}'
```

**Beklenen sonuç**
- Yeni bir `runId` açılır; akış YİNE `RunAwaitingInput` ile kapanır (yönetici
  yeni bir plan sundu). `GET .../requests` YENİ bir `requestId` ve
  farklı bir `prompt` döner. **Maliyet notu:** bu, yöneticinin İKİNCİ kez
  çalışmasıdır — arayüzün `workflowEditor.planApprovalCost` uyarısının
  somut kanıtıdır.

### MT-WF-080 — `GET /graph` düğüm kimlikleri `ExecutorInvoked` ile birebir eşleşir

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-040'ın SSE çıktısından bir `ExecutorInvoked` olayının `text` alanını
  (ör. `ozetleyici_<32-hex>`) not al.

**Girilecek veri**
```bash
curl -s "$APU/api/workflows/summarize-and-translate/graph" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200` (model çağrısı OLMADAN).
- `nodes` listesinde `id` alanı MT-WF-040'ta not edilen `ExecutorInvoked.text`
  ile **birebir** eşleşen bir düğüm var; o düğümün `kind: "Agent"`,
  `agentName: "summarizer"`.
- `startExecutorId` graftaki bir düğüme karşılık gelir.
- `mermaid` alanı `flowchart` ile başlayan bir metin taşır.

---

### MT-WF-081 — Graf HİÇ çalıştırılmamış bir tanım için de `200` döner (derlenmişten, geçmişten değil)

Sınır durumu — bir tanımın grafiğini görmek için önce çalıştırmak GEREKMEZ.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Hiç çalıştırılmamış TAZE bir Sequential tanım kaydet.
2. Hemen ardından `/graph` iste.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/hic-calismadi" -H "$APB" -H "content-type: application/json" \
     -d '{"kind":"Sequential","agentNames":["summarizer"]}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/hic-calismadi/graph" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200` — hiçbir `runs` satırı olmadan graf üretilir; `WorkflowGraphReader`
  tanımı DERLEYİP (`WorkflowDefinitionCompiler`) yansıtır, çalıştırma geçmişi
  okumaz.

---

### MT-WF-082 — Arayüzde canlı düğüm renklendirme: `running` (cyan, nabız) → `done` (yeşil)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. `summarize-and-translate` Detay ekranını aç, mesaj kutusuna `FIX-WF-MSG-01` yaz,
   `Run`'a bas.
2. Çalıştırma sürerken grafı izle (DevTools → Elements, `[data-testid="workflow-node"]`).
3. Çalıştırma bitince tekrar bak.

**Beklenen sonuç**
- Adım 2: sırayla önce `summarizer` düğümünün `data-state="running"` olduğu
  ve etrafında nabız animasyonu (`animate-pulse`) göründüğü, sonra
  `data-state="done"` olduğu, ardından `translator`'in aynı döngüden geçtiği
  gözlenir.
- Adım 3: HER İKİ agent düğümü de `data-state="done"`, kenar rengi yeşil
  (`--ap-emerald`). Hiçbir düğüm `failed` (kırmızı) değildir.

---

### MT-WF-083 — `Concurrent` desende `Batcher` düğümleri agent SAYILMAZ

Sınır durumu — Faz 16'da düzeltilen regresyon (bileşik kimlik ayrıştırma).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. `FIX-WF-03`'ü (`cift-gorus`, Concurrent, 2 agent) kaydet.
2. Grafı iste.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/cift-gorus" -H "$APB" -H "content-type: application/json" \
     -d '{"kind":"Concurrent","agentNames":["summarizer","translator"]}'
curl -s "$APU/api/workflows/cift-gorus/graph" -H "$APB" | \
  python3 -c "import json,sys; g=json.load(sys.stdin); agents=[n for n in g['nodes'] if n['kind']=='Agent']; print(len(agents), [n['agentName'] for n in agents])"
```

**Beklenen sonuç**
- Çıktı `2 ['summarizer', 'translator']` — TAM 2 `Agent` kind düğüm, `Batcher/*`
  ve `ConcurrentEnd`/`Start` düğümleri `Orchestration`/`Output` olarak
  sınıflanır, agent SAYILMAZ (`WorkflowGraphReader.AgentNameOf`, `/` içeren
  kimlikler `null` döner — `WorkflowGraphReader.cs:98-105`).

---

### MT-WF-084 — "Copy Mermaid" panoya `flowchart` içeren metin kopyalar

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Herhangi bir workflow'un Detay ekranında **Copy Mermaid** düğmesine tıkla.
2. Panoyu bir metin editörüne yapıştır.

**Beklenen sonuç**
- Yapıştırılan metin `flowchart` ile başlar (Microsoft Agent Framework'ün
  `WorkflowVisualizer.ToMermaidString` çıktısı, arayüzün kendi çizdiği SVG
  değil).

### MT-WF-090 — `UseWorkflows()` KALDIRILIRSA çalıştırma uçları `501` döner (geçici kod değişikliği)

Negatif senaryo. `MT-SEC-070` deseniyle aynı: geçici kod değişikliği gerektirir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `samples/Tracon.Api/Program.cs` satır ~103'teki `.UseWorkflows()`
   çağrısını GEÇİCİ olarak yorum satırına al.
2. `dotnet run` ile yeniden başlat.
3. Katalog ve çalıştırma uçlarını sırayla dene.
4. Değişikliği GERİ AL, tekrar başlat.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" -d '{}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/summarize-and-translate/graph" -H "$APB"
```

**Beklenen sonuç**
- Katalog (`GET /api/workflows`): `HTTP: 200` — hâlâ çalışır, `runner is null`
  dalı `store.ListAsync` ile devam eder (`WorkflowEndpoints.cs:126-134`). Kod
  tanımlı `summarize-and-translate` listede **artık görünmez** (kayıt hiç yapılmadı).
- `run`: `HTTP: 501`, `title: "Workflow motoru kayitli degil"`, `detail`
  "... Tracon.Workflows paketini ekleyin ve UseWorkflows() cagirin."
  metnini içerir.
- `graph`: `HTTP: 501` (aynı `NotRegistered` yardımcı metodu).

---

### MT-WF-091 — `Tracon:Workflows:Enabled=false` → SSE `error` "calistirma kapali"

Negatif senaryo — yalnız çalıştırma kapanır, katalog etkilenmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Workflows:Enabled" "false"`
   (`samples/Tracon.Api` dizininde), yeniden başlat.
2. Çalıştırmayı dene.
3. Ayarı `dotnet user-secrets remove "Tracon:Workflows:Enabled"`,
   yeniden başlat.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- SSE `event: run` gelir (bir `runId` ÜRETİLİR), ardından `event: error`;
  `message: "Workflow calistirma kapali. 'Tracon:Workflows:Enabled'
  ayarini acin."` (`WorkflowRunner.cs:339-343`). Bu bir `TraconException`
  olduğu için `error` çerçevesi GÜVENİLİR şekilde üretilir — K-296'nın
  boşluğuna girmez.

---

### MT-WF-092 — `MaxSuperSteps` sınırı geçici olarak düşürülürse çalıştırma DURDURULUR

Negatif senaryo — sonsuz döngü korumasının ucuz ve deterministik tetiklemesi.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Workflows:MaxSuperSteps" "2"`,
   yeniden başlat.
2. `summarize-and-translate`'i çalıştır (gerçek kanıtta bu workflow 3 super-step
   üretir — Faz 15 "Gerçek Kanıt": `SuperStepStarted 3`).
3. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç — DÜZELTİLDİ (koşum sırasında, ölçülerek).** Orijinal metin
`event: error` bekliyordu; bu yanlıştı — MT-WF-020'nin aynı deseni (domain
event, HTTP/SSE hatası değil):
- SSE akışı `event: run` ile başlar, süper-step'ler normal ilerler, sonra
  normal bir `event: event` çerçevesi gelir — `"type":"RunFailed"`,
  `"text"`: "Workflow exceeded the 2 super-step limit and was stopped. The
  handoff or group chat loop may not be terminating; lower the
  'maxIterations' value or add a termination condition to the agent
  instructions." (İngilizce — K-228, kusur değil). Akış `event: done` ile
  normal biter, HTTP/SSE düzeyinde hata YOKTUR.
- `GET /api/runs/{runId}`: `status: "Failed"` — kesin, `"Canceled"` değil
  (ölçüldü).

---

### MT-WF-093 — `EnableCheckpointing=false` iken `resume` denemesi → hata

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "Tracon:Workflows:EnableCheckpointing" "false"`,
   yeniden başlat.
2. `summarize-and-translate`'i çalıştır (checkpoint YAZILMAZ), `runId`'yi not al.
3. Aynı `runId`'yi sürdürmeyi dene.
4. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/resume" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç — NETLEŞTİRİLDİ (koşumda ölçüldü).** İki adaydan hiçbiri
birebir değil — üçüncü, İKİSİNİ birleştiren tek bir mesaj geliyor:
- SSE `event: run` (yeni bir `runId` üretilir), ardından `event: error`;
  `message: "Run '<orijinal-runId>' has no checkpoint. A run started while
  checkpoint writing was disabled cannot be resumed."` (İngilizce — K-228).
  Bu, hem "checkpoint hiç yazılmadı" hem "EnableCheckpointing kapalıydı"
  bilgisini TEK cümlede veriyor; kodun iki ayrı savunma katmanı yerine
  `RequireCheckpointAsync`'in kendisi bu birleşik mesajı üretiyor.

---

### MT-WF-094 — Hiç kontrol noktası yazılmamış bir `runId`'yi sürdürmek → "kontrol noktasi yok"

Negatif senaryo — MT-WF-093'ten bağımsız, checkpoint AÇIKKEN ama o run için
henüz hiç nokta üretilmeden (ör. çalıştırma anında hemen kesilirse) aynı
mesaj beklenir; bu case doğrudan var olmayan bir kombinasyonla sınar.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- `EnableCheckpointing` varsayılan (`true`).

**Adımlar**
1. Var olmayan rastgele bir GUID'i "run" olarak sürdürmeyi dene (bu aslında
   MT-WF-095'in `RunNotFound` yolunu da sınar — bkz. beklenen sonuç).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/00000000-0000-0000-0000-000000000000/resume" \
  -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- SSE `event: error`; `message`: "'00000000-0000-0000-0000-000000000000'
  kimlikli calistirma bulunamadi." (`RequireWorkflowRunAsync`, önce
  RUN VARLIĞI denetlenir — çalıştırma yoksa checkpoint denetimine hiç
  gelinmez).

---

### MT-WF-095 — `sessionId` 128 karakter sınırını aşarsa → SSE `error`

Negatif senaryo, sınır değer.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" \
     -d "{\"sessionId\":\"$(python3 -c 'print("a"*129)')\"}"
```

**Beklenen sonuç**
- SSE `event: error`; `message: "Yurutme oturumu kimligi en fazla 128
  karakter olabilir."` (`WorkflowSessionId.cs:33-37`).

---

### MT-WF-096 — `sessionId` izin verilmeyen karakter içerirse → SSE `error`

Negatif senaryo.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Düşük |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/summarize-and-translate/run" -H "$APB" -H "content-type: application/json" \
     -d '{"sessionId":"gecersiz oturum!"}'
```

**Beklenen sonuç**
- SSE `event: error`; `message: "Yurutme oturumu kimligi yalnizca harf,
  rakam, '-' ve '_' icerebilir."` (`WorkflowSessionId.cs:41-48` — boşluk
  VE `!` her ikisi de reddedilir, ilk karşılaşılan karakterde durur).

---

### MT-WF-097 — Başka kiracının kontrol noktası listesi → `404`

Negatif senaryo — kiracı yalıtımı, `IWorkflowCheckpointStore` seviyesinde.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- MT-WF-066'daki gibi `kiraci-alfa` başlığıyla bir çalıştırma üret, `runId`'yi
  not al.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/runs/<runId>/checkpoints" -H "$APB" \
     -H "X-Tracon-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Calistirma bulunamadi"` — kontrol noktası deposu
  `tenant_id` filtresiyle çalışır, "yetkisiz" bile denmez (`IWorkflowCheckpointStore`
  XML dokümanı: "Baska bir kiracinin noktasi bulunamadi doner").

### MT-WF-100 — 🚨 `RunsRead`-kapsamlı bir API anahtarı workflow `PUT`/`run`'a erişebiliyor mu? (ÖNCÜL GÜNCEL DEĞİL — koşumda çürütüldü)

**2026-09-17 koşum notu:** Bu case'in önerme metni 2026-08-10'da yazılmış ve
o tarihte kaynak okumasıyla ölçülmüştü. Bugünkü koşumda
`WorkflowEndpoints.cs`'in GÜNCEL hâli yeniden okundu:
`grep -n "RequireApiKeyScope" src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs`
**her tek ucun** kendi kapsamını taşıdığını gösteriyor — `ListAsync`/`GetAsync`/
`GetGraphAsync`/`ListFunctionsAsync` → `WorkflowsRead`; `SaveAsync`/`DeleteAsync`
→ `WorkflowsAdmin`; `RunAsync`/`ResumeAsync`/`RespondAsync` → `RunsWrite`;
`ListCheckpointsAsync`/`ListRequestsAsync` → `RunsRead`. Aradaki oturumlarda
(tarih bilinmiyor, muhtemelen MT-RET-040'ın kapandığı dalgayla birlikte) bu
boşluk **kapatılmış**. Aşağıdaki özgün metin ve adımlar TARİHÎ kayıt olarak
bırakılıyor; **Beklenen sonuç** bölümü bugünkü gerçek davranışla
değiştirildi.

---
**Özgün önerme (2026-08-10, artık geçersiz):** `WorkflowEndpoints.Map`
(`src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs`) **hiçbir ucunda**
`.RequireApiKeyScope(...)` çağırmaz — karşılaştırma: `AgentEndpoints.cs` ve
`RunEndpoints.cs` her CRUD/çalıştırma ucuna
`RequireApiKeyScope(ApiKeyScope.AgentsAdmin)`/`RunsWrite` ekler
(`AgentEndpoints.cs:43-99`, `RunEndpoints.cs:79-233`). `TraconEndpointFilter.CheckScope`
şu satırı taşır: `if (requirement is null || record.Scopes.Contains(requirement.Scope))
return null;` (`TraconEndpointFilter.cs:194`) — metadata YOKSA denetim
KOŞULSUZ geçer. Sonuç (şüphe): rol politikaları zaten no-op olduğu için
(bkz. dosya başındaki not), `ApiKeyScope` sistemi de workflow uçlarında HİÇ
uygulanmıyor olabilir — yalnız `RunsRead` taşıyan (yalnız okuma niyetiyle
üretilmiş) bir otomasyon anahtarı workflow TANIMLARINI yazabilir, silebilir
ve gerçek para harcayan bir Magentic çalıştırmasını BAŞLATABİLİR.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 15, 53 |
| **İlgili karar** | K-… (53.3, kapsam ∩ rol ilkesi) |

**Ön koşul**
- `13-KIRACI-VE-GUVENLIK.md` MT-SEC-050'nin API anahtarı oluşturma deseni
  bilinir.

**Adımlar**
1. Yalnız `RunsRead` kapsamıyla bir API anahtarı üret.
2. Bu anahtarla bir workflow tanımı YAZMAYI dene.
3. Aynı anahtarla o tanımı ÇALIŞTIRMAYI dene.
4. Karşılaştırma: aynı anahtarla `AgentsAdmin` gerektiren bir agent ucuna
   (ör. `PUT /api/agents/{name}`) yaz, `403 "Kapsam yetersiz"` aldığını
   doğrula (kontrol grubu — kapsam sisteminin genel olarak ÇALIŞTIĞının
   kanıtı, yalnız workflow uçlarında eksik olduğunu göstermek için).

**Girilecek veri**
```bash
KEY_JSON=$(curl -s -X POST "$APU/api/api-keys" -H "$APB" -H "content-type: application/json" \
  -d '{ "name": "wf-kapsam-testi", "scopes": ["RunsRead"] }')
echo "$KEY_JSON" | python3 -c "import json,sys; print(json.load(sys.stdin)['rawKey'])"
export WFKEY="Authorization: Bearer <yukaridaki-rawKey>"

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/kapsam-testi" -H "$WFKEY" \
     -H "content-type: application/json" -d '{"kind":"Sequential","agentNames":["summarizer"]}'

curl -N -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/workflows/kapsam-testi/run" -H "$WFKEY" \
     -H "content-type: application/json" -d '{}'

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/kapsam-kontrol" -H "$WFKEY" \
     -H "content-type: application/json" -d '{"name":"kapsam-kontrol","instructions":"test"}'
```

**Beklenen sonuç — GÜNCEL (2026-09-17, ölçüldü, önerme ÇÜRÜDÜ)**
- Adım 2 (`PUT /api/workflows/kapsam-testi`, yalnız `RunsRead`): `HTTP: 403`,
  `title: "Insufficient scope"`, `detail: "This endpoint requires the
  'WorkflowsAdmin' scope; the key does not carry it."`
- Adım 3 (`POST .../run`, yalnız `RunsRead`): `HTTP: 403`,
  `detail: "... requires the 'RunsWrite' scope ..."`
- Adım 4 (kontrol grubu, `PUT /api/agents/...`): `HTTP: 403`,
  `detail: "... requires the 'AgentsAdmin' scope ..."` — kapsam sistemi
  hem workflow hem agent uçlarında AYNI şekilde çalışıyor.
- **Kusur YOK.** Şüphenin kaynağı olan boşluk artık kapalı; her workflow
  ucu kendi `ApiKeyScope`'unu taşıyor (bkz. yukarıdaki koşum notu).
  Yalnız-okuma niyetli bir anahtar ne workflow tanımı yazabiliyor ne de
  çalıştırma başlatabiliyor.
- Ek gözlem: `POST /api/api-keys` yanıtındaki alan adı `rawKey` değil
  `plaintextKey` — spec'in "Girilecek veri" bloğundaki `python3 -c "...
  ['rawKey']"` satırı bu yüzden `KeyError` verir; düzeltmesi
  `['plaintextKey']`.

---

## Fonksiyon Düğümleri (Faz 71, F-116)

Faz 71 `AddWorkflowFunction` ile kod içinde kayıtlı bir fonksiyonu `Sequential`
bir tanımın `nodes` listesine karıştırma yeteneği getirdi. Kanıt:
`src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs`,
`Internal/WorkflowFunctionRegistry.cs`, `Internal/WorkflowAgentStepExecutor.cs`,
`Internal/WorkflowDefinitionCompiler.cs` (`BuildMixedSequentialAsync`).

### MT-WF-110 — Kod düğümü hiç kaydedilmemişken mevcut davranış birebir korunur

Regresyon kapısı — `Nodes` boşken hiçbir kod yolu değişmemeli.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-494 |

**Adımlar**
1. `AddWorkflowFunction` hiç çağrılmamış bir kurulumda, sıradan bir
   `AgentNames`-tabanlı `Sequential` tanımı kaydet ve çalıştır (ör.
   `summarize-and-translate`, mevcut `MT-WF-040`).

**Beklenen sonuç**
- Davranış `Faz 15/16`'daki ile birebir aynı: `GET /api/workflows/functions`
  boş liste döner, graf ve çalıştırma hiçbir `Function` türü düğüm göstermez.

---

### MT-WF-111 — `GET /api/workflows/functions` kayıtlı fonksiyonu listeler

**Gerçekten koşuldu ve doğrulandı** (2026-08-19, `samples/Tracon.Api`,
port 5091, EchoModelProvider — bkz. `docs/arsiv/fazlar/71-WORKFLOW-KOD-DUGUMU.md` "Doğrulama
komutları" bölümü).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | — |

**Ön koşul**
- `samples/Tracon.Api/Program.cs`'te `word-count` adıyla kayıtlı bir
  fonksiyon (Faz 71'in kendi örneği).

**Girilecek veri**
```bash
curl -s "$APU/api/workflows/functions" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200`; dizide `name: "word-count"`, `description`, `inputType`/
  `outputType` (CLR tip görünen adı, ör.
  `System.Collections.Generic.List\`1[[Microsoft.Extensions.AI.ChatMessage, ...]]`)
  alanları dolu.

---

### MT-WF-112 — Agent → fonksiyon → agent zinciri uçtan uca koşar; graf üçünü ayırt edilebilir çizer

**Gerçekten koşuldu ve doğrulandı** (2026-08-19). `summarize-and-count` adıyla
`{summarizer: Agent, word-count: Function}` düğüm listeli bir `Sequential`
tanım kaydedildi ve çalıştırıldı.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-495 |

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/summarize-and-count" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"word-count","kind":"Function"}]}'

curl -s "$APU/api/workflows/summarize-and-count/graph" -H "$APB" \
  | python3 -c "import json,sys; g=json.load(sys.stdin); print([(n['id'], n['kind'], n.get('agentName')) for n in g['nodes']])"

curl -N -s -X POST "$APU/api/workflows/summarize-and-count/run" -H "$APB" \
     -H "content-type: application/json" -d '{"message":"..."}'
```

**Beklenen sonuç**
- Graf: `[('summarizer', 'Agent', 'summarizer'), ('word-count', 'Function', None)]`
  — iki düğüm de doğru `kind` taşır; `word-count` fonksiyonu `agentName: null`.
- Çalıştırma: SSE akışında her iki düğüm için `ExecutorInvoked` ve
  `ExecutorCompleted` görülür; `WorkflowOutput` fonksiyonun eklediği metni
  taşır (agent'ın çıktısına eklenmiş kelime sayısı).
- `GET /api/runs/{runId}/tree`: yalnız `summarizer` için bir `Agent` satırı
  açılır (`childRunCount: 1`); `word-count` HİÇ `runs` satırı açmaz — bu,
  kod düğümünün maliyet toplamına neden sıfır katkı verdiğinin kanıtıdır
  (özel bir durum kodu YAZILMADAN).
- Arayüzde `word-count` düğümü `summarizer`'dan **farklı renk/köşe
  yarıçapıyla** çizilir (`WorkflowGraphView`'in `KIND_STYLE` haritası); yeni
  bir “fonksiyon” lejant rozeti görünür.

---

### MT-WF-113 — Kayıtlı olmayan fonksiyon adı **kaydetme anında** reddedilir

**Gerçekten koşuldu ve doğrulandı** (unit + fonksiyonel test, ayrıca gerçek
API'ye karşı da).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-496 |

**Adımlar**
1. `nodes` listesinde var olmayan bir fonksiyon adı taşıyan bir tanımı
   `PUT` ile kaydetmeyi dene.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/kayitsiz-fonksiyon" -H "$APB" \
     -H "content-type: application/json" \
     -d '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"yok-boyle-bir-fonksiyon","kind":"Function"}]}'
```

**Beklenen sonuç**
- `HTTP: 400`; `detail` alanı `'yok-boyle-bir-fonksiyon'` adını ve "no such
  function is registered" ifadesini içerir. Tanım hiç kaydedilmez —
  çalıştırma denemesi GEREKMEZ, hata kaydetme anında gelir.

---

### MT-WF-114 — Patlayan fonksiyon düğümü → `ExecutorFailed`, gerçek hata mesajı görünür

**Gerçekten koşuldu ve doğrulandı** (unit test:
`WorkflowFunctionNodeTests.A_function_node_that_throws_fails_the_run_with_the_real_error`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-401 (aynı sarmalayıcı-soyma mekanizması) |

**Beklenen sonuç**
- Çalıştırma `Failed` olarak kapanır; kaydedilen hata mesajı fonksiyonun
  gerçekten fırlattığı istisnanın metnini taşır (K-401'in
  `TargetInvocationException`/tek-elemanlı `AggregateException` soyma
  mantığı, MAF'ın kendi çağrı zincirinden gelen sarmalayıcıyı da kapsar).

---

### MT-WF-115 — İptal, fonksiyon düğümünün ortasında istense de çalıştırmayı `Canceled` yapar

**Gerçekten koşuldu ve doğrulandı** (unit test:
`WorkflowFunctionNodeTests.Cancellation_requested_inside_a_function_node_still_records_Canceled`).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-432 (aynı süper-adım-sınırı mekanizması) |

**Beklenen sonuç**
- MAF adım ORTASINDA dış iptali onurlandırmaz (K-432); `WorkflowRunner`'ın
  kendi süper-adım-sınırı denetimi kod düğümü için de aynı şekilde çalışır —
  düğüm türüne özel bir kod GEREKMEDİ. Çalıştırma `Canceled` olarak kapanır.

---

### MT-WF-116 — 🚨 Kontrol noktasından devam: kod düğümü İDEMPOTENT olmak ZORUNDADIR

**Gerçekten koşuldu ve doğrulandı** — planın kendi "en riskli hata modu"
sorusunun cevabı iki ayrı unit testle ÖLÇÜLDÜ:
`WorkflowFunctionNodeTests.Resuming_an_ALREADY_COMPLETED_mixed_chain_does_NOT_re_run_its_function_node`
ve
`WorkflowFunctionNodeTests.Resuming_from_an_EARLIER_checkpoint_RE_RUNS_the_function_node_that_follows_it`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-498 |

**Ölçülen sözleşme**
- Tamamlanmış bir çalıştırmayı EN SON kontrol noktasından (açık bir
  `checkpointId` verilmeden — `/resume`'un varsayılanı) sürdürmek fonksiyonu
  YENİDEN ÇAĞIRMAZ: kontrol noktası zaten bitmiş grafı yansıtır, yapılacak
  bir şey kalmamıştır.
- Fonksiyonun KENDİ süper-adımından ÖNCEKİ bir kontrol noktasından
  sürdürmek — gerçek bir çökme kurtarmasının alacağı biçim — o süper-adımı
  YENİDEN OYNATIR ve fonksiyon AYNI girdiyle TEKRAR çalışır.
- Sonuç: `AddWorkflowFunction` ile kaydedilen her işleyici, checkpoint
  yazımı açıkken (varsayılan) İKİ KEZ çağrılmaya dayanıklı olmalıdır — bir
  dosya yazan, bir HTTP çağıran veya bir veritabanına satır ekleyen kod
  düğümü kendi idempotency anahtarını taşımalıdır.

**Adımlar (elle koşum için)**
1. Yan etkisi olan (ör. bir sayaç artıran) bir fonksiyon kaydet, agent →
   fonksiyon zinciri kur ve çalıştır.
2. `GET /api/workflows/runs/{runId}/checkpoints` ile İKİ kontrol noktası
   olduğunu doğrula (fonksiyondan önce ve sonra).
3. `POST /api/workflows/runs/{runId}/resume` ile İLK (fonksiyondan önceki)
   kontrol noktasının id'sini vererek sürdür.
4. Yan etkinin (sayaç) İKİNCİ kez tetiklendiğini doğrula.

---

### MT-WF-117 — Düğüm başına retry: geçici sağlayıcı hatası veren fonksiyon yeniden denenir, koşu düşmez (Faz 87)

**Gerçekten koşuldu ve doğrulandı** —
`WorkflowNodeRetryTests.A_transient_error_in_a_function_node_is_retried_and_costs_no_extra_super_step`
üç kez `TraconProviderUnavailableException` fırlatan bir fonksiyon
düğümünü gerçek bir `WorkflowRunner` üzerinden çalıştırır ve koşunun
`Completed` bittiğini, deneme sayısının `3` olduğunu ÖLÇER. Açık Soru 2'nin
cevabı da AYNI testte ölçüldü: retry döngüsü fonksiyonun KENDİ çağrısının
İÇİNDE kalır — Microsoft Agent Framework düğümü mesaj başına yalnız BİR kez
çağırır, bu yüzden kaç deneme yapıldığından BAĞIMSIZ olarak
`SuperStepStartedEvent` sayısı, hiç hata olmayan bir koşumla AYNIDIR (test
bunu iki gerçek koşumu (`flaky`/`baseline`) karşılaştırarak kanıtlar).

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Ölçülen sözleşme**
- `AddWorkflowFunction(..., retryPolicy: new WorkflowNodeRetryPolicy { MaxAttempts = N, ... })`
  ile kaydedilen bir düğüm, çağrısı `IRunErrorClassifier`'ın
  `ProviderError`/`ProviderUnavailable`/`RateLimited`/`Timeout` sınıflarından
  birine sınıflandırdığı bir istisna fırlatırsa, `MaxAttempts`'e kadar
  (üstel geri çekilmeyle) yeniden denenir.
- Başka bir sınıf (`CompilationFailed`, `ToolError`, …) İLK denemede
  yeniden denenmeden yukarı fırlatılır.
- `TraconWorkflowOptions.MaxSuperSteps` sayacı bu düğümün denemelerinden
  ETKİLENMEZ.

**Adımlar (elle koşum için — sample app'e retry'lı bir düğüm eklendiyse)**
1. Örnek uygulamaya (veya bir sınama projesine) ilk `N-1` çağrısında
   `TraconProviderUnavailableException` fırlatan, `N`'inci çağrıda
   başarılı dönen bir fonksiyon kaydet: `retryPolicy: new WorkflowNodeRetryPolicy
   { MaxAttempts = N, InitialDelay = TimeSpan.FromMilliseconds(50) }`.
2. Agent → fonksiyon zincirini çalıştır.
3. Koşunun `Completed` bittiğini doğrula (`Failed` DEĞİL).
4. Sunucu günlüğünde/özel bir sayaçta fonksiyonun `N` kez çağrıldığını
   doğrula.

**Beklenen sonuç**
- Koşu `Completed` biter — geçici hata koşuyu DÜŞÜRMEZ.
- Fonksiyon tam `N` kez çağrılmıştır.
- `GET /api/workflows/runs/{runId}/events`'teki `SuperStepStarted` olay
  sayısı, AYNI zincirin hiç hatasız çalıştığı bir koşumla EŞİTTİR (unit
  testin ölçtüğü sözleşme; elle koşumda bu adım opsiyoneldir).

---

### MT-WF-118 — Düğüm başına retry: kalıcı hata veren fonksiyon yeniden DENENMEZ, koşu düşer (Faz 87)

**Gerçekten koşuldu ve doğrulandı** —
`WorkflowNodeRetryTests.A_permanent_error_is_never_retried` `TraconException`
(kalıcı, `CompilationFailed` sınıfına düşen) fırlatan bir düğümün TEK bir
denemede yukarı fırlatıldığını ölçer;
`WorkflowFunctionNodeTests.A_function_node_that_throws_fails_the_run_with_the_real_error`
(Faz 71, retry'sız) ile AYNI temel davranışın retry POLİTİKASI VARKEN de
korunduğunu tamamlar.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Orta |
| **İlgili faz** | Faz 87 |
| **İlgili karar** | — |

**Adımlar (elle koşum için)**
1. Her çağrıda kalıcı bir hata fırlatan (`InvalidOperationException` gibi
   `ProviderError`/`ProviderUnavailable`/`RateLimited`/`Timeout` DIŞINDA
   sınıflandırılan) bir fonksiyon, `retryPolicy: new WorkflowNodeRetryPolicy
   { MaxAttempts = 3 }` ile kaydet.
2. Zinciri çalıştır.
3. Fonksiyonun kaç kez çağrıldığını ve koşunun son durumunu oku.

**Beklenen sonuç**
- Fonksiyon **bir kez** çağrılır — `MaxAttempts = 3` olmasına RAĞMEN yeniden
  denenmez, çünkü hata sınıfı geçici değildir.
- Koşu `Failed` biter; `error.message` fonksiyonun gerçek istisna metnini
  taşır (metin eşleştirmesi değil, tipli sağlayıcı hatası sınıflandırması —
  Faz 44).

---

### MT-WF-119 — Aynı adda iki kod-tanımlı workflow: `TraconException`, ham `ArgumentException` DEĞİL (BL-039, Faz 122)

**Gerçekten koşuldu ve doğrulandı** —
`WorkflowCatalogTests.Duplicate_code_workflow_names_throw_an_TraconException`
iki `CodeWorkflowRegistration`'ı aynı adla kaydedip `WorkflowCatalog`'un
kurucusunun `TraconException` (mesajda çakışan ad adı geçen) fırlattığını
ölçer — eskiden `Dictionary.ToDictionary` ham `System.ArgumentException`
fırlatıyordu, kardeşi `WorkflowFunctionRegistry`'nin zaten kullandığı desenle
hizalandı.

| | |
|---|---|
| **İzlek** | C |
| **Önem** | Düşük |
| **İlgili faz** | Faz 122 |
| **İlgili karar** | — |

**Adımlar (elle koşum için)**
1. `AddWorkflow<T>()` veya eşdeğer bir kod-tanımlı workflow kaydını AYNI ada
   sahip **iki** kez çağır (örnek uygulamaya geçici bir ikinci `.AddWorkflow(...)`
   satırı ekleyerek).
2. Uygulamayı başlat.

**Beklenen sonuç**
- Uygulama `TraconException` ile başlangıçta düşer; mesaj çakışan workflow
  adını **iki kez de değil, en az bir kez** adlandırır ve "Workflow names must
  be unique." cümlesini taşır.
- Ham `System.ArgumentException` (`"An item with the same key has already
  been added"` metni) **hiçbir zaman** görünmez.
