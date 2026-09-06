# AgentPrism — Tur 4 talep yanıtı

**Kime:** ProdigyEnabler Backend ekibi
**Tarih:** 2026-09-06
**Yanıtlanan belgeler:** `agentprism-feature-talepleri-2026-09-05.md` (A1 · A2 · F1–F8) ve `agentprism-uygulanabilirlik-raporu-2026-09-05.md`
**İncelediğiniz sürüm:** `0.0.0-preview.0.589` · source commit `234d4081`
**Bu yanıtın kaynağı:** commit `fc2f9d8d` — Faz 145 · 146 · 147 · 148 kapandı

---

## 0. Özet

**A1 ve A2 kabul edildi ve sevk edildi. F1 de sevk edildi.** Üçünün kapsamı
istediğinizden **geniş** çıktı; gerekçeleri aşağıda.

| Talebiniz | Karar | Faz |
|---|---|---|
| **A1** · Kullanıcı sahipliği ve end-user HTTP yüzeyi | ✅ Kabul, kapsamı genişletildi | [147](#3-a1--yetki-kapısının-kaynak-kapsamı-faz-147) + [148](#4-a1--oturum-sahipliğinin-kalıcılığı-faz-148) |
| **A2** · Run'a bağlı kota eşiği SSE bildirimi | ✅ Kabul | [146](#2-a2--çalıştırmaya-bağlı-kota-eşiği-faz-146) |
| **F1** · Typed SSE sözleşmesi | ✅ Kabul — bunu "fikir" diye sunmuştunuz, bizim kusurumuz çıktı | [145](#1-f1--kayıtlı-olay-akışının-çerçeve-sözleşmesi-faz-145) |
| **F3** · Kalıcı kota notice ledger | ✅ Kabul — A2 ile aynı faza girdi | [146](#2-a2--çalıştırmaya-bağlı-kota-eşiği-faz-146) |
| **F2 · F6 · F7** | ⏸ Sıralanmadı — boşluk gerçek, talep kanıtı yok | — |
| **F4 · F5 · F8** | ❌ Açılmadı — gerekçeler [§7](#7-açılmayan-kalemler) |

**Faz kapılarınız için doğrudan cevap:**

| Sizin fazınız | Durum |
|---|---|
| Faz 8 · Chat geçişi | 🟢 **Kapı açıldı** — A1 ve A2 sevk edildi |
| Faz 9 · Canlı ses | 🟢 **Kapı açıldı** — ses hem yetki kapısına hem sahipliğe bağlandı |

**Ama iki uyarı okumadan cutover planlamayın:** [§5.2](#52-akışlı-yolda-ret-bir-error-çerçevesidir) (akışlı yolda ret `403` değil) ve
[§6](#6-sizi-etkileyen-iki-bilinen-kusur) (typed istemcide iki açık kusur).

### İddialarınızın doğruluğu

On dört iddianızı koda karşı ölçtük. **On üçü doğru çıktı.** Yanlış olan tek
iddia bir risk satırındaydı: `T:AgentPrism.MigrationDescriptor` public bir tip
değil, `internal sealed record` — R13'ün "hash'ler görünür" öncülü yanlıştı.
Bu, F7'nin gerekçesini de zayıflatıyor.

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
SSE bildiren işlem sayısı **6 → 7**. Path ve operation sayısı değişmedi
(126 / 163) — yeni uç yok.

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

Sıra ölçümünüz özellikle isabetliydi: `RunRecordingAgent.Completion.cs`'de
`Writer.CompleteAsync` otuz satır önce, `RecordQuotaAsync` sonra çalışıyordu.

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

Muhasebe artık terminal olay yazımından **önce** çalışır (K-680). Sıra:

```
kota kaydı + eşik claim  →  notice olayı  →  terminal olay
```

**Bedeli açıkça yazıyoruz:** kota artık `run` satırı kapanmadan önce tüketilir.
`CompleteAsync` hata verirse tüketim geri alınmaz. Token için para zaten
harcandığından bunu doğru davranış sayıyoruz.

`IRunEventSink`'in *"A sink failure never fails the run"* ilkesi korundu.
Bildirim hatası `run`'ı değiştirmez.

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
yazılan tüketici olaylarını reddeder. Yani bu notice'ı yalnız kütüphane
üretebilir; taklit edilemez.

**Korelasyon yoksa uydurulmaz.** `UserId` çözülemezse notice yine o `run`'ın
akışına yazılır (akış `run`'a aittir), ama başka bir aktif kullanıcı tahmin
edilmez.

**Çocuk kullanımı kökte bir kez bildirilir.** Alt `run` kota tüketmez ve notice
yazmaz.

### 🚨 Raporunuzda olmayan bir mekanizma — doğrudan POST akışı

Uygulama sırasında ölçüldü ve planda yoktu: **`scope.Writer.AppendAsync` ile
yazılan hiçbir olay doğrudan POST akışına yansımıyordu.** O akış yalnız MAF'ın
kendi `AgentResponseUpdate`'lerini `update` çerçevesi olarak iletiyor. Yani
"iki yoldan aynı payload" sözü gerçek bir mekanizma gerektiriyordu.

Çözüm: `AgentEndpoints`, akış bittikten sonra ve `done` yazılmadan önce
kalıcı kaydı **ikinci kez okur** ve yalnız `agentprism.quota.threshold`
olaylarını `event: custom` olarak yansıtır. Böylece iki yol aynı payload'ı
**inşa etmez, aynı kaydı okur** — ayrışma riski yok.

⚠️ **Bu mekanizma yalnız kota notice'ı içindir.** Kendi yazdığınız `Custom`
olayları (`prodigy.preview.created` gibi) doğrudan POST akışında **görünmez**;
yalnız `GET /api/runs/{id}/events` üzerinden okunur. Genel bir "tüm `Custom`
olaylarını doğrudan akışa yansıt" mekanizması **bilerek** açılmadı. Callback
eşlemenizin (`PreviewVersionAdded`, `ContentSaved`, `OntologyChanged`,
`AudioGenerating`) bu ayrıma göre kurulması gerekiyor.

### F3 — kalıcı tekilleştirme

`quota_usage` tablosuna `notified_thresholds` sütunu eklendi (K-682, CSV
kodlama, koşullu `UPDATE` ile atomik). Yeniden başlatma ve ikinci worker aynı
eşiği yeniden yayımlamaz. Üç sağlayıcıda sözleşme testiyle kilitli.

**Uyarınızı kabul ettik:** *exactly once delivery* sözü vermiyoruz. Sözleşme
"eşiğin **tespiti** dönem başına tekildir ve `NoticeId` idempotent teslim
kimliğidir" biçiminde yazılı. `IWebhookStore` teslim kaydı ayrı bir kavramdır.

**Bilinen sınır (F-199):** eşik claim edildikten sonra yayın başarısız olursa o
eşik dönem sonuna kadar kaybolur. Retry/backoff ayrı bir kalem — talep
ederseniz açarız.

### Entegrasyonunuz için

- `AgentPrism:Quota:PublishThresholdToRunStream=true` yazın. Varsayılan kapalı.
- FE `noticeId` ile dedup yapsın. Yeniden bağlanma aynı notice'ı tekrar okur.
- `IChatClient.QuotaWarning` karşılığı budur. `QuotaError` hâlâ ayrı: `run`
  başlatma `429`'u ve `AgentRunBudget` terminal sonucu.
- Kota enforcement'ın `AllowOnStoreFailure` ayarı **değişmedi**. Bildirim
  başarısı onu gevşetmez.

---

## 3. A1 — Yetki kapısının kaynak kapsamı (Faz 147)

### Karar

**Kabul edildi ve kapsamı sizin istediğinizden geniş tutuldu.**

Ölçüm beş olgunuzu da doğruladı. `RunEndpoints.cs` (992 satır) içinde
`RunAuthorizationGate` çağrısı **sıfırdı**; approval ve attachment uçlarında da
sıfırdı; ses yalnız kiracı denetliyordu.

### 🚨 Raporunuzda olmayan altı yüzey

Siz yalnız **okuma** grafiğini ölçtünüz. Planlama ve denetim ölçümü, gerçek bir
`run` **başlatan** ve kapıdan geçmeyen altı yüzey daha buldu:

| Yüzey | Ne yapıyor |
|---|---|
| `POST /api/runs/{id}/replay` | Kendi özeti *"Starts a new run with recorded input."* — katalogdan agent çözüyor |
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

`IRunAuthorizationHandler`'ın **metot sayısı değişmedi** — iki metot. Kaynak
erişimi de `AuthorizeRunAsync`'ten geçer.

🚨 **Endişenizi doğrudan karşılıyoruz:** enum'un sayısal değeri bir persistence
sözleşmesi **değildir**. `RunAuthorizationRequest` senkron bir istek kaydıdır ve
sonucu hiçbir yere yazılmaz. Bu cümle enum'un XML'ine açıkça yazıldı (K-683).

### Kapsam — ölçülen sayı

Kapı çağrısı **8'den 35'e** çıktı, 13 dosyaya yayıldı:

| Dosya | Çağrı |
|---|---|
| `RunEndpoints.cs` | 12 |
| `SessionEndpoints.cs` · `AttachmentEndpoints.cs` | 4 + 4 |
| `WorkflowEndpoints.cs` · `ApprovalEndpoints.cs` | 3 + 3 |
| `ObservabilityEndpoints.cs` · `EvalEndpoints.cs` | 2 + 2 |
| `AgentEndpoints` · `TriggerEndpoints` · `OpenAIResponses` · `OpenAIChatCompletions` · `VoiceConversationEndpoint` | 1'er |

### Ret kodları

| Erişim | Ret |
|---|---|
| Tekil kaynak (run · trace · attachment · approval · ses) | `404`, gövdesi **gerçekten var olmayan kaynakla birebir aynı** |
| Liste (`/api/runs`, `/api/attachments`, `/api/approvals/pending`) | `403` |
| Run başlatma (replay · chat completions · workflow devamı) | `403` |
| `POST /api/attachments` (yükleme) | `403` — adreslenen kaynak yok, gizlenecek kimlik yok |

Ret metni kapının kendisinden değil **çağıran uçtan** gelir. Ölçtük: ret metni
uçtan uca değişiyor (`"Run not found"` · `"Trace not found"` · `"Attachment not
found"`). Kapının tek metin üretmesi en az bir uçta gövde birebirliğini bozardı
ve o ayrışma bir varlık oracle'ı olurdu.

**Bir davranış değişikliği:** `GET /api/runs/{id}/tools` artık `run`'ı önce
okuyor; var olmayan `run` için `200 []` yerine `404` dönüyor (K-685). Ret `404`
dönerken yokluk `200 []` dönseydi ret bir varlık kanıtı olurdu.

### Ses

`SessionAccess.Voice` ile kapsandı. Ret `404` — erişilemeyen oturumun
gövdesiyle birebir (K-687). K-283 korundu: **var olmayan oturum reddedilmez**,
ilk tur onu açar.

### `/v1/conversations` da sahipliğe bağlandı

OpenAI uyumlu konuşma uçları (`POST /v1/conversations`,
`GET|DELETE /v1/conversations/{id}`, `GET …/items`) sahiplik kapısından geçiyor.
Bu yüzeyi kullanmıyorsanız sizi etkilemez; kullanırsanız aynı `404` semantiği
geçerlidir.

### Entegrasyonunuz için

- Tek bir `IRunAuthorizationHandler` yazın; `Access` üzerinden dallanın.
  `RunId` artık her kaynak erişiminde dolu.
- `Start` erişiminde `RunId` **replay'de doludur** ve kaynak `run`'ı gösterir.
  "Bu agent'ı çalıştır" ile "başkasının kayıtlı konuşmasını yeniden oynat"
  ayrımını buradan yapın.
- Handler kayıtlı değilken **hiçbir** davranış değişmez. Bunu kanıtlayan
  `RunResourceAuthorizationTests.Every_resource_endpoint_is_unchanged_when_no_handler_is_registered`
  denetim bulgusuyla genişletildi: ilk yazımı 16 ucu kapsıyordu, replay ·
  `/v1/chat/completions` · `DELETE …/feedback/{scoreId}` · HTTP ek yüklemesi
  sonradan eklendi. Workflow devam uçları için ayrı bir kanıt var.

---

## 4. A1 — Oturum sahipliğinin kalıcılığı (Faz 148)

### Karar

**Kabul edildi.** "Harici session-owner tablosu" ve "her kullanıcıyı ayrı
tenant" alternatiflerini reddetme gerekçeleriniz doğruydu; ikisini de biz de
reddettik.

### Sevk edilen API

```csharp
// AgentPrism.Abstractions
public sealed record SessionRecord { /* … */ public string? OwnerId { get; init; } }
public sealed record SessionQuery  { /* … */ public string? OwnerId { get; init; } }

public sealed class AgentPrismSessionOwnershipOptions
{
    public const string SectionName = "AgentPrism:SessionOwnership";
    public bool Enabled { get; set; }                       // varsayılan KAPALI
    public bool RequireAuthenticatedOwner { get; set; } = true;
    public string? ManagementPolicy { get; set; } = "AgentPrism.Operator";
}
```

`sessions` tablosuna `owner_id` sütunu ve `(tenant_id, owner_id, updated_at DESC)`
indeksi eklendi. Üç sağlayıcıda birer migration.

### Davranış — istediğiniz gibi

- **Sahip doğrulanmış kimlikten, yazma anında alınır.** Gövde ve etiketler
  sahibi değiştiremez.
- **Filtre `WHERE`'de, sayfalamadan önce.** Sözleşme XML'i bunu şöyle yazıyor:
  *"A store that filters the page it already fetched returns short pages, empty
  pages, and — worst — lets a caller infer how many sessions OTHER users hold
  from the gaps."* Sayfalama sözleşmesi bozulmuyor, sayı sızmıyor.
- **Sahiplik bir kez atanır.** Dört depo da `COALESCE` eder; "set → unset"
  geçişi yok. Dallandırma, Responses zinciri ve ses devamı **kaynağın**
  sahibini miras alır, çağıranın değil (K-689).
- **Mod açıkken kimlik çözülemezse `403`** (`errorType: session_owner_required`).
  Attribution bu modda bir yetkilendirme girdisidir, muhasebe etiketi değil
  (K-690).

### 🚨 Plandan iki sapma — ikisi de sizi ilgilendiriyor

**1. Kapı `run` başlatan yüzeylere de gerekti.** Fonksiyonel test yazılırken
görüldü: `POST /api/agents/{ad}/run` gövdesinde `sessionId` ile **başka
kullanıcının oturumunu sürdürmek** hâlâ mümkündü. Sahiplik korunuyordu (satır
el değiştirmiyordu) ama çağıran o konuşmanın tamamını modele geri okutmuş
oluyordu. Üç yüzey (`AgentEndpoints`, `WorkflowEndpoints`,
`OpenAIResponsesEndpoints`) sahiplik kapısına bağlandı. **Raporunuzun
A1.2.4 maddesi ("caller başka session ID'sini gönderebilir") tam olarak bu
yolu tarif ediyordu — haklıydınız.**

**2. Ambient scope tek başına yetmedi.** Kuyruğa alınmış `run`'ın sahibini iş
zarfına yazıp işçide geri oynatmak yeterli değildi: siz kendi
`IRunAttributionContext`'inizi kaydettiğinizde ambient scope hiç okunmuyordu ve
oturum, işçinin o an gördüğü kullanıcıya yazılıyordu. Sahiplik çözümünde
ambient scope artık kayıtlı servisi **ezer** (K-692). Bu dar istisna yalnız
sahiplik içindir; attribution okuyucusu değişmedi.

### ⚠️ Bir noktada istediğinizden azını yaptık

Talebiniz: *"Var olan owner'sız session'lar son kullanıcıya açık sayılmaz."*

Sevk edilen (K-693): sahipsiz eski satır **sahipli listede hiç görünmez**, ama
**tekil erişimde reddedilmez**.

Gerekçe: reddetmek, bayrağın açıldığı anda **canlı olan her konuşmayı** anında
sahipsiz bırakırdı — açılış anında devam eden her oturum kopardı.
Keşfedilebilirlik yine de kapanıyor: kullanıcı listede o satırı göremez.

Bu bizim kararımız, sizin talebinizin birebir karşılığı değil. **"Sahipsiz
satırları da reddet" seçeneğini talep ederseniz ayrı bir kalem olarak
açarız** — karar defterinde yeniden açılma koşulu olarak yazılı.

Geçiş planınızda eski oturum yoksa (Karar 2: eski `ChatMessage` verisi
taşınmıyor) bu fark sizi hiç etkilemez.

---

## 5. Cutover'dan önce okuyun

### 5.1 Yönetim payı

`ManagementPolicy` varsayılanı `"AgentPrism.Operator"`. Bu politikayı taşıyan
istek **filtresiz kiracı listesini** görür. AiOps console'unuz bununla çalışır;
son kullanıcı bu politikayı **almamalıdır**. Açık sorunuz *"AiOps operatörü tüm
kullanıcı konuşmalarını okuyabilir mi?"* — cevabı artık bir yapılandırma
anahtarıdır, bir kod değişikliği değil.

### 5.2 Akışlı yolda ret bir `error` çerçevesidir

🚨 SSE başlıkları (`200`, `text/event-stream`) oturum çözümünden **önce**
gönderilir. Bu fiziksel bir kısıttır (K-324 ile aynı sınıf). Akışlı yolda
sahiplik reddi bir `403` değil, bir SSE `error` çerçevesidir.

Fazın invariant'ı orada da korunur: **sahipsiz satır açılmaz**. Ama FE
parser'ınız iki ret biçimini de tanımalıdır:

| Yol | Ret biçimi |
|---|---|
| Akışsız (`Idempotency-Key`, buffered) | `403` + `errorType: session_owner_required` |
| Akışlı (varsayılan SSE) | `event: error` + aynı `errorType` |

İkisi de aynı `errorType`'ı taşır; bu bilerek yapıldı.

### 5.3 Sayı ve sürüm

| Ölçüm | Değer |
|---|---|
| OpenAPI path / operation | **126 / 163** — değişmedi, yeni uç yok |
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

Kendi `IQuotaStore` implementasyonunuz yoksa (yok — `UsePostgreSql`
kullanıyorsunuz) ilk ikisi sizi etkilemez.

---

## 6. Sizi etkileyen iki bilinen kusur

Faz 145'in bağımsız denetimi typed istemcide iki kusur ortaya çıkardı. İkisi de
**bu fazlardan önce** vardı; testler gerçek bir sunucuya karşı çağrı yapmadığı
için görünmüyordu. Karar 26 ile `AgentPrism.Client`'ı kullanmayacağınızı
biliyoruz — yine de bildiriyoruz.

**F-197 · Üretilen DTO koleksiyonları `null` varsayılanlı.**
`AgentRunRequest.Approvals`/`ToolResults`/`AttachmentIds`/`Documents` (ve ~250
üretilen tipte benzerleri) `= default!` yani `null` geliyor. Sunucu onları
koşulsuz `.Count` ile okuduğu için, yalnız `Message` set eden bir çağrı
`NullReferenceException` veriyor. **Geçici çözüm:** her koleksiyonu açıkça `[]`
yapın. Kalıcı çözüm aday listesinde.

**F-198 · `/v1/responses` ve `/v1/chat/completions` typed istemcide yalnız
JSON şeklini taşıyor.** `stream: true` gönderirseniz JSON deserialize hatası
alırsınız. Bu iki uç için `HttpClient` kullanın. Faz 145 saf-SSE beş ucun aynı
kusurunu düzeltti; bu ikisi çift içerikli olduğu için ayrı bir tasarım
gerektiriyor.

**Faz 147'nin kapattığı kusur:** `DELETE /api/runs/{runId}/feedback/{scoreId}`
başkasının skorunu silebiliyordu — [§3](#-raporunuzda-olmayan-altı-yüzey).
Skor puanlama kullanıyorsanız bu düzeltme sizi ilgilendirir.

---

## 7. Açılmayan kalemler

| Kalem | Karar ve gerekçe |
|---|---|
| **F2** · Zorunlu extension binding profili | ⏸ **Sıralanmadı.** Bilgi zaten var: `ExtensionPointDiagnostic` `Contract`/`Implementation`/`IsBuiltInDefault` taşıyor, ve `RequireRolePolicies` tam olarak istediğiniz zorlama deseninin emsali. Eksik olan yalnız anahtar. Küçük ve gerçek — **talep ederseniz açarız** |
| **F6** · Ses/WebSocket test harness'i | ⏸ **Sıralanmadı.** Boşluk gerçek: `AgentPrism.Testing`'de `FakeModelProvider` ve `SseReader` var, ses protokolü fixture'ı yok. Ama FE'nin VAD/WebSocket'i üstlendiğini yazdınız; talep kanıtı yok |
| **F7** · Migration plan artifact'i | ⏸ **Sıralanmadı.** Gerekçenizin öncülü yanlıştı: `MigrationDescriptor` public değil, `internal`. `SqlPersistenceDiagnosticsSnapshot` yalnız `CanConnect` + `PendingMigrations` adlarını taşıyor. Boşluk gerçek, değeri düşük |
| **F4** · Yazma hacmi planlayıcısı | ❌ **Açılmadı.** Kendi belgeniz "GB/ay vaat edilmemeli" diyor; ölçülmüş bir talep yok |
| **F5** · Çok dilli guard corpus'u | ❌ **Açılmadı.** Faz 140 `ContentGuardContext.Source` ayrımını sevk etti. Kalan iş bir test verisi bakımıdır; sabit veri kümesine ezberleme riskini kendiniz de yazmışsınız |
| **F8** · Generator kontrat paketi | ❌ **Açılmadı.** Asıl şikâyet beklenti ve doküman; Faz 127 tool kayıt yüzeyini kısmen kapattı. `AddToolsFrom<T>` açık kayıt zaten doğru yol |

Talep kanıtı gelirse F2 · F6 · F7 yeniden yargılanır. "Bunu şu fazımız
bekliyor" cümlesi yeterli bir kanıttır.

---

## 8. Sizden beklediğimiz

1. **Sahipsiz eski satır kararı.** [§4](#️-bir-noktada-istediğinizden-azını-yaptık) — tekil erişimde de reddedilmesini
   istiyor musunuz? Geçişinizde eski oturum yoksa cevap "hayır" olabilir.
2. **F2 · F6 · F7'den birine ihtiyacınız var mı?** Varsa hangi fazınızı
   bekletiyor?
3. **Kota iş birimi.** Açık sorunuz duruyordu: dönem kotası tenant ortak
   bütçesi mi, kullanıcı başına mı? A2 notice'ı ikisini de taşır (`tenantId`
   ve `userId` payload'da), ama **kota kuralının kendisi** hâlâ
   tenant + agent + dönem kapsamındadır. Kullanıcı başına dönem hakkı
   istiyorsanız bu ayrı bir taleptir.

**Yanlış anladığımız bir yer varsa söyleyin — ölçümü tekrarlarız.**

---

## Ek — doğrulama

Bu yanıt yazılmadan önce koşulan ölçümler (commit `fc2f9d8d`, macOS/arm64,
2026-09-06). Faz dokümanlarının iddiası değil, **yeniden ölçülen** sonuçlar:

| Ölçüm | Sonuç |
|---|---|
| Çalışma ağacı | temiz |
| `RunEventType` üye sayısı ↔ adlandırılan | **31 ↔ 31**, `unknown`'a düşen yok |
| OpenAPI `AgentPrismStreamRunEvents` 200 | `content: {"text/event-stream": {"schema": {"type": "string"}}}` |
| OpenAPI path / operation | 126 / 163 |
| `RunAccess` üyeleri | `Start` · `Read` · `Cancel` · `Feedback` · `Attachment` · `Approval` |
| `SessionAccess` üyeleri | `Read` · `List` · `Delete` · `Branch` · `Voice` |
| Yetki kapısı çağrı yeri | 35 (13 dosya) |
| Sahiplik kapısı çağrı yeri | 11 (6 dosya) |
| `RunEventCustomTypes.QuotaThreshold` | `"agentprism.quota.threshold"` |
| Migration setleri | PostgreSQL 46 · SQL Server 34 · SQLite 34 |
| `dotnet build AgentPrism.slnx -c Release` | ✅ **0 uyarı · 0 hata** (74 s) |
| `dotnet test AgentPrism.slnx` — 1. tam koşum | ⚠️ 2 düşen (aşağıda) |
| `dotnet test AgentPrism.slnx` — 2. tam koşum | ✅ **0 düşen** |
| `secret` taraması · doküman denetimi · script testleri · yetenek haritası | ✅ dördü de temiz |

### İlk koşumdaki iki düşen test

Dürüst olmak için yazıyoruz: **ilk tam koşumda iki test düştü.**

| Test | Düşme yeri |
|---|---|
| `SqlServer.IntegrationTests.DataSubjectStoreTests.Erase_cascades_from_run_to_run_inputs` | Test **gövdesinde değil**, `DisposeAsync` → `DropSchemaAsync` içinde — `Execution Timeout Expired` (Win32 258, kilit bekleme) |
| `Ui.E2ETests.UiTests.Dashboard_charts_render_and_range_can_be_changed` | Playwright zaman aşımı |

İkisi de **izole koşumda geçti** (kapı betiğinin kendi teşhis adımı) ve
**ikinci tam koşumda da geçti** (0 düşen). Bu, bu depoda belgelenmiş bir
sınıftır: tam çözüm koşumunda 24 test yürütülebiliri aynı CPU/RAM bütçesine
yükleniyor ve `mssql/server` bu makinede amd64 emülasyonunda çalışıyor
(K-386). Ürün kusuru değildir.

Sizin CI'nızda SQL Server kullanmayacaksınız (Karar 17: PostgreSQL). Bu
gözlemi yalnız "yeşil dedik ama ilk koşum kırmızıydı" durumunu gizlememek
için yazıyoruz.

### Doğrulanmayanlar

Canlı PostgreSQL/MinIO/provider ile uçtan uca smoke testi bu ölçümde
yapılmadı; örnek uygulama koşumları SQLite ve gerçek OpenAI çağrısıyla faz
kapanışlarında yapıldı ve çıktıları faz dokümanlarında duruyor. Sizin
ortamınızdaki smoke testi bunun yerine geçmez.

Sürüm henüz **yayımlanmadı**. Bu yanıt `fc2f9d8d` commit'ini anlatır; NuGet
paketi çıktığında pin'i güncelleyip ölçümü tekrarlayın.
