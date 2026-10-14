# Faz 209 — Workflow Gelen Kutusu ve İstek Süresi

> **Durum:** ✅ Tamamlandı (2026-10-14)
> **Plan onayı:** onaylandı (2026-10-14) — 🔒1 **B** (`workflow_requests` projeksiyonu, K-128 yeni gerekçeyle açılır) · 🔒2 **B** (`(oturum, requestId)` başına mantıksal istek) · 🔒3 **A** (`Failed` + `WorkflowRequestExpired`) · 🔒4 **A** (cevaplanmış bekleyen istek `Completed` ile kapanır) · 🔒5 **A** (workflow başına `RequestTimeout`, `RequestedAt`'ten) · 🔒6 **A** (`Reader` + `RunAccess.Read`). Açık Soru 7-11 önerileriyle uygulanır (7 B, 8 A, 9 A, 10 A, 11 A). Faz 208 kapandı (`arsiv/fazlar/208-*`)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-319** (gelen kutusu ve süre yarısı; karar yarısı [Faz 208](208-WORKFLOW-KARAR-SOZLESMESI.md))
> **Genellik:** Tracon HITL'ini kullanan her host etkilenir. Kalem tek tüketici turundan geldi (video tüketicisi, 2026-10-11); tüketici geri bildirimi kuralı uygulanır. İkinci kanıt ilk ilkedir: (1) retention bekleyen run'ın istek olayını, checkpoint'lerini ve girdi satırını süresiz tutar (K-888) ve K-130 gereği cevaplanan istek de bekler — bu satırlar **hiç** silinmez (`RetentionTargetRegistry.cs:122-144`); tüketici bunu süre olmadan sınırlayamaz; (2) yönetişim eşitliği: tool onayının kiracı geneli kutusu ve süresi var (`GET /api/approvals/pending`, `ApprovalExpirationService`), workflow isteğinin yok. Ekosistem sinyali: MCP Tasks `input_required` görevi bir yaşam süresiyle (TTL) tanımlar
> **Karşı görüş:** "Onay tüketicinin ekranında tutulabilir." Liste için kısmen doğru: tüketici `GET /api/runs?status=AwaitingInput&kind=Workflow` + run başına `/requests` ile bir liste kurabilir (N+1 çağrı). Ama "yalnız bekleyen" süzgeci sayfalamadan sonra uygulanırsa `skip/take` bozulur (`RunEndpoints.cs:64-67` kuralı); cevap bilgisi yalnız Faz 208'in tablosundadır. Süre ise yalnız Tracon'da kurulabilir: bekleyen run'ı kapatıp retention'ın satırları bırakmasını yalnız Tracon sağlar
> **Önkoşul:** [Faz 208](208-WORKFLOW-KARAR-SOZLESMESI.md) — "yanıtlandı" işareti 208'in `workflow_responses` kaydını okur; iki faz `WorkflowEndpoints.cs`, `WorkflowRunner.cs`, `WorkflowDefinition` ve `WorkflowRegistrationOptions`'ı paylaşır
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.UI`, `.Testing.Contracts.Xunit`, `Tracon.Client` (üretilen); projeksiyon seçilirse `.Sql.Shared`, `.PostgreSql`, `.SqlServer`, `.Sqlite`
> **Yeni paket:** Yok · **Migration:** ölçüme bağlı (Açık Soru 1) — projeksiyon seçilirse üç sağlayıcı; numara uygulama anında alınır
> **Public API:** büyüyor, `1.0.0` GA'dan (K-603) önce ucuz — ölçüldü (2026-10-14): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, toplam 17 satır
> **Tüketici yüzeyi:** site: `concepts/workflows.md` ("Asking a human" `:883-916`, checkpoint ve retention paragrafı `:844-858`), `reference/configuration.md:526-529` (bekleyen run'ın retention istisnası), `ui.md` (Approvals bölümü `:326-347` + ekran görüntüsü), `capabilities.md` · sevk edilen: `WorkflowDefinition`/`AddWorkflow` XML `<example>`, `src/Tracon.Workflows/README.md`, `CHANGELOG.md`, TS istemcisi, `RetentionTargetRegistry.cs:129` yorumu ("an expiry for them is F-319's scope")
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md` (`MT-WF-*`), `docs/manuel-test/13-KIRACI-VE-GUVENLIK.md` (`MT-SEC-*`), `docs/manuel-test/23-SAKLAMA-ARSIV-KOTA.md` (`MT-RET-*`)

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman — önce **Açık Sorular**'ın 🔒 satırları: plan onayı onlara bağlıdır
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-128\|K-130\|K-369\|K-371\|K-372\|K-671\|K-888\|K-911\|K-938\|K-988" docs/KARARLAR.md
   ```
   **K-128** (istek olay yükünde; projeksiyon onu açar), **K-130** (cevaplanan run bekler; süre bunu değiştirir),
   **K-369/K-371/K-372** (`pending_approvals` deseni: FK cascade, `ExpireAsync`, yalnız kuyruk), **K-671** (liste süzülmez, reddedilir),
   **K-888** (retention bekleyen run'ı tutar — süre onun sınırıdır), **K-911** (veri sahibi silmesi bekleyen run'ı da siler),
   **K-938** (beklemeyen run'a cevap `400`), **K-988** (devam kaydedilen sürümle)
3. Önceki faz devri:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/208-*.md
   ```
   208: `workflow_responses` kaydı, iddia anahtarının ölçülen kapsamı (**oturum**, `claim_scope = session_id`), `WorkflowRegistrationOptions`, cevap sırası denetim → satır → kayıt (K-997)
4. Alan hafızası:
   [`hafiza/workflows.md`](../../hafiza/workflows.md) (K-888'in üç kayıp yolu, K-988) ·
   [`hafiza/sql-migration.md`](../../hafiza/sql-migration.md) (projeksiyon seçilirse) ·
   [`hafiza/kapasite-olcumu.md`](../../hafiza/kapasite-olcumu.md) (§209.1 ölçümü)
5. Gerektiğinde: `src/Tracon.Core/Approvals/ApprovalExpirationService.cs` — servisin emsali (tamamı 140 satır)

---

## Amaç

Bugün bekleyen insan istekleri yalnız run başına okunur. Bir host, n revizyonlu onayı tek bir listeden yönetemez. Cevaplanmış istek de K-130 gereği "bekliyor" görünür. İsteğin bir süresi yoktur: retention bekleyen run'ın satırlarını süresiz tutar ve cevaplanan istek de bekler, yani bu satırlar hiç silinmez (K-888). Bu faz iki şey getirir. Kiracı geneli, sayfalı ve "yanıtlandı" işaretli bir istek listesi. İsteğe bağlı, workflow başına bir süre ve süre dolunca kaynak run'ın kapanması — böylece retention satırları bırakır. Kazanan: onayı host ekranından yöneten tüketici ve depolamanın büyümesini sınırlamak isteyen operatör.

- **F-319 (gelen kutusu ve süre yarısı)** — kiracı geneli bekleyen istek listesi (sayfalı, "yanıtlandı" işaretli); workflow başına isteğe bağlı süre; süre dolunca kapanış.

### Kapsam dışı — kapanışta aday açılır

| Kalem | Neden |
|---|---|
| İstek portu başına süre | Süre workflow başınadır; port başına seçim tüketici kanıtı ister |
| Süre dolumu webhook'u (`workflow.request.expired`) | Onay süresinin de webhook'u yok (`ApprovalExpirationService` yazmaz); Açık Soru 9 |
| Kutudan satır içi cevap (kart bileşenini taşımak) | Açık Soru 8; seçilmezse kutu satırı run'ın kartına bağlanır |
| Süresi yaklaşan istek için hatırlatma | Bildirim kanalı ve zamanlama ayrı tasarımdır |
| Onay kutusu ile workflow kutusunu tek uçta birleştirmek | İki kaydın şeması farklıdır (`PendingApproval` ≠ `WorkflowPendingRequest`); konsol aynı ekranı kullanır |

### Bugün ne çalışmıyor — doğrulanmış kanıt

`WorkflowRunner.cs` sembol adıyla anılır (Faz 205 ve 208 aynı dosyaya dokunur).

| Kanıt | Gözlem |
|---|---|
| [`WorkflowEndpoints.cs:315-326`](../../../src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs), `:1074-1114` | İstek listesi yalnız run başınadır (`/api/workflows/runs/{runId}/requests`) |
| `WorkflowRunner.ListPendingRequestsAsync` | Run'ın bütün olayları baştan okunur (`ReadEventsAsync(runId, 0)`); tür süzgeci yok. Cevaplanan run `AwaitingInput` kalır ve isteğini listeler (K-130) |
| [`IRunStore.cs:197`](../../../src/Tracon.Abstractions/Runs/IRunStore.cs), `:220` | `QueryRunsAsync(RunQuery)` kiracı + durum + tür + sayfa taşır; `ReadEventsAsync` tür almaz |
| [`RunEndpoints.cs:43-110`](../../../src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs) | `GET /api/runs?status=AwaitingInput&kind=Workflow` bugün çalışır (`take` en çok 200, `:108`); istek yükü ve cevap bilgisi yok. `:64-67`: liste süzülmez — sayfalama sözleşmesi bozulur |
| `0001_initial.sql` (PG `:156`, SQL Server `:212-213`, Sqlite `:171`) · `runs_workflow_idx` (PG `0007_workflows.sql:14`, SQL Server `0001:232`, Sqlite `0001:177`) | `(tenant_id, status, started_at)` indeksi üç sağlayıcıda var; `run_events`'te yalnız `(run_id, seq)` PK var, tür indeksi yok |
| [`TraconOptions.cs:713`](../../../src/Tracon.Core/TraconOptions.cs), `:726` | `RecordMessageDeltas = true` varsayılandır: run başına tarama delta olaylarını da okur; yük sınırı 8 KB |
| [`WorkflowDefinition.cs:186`](../../../src/Tracon.Abstractions/Workflows/WorkflowDefinition.cs) · [`WorkflowPendingRequest.cs:19-51`](../../../src/Tracon.Abstractions/Workflows/WorkflowPendingRequest.cs) | Tanım yalnız `RunTimeout` taşır; istek `ExpiresAt`, workflow adı ve oturum taşımaz |
| [`RunStatus.cs:19-90`](../../../src/Tracon.Abstractions/Runs/RunStatus.cs) | `Expired` üyesi yok |
| [`RetentionTargetRegistry.cs:122-144`](../../../src/Tracon.Sql.Shared/Internal/RetentionTargetRegistry.cs), `:221-233`, `:336-349` | `AwaitingInput` run'ın istek olayı, checkpoint'leri ve girdi satırı süzgeçle tutulur; `:129` "an expiry for them is F-319's scope". `runs` satırı hiçbir retention hedefinde yok (`RetentionTargets.cs:18-93`) |
| [`ApprovalExpirationService.cs:25-139`](../../../src/Tracon.Core/Approvals/ApprovalExpirationService.cs) · [`TraconApprovalOptions.cs:21-37`](../../../src/Tracon.Core/Approvals/TraconApprovalOptions.cs) | Emsal: `SingletonGuard`, `PeriodicTimer`, `MaxPerScan`; süresi dolan onayın run'ı `CompleteRunAsync` ile `Failed` + `ApprovalExpired` olur (`:113-121`) |
| [`0027_pending_approvals.sql:1-30`](../../../src/Tracon.PostgreSql/Migrations/0027_pending_approvals.sql) | Emsal: kendini "projeksiyon" diye tanımlayan tablo; `(status, expires_at)` süre indeksi (`:29`) |
| [`approvals.tsx`](../../../src/Tracon.UI/frontend/src/screens/approvals.tsx) (263 satır) | Konsolun onay ekranı; workflow isteği orada görünmez |

> Kanıtlar 2026-10-14 tarihinde `245afe91` zemininde (worktree `faz-208`) doğrulandı.

---

## 209.1 — Uygulamanın ilk adımı: ölçüm ve liste kaynağı kararı

Kod yazılmadan önce iki yol **sayıyla** karşılaştırılır. Sonuç bu dokümanın "Plandan Sapmalar" bölümüne ve Açık Soru 1'in cevabına yazılır.

| Yol | Nasıl okur | Bedeli |
|---|---|---|
| **A — olay taraması** | `QueryRunsAsync(Status = AwaitingInput, Kind = Workflow)` bir sayfa; sayfadaki her run için olay taraması; `workflow_responses` ile işaret | Migration yok, K-128 bozulmaz. Sayfa başına N+1 tur. "Yalnız bekleyen" süzgeci ve süre sırası SQL'de yapılamaz |
| **B — projeksiyon** | `workflow_requests` tablosu; satır run `AwaitingInput` kapanırken yazılır; `(tenant, status, created_at)` ve `(status, expires_at)` indeksi | Migration × 3, K-128 açılır, çift yazım (olay + satır) ve geriye dönük doldurma (backfill) |

**Ölçüm** (`hafiza/kapasite-olcumu.md` yöntemiyle, üç SQL sağlayıcısında):

1. Sayfa (`take = 50`) kurma süresi; run başına olay sayısı 10, 100, 1000 (deltalar açık). Tarama yolunda ve projeksiyon sorgusunda.
2. Süre servisinin taraması: 10 000 bekleyen run'da tek tur süresi. Projeksiyonsuz yol süreyi her tur yeniden hesaplar (tanım sürümü okuması dahil).
3. Gerçek dağılım: örnek uygulamanın `summarize-and-approve` run'ının olay sayısı.

**İşlevsel ölçüt — ölçümden bağımsız:** liste "yalnız bekleyen" ya da "süresi en yakın" sırasıyla doğru sayfalanacaksa A yetmez (K-671 kuralı süzgeci sayfadan sonra yasaklar). Plan önerisi: süzgeç kabul edilirse **B**. Ölçüm A'yı yeterli gösterse bile süzgeç ve süre indeksi B'yi gerektirir. B seçilirse K-128 yeni bir gerekçeyle açılır: kararın kendi koşulu run başına istek sayısıdır, kiracı geneli liste değildir.

## 209.2 — Liste sözleşmesi

`GET /api/workflows/requests?state=pending|answered|all&workflowName=&skip=&take=` (varsayılan `state=pending`, `take` 1-200, `RunEndpoints` ile aynı sınır). Yanıt `WorkflowPendingRequest` listesidir; tip dört alan kazanır ve run başına `/requests` da aynı alanları döner:

| Alan | Kaynak |
|---|---|
| `WorkflowName`, `SessionId` | run satırı |
| `ExpiresAt` | §209.4 (süre yoksa `null`) |
| `Answered` + `LastResponseRunId` | Faz 208'in `workflow_responses` kaydı (`Abandoned` sayılmaz) |

Yetki (Açık Soru 6): kiracı süzgeci her zaman uygulanır. `IRunAuthorizationHandler` reddi listeyi **reddeder**, süzmez (K-671; onay listesi `403` emsali, `ApprovalEndpoints.cs:92-108`).

Arayüz: `IWorkflowRunner.ListRequestsAsync(WorkflowRequestQuery, CancellationToken)` varsayılan uygulamalı (default interface member). İstek yükünü ayrıştıran `WorkflowRequestDescriptor` `Tracon.Workflows`'ta `internal`'dır; liste o yüzden runner'dan geçer. Varsayılan uygulama boş liste döner — `ValidateResponseAsync` emsali (bu üyeden önce yazılmış bir uygulama derlenmeye devam eder).

## 209.3 — Mantıksal istek: aynı istek birden çok run'da

MAF bekleyen isteği checkpoint'ten **aynı kimlikle** yeniden yayınlar (K-128). Çok istekli bir run'da bir isteğe cevap verilince, cevaplanmayan istekler hem kaynakta hem devam run'ında listelenir. Düz `/resume` da aynısını yapar. Run başına liste aynı insan kararını iki satırda gösterir.

Öneri (Açık Soru 2): liste **`(oturum, requestId)` başına bir satır** gösterir — o isteği taşıyan en yeni `AwaitingInput` run'ı. "Yanıtlandı" işareti aynı anahtarla okunur; bu, Faz 208'in iddia anahtarıyla aynıdır (208 ölçümü: oturum — `claim_scope = session_id`; işaret de mantıksal istek başınadır). Run başına `/requests` değişmez: o run'ın kendi isteklerini döner.

## 209.4 — Süre

- `WorkflowDefinition.RequestTimeout` (`TimeSpan?`, varsayılan `null` = süresiz; K1). Kod workflow'unda `WorkflowRegistrationOptions.RequestTimeout` (Faz 208'in tipi).
- `ExpiresAt = RequestedAt + RequestTimeout`. Süre, kaynak run'ın **kaydettiği** tanım sürümünden okunur (K-988 emsali): bekleyen run'ın tanımı sonradan değişirse süresi değişmez.
- Sınır: 1 dakika – 365 gün (Açık Soru 10). `WorkflowDefinitionValidator` dışını `400` ile reddeder.
- Süre dolmuş isteğe cevap: kaynak artık `AwaitingInput` değildir, mevcut `400` döner (K-938). Yeni kod yok.

## 209.5 — Süre dolunca kapanış

`WorkflowRequestExpirationService` (`Tracon.Workflows`, `UseWorkflows()` kaydeder), `ApprovalExpirationService`'in birebir deseni: `SchemaReadyGate`, `SingletonGuard("workflow-request-expiration")`, `PeriodicTimer`, tur başına sınır. Seçenekler `TraconWorkflowOptions`'ta: `RequestExpirationScanInterval` (1 dk), `MaxRequestExpirationsPerScan` (100).

Kapanış (öneri, Açık Soru 3): `IRunStore.CompleteRunAsync` ile kaynak run `Failed` + `RunError { Type = "WorkflowRequestExpired", Message = "... expired at {ExpiresAt:O}" }`. Gerekçe: aday onay kutusunu aynalar ve emsal `ApprovalExpired`'dır (`ApprovalExpirationService.cs:113-121`). Yeni `RunStatus.Expired` her `switch`'i, istemciyi ve istatistiği değiştirirdi. `Canceled` + sebep (K-963) "biri durdurdu" diye okunur.

Bu, K-130'un "durum geriye dönük değiştirilmez" cümlesine **bir istisna** açar — onay süresi aynısını `AwaitingApproval` için zaten yapar. Kapanışta K-130 ve K-888 satırları güncellenir (kategori: kalıcı-veri). Kapanan run'ın satırlarını retention bir sonraki turda bırakır: üç süzgeç `status = AwaitingInput`'a bakar.

Cevaplanmış ama bekleyen istek (Açık Soru 4): K-130 gereği o run da bekler ve satırları da hiç silinmez. Öneri: o da süreyle kapanır, ama **`Completed`** ile ve hata taşımadan — cevap verildi, devam ayrı run'dır. "Başarısız" görünmez; retention yine bırakır.

Yarış: `respond` kapıları geçtikten sonra servis kaynağı kapatabilir. Projeksiyon (B) seçilirse iki yazım aynı satırda `WHERE status = Pending` korumasıyla yarışır (`pending_approvals` emsali). A seçilirse pencere kalır: devam açılır, kaynak `Failed` kapanır. Hata modu tablosunda.

## 209.6 — Konsol

`approvals.tsx` ikinci bölüm kazanır: "Workflow requests". Sütunlar: workflow, istem (kısaltılmış), istendiği an, süre, durum (bekliyor / cevaplandı). Satır, run'ın istek kartına (`workflow-detail?runId=`) gider (Açık Soru 8). Süzgeç: durum, workflow. Metinler `locales/{en,tr}/operations.ts` (K-228). `ui.md` Approvals bölümü ve ekran görüntüsü güncellenir.

`workflow-editor.tsx`: "Cevap süresi" alanı (`requestTimeout`). Tam değiştirme editörüdür: alan formda taşınmazsa kayıt süreyi siler (`full-replace-editors.test.tsx`).

---

## Planlanan Public API

> Taslak imzalardır. Her tip için: **bugün eklemek bedava, GA'dan (K-603) sonra eklemek kırıcı.**

```csharp
// Tracon.Abstractions — Workflows
public sealed record WorkflowDefinition
{
    // ... mevcut üyeler + Faz 208'in ResponseMode'u
    public TimeSpan? RequestTimeout { get; init; }      // null = süresiz
}

public sealed record WorkflowPendingRequest
{
    // ... mevcut üyeler
    public string? WorkflowName { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool Answered { get; init; }
    public Guid? LastResponseRunId { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter<WorkflowRequestState>))]
public enum WorkflowRequestState { Pending = 0, Answered = 1, All = 2 }

public sealed record WorkflowRequestQuery
{
    public WorkflowRequestState State { get; init; }
    public string? WorkflowName { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}

public interface IWorkflowRunner
{
    // ... mevcut üyeler
    ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListRequestsAsync(
        WorkflowRequestQuery query, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<WorkflowPendingRequest>>([]);
}

// Yalnız Açık Soru 1-B seçilirse:
public interface IWorkflowRequestStore { /* AddAsync, ListAsync(query), ExpireAsync(now, max), MarkAnsweredAsync */ }
public sealed record WorkflowRequestRecord { /* tenant, run, session, requestId, status, expiresAt, createdAt */ }
// RetentionTargets (mevcut static class) yeni sabit: WorkflowRequests = "workflow_requests"

// Tracon.Workflows
public sealed class WorkflowRegistrationOptions { /* Faz 208 + */ public TimeSpan? RequestTimeout { get; set; } }
public sealed class TraconWorkflowOptions
{
    // ... mevcut üyeler
    public TimeSpan RequestExpirationScanInterval { get; set; } = TimeSpan.FromMinutes(1);
    public int MaxRequestExpirationsPerScan { get; set; } = 100;
}

// Tracon.AspNetCore — Contracts
public sealed record WorkflowSaveRequest { /* ... */ public TimeSpan? RequestTimeout { get; init; } }
```

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/workflows/requests` | Açık Soru 6 (öneri `Reader` + `RunsRead`) | Yeni: kiracının bekleyen istekleri, sayfalı, "yanıtlandı" işaretli, süre taşır |
| `GET` | `/api/workflows/runs/{runId}/requests` | Reader (değişmez) | Öğeler dört yeni alanı taşır |
| `PUT` | `/api/workflows/{name}` | Admin (değişmez) | `requestTimeout` alanı |

### Arayüz payı

Onay ekranına bir tablo bölümü ve editörde bir alan. Pay uygulama anında gzip KB olarak ölçülür; bütçe 250 KB. Faz 208'in ölçümü taban alınır.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/Workflows/
├── WorkflowDefinition.cs, WorkflowPendingRequest.cs, IWorkflowRunner.cs
├── WorkflowRequestQuery.cs                  (yeni: sorgu + durum)
└── IWorkflowRequestStore.cs                 (yalnız 1-B)
src/Tracon.Core/Workflows/WorkflowDefinitionValidator.cs
src/Tracon.Core/Workflows/InMemoryWorkflowRequestStore.cs        (yalnız 1-B)
src/Tracon.Workflows/
├── TraconWorkflowOptions.cs, WorkflowRegistrationOptions.cs, TraconWorkflowsBuilderExtensions.cs
├── Internal/WorkflowRequestExpirationService.cs                 (yeni)
├── Internal/WorkflowRequestDescriptor.cs                        (ExpiresAt)
└── Internal/WorkflowRunner.cs                                   (ListRequestsAsync; 1-B'de projeksiyon yazımı CompleteAsync'te)
src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs, Contracts/WorkflowContracts.cs
src/Tracon.Sql.Shared/Internal/WorkflowDefinitionPayload.cs
src/Tracon.Sql.Shared/{Stores/SqlWorkflowRequestStore.cs, Internal/RetentionTargetRegistry.cs}   (yalnız 1-B; :129 yorumu her durumda)
src/Tracon.{PostgreSql,SqlServer,Sqlite}/Migrations/<sıradaki>_workflow_requests.sql            (yalnız 1-B)
src/Tracon.Testing.Contracts.Xunit/Contracts/WorkflowRequestStoreContract.cs                    (yalnız 1-B)
src/Tracon.UI/frontend/src/screens/{approvals,workflow-editor}.tsx, locales/{en,tr}/*.ts
```

---

## Hata Modları ve Testler

> Seviye kuralı: [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Liste başka kiracının isteğini gösterir | Fonksiyonel, iki kiracı + sözleşme (1-B) | `WorkflowRequestInboxTests`; `TenantIsolationContract` |
| Reddeden `IRunAuthorizationHandler` listeyi süzer (sayfa kısalır) | Fonksiyonel | Liste `403` ile reddedilir (K-671) |
| Aynı istek iki satırda görünür (yeniden yayın) | Fonksiyonel | Çok istekli graf: bir istek cevaplanınca öbürü tek satır |
| "Yanıtlandı" işareti `Abandoned` satırı sayar | Fonksiyonel | Faz 208'in yeniden sürme case'i üstünde |
| `state=pending` sayfası süzgeç yüzünden kısa döner | Fonksiyonel | 60 istek, 10'u cevaplı: üç sayfa toplamı 50 |
| `take` sınır dışı (0, 10 000) | Fonksiyonel | 1-200'e sıkıştırılır |
| Süre dolmamış istek kapanır ya da dolmuş istek açık kalır | Fonksiyonel, `FakeTimeProvider` | `WorkflowRequestExpirationTests` |
| Tanım düzenlenince bekleyen isteğin süresi değişir | Fonksiyonel | Kaydedilen sürümün süresi (K-988) |
| İki örnek aynı anda tarar, run iki kez kapanır | Fonksiyonel | `SingletonGuard`; ikinci `CompleteRunAsync` zararsız |
| `respond` ile süre dolumu yarışır | Fonksiyonel | 1-B: biri kazanır; 1-A: devam açılır, kaynak kapanır — davranış sabitlenir ve sitede yazılır |
| Kapanan run'ın satırlarını retention bırakmaz | Fonksiyonel (retention turu) | Süre dolar → retention → istek olayı, checkpoint ve girdi satırı silinir |
| Cevaplanmış bekleyen istek `Failed` görünür | Fonksiyonel | Açık Soru 4 kararı sabitlenir |
| Tarama sırasında depo hatası servisi durdurur | Fonksiyonel | Uyarı loglanır, sonraki tur dener (onay servisi emsali) |
| Kapanış yazımı ortam kiracısına yazar | Fonksiyonel | Servis kaydın kiracısıyla yazar (`AmbientTenantScope`) |
| Kapanış sırasında süreç durur | Fonksiyonel | `stoppingToken` iptali; sonraki örnek kalanı kapatır |
| Projeksiyon satırı yazılamaz (1-B) | Fonksiyonel | İstek listede yok ama run başına `/requests` ile cevaplanabilir; uyarı ve metrik |
| Projeksiyon öncesi bekleyen run'lar listede yok (1-B) | Fonksiyonel | Doldurma kararı (Açık Soru 7) sabitlenir |
| Kesik yük (8 KB) listede kimliksiz satır üretir | Fonksiyonel | Kimlik `Text`'ten okunur (K-888); kesik istem işaretli |
| Konsol kaydı süreyi siler | Birim (vitest) | `full-replace-editors.test.tsx` |

Beş soru: **iptal** (tarama ve liste isteği iptali) · **eşzamanlılık** (iki tarayıcı, `respond` ile süre yarışı) · **boş/aşırı girdi** (`take` sınırı, kesik yük, boş kiracı) · **başka kiracı** (liste yalıtımı, servisin kiracıyla yazması) · **alt sistem hatası** (depo hatası, kapanış yazımı, projeksiyon yazımı).

---

## Manuel Kabul Case'leri

> Kapanışta `15-WORKFLOWS.md`, `13-KIRACI-VE-GUVENLIK.md` ve `23-SAKLAMA-ARSIV-KOTA.md` içine eklenir.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulama; üç `summarize-and-approve` run'ı bekliyor, biri cevaplı | `GET /api/workflows/requests` | Üç satır (ya da Açık Soru 2'ye göre mantıksal sayı); cevaplı olan `answered: true` |
| 2 | Aynı | `?state=pending` | İki satır |
| 3 | Konsol, Approvals ekranı | "Workflow requests" bölümü; satıra tıkla | Run'ın istek kartı açılır · 👤 |
| 4 | `RequestTimeout = 1 dk` olan workflow; run bekliyor | 2 dk bekle; `GET /api/runs/{id}` | `Failed`, `error.type = WorkflowRequestExpired` |
| 5 | Aynı | Süresi dolan run'a `respond` | `400` "not awaiting human input"; run açılmaz |
| 6 | Süre dolmuş run; retention açık, kısa ufuk | Retention turu; `GET …/events` ve checkpoint listesi | İstek olayı ve checkpoint'ler silinmiş (MT-RET) |
| 7 | İki kiracı | Beta kiracısı listeyi okur | Alfa'nın istekleri yok (MT-SEC) |

---

## Açık Sorular

> 🔒 = engelleyici, plan onayından önce cevaplanır. Kalanlar uygulama sırasında karara bağlanır.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 🔒 | Liste kaynağı | A: olay taraması (migration yok, K-128 kalır) · B: `workflow_requests` projeksiyonu (K-128 açılır) | §209.1 ölçümü sayıyla önerir. İşlevsel ölçüt: "yalnız bekleyen" süzgeci ve süre indeksi istenirse **B** |
| 2 🔒 | Liste kimliği | A: run başına satır · B: `(oturum, requestId)` başına mantıksal istek, en yeni run | **B** — aynı insan kararı iki satırda görünmez; Faz 208'in iddia anahtarıyla aynı |
| 3 🔒 | Süre dolunca kaynak run | A: `Failed` + `WorkflowRequestExpired` · B: `Canceled` + sebep (K-963) · C: yeni `RunStatus.Expired` | **A** — `ApprovalExpired` emsali; C her `switch`'i ve istemciyi değiştirir |
| 4 🔒 | Cevaplanmış, K-130 ile bekleyen istek | A: süreyle `Completed` kapanır · B: A'daki gibi `Failed` · C: hiç kapanmaz | **A** — cevap verildi; retention yine bırakır ve run "başarısız" görünmez. C, K-888 büyümesini açık bırakır |
| 5 🔒 | Sürenin kaynağı ve başlangıcı | A: workflow başına `RequestTimeout`, `RequestedAt`'ten · B: port başına · C: run kapanışından (`completed_at`) | **A** — tek ayar, kayıtlı sürümden okunur; iki başlangıç pratikte aynı andır, `RequestedAt` istekte zaten var |
| 6 🔒 | Liste yetkisi | A: `Reader` + `RunAccess.Read` · B: `Operator` + `RunAccess.Approval` | **A** — run başına `/requests` Reader'dır; liste aynı bilgiyi toplar. B onay listesiyle tutarlıdır ama bugünkü okuma yetkisini daraltır |
| 7 | 1-B seçilirse geriye dönük doldurma | A: başlangıçta bir kerelik doldurma (olaydan) · B: belgelenir — eski bekleyen run'lar yalnız run başına listede | **B** — preview hattı; doldurma üç dilde SQL JSON ayrıştırma ister |
| 8 | Kutudan satır içi cevap | A: satır run'ın kartına bağlanır · B: kart bileşeni kutuya taşınır | **A** — kart tek yerde kalır; B ileride aday |
| 9 | Süre dolumu webhook'u | A: yok · B: `run.failed` yayınlanır · C: yeni olay | **A** — onay süresi de yayınlamaz; ihtiyaç ölçülmedi |
| 10 | Süre sınırı | A: 1 dk – 365 gün · B: üst sınır yok | **A** — sınırsız süre K-888 büyümesini yeniden açar |
| 11 | 1-B seçilirse projeksiyon satırının ömrü | A: `RetentionTargets.WorkflowRequests` (kapanan satır) · B: satır run'la yaşar | **A** — `runs` satırı retention hedefi değildir; projeksiyon aksi hâlde hiç silinmez |

---

## Bitiş Ölçütleri (DoD)

- [x] §209.1 ölçümü üç sağlayıcıda yapıldı; sayılar ve Açık Soru 1'in kararı "Plandan Sapmalar"da — A olay taraması run başına olay sayısıyla büyür (SQLite 3.3 → 54.1 ms, SQL Server 46.3 → 118.7 ms, PostgreSQL 21.8 → 40.7 ms); B düz kalır; B seçildi
- [x] `GET /api/workflows/requests` kiracının bekleyen isteklerini sayfalı döner; `state=pending` sayfaları doğru toplar; cevaplanan istek `answered: true` — `WorkflowRequestInboxHttpTests` (28), `WorkflowRequestStoreContract.The_state_filter_runs_before_the_page_so_a_page_is_never_short` (4 depo); örnek uygulama: 3 öğe → cevap sonrası `pending` 2, `answered` 1, `take=1&skip=1` 1
- [x] Başka kiracının isteği listede yok; reddeden yetki işleyicisi listeyi `403` ile reddeder — `A_tenant_sees_only_its_own_requests`, `A_handler_that_refuses_reading_runs_rejects_the_list_with_403_and_never_filters_it`, `TenantIsolationContract` (4 depo)
- [x] `RequestTimeout`'u dolan istek kapanır (Açık Soru 3-4 kararıyla); süre dolmuş run'a `respond` `400` — örnek uygulama: cevapsız run vadeden 1.2 sn sonra `Failed` + `WorkflowRequestExpired`, `respond` `400` ve yeni run yok (workflow run sayısı 10 → 10); cevaplı iki run `Completed`, hatasız
- [x] Süre dolan run'ın istek olayı, checkpoint'leri ve girdi satırı bir sonraki retention turunda silinir — `What_a_waiting_run_keeps_is_kept_until_its_request_expires_and_released_after` (olay ve checkpoint; girdi satırı yalnız parametre bildiren workflow'larda vardır, aynı `runs.status` süzgeci `RunInputRetentionTests`'te), `WorkflowRequestRetentionTests` ×3
- [x] K-130 ve K-888 satırları güncellendi; K-128 yeni gerekçeyle açıldı (kategori: kalıcı-veri) — K-1000…K-1005
- [x] Dört doğrulama kapısı sıfır uyarı verir — *Kapanış kapısı* altında
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1, 2, 4, 5, 174), çıktı belgede — *Örnek uygulama koşumu*
- [x] `secret` taraması boş döndü — `kapi.py kapanis`'in ilk komutu (`tarama`), *Kapanış kapısı* altında
- [x] Manuel kabul case'leri `15-WORKFLOWS.md` (`MT-WF-165`…`174`), `13-KIRACI-VE-GUVENLIK.md` (`MT-SEC-222`, `223`), `23-SAKLAMA-ARSIV-KOTA.md` (`MT-RET-085`, `086`) içine eklendi; otomatikleştirilebilenler koşuldu
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı (0 🔴, 5 🟡 kapandı)
- [x] `docs-site/` güncellendi (`concepts/workflow-requests.md` yeni; beş sayfa daha); `npm run check` temiz — *Kapanış kapısı* altında
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı gzip KB olarak ölçüldü: **211.9 KB** (Faz 208 sonu 209.8 KB; pay +2.1 KB; bütçe 250 KB)

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*WorkflowRequestInboxHttpTests" "*WorkflowRequestExpirationHttpTests"

curl -s "http://localhost:5081/tracon/api/workflows/requests?state=pending&take=50" -H "Authorization: Bearer $TOKEN"
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Olay taraması büyük kiracıda yavaşlar | §209.1 ölçümü; sayfa sınırı 200 |
| Projeksiyon olayla ayrışır (K-128'in endişesi) | Tek yazım noktası (`CompleteAsync`, `AwaitingInput`); `respond` kaynağı hâlâ olaydan doğrular; ayrışma testi |
| Süre K-130'u geriye dönük değiştirir | Bilinçli istisna, onay süresi emsali; karar satırı ve site cümlesi |
| Retention kapanan run'ın satırlarını beklenenden erken siler | Retention ufku değişmez; yalnız `AwaitingInput` muafiyeti kalkar — site ve `configuration.md:526-529` cümlesi |
| Servis tüm kiracıları tarar | `pending_approvals` taramasıyla aynı K1 emsali; tur sınırı |
| Faz 208 ile aynı dosyalar | 208 kapanmadan başlamaz; plan `WorkflowRunner`'ı sembolle anar |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULDU — `faz-tamamlama` skill'i.
     ============================================================ -->

## Plandan Sapmalar

> Plan ile gerçek arasındaki fark **gizlenmez** — sonraki oturumun en değerli bilgisidir.
> §209.1'in ölçümü aşağıdadır.

### §209.1 ölçümü (2026-10-14, taban `661618fe`)

Yöntem: 50 bekleyen workflow run'ı tohumlanır (run başına 10 / 100 / 1000 olay: `MessageDelta` yığını + bir `WorkflowRequest` + `RunAwaitingInput`). **A** her run'ın olaylarını okuyup istek olayını süzer (sayfa başına 50 okuma). **B** `workflow_requests` satırlarından bir sayfa (`take = 50`) okur. Her sağlayıcıda gerçek veritabanı. Ölçüm kodu atılmıştır (tek seferlik prob, depoda yoktur).

| Sağlayıcı | Olay / run | A: olay taraması | B: projeksiyon |
|---|---:|---:|---:|
| SQLite | 10 | 3.3 ms | 1.5 ms |
| SQLite | 100 | 10.7 ms | 1.6 ms |
| SQLite | 1000 | 54.1 ms | 1.3 ms |
| PostgreSQL | 10 | 21.8 ms | 19.2 ms |
| PostgreSQL | 100 | 24.7 ms | 17.6 ms |
| PostgreSQL | 1000 | 40.7 ms | 17.0 ms |
| SQL Server | 10 | 46.3 ms | 40.0 ms |
| SQL Server | 100 | 45.3 ms | 34.0 ms |
| SQL Server | 1000 | 118.7 ms | 38.4 ms |

Süre taraması (`ExpireAsync`, 10 000 açık satır): vadesi gelen yokken ortanca tur SQLite 0.1 ms, PostgreSQL 0.4 ms, SQL Server 1.9 ms. Vadesi gelen 100 satırı kapatmak SQLite 7.7 ms, PostgreSQL 1.9 ms, SQL Server 11.9 ms. Örnek uygulamanın `summarize-and-approve` run'ı 28–35 olay yazdı.

**Sonuç:** A'nın maliyeti run başına olay sayısıyla büyür (SQLite 16×, SQL Server 2.6×, PostgreSQL 1.9× 10 → 1000 olay arasında); B düzdür. İşlevsel ölçüt zaten B'yi gerektiriyordu (süzgeç sayfadan önce, süre indeksi). 🔒1 **B** kesinleşti; A'nın sayfa başına 50 ayrı okuması hiçbir sağlayıcıda B'den hızlı çıkmadı.

### Planda olmayan ya da planı değiştiren kararlar

1. **K-128 yeniden açıldı (K-1000).** Plan bunu öngörüyordu; gerekçe: kararın kendi koşulu run başına istek sayısıydı, kiracı geneli liste değildi. İstek yükü (istem, form) olayda **kalır**; satır olayı sırasıyla (`event_seq`) adlandırır ve istemi kopyalamaz — kopya, içerik korumasını atlardı (K-325, K-899).
2. **Depo sözleşmesi plan taslağından farklı.** Taslak `AddAsync` tek kayıt, `ListAsync(query)`, `ExpireAsync(now, max)`, `MarkAnsweredAsync` diyordu. Gerçek: `AddAsync(IReadOnlyList<WorkflowRequestRecord>)` (bir run birkaç istek yayınlar, toplu yazım), **`ListByRunAsync(runId)`** eklendi (run başına `/requests` aynı alanları satırdan doldurur; olay yükü ayrıştırması aynı kalır) ve `ExpireAsync` **kiracıdan bağımsız** tek `UPDATE … RETURNING` / `OUTPUT` döner (`[TenantAgnostic]`; servis her satır için `AmbientTenantScope` açar).
3. **Satır `(run, istek)` başınadır, mantıksal istek başına değil.** Mantıksal istek (`(oturum, requestId)`) listede `NOT EXISTS` ile türer: o isteği tutan en yeni **açık** satır görünür. Yeniden yayınlayan devam run'ı kendi satırını yazar; eski run `AwaitingInput` kaldıkça eski satırı da açık kalır ve liste yalnız en yenisini gösterir. `MarkAnsweredAsync` o isteğin **bütün açık satırlarını** işaretler; kapalı satıra dokunmaz (K-1001).
4. **Satır tek yerde yazılır:** `WorkflowRunner.CompleteAsync`, durum `AwaitingInput` iken (`RecordRequestsAsync`). Yazım **best-effort**: hata uyarı olarak loglanır, run etkilenmez. Plan "uyarı ve metrik" diyordu — **metrik eklenmedi**, yalnız uyarı var. Satırı yazılamayan istek listede yoktur ve **hiç sona ermez**; run'dan cevaplanabilir.
5. **`expires_at` yazım anında dondurulur** (`RequestedAt + RequestTimeout`, kaydedilen tanım sürümünden). Tanım sonradan değişirse bekleyen run'ın süresi değişmez (K-988 emsali, K-1002).
6. **Liste ucu `state`'i dize okur.** Plan `WorkflowRequestState` enum'unu doğrudan bağlıyordu; minimal API'nin enum bağlayıcısı büyük/küçük harfe duyarlıdır (`state=all` `400` verdi). Uç `Enum.TryParse(ignoreCase)` kullanır; bilinmeyen değer `400`. `WorkflowRequestState`'te `JsonConverter` yok (sorgu parametresidir).
7. **`requests` ayrılmış workflow adı** (`functions` gibi): rota `/api/workflows/{name}`'den önce eşleşir, bu adla kaydedilen workflow okunamazdı. `PUT` `400` (K-1005). Plan bunu görmemişti.
8. **`ApprovalExpirationService` kusuru bulundu ve giderildi** (aşağıda, *Süre servisinin emsali*).
9. **Süre servisi emsalinden üç yerde ayrılır.** (a) Run'ı kapatırken `CompleteRunAsync` `EventCount`, `Usage`, `Cost` ve `NonTokenCost`'u **değiştirdiği** için önce run'ı okuyup değerleri geri yazar. (b) Satırı kapatıp run'ı kapatamadığında (depo hatası) run'ı **bellekte** bir yeniden deneme listesinde tutar (en çok 10 tur). (c) 🚨 **Süreç iki yazımın arasında durursa** (satır kapalı, run kapanmamış) kapanmış satır `ExpireAsync` ile bir daha bulunmaz; ilk uygulama bunu "belgeli sınır" bıraktı ve bağımsız denetim bunun planın "sonraki örnek kalanı kapatır" cümlesini karşılamadığını gösterdi (🟡1). Düzeltme: `IWorkflowRequestStore.ListStrandedAsync` (altıncı üye) ve servisin lease'i aldığında bir kez koşan taraması (aşağıda *Denetim sonrası eklenenler*).
10. **Yalnız `AwaitingInput` run kapatılır.** Servis run okur; durum başkaysa dokunmaz (cevap kazandıysa ya da run silindiyse).
11. **Geriye dönük doldurma yok** (Açık Soru 7-B). Satırdan önce `AwaitingInput`'a giren run'lar listede görünmez ve süresiz bekler; run başına `/requests` çalışır. Sitede ve `CHANGELOG`'da yazılı.
12. **`IWorkflowRunner.ListRequestsAsync` varsayılan uygulaması `[]`** (plan gibi); `WorkflowRunner` gerçeği döner. Run başına `/requests` ve kiracı listesi aynı `WorkflowRunner.Enrich` adımından geçer. Sayfa başına olay okuması **N+1** kaldı (satır olayı `event_seq` ile adlandırır ama olay tek tek okunur): sayfa en çok 200 öğe ile sınırlı; toplu okuma ADAYLAR'a girdi.
13. **`WorkflowRegistrationOptions.RequestTimeout`, `WorkflowDefinition.RequestTimeout`, `WorkflowDescriptor.RequestTimeout`, `WorkflowSaveRequest.RequestTimeout`** — plan `WorkflowDescriptor`'ı anmıyordu; katalog bunu da taşır ki tüketici süreyi görsün. Sürüm karşılaştırması (`versions.tsx`) yeni bir etiket kazandı.
14. **Retention hedefi `workflow_requests`:** yalnız kapalı satırlar `closed_at`'e göre silinir; açık satır asla silinmez ve `MaxRows`'a sayılmaz; varsayılan 30 gün. Plan "kapanan satır" diyordu (Açık Soru 11-A); ayrıntı K-1004.
15. **Örnek uygulamaya `summarize-and-approve-timed` eklendi** (2 dakikalık `RequestTimeout`); manuel case 4 için gerekliydi. E2E sunucusunun `approval-flow` grafı 2 günlük süre aldı (konsol panelinde "expires" sütunu görünsün).
16. **E2E ve ekran görüntüsü işi plandan büyüktü.** Konsol paneli test kimliği panelin tamamını sarar (araç çubuğu değil); `DocumentationScreenshotTests` bekleyen bir workflow isteği tohumlar; 45 ekran görüntüsü ve `.ui-source.sha256` yenilendi. Site sayfası `workflows.md` gzip tavanına (59 000 B) 58 983 B ile yakın: yeni anlatı `concepts/workflow-requests.md`'ye konuldu, tavan yükseltilmedi.
17. **Migration numaraları:** PostgreSQL `0061`, SQL Server `0049`, SQLite `0048`. `scripts/applied-migrations.json` ankrajı ikinci commit'te (`327de791`) yazıldı. Geliştirici PostgreSQL veritabanı (`agentprism`) örnek uygulamanın ilk koşusunda `0060` ve `0061`'i uyguladı; tekrar uygulanmaz, geri alma yok.

### Denetim sonrası eklenenler (2026-10-14)

Bağımsız denetimin 🟡'leri (aşağıda *Denetim Bulguları*) şunları ekledi; hepsi planda yoktu.

- **`IWorkflowRequestStore.ListStrandedAsync(max)`** — kiracıdan bağımsız (`[TenantAgnostic]`); kapalı satırı olan, açık satırı kalmamış ve `runs.status = AwaitingInput` run'ın kapalı satırlarını döner. Tek `SELECT … JOIN runs … NOT EXISTS`, üç sağlayıcıda aynı metin (yalnız satır sınırı sözdizimi: `LIMIT` / `TOP`). Bellek içi depo boş döner. Migration gerekmedi: sorgu lease alındığında bir kez koşar ve kapalı satırları (en çok 30 gün yaşar) tarar.
- **Servis taraması:** lease alındığında bir kez bakar; tam tur (`MaxRequestExpirationsPerScan` satır) dönerse sonraki turlarda yine bakar; bulduğu run'ları yeniden deneme listesine ekler ve aynı yolla kapatır (`Failed`/`Completed` ayrımı, sayaçlar korunur). Lease kaybolunca bayrak sıfırlanır.
- **`WorkflowRequestStoreContract` +3 case** (dört depoda; `SetRunStatusAsync` kancası: SQL'de run'ı `AwaitingInput`/`Failed` yapar, bellek içinde `false`). **`WorkflowDefinitionStoreContract` +1 case** (`The_request_timeout_survives_the_round_trip`). İkisi de sevk edilen sözleşme: yayın provası *Kapanış kapısı*nda.
- **`?state=` yalnız bir ad kabul eder** (`1`, `pending,answered` artık `400`); denetim 🟢1.
- **Testler:** `WorkflowRequestExpirationServiceTests` +6, `ApprovalExpirationServiceTests` +1 (kiracılı), `WorkflowRequestExpirationHttpTests` +2 (iki SQLite host'u art arda: kurtarma ve cevap-süre penceresi), `WorkflowRequestInboxHttpTests` `state` Theory'leri.
- **Sitede** *An answer that arrives with the time limit* bölümü ve süreç durması cümlesi; **K-1003** satırı ve geçmişi güncellendi; **MT-WF-174** (örnek uygulamada ölçüldü).

### Süre servisinin emsali — bulunan kusur (giderildi)

Plan `ApprovalExpirationService`'i "birebir desen" olarak gösterdi. Desen okunurken şu kusur çıktı: süresi dolan onayın run'ı `CompleteRunAsync` ile kapatılırken run'ın **`EventCount`, `Usage`, `Cost` ve `NonTokenCost` değerleri sıfırlanıyordu** (`CompleteRunAsync` bu kolonları değiştirir, servis önce okumuyordu). Kapanan her süresi dolmuş onay, run'ın olay sayısını ve maliyetini siler; panolar ve stat'lar bunu gösterirdi. Önce kırmızı test (`ApprovalExpirationServiceTests`, 3 case) yazıldı, düştüğü görüldü, servis run'ı okuyup değerleri geri yazacak biçimde düzeltildi (`74be3ae5`, `CHANGELOG` *Fixed*). Süre servisi aynı deseni baştan doğru kurdu.

### Örnek uygulama koşumu (2026-10-14)

`samples/Tracon.Api`, geçici SQLite deposu (`Tracon__Sqlite__ConnectionString` scratchpad'e), port 5081, `Tracon__Ui__AuthToken` tek kullanımlık, gerçek model çağrısı. Açılışta 48 SQLite migration uygulandı (`0048_workflow_requests` dahil). İlk deneme ortak PostgreSQL'e bağlandı (kullanıcı sırları) ve jeton istedi — durdurulup geçici SQLite ile yeniden açıldı; ortak veritabanına yalnız yukarıdaki iki migration uygulanmıştı.

| Manuel case | Gözlem |
|---|---|
| 1 — üç bekleyen run, biri cevaplı | `GET /api/workflows/requests?state=all` → 3 öğe (üç ayrı oturum, üç mantıksal istek); cevap sonrası `answered: true`, `lastResponseRunId` dolu |
| 2 — `state=pending` (varsayılan) | 2 öğe; `state=answered` → 1 öğe; `take=1&skip=1` → 1 öğe |
| 4 — `summarize-and-approve-timed` (2 dk), cevapsız üçüncü run | `requestedAt 19:22:12.25` → `expiresAt 19:24:12.25` (tam 2 dk). Cevapsız run `Failed` oldu: `completedAt 19:26:15.09` (süre 19:26:13.88, 1.2 sn sonra), `eventCount 32`, `error.type = WorkflowRequestExpired`, `error.class = Timeout`, ileti istek kimliğini ve vadeyi söyler |
| Cevaplanmış iki zamanlı run | `Completed` (`error = null`), `completedAt` vadeden 2–3 sn sonra; ilk run'ın `eventCount`'u kapanıştan önce de sonra da 35 — süre kapanışı olay sayısını silmedi |
| 5 — süresi dolan run'a `respond` | `400` `Run '…' is not awaiting human input (status: Failed)`; workflow run sayısı önce/sonra 10/10 (yeni run açılmadı); kiracı listesi 3 öğeye indi, dolan run listede yok |
| Tanım sınırları | `requestTimeout 00:00:30` → `400`; `400.00:00:00` → `400`; `06:00:00` → `200` (`GET` aynısını döner, sürüm 1); süresiz kayıt → `requestTimeout: null`, sürüm 2 |
| Ayrılmış ad | `PUT requests` / `Requests` / `functions` → `400`, ileti adın neden ayrıldığını söyler |
| Hata yolları | `state=waiting` → `400`; jetonsuz → `401` |
| 174 — süreç iki yazımın arasında durdu | Tarama saatte bire ayarlı örnek; zamanlı run `AwaitingInput` (`eventCount 38`). Uygulama durduruldu, `workflow_requests` satırı `sqlite3` ile `status = 1, closed_at = şimdi` yapıldı (run hâlâ `AwaitingInput`, liste boş). Yeniden başlatınca günlükte `1 workflow request rows were closed but their runs still wait; closing the runs.` ve run `Failed`, `error.type = WorkflowRequestExpired`, `eventCount` 38 (korundu) |

Manuel case 3 (konsol, satıra tıkla) ve 6/7 ayrı yerlerde kanıtlandı: 3 → E2E `ApprovalTests` + ekran görüntüsü (👤 göz denetimi kaldı: `MT-WF-166`), 6 → `WorkflowRequestExpirationHttpTests` + üç sağlayıcıda `WorkflowRequestRetentionTests`, 7 → `TenantIsolationContract` + `WorkflowRequestInboxHttpTests` iki kiracı.

### Önce kırmızı kaydı (dürüst)

| Test | Nasıl kırmızı görüldü |
|---|---|
| `ApprovalExpirationServiceTests` (3 case) | Gerçek kırmızı: onay süresi dolunca run'ın olay sayısı, kullanımı ve maliyeti siliniyordu; düzeltmeden önce düştü |
| `WorkflowRequestStoreContract` | Gerçek kırmızı iki kez, ilk koşumda: SQLite `RETURNING w.run_id` takma adı bulamadı (`no such column`); `RETURNING` sırası tanımsız olduğundan sıra iddiası düştü → küme karşılaştırması |
| `WorkflowRequestInboxHttpTests` — `state=all` | Gerçek kırmızı: `400` (minimal API enum bağlayıcısı büyük/küçük harfe duyarlı); uç `state`'i dize okuyup `Enum.TryParse(ignoreCase)` ile ayrıştırır |
| Denetim sonrası eklenenler: iki kurtarma, tek arama, başarısız arama (`WorkflowRequestExpirationServiceTests`), iki restart testi (`WorkflowRequestExpirationHttpTests`) | **Mutasyonla görüldü:** kurtarma taraması devre dışı → servis testlerinden dördü, HTTP testlerinden ikisi kırmızı; geri alındı |
| `The_request_timeout_survives_the_round_trip` | Mutasyonla görüldü: `WorkflowDefinitionPayload.ToDefinition` süreyi `null` yazınca SQLite'ta kırmızı; geri alındı |
| `ApprovalExpirationServiceTests` kiracılı case | Mutasyonla görüldü: `AmbientTenantScope.Begin` satırı kaldırılınca kırmızı; geri alındı |
| Mimari kapılar, taban çizgileri, SQL metin anlık görüntüsü, kapsama listeleri (yedi kapı) | Yeni yüzey önce kırmızı yaptı, sonra güncellendi |
| Kalan yeni sınıflar (`WorkflowRequestInboxTests`, `WorkflowRequestTimeoutValidationTests`, `WorkflowRequestInboxHttpTests`, üç `WorkflowRequestRetentionTests`, `workflow-requests.test.tsx`) | Önce kırmızı kaydı tutulmadı; kod ve test birlikte yazıldı. Mutasyonla sınanmadı |

### Kapanış kapısı (2026-10-14)

*(kapıların sonucu aşağıya kapanışta yazılır)*

## Bu Fazda Verilen Kararlar

| Karar | Kayıt |
|---|---|
| `workflow_requests` projeksiyonu açıldı; istek yükü olayda kalır, satır onu `event_seq` ile adlandırır; yazım `CompleteAsync`'te, best-effort; geriye dönük doldurma yok. K-128 yeniden açıldı (kullanıcı kararı) | **K-1000** *(kalıcı-veri, public-api)*; K-128 satırı yerinde annotated |
| Liste `(oturum, requestId)` başına bir mantıksal istektir; süzgeç sayfadan önce; "yanıtlandı" `MarkAnsweredAsync` ile; sıra `RequestedAt` (kullanıcı kararı) | **K-1001** *(public-api)* |
| Workflow başına `RequestTimeout` (1 dk – 365 gün), `RequestedAt`'ten sayılır, yazım anında `expires_at`'e sabitlenir (kullanıcı kararı) | **K-1002** *(public-api)* |
| Süre dolunca kapanış: cevapsız istek → `Failed` + `WorkflowRequestExpired` (`Timeout`); hepsi cevaplı → `Completed`, hatasız; K-130 istisnası (kullanıcı kararı) | **K-1003** *(kalıcı-veri, geri-dönüşü-pahalı)*; K-130 ve K-888 satırları işaretli |
| `RetentionTargets.WorkflowRequests`: yalnız kapalı satır, `closed_at`'e göre, 30 gün; K-888 muafiyetleri run kapanınca kendiliğinden serbest kalır | **K-1004** *(kalıcı-veri)* |
| Liste yetkisi `Reader` + `RunAccess.Read`; reddeden işleyici listeyi `403` ile reddeder; `requests` ayrılmış ad (kullanıcı kararı) | **K-1005** *(güvenlik, public-api)* |
| Kapatılamayan run bellekte en çok 10 tur denenir; süreç iki yazımın arasında durursa lease'i alan örnek `ListStrandedAsync` ile bulup kapatır (bağımsız denetim 🟡1 sonrası) | K-1003 içinde (satırı ve geçmişi güncellendi) |
| Kategori dışı: satır yazımı başarısız olursa yalnız uyarı loglanır, metrik yoktur | Yalnız burada (planda "uyarı ve metrik" idi) |
| Kategori dışı: sayfa başına olay okuması N+1 kalır; sayfa en çok 200 | Yalnız burada; ADAYLAR'a aday |
| Kategori dışı: `state` sorgu parametresi dize okunur (minimal API enum bağlayıcısı büyük/küçük harfe duyarlı) | Yalnız burada |

## Gerçekleşen Public API

`PublicAPI.Unshipped.txt` ile birebir (127 satır, beş dosya: Abstractions, Core, Workflows, AspNetCore, Testing.Contracts.Xunit). Taslaktan farklar: `IWorkflowRequestStore` dört yerine **altı** üye (`ListByRunAsync` ve `ListStrandedAsync` eklendi), `AddAsync` toplu, `MarkAnsweredAsync` etkilenen satır sayısını (`int`) döner, `WorkflowDescriptor.RequestTimeout` ve `RetentionTargets.WorkflowRequests` / `TraconRetentionOptions.WorkflowRequests` eklendi.

```csharp
// Tracon.Abstractions
public sealed record WorkflowDefinition { public TimeSpan? RequestTimeout { get; init; } }
public sealed record WorkflowDescriptor  { public TimeSpan? RequestTimeout { get; init; } }

public sealed record WorkflowPendingRequest
{
    // mevcut üyeler +
    public string? WorkflowName { get; init; }
    public string? SessionId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public bool Answered { get; init; }
    public Guid? LastResponseRunId { get; init; }
}

public enum WorkflowRequestState { Pending = 0, Answered = 1, All = 2 }
public enum WorkflowRequestStatus { Open = 0, Closed = 1 }

public sealed record WorkflowRequestQuery
{
    public const int MaxTake = 200;
    public WorkflowRequestState State { get; init; }
    public string? WorkflowName { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}

public sealed record WorkflowRequestRecord
{
    public Guid RunId { get; init; }            public string RequestId { get; init; }
    public string SessionId { get; init; }      public string WorkflowName { get; init; }
    public string PortId { get; init; }         public long EventSequence { get; init; }
    public DateTimeOffset RequestedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public WorkflowRequestStatus Status { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }
    public Guid? LastResponseRunId { get; init; }
    public DateTimeOffset? ClosedAt { get; init; }
    public string? TenantId { get; init; }
}

public interface IWorkflowRequestStore
{
    ValueTask AddAsync(IReadOnlyList<WorkflowRequestRecord> records, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListAsync(WorkflowRequestQuery query, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListByRunAsync(Guid runId, CancellationToken cancellationToken = default);
    ValueTask<int> MarkAnsweredAsync(string sessionId, string requestId, Guid responseRunId, DateTimeOffset answeredAt, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ExpireAsync(DateTimeOffset now, int max, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<WorkflowRequestRecord>> ListStrandedAsync(int max, CancellationToken cancellationToken = default);   // kapalı satırı olup run'ı hâlâ bekleyen
}

public interface IWorkflowRunner
{
    ValueTask<IReadOnlyList<WorkflowPendingRequest>> ListRequestsAsync(
        WorkflowRequestQuery query, CancellationToken cancellationToken = default)
        => ValueTask.FromResult<IReadOnlyList<WorkflowPendingRequest>>([]);   // varsayılan uygulama
}

public static class RetentionTargets { public const string WorkflowRequests = "workflow_requests"; }

// Tracon.Core
public sealed class TraconRetentionOptions { public RetentionTargetOptions WorkflowRequests { get; } }   // MaxAgeDays = 30

// Tracon.Workflows
public sealed class WorkflowRegistrationOptions { public TimeSpan? RequestTimeout { get; set; } }
public sealed class TraconWorkflowOptions
{
    public TimeSpan RequestExpirationScanInterval { get; set; }   // 1 dk
    public int MaxRequestExpirationsPerScan { get; set; }         // 100
}

// Tracon.AspNetCore
public sealed record WorkflowSaveRequest { public TimeSpan? RequestTimeout { get; init; } }

// Tracon.Testing.Contracts.Xunit
public abstract class WorkflowRequestStoreContract : /* TenantIsolationContract */ { /* 26 case; SetRunStatusAsync kancası */ }
// WorkflowDefinitionStoreContract: +The_request_timeout_survives_the_round_trip
```

HTTP: `GET /api/workflows/requests` (yeni, `Reader`, `RunAccess.Read`); `GET /api/workflows/runs/{runId}/requests` öğeleri beş yeni alan taşır; `PUT /api/workflows/{name}` `requestTimeout` alır; `GET /api/workflows` ve `GET /api/workflows/{name}` `requestTimeout` döner. İşlem sayısı 175 → **176**, yol sayısı 137 → **138**. Üretilen TS ve .NET istemcileri yenilendi.

## Dosya Listesi (gerçekleşen)

```
src/Tracon.Abstractions/Workflows/
├── IWorkflowRequestStore.cs, WorkflowRequestQuery.cs              (yeni)
└── IWorkflowRunner.cs, WorkflowDefinition.cs, WorkflowDescriptor.cs, WorkflowPendingRequest.cs
src/Tracon.Abstractions/Retention/RetentionTargets.cs
src/Tracon.Core/
├── Workflows/{InMemoryWorkflowRequestStore, WorkflowRequestExpiry}.cs (yeni) · WorkflowDefinitionValidator.cs
├── Approvals/ApprovalExpirationService.cs                         (kusur düzeltmesi; TickAsync internal)
├── Runs/DefaultRunErrorClassifier.cs, Retention/TraconRetentionOptions.cs
└── TraconServiceCollectionExtensions.{Binding.Security,Registration.Storage}.cs
src/Tracon.Sql.Shared/{Stores/SqlWorkflowRequestStore.cs (yeni), Internal/{RetentionTargetRegistry,SqlQueriesBase,WorkflowDefinitionPayload}.cs}
src/Tracon.{PostgreSql 0061, SqlServer 0049, Sqlite 0048}/Migrations/*_workflow_requests.sql     (yeni) · Internal/*Queries.cs · builder uzantıları
src/Tracon.Workflows/Internal/{WorkflowRequestExpirationService (yeni), WorkflowRunner, CodeWorkflowRegistration, WorkflowCatalog, TraconWorkflowOptionsValidator}.cs
src/Tracon.Workflows/{TraconWorkflowOptions, WorkflowRegistrationOptions, TraconWorkflowsBuilderExtensions}.cs
src/Tracon.AspNetCore/{Endpoints/WorkflowEndpoints.cs, Contracts/WorkflowContracts.cs, Internal/WorkflowResponseGate.cs}
src/Tracon.Testing.Contracts.Xunit/Contracts/WorkflowRequestStoreContract.cs    (yeni; sevk edilen sözleşme)
src/Tracon.UI/frontend/src/screens/{approvals.tsx, approvals/workflow-requests.tsx (yeni), workflow-editor.tsx, workflow-detail/versions.tsx} · locales/{en,tr}/{operations,workflows}.ts · test/api-fixtures.ts
src/Tracon.Client/Generated/TraconApiClient.g.cs · packages/tracon-client/src/schema.ts · docs/openapi/tracon.json
samples/Tracon.Api/Program.cs                                                  (summarize-and-approve-timed)
tests/                                                                         (aşağıda)
docs-site/ (concepts/workflow-requests.md yeni; workflows, configuration, ui, capabilities, index, getting-started, http-api, compatibility; llms*.txt; 45 ekran görüntüsü)
docs/ (KARARLAR K-1000…K-1005, KARARLAR-GECMISI, hafiza/{workflow-cevaplari,kod-haritasi}, manuel-test/{15,13,23,00}, ADAYLAR) · CHANGELOG.md · src/*/README.md
```

### Testler

| Sınıf | Seviye | Case | Ne doğrular |
|---|---|---:|---|
| `WorkflowRequestStoreContract` (bellek içi, SQLite, PostgreSQL, SQL Server) | Sözleşme | 26 × 4 depo | Yazım ve geri okuma, mantıksal istek (en yeni açık satır), süzgeç sayfadan önce, sıra, aralık dışı sayfa, kiracı yalıtımı, `MarkAnsweredAsync` (ikinci cevap, başka kiracı), `ExpireAsync` (vade, sıra ve `max`, tek kapanış, eşzamanlı çift, çok kiracılı), `ListStrandedAsync` (üç case), veri sahibi silmesi |
| `WorkflowRequestInboxTests` | Birim (gerçek runner) | 11 | Satır ve vade yazımı, süresiz istek, beklemeyen run satır yazmaz, kayıtlı tanımın süresi, bekleme sırasındaki düzenleme vadeyi oynatmaz, iki run'ın aynı isteği tek satır, istem ve form olaydan okunur, olayı silinmiş satır, yazılamayan satır run'ı bozmaz, okunamayan kutu run başına listeyi durdurmaz, kutusuz runner |
| `WorkflowRequestExpirationServiceTests` | Birim | 16 | Vade → `Failed` + hata, vadeden önce dokunmaz, hepsi cevaplı → `Completed` hatasız, biri cevaplı biri değil → `Failed`, olay sayısı ve kullanım/maliyet/`NonTokenCost` korunur, son istek dolunca kapanır, beklemeyen run'a dokunulmaz, kapanış hatası sonraki turda tekrar denenir ve 10 denemeden sonra vazgeçilir, iki kurtarma (`Failed`/`Completed`), arama bir kez yapılır, başarısız arama süreyi durdurmaz, boş tur, ulaşılamayan kutu servisi düşürmez |
| `WorkflowRequestTimeoutValidationTests` | Birim | 10 (+17 satır) | Tarama aralığı ve tur sınırı doğrulaması, varsayılanlar ve kayıt, tanım sınırı (1 dk – 365 gün), süresiz tanım, kod kaydı, katalog, ayrılmış rota adları |
| `ApprovalExpirationServiceTests` | Birim | 4 | Süresi dolan onay run'ın kaydını silmez (bulunan kusur), süresi dolmamış onaya dokunulmaz, başka yerde kapanmış run'a dokunulmaz, kiracının run'ı istek dışı taramada da kapanır |
| `WorkflowRequestInboxHttpTests` | Fonksiyonel (HTTP) | 28 (Theory satırları dahil) | Liste ve vade, run başına aynı alanlar, cevap işareti ve `state`, süzgeç sayfadan önce, sıra, `take` aralığı, `workflowName`, `state` yalnız ad (`1`, `pending,answered` → `400`), harf duyarsız, iki kiracı, reddeden işleyici `403` (süzmez), motorsuz kurulum `501`, ayrılmış ad, `PUT` süre doğrulaması, kayıtlı workflow'un vadesi, kutu işaretlenemese de cevap çalışır |
| `WorkflowRequestExpirationHttpTests` | Fonksiyonel (HTTP + gerçek servis, oynatılan saat) | 8 | Kapanış ve cevap reddi (`400`, yeni run yok), süresiz istek hiç dolmaz, cevaplı istek `Completed`, kiracı başına kapanış, kaydedilen sürümün vadesi, bekleyen run'ın tuttukları süre dolana kadar kalır ve sonra retention'a serbest kalır; **iki SQLite host'u art arda:** süreç durmasından kalan run kapanır, süre ile cevabın penceresi |
| `WorkflowRequestRetentionTests` ×3 (SQLite, PostgreSQL, SQL Server) | Entegrasyon | 3 × 3 | Kapalı satır yaşa göre gider, açık satır asla; kesim sonrası kapanan kalır; açık satırlar satır sınırına sayılmaz |
| `RetentionArchiveKeyTests` ×3 | Entegrasyon | `request_id` eşlemesi | Arşiv anahtarı `workflow_requests` için çözülür |
| `workflow-requests.test.tsx` · `full-replace-editors.test.tsx` · `workflow-editor.test.tsx` | Vitest | 7 + editör case'leri | Panel, süzgeç, durum, vade; editör `requestTimeout`'u taşır, kayıt süreyi silmez |
| `ApprovalTests` (yeni case) · `DocumentationScreenshotTests` | E2E (Playwright) | 1 + ekran görüntüsü | "Workflow requests" paneli gerçek tarayıcıda, satır run'ın kartına gider |

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 0 (altı 🔒 cevabı belge başlığına işlendi; plan gövdesi yeniden yazılmadı, sapmalar *Plandan Sapmalar*'da) |
| Düzeltme turu sayısı | 1 (bağımsız denetimin beş 🟡'sinin ve bir 🟢'sinin düzeltmesi; kapılar yeniden koşuldu) |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 0 / 0 / 0 (denetim 🔴 bulmadı; beş 🟡'nin dördü gerçek, biri belgelenen test boşluğu — hepsi kapandı) |
| Fazın ürettiği regresyon | 0 (mevcut davranışı bozan bulunmadı). Bulunan **mevcut** kusur: `ApprovalExpirationService` süresi dolan onayın run sayaçlarını siliyordu — giderildi |
| Faz kapandıktan sonra bulunan kusur | 0 (kapanış anı; sonraki `kusur-giderme` koşumu günceller) |

## Denetim Bulguları

Bağımsız `faz-denetim` (`faz-denetcisi`, salt-okunur), 2026-10-14: **0 🔴**, 5 🟡, 7 🟢. Denetçi kiracı sınırını, yetki kapısını, üç sağlayıcının migration ve sorgu eşdeğerliğini, retention muafiyetlerini, uyumluluğu ve `K-1000…K-1005` ile kodun çelişmediğini kanıtla doğruladı.

| Bulgu | Seviye | Triyaj | Sonuç |
|---|---|---|---|
| Satır kapanır, sonra run kapanır; süreç arada durursa run `AwaitingInput` kalır, kapalı satır bir daha bulunmaz (planın "sonraki örnek kalanı kapatır" cümlesi uygulanmamış; K-1003 sınırı kullanıcıya sorulmadan "kullanıcı kararı" altında) | 🟡1 | gerçek | **Düzeltildi:** `IWorkflowRequestStore.ListStrandedAsync` + servisin lease'te koşan taraması; dört depoda sözleşme case'i, dört servis testi, iki uçtan uca restart testi (mutasyonla görüldü); K-1003 satırı düzeltildi. *Plandan Sapmalar* 9(c) |
| Cevap ile süre dolumu yarışı test edilmemiş ve sitede yazılmamış | 🟡2 | gerçek | **Düzeltildi:** akışlı yol için pencere testi (satır kapalı, run bekliyor → cevap alınır, işi koşar, işaret 0 satır değiştirir, run sonra `Failed` + `WorkflowRequestExpired`); sitede *An answer that arrives with the time limit*. Kuyruklu yol testle sabitlenmedi: iş süre dolduktan sonra başlarsa `WorkflowRunJobHandler` kaynağı reddeden isteği kendi run'ını `Failed` kapatır (kod okuması, `RefuseAsync`); sitede yazılı |
| `RequestTimeout`'un `WorkflowDefinitionPayload` gidiş-dönüşü SQL depolarda sözleşmeyle sabit değil | 🟡3 | gerçek | **Düzeltildi:** `The_request_timeout_survives_the_round_trip` (`GetAsync`, `ListAsync`, `GetVersionAsync`, `RollbackAsync`); payload'ı `null` yazınca kırmızı (mutasyon). Sevk edilen sözleşme genişledi → yayın provası |
| Workflow süre servisinin testi yalnız `EventCount`'u sabitliyor; `Usage`/`Cost`/`NonTokenCost` değil | 🟡4 | gerçek | **Düzeltildi:** `The_usage_cost_and_non_token_cost_of_the_run_survive_the_close` |
| `ApprovalExpirationService` ortam kiracısına bağımlı (onay düzeltmesinde doğdu), kiracılı test yok | 🟡5 | gerçek | **Düzeltildi:** `The_run_of_a_tenant_is_closed_although_the_scan_runs_in_no_tenants_request`; kapsam satırı kaldırılınca kırmızı (mutasyon) |
| `?state=` ayrıştırması gevşek: `1`, `pending,answered`, `pending,all` kabul edilir; site "üçünden biri değilse `400`" der | 🟢1 | gerçek | **Düzeltildi** (ucuzdu): yalnız ad kabul edilir; `A_state_that_is_not_one_of_the_three_names_is_refused` (5 satır) |
| Süre kapanışı normal kapanışın `KeepCheckpointsAfterCompletion = false` temizliğini ve `RecordRun` metriğini atlar | 🟢2 | gerçek, düşük | Devredildi: **F-532** (onay emsali aynı; retention yolu açık) |
| `What_a_waiting_run_keeps…` yalnız `RunEvents` ve `WorkflowCheckpoints`'ı sınar; `RunInputs` sınanmaz | 🟢3 | gerçek, düşük | **Gerekçelendi:** denendi, `run_inputs` satırı yalnız parametre bildiren workflow'lar için yazılır (`BindParametersAsync`), testin graflarında satır yok. Filtre aynı `runs.status` koşuludur ve `RunInputRetentionTests` `AwaitingInput` muafiyetini ayrı kanıtlar |
| §209.1 ölçüm düzeneği depoda yok; sayılar yeniden üretilemez | 🟢4 | gerçek | **Gerekçelendi:** tek seferlik prob atıldı; yöntem *Plandan Sapmalar*'da yazılı (50 run, 10/100/1000 olay, A ve B). Kalıcı kapasite düzeneği kapsam dışı |
| Kısmen dolmuş run'da kapanmış isteğe cevap alınır (kapı satır durumuna bakmaz; işaret kapalı satıra yazılmaz) | 🟢5 | gerçek, dar pencere | Devredildi: **F-533** |
| `MaxCloseAttempts` vazgeçme dalı ve iptal yolu testsiz | 🟢6 | gerçek | Vazgeçme dalı **eklendi** (`A_run_that_cannot_be_closed_is_given_up_after_ten_scans_and_logged_as_an_error`), ayrıca kurtarma taraması bu run'ı bir sonraki lease'te yeniden dener. İptal yolu testsiz kaldı: servis iptalde dönen tek `return`'dür |
| `--site-denetle` ❌: `kalicilik` (`PostgresQueries.cs`), `paket-readme` (`Tracon.AspNetCore/README.md`) | 🟢7 | gürültü | **Gerekçelendi** (aşağıda *Site gerekçesi*) |

### Site gerekçesi (`--site-gerekce-yazildi`)

- `kalicilik` → `getting-started/persistence.md`: migration yalnız yeni tablo `workflow_requests` ekler. Sayfa tablo listesi ya da tek yönlü migration örneği taşımaz, retention'dan "run olayları 30 gün, span'ler 14 gün gibi" örnekle söz eder. Tablonun ve hedefin kuralları `concepts/workflow-requests.md` ve `reference/configuration.md`'dedir (ikisi de güncellendi).
- `paket-readme` → `packages.md`: `Tracon.AspNetCore` README'sinde yalnız işlem sayısı değişti (175 → 176, 137 → 138 yol); `packages.md` sayı taşımaz.

## Sonraki Faza Devir Notu

Sıradaki faz seçilmedi; aday sırası [`ADAYLAR.md`](../../../ADAYLAR.md)'dedir (Faz 209 için F-319 satırı çıkarıldı). Bu faz şunları bıraktı:

- **Devralınan sözleşmeler:** yukarıdaki *Gerçekleşen Public API*. `IWorkflowRequestStore` altı üyeli; yeni bir depo uygulayan `WorkflowRequestStoreContract`'ı geçmelidir (`SetRunStatusAsync` kancasını run tablosu varsa uygulamalıdır). Satır tek yerde yazılır: `WorkflowRunner.CompleteAsync` → `RecordRequestsAsync`.
- 🚨 **Bilinen tuzaklar** (ayrıntı [`hafiza/workflow-cevaplari.md`](../../../hafiza/workflow-cevaplari.md)): `CompleteRunAsync` sayaçları **değiştirir** (kapatırken önce oku, geri yaz); arka plan servisi kiracısız koşar (`AmbientTenantScope.Begin`, test host'unda sına); "satırı kapat sonra bir şey yap" adımının çökme boşluğu `ListStrandedAsync`'le kapatıldı; `UPDATE … RETURNING` sırası tanımsızdır; minimal API enum bağlayıcısı harf duyarlıdır.
- 🚨 **Uygulanmış migration'lar değişmez:** PostgreSQL `0061`, SQL Server `0049`, SQLite `0048` (`applied-migrations.json` ankrajlı). Yeni sütun/indeks yeni migration ister.
- **Açık uçlar (ADAYLAR):** F-532 (süre kapanışı normal kapanış temizliğini atlar), F-533 (kapanmış isteğe cevap), F-534 (sayfa başına olay okuması), F-535 (satırı olmayan bekleyen run için uzlaştırma), F-518 (`workflow_responses` büyümesi; `workflow_requests` için kalıp hazır, K-1004).
- **Sınırlar (kullanıcıya söylenmiştir):** Faz 209 öncesi bekleyen run'lar listede yok ve dolmaz (geriye dönük doldurma yok); satırı yazılamayan istek listede yok ve dolmaz; süre ile cevap arasında iki yazımlık (ms) pencere vardır ve davranışı sabit; webhook yayınlanmaz.
- **El ile kalan:** `MT-WF-166` 👤 (konsol, göz denetimi; Vitest ve E2E karşılığı var). `MT-WF-172`, `173`, `MT-SEC-222`, `223` ☐ (➜ CI: otomatik karşılıkları var).
- **Geliştirici ortamı notu:** örnek uygulama kullanıcı sırlarıyla ortak PostgreSQL'e bağlanır; ilk koşuda yerel `agentprism` şeması `0060` ve `0061`'i aldı (geri alma yok). SQLite için `Tracon__PostgreSql__ConnectionString=""` ver.
