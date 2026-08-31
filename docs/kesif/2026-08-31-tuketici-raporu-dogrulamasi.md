# Tüketici Uygulanabilirlik Raporunun Doğrulanması

> **Kimden:** AgentPrism geliştirme tarafı · **Tarih:** 2026-08-31
> **Neye yanıt:** `agentprism-uygulanabilirlik-analizi-2026-08-31.md`
> (ProdigyEnabler Backend · "AgentPrism merkezli dönüşüm önerilir")
> **Ölçüm tabanı:** `8105c00` · her iddia kaynak kodda yeniden ölçüldü

---

## 0. Kısa cevap

Rapor iyi yazılmış ve çoğu yerde doğru. Ana kararı ("generic runtime AgentPrism,
domain orchestration ProdigyEnabler") bizim kendi mimari duruşumuzla birebir
örtüşür. Bu doküman o kararı tartışmaz; **ölçümle çürüyen 11 iddiayı** (§2) ve
**raporun kaçırdığı 3 riski** (§3) sayar.

Raporunuz bizde de iş çıkardı. Üç kusur bulundu; **ikisi düzeltildi ve sevk
edilecek**, biri planlandı:

| Bulgu | Durum |
|---|---|
| Oturumun ikinci ve sonraki kayıtlarında son-yazan-kazanır (§3, R-2) | ✅ Düzeltildi — her kayıt artık eşzamanlılık denetiminden geçiyor |
| Olay dokümanının payload'da olmayan alan vaat etmesi (§5) | ✅ Düzeltildi — iki vaka + kalıcı kapı |
| Sağlayıcı yedeklemesinin tool turunu baştan çalıştırması (§3, R-1) | 📋 Planlandı; **kapsamı daraldı — akışlı yollarınız bugün güvende, §3'e bakın** |

Ayrıca raporunuzun doğru çıkan beş iddiası **plana dönüştü** (§8). Gap
listenizden düşürmeyin, ama "AgentPrism'de yok" yerine "AgentPrism'de
planlandı" diye okuyun — ikisi farklı bir ADR üretir.

Sizin tarafınızda en çok işe yarayacak üç cümle:

1. **`IRunStore.ReadEventsAsync(runId, fromSequence)` public'tir.** "Yüksek
   öncelikli gap" dediğiniz transport-nötr olay imleci bugün vardır. SignalR
   köprünüz için yeni bir API beklemeyin.
2. **Hangfire job'ının içinden AgentPrism çalıştırmak için yeni bir API
   gerekmiyor.** `IAgentCatalog.ResolveAsync` + `AmbientTenantScope` +
   `AmbientRunAttributionScope` bugün yeterlidir; AgentPrism kuyruğu tek satır
   yapılandırma ile kapanır (`Scheduling:RunWorker = false`).
3. **Tool şeması iddianız ters yönde.** Üreteç `description` alanını **hiç**
   yazmıyor; sizin `BackendToolRegistry` yazıyor. Bu eksende AgentPrism
   bugün **daha zayıftır** — ve bu bizim tarafımızda bir faz adayı oldu.

Raporun "Doğrulanamadı" etiketli yedi kaleminin **üçü aslında dokümante
edilmiştir** (§4). O üçü için prototip koşmanıza gerek yok; bütçenizi §4'ün
sonunda kalan üç gerçek belirsizliğe ayırın.

---

## 1. Sürüm etiketi — ölçüm tabanı hakkında bir düzeltme

Rapor "İncelenen AgentPrism sürümü: `0.0.0-preview.0.486`" diyor ve aynı
bölümde repository commit'ini `8105c005…` olarak kaydediyor. Bu ikisi aynı
şeyin iki adıdır: `0.0.0-preview.0.486` **yayımlanmış bir sürüm değil**, o
commit'ten üretilmiş yerel bir CI derlemesidir. Yayımlanmış aile
`1.0.0-preview.N`'dir (`CHANGELOG.md`, 2026-08-28).

Pratik sonucu tek: `reference/versioning.md`'nin "paket ailesini tek sürüme
sabitle" kuralı yerel bir derleme numarasına uygulanamaz. **ADR yazmadan önce
yayımlanmış `1.0.0-preview.N` ailesine geçin**; aksi hâlde §14'teki Faz 0'ın
"`.486` migration ayrı deploy adımında çalışır" kabul kriteri tekrar
üretilemez bir tabana yaslanır.

Raporda "`.486` içinde" diye geçen her ifade, bu dokümanda "`8105c00`'da" diye
okunmuştur.

---

## 2. Ölçümle çürüyen iddialar

Her satır kendi kopyanızda `dosya:satır` ile doğrulanabilir.

### Y-1 · "Transport-nötr kalıcı run olayı imleci yok" — **YANLIŞ**

> Rapor §11, Gap tablosu, **Yüksek**: *"SignalR-neutral resumable event cursor
> … `IRunEventReader.ReadAfterAsync(runId, sequence)` için açık host API ve
> cursor contract"*

İstenen API vardır, adı farklıdır:

```csharp
// src/AgentPrism.Abstractions/Runs/IRunStore.cs:191
IAsyncEnumerable<RunEvent> ReadEventsAsync(
    Guid runId,
    long fromSequence = 0,
    CancellationToken cancellationToken = default);
```

`IRunStore` public bir arayüzdür ve bu metodun kendi dokümanı şunu yazar:
*"Live streaming and historical replay take the same path."* Sıra numarası
sözleşmesi de yazılıdır: `RunEvent.Sequence` tek yazardan gelir ve run içinde
0'dan artar (`RunEvent.cs:11`); `AppendEventAsync` aynı sıra numarasının ikinci
kez yazılmasını `AgentPrismException` ile **reddetmek zorundadır**
(`IRunStore.cs:139-152`).

SignalR köprünüzün ihtiyacı olan şey budur:

- **Canlı yol:** `IRunEventSink.OnEventAsync` → `IChatClient` projeksiyonu.
- **Reconnect:** `ReadEventsAsync(runId, sonTeslimEdilenSıra + 1)`.

`SseWriter`'a bakmanıza gerek yok (bkz. Y-9).

### Y-2 · "Dış job içinde durable run için yeni API gerekir" — **BÜYÜK ÖLÇÜDE YANLIŞ**

> Rapor §11, **Yüksek**: *"`RunInExternalJobAsync(ExternalJobContext, …)` veya
> queue-neutral durable execution API"* · Risk tablosu: *"Üçüncü queue
> oluşması … Kabul edilmez"*

Üç ölçüm:

| İddia | Ölçüm |
|---|---|
| "AgentPrism kuyruğu eklemek zorundayız" | Hayır. `AgentPrismSchedulingOptions.RunWorker = false` → `JobWorkerBackgroundService` hiç iş kiralamaz (`JobWorkerBackgroundService.cs:45`). Kuyruk ve zamanlama depoları çalışır, **bu süreçte hiçbir job yürütülmez**. Üçüncü kuyruk bir varsayılan değil, bir tercihtir; anahtar `guides/background-work.md` § *Configure the worker* ve § *Separate API and worker processes* içinde dokümante edilmiştir |
| "Run kaydı için AgentPrism'in kendi kuyruğundan geçmek gerekir" | Hayır. `IAgentCatalog.ResolveAsync` **zaten kayıt sarmalayıcısıyla sarılmış** bir `AIAgent` döndürür (`IAgentCatalog.cs:20-22`, `:32`). Hangfire job'ının gövdesinde `RunAsync`/`RunStreamingAsync` çağırmak run kaydı, olaylar, usage, cost, hata sınıflandırması, telemetri, bütçe ve iptal kaydını üretir |
| "Correlation, attribution ve cancellation boilerplate yazmamız gerekir" | Kısmen. İkisi hazır: `AmbientTenantScope.Begin(tenantId)` ve `AmbientRunAttributionScope.Begin(userId, labels)`. İkisinin de XML dokümanı bu senaryoyu adıyla anar: *"a queued job, a scheduled run or a direct .NET API call has no request to resolve from"* |

**🚨 Tek gerçek tuzak** — ve rapor bunu kaçırmış: `AmbientRunAttributionScope`
bir `AsyncLocal`'dır ve **`async` bir yardımcı metotta açılan yazım çağırana
geri akmaz**. Scope, run'ı **başlatan metodun kendi gövdesinde** açılmalı ve
akışlı yolda **her `MoveNextAsync` öncesinde** açık kalmalıdır
(`AmbientRunAttributionScope.cs:30-38`). Bu bizim dört kez bedelini ödediğimiz
tuzaktır; sizin `ChatResponseStreamer` yolunuz tam olarak bu şekle sahiptir.

Geriye kalan **gerçek** eksik: bu deseni gösteren bir **örnek proje yok**
(`samples/` altında dış kuyruk entegrasyonu yoktur). Bu bir doküman
eksikliğidir, bir yetenek boşluğu değil. Doküman işi olarak kaydedildi ve
[Faz 127](../127-TOOL-KAYIT-YUZEYI.md)'nin doküman senkronuna bağlandı.

### Y-3 · "Captive scoped tool bağımlılığı — kabul edilmez" — **YANLIŞ ÇERÇEVE**

> Rapor §8.1 ve Risk tablosu: *"Captive scoped tool dependency … Kabul
> edilmez"* · Gap tablosu aynı kalemi **Orta** sayar (raporun kendi içinde
> çelişki)

Desen dokümante edilmiştir ve reçetesi verilmiştir:

> *"Do not resolve dependencies from `AIFunctionArguments.Services`: MAF
> supplies an empty provider. Resolve singleton dependencies when you register
> an `AIFunction`. For scoped work, inject `IServiceScopeFactory` into that
> registration and create a scope inside the invocation."*
> — `docs-site/src/content/docs/guides/write-your-own-tool.md:44-67`
> (çalışan kod örneğiyle birlikte)

Ayrıca tool gövdesi **run bağlamını zaten görür**:
`AgentPrismRunContext.Current` public'tir ve `RunId`, `RootRunId`, `Depth`,
`AgentName`, **`TenantId`**, `SessionId`, `Budget`, `AgentVersion` taşır.
Eyleyen kullanıcı da okunabilir: `AmbientRunAttributionScope.CurrentUserId`.

Yani §8.1'in *"Tenant ve principal context'i scope açılmadan önce
kurmalıdır"* cümlesi, **job sınırında bir kez** yapılacak işi tarif eder;
tool başına tekrarlanacak bir iş değildir.

Kalan eksik: `AddScopedTool<THandler>()` gibi tek satırlık bir kolaylık. Bu bir
ergonomi boşluğudur — sizin kendi tablonuzdaki "Orta" doğrudur, "Kabul
edilmez" değil. **Planlandı:** [Faz 127](../127-TOOL-KAYIT-YUZEYI.md), `AddScopedTool`.

### Y-4 · "B10 — üretilen şema mevcut şemadan güçlüdür" — **TERS YÖNDE YANLIŞ**

> Rapor Tablo 6, B10: *"Çözer. Generated `AIFunction` JSON Schema, mevcut basit
> reflection schema'dan güçlüdür."*

Üretecin gerçekte yazdığı şema (`SourceWriter.cs:212-263`):

```json
{"type":"object","properties":{ … },"required":[ … ],"additionalProperties":false}
```

Yaprak düğümler yalnız şunlardır: `boolean` · `integer` · `number` · `string` ·
`string`+`format:uuid` · `string`+`format:date-time` · `string`+`enum[…]`.

Bulunmayanlar:

- **Parametre başına `description` — hiç yok.** Üreteç yalnız tool düzeyinde
  açıklama okur (`ToolCandidate.cs:187-217`). Sizin `BackendToolRegistry`
  raporunuza göre *"property tipi, açıklama ve required"* üretiyor. Modelin
  hangi tool'u ve hangi argümanı seçtiğini en çok etkileyen alan budur; bu
  eksende **AgentPrism bugün sizin sisteminizden geridedir**.
- `minimum` / `maximum` / `pattern` yok.
- **İç içe nesne ifade edilemez.** `ParameterTypeValidator` skaler, enum ve
  bunların dizisi dışındaki her parametreyi APG0003 ile **derleme anında
  reddeder**.

Pratik sonucu: Faz 3 test planınızdaki *"nested schema"* case'i üretecin ifade
bile edemediği bir şeyi test eder. Ya sarmalayıcı `AIFunction`'ları elle
yazacaksınız (`AIFunctionFactory.Create` yolu — nesne parametresi kabul eder),
ya da 23 handler'ın imzalarını düzleştireceksiniz. Bu karar Faz 3'ün
kapsamını doğrudan değiştirir.

İki kalem doğurdu; ikisi de [Faz 125](../arsiv/fazlar/125-URETILEN-TOOL-SEMASININ-IFADE-GUCU.md)'te
planlandı: parametre açıklaması ve ifade sınırının ilanı.

### Y-5 · "Prompt caching → yalnız Claude için yüksek" — **DAR**

> Rapor Tablo 2: *"Anthropic prompt cache açılabilir … Yüksek Claude için"*

`CachedInputTokens` sağlayıcıdan bağımsız okunur:

```csharp
// src/AgentPrism.Core/Recording/UsageBreakdown.cs:36
public static long? CachedInputTokens(UsageDetails usage) => usage.CachedInputTokenCount;
```

Fiyat da öyle: `ModelDescriptor.CachedInputCostPerMillionTokens`'ı OpenAI,
Azure ve Google yapılandırma okuyucularının **üçü de** doldurur. OpenAI'ın
otomatik önbelleği hiçbir ayar açmadan ölçülür ve fiyatlanır.

`anthropic.promptCaching` ayarının var olmasının sebebi, Anthropic'in
önbelleğinin **API'de opt-in** olmasıdır. Bu bir sağlayıcı özelliğidir, bir
AgentPrism sınırı değil. B26'yı "yalnız Claude" diye planlamayın — cache
kazancınız OpenAI yollarında da ölçülür.

### Y-6 · "B15 — guard'lar tek dilli" — **YANLIŞ ÖNCÜL**

AgentPrism **hiçbir dile bağlı hazır kural seti sevk etmez.**
`PatternContentGuardOptions` iki alan taşır: `DeniedTerms` (varsayılan **boş**,
tüketici doldurur, `:37`) ve `MaskedPii` (varsayılan `PiiPatterns.None`, `:43`).

Yerleşik aile yapısaldır ve dilden bağımsızdır: `Email` · `Iban` ·
`CreditCard` (Luhn doğrulamalı) · `TurkishNationalId` (onuncu ve on birinci
basamak kuralı doğrulamalı) · `ProviderApiKey`. Beş aileden biri zaten
Türkçe'ye özeldir.

"Dil kapsamı" sorusu tamamen sizin `DeniedTerms` listenizin ve kendi
`IContentGuard`'ınızın sorusudur. Ölçülecek bir AgentPrism davranışı yok.

### Y-7 · "B27 — domain idempotency otomatik değildir" — **EKSİK ÖLÇÜM**

Kesintiye uğramış run'ın devamı, raporun kabul ettiğinden daha fazlasını yapar
(`guides/reliability.md`, *"Continue an interrupted run automatically"*):

- Kesilen run'ın **tamamlanmış her tool çağrısı, kendi kayıtlı sonucundan
  cevaplanır**; yeniden çalışmaz. Eşleştirme **birebir kayıtlı argümanlara**
  göredir, tool adına göre değil.
- Yıkıcı veya süreç dışına çıkan bir tool (silme, ödeme, webhook) devamı
  **varsayılan olarak bloke eder**; run `Failed` kalır ve olay akışına
  `RunContinuationBlocked` yazılır (`RunEventType.cs:25`, `:185`).
- Tool yazarı bir çağrının tekrarlanabilir olduğunu **kanıtlayabiliyorsa**
  (tipik olarak kendi idempotency anahtarını taşıdığı için) o **tek** tool'u
  `SafeToRepeat` ile geri açar.

Yani "domain idempotency" kesinti senaryosunda kapı **varsayılan kapalıdır** ve
tüketiciye bırakılmamıştır.

Doğru kalan tek kısım: `TimeoutAIFunction` gövdeyi **zorla durduramaz**. Bu
dokümante edilmiş bir sınırdır (`TimeoutAIFunction.cs:23-28`: *"This is a
documented limit, not a bug"*) ve .NET'in kendi sınırıdır — bir tasarım tercihi
değil. `CancellationToken` gövdeye **geçirilir**; raporun istediği "token
propagation" zaten vardır.

### Y-8 · "Dağıtık canlı iptal capability'si eksik" — **SEAM VAR**

`IRunCancellationRegistry` public bir arayüzdür, `TryAddSingleton` ile
kayıtlıdır ve kendi XML dokümanı değiştirme yolunu adıyla söyler:

> *"A setup that needs a distributed registry can replace this interface with
> its own implementation (`TryAddSingleton`)."*
> — `IRunCancellationRegistry.cs:9-15`

Ayrıca **kuyruktaki** run'ın iptali bugün de süreçler arasıdır:
`IJobStore.CancelAsync` (`RunEndpoints.cs:718-745`).

Eksik olan, sevk edilmiş bir SQL implementasyonudur; genişleme noktası değil.
Sizin tek örnekli kurulumunuzda bugün gereksizdir.

### Y-9 · `SseWriter.ReadResumeSequence` kanıt olarak gösterildi — **PUBLIC DEĞİL**

```csharp
// src/AgentPrism.AspNetCore/Streaming/SseWriter.cs:21
internal sealed class SseWriter
```

Gösterdiği davranış (SSE `Last-Event-ID` ile devam) gerçektir, ama **tip
tüketiciden referanslanamaz**. Bu, raporun kanıt sütununda tekrarlanan bir
sınıf hatasıdır: `RunRecordingAgent` ve `AgentPrismMetrics` public'tir,
`SseWriter` değildir. Ayrım önemlidir — yalnız public yüzey uyumluluk sözü
taşır (`reference/compatibility.md`).

SignalR köprünüzün public karşılığı Y-1'dedir.

### Y-10 · "Kiracı/use-case bazlı model routing yok" — **KISMEN YANLIŞ**

> Rapor §11: *"Hedef `AiModelBinding` alias, tenant ve use-case policy ister"*

Kiracı ve use-case ekseni bugün çözülebilir:

- **Kendi `IAgentSource`'unuz** yazabilirsiniz; arayüzün dokümanı açıkça izin
  verir: *"A source may be global or tenant-aware. Reading `ITenantContext` is
  valid."* (`IAgentSource.cs:21-22`) Rehber: `guides/write-your-own-agent-source.md`.
- Kiracı bazlı sağlayıcı/anahtar bağlaması zaten vardır
  (`ITenantProviderBindingStore`, BYOK).

Gerçekten yok olan: **maliyete/gecikmeye tepki veren** dinamik router. Gap
metninizi bu tek eksene daraltın; aksi hâlde bugün yazabileceğiniz bir adaptörü
bekliyor olursunuz.

### Y-11 · "ABP integration paketi — Yüksek öncelikli gap" — **AŞIRI DERECELENDİRİLMİŞ**

Saydığınız dört ihtiyacın dördü de bugün açık bir genişleme noktasına düşer:

| İhtiyaç | Bugünkü yüzey |
|---|---|
| `ICurrentTenant` | `ITenantContext` (değiştirilebilir) + `AmbientTenantScope` (arka plan işi için) |
| `ICurrentUser` / attribution | `IRunAttributionContext` + `AmbientRunAttributionScope` |
| Permission | `IToolAuthorizationHandler` — `ToolAuthorizationRequest` `TenantId`, `UserId`, `RequiredPermission`, `Effect`, `RunId`, `AgentName` taşır; handler **fırlatırsa çağrı reddedilir** (fail-closed) |
| UoW | Ortak transaction **bilinçli olarak yoktur** ve nedeni yazılıdır (`guides/ef-core.md`, *"There is no shared transaction"*). Aynı sayfa sizin bağımsız olarak önerdiğiniz `RunId` referans desenini de veriyor |

Bu bir "Yüksek" yetenek boşluğu değil, bir **paketleme kolaylığı** talebidir.
Ayrı bir NuGet paketi bizim tarafımızda en pahalı değişiklik türüdür; reçetesi
doküman kanalında ele alındı ([Faz 127](../127-TOOL-KAYIT-YUZEYI.md) doküman senkronu).

---

## 3. Raporun kaçırdığı üç risk

Bunlar sizin geçiş planınızı doğrudan etkiler. **R-2 düzeltildi**, R-1 faza
dönüştü, R-3 zaten bizim dokümanımızda yazılıydı ve raporda taşınmamıştı.

### R-1 · 🚨 Sağlayıcı yedeklemesi tool turunu **baştan** çalıştırır

`FallbackChatClient` tool döngüsünün **dışında** oturur ve kendi dokümanı
sonucu açıkça yazar:

> *"Because the loop is behind this client, not in front of it, **a fallback
> restarts the agent's tool-call turn from scratch** on the fallback provider."*
> — `FallbackChatClient.cs:30-35`

Yürütme yolunda `SafeToRepeat` veya `ToolEffect` **kontrolü yoktur**
(`FallbackChatClient.cs:107-158`). Yani birincil sağlayıcı bir
`ToolEffect.External` tool'u çalıştırdıktan **sonra** geçici bir hata verirse,
yedek bağlantı aynı tool'u tekrar çalıştırabilir.

Bu, kesinti devamı yolundaki kapının (Y-7) tersidir — orada aynı risk
varsayılan olarak bloke edilir. Sizin `SaveContent`, skill mutation ve görsel
tool'larınız tam olarak bu sınıfa girer.

### 🚨 Kapsam daraldı — planlama turunda ölçüldü

Bu, azaltmanızı doğrudan değiştirir: **kusur yalnız akışsız
`GetResponseAsync` yolundadır. Akışlı yol bugün zaten kapalıdır.**

```csharp
// src/AgentPrism.Core/Models/FallbackChatClient.cs:200-206
// 🚨 A stream cannot be retried past its first frame: once a chunk
// reached the caller, falling back would either duplicate it or
// corrupt the sequence.
catch (Exception ex) when (sawUpdate || (reason = ClassifyFailure(ex)) == FallbackSkipReason.None)
{
    throw;
}
```

Akışlı yolda yedeğe geçiş **ancak ilk kare gelmeden önce** olur; o noktada
hiçbir tool çalışmamıştır. Tool döngüsü akışta çağrı içeriğini kareye
çevirdiği için, bir tool koştuysa `sawUpdate` çoktan `true`'dur.

**Sizin için bugünkü azaltma — güncellendi:**

- **Akışlı `run` yollarınız** (`ChatMessageProcessor` → `ChatResponseStreamer`)
  bu riski **taşımıyor**. Orada `Fallbacks`'ı kapatmanıza gerek yok.
- **Akışsız yollarınız** — `QAGenerationJob`, `AssessmentGenerationJob`,
  `ConvAIEvaluationJob`, `ArticleContentValidator` ve tool taşıyan her akışsız
  çağrı — risk altındadır. Yan etkili tool taşıyan bu tanımlarda
  `ModelBinding.Fallbacks` listesini düzeltme sevk edilene kadar **boş
  bırakın**.
- Risk tablonuza giren satır bu yüzden "yedekleme yan etkili tool'u tekrar
  çalıştırır" değil, **"akışsız yedekleme yan etkili tool'u tekrar
  çalıştırır"** olmalıdır. Faz 1'iniz (`QAGenerationJob`) tam olarak bu
  sınıftadır.

**Bizim tarafımızdaki karar:** bu, kesinti devamı yolunun deseniyle
kapatılacak — yedeklemeye geçilirken **tamamlanmış tool çağrıları kayıtlı
sonuçlarından cevaplanır**, yeniden çalıştırılmaz. Aynı riske aynı cevabı
vermek, ucuz olanı seçmekten önemliydi.

Bir ayrıntı sizi ilgilendirir: defterden cevaplanan çağrılar **yalnız yıkıcı
olanlarla sınırlı değildir.** `(ad, argüman)` çifti eşleşen **her** tamamlanmış
çağrı kayıtlı sonucundan cevaplanır; eşleşmeyen çağrı canlı koşar. Yani yedek
model aynı soruyu sorarsa aynı cevabı alır, yeni bir soru sorarsa tool gerçekten
çalışır. Tek eşleştirme kuralı olması, aynı riske ürünün iki farklı kuralı
olmasından değerliydi.

Bu rapordaki diğer düzeltmelerle aynı sürüme yetişmeyebilir; yukarıdaki
azaltmayı şimdilik uygulayın.

### R-2 · Aynı oturumun ikinci ve sonraki kayıtlarında **son yazan kazanır**

`AgentSessionManager.SaveSessionAsync` iki farklı yol izler:

- **İlk kayıt** atomiktir: `ISessionStore.TryCreateAsync`, kaybeden
  `AgentPrismSessionConflictException` alır.
- **Sonraki her kayıt** koşulsuzdur: *"Subsequent saves (and EVERY save of a
  session that was already found existing) continue, unchanged, to use the
  unconditional `ISessionStore.SaveAsync`."* (`AgentSessionManager.cs:157-161`)

Yani var olan bir oturumda **eşzamanlı iki tur** çalışırsa, ikinci kayıt
birincinin mesajlarını sessizce düşürür. Optimistic concurrency veya sürüm
alanı yoktur.

Sizin `ChatHub` → job → processor zinciriniz bir oturumda birden çok iş
kuyruğa alabiliyor (`ChatToolCallCoordinator` tool sonrası **yeniden enqueue**
ediyor). §15.2'de "AgentPrism session concurrent turn conflict davranışı"nı
prototip listesine almanız doğruydu; **cevabı budur ve prototip gerekmez.**
Faz 4'ün tasarımı oturum başına tek uçuşta tur garantisi vermelidir.

**✅ Düzeltildi (2026-08-31).** Kapsam kararı değil, gözden kaçmaydı. İlk kaydın
çakışma davranışı sonraki her kayda taşındı:

- `ISessionStore.TryUpdateAsync(record, expectedVersion)` ve
  `SessionRecord.Version` eklendi; `sessions` tablosu üç SQL sağlayıcıda
  `version` sütunu aldı. Koşul ve artırım **aynı `UPDATE` ifadesindedir**, yani
  kontrol ile yazım bölünemez.
- Var olan bir oturumda eşzamanlı ikinci tur artık
  `AgentPrismSessionConflictException` alıyor — retry **çağırana** düşüyor.
  AgentPrism'in içeride sessizce retry denemesi reddedildi: kaybeden turun
  hangi kararla düştüğünü sizden gizlerdi.
- Sözleşme testi (`SessionStoreContract`) bunu dört depoda birden zorluyor:
  PostgreSQL, SQL Server, SQLite ve bellek içi.

**Sizi ilgilendiren iki ayrıntı:**

1. **Çakışma HTTP'de artık her yerde aynı görünüyor.** Eskiden yalnız
   `/api/agents` akışsız dalı 409 veriyordu; OpenAI-uyumlu akışsız uç aynı
   duruma **502** diyordu — yani bir istemci yarışını "sağlayıcı bozuk" diye
   raporluyordu. O da 409'a alındı. Akışlı dallarda başlıklar zaten gönderilmiş
   olduğu için orada tipli bir `error` frame'i doğru davranıştır.
2. **Farklı bir kimliğe kayıt hâlâ koşulsuzdur ve öyle kalmalıdır.** Responses
   ucunun `previous_response_id` zincirlemesi oturumu bir kimlikten okuyup
   **başka** bir kimliğe yazar; orada karşılaştırılacak bir nesil yoktur.
   Kendi adaptörünüz bir oturumu okuduğu kimlikten farklı bir kimliğe
   yazıyorsa aynı kural geçerlidir.

### R-3 · `IRunEventSink` sıcak yoldadır ve **bir kez hata verirse run boyunca kapanır**

> *"`OnEventAsync` is awaited before the run's response continues streaming to
> its own caller — queue the event and return, do not block on further I/O."*
> *"A sink failure never fails the run. The writer … logs the exception,
> **disables the sink for the rest of the run** and keeps going."*
> — `IRunEventSink.cs:7-18`

SignalR köprünüz bu sink'in içinde olacak. `IHubContext` çağrısını doğrudan
`OnEventAsync` içinde `await` ederseniz: (a) model akışını yavaşlatırsınız,
(b) tek bir geçici SignalR hatası **o run'ın geri kalanının tamamını**
sessizce sizin FE'nize ulaşmaz hâle getirir. Köprü bir kanal/queue arkasına
alınmalıdır. Faz 6'nın kabul kriterlerine bu girmelidir.

Bu bizim tarafımızda bir boşluk değildir — kural yazılıdır ve kontrol listesine
girmiştir: *"`IRunEventSink.OnEventAsync` never performs blocking I/O inline —
it queues and returns"* (`guides/embedding.md:240`), kuyruklayan örnek sink
`concepts/runs.md:98-104`'tedir. Raporun §7.2'de *"`IRunEventSink`, mevcut
`IChatClient` event'lerine projection yapabilir"* derken bu kısıtı taşımaması
bir eksiktir.

---

## 4. "Doğrulanamadı" etiketi yanlış konulmuş kalemler

Bu üçü §17'de "kritik doğrulanamayan" olarak listelenmiş ve §18'de prototip
kuyruğuna alınmış. Üçü de **dokümante edilmiştir**; prototip bütçenizi
harcamayın.

| Rapordaki kalem | Gerçek durum |
|---|---|
| §17.4 · "Bütün provider'larda aynı structured output garantisi" ve Tablo 2'nin *"Local response-schema validation yapmaz"* satırının "doğrulanamadı" tonu | **Açıkça yazılıdır.** `guides/structured-output.md` ilk paragraf: *"It does not turn a model response into a trusted .NET object, and it does not add server-side schema validation after the response arrives"* — ve dört mod için de "Local response validation: None" sütunlu tablo. Sonucunuz doğru, etiketi yanlış |
| §17.3 · "Provider stream'in kesin token noktasında hard stop davranışı" | **Spesifiye edilmiştir.** `AgentRunBudget.cs:25-38`: token/cost boyutu tool döngüsünün içindeki bir dekoratörle uygulanır, ağacın **bir sonraki** model çağrısını reddeder ve *"The cutoff always lands between two model turns, never inside one"*. Tek uzun stream belirli bir token'da **kesilmez** — bu bir belirsizlik değil, yazılı bir tasarım |
| §17.6 · "Provider retry'nin evrensel davranışı" | Kısmen. Evrensel bir otomatik retry politikası gerçekten **yoktur** (doğru); ama karar yüzeyi vardır ve public'tir: `IProviderRetryClassifier` (`TryAddSingleton`, tüketicinin kaydı kazanır) her başarısız denemede yedeklemeden **önce** danışılır. Tek tek sağlayıcı SDK'ları kendi `MaxRetries`'ini taşır (örn. `AnthropicProviderOptions.MaxRetries`). Ölçülecek yeni bir şey yok; kararlaştırılacak bir politika var |

Prototip listenizde **haklı olarak** duranlar: kalıcı payload sürüm yükseltme
(§17.1), tool argümanı için tam yerel şema doğrulaması (§17.2), ham sağlayıcı
istek/yanıt kaydı (§17.7).

---

## 5. Bizim tarafımızda bulunan ve düzeltilen kusur

Doğrulama sırasında bir doküman–kod çelişkisi çıktı. Kuralımıza göre doküman ile
kod çelişirse doküman yanlıştır — ama düzeltmenin hangi tarafa uygulanacağı
vakaya göre değişir. **Sınıf taraması iki vaka buldu; ikisi de kapatıldı.**

### Vaka 1 — `ModelFallbackUsed` sebebi vaat ediyordu, taşımıyordu

`RunEventType.cs:159` payload'ın *"the reason the primary was skipped"*
taşıdığını yazıyordu; `ModelFallbackUsedEventPayload` dört alan taşıyordu ve
sebep yoktu.

Sizi doğrudan etkiliyordu: §11'de *"Provider call attempt ayrıntısı"* gap'ini
yazarken sebebin kayıtlı olduğunu varsaymışsınız. Değildi.

**Düzeltme yönü: kod.** Sebep zaten çağrı yerinde vardı, yalnız yazılmıyordu.
Payload artık `reason` alanını taşıyor ve değeri **kapalı bir kümedir**:
`provider_unavailable` · `rate_limited` · `http_error` · `transport_error` ·
`classifier`. Sağlayıcının kendi hata **metni değildir** — bir sağlayıcı mesajı
isteği veya yanıt gövdesini alıntılayabilir ve bir run olayı kalıcıdır.

Karar verme ifadesi tek yerde tutuldu: `FallbackRetryClassifier.Classify`
sebebi üretir, `IsRetryable` ondan türer. "Yedeğe düşülecek mi" ve "neden
düşüldü" iki ayrı ifadeye yazılsaydı, birine eklenen bir kural diğerinden
sessizce düşerdi.

Sizin için pratik sonuç: **T-6 diye ayrı bir gap yazmanıza gerek kalmadı** —
sebep artık olay hattında. Deneme başına gecikme ve sağlayıcı istek kimliği
hâlâ yok; o kısım açık kalıyor.

### Vaka 2 — `ContentMasked` hiç var olmamış bir alanı vaat ediyordu

Doküman *"`Payload` carries the match count"* diyordu. `ContentGuardResult`'ta
eşleşme sayısı diye bir alan **hiç olmadı** — kayıt `Action`, `MaskedText`,
`RuleName`, `Reason` taşır. Yazılan payload ise
`{"guard":…,"rule":…,"direction":…,"action":…}`.

**Düzeltme yönü: doküman.** Payload'ın gerçekte taşıdığı dört olgu yazıldı; aynı
cümle `ContentBlocked`'a da eklendi (aynı şekli yazıyor, dokümanı bunu hiç
söylemiyordu).

### Neden ikisi de görünmezdi — ve kapı

Ölçüm sebebi verdi: payload hakkında iddia taşıyan **14** `RunEventType`
üyesinin **4'ünün** payload'ını hiçbir test okumuyordu. `ModelFallbackUsed` o
dördün içindeydi. Prose ile yazan kod arasında hiçbir şey yoktu.

Kapı olarak `RunEventPayloadContractTests` eklendi: payload hakkında iddia
taşıyan her üye, o payload'ı **okuyan** bir testle eşleşmek zorunda; eşleşmeyen
üyeler `uncovered` olarak taban çizgisinde duruyor ve o liste **yalnız
küçülebilir**. Yeni bir payload cümlesi, eşleştirilmeden derlemeden geçmiyor.

Kapının sınırını da yazdık: **prose'u anahtarla karşılaştıramaz.**
`ContentMasked` gerçek bir testle kapsanmıştı ve yine de kaydı. Kapının yaptığı
tek şey, iddianın önüne bir insan koymaktır.

## 6. Doğru çıkan iddialar

Bunlar ölçümde ayakta kaldı ve bizim tarafımızda kapalıdır. Faz adayına
dönüşenler kardeş dokümandadır.

| Rapordaki kalem | Ölçüm |
|---|---|
| Yerel JSON Schema tool argümanı doğrulaması yok | Doğru. Üretilen sarmalayıcı **tip binding + required** yapar (`AgentPrismGeneratedToolArguments.cs`); `enum` dışında hiçbir schema keyword'ü yerel doğrulanmaz. **→ Planlandı:** değiştirilebilir bir `IToolArgumentsValidator` halkası, varsayılanı no-op; MCP tool'larını da kapsar |
| Yanıtın yerel şema doğrulaması yok | Doğru ve yazılı (§4) |
| Kalıcı session/checkpoint payload'ının sürümler arası uyum sözü yok | Doğru. `reference/versioning.md` yükseltme adımı olarak *"read the source diff"* der; `WorkflowCheckpointState` ve `SessionRecord` payload'ında sürüm damgası **yoktur**. **→ Planlandı:** yazılı uyumluluk politikası + kardeş sütunda şema damgası + yükseltme fixture kapısı |
| Tarihsel/effective-dated fiyat yok | Doğru. `IRunPricingResolver.Resolve(provider, model, usage)` zamandan bağımsız saf bir fonksiyondur; `PricingSource` yalnız `Catalog`/`Configuration`/`Unknown` ayrımı yapar. `EffectiveFrom/To`, para birimi sürümü ve uygulanmış fiyat anlık görüntüsü yok |
| Bütün run ağacı için süre bütçesi (`MaxDuration`) yok | Doğru. `AgentRunBudget` token/cost/run sayısı/derinlik taşır; **süre taşımaz**. **→ Planlandı:** `MaxDuration` + türetilen son tarih; kesme mevcut desene uyar — iki model turu arasında |
| Varsayılan tool yetkilendirmesi allow-all | Doğru ve bilinçli (`AllowAllToolAuthorizationHandler`, `TryAdd` — "no-surprises" kuralı). Sizin tarafınızda startup'ta fail-fast koymanız doğru bir karardır. **→ Doküman işi:** kurulum teşhisindeki görünürlüğü ölçülecek |
| Sağlayıcı deneme ayrıntısı (gecikme, retry indeksi, sağlayıcı istek kimliği) kayıtlı değil | **Yarısı doğruydu, yarısı düzeltildi.** Yedeklemenin **sebebi** artık payload'da (§5). Kalan yarısı **reddedildi**: sağlayıcı istek kimliği bir exception'ın içinde jenerik olarak yoktur; onu çıkarmak beş sağlayıcı paketinde ayrı SDK'ya özel kod ister. Somut bir olay analizi gösterirseniz gecikme ve retry indeksi o kimlik olmadan planlanır |
| MCP stdio yok | Doğru ve **bilinçli**: *"Local process (stdio) transport is deliberately not supported."* (`McpConnection.cs:184`). Bir güvenlik sınırıdır; talep bu eksende yargılanır |
| AgentPrism domain state, ABP UoW, Hangfire orchestration, SignalR kontratı ve billing domain'i devralmaz | Doğru. Bu bizim de yazılı duruşumuzdur |
| `QAGenerationJob`'ın ilk dikey dilim olması | Katılıyoruz. Chat döngüsünü ilk faz yapmama gerekçeniz (R-1 ve R-2 ışığında) daha da güçlü |

---

## 7. Ne yapmanızı öneriyoruz

Sırayla, ADR yazmadan önce:

1. **Yayımlanmış `1.0.0-preview.N` ailesine geçin** (§1). Yerel derleme numarası
   üzerinde uyumluluk kararı yazılamaz.
2. **Gap listenizden iki kalemi düşürün:** run olayı imleci (Y-1) ve
   queue-nötr dış job API'si (Y-2). İkisi de bugün mevcuttur.
3. **Gap listenize bir kalem ekleyin:** üretilen tool şemasında parametre
   açıklaması yok (Y-4). Faz 3'ünüzün kapsamını bu değiştirir. Bizde de
   öncelikli bir kalem oldu ve planlandı (§8).
4. **Risk tablonuza bir satır ekleyin, ama dar yazın:** *akışsız* yedeklemenin
   tool turunu baştan çalıştırması (R-1). Akışlı yollarınız bu riski
   taşımıyor; azaltmayı yalnız akışsız çağrılara uygulayın. R-2 (oturumun
   ikinci kaydında son-yazan-kazanır) **düzeltildi**; onun yerine Faz 4
   tasarımınızda çakışma yanıtını (409) ele almanız gerekiyor.
5. **Prototip listenizden üç kalemi düşürün** (§4) ve kalan üçüne yoğunlaşın.
   §5'teki düzeltmeden sonra "sağlayıcı denemesi ayrıntısı" gap'iniz de
   yarıya indi — sebep artık olay hattında.
6. **Faz 6'nızın kabul kriterine** sink'in tamponlanması şartını koyun (§3, R-3).
7. **Faz 1'inizi (`QAGenerationJob`) hâlâ ilk dilim tutun** — ama o dilim
   akışsızdır, yani R-1'in tam sınıfındadır. Yan etkili tool taşıyorsa o
   tanımda `Fallbacks`'ı boş bırakın; taşımıyorsa yedeklemeyi açık
   bırakabilirsiniz.

### Bu raporun sonucunda değişen AgentPrism davranışı

| Değişiklik | Sizi nerede etkiler |
|---|---|
| `ModelFallbackUsed` payload'ı artık `reason` taşıyor (kapalı küme) | §11 "Provider call attempt ayrıntısı" gap'i yarıya indi |
| `ContentMasked`/`ContentBlocked` payload dokümanı gerçeğe hizalandı | Guard olaylarını ayrıştıran her kod |
| `RunEventPayloadContractTests` kapısı eklendi | Bundan sonraki payload sözleşmelerinin doğruluğu |
| Oturumun **her** kaydı eşzamanlılık denetiminden geçiyor (`TryUpdateAsync` + `sessions.version`, üç SQL sağlayıcıda migration) | §15.2 prototip listenizden "session concurrent turn conflict" düşüyor; Faz 4 çakışma yanıtını ele almalı |
| Oturum çakışması OpenAI-uyumlu akışsız uçta 502 yerine 409 | Bu ucu kullanıyorsanız hata sınıflandırmanız |

---

## 8. Raporunuz sonucunda planlanan AgentPrism işi

Bunlar **henüz sevk edilmedi.** Planlandılar; her biri kendi dokümanına,
kabul ölçütlerine ve hata modu tablosuna sahip. ADR'nizde "yok" yerine
"planlandı" diye okuyun — ikisi farklı bir geçiş planı üretir.

| Ne | Sizin raporunuzdaki karşılığı | Sizi nerede etkiler |
|---|---|---|
| Yedeklemede tur-içi tool defteri | §16 risk tablosu; §11 timeout/idempotency | Akışsız yollarınızda yan etkili tool + `Fallbacks` birlikte kullanılabilir hâle gelir. Bugünkü azaltma o zaman kalkar |
| Üretilen tool şemasında parametre açıklaması | Y-4 · B10 | Faz 3'ünüzde 23 handler'ın şema paritesi. Bugün `BackendToolRegistry`'niz önde; bu kapandığında eşitlenir |
| Üreteç şemasının ifade sınırının ilanı | Faz 3 test planınızdaki *"nested schema"* case'i | O case'i **planlamayın**: üreteç iç içe nesneyi ifade edemez ve bu artık rehberde yazılı olacak. Nesne parametresi için `AIFunctionFactory.Create` yolu |
| Kalıcı payload sürüm sözleşmesi (söz + damga + prova) | §17.1 "kritik doğrulanamayan" | Prototip listenizde **haklı olarak** duruyordu. Sevk edildiğinde o prototipi koşmanız gerekmez; politikayı okursunuz |
| `IToolArgumentsValidator` halkası | §11 "Kritik" gap | Kendi DataAnnotations doğrulayıcınızı **bir kez** bağlarsınız. 🚨 Ölçüm bir şeyi netleştirdi: bu halka MCP tool'larını da kapsayacak — bugün onları sarmalayabileceğiniz bir yer yok |
| `AddScopedTool` | §8.1 · §11 "Yüksek" gap | 23 handler'ın her biri için sekiz satırlık `IServiceScopeFactory` kalıbı yerine tek satır. **Not:** talebinizdeki `AddScopedTool<THandler>()` imzası reddedildi (yansıma/AOT); şekli `AddTool` ile aynı olacak |
| `AgentRunBudget.MaxDuration` | §11 "Yüksek" gap | Toplam süre bütçesi. Kesme **iki model turu arasındadır** — sert bir zaman aşımı değildir; uzun bir tool o tur içinde kesilmez |

**Planlanmayan ve neden:** sağlayıcı denemesi ayrıntısının kalan yarısı
(gecikme, retry indeksi, sağlayıcı istek kimliği) reddedildi — gerekçe §6
tablosunda. ABP entegrasyon paketi, `RunStructuredAsync<T>`, effective-dated
fiyat, outbox kancası, SQL tabanlı dağıtık iptal ve `UseMcpStdio` için
duruşumuz değişmedi; gerekçeleri §2 ve §4'tedir.

Sürüm numaralarını AgentPrism tarafı ayrıca bildirecek.

Sorularınız için: bu dokümandaki her `dosya:satır` referansı `8105c00`
üzerinde doğrulanabilir.
