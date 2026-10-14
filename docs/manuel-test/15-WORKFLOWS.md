# 15 — Workflows: Yürütme, Kontrol Noktası, Graf ve Human-in-the-Loop (`WF`)

> **Alan kodu:** `WF` · **Faz:** 15, 16, 71, 87 (yalnız düğüm başına retry), 194, 195, 196, 197, 200, 208
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
> `summarize-and-translate`, `summarize-and-approve`; ve `AddWorkflowFunction(...)`
> blokları: `word-count` (Faz 71), `speak` (Faz 195, ayarlı)).
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

### MT-WF-114 — Patlayan fonksiyon düğümü → `ExecutorFailed`, hata güvenli metinle görünür

**Gerçekten koşuldu ve doğrulandı** (unit test:
`WorkflowFunctionNodeTests.A_function_node_that_throws_fails_the_run_without_leaking_the_raw_message`;
2026-10-12'de F-334 ile yeniden yazıldı — eski adı ve "gerçek mesaj" iddiası
K-640'a aykırıydı).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 |
| **İlgili karar** | K-401 (aynı sarmalayıcı-soyma mekanizması) |

**Beklenen sonuç**
- Çalıştırma `Failed` olarak kapanır. Hata mesajı ve `ExecutorFailed` yükü
  fonksiyonun fırlattığı istisnanın **tipini** ve bir `ref` taşır
  (`InvalidOperationException failed. (ref: …)`); istisnanın kendi metni
  yalnız günlükte, aynı `ref` ile durur (K-640). Tip, sarmalayıcının değil
  gerçek istisnanındır (K-401'in `TargetInvocationException`/tek-elemanlı
  `AggregateException` soyma mantığı).

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
- Kalıcı bir HTTP durumu (`408` ve `429` dışındaki her `4xx`) taşıyan hata,
  sınıfı `ProviderError` olsa bile İLK denemede fırlatılır (F-338, MT-WF-124).
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
`WorkflowFunctionNodeTests.A_function_node_that_throws_fails_the_run_without_leaking_the_raw_message`
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
- Koşu `Failed` biter; `error.message` istisnanın tipini ve bir `ref` taşır,
  metnini taşımaz (K-640; sınıflandırma metin eşleştirmesi değil, tipli
  sağlayıcı hatasıdır — Faz 44).

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

---

### MT-WF-120 — Konsolda fonksiyon düğümlü workflow'u düzenlemek zinciri KORUR (kusur-giderme, 2026-10-10)

**Gerçekten koşuldu ve doğrulandı** — örnek uygulamada (bellek içi store)
düzeltmeden önce zincir silindi, düzeltmeden sonra korundu. Otomatik karşılığı:
`WorkflowTests.Editing_a_function_node_workflow_keeps_its_chain` (E2E) ve
`src/Tracon.UI/frontend/src/full-replace-editors.test.tsx` → "workflow that
mixes function nodes in".

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 71 (Sapma #6) |
| **İlgili karar** | K-725 |

**Ön koşul**
- Örnek uygulama; `word-count` fonksiyonu kayıtlı (örnekte hazır gelir).

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/mixed-chain" -H "$APB" -H 'content-type: application/json' \
  -d '{"description":"Summarize, then count words.","kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"word-count","kind":"Function"}]}'
```

**Adımlar**
1. Konsolda `Workflows → mixed-chain → Edit` aç.
2. Açıklamayı değiştir ve **Save**'e bas.
3. `GET /api/workflows/mixed-chain` ve `GET /api/workflows/mixed-chain/graph` oku.

**Beklenen sonuç**
- Editör zinciri sırayla gösterir: `summarizer` (agent), `word-count`
  (function). Satırlarda taşıma okları ve ✕ vardır (Faz 195'ten beri zincir
  konsoldan düzenlenir; bu case artık "başka bir alanı kaydetmek zinciri
  silmez" kapısıdır).
- **Kind** `Sequential`'da kilitlidir ve nedenini söyler. **Save** açıktır.
- Kayıtta açıklama değişmiştir. `nodes` iki düğümü aynı sırayla taşır,
  `agentNames` boştur. Graf hâlâ bir `Function` düğümü çizer.
- 🚨 Eski kusur: editör boş katılımcı listesiyle açılıyordu. Agent eklenip
  kaydedilince `nodes` `[]` oluyor, fonksiyon düğümü uyarısız siliniyordu.

---

### MT-WF-121 — Sessiz çalışan düğüm konsolda 30 sn'yi aşar, run `Completed` biter (kusur-giderme, 2026-10-12, F-325)

Regresyon. Workflow akışı düğüm çalışırken hiç bayt yazmıyordu; konsol 30 sn
sessizlikte okumayı bırakıyor, kapanan bağlantı run'ı `Canceled` yapıyordu.
Otomatik karşılığı: `StreamKeepAliveTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-885 |

**Ön koşul**
- Örnek uygulamada 45 sn bekleyen bir fonksiyon düğümü (`Task.Delay(45 s, ct)`)
  ve onu taşıyan bir workflow.

**Adımlar**
1. Konsolda workflow'u **Run** ile başlat; 45 sn bekle.
2. Aynı anda `curl -N` ile akışı izle.

**Beklenen sonuç**
- Akışta sessizlik sırasında yaklaşık 15 sn'de bir `: waiting` satırı görünür.
- Konsol "bağlantı koptu" demez; run `Completed` biter.
- 🚨 Eski kusur: 30 sn'de konsol okumayı bıraktı, run `Canceled` oldu.

---

### MT-WF-122 — Süresi dolan agent adımı workflow'u `Failed` bitirir, sonraki düğüm koşmaz (kusur-giderme, 2026-10-12, F-328)

Regresyon. Süre, bütçe ve onay reddi metin olarak sonraki düğüme akıyor,
run `Completed` bitiyordu. Otomatik karşılığı: `WorkflowStepRefusalTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-887 |

**Ön koşul**
- `Tracon__AgentGraph__ChildDeadline=00:00:02`; ilk düğüm uzun cevap üreten
  bir agent, ikinci düğüm `word-count`.

**Adımlar**
1. Workflow'u çalıştır.
2. `GET /api/runs/{workflowRunId}` ve kök olay akışını oku.

**Beklenen sonuç**
- Workflow `Failed`; `error.type` `child_run_timeout`, `error.class` `Timeout`.
- Kök akışta `child.timed-out` vardır; `word-count` için `ExecutorInvoked` yoktur.
- 🚨 Eski kusur: "did not respond in time" cümlesi `word-count`'a girdi oldu.

---

### MT-WF-123 — Uzun istemli bekleyen istek listelenir ve iki kez cevaplanabilir (kusur-giderme, 2026-10-12, F-327)

Regresyon. 8 KB'ı aşan istem isteği listeden düşürüyordu; cevap run'ı kaynağın
checkpoint'lerini siliyordu. Otomatik karşılığı: `WorkflowHumanInTheLoopTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-888 |

**Ön koşul**
- `Tracon__Workflows__KeepCheckpointsAfterCompletion=false`; istemi ~2.000
  Türkçe karakterlik bir `RequestPort` workflow'u.

**Adımlar**
1. Workflow'u çalıştır; `GET /api/workflows/runs/{runId}/requests` oku.
2. İsteği bir kez yanlış tipte (`{"text":"belki"}`), sonra doğru cevapla.
3. Aynı isteği ikinci kez cevapla.

**Beklenen sonuç**
- İstek listelenir; istem `…[truncated]` ile biter, `form` doğrudur.
- Yanlış cevap run'ı `Failed` biter ama doğru cevap çalışır; ikinci cevap yeni
  bir dal açar (K-130).
- 🚨 Eski kusur: istek listede yoktu; yanlış cevaptan sonra "has no checkpoint".

---

### MT-WF-124 — `401` alan düğüm bir kez çağrılır (kusur-giderme, 2026-10-12, F-338)

Regresyon. Düğüm retry'ı yalnız hata sınıfına bakıyordu ve `ProviderError`
kalıcı `4xx`'i de kapsar. `401` alan düğüm `MaxAttempts` kez çağrılıyordu.
Otomatik karşılığı: `WorkflowNodeRetryTests`, `WorkflowNodeRetryEndpointTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | — |

**Ön koşul**
- Örnek uygulamada `retryPolicy: new WorkflowNodeRetryPolicy { MaxAttempts = 3,
  InitialDelay = TimeSpan.FromMilliseconds(200) }` ile kayıtlı bir fonksiyon
  düğümü. Düğüm her çağrıda günlüğe bir satır yazar, sonra
  `throw new HttpRequestException(null, null, HttpStatusCode.Unauthorized)`
  ile `EnsureSuccessStatusCode()`'un `401` istisnasını üretir.
- Agent → bu fonksiyon zincirini taşıyan bir workflow.

**Adımlar**
1. Workflow'u çalıştır.
2. Günlükte düğümün satırını say; `GET /api/runs/{workflowRunId}` oku.
3. Durumu `HttpStatusCode.ServiceUnavailable` yap, uygulamayı yeniden başlat ve
   1–2'yi tekrarla.

**Beklenen sonuç**
- `401` ile düğüm **bir kez** çağrılır; workflow `Failed` biter.
- `503` ile düğüm **üç kez** çağrılır; geçici hata yine yeniden denenir.
- 🚨 Eski kusur: `401` ile düğüm üç kez çağrıldı.
- Koşum sonunda geçici düğüm ve workflow geri alınır.

---

### MT-WF-125 — Patlayan düğümün `ExecutorFailed` olayı ham istisna metnini taşımaz (kusur-giderme, 2026-10-12, F-334)

Regresyon. Run satırı güvenli metni yazıyordu, ama aynı hatanın
`ExecutorFailed` olayı istisnanın kendi mesajını olay tablosuna, SSE akışına ve
her sink'e yazıyordu. Otomatik karşılığı:
`WorkflowFunctionNodeTests.A_failed_node_event_carries_the_safe_text_and_never_the_raw_message`,
`WorkflowFailureTextTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Kritik |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-640 |

**Ön koşul**
- Örnek uygulamada bir fonksiyon düğümü
  `throw new HttpRequestException("connect to media-worker:8080 failed")` atar.
- Agent → bu fonksiyon zincirini taşıyan bir workflow.

**Adımlar**
1. Workflow'u konsoldan çalıştır; akıştaki hata satırını oku.
2. `GET /api/runs/{workflowRunId}/events` ve `GET /api/runs/{workflowRunId}` oku.
3. Günlükte `ref` değerini ara.

**Beklenen sonuç**
- Akışta, olaylarda ve run satırında `media-worker:8080` geçmez.
- `ExecutorFailed` yükü ve `error.message` `HttpRequestException failed. (ref: <id>)`
  taşır; iki yerdeki `ref` aynıdır.
- Günlükte aynı `ref` ile tek bir hata girdisi istisnanın tamamını taşır.
- 🚨 Eski kusur: `ExecutorFailed` yükü `connect to media-worker:8080 failed` idi.

---

### MT-WF-126 — Bekleyen workflow'un oturumundaki ek 7 günden sonra da durur (kusur-giderme, 2026-10-12, F-332)

Regresyon. Workflow oturum kimliği hiçbir zaman `sessions` satırı almaz.
Saklama bu yüzden agent adımının ürettiği görseli yetim sayıyordu. Run
`AwaitingInput`'ta beklerken ek 7 gün sonra siliniyordu. Otomatik karşılığı:
`AttachmentByteLifecycleTests.A_workflow_runs_attachment_is_an_orphan_only_after_the_run_is_done`
(üç SQL sağlayıcısı).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-888 |

**Ön koşul**
- SQLite ile çalışıyor; `Tracon__Retention__Enabled=true`.
- İnsan cevabı bekleyen bir `RequestPort` workflow'u (MT-WF-123'teki gibi).

**Girilecek veri**
```bash
ID=$(curl -s -X POST "$APU/api/attachments?sessionId=wf-ek-oturum" -H "$APB" \
     -F "file=@/tmp/fix-mm.png;type=image/png" | jq -r '.id')
sqlite3 samples/Tracon.Api/tracon-manuel.db \
  "UPDATE tracon_attachments SET created_at = strftime('%Y-%m-%dT%H:%M:%fZ','now','-30 days') WHERE id = upper('$ID');"
curl -s -X POST "$APU/api/retention/run?target=attachments" -H "$APB" | jq
sleep 2
curl -s "$APU/api/attachments?sessionId=wf-ek-oturum" -H "$APB" | jq 'length'
```

**Adımlar**
1. Workflow'u `"sessionId":"wf-ek-oturum"` ile çalıştır. Run `AwaitingInput`
   olur.
2. Bu oturuma bir ek yükle. Satırı SQL ile 30 gün geriye çek.
3. Saklamayı çalıştır ve oturumun eklerini listele.
4. Run'ı SQL ile `Completed` (`status = 1`) yap ve `completed_at`'i 30 gün
   geriye çek. 3. adımı tekrarla.

**Beklenen sonuç**
- 3. adımda liste `1` döner; ek durur.
- 4. adımda liste `0` döner; run bitmiş ve kesim tarihinden eskidir.
- Cevaplanan bir istek de run'ı `AwaitingInput` bırakır (K-130). Bu yüzden ek,
  o run beklediği sürece durur.
- 🚨 Eski kusur: 3. adımda ek silindi; cevap geldiğinde görsel yoktu.

---

### MT-WF-127 — Workflow değerleri her agent adımının talimatına ulaşır (Faz 194, F-294)

Mutlu yol. Workflow bir parametre şeması bildirir; run değer verir; her adım
yalnız **kendi** şemasının adını alır. Otomatik karşılığı:
`WorkflowParameterBindingTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 194 |
| **İlgili karar** | K-897 |

**Ön koşul**
- Örnek uygulama **Echo** sağlayıcıyla çalışıyor: `Tracon__Providers__OpenAI__ApiKey=`
  (boş). `podcast-writer` ve `podcast-editor` yalnız bu kipte kayıtlıdır; ikisi
  de talimatı yanıta yazar.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/podcast" -H 'Content-Type: application/json' -d '{
  "kind":"Sequential","agentNames":["podcast-writer","podcast-editor"],
  "parameters":[{"name":"tone","kind":"Text","required":true},
                {"name":"voice","kind":"Text","defaultValue":"alloy"}]}'
curl -sN -X POST "$APU/api/workflows/podcast/run" -H 'Content-Type: application/json' \
  -d '{"message":"Episode 12","parameters":{"tone":"formal","voice":"nova"}}'
```

**Adımlar**
1. Workflow'u kaydet. Yanıtın `parameters` dizisini oku.
2. Run'ı çalıştır. `WorkflowOutput` olayının `text` alanını oku.
3. `GET /api/runs/{runId}/tree` ile alt satırları listele.

**Beklenen sonuç**
- `WorkflowOutput` iki talimatı da taşır: `Write the script in a formal tone.`
  ve `Edit for a formal tone, voice nova.` (writer `voice`'u bildirmez, o ad
  writer için düşer).
- Ağaçta `podcast-writer` ve `podcast-editor` `Completed` iki alt satırdır.
- `voice` verilmeseydi editor'ün talimatı `voice alloy` taşırdı: workflow'un
  varsayılanı, editor'ün kendi varsayılanını ezer.

---

### MT-WF-128 — Geçersiz değer akış başlamadan `400` olur; eksik katılımcı parametresi run'ı model çağrısız düşürür (Faz 194, F-294)

Sınır senaryosu. Üç ayrı ret: değer eksik, ad bilinmiyor, katılımcının zorunlu
parametresini workflow karşılamıyor. Otomatik karşılığı:
`WorkflowRunParameterGateTests` · `WorkflowParticipantCoverageTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 194 |
| **İlgili karar** | K-897 |

**Ön koşul**
- MT-WF-127'deki `podcast` workflow'u kayıtlı.

**Adımlar**
1. `parameters` vermeden run çağır.
2. `{"tone":"formal","tonne":"x"}` ile run çağır.
3. `podcast2` adlı yeni bir workflow kaydet: `agentNames` `["podcast-writer","podcast-editor"]`
   ve **yalnız** `tone`'u bildirsin; sonra bir agent ekleyin ki zorunlu, varsayılansız
   bir parametresi workflow'da olmasın (ör. ayrı bir agent `{{style}}`, `required`).
   Run'ı `{"tone":"formal"}` ile çağır.

**Beklenen sonuç**
- Adım 1: `400`, `missingParameters: ["tone"]`; run satırı açılmaz.
- Adım 2: `400`, `unknownParameters: ["tonne"]`. Gövde, agent `/run`'ın gövdesiyle
  aynı üç dizi (`missingParameters`, `unknownParameters`, `tooLongParameters`)
  taşır. Yanıtta `x` değeri geçmez.
- Adım 3: akış açılır, run `Failed` biter; `error.message` katılımcıyı ve
  parametreyi adlandırır; **hiçbir adım satırı yoktur** (sıfır model çağrısı).

---

### MT-WF-129 — Resume aynı değerleri kullanır; değeri kayıtlı olmayan run sessizce varsayılanla sürmez (Faz 194, F-294)

Sınır senaryosu. Otomatik karşılığı: `WorkflowParameterResumeTests` ·
`WorkflowParameterRespondTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 194 |
| **İlgili karar** | K-897 · K-898 |

**Ön koşul**
- MT-WF-127'deki `podcast` workflow'u ve tamamlanmış bir run (`$RID`).

**Adımlar**
1. `GET /api/workflows/runs/$RID/checkpoints` ile ilk kontrol noktasını al.
2. `POST /api/workflows/runs/$RID/resume` gövdesi `{"checkpointId":"<ilk>"}`.
3. Yeni run'ın `run_inputs` satırını oku (SQLite: `SELECT parameters FROM tracon_run_inputs ...`).
4. Uygulamayı `Tracon__RunRecording__RecordRunInput=false` ile yeniden başlat,
   yeni bir run çalıştır ve onu resume et.

**Beklenen sonuç**
- Adım 2: akış `run` ile açılır, `done` ile kapanır; `error` yoktur. Yeniden
  koşan adımlar `formal` tonunu taşır.
- Adım 3: `parameters` sütunu doludur ve `{"$apEnc":1,...}` ile başlar
  (içerik koruması açıkken): düz metinde `nova` ya da `formal` yoktur.
- Adım 4: akış `error` çerçevesiyle kapanır; mesaj `did not record the values` ve
  `RecordRunInput` adını içerir. Yeni `runs` satırı açılmaz.

---

### MT-WF-130 — Fonksiyon düğümü run'ın değerlerini `GetRunParameters()` ile okur (Faz 194, F-294)

Mutlu yol. MAF `IWorkflowContext`'e başlangıç durumu verilemez; değerler run
kapsamından gelir. Otomatik karşılığı: `WorkflowFunctionParameterTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 194 |
| **İlgili karar** | K-897 |

**Ön koşul**
- Örnek uygulama Echo kipinde; `narrate` fonksiyonu kayıtlı.

**Adımlar**
1. `PUT /api/workflows/podcast-narrated`: `nodes` `[{"name":"podcast-writer","kind":"Agent"},{"name":"narrate","kind":"Function"}]`,
   `parameters` MT-WF-127'deki gibi.
2. `{"message":"Episode 15","parameters":{"tone":"calm","voice":"shimmer"}}` ile çalıştır.

**Beklenen sonuç**
- Çıktı `[shimmer] Echo [instructions: Write the script in a calm tone.]: Episode 15`
  olur: düğüm `voice`'u okur, agent `tone`'u bağlar.

---

### MT-WF-131 — Konsol: workflow detayında değer alanları görünür, editör kaydı `parameters`'ı korur (Faz 194, F-294) · 👤 görsel

Konsol. Otomatik karşılığı: `workflow-detail.test.tsx` ·
`full-replace-editors.test.tsx` (yapısal yarı); görünüm 👤 insan gerekir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 194 |
| **İlgili karar** | K-897 |

**Ön koşul**
- MT-WF-127'deki `podcast` workflow'u.

**Adımlar**
1. Konsolda `Workflows → podcast` detayını aç.
2. `tone` alanı boşken Run düğmesine bak. Sonra `tone`'a `formal` yaz ve çalıştır.
3. Editörü aç, hiçbir şeyi değiştirmeden kaydet. `GET /api/workflows/podcast` ile karşılaştır.

**Beklenen sonuç**
- Detay ekranında `tone` (zorunlu) ve `voice` alanları görünür; `tone` boşken
  Run kapalıdır. Ekran başlığı playground'daki alan bölümüyle aynı görünümdedir.
- Çalıştırma `parameters` gövdesiyle gider; çıktı MT-WF-127'deki gibi olur.
- Editör kaydından sonra `parameters` dizisi aynen durur.

---

### MT-WF-132 — Konsolda ayarlı fonksiyon düğümlü zincir kurulur, kaydedilir ve çalışır (Faz 195, F-292, F-295) · 👤 görsel

Mutlu yol, baştan sona. Otomatik karşılığı: `WorkflowTests.A_chain_built_in_the_console_with_function_settings_is_saved_and_runs`
(E2E) · `workflow-editor.test.tsx`; görünüm 👤 insan gerekir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 195 |
| **İlgili karar** | K-900, K-901 |

**Ön koşul**
- Örnek uygulama **Echo** kipinde (`Tracon__Providers__OpenAI__ApiKey=` boş);
  `speak` fonksiyonu (ayarlı) ve `summarizer` agent'ı kayıtlı.

**Adımlar**
1. Konsolda `Workflows → New workflow`. Ad `narrated`; desen `Sequential`.
2. **Add an agent…** → `summarizer`. **Add a function…** → `speak`.
3. `speak` satırının altındaki **Settings of speak** alanına
   `{"voice":"alloy","loudnessLufs":-14}` yaz. Alanın altındaki **Fields** satırını oku.
4. **Save**. Açılan detay ekranında mesaja `Hello` yaz ve **Run**'a bas.
5. `GET $APU/api/workflows/narrated` oku.

**Beklenen sonuç**
- 3. adımda **Fields** satırı `voice (string, required); loudnessLufs (number);
  apiKeyConfigurationKey (string?)` der. Alanın ipucu "Never put a secret here" der.
- Kayıt: `agentNames` boş, `nodes` iki düğüm; ikincinin `settings`'i yazılan nesnedir,
  birincinin `settings`'i `null`'dır.
- Run `Completed` olur; çıktı `Echo: Hello \n\n[spoken by alloy at -14 LUFS]`.
- Ölçüldü (2026-10-12, örnek uygulama, bellek içi, `support` agent'ı): konsolda (gömülü tarayıcı) `console-narrated`
  kuruldu; ayar `{"voice":"alloy","loudnessLufs":-12}` ile kaydedilince `nodes` iki düğüm, `version` 1; detay
  ekranındaki Run `Echo: Hello from the console [spoken by alloy at -12 LUFS]` verdi. Aynı çıktı HTTP ile de
  alındı (`-14 LUFS`). Konsol akışı E2E ile de kanıtlıdır.

---

### MT-WF-133 — Ayarda bilinmeyen alan, eksik zorunlu alan veya nesne olmayan değer kayıtta `400` olur ve mesaj düğümü ile alanı adlandırır (Faz 195, F-295)

Hata yolları. Otomatik karşılığı: `WorkflowFunctionSettingsValidationTests` ·
`WorkflowTests.A_misspelled_settings_member_is_refused_and_the_servers_answer_is_shown`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 195 |
| **İlgili karar** | K-900 |

**Ön koşul**
- MT-WF-132'nin ön koşulu.

**Girilecek veri**
```bash
put() { curl -s -w "\n[HTTP %{http_code}]\n" -X PUT "$APU/api/workflows/narrated-bad" -H "$APB" \
        -H 'Content-Type: application/json' -d "$1"; }
put '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"speak","kind":"Function","settings":{"voise":"alloy"}}]}'
put '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"speak","kind":"Function"}]}'
put '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"speak","kind":"Function","settings":[1,2]}]}'
put '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent","settings":{"x":1}},{"name":"speak","kind":"Function","settings":{"voice":"a"}}]}'
put '{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"},{"name":"word-count","kind":"Function","settings":{"x":1}}]}'
```

**Adımlar**
1. Beş çağrıyı sırayla koş; her yanıtın `detail` alanını oku.
2. `GET $APU/api/workflows/narrated-bad` oku.
3. Konsolda aynı `{"voise":"alloy"}` metnini `speak`'in Settings alanına yazıp **Save**'e bas.

**Beklenen sonuç**
- Beşi de `400`, `title` `Workflow definition invalid`. `detail` sırasıyla:
  `Function node 'speak' has invalid settings: The JSON property 'voise' could not be mapped to any .NET member … Path: $.voise` ·
  `… missing required properties including: 'voice'.` ·
  `Node 'speak' in workflow 'narrated-bad' has 'settings' that are a JSON array. Settings must be a JSON object …` ·
  `Node 'summarizer' … is an agent node but carries 'settings' …` ·
  `Function node 'word-count' carries settings, but the function declares none …`.
- Hiçbir mesaj girilen **değeri** tekrarlamaz; yalnız alan adı ve yol görünür.
- `GET` `404` döner: reddedilen tanım saklanmaz.
- Konsolda sunucunun aynı metni formun üstünde görünür ve düzenleyici açık kalır.

---

### MT-WF-134 — Konsol zinciri tek kanonik biçimde yazar: son fonksiyon silinince `agentNames`, ilk fonksiyon eklenince `nodes` (Faz 195, F-292) · 👤 görsel

Otomatik karşılığı: `workflow-editor.test.tsx` ("the stored form") · `workflow-steps.test.ts`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 195 |
| **İlgili karar** | K-901 |

**Ön koşul**
- `narrated` MT-WF-132'deki gibi kayıtlı.

**Adımlar**
1. Konsolda `narrated → Edit`. `speak` satırındaki ✕'e bas. Notlara bak.
2. **Save**. `GET $APU/api/workflows/narrated` oku.
3. Yeniden `Edit`. **Add a function…** → `speak`; ayarı yaz; **Save**. `GET` oku.
4. API ile yalnız agent'lı bir `nodes` listesi yaz
   (`{"kind":"Sequential","nodes":[{"name":"summarizer","kind":"Agent"}]}`), konsolda aç ve **Save**'e bas. `GET` oku.

**Beklenen sonuç**
- 1. adımda iki not görünür: biçim notu ("saved as a plain agent list …") ve yapı uyarısı
  ("… cannot be resumed after you save …").
- 2. adımda yanıt `agentNames: ["summarizer"]`, `nodes: []` taşır.
- 3. adımda yanıt `nodes` iki düğüm, `agentNames: []` taşır; biçim notu "node list" der.
- 4. adımda kayıt `agentNames: ["summarizer"]`, `nodes: []` olur (kanonik biçim).
- Sunucu iki biçimi de kabul eder: 4. adımın API çağrısı `200` döner.
- Ölçüldü (2026-10-12, örnek uygulama, konsol, `support` agent'ı): 1. ve 2. adım — `speak` silinince iki not göründü,
  Save sonrası yanıt `agentNames: ["support"]`, `nodes: []`, `version: 2`; desen seçici yeniden açıldı.
  3. ve 4. adım otomatik testle kanıtlıdır.

---

### MT-WF-135 — Fonksiyonlu zincirde desen kilitlidir; fonksiyon seçici yalnız `Sequential`'da görünür (Faz 195, F-292) · 👤 görsel

Otomatik karşılığı: `workflow-editor.test.tsx` ("the pattern").

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 195 |
| **İlgili karar** | K-901 |

**Ön koşul**
- `narrated` MT-WF-132'deki gibi; ayrıca yalnız agent'lı bir `Sequential` workflow (`review`).

**Adımlar**
1. `narrated → Edit`. **Kind** seçicisine tıklamayı dene; nedenini okuyan satıra bak.
2. `review → Edit`. **Add a function…** seçicisini gör. **Kind**'ı `Concurrent` yap.

**Beklenen sonuç**
- 1. adımda **Kind** devre dışıdır ve "Function nodes run only in a Sequential chain." der.
- 2. adımda seçici `Sequential`'da görünür; `Concurrent`'te kaybolur ve adımlar yerinde kalır.

---

### MT-WF-136 — Ayar değişikliği sonraki run'da geçerlidir; eski checkpoint'ten resume reddedilmez, yapı değişikliği reddedilir (Faz 195, F-295)

Otomatik karşılığı: `WorkflowFunctionSettingsResumeTests` ·
`WorkflowFunctionSettingsConcurrencyTests` · `WorkflowStructuralChangeResumeTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 195 |
| **İlgili karar** | K-900 |

**Ön koşul**
- `narrated` `{"voice":"alloy","loudnessLufs":-14}` ayarıyla kayıtlı ve bir kez koşmuş (`$RID`);
  `GET $APU/api/workflows/runs/$RID/checkpoints` iki kayıt döner, ilki (`parentCheckpointId` `null`)
  fonksiyondan ÖNCEDİR (`$CP0`).

**Adımlar**
1. `PUT` ile `speak` ayarını `{"voice":"nova","loudnessLufs":-20}` yap. Yeni run başlat.
2. `POST $APU/api/workflows/runs/$RID/resume` gövde `{"checkpointId":"$CP0"}`.
3. `PUT` ile zinciri yalnız agent'a indir (`{"kind":"Sequential","agentNames":["summarizer"]}`) ve 2. adımı tekrarla.

**Beklenen sonuç**
- 1. adım: run `Completed`, çıktı `Echo: Hello \n\n[spoken by nova at -20 LUFS]`.
- 2. adım: resume `Completed`; fonksiyon YENİ ayarla yeniden koşar (aynı çıktı). Ayar executor kimliğinin
  parçası değildir, bu yüzden eski checkpoint uyumludur.
- 3. adım: `RunFailed`; mesaj `Workflow 'narrated' cannot be resumed from this checkpoint: the graph's
  structure differs from when the checkpoint was written …` der. Konsolun yapı uyarısı bunu önceden söyler.
- Ölçüldü (2026-10-12, örnek uygulama, bellek içi): üç adımın çıktısı yukarıdaki gibi alındı.

---

### MT-WF-137 — `GET /api/workflows/functions` ayarlı fonksiyon için `settingsSchema` taşır, ayarsız için `null` (Faz 195, F-295)

Otomatik karşılığı: `WorkflowFunctionCatalogSchemaTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 195 |
| **İlgili karar** | K-900 |

**Ön koşul**
- Örnek uygulama; `speak` (ayarlı), `word-count` ve `narrate` (ayarsız) kayıtlı.

**Girilecek veri**
```bash
curl -s "$APU/api/workflows/functions" -H "$APB"
```

**Beklenen sonuç**
- `speak` için `settingsSchema` bir nesne şemasıdır: `properties` altında `voice` (`string`),
  `loudnessLufs` (`number`), `apiKeyConfigurationKey` (`["string","null"]`); `required` `["voice"]`;
  `additionalProperties` `false`.
- `word-count` ve `narrate` için `settingsSchema` `null`'dır.
- Şema yalnız gösterim içindir: kayıt, şemaya değil ayar tipine okunarak denetlenir (MT-WF-133).

---

### MT-WF-138 — Porta uymayan yanıt `400` alır ve run açmaz (kusur-giderme, 2026-10-12, F-341)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-908 |
| **Devir** | ➜ CI: `WorkflowRespondValidationTests.A_yes_no_port_answered_without_approved_is_refused_with_400` · `WorkflowNodeTypeChainTests.A_node_that_cannot_take_the_previous_nodes_output_is_refused_at_save_naming_both` |

Regresyon. Yanıt yalnız sürdürülen run içinde çevriliyordu: uç `200` dönüyor,
yeni run `Failed` kapanıyordu. Uyumsuz zincir de kayıttan geçiyor, run çıktısız
`Completed` bitiyordu.

**Ön koşul** — Örnek uygulama; `summarize-and-approve` kodda kayıtlı (port `bool` bekler).

**Adımlar**
1. `POST $APU/api/workflows/summarize-and-approve/run` ile başlat; run `AwaitingInput` olsun.
2. `GET $APU/api/workflows/runs/{runId}/requests` ile `requestId`'yi al (`form: Boolean`).
3. `POST $APU/api/workflows/runs/{runId}/respond` gövdesi `{"requestId":"<id>","text":"yes"}`; sonra `GET $APU/api/runs` ile run sayısını oku.
4. Aynı uca `{"requestId":"<id>","approved":true}` gönder.

**Beklenen sonuç**
- Adım 3: `400 application/problem+json`, `title: "Response invalid"`; detay portu ve beklenen alanı adlandırır. Run sayısı değişmez.
- Adım 4: `200` SSE; `RunCompleted`, çıktı "Summary published.".
- 🚨 Eski kusur: adım 3 `200` döndü ve yeni bir `Failed` run açıldı.

---

### MT-WF-139 — `Prefer: respond-async` ile workflow run'ı kuyruğa girer, `202` döner ve worker'da tamamlanır (Faz 197, F-318)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 197 |
| **İlgili karar** | — |
| **Devir** | ➜ CI: `WorkflowAsyncRunTests.A_queued_run_answers_202_and_completes_in_the_worker` · `WorkflowAsyncRunTests.The_queued_row_names_the_workflow_session_and_caller_and_keeps_them_when_the_run_starts` |

Workflow run'ı okuyan HTTP akışıyla yaşıyordu: okuyucu düşünce run `Canceled` oluyordu.
Kuyruklu run bağlantıya bağlı değildir.

**Ön koşul** — Örnek uygulama, kuyruk açık (varsayılan); `episode-quick` kayıtlı:
`PUT $APU/api/workflows/episode-quick` gövdesi
`{"kind":"Sequential","nodes":[{"name":"word-count","kind":"Function"},{"name":"render","kind":"Function","settings":{"seconds":2}}]}`.

**Adımlar**
1. `POST $APU/api/workflows/episode-quick/run`, başlık `Prefer: respond-async`, gövde `{"message":"Episode seven"}`.
2. Hemen `GET $APU/api/runs/{runId}`.
3. 5 sn sonra aynı isteği tekrarla; `GET $APU/api/jobs?handlerKey=tracon.workflow-run` oku.

**Beklenen sonuç**
- Adım 1: `202 Accepted`; `Location: /tracon/api/runs/{runId}`, `Preference-Applied: respond-async`; gövdede `runId` = `jobId`, `eventsLocation` = `.../events`.
- Adım 2: `status: Queued` (ya da worker hızlıysa `Running`), `kind: Workflow`, `workflowName: episode-quick`, boş olmayan `sessionId`.
- Adım 3: run `Completed`; iş `Completed`, `attempt: 1`.

---

### MT-WF-140 — Aynı `Idempotency-Key` aynı run'ı döndürür; `Prefer`'siz anahtar `400` alır (Faz 197, F-318)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 197 |
| **İlgili karar** | — |
| **Devir** | ➜ CI: `WorkflowAsyncIdempotencyTests.The_same_key_sent_twice_returns_the_same_run_and_queues_one_job` · `…Concurrent_requests_with_one_key_open_exactly_one_job` · `…A_key_without_respond_async_is_refused_with_400_on_all_three_endpoints_and_nothing_runs` |

Akış saklanıp yeniden oynatılamaz; anahtar yalnız kuyruklu `202` ile anlamlıdır.

**Ön koşul** — MT-WF-139'un ön koşulu.

**Adımlar**
1. `POST $APU/api/workflows/episode-quick/run`, başlıklar `Prefer: respond-async` ve `Idempotency-Key: episode-7`, gövde `{"message":"Episode seven"}`.
2. Aynı isteği aynı anahtar ve gövdeyle tekrarla.
3. `POST $APU/api/workflows/episode-quick/run`, yalnız `Idempotency-Key: episode-8` (Prefer yok).

**Beklenen sonuç**
- Adım 1 ve 2: ikisi de `202`; aynı `runId`; ikinci yanıtta `Idempotency-Replayed: true`. Yalnız bir iş açılır.
- Adım 3: `400`, `title: "Idempotency-Key requires Prefer: respond-async"`; run ve iş açılmaz.

---

### MT-WF-141 — Kuyruklu yanıt `202` alır; yanlış `requestId` ve porta uymayan yanıt `400` alır (Faz 197, F-318)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 197 |
| **İlgili karar** | K-908 |
| **Devir** | ➜ CI: `WorkflowAsyncRespondTests` (beş case) |

Makine yanıtı deseni: render bitince host bekleyen isteği yanıtlar, bağlantı tutmaz.

**Ön koşul** — Örnek uygulama; `summarize-and-approve` kodda kayıtlı (port `bool` bekler).

**Adımlar**
1. `POST $APU/api/workflows/summarize-and-approve/run`, `Prefer: respond-async`, gövde `{"message":"Tracon is a control plane."}`; run `AwaitingInput` olana kadar `GET $APU/api/runs/{runId}`.
2. `GET $APU/api/workflows/runs/{runId}/requests` ile `requestId`'yi al.
3. `POST $APU/api/workflows/runs/{runId}/respond`, `Prefer: respond-async`, gövde `{"requestId":"no-such-request","approved":true}`.
4. Aynı uca gövde `{"requestId":"<id>","text":"yes"}`.
5. Aynı uca gövde `{"requestId":"<id>","approved":true}`.

**Beklenen sonuç**
- Adım 3: `400`, `title: "Response invalid"`, detay `no-such-request`'i adlandırır.
- Adım 4: `400`; detay portu ve `approved` alanını adlandırır.
- Adım 3 ve 4 iş açmaz.
- Adım 5: `202`; yeni run `Completed`, çıktı `Summary published.`; yanıtlanan run `AwaitingInput` kalır.

---

### MT-WF-142 — Kayıtlı zaman aşımını aşan fonksiyon run'ı `Failed` + `workflow_function_timeout` ile düşürür (Faz 196, F-303)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 196 |
| **İlgili karar** | K-497, K-805 |
| **Devir** | ➜ CI: `WorkflowFunctionTimeoutTests` |

**Ön koşul** — Örnek uygulama; `slow` kodda 2 sn zaman aşımıyla kayıtlı (gövde 10 sn, token'ı dinler).
`PUT $APU/api/workflows/bounded` gövdesi `{"kind":"Sequential","nodes":[{"name":"word-count","kind":"Function"},{"name":"slow","kind":"Function"}]}`.

**Adımlar**
1. `POST $APU/api/workflows/bounded/run`, gövde `{"message":"hello"}`; süreyi ölç.
2. `GET $APU/api/runs/{runId}`.
3. `GET $APU/api/workflows/functions`.

**Beklenen sonuç**
- Adım 1: akış ~2 sn'de kapanır, 10 sn değil.
- Adım 2: `status: Failed`; `error.type: workflow_function_timeout`; `error.class: Timeout`; mesaj `'slow'` ve `2 s` adlandırır.
- Adım 3: `slow` ve `deaf` için `timeout: "00:00:02"`; diğerleri `null`.

---

### MT-WF-143 — Düğümün kendi `timeout` değeri kaydı ezer; aralık dışı değer `400` alır (Faz 196, F-303, F-405)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 196 |
| **İlgili karar** | — |
| **Devir** | ➜ CI: `WorkflowFunctionTimeoutTests` · `WorkflowTimeoutSaveTests` |

**Ön koşul** — MT-WF-142.

**Adımlar**
1. `bounded`'ı `slow` düğümünde `"timeout":"00:00:05"` ile yeniden `PUT` et ve koş.
2. `PUT $APU/api/workflows/bad` gövdesi `{"kind":"Sequential","nodes":[{"name":"slow","kind":"Function","timeout":"00:00:00"}]}`.
3. `PUT $APU/api/workflows/agentnode` gövdesi `{"kind":"Sequential","nodes":[{"name":"support","kind":"Agent","timeout":"00:00:05"},{"name":"word-count","kind":"Function"}]}`.

**Beklenen sonuç**
- Adım 1: run ~5 sn'de `Failed`; mesajda `5 s`.
- Adım 2: `400`; detay `'slow'`, `'timeout'` ve aralığı adlandırır.
- Adım 3: `200` (agent düğümü de süre taşır, F-405).

---

### MT-WF-144 — Konsolda adım süresi ve run süresi kaydedilir, geri açılır ve boşaltılınca silinir (Faz 196) · 👤

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 196 |
| **İlgili karar** | K-725, K-901, K-918 |
| **Devir** | ➜ CI: `workflow-editor.test.tsx` · `full-replace-editors.test.tsx` · E2E `WorkflowTests` |

**Ön koşul** — Konsol; `bounded` kayıtlı (MT-WF-142).

**Adımlar**
1. Workflows > `bounded` > Edit; `slow` adımının süre alanına `5`, Run timeout alanına `300` yaz; kaydet; yeniden aç.
2. İki alanı boşalt; kaydet; yeniden aç.
3. Workflows > New; Sequential; yalnız `support` agent'ını ekle.
4. `word-count`'u ekle; `support` süre alanına `30` yaz; `word-count`'u kaldır; sonra `support` süresini sil.

**Beklenen sonuç**
- Adım 1: alanlar `5` ve `300` gösterir; `GET` `timeout: "00:00:05"`, `runTimeout: "00:05:00"` döner. Boş `slow` alanının yer tutucusu `2 (registered)`'dır.
- Adım 2: alanlar boş; `GET` iki değeri `null` döner.
- Adım 3: agent adımında süre alanı yok: zincir agent listesi olarak kaydedilir; süre biçimi değiştirmez (K-901).
- Adım 4: fonksiyon eklenince alan çıkar. Fonksiyon kalkınca uyarı (`workflow-timeouts-notice`) çıkar ve Save kapanır; süre silinince ikisi de kalkar.

---

### MT-WF-145 — Token'ı dinlemeyen fonksiyon run'ı tutmaz; geç bitişi log'a düşer (Faz 196, F-303)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | 196 |
| **İlgili karar** | K-805 |
| **Devir** | ➜ CI: `WorkflowFunctionTimeoutLateCompletionTests` |

**Ön koşul** — `deaf` kodda 2 sn zaman aşımıyla kayıtlı (gövde 6 sn, token'ı dinlemez). `PUT $APU/api/workflows/deafchain` gövdesi `{"kind":"Sequential","nodes":[{"name":"deaf","kind":"Function"}]}`.

**Adımlar**
1. `POST $APU/api/workflows/deafchain/run`; süreyi ölç.
2. 6 sn bekle; uygulama günlüğünü oku.

**Beklenen sonuç**
- Adım 1: ~2 sn'de `Failed`, `workflow_function_timeout`.
- Adım 2: `Warning`: `Function node deaf of workflow run {runId} returned 00:00:06… past its timeout of 00:00:02; its result is discarded`. Run satırı değişmez.

---

### MT-WF-146 — Agent düğümünün `timeout` değeri `ChildDeadline`'ı o adım için ezer (Faz 196, F-405)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 196 |
| **İlgili karar** | K-887 |
| **Devir** | ➜ CI: `WorkflowAgentNodeTimeoutTests` · `WorkflowAgentNodeTimeoutResumeTests` |

**Ön koşul** — Örnek uygulama `Tracon__Demo__SlowModel__Seconds=20` ile (sağlayıcı `slow`, 20 sn'de cevap verir). `PUT $APU/api/agents/slow-writer` gövdesi `{"instructions":"Write the episode.","model":{"provider":"slow","model":"slow-1"}}`. `podcast`: `slow-writer` agent düğümü `"timeout":"00:00:05"` + `word-count`; `podcast-plain`: aynısı, süresiz.

**Adımlar**
1. `POST $APU/api/workflows/podcast/run`; süreyi ölç.
2. `POST $APU/api/workflows/podcast-plain/run`.

**Beklenen sonuç**
- Adım 1: ~5 sn'de `Failed`; `error.type: child_run_timeout`, `class: Timeout`; mesaj `limit: 00:00:05, set by the 'timeout' of node 'slow-writer'` der.
- Adım 2: ~20 sn'de `Completed` (kurulum `ChildDeadline` 2 dk).

---

### MT-WF-147 — Tanımın `runTimeout` değeri kurulum değerini yalnız kısaltır; tavan açılınca uzatır (Faz 196, F-405)

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 196 |
| **İlgili karar** | K-844 |
| **Devir** | ➜ CI: `WorkflowTimeoutSaveTests` · `WorkflowDefinitionRunTimeoutTests` |

**Ön koşul** — Örnek uygulama varsayılan ayarlarla (`RunTimeout` 10 dk, `MaxRunTimeout` yok).

**Adımlar**
1. `PUT $APU/api/workflows/longrun` gövdesi `{"kind":"Sequential","nodes":[{"name":"word-count","kind":"Function"}],"runTimeout":"00:30:00"}`.
2. Uygulamayı `--Tracon:Workflows:RunTimeout=00:00:10 --Tracon:Workflows:MaxRunTimeout=00:01:00` ile başlat. `longchain` (`word-count` + `render` `{"seconds":15}`) için `runTimeout` `00:02:00`, sonra `00:00:30` ile `PUT` et ve koş.
3. Aynı zinciri `runTimeout`'suz (`shortchain`) ve `"runTimeout":"00:00:05"` ile (`shortened`) koş.

**Beklenen sonuç**
- Adım 1: `400`; detay `Tracon:Workflows:MaxRunTimeout`'u adlandırır.
- Adım 2: `00:02:00` → `400` (`above the host's ceiling … (00:01:00)`); `00:00:30` → `200`, run ~15 sn'de `Completed`.
- Adım 3: `shortchain` ~10 sn'de `Canceled`, `TimeoutException`, mesaj `Tracon:Workflows:RunTimeout`; `shortened` ~5 sn'de `Canceled`, mesaj `the run timeout its definition sets`.

---

### MT-WF-148 — `object` alan ilk düğüm reddedilir; izin verilen ilk düğüm resume'da yeniden koşmaz (kusur-giderme, 2026-10-14, F-398)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-975 |
| **Devir** | ➜ CI: `WorkflowEntryFunctionRunsOnceTests` · `WorkflowNodeTypeRuleTests.Only_object_takes_the_turn_token_so_the_first_node_rule_names_object_alone` |

Regresyon. Runner ilk düğüme mesajdan sonra `TurnToken` da gönderir; `object`
alan ilk düğüm ikisini de aldı ve iki kez koştu.

**Ön koşul** — Örnek uygulamaya `AddWorkflowFunction<object, List<ChatMessage>>("loose", …)` ekle; işleyici her çağrıda bir log satırı yazsın.

**Adımlar**
1. `PUT $APU/api/workflows/loose-start` gövdesi `{"kind":"Sequential","nodes":[{"name":"loose","kind":"Function"},{"name":"<agent>","kind":"Agent"}]}`.
2. Aynı zinciri ilk düğümü `IEnumerable<ChatMessage>` alan bir fonksiyonla kaydet ve koş; `GET $APU/api/workflows/runs/{runId}/checkpoints` ile ilk checkpoint'i al ve `POST …/resume` gövdesi `{"checkpointId":"<ilk>"}` ile sürdür.

**Beklenen sonuç**
- Adım 1: `400`; detay `'loose'`, `turn token` ve `run twice` der.
- Adım 2: ilk düğümün log satırı başlangıçta bir kez yazılır, resume'dan sonra artmaz; resume run'ı `Completed`.
- 🚨 Eski kusur: adım 1 `200` döndü ve her run ilk düğümü iki kez koştu.

---

### MT-WF-149 — Zarif kapanış, checkpoint temizliği kapalıyken de devamı baştan koşturmaz (kusur-giderme, 2026-10-14, F-433)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | — (faz dışı kusur giderme) |
| **İlgili karar** | K-978 |
| **Devir** | ➜ CI: `WorkflowRecoveryTests.A_run_stopped_by_a_graceful_shutdown_is_continued_by_the_next_worker(false)` |

Regresyon. `KeepCheckpointsAfterCompletion=false` iken zarif kapanışın `Canceled`
kapattığı run checkpoint'ini siliyordu; devam ilk adımı yeniden koşturuyordu.

**Ön koşul** — Örnek uygulama SQLite ile; `Tracon__Workflows__ResumeInterruptedRuns=true`, `Tracon__Workflows__KeepCheckpointsAfterCompletion=false`; iki adımlı, ikinci adımı ~30 sn süren bir workflow.

**Adımlar**
1. Workflow'u `Prefer: respond-async` ile kuyruğa koy; ikinci adım başlayınca uygulamayı Ctrl+C ile durdur.
2. Uygulamayı yeniden başlat; işin yeniden kiralanmasını bekle (`LeaseDuration`).
3. `GET $APU/api/runs?kind=Workflow` ile zinciri oku; birinci adımın log satırlarını say.

**Beklenen sonuç**
- Durdurulan run `Canceled`; devam run'ı `ContinuedFromRunId` ile ona bağlı ve `Completed`.
- Birinci adım bir kez koştu; devam bittikten sonra oturumun checkpoint'leri silinmiş.
- 🚨 Eski kusur: birinci adım iki kez koştu.


### MT-WF-150 — Sürüm geçmişi her kaydı tutar; iki sürümün farkı okunur (Faz 200, F-300) · 👤

Konsol karşılığı 👤 görseldir (Sürümler bölümü, iki sürümü işaretleyince fark tablosu); HTTP
karşılığı `curl` ile koşulur. Otomatik karşılığı: `WorkflowVersionEndpointTests` ·
`WorkflowDefinitionStoreContract` · `workflow-detail.test.tsx`.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 200 |
| **İlgili karar** | K-987 |

**Ön koşul** — Örnek uygulama SQLite ile ayakta; `word-count` fonksiyonu kayıtlı; `scribe` adlı ve `proof` adlı iki stored agent (Echo sağlayıcı, `echo-instructions-1` modeli).

**Adımlar**
1. `PUT $APU/api/workflows/pinned-demo` ile `scribe` + `word-count` zincirini kaydet; sonra `proof` adımını ekleyip yeniden kaydet.
2. `GET $APU/api/workflows/pinned-demo/versions` ve `GET .../versions/1/diff/2`.
3. Konsolda workflow'u aç → **Sürümler**; 1 ve 2'yi işaretle.

**Beklenen sonuç**
- Liste `[2, 1]` sırasında iki tam anlık görüntü verir (sürüm 2 üç düğüm, sürüm 1 iki düğüm).
- Fark `left` = sürüm 1 (iki düğüm), `right` = sürüm 2 (üç düğüm); sunucu fark hesaplamaz.
- Konsol eklenen `proof` adımını gösterir.

### MT-WF-151 — Geri alma eski içeriği YENİ sürüm yazar; geçersiz içerik yazılmaz (Faz 200, F-300)

Otomatik karşılığı: `WorkflowVersionEndpointTests` · `WorkflowRollbackTests` ·
`AuditingWorkflowDefinitionStoreTests`.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 200 |
| **İlgili karar** | K-987 |

**Ön koşul** — MT-WF-150'deki `pinned-demo` (sürüm 1 ve 2).

**Adımlar**
1. `POST $APU/api/workflows/pinned-demo/rollback {"version":1}`.
2. `GET .../versions`.
3. `POST .../rollback {"version":42}`.
4. `GET $APU/api/audit?action=workflow.rollback`.

**Beklenen sonuç**
- 1. adım `200`, `version: 3`, düğümler sürüm 1'inkiyle aynı (`scribe`, `word-count`).
- 2. adım `[3, 2, 1]` verir; sürüm 2 hâlâ okunur.
- 3. adım `404`: `Workflow 'pinned-demo' has no version 42.`; hiçbir şey yazılmaz.
- 4. adım tek kayıt verir: `workflow:pinned-demo`, `{"rolledBackToVersion":1,"newVersion":3}`.

### MT-WF-152 — Bekleyen run yapısal düzenlemeden sonra kaydettiği sürümle sürer; düzenlenen sürüm adı konunca checkpoint uymaz (Faz 200, F-300)

Otomatik karşılığı: `WorkflowStructuralChangeResumeTests` · `WorkflowResumePinTests` ·
`WorkflowFunctionSettingsResumeTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 200 |
| **İlgili karar** | K-988 |

**Ön koşul** — `pinned-demo` sürüm 1 (iki düğüm: `scribe` sabitli, `word-count`) ile koşturulmuş ve tamamlanmış bir run (`$RID`, ilk checkpoint'i `$CP`); sonra `proof` adımı eklenerek sürüm 2 kaydedilmiş (MT-WF-150 sırası, run'ı 1. ve 2. kayıt arasında koş).

**Adımlar**
1. `POST $APU/api/workflows/runs/$RID/resume {"checkpointId":"$CP"}`.
2. Yeni run'ı oku: `GET $APU/api/runs/<yeni>`.
3. `POST .../resume {"checkpointId":"$CP","definitionVersion":2}`.

**Beklenen sonuç**
- 1. adım `200`, run `Completed`; satırın `agentVersion` değeri `$RID`'ninkiyle (1) aynı: düzenleme bekleyen run'ı etkilemedi.
- 3. adım `200` ve akış `RunFailed` ile biter: `the graph's structure differs from when the checkpoint was written`.

### MT-WF-153 — Olmayan sürümle resume akış açılmadan `400` olur; güncel tanıma düşmez (Faz 200, F-300)

Sınır senaryosu. Otomatik karşılığı: `WorkflowResumeVersionHttpTests` ·
`WorkflowResumePinTests`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 200 |
| **İlgili karar** | K-988 |

**Ön koşul** — MT-WF-152'deki `$RID` ve `$CP`.

**Adımlar**
1. `POST .../resume {"checkpointId":"$CP","definitionVersion":9}`.
2. `POST .../resume {"checkpointId":"$CP","definitionVersion":0}`.
3. `GET $APU/api/runs?kind=Workflow` ile satır sayısını önce ve sonra karşılaştır.

**Beklenen sonuç**
- 1. adım `400`, başlık `Definition version not found`, ayrıntı `Workflow 'pinned-demo' has no version 9`.
- 2. adım `400`, başlık `Definition version invalid`.
- Hiçbir istek yeni run satırı açmaz.

### MT-WF-154 — Sabitli adım ajan düzenlense de sabit sürümü koşar; sabit kayıtta doğrulanır (Faz 200, F-300)

Otomatik karşılığı: `WorkflowAgentVersionPinTests` · `WorkflowNodeAgentVersionTests` ·
`AgentVersionPinTests`.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Yüksek |
| **İlgili faz** | 200 |
| **İlgili karar** | K-989 |

**Ön koşul** — `scribe` iki sürümlü (1: `SCRIBE-ALPHA: write one line.`, 2: `SCRIBE-BETA: write two lines.`); `proof` stored agent; `podcast-writer` kodda tanımlı.

**Adımlar**
1. `PUT .../workflows/pinned-demo` ile `scribe` düğümüne `"agentVersion":9`, sonra `0`, sonra `word-count` fonksiyon düğümüne `"agentVersion":1`, sonra kodda tanımlı `podcast-writer`'a `"agentVersion":1` yaz.
2. `scribe` düğümüne `"agentVersion":1` yazıp kaydet ve workflow'u koş.
3. `GET $APU/api/runs/<run>/tree` ile satırları oku.

**Beklenen sonuç**
- 1. adım dört `400`: sürüm yok · `of 0` · function node · `defined in code and keeps no version history`; hiçbiri yazılmaz.
- 2. adım: çıktı `Echo [instructions: SCRIBE-ALPHA: write one line.]: hello` ile başlar (BETA yok).
- 3. adım: kök satır `Workflow pinned-demo v1`, çocuk satır `Agent scribe v1`.

### MT-WF-155 — Silinip yeniden oluşturulan workflow'un ilk sürümü eskinin devamıdır (Faz 200, F-300)

Otomatik karşılığı: `WorkflowDefinitionStoreContract.Deleting_keeps_the_history_and_the_numbering_continues_after_it` ·
`WorkflowVersionEndpointTests.A_deleted_workflow_serves_no_history_but_the_numbering_continues_when_it_is_saved_again`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 200 |
| **İlgili karar** | K-987 |

**Ön koşul** — MT-WF-151 sonrası `pinned-demo`, sürüm 3.

**Adımlar**
1. `DELETE $APU/api/workflows/pinned-demo`.
2. `GET .../versions`.
3. `PUT .../workflows/pinned-demo` ile tek adımlı bir zincir kaydet.

**Beklenen sonuç**
- 1. adım `204`; 2. adım `404` (silinmiş workflow geçmişi sunmaz).
- 3. adım `version: 4`; `GET .../versions` artık `[4, 3, 2, 1]` verir.
- Eski bir run'ın damgası olan sürüm 3'ün içeriği değişmemiştir.

### MT-WF-156 — Tek cevaplı workflow'da çift tık tek devam açar (Faz 208, F-319) · 👤

Konsol çift tıkı 👤 görseldir; HTTP yarısı örnek uygulamada ölçüldü. Otomatik karşılığı:
`WorkflowSingleResponseTests.Two_identical_streamed_answers_at_once_start_one_run_and_the_second_follows_it`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 208 |
| **İlgili karar** | K-994, K-997 |

**Ön koşul** — Örnek uygulama; `summarize-and-approve-once` (`SingleAnswer`) run'ı `AwaitingInput`.

**Adımlar**
1. Konsolda kartta onayla düğmesine hızlıca iki kez bas (ya da aynı `respond` gövdesini iki eşzamanlı istekle gönder).
2. `GET $APU/api/workflows/runs/<kaynak>/responses`.
3. `GET $APU/api/runs?limit=20` ile yeni run'ları oku.

**Beklenen sonuç**
- İki yanıt da `200`; ikisinin ilk `run` çerçevesi AYNI devamı adlandırır; izleyen akış `done` ile biter; devam `Completed`.
- 2: tek satır, `claimScope` dolu, `answerHash` 64 karakter.
- 3: iki cevap aynı anda geldiyse ikinci bir run olabilir: `Canceled`, `error.type` `request_already_answered`, sıfır olay.

### MT-WF-157 — Tek cevaplı workflow'da zıt cevap `409` alır (Faz 208, F-319)

Otomatik karşılığı: `WorkflowSingleResponseTests.Two_different_answers_at_once_start_one_run_and_the_other_gets_409_that_names_neither`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 208 |
| **İlgili karar** | K-994 |

**Ön koşul** — MT-WF-156 sonrası kaynak run.

**Adımlar**
1. Aynı `requestId`'ye `{"approved": false}` gönder.

**Beklenen sonuç**
- `409`, başlık `Request already answered`; gövde devam kimliğini ve öbür cevabı taşımaz. Yeni run açılmaz.

### MT-WF-158 — Kuyruklu tekrar ilk cevabın `202`'sini alır (Faz 208, F-319)

Otomatik karşılığı: `WorkflowSingleResponseTests.Two_identical_queued_answers_with_different_keys_get_the_same_202_and_queue_one_job`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 208 |
| **İlgili karar** | K-994 |

**Ön koşul** — Yeni bir `summarize-and-approve-once` run'ı `AwaitingInput`.

**Adımlar**
1. Aynı cevabı `Prefer: respond-async` ve iki farklı `Idempotency-Key` ile iki kez gönder.

**Beklenen sonuç**
- İki `202`; aynı `runId`, aynı `jobId` (run kimliği), aynı `Location`; kuyrukta tek iş; run `Completed`.

### MT-WF-159 — Varsayılan `Branch` modu her cevaba yeni dal açar (Faz 208, F-319)

Otomatik karşılığı: `WorkflowSingleResponseTests.Branch_mode_still_opens_a_branch_for_every_answer`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 208 |
| **İlgili karar** | K-130, K-994 |

**Ön koşul** — `summarize-and-approve` (`Branch`) run'ı `AwaitingInput`.

**Adımlar**
1. Aynı isteğe iki cevap gönder.
2. `GET .../responses` ve `GET $APU/api/audit?action=workflow.respond`.

**Beklenen sonuç**
- İki `200`, iki ayrı devam; 2: iki satır (`claimScope` boş), iki denetim kaydı (`mode: Branch`).

### MT-WF-160 — Düz `/resume` sonrası aynı istek ilk cevabı izler (Faz 208, F-319)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 208 |
| **İlgili karar** | K-994 |
| **Devir** | ➜ CI: `WorkflowSingleResponseTests.A_resumed_source_answered_again_follows_the_first_answer_and_a_different_answer_conflicts` |

**Ön koşul** — `summarize-and-approve-once` cevaplandı; aynı oturumda kaynak `POST .../resume` ile yeniden yayınlandı.

**Adımlar**
1. Yeni run'ın aynı `requestId`'sine önce aynı, sonra zıt cevap ver.

**Beklenen sonuç**
- Aynı cevap ilk devamı izler (yeni run yok); zıt cevap `409`.

### MT-WF-161 — Editörde "One answer per request" kaydedilir ve sürüm farkında görünür (Faz 208, F-319) · 👤

Otomatik karşılığı: `full-replace-editors.test.tsx` · `WorkflowResponseModeVersionTests`.

| | |
|---|---|
| **İzlek** | A |
| **Önem** | Orta |
| **İlgili faz** | 208 |
| **İlgili karar** | K-994 |

**Ön koşul** — Konsol, Admin; kayıtlı bir workflow.

**Adımlar**
1. Editörde **One answer per request**'i işaretle, kaydet.
2. Sürümler bölümünde iki sürümü karşılaştır.
3. Başka bir alanı değiştirip yeniden kaydet.

**Beklenen sonuç**
- `GET $APU/api/workflows/<ad>` `responseMode: "SingleAnswer"`; karşılaştırmada **Answers per request** satırı; 3. adım modu sıfırlamaz.

### MT-WF-162 — Fırlatan atıf getter'ı tek cevaplı isteği kilitlemez (Faz 208, F-319)

Otomatik karşılığı: `WorkflowResponseOwnershipTests.A_throwing_attribution_getter_does_not_lock_a_streamed_request` (ve kuyruklu kardeşi).

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 208 |
| **İlgili karar** | K-997 |

**Ön koşul** — Örnek uygulama `Tracon:Demo:Attribution:Mode = throw` ile; `summarize-and-approve-once` run'ı `AwaitingInput`.

**Adımlar**
1. Cevap ver; aynı cevabı tekrarla; zıt cevap ver.
2. Başka bir run'a `Prefer: respond-async` ile cevap ver.

**Beklenen sonuç**
- 1: ilk cevap `200`, devam `Completed`, `userId` boş; tekrar aynı devamı izler (`done`); zıt cevap `409`.
- 2: `202`, run `Completed`. Günlükte atıf uyarısı; istek kilitlenmez.

### MT-WF-163 — Devam run'ı cevapladığı run'a bağlanır (Faz 208, F-319) · 👤

Run detayındaki bağlantı 👤 görseldir; olay sırası HTTP ile ölçüldü. Otomatik karşılığı:
`WorkflowResponseLinkTests.The_run_an_answer_starts_names_its_source_even_with_payload_recording_off`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | 208 |
| **İlgili karar** | K-995 |

**Ön koşul** — Bir cevabın başlattığı devam run'ı.

**Adımlar**
1. `GET $APU/api/runs/<devam>/events`.
2. Konsolda devamın run detayını aç.

**Beklenen sonuç**
- 1: olaylar `run.started`, `workflow.response-applied`, `workflow.started`… sırasıyla; ikincisi kaynak run'ı ve `requestId`'yi taşır.
- 2: zaman çizelgesinde *answers a request of run …* bağlantısı kaynağa gider.

### MT-WF-164 — İşi yazılmamış kuyruklu cevabı aynı cevabın tekrarı bitirir (Faz 208, F-319, ikinci tur denetim)

Otomatik karşılığı: `WorkflowResponseOwnershipTests.A_queued_answer_whose_job_was_never_written_is_finished_by_the_same_answer` ·
`Two_repeats_that_both_find_the_job_missing_write_it_once`.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | 208 |
| **İlgili karar** | K-997, K-998 |

**Ön koşul** — Örnek uygulama SQLite ile, `Tracon__Scheduling__RunWorker=false`; iki `summarize-and-approve-once` run'ı `AwaitingInput`.

**Adımlar**
1. İki run'a da `Prefer: respond-async` ile cevap ver (`202`, run `Queued`).
2. Uygulamayı durdur; SQLite'ta iki run'ın `tracon_job_items` ve `tracon_jobs` satırını sil (kimlikler büyük harf). Bu, kayıttan sonra ve kuyruğa yazımdan önce duran sürecin bıraktığı durumdur.
3. İşçi açık yeniden başlat. Birinci run'a aynı cevabı kuyruklu, ikincisine akışlı gönder.

**Beklenen sonuç**
- 3: kuyruklu tekrar `202`, aynı `runId`, `jobId` run kimliği; akışlı tekrar `200`, ilk çerçeve aynı run, son çerçeve `done`.
- İki run da `Completed`, 13 olay; her run için tek iş (`Completed`); `…/responses` tek satır (`Delivered`).
- Günlükte `had a recorded answer but no job; a repeat of the answer writes the job` uyarısı.
