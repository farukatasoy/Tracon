# Faz 209 — Workflow Gelen Kutusu ve İstek Süresi

> **Durum:** 📋 Planlandı (2026-10-14)
> **Plan onayı:** onaylanmadı — cevaplanmamış engelleyici sorular (Açık Sorular 1-6, 🔒): (1) liste kaynağı olay taraması mı, `workflow_requests` projeksiyonu mu (K-128 açılır) — ilk adımın ölçümü sayıyla karar önerir; (2) liste kimliği run başına satır mı, `(oturum, requestId)` başına mantıksal istek mi; (3) süre dolunca kaynak run'ın durumu (öneri `Failed` + `WorkflowRequestExpired`); (4) cevaplanmış ama K-130 gereği bekleyen istek de süreyle kapanır mı, hangi durumla; (5) sürenin kaynağı ve başlangıcı (öneri: workflow başına `RequestTimeout`, `RequestedAt`'ten); (6) liste yetkisi `Reader`/`RunAccess.Read` mi, `Operator`/`RunAccess.Approval` mı. Uygulama Faz 208 kapanmadan başlamaz
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-319** (gelen kutusu ve süre yarısı; karar yarısı [Faz 208](208-WORKFLOW-KARAR-SOZLESMESI.md))
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
   208: `workflow_responses` kaydı, iddia anahtarının ölçülen kapsamı (oturum mu kaynak run mı), `WorkflowRegistrationOptions`
4. Alan hafızası:
   [`hafiza/workflows.md`](hafiza/workflows.md) (K-888'in üç kayıp yolu, K-988) ·
   [`hafiza/sql-migration.md`](hafiza/sql-migration.md) (projeksiyon seçilirse) ·
   [`hafiza/kapasite-olcumu.md`](hafiza/kapasite-olcumu.md) (§209.1 ölçümü)
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
| [`WorkflowEndpoints.cs:315-326`](../src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs), `:1074-1114` | İstek listesi yalnız run başınadır (`/api/workflows/runs/{runId}/requests`) |
| `WorkflowRunner.ListPendingRequestsAsync` | Run'ın bütün olayları baştan okunur (`ReadEventsAsync(runId, 0)`); tür süzgeci yok. Cevaplanan run `AwaitingInput` kalır ve isteğini listeler (K-130) |
| [`IRunStore.cs:197`](../src/Tracon.Abstractions/Runs/IRunStore.cs), `:220` | `QueryRunsAsync(RunQuery)` kiracı + durum + tür + sayfa taşır; `ReadEventsAsync` tür almaz |
| [`RunEndpoints.cs:43-110`](../src/Tracon.AspNetCore/Endpoints/RunEndpoints.cs) | `GET /api/runs?status=AwaitingInput&kind=Workflow` bugün çalışır (`take` en çok 200, `:108`); istek yükü ve cevap bilgisi yok. `:64-67`: liste süzülmez — sayfalama sözleşmesi bozulur |
| `0001_initial.sql` (PG `:156`, SQL Server `:212-213`, Sqlite `:171`) · `runs_workflow_idx` (PG `0007_workflows.sql:14`, SQL Server `0001:232`, Sqlite `0001:177`) | `(tenant_id, status, started_at)` indeksi üç sağlayıcıda var; `run_events`'te yalnız `(run_id, seq)` PK var, tür indeksi yok |
| [`TraconOptions.cs:713`](../src/Tracon.Core/TraconOptions.cs), `:726` | `RecordMessageDeltas = true` varsayılandır: run başına tarama delta olaylarını da okur; yük sınırı 8 KB |
| [`WorkflowDefinition.cs:186`](../src/Tracon.Abstractions/Workflows/WorkflowDefinition.cs) · [`WorkflowPendingRequest.cs:19-51`](../src/Tracon.Abstractions/Workflows/WorkflowPendingRequest.cs) | Tanım yalnız `RunTimeout` taşır; istek `ExpiresAt`, workflow adı ve oturum taşımaz |
| [`RunStatus.cs:19-90`](../src/Tracon.Abstractions/Runs/RunStatus.cs) | `Expired` üyesi yok |
| [`RetentionTargetRegistry.cs:122-144`](../src/Tracon.Sql.Shared/Internal/RetentionTargetRegistry.cs), `:221-233`, `:336-349` | `AwaitingInput` run'ın istek olayı, checkpoint'leri ve girdi satırı süzgeçle tutulur; `:129` "an expiry for them is F-319's scope". `runs` satırı hiçbir retention hedefinde yok (`RetentionTargets.cs:18-93`) |
| [`ApprovalExpirationService.cs:25-139`](../src/Tracon.Core/Approvals/ApprovalExpirationService.cs) · [`TraconApprovalOptions.cs:21-37`](../src/Tracon.Core/Approvals/TraconApprovalOptions.cs) | Emsal: `SingletonGuard`, `PeriodicTimer`, `MaxPerScan`; süresi dolan onayın run'ı `CompleteRunAsync` ile `Failed` + `ApprovalExpired` olur (`:113-121`) |
| [`0027_pending_approvals.sql:1-30`](../src/Tracon.PostgreSql/Migrations/0027_pending_approvals.sql) | Emsal: kendini "projeksiyon" diye tanımlayan tablo; `(status, expires_at)` süre indeksi (`:29`) |
| [`approvals.tsx`](../src/Tracon.UI/frontend/src/screens/approvals.tsx) (263 satır) | Konsolun onay ekranı; workflow isteği orada görünmez |

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

Öneri (Açık Soru 2): liste **`(oturum, requestId)` başına bir satır** gösterir — o isteği taşıyan en yeni `AwaitingInput` run'ı. "Yanıtlandı" işareti aynı anahtarla okunur; bu, Faz 208'in iddia anahtarıyla aynıdır (208 ölçümü kaynak run dediyse işaret de run başınadır). Run başına `/requests` değişmez: o run'ın kendi isteklerini döner.

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

> Seviye kuralı: [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

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

- [ ] §209.1 ölçümü üç sağlayıcıda yapıldı; sayılar ve Açık Soru 1'in kararı "Plandan Sapmalar"da
- [ ] `GET /api/workflows/requests` kiracının bekleyen isteklerini sayfalı döner; `state=pending` sayfaları doğru toplar; cevaplanan istek `answered: true`
- [ ] Başka kiracının isteği listede yok; reddeden yetki işleyicisi listeyi `403` ile reddeder
- [ ] `RequestTimeout`'u dolan istek kapanır (Açık Soru 3-4 kararıyla); süre dolmuş run'a `respond` `400`
- [ ] Süre dolan run'ın istek olayı, checkpoint'leri ve girdi satırı bir sonraki retention turunda silinir
- [ ] K-130 ve K-888 satırları güncellendi; 1-B seçildiyse K-128 yeni gerekçeyle açıldı (kategori: kalıcı-veri)
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (manuel case 1, 4), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `15-WORKFLOWS.md`, `13-KIRACI-VE-GUVENLIK.md`, `23-SAKLAMA-ARSIV-KOTA.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (Tüketici yüzeyi satırı); `npm run check` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı gzip KB olarak ölçüldü ve yazıldı

### Doğrulama komutları

```bash
python3 scripts/kapi.py test --proje Tracon.AspNetCore.FunctionalTests --sinif "*WorkflowRequestInboxTests" "*WorkflowRequestExpirationTests"

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
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir. §209.1'in ölçümü buraya yazılır.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Fazın kararları burada yazılır. Dört kategoriden birine
> giren karar (AGENTS.md) ayrıca `docs/KARARLAR.md`'ye K-NNN ve kategori
> etiketiyle girer ve numarası burada anılır; girmeyen karar yalnız burada
> kalır (`faz-tamamlama` Adım 8). Plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
