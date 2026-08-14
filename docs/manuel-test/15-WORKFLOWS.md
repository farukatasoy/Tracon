# 15 — Workflows: Yürütme, Kontrol Noktası, Graf ve Human-in-the-Loop (`WF`)

> **Alan kodu:** `WF` · **Faz:** 15, 16
> **Kaynak:** `src/AgentPrism.Workflows/` (tümü: `AgentPrismWorkflowOptions`,
> `AgentPrismWorkflowsBuilderExtensions`, `WorkflowAgentBinding`, `Internal/*`) ·
> `src/AgentPrism.Abstractions/Workflows/` (tümü) ·
> `src/AgentPrism.Abstractions/Runs/RunKind.cs` ·
> `src/AgentPrism.Core/Workflows/WorkflowDefinitionValidator.cs` ·
> `src/AgentPrism.Core/Storage/InMemoryWorkflowStores.cs` ·
> `src/AgentPrism.Core/Audit/AuditingWorkflowDefinitionStore.cs` (yalnız denetim
> eylem adları) · `src/AgentPrism.PostgreSql/Migrations/0007_workflows.sql` ·
> `src/AgentPrism.PostgreSql/Stores/PostgresWorkflow*.cs` ·
> `src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs` ·
> `src/AgentPrism.AspNetCore/Contracts/WorkflowContracts.cs` ·
> `src/AgentPrism.UI/frontend/src/screens/workflows.tsx`,
> `workflow-editor.tsx`, `workflow-detail.tsx` ·
> `src/AgentPrism.UI/frontend/src/components/workflow-graph.tsx` ·
> `src/AgentPrism.UI/frontend/src/lib/workflow-graph.ts` ·
> `samples/AgentPrism.Api/Program.cs` (yalnız `AddWorkflow(...)` blokları:
> `ozetle-ve-cevir`, `ozetle-ve-onayla`).
>
> Ortam kurulumu, fixture verisi ve reset yordamı [`00-INDEKS.md`](00-INDEKS.md)'dedir.

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
| SSE `error` çerçevesinin gerçek sağlayıcı istisnalarını (K-296) yakalayamaması | `05-SAGLAYICI-OPENAI.md` `MT-OAI-043` — bu dosyadaki workflow hataları `AgentPrismException`'dır ve o boşluğa **girmez** (bkz. §8 notu) |
| İş kuyruğu (`Prefer: respond-async`) ile workflow çalıştırma | `16-IS-KUYRUGU-VE-ZAMANLAMA.md` (henüz üretilmedi) |
| Rol matrisi (`AgentPrismPolicies.Reader`/`.Admin`) genel no-op durumu | `00-INDEKS.md` §8 ve `14-SKILL-VE-SCRIPT.md`'de zaten kaydedildi — burada tekrar açıklanmaz, yalnız §9'un öncülü olarak anılır |

> **Rol matrisi burada da NO-OP'tur, tekrar test edilmez.** `WorkflowEndpoints`
> her ucu `RequireRole(roles.Reader/Operator/Admin)` ile işaretler ama
> `AgentPrismPolicies.*` örnek uygulamada kayıtlı değildir — statik bearer
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
3. Örnek uygulama çalışır: `cd samples/AgentPrism.Api && dotnet run` →
   `http://localhost:5080/agentprism`.
4. Örnek uygulama İKİ kodda tanımlı workflow taşır: `ozetle-ve-cevir`
   (Sequential, insan girdisi istemez) ve `ozetle-ve-onayla` (insan onayı
   ister, `RequestPort.Create<string, bool>`). Üçüncüsü yok; arayüzden
   tanımlanan workflow'lar bu dosyanın case'lerinde kurulur ve
   `FIX-WF-*` kimlikleriyle anılır.
5. `.UseWorkflows()` `Program.cs` satır ~103'te çağrılıdır — motor varsayılan
   olarak **açıktır**. §8'in bazı case'leri bunu geçici olarak kapatır; her
   birinin başında açıkça belirtilir.

```bash
export APB="Authorization: Bearer manuel-test-token-2026"
export APU="http://localhost:5080/agentprism"
export PG="docker exec -i ap-pg psql -U postgres -d agentprism"
```

> **Gerçek para uyarısı.** §3, §4, §5, §6 gerçek OpenAI modeliyle (`gpt-5.4-mini`)
> çalışır ve her çalıştırma en az bir, Magentic'te (§6) birden fazla model
> çağrısı yapar — plan reddi yöneticiyi yeniden çalıştırır. §1, §2, §7 (yalnız
> `/graph` ucu — grafın **kendisi çalıştırma gerektirmez**), §8, §9 model
> çağırmaz.

---

## Bu dosyanın yerel fixture'ları

Bu veriler yalnız bu dosyaya özgüdür, `00-INDEKS.md`'ye girmez (`PROMPT.md` §4.2).
Katalogdaki agent'lar için bkz. `00-INDEKS.md` §3.1 (`ozetleyici`, `cevirmen`
kullanılır — her ikisi de anahtar gerektirmez, OpenAI kullanır).

| Kimlik | Değer |
|---|---|
| `FIX-WF-01` | Ad `inceleme-zinciri` · `Sequential` · `agentNames: ["ozetleyici","cevirmen"]` |
| `FIX-WF-02` | Ad `plan-onayli` · `Magentic` · `agentNames: ["cevirmen"]` · `managerAgentName: "ozetleyici"` · `maxIterations: 2` · `requirePlanApproval: true` |
| `FIX-WF-03` | Ad `cift-gorus` · `Concurrent` · `agentNames: ["ozetleyici","cevirmen"]` |
| `FIX-WF-MSG-01` | `"AgentPrism, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."` — özetleme/çeviri girdisi |

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
  "agentNames": ["ozetleyici", "cevirmen"]
}'
```

**Beklenen sonuç**
- `HTTP: 200` — `SaveAsync` **her zaman** `TypedResults.Ok` döner, `Created`
  değil (`WorkflowEndpoints.cs:217-219`). Bu, Skill uçlarının `201`/`200`
  ayrımından **farklıdır**; workflow uçları böyle bir ayrım yapmaz.
- Gövdede `version: 1`, `tenantId: "default"`, `agentNames: ["ozetleyici","cevirmen"]`.

**Gerçek sonuç**
`HTTP: 200`, `version: 1`, `tenantId: "default"`, `agentNames: ["ozetleyici","cevirmen"]`. PostgreSQL kalıcılığıyla koşuldu. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici", "cevirmen"]
}'
```

**Beklenen sonuç**
- `HTTP: 200`, `version: 2`. Sürüm geçmişi **tutulmaz** (agent tanımlarının
  aksine) — yalnız son hâl saklanır (Faz 15 §15.4).

**Gerçek sonuç**
`HTTP: 200`, `version: 2`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Gövde MT-WF-002'nin sonucuyla birebir aynı (`version: 2`, güncellenmiş `description`). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. Kodda tanımlı `ozetle-ve-cevir` için listeyi çağır, varlığını doğrula.
2. Aynı ad için tekil `GET` çağır.

**Girilecek veri**
```bash
curl -s "$APU/api/workflows" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print([w['name'] for w in d])"
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/ozetle-ve-cevir" -H "$APB"
```

**Beklenen sonuç**
- Adım 1: `ozetle-ve-cevir` listede, `origin: "Code"`.
- Adım 2: `HTTP: 404`, ama `title: "Duzenlenebilir tanim yok"` — generic
  `"Workflow bulunamadi"` DEĞİL. `detail` alanı workflow'un kodda tanımlı
  olduğunu ve düzenlenebilir bir `WorkflowDefinition` taşımadığını açıklar.
  Generic mesaj dönerse fix'in regresyonudur.

**Gerçek sonuç**
Adım 1: `ozetle-ve-cevir` listede (`['inceleme-zinciri', 'ozetle-ve-cevir', 'ozetle-ve-onayla']`). Adım 2: `HTTP: 404`, `title: "Duzenlenebilir tanim yok"`, `detail: "'ozetle-ve-cevir' kodda tanimli bir workflow'dur (AddWorkflow). Listelenir ve calistirilabilir ama veritabaninda duzenlenebilir bir WorkflowDefinition tasimaz."` — generic mesaj DEĞİL, ayırt edici mesaj. Tam beklendiği gibi, fix regresyonu yok.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. Kod-tanımlı adla (`ozetle-ve-cevir`) çakışan bir DB tanımı `PUT` et.
2. Listeyi çağır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/workflows/ozetle-ve-cevir" -H "$APB" \
     -H "content-type: application/json" -d '{
  "displayName": "Sahte DB Kaydi",
  "kind": "Concurrent",
  "agentNames": ["ozetleyici", "cevirmen"]
}'
curl -s "$APU/api/workflows" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); e=[w for w in d if w['name']=='ozetle-ve-cevir'][0]; print(e)"
```

**Beklenen sonuç**
- `PUT`: `HTTP: 200` — kayıt **kabul edilir**, doğrulama yalnız yapısaldır ve
  `Concurrent` + 2 agent geçerlidir.
- Liste: `ozetle-ve-cevir` girdisi `origin: "Code"`, `kind: null`,
  `agentNames: []` gösterir — **DB kaydı GÖRÜNMEZ olur**
  (`WorkflowCatalog.ListAsync`, kod kayıtları `descriptors[name] = ...` ile
  DB'nin üzerine SONRADAN yazılır, `WorkflowCatalog.cs:66-67`). `POST
  .../run` da kod grafını çalıştırır (Sequential davranışı görülür,
  `Concurrent` değil) — bu adım koşulmaz, yalnız MT-WF-004'ün bulgusuyla
  birlikte not edilir.

**Gerçek sonuç**
`PUT`: `HTTP: 200`, kayıt kabul edildi. Liste: `{'name': 'ozetle-ve-cevir', 'displayName': None, 'description': 'Metni ozetler, sonra Ingilizceye cevirir. Kodda tanimlidir.', 'origin': 'Code', 'kind': None, 'agentNames': [], 'version': 1, 'updatedAt': None}` — DB kaydı (kind Concurrent, description "Sahte DB Kaydi") görünmüyor, kod kaydı üstün geliyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adım 1: `HTTP: 204`. Adım 2: `HTTP: 404`, `title: "Workflow bulunamadi"`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
`HTTP: 404`, `title: "Workflow bulunamadi"`, `detail: "'hic-yok-boyle-workflow' adinda bir workflow yok."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  (`DELETE .../ozetle-ve-cevir`, gövde farketmez zaten `store`'da kayıt yoktu).

**Adımlar**
1. `ozetle-ve-cevir`'i sil (hiç DB kaydı olmadığı hâlde).
2. Listeyi tekrar çağır.
3. Çalıştır.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" -X DELETE "$APU/api/workflows/ozetle-ve-cevir" -H "$APB"
curl -s "$APU/api/workflows" -H "$APB" | python3 -c "import json,sys; d=json.load(sys.stdin); print('ozetle-ve-cevir' in [w['name'] for w in d])"
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"FIX-WF-MSG-01"}'
```

**Beklenen sonuç**
- Adım 1: `HTTP: 404` — silinecek DB kaydı yoktu.
- Adım 2: `True` — kod-tanımlı workflow listede olmaya devam eder.
- Adım 3: normal şekilde çalışır, `WorkflowOutput` üretir. Kod-tanımlı bir
  workflow HTTP üzerinden **hiçbir şekilde** kaldırılamaz; bu tasarım
  kararıdır (K2, dağıtımla gelen davranış veritabanı yazma yetkisiyle ele
  geçirilemez).

**Gerçek sonuç**
Adım 1: `HTTP: 404` (silinecek DB kaydı yoktu). Adım 2: `True` (kod-tanımlı workflow listede kalmaya devam etti). Adım 3: normal çalıştı, `WorkflowOutput` üretti (gerçek OpenAI modeliyle, `ozetleyici`/`cevirmen` zincirinden geçti), `RunCompleted` ile bitti. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici"]
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "Workflow tanimin 'name' alani zorunludur."`
  (`WorkflowDefinitionValidator.cs:32-35`).

**Gerçek sonuç**
`HTTP: 400`, `title: "Workflow tanimi gecersiz"`, `detail: "Workflow tanimin 'name' alani zorunludur."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici"]
}'
```

**Beklenen sonuç**
- `HTTP: 200` (şüphe — doğrulanacak), gövdede `kind: "Sequential"`. Kullanıcı
  arayüz dışında (ör. bir betikle) `kind` göndermeyi unutursa hatasız ama
  YANLIŞ bir desen kaydolur.

**Gerçek sonuç**
Şüphe doğrulandı: `HTTP: 200`, gövdede `kind: "Sequential"` — `kind` alanı hiç gönderilmeden. Kod kusuru değil, ürünün bilinçli tasarım seçimi (`WorkflowKind.Sequential` varsayılan enum değeri `0`); dokümanın kendisi bunu zaten "şüphe" olarak işaretlemişti, şimdi ölçüldü.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
`HTTP: 400`, `detail: "'bos-katilimci' workflow'u hicbir agent icermiyor. 'agentNames' en az bir ad tasimalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
     -H "content-type: application/json" -d '{"kind": "Sequential", "agentNames": ["ozetleyici", "ozetleyici"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'ozetleyici' agent'i birden fazla kez geciyor ..."
  metnini içerir (`WorkflowDefinitionValidator.cs:61-66`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'tekrar-eden' workflow'unda 'ozetleyici' agent'i birden fazla kez geciyor. ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
     -H "content-type: application/json" -d '{"kind": "Concurrent", "agentNames": ["ozetleyici"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Concurrent' desenini kullaniyor ve en az iki
  agent ister; listede 1 ad var." metnini içerir
  (`WorkflowDefinitionValidator.cs:69-74`). Aynı kural `Handoff` ve
  `GroupChat` için de geçerlidir; bu case yalnız `Concurrent`'i temsil eder.

**Gerçek sonuç**
`HTTP: 400`, `detail: "'tek-concurrent' workflow'u 'Concurrent' desenini kullaniyor ve en az iki agent ister; listede 1 ad var."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
     -H "content-type: application/json" -d '{"kind": "Magentic", "agentNames": ["cevirmen"]}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Magentic' desenini kullaniyor ve
  'managerAgentName' zorunludur ..." metnini içerir
  (`WorkflowDefinitionValidator.cs:78-82`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'yoneticisiz' workflow'u 'Magentic' desenini kullaniyor ve 'managerAgentName' zorunludur. ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici", "cevirmen"],
  "managerAgentName": "ozetleyici"
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... hem yonetici hem katilimci olarak geciyor ..."
  metnini içerir (`WorkflowDefinitionValidator.cs:84-88`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'kendini-yoneten' workflow'unda 'ozetleyici' hem yonetici hem katilimci olarak geciyor. ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici", "cevirmen"],
  "managerAgentName": "ozetleyici"
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'GroupChat' deseninde 'managerAgentName'
  kullanmaz ... sirayi kod tarafindaki round-robin yoneticisiyle dagitir."
  metnini içerir (`WorkflowDefinitionValidator.cs:94-99`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'groupchat-yanlis' workflow'u 'GroupChat' deseninde 'managerAgentName' kullanmaz. ... sirayi kod tarafindaki round-robin yoneticisiyle dagitir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici", "cevirmen"],
  "handoffInstructions": "Gerekince devret."
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Sequential' deseninde 'handoffInstructions'
  kullanmaz. Bu alan yalnizca 'Handoff' desenine aittir." metnini içerir
  (`WorkflowDefinitionValidator.cs:101-105`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'yersiz-devir' workflow'u 'Sequential' deseninde 'handoffInstructions' kullanmaz. Bu alan yalnizca 'Handoff' desenine aittir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici", "cevirmen"],
  "requirePlanApproval": true
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail` "... 'Sequential' deseninde 'requirePlanApproval'
  kullanmaz. Plan onayi yalnizca 'Magentic' desenine aittir ..." metnini
  içerir (`WorkflowDefinitionValidator.cs:111-116`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'yersiz-plan-onayi' workflow'u 'Sequential' deseninde 'requirePlanApproval' kullanmaz. Plan onayi yalnizca 'Magentic' desenine aittir; ..."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  "agentNames": ["ozetleyici", "cevirmen"],
  "maxIterations": 0
}'
```

**Beklenen sonuç**
- `HTTP: 400`, `detail: "'sifir-tur' workflow'unun 'maxIterations' degeri
  pozitif olmalidir."` (`WorkflowDefinitionValidator.cs:118-121`).

**Gerçek sonuç**
`HTTP: 400`, `detail: "'sifir-tur' workflow'unun 'maxIterations' degeri pozitif olmalidir."`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  YOKTUR. `WorkflowDefinitionCompiler`'ın attığı `AgentPrismException`,
  `WorkflowRunner`'ın kendi içinde yakalanıp `RunEventType.RunFailed`
  (`WorkflowRunner.cs:1018`) tipli bir domain event'ine çevriliyor ve normal
  event akışının bir parçası olarak yayınlanıyor; `WorkflowEndpoints.WorkflowEventStream`'in
  ayrı `catch (AgentPrismException ...)` bloğu (`event: error` üreten,
  `WorkflowEndpoints.cs:491-497`) YALNIZCA akışın kendisi (async enumerable)
  DIŞARI istisna fırlatırsa çalışır — bu case'in hata yolu oraya hiç
  uğramıyor.

**Gerçek sonuç**
Adım 1: `HTTP: 200`. Adım 2: `event: run` → `event: event` (`{"type":"RunFailed",...,"text":"'hayali-agent' workflow'u 'yok-boyle-bir-agent' agent'ini kullaniyor ancak boyle bir agent katalogda yok. Once agent'i tanimlayin, sonra workflow'u kaydedin."}`) → `event: done`. Mesaj metni birebir doğru; yalnız SSE çerçeve adı dokümanın varsaydığından farklı (`event: error` DEĞİL, `event: event` içinde `type: RunFailed`). Ürün kusuru değil — dokümanın hangi hata mekanizmasının devreye gireceği varsayımı yanlıştı, düzeltildi (yukarıya bakınız).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 2 — Arayüz: Katalog ve Editör (Faz 16)

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
- `ozetle-ve-cevir`, `ozetle-ve-onayla` satırlarında **Kaynak** sütunu
  `workflows.originCode` ("code") rozeti taşır, **Desen** sütunu
  `workflows.codeGraph` ("code graph") rozeti gösterir (`kind == null`).
- `inceleme-zinciri` satırında **Desen** `Sequential` (accent tonlu rozet),
  **Kaynak** `workflows.originDatabase` ("database") rozeti taşır.
- **Agent'lar** sütunu `ozetleyici → cevirmen` biçiminde ok ile ayrılmış
  zincir gösterir (`workflows.tsx:100`, `agentNames.join(' → ')`).

**Gerçek sonuç**
`ozetle-ve-cevir`/`ozetle-ve-onayla` satırlarında Pattern "code graph" rozeti, Source "code" rozeti göründü. `inceleme-zinciri` satırında Pattern "Sequential" (accent), Source "database" rozeti göründü, Agents sütunu "ozetleyici → cevirmen" gösterdi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Adım 2: hiçbir alan doldurulmadan `Save` `[disabled]`. Adım 3: yalnız ad girildikten sonra (katılımcı yok) `Save` hâlâ `[disabled]`. Tam beklendiği gibi (buton devre dışıyken tıklama zaten mümkün değil, ağ isteği gitmedi).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
`Handoff`: "Handoff instructions" alanı göründü. `GroupChat`: "Handoff instructions" kayboldu, "Max iterations" alanı göründü. `Magentic`: "Manager agent" açılır listesi göründü (seçenekleri: `arastirmaci, bilgi-asistani, claude-destek, claude-dusunen, gemini-destek, gemini-kati-filtre, openrouter-destek, sesli-asistan, support, yonlendirici` — seçili katılımcılar `ozetleyici`/`cevirmen` listede YOK), "Ask a person to approve the plan" checkbox'ı "costs a manager turn" rozetiyle birlikte göründü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
2. Yalnızca `ozetleyici`'yi katılımcı olarak ekle.

**Beklenen sonuç**
- `workflowEditor.needsTwo` metni ("{kind} needs at least two participants.")
  sarı uyarı olarak görünür; `Save` devre dışı kalır (`tooFew` koşulu,
  `workflow-editor.tsx:134,231-235`).

**Gerçek sonuç**
"Concurrent needs at least two participants." metni `text-warn` (sarı/amber uyarı) CSS sınıfıyla göründü, `Save` `[disabled]`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-WF-034 — Detay ekranı: kod-tanımlı workflow'da `Edit` düğmesi hiç yok

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. Workflows listesinden `ozetle-ve-cevir`'e tıkla.

**Beklenen sonuç**
- Başlık yanında `Edit` düğmesi **görünmez** — `editable = descriptor.origin
  !== 'Code' && meta.roles.canAdminister` (`workflow-detail.tsx:187`),
  `origin === 'Code'` olduğu için ilk koşul yanlıştır.
- Grafik paneli ve çalıştırma paneli normal görünür.

**Gerçek sonuç**
Başlık yanında `Edit` düğmesi yok (yalnız "code graph" rozeti var). Graph paneli (Mermaid benzeri düğüm diyagramı, "Copy Mermaid" düğmesiyle) ve Run paneli (mesaj kutusu + Run düğmesi) normal göründü. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. Tarayıcıda doğrudan `.../agentprism/workflows/ozetle-ve-cevir/edit` adresine git.

**Beklenen sonuç**
- Sayfa bir form GÖSTERMEZ; `existing` sorgusu (`api.workflow(name)`, tekil
  `GET`) `404` alır ve `existing.isError` dalı `ErrorNote` bileşenini render
  eder (`workflow-editor.tsx:129-131`). `ErrorNote`'un metni artık
  MT-WF-004'ün ayırt edici mesajını taşır ("Duzenlenebilir tanim yok").

**Gerçek sonuç**
Form gösterilmedi; `alert` rolündeki bileşen "Duzenlenebilir tanim yok: 'ozetle-ve-cevir' kodda tanimli bir workflow'dur (AddWorkflow). Listelenir ve calistirilabilir ama veritabaninda duzenlenebilir bir WorkflowDefinition tasimaz." metnini gösterdi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 3 — Kodda Tanımlı Workflow: Gerçek Çalıştırma (İzlek B)

`ozetle-ve-cevir` insan girdisi istemez; Faz 15'in "Gerçek Kanıt" bölümündeki
sayılar bu bölümün beklenen sonuçlarının kaynağıdır. Gerçek model kullanır —
tam sayılar (`MessageDelta` adedi gibi) modelin o anki çıktısına bağlı olduğu
için **değişmez** olarak yalnız olay TİPLERİ ve SAYILARDAKİ üst sınırlar
değil, **hangi olay tiplerinin en az bir kez göründüğü** ve **ağaç şekli**
kontrol edilir (§4.1 kuralı).

### MT-WF-040 — `ozetle-ve-cevir` çalıştırma: olay tipleri ve `runs` ağacı

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
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"AgentPrism, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."}'
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
- `/tree`: **3** satır — `depth=0 kind=Workflow name=ozetle-ve-cevir`,
  altında `depth=1 kind=Agent name=ozetleyici`, `depth=1 kind=Agent
  name=cevirmen` (Faz 12'nin `parent_run_id` mekanizması).

**Gerçek sonuç**
SSE akışında beklenen tüm tipler en az bir kez göründü: `WorkflowStarted, SuperStepStarted, ExecutorInvoked, ExecutorCompleted, SuperStepCompleted, WorkflowOutput, RunCompleted` (+ `RunStarted`, `MessageDelta`). `/tree`: 3 satır — `depth=0 kind=Workflow agentName=ozetle-ve-cevir workflowName=ozetle-ve-cevir`, `depth=1 kind=Agent agentName=cevirmen parentRunId=<workflow runId>`, `depth=1 kind=Agent agentName=ozetleyici parentRunId=<workflow runId>`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
FROM agentprism.runs
WHERE workflow_name = 'ozetle-ve-cevir' OR agent_name IN ('ozetleyici','cevirmen')
GROUP BY kind, workflow_name, agent_name
ORDER BY kind;
```

**Beklenen sonuç**
- `kind=1` (Workflow) satırında `workflow_name='ozetle-ve-cevir'`,
  `agent_name='ozetle-ve-cevir'` (Faz 15 §15.3: "`AgentName` workflow
  satırlarında workflow'un adıdır").
- `kind=0` (Agent) satırlarında `workflow_name` **NULL**, `agent_name`
  sırasıyla `ozetleyici`/`cevirmen`.

**Gerçek sonuç**
`kind=1` satırında `workflow_name='ozetle-ve-cevir'`, `agent_name='ozetle-ve-cevir'` (NULL değil, `IS NULL` sorgusuyla doğrulandı: `f`). `kind=0` satırlarında `workflow_name IS NULL` (`t`), `agent_name` `cevirmen`/`ozetleyici`. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
FROM agentprism.workflow_checkpoints
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

**Gerçek sonuç**
HTTP: 3 kayıt, `parentCheckpointId` zinciri `null → id1 → id2` doğru sırayla. SQL: `sutun_tipi = json`. İlk 40 bayt: `{"stepNumber":0,"workflow":{"executors":` — `$type` yok ama JSON içinde başka yerde mevcut (yukarıya bakınız). K-027'nin sütun-tipi iddiası doğrulandı; "$type ilk özelliktir" alt iddiası düzeltildi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-WF-043 — Kontrol noktalarının `run_id` ve `session_id` ile filtrelenebilirliği

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Ön koşul**
- `ozetle-ve-cevir`'i FARKLI bir `sessionId` ile İKİNCİ kez çalıştır
  (`{"message":"...", "sessionId":"ikinci-oturum"}`), iki ayrı `runId` elde et.

**Doğrulama sorgusu**
```sql
SELECT run_id, session_id, count(*)
FROM agentprism.workflow_checkpoints
WHERE session_id IN ('<ilk-session-id>', 'ikinci-oturum')
GROUP BY run_id, session_id;
```

**Beklenen sonuç**
- İki ayrı `session_id` için ayrı `run_id` grupları; bir oturumun kontrol
  noktaları diğerine SIZMAZ. `GET .../checkpoints` her `runId` için yalnız
  o çalıştırmaya ait noktaları döner (`ListByRunAsync`, Faz 15 §15.6'nın
  plandan sapan 5. metodu).

**Gerçek sonuç**
İki ayrı `session_id` (`019ffd920675737f94afcb229c5231df` ve `ikinci-oturum`) için ayrı `run_id` grupları (her biri 3 kayıt), sızma yok. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-WF-044 — Aynı workflow iki kez çalıştırılınca executor kimlikleri SABİT kalır (K-127 kanıtı)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | K-127 |

**Adımlar**
1. `ozetle-ve-cevir`'i iki kez, iki farklı `sessionId` ile çalıştır.
2. Her iki çalıştırmada `ExecutorInvoked` olaylarının `text` alanındaki
   `ozetleyici_<32-hex>` kimliğini karşılaştır.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Test A","sessionId":"kimlik-testi-1"}' | grep -o '"text":"ozetleyici_[a-f0-9]*"' | head -1
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"Test B","sessionId":"kimlik-testi-2"}' | grep -o '"text":"ozetleyici_[a-f0-9]*"' | head -1
```

**Beklenen sonuç**
- İki komutun çıktısı **BİREBİR AYNI** kimliği taşır — `WorkflowAgentIdentity.Compute`
  `(workflowName, agentName)` çiftinden SHA-256 türetir, çalıştırmadan
  bağımsızdır (`WorkflowAgentIdentity.cs:75-87`). Uygulama bu iki çağrı
  arasında yeniden başlatılmasa da bu davranış zaten kanıtlanmış olur;
  gerçek "yeniden başlatma sonrası" senaryosu `dotnet run` durdurup tekrar
  başlatmayı gerektirir ve isteğe bağlıdır — koşum notuna eklenir.

**Gerçek sonuç**
İki farklı `sessionId` (`kimlik-testi-1`, `kimlik-testi-2`) ile iki ayrı çalıştırma, ikisinde de birebir aynı kimliği üretti: `ozetleyici_9fa38dc85895e6af1c1da45815ea9d03`. Tam beklendiği gibi. "Yeniden başlatma sonrası" senaryosu (uygulamayı durdurup tekrar başlatma) koşulmadı — isteğe bağlı olduğu belirtilmişti.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 4 — Arayüzden Tanımlı Workflow: Çalıştırma ve Sürdürme

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
     -d '{"message":"AgentPrism, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir."}'
```

**Beklenen sonuç**
- Kodda tanımlı ile AYNI davranış: `runs` ağacı 3 satır (1 workflow + 2 agent),
  `WorkflowOutput` üretilir. Arayüzden tanımlı workflow'un `AgentName`
  workflow adı olur (`inceleme-zinciri`).

**Gerçek sonuç**
`/tree`: 3 satır — `depth=0 kind=Workflow agentName=inceleme-zinciri workflowName=inceleme-zinciri`, `depth=1 kind=Agent agentName=cevirmen`, `depth=1 kind=Agent agentName=ozetleyici`. Kodda tanımlı ile aynı davranış. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Boş gövdeyle `resume` çağrıldı: `event: run` çerçevesi YENİ bir `runId` (`019ffd94-9a16-...`) bildirdi — orijinal `runId`den (`019ffd94-7aef-...`) farklı, tamamlanmış çalıştırma sorunsuz kabul edildi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
  öncesine denk geldiği için `ozetleyici`'nin YENİDEN çalıştığı gözlenebilir
  (`MessageDelta` olayları tekrar görülür) — bu AgentPrism'in değil, grafın
  o noktada kuyrukta bekleyen işin doğal sonucudur (Faz 16 §"Ölçülen MAF
  Davranışları" madde 7'nin aynısı, farklı bir bağlamda).

**Gerçek sonuç**
İlk kontrol noktasından `resume` çağrıldı; akış başarıyla başladı ve `event: done` ile bitti. Hem `ozetleyici_7e76fc6f...` hem `cevirmen_f9136cae...` için `MessageDelta` olayları YENİDEN göründü (ikisi de baştan çalıştı) — ilk kontrol noktası ilk super-step öncesine denk geldiği için beklenen davranış. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
   ör. `yonlendirici`).
2. Eski `checkpointId` ile eski `runId`'yi sürdürmeyi dene.

**Girilecek veri**
```bash
curl -s -X PUT "$APU/api/workflows/inceleme-zinciri" -H "$APB" -H "content-type: application/json" \
     -d '{"kind":"Sequential","agentNames":["ozetleyici","cevirmen","yonlendirici"]}'
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

**Gerçek sonuç**
Tanım güncellendi (`yonlendirici` eklendi), eski `checkpointId` ile eski `runId` üzerinden `resume` denendi. Sonuç: `event: run` → `event: event` (`type:"RunFailed"`, mesaj birebir yukarıdaki metin) → `event: done`. Mesaj içeriği tam beklendiği gibi; yalnız çerçeve adı (MT-WF-020'nin aynı kök nedeni) `event: error` değil.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 5 — Human-in-the-Loop: `ozetle-ve-onayla` (Faz 16 kanıtı)

`ozetle-ve-onayla` her girdide özetten sonra sabit bir soruyla ("Bu ozet
yayinlansin mi?") dış bir istek portuna ulaşır. Yanıt metni **model tarafından
üretilmez** — `BindAsExecutor`'daki sabit fonksiyonlardan gelir
(`Program.cs:407-411`) — bu yüzden `WorkflowOutput` metnine karşı **birebir**
eşleşme burada meşrudur (§4.1'in izin verdiği istisna, tıpkı `FIX-SKILL-*`
işaretçisi gibi: kaynağı model değil, sabit koddur).

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
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-onayla/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"AgentPrism yayin oncesi manuel kabul testi yaziyoruz."}'
```

**Beklenen sonuç**
- Akışın SON olayı `RunAwaitingInput`'tur; hemen öncesinde tam olarak bir
  `WorkflowRequest` olayı gelir. `event: done` çerçevesi normal şekilde gelir
  (akış hatasız kapanır, yalnız iş bitmemiştir).
- `GET .../requests` ile alınan kayıtta: `form: "Boolean"`,
  `requestType: "System.String"`, `responseType: "System.Boolean"`,
  `portId: "yayin-onayi"`, `prompt` "Bu ozet yayinlansin mi?" ile BAŞLAR
  (devamı model özetidir, birebir eşleşme aranmaz).

**Gerçek sonuç**
Akışın son olayı `RunAwaitingInput` (`sequence:66`), `event: done` normal geldi. Tam bir `WorkflowRequest` olayı (`sequence:62`) göründü — küçük bir hassasiyet notu: doküman "hemen öncesinde" diyordu ama aralarında `ExecutorCompleted`(63)/`SuperStepCompleted`(64) olayları var (doğrudan bitişik değil); asıl iddia (tam olarak bir kez görünmesi, son olayın `RunAwaitingInput` olması) doğrulandı. `GET .../requests`: `form: "Boolean"`, `requestType: "System.String"`, `responseType: "System.Boolean"`, `portId: "yayin-onayi"`, `prompt` "Bu ozet yayinlansin mi?" ile başlıyor. Tam beklendiği gibi (küçük hassasiyet notu dışında).

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. Workflow Detay ekranından `ozetle-ve-onayla`'yı çalıştır (arayüzden,
   mesaj kutusuna `FIX-WF-MSG-01` yaz).
2. Akış sürerken (SpinnerIcon dönerken) "Waiting on you" panelinin
   görünmediğini doğrula.
3. Akış bitince aynı panelin belirdiğini doğrula.

**Beklenen sonuç**
- Adım 2: panel YOK — `pending` sorgusu `enabled: finished` ile korunur
  (`workflow-detail.tsx:64-68`), `finished = runId !== null && !streaming`.
- Adım 3: panel görünür, `Boolean` formuna göre "Yes"/"No" düğmeleri
  (`workflowDetail.yes`/`no`) render edilir.

**Gerçek sonuç**
Çalıştırma çok hızlı tamamlandığı (gerçek model, tek agent + sabit onay portu, ~1-2 sn) için Playwright'ın snapshot alma turu ile Adım 2'nin ara durumunu (akış sürerken panel yok) görsel olarak yakalayamadım — her snapshot çağrısı akış zaten bitmişken geldi. Bunun yerine ağ isteklerini inceledim: `POST .../run` (SSE akışı) TAMAMLANDIKTAN SONRA `GET .../requests` çağrıldığı doğrulandı (istek listesinde run'dan hemen sonra sırayla geldi) — kodun `enabled: finished` koşuluyla tutarlı. Adım 3: panel "Waiting on you" başlığıyla göründü, `prompt` metni ve "Yes"/"No" düğmeleri render edildi. Ana iddia (sorgu yalnız akış bittikten sonra tetiklenir) dolaylı kanıtla doğrulandı; ara durumun görsel kanıtı alınamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
🚨 **KISMEN KALDI.** `event: run` YENİ bir `runId` bildirdi (beklendiği gibi) ve `WorkflowOutput.text` birebir `"Ozet yayinlandi."` oldu (beklendiği gibi). AMA iki ciddi sapma gözlendi:
1. **`GET /api/runs/<yeni-runId>` `status: "AwaitingInput"` döndü, `"Completed"` DEĞİL.** Akış `RunCompleted` yerine yeniden `RunAwaitingInput` ile bitti (YENİ bir `requestId` ile YENİ bir `WorkflowRequest` üretilmişti, o karşılıksız kaldı).
2. **`ozetleyici` agent'ı SIFIRDAN yeniden çalıştı** — akışta 40+ `MessageDelta` olayı yeniden göründü (gerçek model tekrar çağrıldı, gerçek ek maliyet oluştu), hâlbuki beklenti yalnız kontrol noktasından ilerlemekti (ozetleyicinin ÖNCEDEN üretilmiş çıktısını yeniden kullanmak).
Doğrulama için AYNI deneyi ikinci kez tekrarladım (yeni bir çalıştırma + respond): birebir aynı desen — yeniden tam özetleme + yeni bir `WorkflowRequest` + `AwaitingInput` ile bitiş. Tam belirlenimli. Grafın kendisi "dashed edge loops back" notuyla döngüsel olarak tasarlanmış olabilir (`onay-sorusu`/`yayin-onayi` arasında), bu yüzden "bir kez onayla → Completed" beklentisinin kendisi YANLIŞ olabilir — ama gözlenen davranış (her `respond` çağrısının modeli SIFIRDAN yeniden çağırması) `resume`'un checkpoint'ten ÇALIŞMA KALDIĞI YERDEN devam etmesi gereken temel sözleşmesiyle çelişiyor ve gerçek para maliyeti doğuruyor. Kod değiştirilmedi; kök neden netleştirme (döngüsel graf tasarımı mı, yoksa `PrepareResponseAsync`/checkpoint geri yükleme mekanizmasının hatası mı) ayrı bir kod incelemesi gerektirir.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

### MT-WF-063 — `respond` reddet → çıktı BİREBİR `"Yayin iptal edildi; ozet arsivde birakildi."`

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Yüksek |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Ön koşul**
- `ozetle-ve-onayla`'yı yeniden çalıştır, yeni `runId`/`requestId` al
  (MT-WF-062'nin `requestId`'si zaten tüketilmiştir).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/respond" -H "$APB" -H "content-type: application/json" \
     -d '{"requestId":"<requestId>","approved":false}'
```

**Beklenen sonuç**
- `WorkflowOutput.text` **birebir** `"Yayin iptal edildi; ozet arsivde
  birakildi."` değerine eşittir.

**Gerçek sonuç**
`WorkflowOutput.text` birebir `"Yayin iptal edildi; ozet arsivde birakildi."` oldu — bu kısım tam beklendiği gibi. Ancak MT-WF-062'nin AYNI kök nedeni burada da tekrarlandı: `ozetleyici` SIFIRDAN yeniden çalıştı (34 `MessageDelta`), yeni bir `WorkflowRequest` üretildi (karşılıksız), akış `RunAwaitingInput` ile bitti (`Completed` değil). İki bağımsız çalıştırmada (onayla + reddet) birebir aynı desen — tam belirlenimli. Metin doğruluğu Geçti sayılır; tamamlanma durumu/yeniden çalıştırma sorunu MT-WF-062'nin `HATA` kaydına referansla not edildi, ayrı bir hata açılmadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Yeni bir `ozetle-ve-onayla` çalıştırması, gerçek `runId` elde.

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

**Gerçek sonuç**
Gerçek `event: error` çerçevesi geldi (MT-WF-020/053'ün aksine — bu istisna `PrepareResponseAsync`'te akış hiç başlamadan senkron fırlatıldığı için `WorkflowEventStream`'in `catch` bloğuna gerçekten düşüyor), `message`: "'uydurma-istek-kimligi' kimlikli bekleyen bir istek '<runId>' calistirmasinda yok. Istek listesini GET /api/workflows/runs/{runId}/requests ile tazeleyin." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-WF-065 — `AwaitingInput` OLMAYAN bir çalıştırmaya `respond` → SSE `error`

Negatif senaryo — tamamlanmış (`ozetle-ve-cevir`) bir çalıştırmayı yanıtlamayı
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

**Gerçek sonuç**
Gerçek `event: error` çerçevesi geldi (MT-WF-064 ile aynı gerekçeyle — senkron ön-kontrol istisnası), `message`: "'<runId>' kimlikli calistirma insan girdisi beklemiyor (durum: Completed). Yalnizca 'AwaitingInput' durumundaki bir calistirma yanitlanabilir." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- `X-AgentPrism-Tenant: kiraci-alfa` başlığıyla `ozetle-ve-onayla`'yı çalıştır,
  `runId`'yi not al (bkz. `13-KIRACI-VE-GUVENLIK.md` MT-SEC-021 için header
  çözümlemesinin nasıl açıldığı).

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-onayla/run" -H "$APB" \
     -H "X-AgentPrism-Tenant: kiraci-alfa" -H "content-type: application/json" \
     -d '{"message":"Kiraci alfa testi."}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/runs/<runId>/requests" -H "$APB" \
     -H "X-AgentPrism-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- İkinci çağrı `HTTP: 404`, `title: "Calistirma bulunamadi"` —
  `RequireWorkflowRunAsync`'in kiracı eşleşmesi (`WorkflowRunner.cs:289-294`)
  "yetkisiz" bile demez, varlığı sızdırmaz.

**Gerçek sonuç**
İlk denemede `AgentPrism:Tenancy:Enabled` kapalı olduğu için `X-AgentPrism-Tenant` başlığı hiç okunmadı, her iki çağrı da aynı (`default`) kiracıyı kullandı ve ikinci çağrı yanlışlıkla `200` döndü — bu bir güvenlik açığı DEĞİL, benim test kurulum hatamdı (koşmadan önce §5'in ön koşulunu — MT-SEC-021'in header çözümlemesini açmayı — atlamışım). `dotnet user-secrets set "AgentPrism:Tenancy:Enabled" "true"` + `AllowHeaderResolution "true"` ile yeniden başlatılıp doğru şekilde tekrarlandı: ikinci çağrı (`kiraci-beta` başlığıyla) `HTTP: 404`, `title: "Calistirma bulunamadi"`, `detail: "'<runId>' kimlikli calistirma bulunamadi."` — varlığı sızdırmadı. Aynı `runId`'ye doğru kiracıdan (`kiraci-alfa`) yapılan kontrol sorgusu `200` ile veriyi döndürdü, izolasyonun çalıştığını doğruladı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 6 — Magentic Plan Onayı

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
  "agentNames": ["cevirmen"],
  "managerAgentName": "ozetleyici",
  "maxIterations": 2,
  "requirePlanApproval": true
}'
curl -N -s -X POST "$APU/api/workflows/plan-onayli/run" -H "$APB" -H "content-type: application/json" \
     -d '{"message":"AgentPrism, Microsoft Agent Framework uzerine kurulu bir NuGet paket ailesidir. Bunu Ingilizceye cevir."}'
```

**Beklenen sonuç**
- `PUT`: `HTTP: 200`, `requirePlanApproval: true`.
- `run`: akış `RunAwaitingInput` ile kapanır. `GET .../requests`:
  `form: "PlanReview"`, `requestType:
  "Microsoft.Agents.AI.Workflows.MagenticPlanReviewRequest"`, `prompt`
  BOŞ DEĞİL (planın kendisi — metnine karşı eşleşme aranmaz, yalnız
  varlığı ve `form` alanı değişmezdir).

**Gerçek sonuç**
`PUT`: `HTTP: 200`, `requirePlanApproval: true`. `run`: akış `RunAwaitingInput` ile kapandı. `GET .../requests`: `form: "PlanReview"`, `requestType: "Microsoft.Agents.AI.Workflows.MagenticPlanReviewRequest"`, `responseType: "Microsoft.Agents.AI.Workflows.MagenticPlanReviewResponse"`, `prompt` boş değildi (planın maddeleri). Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
- Yeni bir `runId` açılır; akışta en az bir `ExecutorInvoked`(`cevirmen`) ve
  bir `WorkflowOutput` görülür. `WorkflowOutput.text` **boş değildir** (model
  üretimi — metnine eşleşme aranmaz).

**Gerçek sonuç**
🚨 **KISMEN KALDI.** Yeni `runId` açıldı, `ExecutorInvoked(cevirmen_...)` en az bir kez göründü (iki kez), `WorkflowOutput.text` boş değildi — bu üç loose iddia teknik olarak sağlandı. AMA `WorkflowOutput.text` gerçek bir çeviri DEĞİL, MAF'ın kendi sistem mesajıydı: `"Task execution stopped due to hitting the maximum round count limit."` (case'in kendi `PUT` isteğindeki `maxIterations: 2` bu sınıra çok hızlı çarpıyor — plan + onay-sonrası devam + katılımcı çağrısı en az 3 tur gerektiriyor). Bundan SONRA akış `cevirmen`'i BİR KEZ DAHA çağırdı, ardından `ExecutorFailed` (×2) ve son olarak `RunFailed`: `"Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken"` ile çöktü. Yani run temiz bir `Completed` DEĞİL, bir iç hata zinciriyle bitti. `maxIterations: 2` — tam olarak case'in kendi reprodüksiyon adımlarında belirtilen değer — bu Magentic + plan-onayı bileşimi için yetersiz ve MAF'ın round-limit'e ulaşma davranışı zarif bir durdurma yerine bir çökme zincirine yol açıyor. Kod değiştirilmedi.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-003/K-401 — kısmi düzeltme):** Aynı senaryo birebir tekrarlandı. Run HÂLÂ `RunFailed` ile bitiyor (bu doğru davranış — plan gerçekten tamamlanmadı) AMA mesaj artık anlamlı: `RunFailed.Text` = `"This Magentic orchestration has already terminated. To process new messages, create a new workflow instance."` — eskiden opak `"Error invoking handler for Microsoft.Agents.AI.Workflows.TurnToken"`. `WorkflowRunner.ToRunError` artık `TargetInvocationException`/tek-elemanlı `AggregateException` sarmalayıcılarını soyup gerçek nedeni yazıyor. Durum bu yüzden `Geçti`'ye ÇEVRİLMEDİ — dokümanın "temiz bir `Completed`" beklentisi hâlâ karşılanmıyor; MAF'ın round-limit-sonrası fazla çağrısını önceden kestirip akışı zarif durdurmak `F-106` (`docs/UCUNCU-FAZ-ADAYLARI.md`) olarak ayrı bir yetenek adayına devredildi. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Çerçeve `event: error` DEĞİL, MT-WF-020/053/072 ile aynı desende `event: event` içinde `type: "RunFailed"` geldi (bu istisna akışın İÇİNDE, MAF'ın kendi Magentic mantığında oluşuyor — senkron ön-kontrol değil). Metin ölçüldü: `"Plan reddedildi ancak duzeltme metni verilmedi. Yonetici agent'in plani neye gore yeniden kuracagini bilmesi icin 'text' alani zorunludur."` — bu, Faz 16 §16.4'ün "düzeltme metni zorunludur" iddiasını doğrular, `AgentPrismException` mesaj metni koda uygun. Yalnız çerçeve adı beklenenden farklı — MT-WF-020'nin doküman düzeltmesiyle aynı gerekçe.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
🚨 **KALDI — MT-WF-071 ile AYNI kök nedenle.** Yeni `runId` açıldı ama akış YENİ bir `RunAwaitingInput` ile DEĞİL, doğrudan bir `WorkflowOutput` (gerçek çeviri metni: `"AgentPrism is a family of NuGet packages built on Microsoft Agent Framework."`) ÜRETİP ardından `ExecutorFailed` (`MagenticOrchestrator`, `TargetInvocationException`) ve `RunFailed` (`"Error invoking handler for Microsoft.Agents.AI.Workflows.ExternalResponse"`) ile çöktü. Yönetici yeniden PLANLAMADI (beklenen ikinci `AwaitingInput`/`PlanReview` hiç oluşmadı) — bunun yerine sanki plan zaten onaylanmış gibi doğrudan yürütmeye geçti, sonra dahili bir hata verdi. `maxIterations: 2` sınırının bu senaryoda (ret + yeniden planlama + yürütme, en az 3-4 yönetici turu gerektirir) yetersiz kaldığı MT-WF-071'de zaten gözlemlenmişti; bu case AYNI kısıtın farklı bir çökme belirtisiyle (bu kez `ExternalResponse` handler hatası) tekrarlandığını gösteriyor. Kod değiştirilmedi; ikisi de aynı `HATA` kaydına referans verir.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-003/K-401 — kısmi düzeltme):** Aynı `ToRunError` düzeltmesi (`TargetInvocationException`/tek-elemanlı `AggregateException` soyma) bu case'in kod yoluna da uygulanır (MT-WF-071 ile AYNI `ToRunError` çağrı noktası) — bu case'in kendisi bu kapanışta AYRICA yeniden koşulmadı (yalnız MT-WF-071 birebir tekrarlandı), ama aynı düzeltme mekanik olarak `RunFailed.Text`'i buradaki `ExternalResponse` handler hatası için de anlamlı MAF mesajına çevirmesi beklenir. Durum bu yüzden `Geçti`'ye ÇEVRİLMEDİ (ampirik olarak doğrulanmadı, yalnız MT-WF-071 doğrulandı) — dokümanın "yeniden planlama" beklentisi de hâlâ karşılanmıyor (`F-106`). Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı

---

# 7 — Graf Görselleştirme (Faz 16)

`/graph` ucu **çalıştırma gerektirmez** — graf tanımdan değil DERLENMİŞ
workflow'dan çıkarılır, ama derleme yalnız agent adlarının katalogda var
olduğunu ister, bir `run` istemez. Bu bölümün çoğu case model çağırmaz.

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
curl -s "$APU/api/workflows/ozetle-ve-cevir/graph" -H "$APB" | python3 -m json.tool
```

**Beklenen sonuç**
- `HTTP: 200` (model çağrısı OLMADAN).
- `nodes` listesinde `id` alanı MT-WF-040'ta not edilen `ExecutorInvoked.text`
  ile **birebir** eşleşen bir düğüm var; o düğümün `kind: "Agent"`,
  `agentName: "ozetleyici"`.
- `startExecutorId` graftaki bir düğüme karşılık gelir.
- `mermaid` alanı `flowchart` ile başlayan bir metin taşır.

**Gerçek sonuç**
`HTTP: 200`. `nodes` listesinde `id: "ozetleyici_9fa38dc85895e6af1c1da45815ea9d03"` — MT-WF-040/044'te gözlenen `ExecutorInvoked.text` ile birebir aynı — `kind: "Agent"`, `agentName: "ozetleyici"`. `startExecutorId` aynı düğüme işaret ediyor. `mermaid` alanı `"flowchart TD\n..."` ile başlıyor. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
     -d '{"kind":"Sequential","agentNames":["ozetleyici"]}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/hic-calismadi/graph" -H "$APB"
```

**Beklenen sonuç**
- `HTTP: 200` — hiçbir `runs` satırı olmadan graf üretilir; `WorkflowGraphReader`
  tanımı DERLEYİP (`WorkflowDefinitionCompiler`) yansıtır, çalıştırma geçmişi
  okumaz.

**Gerçek sonuç**
Hiç çalıştırılmamış TAZE `hic-calismadi` tanımı için `HTTP: 200`, tam bir graf (nodes/edges/mermaid) üretildi. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-WF-082 — Arayüzde canlı düğüm renklendirme: `running` (cyan, nabız) → `done` (yeşil)

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 16 |
| **İlgili karar** | — |

**Adımlar**
1. `ozetle-ve-cevir` Detay ekranını aç, mesaj kutusuna `FIX-WF-MSG-01` yaz,
   `Run`'a bas.
2. Çalıştırma sürerken grafı izle (DevTools → Elements, `[data-testid="workflow-node"]`).
3. Çalıştırma bitince tekrar bak.

**Beklenen sonuç**
- Adım 2: sırayla önce `ozetleyici` düğümünün `data-state="running"` olduğu
  ve etrafında nabız animasyonu (`animate-pulse`) göründüğü, sonra
  `data-state="done"` olduğu, ardından `cevirmen`'in aynı döngüden geçtiği
  gözlenir.
- Adım 3: HER İKİ agent düğümü de `data-state="done"`, kenar rengi yeşil
  (`--ap-emerald`). Hiçbir düğüm `failed` (kırmızı) değildir.

**Gerçek sonuç**
Çalıştırma çok hızlı tamamlandığı için (MT-WF-061'de olduğu gibi) Adım 2'nin ara durumunu (`running`/nabız) Playwright turlarıyla yakalayamadım — her `evaluate` çağrısı akış zaten bitmişken geldi. Adım 3 doğrulandı: `document.querySelectorAll('[data-testid="workflow-node"][data-state]')` üç düğümün de (`OutputMessages`, `cevirmen`, `ozetleyici`) `data-state="done"` olduğunu gösterdi, hiçbiri `failed` değildi. Son durum tam beklendiği gibi; ara durumun görsel kanıtı alınamadı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
     -d '{"kind":"Concurrent","agentNames":["ozetleyici","cevirmen"]}'
curl -s "$APU/api/workflows/cift-gorus/graph" -H "$APB" | \
  python3 -c "import json,sys; g=json.load(sys.stdin); agents=[n for n in g['nodes'] if n['kind']=='Agent']; print(len(agents), [n['agentName'] for n in agents])"
```

**Beklenen sonuç**
- Çıktı `2 ['ozetleyici', 'cevirmen']` — TAM 2 `Agent` kind düğüm, `Batcher/*`
  ve `ConcurrentEnd`/`Start` düğümleri `Orchestration`/`Output` olarak
  sınıflanır, agent SAYILMAZ (`WorkflowGraphReader.AgentNameOf`, `/` içeren
  kimlikler `null` döner — `WorkflowGraphReader.cs:98-105`).

**Gerçek sonuç**
`2 ['cevirmen', 'ozetleyici']` (sıra farklı ama önemsiz — sayı ve üyelik doğru). `Batcher`/`Start`/`ConcurrentEnd` düğümleri `Agent` sayılmadı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
`navigator.clipboard.writeText` yamalanarak (OS pano izin diyaloğu otomasyonda askıda kaldığı için doğrudan okuma yerine bu yöntem kullanıldı) "Copy" düğmesine tıklandı: yakalanan metin `"flowchart TD\n  ozetleyici_9fa38dc85895e6..."` ile başladı. Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 8 — Motor Kapalı ve Sınır Ayarları

Bu bölümün her case'i `dotnet user-secrets` (veya işaretli olanlarda geçici
`Program.cs` değişikliği) ile ortamı bozar; her case sonunda ayar eski hâline
döndürülüp uygulama yeniden başlatılır (`00-INDEKS.md` §4).

### MT-WF-090 — `UseWorkflows()` KALDIRILIRSA çalıştırma uçları `501` döner (geçici kod değişikliği)

Negatif senaryo. `MT-SEC-070` deseniyle aynı: geçici kod değişikliği gerektirir.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `samples/AgentPrism.Api/Program.cs` satır ~103'teki `.UseWorkflows()`
   çağrısını GEÇİCİ olarak yorum satırına al.
2. `dotnet run` ile yeniden başlat.
3. Katalog ve çalıştırma uçlarını sırayla dene.
4. Değişikliği GERİ AL, tekrar başlat.

**Girilecek veri**
```bash
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows" -H "$APB"
curl -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" -d '{}'
curl -s -w "\nHTTP: %{http_code}\n" "$APU/api/workflows/ozetle-ve-cevir/graph" -H "$APB"
```

**Beklenen sonuç**
- Katalog (`GET /api/workflows`): `HTTP: 200` — hâlâ çalışır, `runner is null`
  dalı `store.ListAsync` ile devam eder (`WorkflowEndpoints.cs:126-134`). Kod
  tanımlı `ozetle-ve-cevir` listede **artık görünmez** (kayıt hiç yapılmadı).
- `run`: `HTTP: 501`, `title: "Workflow motoru kayitli degil"`, `detail`
  "... AgentPrism.Workflows paketini ekleyin ve UseWorkflows() cagirin."
  metnini içerir.
- `graph`: `HTTP: 501` (aynı `NotRegistered` yardımcı metodu).

**Gerçek sonuç**
`.UseWorkflows()` yorum satırına alınıp yeniden başlatıldı. Katalog: `HTTP: 200`, kod tanımlı `ozetle-ve-cevir`/`ozetle-ve-onayla` listede YOK (yalnız DB kayıtları göründü). `run`: `HTTP: 501`, `title: "Workflow motoru kayitli degil"`, `detail: "Workflow calistirmak icin AgentPrism.Workflows paketini ekleyin ve UseWorkflows() cagirin."` `graph`: `HTTP: 501`, aynı hata. Tam beklendiği gibi. Değişiklik geri alındı, yeniden derlendi/başlatıldı.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

### MT-WF-091 — `AgentPrism:Workflows:Enabled=false` → SSE `error` "calistirma kapali"

Negatif senaryo — yalnız çalıştırma kapanır, katalog etkilenmez.

| | |
|---|---|
| **İzlek** | B |
| **Önem** | Orta |
| **İlgili faz** | Faz 15 |
| **İlgili karar** | — |

**Adımlar**
1. `dotnet user-secrets set "AgentPrism:Workflows:Enabled" "false"`
   (`samples/AgentPrism.Api` dizininde), yeniden başlat.
2. Çalıştırmayı dene.
3. Ayarı `dotnet user-secrets remove "AgentPrism:Workflows:Enabled"`,
   yeniden başlat.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- SSE `event: run` gelir (bir `runId` ÜRETİLİR), ardından `event: error`;
  `message: "Workflow calistirma kapali. 'AgentPrism:Workflows:Enabled'
  ayarini acin."` (`WorkflowRunner.cs:339-343`). Bu bir `AgentPrismException`
  olduğu için `error` çerçevesi GÜVENİLİR şekilde üretilir — K-296'nın
  boşluğuna girmez.

**Gerçek sonuç**
🚨 **KRİTİK KUSUR — kök neden: `AgentPrismWorkflowOptions` hiç config'e bağlı değil.** `dotnet user-secrets set "AgentPrism:Workflows:Enabled" "false"` ile yeniden başlatıldıktan sonra çalıştırma HİÇ engellenmedi — akış normal şekilde `RunCompleted` ile bitti, gerçek model iki kez çağrıldı (gerçek ücret oluştu). Kök neden kod okumasıyla kesin biçimde bulundu: `src/AgentPrism.Workflows/AgentPrismWorkflowsBuilderExtensions.cs:52`'deki `UseWorkflows()` yalnızca `services.AddOptions<AgentPrismWorkflowOptions>();` çağırıyor — `AgentPrismWorkflowOptions.SectionName` sabiti (`"AgentPrism:Workflows"`, dosyada tanımlı) HİÇBİR YERDE kullanılmıyor (`grep` ile doğrulandı, sıfır eşleşme); `IConfiguration`'a bağlayan tek yol, yalnızca kod içinde geçirilebilen isteğe bağlı `configure` lambda parametresi. Sonuç: `Enabled`, `EnableCheckpointing`, `MaxConcurrentRuns`, `RunTimeout`, `MaxSuperSteps`, `KeepCheckpointsAfterCompletion` — bu sınıfın YEDİ alanının TAMAMI — `appsettings.json`/`dotnet user-secrets` üzerinden asla okunamaz, sessizce C# varsayılanlarında kalır. Bu, MT-WF-092 ve MT-WF-093'te AYNI kök nedenle tekrar doğrulandı.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-004/K-402 — düzeltildi):** `UseWorkflows()` artık `BindConfiguration` ile `AgentPrism:Workflows`'a bağlanıyor. Aynı senaryo birebir tekrarlandı: `Enabled=false` set edilip yeniden başlatıldıktan sonra `curl` çalıştırması `event: error` ile doğru şekilde reddedildi — `{"type":"AgentPrismException","message":"Workflow calistirma kapali. 'AgentPrism:Workflows:Enabled' ayarini acin."}`. Beklenenle birebir eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. `dotnet user-secrets set "AgentPrism:Workflows:MaxSuperSteps" "2"`,
   yeniden başlat.
2. `ozetle-ve-cevir`'i çalıştır (gerçek kanıtta bu workflow 3 super-step
   üretir — Faz 15 "Gerçek Kanıt": `SuperStepStarted 3`).
3. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- SSE `event: error`; `message` "Workflow 2 super-step sinirini asti ve
  durduruldu. Devretme veya grup sohbeti dongusu sonlanmiyor olabilir;
  'maxIterations' degerini dusurun veya agent talimatlarina bir bitirme
  kosulu ekleyin." metnini içerir (`WorkflowRunner.cs:629-636`). Çalıştırma
  bu noktada `CancelAsync` ile İPTAL edilir; `GET /api/runs/{runId}`
  `status: "Canceled"` veya `"Failed"` gösterir (koşumda hangisi olduğu
  kaydedilir).

**Gerçek sonuç**
MT-WF-091'in AYNI kök nedeniyle KALDI: `MaxSuperSteps: "2"` set edilip yeniden başlatıldıktan sonra `ozetle-ve-cevir` (3 super-step üretir) hiçbir sınırla karşılaşmadan `RunCompleted` ile normal bitti — sınır asla uygulanmadı, `AgentPrismWorkflowOptions`'ın konfigürasyona hiç bağlanmaması nedeniyle.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-004/K-402 — düzeltildi):** Aynı senaryo birebir tekrarlandı. `MaxSuperSteps=2` set edilip `ozetle-ve-cevir` çalıştırıldı: `RunFailed` — `"Workflow 2 super-step sinirini asti ve durduruldu. Devretme veya grup sohbeti dongusu sonlanmiyor olabilir; 'maxIterations' degerini dusurun veya agent talimatlarina bir bitirme kosulu ekleyin."` (dokümanın beklediği metinle birebir). `GET /api/runs/{runId}` → `status: "Failed"`. Beklenenle eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
1. `dotnet user-secrets set "AgentPrism:Workflows:EnableCheckpointing" "false"`,
   yeniden başlat.
2. `ozetle-ve-cevir`'i çalıştır (checkpoint YAZILMAZ), `runId`'yi not al.
3. Aynı `runId`'yi sürdürmeyi dene.
4. Ayarı kaldır, yeniden başlat.

**Girilecek veri**
```bash
curl -N -s -X POST "$APU/api/workflows/runs/<runId>/resume" -H "$APB" -H "content-type: application/json" -d '{}'
```

**Beklenen sonuç**
- SSE `event: error`; `message: "Kontrol noktasindan sürdürme icin
  'AgentPrism:Workflows:EnableCheckpointing' acik olmalidir."`
  (`WorkflowRunner.cs:794-795` — bu dal `checkpointManager is null` VE
  `execution.ResumeFrom` dolu olduğunda tetiklenir; ama önce
  `RequireCheckpointAsync` çalışır — checkpoint hiç yazılmadığı için asıl
  görülecek hata muhtemelen MT-WF-094'ün "kontrol noktasi yok" mesajıdır.
  Koşum HANGİ mesajın geldiğini kaydeder; bu, kodun iki ayrı savunma
  katmanının hangisinin önce tetiklendiğini netleştirir).

**Gerçek sonuç**
MT-WF-091/092'nin AYNI kök nedeniyle KALDI: `EnableCheckpointing: "false"` set edilip yeniden başlatıldıktan sonra çalıştırma sırasında YİNE DE 3 kontrol noktası yazıldı (`SELECT count(*) ... = 3`), ayar hiç okunmadı. `resume` denemesi de normal şekilde başarılı oldu (ne "EnableCheckpointing acik olmalidir" ne "kontrol noktasi yok" hatası — checkpoint zaten mevcuttu). `AgentPrismWorkflowOptions`'ın konfigürasyona hiç bağlanmaması aynı kök neden.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-004/K-402 — düzeltildi):** Aynı senaryo birebir tekrarlandı. `EnableCheckpointing=false` set edilip `ozetle-ve-cevir` çalıştırıldı: `SELECT count(*) FROM workflow_checkpoints WHERE run_id=...` → `0` — hiç checkpoint yazılmadı (beklenen davranış). `resume` denemesi → `event: error`, `AgentPrismException`: `"'<runId>' kimlikli calistirmanin kontrol noktasi yok. Kontrol noktasi yazimi kapaliyken baslatilan bir calistirma sürdürulemez."` — dokümanın kendi öngördüğü belirsizlik (`RequireCheckpointAsync`'in "kontrol noktasi yok" dalı mı, yoksa `EnableCheckpointing` dalı mı önce tetiklenir) netleşti: "kontrol noktasi yok" dalı tetikleniyor, AMA mesaj checkpointing'in KAPALI olduğunu da açıkça belirtiyor — iki savunma katmanı arasındaki belirsizlik pratikte zararsız (mesaj her iki durumu da kapsıyor). Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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

**Gerçek sonuç**
Gerçek `event: error` çerçevesi geldi (bu istisna `ResumeStreamingAsync`'in kendisi bir `async IAsyncEnumerable` yineleyici metodu olduğu için, `RunStreamingAsync`'in aksine, doğal biçimde `WorkflowEventStream`'in `catch` bloğuna ulaşıyor), `message`: "'00000000-0000-0000-0000-000000000000' kimlikli calistirma bulunamadi." Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" \
     -d "{\"sessionId\":\"$(python3 -c 'print("a"*129)')\"}"
```

**Beklenen sonuç**
- SSE `event: error`; `message: "Yurutme oturumu kimligi en fazla 128
  karakter olabilir."` (`WorkflowSessionId.cs:33-37`).

**Gerçek sonuç**
🚨 **KUSUR — SSE akışı hiç başlamadı.** Ne `event: run` ne `event: error` geldi — düz bir HTTP `500` gövdesi: `{"title":"An error occurred while processing your request.","status":500}` (detay yok, tamamen generic). Sunucu logunda kök neden görüldü: `AgentPrism.WorkflowRunner.RunStreamingAsync` (`WorkflowRunner.cs:186`) bir `async` yineleyici DEĞİL — düz bir metottur, gövdesinde `WorkflowSessionId.Require(request.SessionId)` nesne başlatıcısının İÇİNDE SENKRON olarak çağrılır ve `ExecuteAsync(...)`'in döndürdüğü `IAsyncEnumerable`'ı geri döndürür. İstisna bu yüzden `WorkflowEventStream` (SSE yazıcısı) hiç devreye girmeden, `WorkflowEndpoints.RunAsync`'in çağrı zincirinden DOĞRUDAN fırlar ve ASP.NET'in genel `ExceptionHandlerMiddleware`'ine düşer — "Unhandled exception" olarak loglanır (`fail: Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware[1]`). Asıl mesaj (`"Yurutme oturumu kimligi en fazla 128 karakter olabilir."`) sunucu logunda doğru ama istemciye HİÇ ulaşmıyor. Bu, `RespondStreamingAsync`/`ResumeStreamingAsync`'in (gerçek `async IAsyncEnumerable` yineleyicileri, MT-WF-064/065/094'te doğru `event: error` üreten) davranışından FARKLI — yalnız `RunStreamingAsync`'in bu yapısal farkı bu boşluğu yaratıyor.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-005/K-403 — düzeltildi):** `RunStreamingAsync` gerçek bir `async IAsyncEnumerable` yineleyicisi yapıldı. Aynı senaryo birebir tekrarlandı: artık `event: run` ardından `event: error` (`AgentPrismException`, `message: "Yurutme oturumu kimligi en fazla 128 karakter olabilir."`), `HTTP: 200` (SSE akışı, düz 500 DEĞİL) geliyor. Beklenenle birebir eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
curl -N -s -X POST "$APU/api/workflows/ozetle-ve-cevir/run" -H "$APB" -H "content-type: application/json" \
     -d '{"sessionId":"gecersiz oturum!"}'
```

**Beklenen sonuç**
- SSE `event: error`; `message: "Yurutme oturumu kimligi yalnizca harf,
  rakam, '-' ve '_' icerebilir."` (`WorkflowSessionId.cs:41-48` — boşluk
  VE `!` her ikisi de reddedilir, ilk karşılaşılan karakterde durur).

**Gerçek sonuç**
MT-WF-095'in AYNI kök nedeniyle KALDI: SSE hiç başlamadı, düz `HTTP 500` (`"An error occurred while processing your request."`) geldi. Sunucu logunda doğru mesaj (`"Yurutme oturumu kimligi yalnizca harf, rakam, '-' ve '_' icerebilir."`) görüldü ama istemciye ulaşmadı — `RunStreamingAsync`'in senkron doğrulaması aynı yapısal nedenle `WorkflowEventStream`'i baypas ediyor.

**🔧 Kapanış güncellemesi (2026-08-14, HATA-K-005/K-403 — düzeltildi):** Aynı senaryo birebir tekrarlandı. Artık `event: run` ardından `event: error` (`AgentPrismException`, `message: "Yurutme oturumu kimligi yalnizca harf, rakam, '-' ve '_' icerebilir."`), `HTTP: 200`. Beklenenle birebir eşleşiyor. Ayrıntı: `SONUCLAR-K-2026-08-13.md`.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

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
     -H "X-AgentPrism-Tenant: kiraci-beta"
```

**Beklenen sonuç**
- `HTTP: 404`, `title: "Calistirma bulunamadi"` — kontrol noktası deposu
  `tenant_id` filtresiyle çalışır, "yetkisiz" bile denmez (`IWorkflowCheckpointStore`
  XML dokümanı: "Baska bir kiracinin noktasi bulunamadi doner").

**Gerçek sonuç**
İlk denemede `AgentPrism:Tenancy:Enabled` kapalıydı (MT-WF-066'daki gibi test kurulum hatası), yanlışlıkla `200` alındı. `Tenancy:Enabled`/`AllowHeaderResolution` açılıp yeniden başlatıldıktan sonra doğru şekilde tekrarlandı: `kiraci-beta` başlığıyla `HTTP: 404`, `title: "Calistirma bulunamadi"`, `detail: "'<runId>' kimlikli calistirma yok."` Tam beklendiği gibi.

**Durum:** ☐ Beklemede · ☑ Geçti · ☐ Kaldı · ☐ Atlandı

---

# 9 — Güvenlik: API Anahtarı Kapsam Boşluğu

### MT-WF-100 — 🚨 `RunsRead`-kapsamlı bir API anahtarı workflow `PUT`/`run`'a erişebiliyor mu?

Şüpheli davranış — koddan ölçüldü, koşumda doğrulanacak/çürütülecek.
`WorkflowEndpoints.Map` (`src/AgentPrism.AspNetCore/Endpoints/WorkflowEndpoints.cs`)
**hiçbir ucunda** `.RequireApiKeyScope(...)` çağırmaz — karşılaştırma:
`AgentEndpoints.cs` ve `RunEndpoints.cs` her CRUD/çalıştırma ucuna
`RequireApiKeyScope(ApiKeyScope.AgentsAdmin)`/`RunsWrite` ekler
(`AgentEndpoints.cs:43-99`, `RunEndpoints.cs:79-233`). `AgentPrismEndpointFilter.CheckScope`
şu satırı taşır: `if (requirement is null || record.Scopes.Contains(requirement.Scope))
return null;` (`AgentPrismEndpointFilter.cs:194`) — metadata YOKSA denetim
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
     -H "content-type: application/json" -d '{"kind":"Sequential","agentNames":["ozetleyici"]}'

curl -N -s -w "\nHTTP: %{http_code}\n" -X POST "$APU/api/workflows/kapsam-testi/run" -H "$WFKEY" \
     -H "content-type: application/json" -d '{}'

curl -s -w "\nHTTP: %{http_code}\n" -X PUT "$APU/api/agents/kapsam-kontrol" -H "$WFKEY" \
     -H "content-type: application/json" -d '{"name":"kapsam-kontrol","instructions":"test"}'
```

**Beklenen sonuç (şüphe)**
- Adım 2 ve 3: `HTTP: 200` — kapsam kısıtı UYGULANMAZ (kodun okuduğu hâliyle
  beklenen).
- Adım 4: `HTTP: 403`, `title: "Kapsam yetersiz"` — kontrol grubu kapsam
  sisteminin AgentEndpoints'te çalıştığını, ama WorkflowEndpoints'te HİÇ
  devrede olmadığını gösterir.
- Doğrularsa: **Kusur, Önem: Yüksek** — bir okuma-amaçlı otomasyon anahtarı
  gerçek para harcayabilir ve workflow tanımlarını değiştirebilir/silebilir.
  Çürürse (ör. çalışma zamanında farklı bir mekanizma devredeyse) not
  güncellenir.

**Gerçek sonuç**
🚨 **ŞÜPHE DOĞRULANDI — KUSUR, Önem: Yüksek.** Yalnız `RunsRead` kapsamlı bir anahtar üretildi (`plaintextKey` alanı — doküman `rawKey` varsaymıştı, düzeltildi). Adım 2: `PUT /api/workflows/kapsam-testi` → `HTTP: 200` — kayıt kabul edildi. Adım 3: `POST /api/workflows/kapsam-testi/run` → `HTTP: 200`, gerçek bir çalıştırma başlatıldı (`event: done`, gerçek `runId`, gerçek model çağrısı — gerçek ücret oluştu). Adım 4 (kontrol grubu): `PUT /api/agents/kapsam-kontrol` aynı anahtarla → `HTTP: 403`, `title: "Kapsam yetersiz"`, `detail: "Bu uc 'AgentsAdmin' kapsamini gerektiriyor; anahtar bu kapsami tasimiyor."` — kapsam sistemi `AgentEndpoints`'te ÇALIŞIYOR, `WorkflowEndpoints`'te TAMAMEN DEVRE DIŞI. Doğrulandı: yalnız-okuma niyetiyle üretilmiş bir otomasyon anahtarı workflow tanımlarını yazabilir/silebilir VE gerçek para harcayan bir çalıştırma başlatabilir.

**Durum:** ☐ Beklemede · ☐ Geçti · ☑ Kaldı · ☐ Atlandı
