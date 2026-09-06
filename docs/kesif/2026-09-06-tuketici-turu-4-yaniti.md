# AgentPrism — Tur 4 talep yanıtı

**Kime:** ProdigyEnabler Backend ekibi
**Tarih:** 2026-09-06 (güncellendi — ilk sürüm aynı gün, `fc2f9d8d`)
**Yanıtlanan belgeler:** `agentprism-feature-talepleri-2026-09-05.md` (A1 · A2 · F1–F8), `agentprism-uygulanabilirlik-raporu-2026-09-05.md` ve §8'e verdiğiniz yanıt
**İncelediğiniz sürüm:** `0.0.0-preview.0.589` · source commit `234d4081`
**Bu yanıtın kaynağı:** commit `7b1e101a` = **`0.0.0-preview.0.622`** — pin'iniz doğrulandı, [§0.1](#01-pininiz-doğrulandı)
**Kapsanan:** Faz **145 · 146 · 147 · 148 · 149 · 150** + beş kusur

> ### 🔄 Bu sürümde ne değişti
>
> İlk yanıttan sonra §8'deki üç sorunuza cevap verdiniz ve **iki faz daha
> kapandı**. Bu belge onları içerir.
>
> - **§4'teki "istediğinizden azını yaptık" maddesi kapandı** — sahipsiz satır
>   katı reddi sevk edildi ([§4.1](#41-katı-mod-sevk-edildi-faz-149)).
> - **F2 sevk edildi** ([§7](#7-f2--zorunlu-binding-profili-faz-150)); §9'daki
>   "açılmayan kalemler" tablosundan çıktı.
> - 🚨 **§5.2 DÜZELTİLDİ.** İlk sürümde "akışlı yolda ret `403` değil, `error`
>   çerçevesidir" yazmıştık. **Bu yanlıştı** — ölçüm ret'in iki dalda da gerçek
>   `403` olduğunu gösterdi. Ayrıntı ve nedeni [§5.2](#52-ret-iki-dalda-da-gerçek-bir-403--düzeltme).
> - **F-197 kapandı** ve yanında sevk edilmiş bir davranış değişikliği getirdi
>   (K-702) — [§8](#8-kusur-turu--beş-kusur-kapandı).
> - 🚨 Sevk edilmiş bir dokümantasyon iddiamız yanlış çıktı ve düzeltildi:
>   `AddAgentPrism`'den **sonra** yapılan `Add*` kaydı kazanır
>   ([§7'deki uyarı](#-di-kayıt-sırası--sevk-edilmiş-bir-iddiamız-yanlıştı)).

---

## 0.1 Pin'iniz doğrulandı

`0.0.0-preview.0.622` **altı fazın ve kusur turunun tamamını** taşıyor. Bunu
tahminle değil, diskteki `.nupkg`'nin XML'ini açarak ölçtük:

| Aranan üye | Geldiği faz | `.622` XML'inde |
|---|---|---|
| `QuotaThresholdCrossing` | 146 | ✅ |
| `RunAccess.Read` | 147 | ✅ |
| `SessionRecord.OwnerId` | 148 | ✅ |
| `RefuseUnownedSessions` | **149** | ✅ |
| `RequireCustomBinding` | **150** | ✅ |
| `MapOpenAIConversations` | **149** | ✅ |
| OpenAPI: SSE bildiren operation | 145 | **7** · `AgentPrismStreamRunEvents` dahil |
| OpenAPI: path / operation | — | 126 / 163 |

`0.0.0-preview.0.622` = commit `7b1e101a`. Sürüm MinVer ile commit
yüksekliğinden üretilir; sayı ile commit arasındaki eşleme elle
hesaplanamaz — bu tabloyu paketin kendisinden okuduk.

---

## 0. Özet

**A1, A2 ve F1 kabul edildi ve sevk edildi. §8 yanıtınızdan sonra F2 ve katı
sahiplik modu da sevk edildi.** Kapsam her seferinde istediğinizden **geniş**
çıktı; gerekçeleri aşağıda.

| Talebiniz | Karar | Faz |
|---|---|---|
| **A1** · Kullanıcı sahipliği ve end-user HTTP yüzeyi | ✅ Sevk edildi, kapsamı genişletildi | [147](#3-a1--yetki-kapısının-kaynak-kapsamı-faz-147) + [148](#4-a1--oturum-sahipliğinin-kalıcılığı-faz-148) + [149](#41-katı-mod-sevk-edildi-faz-149) |
| **A2** · Run'a bağlı kota eşiği SSE bildirimi | ✅ Sevk edildi | [146](#2-a2--çalıştırmaya-bağlı-kota-eşiği-faz-146) |
| **F1** · Typed SSE sözleşmesi | ✅ Sevk edildi — "fikir" diye sunmuştunuz, bizim kusurumuz çıktı | [145](#1-f1--kayıtlı-olay-akışının-çerçeve-sözleşmesi-faz-145) |
| **F3** · Kalıcı kota notice ledger | ✅ Sevk edildi — A2 ile aynı faza girdi | [146](#2-a2--çalıştırmaya-bağlı-kota-eşiği-faz-146) |
| **F2** · Zorunlu extension binding profili | ✅ Sevk edildi (§8 yanıtınız üzerine) | [150](#7-f2--zorunlu-binding-profili-faz-150) |
| **F6 · F7** | ⏸ Sizin tarafınızdan geri çekildi | — |
| **F4 · F5 · F8** | ❌ Açılmadı — gerekçeler [§9](#9-açılmayan-kalemler) |

**Faz kapılarınız için doğrudan cevap:**

| Sizin fazınız | Durum |
|---|---|
| Faz 8 · Chat geçişi | 🟢 **Kapı açıldı** — A1 ve A2 sevk edildi |
| Faz 9 · Canlı ses | 🟢 **Kapı açıldı** — ses hem yetki kapısına hem sahipliğe bağlandı |

**Cutover'dan önce dört şeyi okuyun:** [§5.2](#52-ret-iki-dalda-da-gerçek-bir-403--düzeltme) (ilk
sürümün düzeltmesi) · [§5.3](#53-yönetim-payı-ve-katı-modun-sınırı) (yönetim payının katı moddaki
sınırı) · [§7'deki DI uyarısı](#-di-kayıt-sırası--sevk-edilmiş-bir-iddiamız-yanlıştı) · [§8](#8-kusur-turu--beş-kusur-kapandı)
(K-702 davranış değişikliği).

### İddialarınızın doğruluğu

On dört iddianızı koda karşı ölçtük. **On üçü doğru çıktı.** Yanlış olan tek
iddia bir risk satırındaydı: `T:AgentPrism.MigrationDescriptor` public bir tip
değil, `internal sealed record` — R13'ün "hash'ler görünür" öncülü yanlıştı.
F7'yi kendiniz geri çekerken bunu zaten doğruladınız.

"Yok" yerine "bulunamadı" demeniz ve kaynak göstermeniz ölçümü hızlandırdı.

---

## 1. F1 — Kayıtlı olay akışının çerçeve sözleşmesi (Faz 145)

### Karar

**Kabul edildi.** Bunu "projeden bağımsız fikir" diye sundunuz; ölçüm bunun bir
**kusur** olduğunu gösterdi ve önceliği yükseltti.

Ölçüm: `RunEventType`'ın **31 üyesinden 21'i** `unknown` adıyla gidiyordu —
`Custom`, `RunAwaitingInput`, `ContentBlocked`, `ContentMasked`,
`ModelFallbackUsed`, bütün workflow olayları ve `ToolOutputTruncated` dahil.
Metodun kendi yorumu adları *"a **stable** contract"* ilan ediyordu.

MIME yarısı için de haklıydınız. Nedenini kendi karar defterimizde bulduk
(K-273, Faz 40): `.Produces(200, contentType: …)` `responseType` verilmeden
yazılırsa ASP.NET Core içerik tipini **sessizce düşürür**. `AgentPrismStreamRunEvents`
o çağrıyı hiç almamıştı.

### Sevk edilen

**31 olay tipinin hepsi adlandırıldı.** Sevk edilmiş 10 ad **değişmedi**.

| `RunEventType` | Çerçeve adı | | `RunEventType` | Çerçeve adı |
|---|---|---|---|---|
| `RunStarted` | `run.started` | | `WorkflowOutput` | `workflow.output` |
| `MessageDelta` | `message.delta` | | `WorkflowRequest` | `workflow.request` |
| `MessageCompleted` | `message.completed` | | `RunAwaitingInput` | `run.awaiting-input` |
| `ToolInvoking` | `tool.invoking` | | `ContentMasked` | `content.masked` |
| `ToolInvoked` | `tool.invoked` | | `ContentBlocked` | `content.blocked` |
| `ToolFailed` | `tool.failed` | | `ModelFallbackUsed` | `model.fallback-used` |
| `RunCompleted` | `run.completed` | | `ReasoningDelta` | `reasoning.delta` |
| `RunFailed` | `run.failed` | | `DocumentAttached` | `document.attached` |
| `ChildRunStarted` | `child.started` | | `ToolOutputTruncated` | `tool.output-truncated` |
| `ChildRunCompleted` | `child.completed` | | `RunContinuationBlocked` | `run.continuation-blocked` |
| `HistoryCompacted` | `history.compacted` | | `StructuredResponseRejected` | `structured-response.rejected` |
| `WorkflowStarted` | `workflow.started` | | `StructuredResponseRepairAttempted` | `structured-response.repair-attempted` |
| `SuperStepStarted` | `superstep.started` | | `Custom` | `custom` |
| `SuperStepCompleted` | `superstep.completed` | | `ChildRunTimedOut` | `child.timed-out` |
| `ExecutorInvoked` | `executor.invoked` | | | |
| `ExecutorCompleted` | `executor.completed` | | | |
| `ExecutorFailed` | `executor.failed` | | | |

**`Custom` sabit `custom` çerçevesi alır.** `CustomType` gövdede kalır.
Alternatifi (çerçeve adının `CustomType`'ın kendisi olması) reddedildi: o
tasarım çerçeve ad uzayını tüketiciye açar ve çekirdek adlarla çakışabilir.
`addEventListener('custom')` dinleyip gövdedeki `customType` ile ayırın.

**OpenAPI:** `AgentPrismStreamRunEvents` artık `text/event-stream` bildiriyor.
SSE bildiren işlem sayısı **6 → 7**.

**Kapı:** `RunEventFrameNameContractTests` dört iddia taşıyor — her üyenin adı
var, hiçbiri `unknown` değil, sunucu adı arayüz etiketiyle aynı, sevk edilmiş
10 ad sabit. Yeni bir olay tipi eklenirse derleme değil **test** kırmızı olur.

### Entegrasyonunuz için

- `unknown` çerçevesi artık gelmiyor. Parser'ınızdaki `unknown` dalını
  koruyun ama onu bir **hata** olarak işaretleyin — geldiyse sürüm uyumsuzluğu
  vardır.
- `@agentprism/client`'ın `schema.ts`'i yeniden üretildi; `AgentPrismStreamRunEvents`
  artık `text/event-stream` taşıyor. Orval dışlama listenizde bu operasyonu
  **adıyla** dışlamaya devam edin — MIME düzeldi ama transport lifecycle'ı
  hâlâ elle yazılıyor.
- İki akış modeli ayrımı `docs-site/concepts/runs.md` içinde artık tablo olarak
  duruyor. Raporunuzun *"ikisini content-type üzerinden ayırt edemedim"*
  şikâyeti bu tabloyla kapanıyor.

---

## 2. A2 — Çalıştırmaya bağlı kota eşiği (Faz 146)

### Karar

**Kabul edildi.** Dört olgunuzun dördü de doğru çıktı: korelasyon yoktu, sıra
yanlıştı, `RunEventType`'ta kota yoktu ve eşik hafızası süreç içiydi.

### Sevk edilen API

```csharp
// AgentPrism.Abstractions
public sealed record QuotaThresholdCrossing
{
    public required string NoticeId { get; init; }
    public required QuotaMetric Metric { get; init; }
    public required QuotaPeriod Period { get; init; }
    public required int ThresholdPercent { get; init; }
    public required decimal Limit { get; init; }
    public required decimal Used { get; init; }
    public string? AgentName { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
}

// QuotaConsumption ve WebhookQuotaSummary artık korelasyon taşıyor:
//   public string? RunId { get; init; }
//   public string? UserId { get; init; }

// Kalıcı tekilleştirme IQuotaStore'a girdi (KIRICI genişleme):
ValueTask<bool> TryClaimThresholdNotificationAsync(
    string tenantId, string agentName, QuotaPeriod period,
    DateOnly periodStart, QuotaMetric metric, int thresholdPercent,
    CancellationToken cancellationToken = default);

// QuotaEnforcer.RecordAsync artık geçilen eşikleri döner (KIRICI):
ValueTask<IReadOnlyList<QuotaThresholdCrossing>> RecordAsync(...);

// Anahtar — varsayılan KAPALI:
//   AgentPrismQuotaOptions.PublishThresholdToRunStream  (bool, false)
//   yapılandırma: AgentPrism:Quota:PublishThresholdToRunStream

// Rezerve CustomType sabiti:
RunEventCustomTypes.QuotaThreshold  // "agentprism.quota.threshold"
```

### Sıra — istediğiniz gibi

Muhasebe artık terminal olay yazımından **önce** çalışır (K-680):

```
kota kaydı + eşik claim  →  notice olayı  →  terminal olay
```

**Bedeli açıkça yazıyoruz:** kota artık `run` satırı kapanmadan önce tüketilir.
`CompleteAsync` hata verirse tüketim geri alınmaz. Token için para zaten
harcandığından bunu doğru davranış sayıyoruz.

`IRunEventSink`'in *"A sink failure never fails the run"* ilkesi korundu.

### Notice payload'ı

```
event: custom
id: 41
data: {"type":"Custom",
       "customType":"agentprism.quota.threshold",
       "payload":{"noticeId":"…","tenantId":"…","userId":"…","runId":"…",
                  "sessionId":"…","metric":"Tokens","period":"Monthly",
                  "thresholdPercent":80,"limit":1000000,"used":812340,
                  "resetsAt":"2026-10-01T00:00:00+00:00"}}
```

`agentprism.` öneki **rezerve**dir — `RunEventWriter.AppendAsync` bu önekle
yazılan tüketici olaylarını reddeder. Bu notice'ı yalnız kütüphane üretebilir.

**Korelasyon yoksa uydurulmaz.** `UserId` çözülemezse notice yine o `run`'ın
akışına yazılır, ama başka bir aktif kullanıcı tahmin edilmez.

**Çocuk kullanımı kökte bir kez bildirilir.** Alt `run` kota tüketmez ve notice
yazmaz.

### 🚨 Raporunuzda olmayan bir mekanizma — doğrudan POST akışı

Uygulama sırasında ölçüldü: **`scope.Writer.AppendAsync` ile yazılan hiçbir
olay doğrudan POST akışına yansımıyordu.** O akış yalnız MAF'ın kendi
`AgentResponseUpdate`'lerini `update` çerçevesi olarak iletiyor.

Çözüm: `AgentEndpoints`, akış bittikten sonra ve `done` yazılmadan önce kalıcı
kaydı **ikinci kez okur** ve yalnız `agentprism.quota.threshold` olaylarını
`event: custom` olarak yansıtır. İki yol aynı payload'ı **inşa etmez, aynı
kaydı okur**.

⚠️ **Bu mekanizma yalnız kota notice'ı içindir.** Kendi yazdığınız `Custom`
olayları (`prodigy.preview.created` gibi) doğrudan POST akışında **görünmez**;
yalnız `GET /api/runs/{id}/events` üzerinden okunur. Callback eşlemenizin
(`PreviewVersionAdded`, `ContentSaved`, `OntologyChanged`, `AudioGenerating`)
bu ayrıma göre kurulması gerekiyor.

### F3 — kalıcı tekilleştirme

`quota_usage` tablosuna `notified_thresholds` sütunu eklendi (K-682, CSV
kodlama, koşullu `UPDATE` ile atomik). Yeniden başlatma ve ikinci worker aynı
eşiği yeniden yayımlamaz. Üç sağlayıcıda sözleşme testiyle kilitli.

**Uyarınızı kabul ettik:** *exactly once delivery* sözü vermiyoruz. Sözleşme
"eşiğin **tespiti** dönem başına tekildir ve `NoticeId` idempotent teslim
kimliğidir" biçiminde yazılı.

**Bilinen sınır (F-199):** eşik claim edildikten sonra yayın başarısız olursa o
eşik dönem sonuna kadar kaybolur. Retry/backoff ayrı bir kalem.

### Entegrasyonunuz için

- `AgentPrism:Quota:PublishThresholdToRunStream=true` yazın. Varsayılan kapalı.
- FE `noticeId` ile dedup yapsın. Yeniden bağlanma aynı notice'ı tekrar okur.
- `IChatClient.QuotaWarning` karşılığı budur. `QuotaError` hâlâ ayrı: `run`
  başlatma `429`'u ve `AgentRunBudget` terminal sonucu.
- Kota enforcement'ın `AllowOnStoreFailure` ayarı **değişmedi**.
- **§8 yanıtınız kaydedildi:** dönem kotası tenant ortak bütçesidir. Mevcut
  `(tenant, agent, dönem)` kapsamı bunu karşılıyor — bu yönde **iş açılmadı**.
  Notice'taki `userId` yalnız korelasyondur ve kullanıcıya dönem hakkı
  tanımlamaz; bunu XML'de de böyle yazıyoruz.

---

## 3. A1 — Yetki kapısının kaynak kapsamı (Faz 147)

### Karar

**Kabul edildi ve kapsamı sizin istediğinizden geniş tutuldu.**

Ölçüm beş olgunuzu da doğruladı. `RunEndpoints.cs` (992 satır) içinde
`RunAuthorizationGate` çağrısı **sıfırdı**; approval ve attachment uçlarında da
sıfırdı; ses yalnız kiracı denetliyordu.

### 🚨 Raporunuzda olmayan altı yüzey

Siz yalnız **okuma** grafiğini ölçtünüz. Planlama ve denetim ölçümü, gerçek bir
`run` **başlatan** veya `run` okuyan altı yüzey daha buldu:

| Yüzey | Ne yapıyor |
|---|---|
| `POST /api/runs/{id}/replay` | Kendi özeti *"Starts a new run with recorded input."* |
| `POST /v1/chat/completions` | Katalogdan agent çözüp `RunAsync`/`RunStreamingAsync` çağırıyor |
| `POST /api/workflows/runs/{id}/resume` | Yeni `run` satırı açıyor — kota kapısından da geçmiyordu |
| `POST /api/workflows/runs/{id}/respond` | Aynı |
| `POST /api/runs/{id}/judge` | `run` okuyup skor yazıyor |
| `POST /api/evals/{name}/cases/from-run/{id}` | `run` okuyup eval case'i üretiyor |

Faz 139'un "dört run başlatan yüzey" iddiası (K-670) **eksikti**. Altısı da
kapsandı.

Bağımsız denetim ayrıca **var olan bir güvenlik kusuru** buldu:
`DELETE /api/runs/{runId}/feedback/{scoreId}` kapıyı `runId` için soruyor ama
silmeyi `scoreId` ile yapıyordu; ikisi hiç bağlanmamıştı. Kendi `run`'ını
puanlamaya izinli bir çağıran **başkasının skorunu** silebiliyordu. Kusur bu
fazdan önce de vardı; kapı onu görünür yaptı. Kapatıldı.

### Sevk edilen API

```csharp
public enum RunAccess
{
    Start = 0,       // yeni run — replay ve workflow devamı dahil
    Read = 1,        // run · tree · events · input · trace · tools · skorlar
    Cancel = 2,
    Feedback = 3,    // skor yazma · silme
    Attachment = 4,
    Approval = 5,
}

public enum SessionAccess
{
    Read = 0, List = 1, Delete = 2, Branch = 3,
    Voice = 4,       // WebSocket konuşma oturumu
}

public sealed record RunAuthorizationRequest
{
    public required string TenantId { get; init; }
    public string? AgentName { get; init; }   // artık required DEĞİL
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
    public Guid? RunId { get; init; }         // YENİ
    public required RunAccess Access { get; init; }
}
```

`IRunAuthorizationHandler`'ın **metot sayısı değişmedi** — iki metot.

🚨 **Endişenizi doğrudan karşılıyoruz:** enum'un sayısal değeri bir persistence
sözleşmesi **değildir**. `RunAuthorizationRequest` senkron bir istek kaydıdır ve
sonucu hiçbir yere yazılmaz. Bu cümle enum'un XML'ine yazıldı (K-683).

### Kapsam — ölçülen sayı

Kapı çağrısı **8'den 35'e** çıktı, 13 dosyaya yayıldı. Faz 149 conversations
yüzeyini de ekledi (aşağıda).

### Ret kodları

| Erişim | Ret |
|---|---|
| Tekil kaynak (run · trace · attachment · approval · ses) | `404`, gövdesi **gerçekten var olmayan kaynakla birebir aynı** |
| Liste (`/api/runs`, `/api/attachments`, `/api/approvals/pending`) | `403` |
| Run başlatma (replay · chat completions · workflow devamı) | `403` |
| `POST /api/attachments` (yükleme) | `403` — adreslenen kaynak yok |

Ret metni kapının kendisinden değil **çağıran uçtan** gelir: ret metni uçtan uca
değişiyor (`"Run not found"` · `"Trace not found"` · `"Attachment not found"`) ve
kapının tek metin üretmesi en az bir uçta gövde birebirliğini bozardı.

**Bir davranış değişikliği:** `GET /api/runs/{id}/tools` artık `run`'ı önce
okuyor; var olmayan `run` için `200 []` yerine `404` dönüyor (K-685).

### Ses

`SessionAccess.Voice` ile kapsandı. Ret `404` (K-687). K-283 korundu: **var
olmayan oturum reddedilmez**, ilk tur onu açar.

### Entegrasyonunuz için

- Tek bir `IRunAuthorizationHandler` yazın; `Access` üzerinden dallanın.
  `RunId` artık her kaynak erişiminde dolu.
- `Start` erişiminde `RunId` **replay'de doludur** ve kaynak `run`'ı gösterir.
- Handler kayıtlı değilken **hiçbir** davranış değişmez.

---

## 4. A1 — Oturum sahipliğinin kalıcılığı (Faz 148)

### Sevk edilen API

```csharp
public sealed record SessionRecord { /* … */ public string? OwnerId { get; init; } }
public sealed record SessionQuery  { /* … */ public string? OwnerId { get; init; } }

public sealed class AgentPrismSessionOwnershipOptions
{
    public const string SectionName = "AgentPrism:SessionOwnership";
    public bool Enabled { get; set; }                       // varsayılan KAPALI
    public bool RequireAuthenticatedOwner { get; set; } = true;
    public string? ManagementPolicy { get; set; } = "AgentPrism.Operator";
    public bool RefuseUnownedSessions { get; set; }         // Faz 149, varsayılan KAPALI
}
```

`sessions` tablosuna `owner_id` sütunu ve `(tenant_id, owner_id, updated_at DESC)`
indeksi eklendi. Üç sağlayıcıda birer migration.

### Davranış — istediğiniz gibi

- **Sahip doğrulanmış kimlikten, yazma anında alınır.** Gövde ve etiketler
  sahibi değiştiremez.
- **Filtre `WHERE`'de, sayfalamadan önce.** Sözleşme XML'i: *"A store that
  filters the page it already fetched returns short pages, empty pages, and —
  worst — lets a caller infer how many sessions OTHER users hold from the
  gaps."* Sayfalama sözleşmesi bozulmuyor, sayı sızmıyor.
- **Sahiplik bir kez atanır.** Dört depo da `COALESCE` eder. Dallandırma,
  Responses zinciri ve ses devamı **kaynağın** sahibini miras alır (K-689).
- **Mod açıkken kimlik çözülemezse `403`** (`errorType: session_owner_required`).
  Attribution bu modda bir yetkilendirme girdisidir (K-690).

### 🚨 Plandan iki sapma — ikisi de sizi ilgilendiriyor

**1. Kapı `run` başlatan yüzeylere de gerekti.** Fonksiyonel test yazılırken
görüldü: `POST /api/agents/{ad}/run` gövdesinde `sessionId` ile **başka
kullanıcının oturumunu sürdürmek** hâlâ mümkündü. Sahiplik korunuyordu ama
çağıran o konuşmanın tamamını modele geri okutmuş oluyordu. Üç yüzey sahiplik
kapısına bağlandı. **Raporunuzun A1.2.4 maddesi tam olarak bu yolu tarif
ediyordu — haklıydınız.**

**2. Ambient scope tek başına yetmedi.** Kuyruğa alınmış `run`'ın sahibini iş
zarfına yazıp işçide geri oynatmak yeterli değildi: siz kendi
`IRunAttributionContext`'inizi kaydettiğinizde ambient scope hiç okunmuyordu.
Sahiplik çözümünde ambient scope artık kayıtlı servisi **ezer** (K-692). Bu dar
istisna yalnız sahiplik içindir.

### 4.1 Katı mod sevk edildi (Faz 149)

İlk yanıtımızda burada bir **taviz** vardı: sahipsiz eski satır sahipli listede
görünmüyordu ama tekil erişimde reddedilmiyordu (K-693). Siz bunu reddettiniz:

> *"Sahipliği belirlenemeyen mevcut bir session, kimliğini bilen son kullanıcıya
> açık olmamalı. Listede görünmemesi erişim kontrolünün yerine geçmez."*

**Kabul edildi ve sevk edildi.** K-693'ün yeniden açılma koşulu karşılandı.

```jsonc
// varsayılan KAPALI — mevcut kurulumlar korunur, istediğiniz gibi
"AgentPrism": { "SessionOwnership": {
    "Enabled": true,
    "RefuseUnownedSessions": true
}}
```

**İstediğiniz ayrımın ikisi de korunuyor:**

| Durum | Davranış |
|---|---|
| Oturum **henüz yok** | Açılır — yeni session yolu korundu (K-283) |
| Oturum **var**, `OwnerId = null` | **Reddedilir** |
| Oturum var, başka kullanıcının | Reddedilir (zaten böyleydi) |

Kural **on bir sahiplik çağrı yerinin hepsinde** geçerlidir: oturum uçları,
`run` başlatan üç yüzey, `/v1/conversations` ve ses.

**Ret metni ikisinde de aynıdır** (K-695): sahipsiz satır reddi ile başkasının
oturumu reddi ayırt edilemez. İki ayrı metin, çağıranın hangi oturumların
sahiplikten **önce** yazıldığını öğrenmesini sağlardı. `errorType` tektir:
`session_owner_required`.

### 4.2 `/v1/conversations` kapatıldı ve kapıya bağlandı

Planlama ölçümü size bildirdiğimiz boşluğu doğruladı: `/v1/conversations`
(dört uç, `MapAgentPrism` tarafından koşulsuz map'leniyordu) sahiplik
kapısından geçiyordu ama **sizin handler'ınızdan geçmiyordu**.

| Uç | Sevk edilen |
|---|---|
| `GET /v1/conversations/{id}` | `SessionAccess.Read` → handler'a sorulur |
| `DELETE /v1/conversations/{id}` | `SessionAccess.Delete` → handler'a sorulur |
| `GET /v1/conversations/{id}/items` | `SessionAccess.Read` → handler'a sorulur |
| `POST /v1/conversations` | **Bağlanmadı** — ölçüldü: `ISessionStore`'a hiç dokunmuyor, yalnız kimlik ayırıyor (K-696) |

Ayrıca yüzeyi hiç istemiyorsanız kapatabilirsiniz:

```csharp
app.MapAgentPrism(options => options.MapOpenAIConversations = false);
```

Varsayılan `true` — sevk edilmiş bir yüzey sessizce geri çekilmez. Bayrak
yalnız conversations ailesini yönetir; `/v1/responses` ve
`/v1/chat/completions` kapsam dışıdır ve zaten kapıdan geçiyorlar (K-697).

⚠️ **Handler kaydetmiş mevcut kurulumlarda davranış değişikliğidir** — orada
bugüne kadar hiç sorulmayan handler artık sorulur ve reddedebilir. Yön
güvenlidir (fail-closed).

---

## 5. Cutover'dan önce okuyun

### 5.1 Sahiplik ve yetki iki ayrı kapıdır

AgentPrism iki kapı çalıştırır ve **ikisi de sorulur; biri reddederse istek
reddedilir**:

| Kapı | Kim karar verir | Neyi bilir |
|---|---|---|
| Sahiplik (`SessionOwnershipGate`) | AgentPrism | `sessions.owner_id` ile çağıranın kimliği |
| Yetki (`IRunAuthorizationHandler`) | **Siz** | Kendi politikanız — proje, rol, kiracı üstü kurallar |

Sahiplik modunu açmanız handler'ı gereksiz yapmaz; handler yazmanız sahiplik
modunu gereksiz yapmaz.

### 5.2 Ret iki dalda da gerçek bir `403` — düzeltme

🚨 **İlk sürümde bu bölüm yanlıştı.** *"Akışlı yolda ret bir `403` değil, bir
SSE `error` çerçevesidir"* yazmıştık ve FE parser'ınızın iki ret biçimini de
tanıması gerektiğini söylemiştik.

**Ölçüm bunu çürüttü.** Sahiplik kapısı uç gövdesinde `SseWriter.StartAsync`'ten
**önce** koşuyor — `AgentEndpoints.cs`'de kapı 236. satırda, SSE başlıkları
1134. satırda. Başlıklar henüz gitmemiştir, bu yüzden ret **gerçek bir
`403`'tür**:

| Yol | Ret |
|---|---|
| Akışlı (varsayılan SSE) | `403` + `errorType: session_owner_required` |
| Akışsız (`Idempotency-Key`, buffered) | `403` + aynı `errorType` |

Yanlışın kaynağı bizim tarafımızdaydı: Faz 148'in plan metni bunu böyle
varsayıyordu ve ilk yanıtı o metinden yazdık. Faz 149 koda karşı ölçtü ve
düzeltti. **FE parser'ınızda ikinci bir ret biçimi için kod yazmanıza gerek
yok.**

Bir kalıntı yol var ve dürüst olmak için yazıyoruz: kapı geçtikten **sonra**
oturum yazımında bir sahiplik istisnası doğarsa, akışlı dalda o istisna genel
`error` çerçevesi yoluna düşer (K-296/K-384 sınıfı). Bu bir yarış durumudur,
beklenen yol değildir; `errorType` yine aynıdır.

### 5.3 Yönetim payı ve katı modun sınırı

`ManagementPolicy` varsayılanı `"AgentPrism.Operator"`. Bu politikayı taşıyan
istek **filtresiz kiracı listesini** görür. AiOps console'unuz bununla çalışır;
son kullanıcı bu politikayı **almamalıdır**. Açık sorunuz *"AiOps operatörü tüm
kullanıcı konuşmalarını okuyabilir mi?"* — cevabı artık bir yapılandırma
anahtarıdır.

🚨 **Katı modda yönetim muafiyetinin sınırı dar** (K-694): muafiyet yalnız
**okuma ve silme** kapısındadır; **`run` başlatmada yoktur.** Yani operatör
sahipsiz bir konuşmayı okuyabilir ama onu **sürdüremez**. Gerekçe: bir turu
sürdürmek konuşmaya **yazar** ve operatörü sahipsiz bir konuşmanın yazarı
yapardı. Destek akışınız "operatör konuşmayı devralıp devam etsin" gerektiriyorsa
bu bir kısıttır — bize söyleyin.

### 5.4 Sayı ve sürüm

| Ölçüm | Değer |
|---|---|
| OpenAPI path / operation | **126 / 163** — altı fazda değişmedi, yeni uç yok |
| SSE bildiren operation | 6 → **7** |
| Yetki kapısı çağrı yeri | 8 → **35** |
| Sahiplik kapısı çağrı yeri | **11** |
| Migration (PostgreSQL) | 44 → **46** |
| Migration (SQL Server · SQLite) | 32 → **34** her biri |

**Kırıcı değişiklikler** (`PublicAPI.Shipped.txt` boş olduğu için bugün
ücretsiz, GA'dan sonra olmazdı):

- `QuotaEnforcer.RecordAsync` artık `IReadOnlyList<QuotaThresholdCrossing>` döner
- `IQuotaStore` bir üye kazandı (`TryClaimThresholdNotificationAsync`)
- `RunAuthorizationRequest.AgentName` artık `required` değil

Kendi `IQuotaStore` implementasyonunuz olmadığı için ilk ikisi sizi etkilemez.

**Sürüm yayımlandı:** `0.0.0-preview.0.622` ([§0.1](#01-pininiz-doğrulandı)).

---

## 6. Sizi etkileyen bilinen kusur — biri kapandı

**F-197 · KAPANDI.** İlk yanıtımızda açık bir kusur olarak bildirmiştik:
üretilen istemcinin koleksiyonları `null` başlıyordu. Kapatıldı — ve sınıf
taraması kayıtta hiç olmayan **ikinci ve daha ağır** bir vaka buldu. Ayrıntı
[§8](#8-kusur-turu--beş-kusur-kapandı).

**F-198 · HÂLÂ AÇIK.** `/v1/responses` ve `/v1/chat/completions` typed
istemcide yalnız JSON şeklini taşıyor. `stream: true` gönderirseniz JSON
deserialize hatası alırsınız. **Bu iki uç için `HttpClient` kullanın.** Faz 145
saf-SSE beş ucun aynı kusurunu düzeltti; bu ikisi çift içerikli olduğu için
ayrı bir tasarım gerektiriyor ve kapsam dışı bırakıldı.

Karar 26 ile `AgentPrism.Client`'ı kullanmayacağınızı biliyoruz; yine de
bildiriyoruz.

---

## 7. F2 — Zorunlu binding profili (Faz 150)

### Karar

**§8 yanıtınız üzerine kabul edildi ve sevk edildi.** "P2, fazı bekletmiyor"
dediniz; biz de bir sonraki turda kapattık.

### Sevk edilen API

```csharp
builder.Services.AddSingleton<IRunAuthorizationHandler, ProdigyRunAuthorization>();

builder.AddAgentPrism()
       .RequireCustomBinding<IRunAuthorizationHandler>()
       .RequireCustomBinding<IToolAuthorizationHandler>()
       .RequireCustomBinding<IAttachmentStorage>();
```

`IAgentPrismBuilder RequireCustomBinding<T>() where T : class`

Zorunlu ilan edilen sözleşme AgentPrism'in **yerleşik varsayılanıyla**
çözülüyorsa **host başlamaz**. Hata mesajı üç bilgiyi taşır: hangi sözleşme ·
hangi tip çözüldü · nasıl düzeltilir.

**Kapsanan yedi sözleşme:** `ITenantContext` · `IRunAttributionContext` ·
`IToolAuthorizationHandler` · `IRunAuthorizationHandler` · `IRunEventSink` ·
`IAttachmentStorage` · `IToolApprovalPresenter`.

Son ikisinde "varsayılan" bir tip değil bir **yokluk**tur — orada kontrol
"hiç kayıt var mı" diye sorar. `IAttachmentStorage` kaydı yoksa host durur;
sizin MinIO adaptörünüz için doğru davranış budur.

**Tasarım kararları:**

| Karar | Ne demek |
|---|---|
| K-698 | Yedi sözleşmenin kümesi **çalışma anında** zorlanır. Tanınmayan tip host başlangıcında yediyi listeleyen açık bir hata verir |
| K-699 | İhlal `InvalidOperationException` atar — `AgentPrismRolePolicies` ile aynı sınıf |
| K-700 | Zorunluluk `/api/diagnostics`'te **görünmez**. Rapor olguyu taşır, niyeti değil; ihlal varsa host zaten ayakta değildir |
| K-701 | Lifetime iddiası kapsam dışı. `ValidateOnBuild`/`ValidateScopes` onu zaten yakalar |

Kontrol **host başlangıcında** koşar, `MapAgentPrism` anında değil — HTTP'siz
gömülü host'lar da kapsanır.

⚠️ **Bu bir kompozisyon kapısıdır.** Hangi implementasyonun bağlandığını
kanıtlar; o implementasyonun **doğru karar verdiğini** kanıtlamaz.

### 🚨 DI kayıt sırası — sevk edilmiş bir iddiamız yanlıştı

Bu fazın ölçümü, dokümanımızda **sevk edilmiş** bir cümlenin yanlış olduğunu
gösterdi. Sizin ABP modül sırası senaryonuzu doğrudan ilgilendiriyor.

| Kayıt | `AddAgentPrism`'e göre | Sonuç |
|---|---|---|
| `AddSingleton<I, T>()` | önce | ✅ sizin tipiniz bağlanır |
| `AddSingleton<I, T>()` | **sonra** | ✅ **sizin tipiniz bağlanır** — dokümanımız bunun tersini yazıyordu |
| `TryAddSingleton<I, T>()` | **sonra** | ❌ **düşer**; yerleşik varsayılan bağlı kalır |

Yerleşik DI kabı bir servis tipini çözerken **son** `ServiceDescriptor`'ı
kullanır. Yani `Add*` sonra da kazanır; kaybeden yalnız `TryAdd*` sonradır.

**Sizin gerçek riskiniz üçüncü satırdır:** bir ABP modülü kaydını `TryAdd` ile
yapar, AgentPrism'in varsayılanı slotu zaten tutuyordur ve kayıt **sessizce
düşer**. F2 kapısının değeri tam olarak oradadır. Yanlış cümle iki sevk edilmiş
yerde düzeltildi.

---

## 8. Kusur turu — beş kusur kapandı

Fazlardan bağımsız bir `kusur-giderme` turu koşuldu. İkisi sizi doğrudan
ilgilendiriyor.

### F-197 — üretilen istemci **ve sunucu**

Kayıt tek vakayı anlatıyordu; sınıf taraması ikinci ve daha ağır vakayı buldu.

| Yarı | Düzeltme öncesi ölçüldü | Düzeltme |
|---|---|---|
| İstemci | 50 non-nullable koleksiyon property'si `= default!`; yalnız `Message` ile çağrı **`500`** | Üretim betiğine beşinci geçiş; 50 property boş koleksiyonla başlar |
| **Sunucu** (kayıtta yoktu) | Ham `{"approvals":null}` → **`500`** NRE; `{"documents":null}` → `200` ama SSE gövdesinde NRE | Gövde okumasına `RespectNullableAnnotations` → **`400 ProblemDetails`** |

### 🚨 K-702 — sevk edilmiş bir davranış değişikliği

**Sözleşmenin non-nullable ilan ettiği bir koleksiyona AÇIK `null` göndermek
artık `400` döner; eskiden `500` veya sessiz-null'du.**

| Gövde | Eski | Yeni |
|---|---|---|
| `{"message":"…","approvals":null}` | `500` | **`400 ProblemDetails`** |
| `{"message":"…"}` (alan atlanmış) | `200` | `200` — **değişmedi** |
| Nullable bir alana `null` | `200` | `200` — **değişmedi** |

Ayırt edici iki tarafta da `nullable` annotation'ıdır. 56 nullable koleksiyon
property'si dokunulmadan kaldı — `?` işareti "verilmedi" ile "boş verildi"yi
ayırdığını söyleyen bir sözleşmedir.

**Kırıcı saymadık**, çünkü hiçbir çağıran `500`'e bağımlı olamaz. Ama sevk
edilmiş bir sözleşme değişikliği olduğu için karar defterine girdi ve
`http-api.md`'ye yazıldı. **Gövde üreticinizi kontrol edin:** açık `null`
gönderen bir serializer artık `400` alır.

### F-170 — denetim izi boşluğu

`IAgentSkillStore` denetim izine hiçbir şey yazmıyordu: `PUT`/`DELETE
/api/skills/{name}` iz bırakmıyordu — oysa kardeşi `ISkillScriptGrantStore`
yazıyordu. Yani iz, bir script'i kimin **çalıştırmaya izin verdiğini**
kaydediyor, kimin **yazdığını** kaydetmiyordu.

`AuditingAgentSkillStore` eklendi (`skill.create` · `skill.update` ·
`skill.delete`), dört kayıt yerinde. Yeni kapı (`AuditCoverageTests`) her
`I*Store`'un ya denetlenmesini ya da **gerekçesiyle** hariç listesinde olmasını
zorunlu kılıyor.

**Denetim izi gereksiniminiz varsa** (rapordaki "denetim izi" kalemi) bu
düzeltme sizi ilgilendirir.

### Diğer üçü

**F-203 · F-204 · F-206** — doküman bakım kapısı, yetki kapsam kapısı ve test
tiyatrosu tarayıcısı. Üçü de bizim iç kapılarımızdır; sevk edilen yüzeye
dokunmuyorlar. F-204'ün ölçümü kaydın söylediğinden büyük çıktı: kapı bir
çağrının silinmesini göremiyordu ve varlık kontrolünden **tam sayı
eşitliğine** çevrildi.

---

## 9. Açılmayan kalemler

| Kalem | Karar ve gerekçe |
|---|---|
| **F6** · Ses/WebSocket test harness'i | ⏸ **Sizin tarafınızdan geri çekildi.** Boşluk gerçek: `AgentPrism.Testing`'de `FakeModelProvider` ve `SseReader` var, ses protokolü fixture'ı yok. Ölçülmüş bir eksiklik çıkarsa iletin |
| **F7** · Migration plan artifact'i | ⏸ **Sizin tarafınızdan geri çekildi.** `MigrationDescriptor`'ın `internal` olduğunu kendiniz doğruladınız |
| **F4** · Yazma hacmi planlayıcısı | ❌ **Açılmadı.** Kendi belgeniz "GB/ay vaat edilmemeli" diyor; ölçülmüş bir talep yok |
| **F5** · Çok dilli guard corpus'u | ❌ **Açılmadı.** Faz 140 `ContentGuardContext.Source` ayrımını sevk etti. Kalan iş bir test verisi bakımıdır |
| **F8** · Generator kontrat paketi | ❌ **Açılmadı.** Asıl şikâyet beklenti ve doküman; `AddToolsFrom<T>` açık kayıt zaten doğru yol |
| **F-198** · Çift içerikli uçların typed istemcisi | ⏸ **Kapsam dışı bırakıldı.** [§6](#6-sizi-etkileyen-bilinen-kusur--biri-kapandı) |
| **F-199** · Kota notice teslim retry'ı | ⏸ Talep ederseniz açarız |

"Bunu şu fazımız bekliyor" cümlesi yeterli bir talep kanıtıdır.

---

## 10. Üç sorunun cevabı

İki maddeye cevabınız kaydedildi; üçüncüsünü siz bize sordunuz.

### 10.1 · `MapOpenAIConversations=false` — kaydedildi ✅

Doğru okumuşsunuz: bayrak yalnız conversations ailesini kapatır,
`/v1/responses` ve `/v1/chat/completions` **açık kalır** (K-697) ve ikisi de
zaten yetki kapısından geçiyor.

Kapattığınızda dört uç haritalanmaz **ve OpenAPI'den de düşer** — çalışan
host'un Swagger çıktısından Orval üretme kararınızla bu doğal olarak uyuşuyor:
üretilen istemcide `/v1/conversations` hiç görünmez, dışlama listesi
tutmanıza gerek kalmaz.

### 10.2 · K-702 — kaydedildi ✅

Planınız sözleşmeyle birebir örtüşüyor: alanı atlamak veya `[]` göndermek
ikisi de `200`; açık `null` `400`; nullable alanlar `null` kabul etmeye devam
eder. FE serializer'ını doğrulayamadığınızı yazmışsınız — testi entegrasyonda
koşmanız yeterli, bizden ek bir şey gerekmiyor.

### 10.3 · Operatörün konuşmayı devralması — cevabımız

**Bugün hiçbir devralma yolu yok.** Üç yolu da ölçtük:

| Yol | Bugünkü davranış |
|---|---|
| `run` ile sürdürme | ❌ `CheckRunSessionAsync` yönetim politikasını **hiç göremiyor** — imzasında `HttpContext` yok |
| Dallandırma | ❌ Dal, **kaynağın** sahibini miras alır (`ConversationBranchService.cs:227`). Sahipsiz bir satırın dalı da sahipsizdir; operatör onu yine sürdüremez |
| Sahiplik devri | ❌ `owner_id = COALESCE(owner_id, @owner_id)` — bir kez atanır, hiç değişmez (K-689) |

Operatörün katı modda yapabildiği tam olarak şudur: **sahipsiz** bir satırı
listede görmek, okumak ve silmek. Başka bir kullanıcının **sahipli** satırı
operatöre de kapalıdır (K-691) ve bu faz onu değiştirmedi.

**Önerimiz — üç seçenek, birini yapmayın:**

**❌ (A) Yönetim muafiyetini `run` başlatmaya taşımak.** Bunu önermiyoruz.
Operatörü **görünmez bir ortak yazar** yapar: kullanıcının konuşmasına onun
yazmadığı turlar eklenir ve oturum satırında bunu kaydeden hiçbir şey yoktur.
Katı modun daralttığı sınırı tam olarak geri açar. Sahipli satırlara da
uzatılırsa K-691 doğrudan düşer.

**✅ (B) Dallandırmada sahip çağıran olsun — teşhis amaçlı devralma.** Yönetim
politikasını taşıyan çağıran dallandırdığında dal **kaynağın** değil
**çağıranın** sahipliğine geçer. Operatör kendi kopyasında sürdürür, teşhis
eder, üretir; kullanıcının konuşmasına **hiç dokunulmaz**. Hiçbir invariant
kırılmaz — dal yeni bir oturumdur ve yeni oturumun sahibini yaratma anında
atamak zaten normal kuraldır. En ucuz seçenek.

**⚠️ (C) Açık ve denetlenen sahiplik devri — gerçek devralma.** Operatör
kullanıcının **kendi thread'inde** cevap verecekse bu gerekir. Bedeli:
K-689'un "bir kez atanır" kuralı bir yönetim işlemi için delinir, devir denetim
izine yazılır, ve devirden sonra kullanıcı o oturumu **kendi listesinde
göremez**.

**Kararı destek akışınız veriyor:**

- Destek ekibi konuşmayı **okuyup teşhis ediyorsa** → bugünkü davranış yeterli,
  hiçbir şey yapmayın.
- Destek **kendi kopyasında yeniden üretmek** istiyorsa → (B). Küçük bir faz,
  geçişinizi bloklamaz.
- Destek **kullanıcının thread'ine yazacaksa** → (C). Bu bir ürün kararıdır ve
  kullanıcıya "konuşmanız destek ekibine devredildi" demeyi gerektirir.

Hangisi olduğunu söyleyin, ölçüp planlayalım. **Üçü de Faz 8/9 kapılarınızı
bekletmiyor** — kapılar açık.

---

## Ek — doğrulama

Bu güncelleme yazılmadan önce koşulan ölçümler (commit `916f2d78`, macOS/arm64,
2026-09-06). Faz dokümanlarının iddiası değil, **yeniden ölçülen** sonuçlar:

| Ölçüm | Sonuç |
|---|---|
| Çalışma ağacı | temiz |
| `RunEventType` üye ↔ adlandırılan | **31 ↔ 31**, `unknown`'a düşen yok |
| OpenAPI `AgentPrismStreamRunEvents` 200 | `content: {"text/event-stream": {"schema": {"type": "string"}}}` |
| OpenAPI path / operation | 126 / 163 |
| `RunAccess` üyeleri | `Start` · `Read` · `Cancel` · `Feedback` · `Attachment` · `Approval` |
| `SessionAccess` üyeleri | `Read` · `List` · `Delete` · `Branch` · `Voice` |
| Yetki kapısı çağrı yeri | 35 (13 dosya) |
| Sahiplik kapısı çağrı yeri | 11 (6 dosya) |
| `AgentPrismSessionOwnershipOptions` üyeleri | `Enabled` · `RequireAuthenticatedOwner` · `ManagementPolicy` · **`RefuseUnownedSessions`** |
| `AgentPrismEndpointOptions.MapOpenAIConversations` | varsayılan `true` |
| `IAgentPrismBuilder.RequireCustomBinding<T>()` | `where T : class`, public yüzeyde |
| 🚨 Sahiplik kapısı ↔ SSE başlığı sırası | kapı `AgentEndpoints.cs:236`, `SseWriter.StartAsync` `:1134` → **ret gerçek `403`** |
| `RunEventCustomTypes.QuotaThreshold` | `"agentprism.quota.threshold"` |
| Migration setleri | PostgreSQL 46 · SQL Server 34 · SQLite 34 |
| `dotnet build AgentPrism.slnx -c Release` | ✅ **0 uyarı · 0 hata** (92 s) |
| `dotnet test AgentPrism.slnx` (tam çözüm) | ✅ **6442 / 6442 geçti · 0 düşen · 0 atlanan** (617 s, ilk koşumda) |
| `dotnet pack` · `dotnet format --verify-no-changes` | ✅ ikisi de temiz |
| `secret` taraması · doküman denetimi · script testleri (235) · yetenek haritası · denetim paketi | ✅ beşi de temiz |
| `docs-site` `npm run check` | ✅ temiz |

> **Not:** İlk yanıtta bildirdiğimiz iki kararsız test (SQL Server teardown
> zaman aşımı ve bir Playwright E2E) bu koşumda **düşmedi**. O ikisi tam çözüm
> koşumundaki kaynak çekişmesi sınıfındaydı, ürün kusuru değil; kayıt için
> yazıyoruz.

### Doğrulanmayanlar

Canlı PostgreSQL/MinIO/provider ile uçtan uca smoke testi bu ölçümde
yapılmadı; örnek uygulama koşumları SQLite/bellek içi `store` ve gerçek
sağlayıcı çağrılarıyla faz kapanışlarında yapıldı ve çıktıları faz
dokümanlarında duruyor. Sizin ortamınızdaki smoke testi bunun yerine geçmez.

Pin'iniz (`0.0.0-preview.0.622`) bu yanıtın anlattığı her şeyi taşıyor;
[§0.1](#01-pininiz-doğrulandı) tabloyu paketin XML'inden okuyarak kanıtlar.
