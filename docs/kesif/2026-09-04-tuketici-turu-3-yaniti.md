# Tracon → ProdigyEnabler · Tüketici Turu 3 Yanıtı

> **Kimden:** Tracon geliştirme tarafı · **Tarih:** 2026-09-04
> **Neye yanıt:** "Tracon — Feature Talepleri ve Fikirler" (ProdigyEnabler
> backend ekibi, 2026-09-03) · Bölüm A (A1–A9) ve Bölüm B (B1–B10)
> **Ölçüm kaydı:** [`2026-09-03-tuketici-turu-3-olcumu.md`](2026-09-03-tuketici-turu-3-olcumu.md)

---

## Özet

Raporunuzu koda karşı ölçtük. **On iki iddianın onu doğru çıktı.** İki yanlış
iddianın ikisi de sizin okuma hatanız değil, **bizim dokümanımızın** ürettiği
yanlış anlamaydı; ikisini de düzelttik ve birine kalıcı bir kapı koyduk.

Beş kalem faza dönüştü ve **beşi de tamamlandı**:

| Talep | Karar | Faz | Kırıcı |
|---|---|---|---|
| A1 + A8 · Kullanıcı düzeyinde run/session sahipliği | Kabul edildi, **kapsamı genişletilerek** | 139 | Hayır |
| A4 · İçerik guard'ında kaynak ve tool adı | Kabul edildi | 140 | Hayır |
| A5 · Genişletilebilir run olayı türü | Kabul edildi | 141 | Hayır |
| A3 · Onay isteğine görüntülenebilir varlık kimliği | Kabul edildi | 142 | Hayır |
| B7 · Tool şemasından üretilen sözleşme testleri | Kabul edildi | 143 | Hayır |
| A2 · Kota eşiği bildirimi | **Reddedildi — özellik zaten vardı** | — | — |
| A9 · `IRunEventSink` düşürme görünürlüğü | **Reddedildi — dayandığı olgu yanlıştı** | — | — |
| A6 · Akış delta birleştirme | Bu turda alınmadı | — | — |
| A7 · Bağlama başına endpoint | Bu turda alınmadı | — | — |
| B1–B6, B8–B10 | Bu turda alınmadı | — | — |

**Beşinin de public API'si additive'dir.** Kaydetmediğiniz hiçbir genişleme
noktası davranışınızı değiştirmez.

### 🚨 Henüz yayınlanmadı

Beş faz `main`'de ve kapanış kapılarından geçti, ama **bir sürüm olarak
yayınlanmadı.** Bugünkü geliştirme hattı `0.0.0-preview.0.572`; sizin
incelediğiniz `1.0.0-preview.1` bunları **içermez**. Bir sonraki preview'a
girecekler. Entegrasyon planınızı buna göre sıralayın: kod hazır, paket değil.

### Doğrulama — bu yanıt yazılmadan önce koşuldu

Aşağıdakiler faz dokümanlarının iddiası değil, bu yanıtı hazırlarken
**yeniden ölçülen** sonuçlardır (commit `780e45ca`, macOS/arm64, 2026-09-04):

| Ölçüm | Sonuç |
|---|---|
| `dotnet build Tracon.slnx -c Release` | ✅ **0 Warning · 0 Error** |
| `dotnet format --verify-no-changes` | ✅ temiz |
| `dotnet pack -c Release` | ✅ uyarısız |
| `dotnet test Tracon.slnx` (tüm çözüm) | ⚠️ **3 test düştü** — üçü de MCP Tasks, aşağıda |
| `Tracon.AspNetCore.FunctionalTests` tek başına | ✅ **766/766** |
| Genişleme noktası sayısı | 5 → **7** |
| OpenAPI yüzeyi | **163 operasyon / 126 path — değişmedi** (yeni uç yok) |
| Yeni migration | **6** (üç SQL sağlayıcı × 2) |

---

## A1 + A8 — Çalıştırma ve oturum yetkilendirmesi (Faz 139)

**Faz:** [139 — Çalıştırma ve Oturum Yetkilendirmesi](../arsiv/fazlar/139-CALISTIRMA-VE-OTURUM-YETKILENDIRMESI.md)

### Karar

**Kabul edildi ve kapsamı istediğinizden geniş tutuldu.**

İddianız doğruydu. Ölçüm üç olgunuzu da doğruladı: `sessionId` gövdeden geliyor
(`AgentEndpoints.cs:825`), sahiplik yalnız kiracı düzeyinde doğrulanıyor
(`AgentSessionManager.cs:365`), ve run yolundaki dört kapının hiçbiri çağıranı
session sahipliğiyle karşılaştırmıyordu.

"Mevcut yeteneklerle neden çözemiyoruz" tablonuzun **altı satırı da** doğru
çıktı. Sevk edilen genişleme noktalarını saydık: beş taneydi ve hiçbiri run
başlatmayı veya session okumayı yetkilendirmiyordu.

En ikna edici bulduğunuz argüman — bizim `userId` gerekçemizin `sessionId` için
de geçerli olması — kabul edildi. İkisi arasında güvenlik açısından bir fark
yok, ve sizin dediğiniz gibi ikincisi daha ağır: yanlış atıf bir **kayıt**
hatası, yanlış session bir **sızıntıdır**.

### 🚨 Kapsamı neden genişlettik

Planlama sırasında bir ölçüm yaptık ve sonucu raporunuzda yoktu. Kendi
kodumuzun yorumu şunu yazıyor (`RunAttributionGate.cs:19`):

> *"the endpoints that start runs do not share one"*

Yani run başlatan uçlar **ortak bir filtre paylaşmıyor**; her biri kapılarını
kendi gövdesinde açıkça çağırıyor. Ve run başlatan **dört** yüzey var:

1. `POST /api/agents/{name}/run`
2. `POST /api/workflows/{name}/run`
3. Inbound trigger yolu
4. OpenAI uyumlu yüzey (`/v1/responses`)

Yalnız birincisini kapsasaydık kapı bir **bypass**'a dönerdi ve yanlış bir
güvenlik hissi üretirdi — bu, kapının hiç olmamasından kötüdür. Dördü de
kapsandı; ayrıca dört session ucu. Toplam sekiz çağrı yeri, hepsi ayrı ayrı
fonksiyonel testle kilitli.

### Sevk edilen API

```csharp
public interface IRunAuthorizationHandler
{
    ValueTask<RunAuthorizationResult> AuthorizeRunAsync(
        RunAuthorizationRequest request, CancellationToken cancellationToken = default);

    ValueTask<RunAuthorizationResult> AuthorizeSessionAsync(
        SessionAuthorizationRequest request, CancellationToken cancellationToken = default);
}

public sealed record RunAuthorizationRequest
{
    public required string TenantId { get; init; }
    public required string AgentName { get; init; }
    public string? SessionId { get; init; }
    public string? UserId { get; init; }
    public required RunAccess Access { get; init; }
}

public sealed record SessionAuthorizationRequest
{
    public required string TenantId { get; init; }
    public string? SessionId { get; init; }   // ← List için null
    public string? UserId { get; init; }
    public required SessionAccess Access { get; init; }
}

public enum RunAccess { Start = 0 }
public enum SessionAccess { Read = 0, List = 1, Delete = 2, Branch = 3 }

public sealed record RunAuthorizationResult
{
    public required bool IsAllowed { get; init; }
    public string? Reason { get; init; }
    public static RunAuthorizationResult Allow();
    public static RunAuthorizationResult Deny(string reason);
}
```

**Önerinizden tek sapma:** `SessionAuthorizationRequest.SessionId` `required`
değil, **nullable**. Sebep sizin de kabul ettiğiniz `List` erişimidir — bir
listenin tekil bir session kimliği yoktur. Önerinizde `required string`
yazıyordu; o hâliyle `List` çağrısı anlamsız bir değer taşımak zorunda kalırdı.

### Davranış — istediğiniz gibi

| Söz | Durum |
|---|---|
| `TryAddSingleton` ile kayıt; kaydedilmezse hiçbir şey değişmez | ✅ `AllowAllRunAuthorizationHandler` |
| Handler `throw` ederse çağrı **reddedilir** (fail-closed) | ✅ |
| Reddedilen run `403` | ✅ |
| Reddedilen session okuması `404` | ✅ |
| `List` için filtre yok, red yeterli | ✅ `403` |
| `Access` enum'ları kapalı | ✅ |

### A8 — scope'ta çağıran kimliği

`AgentRunScope` artık `UserId` **ve** `Labels` taşıyor
(`TraconRunContext.cs:93,102`). Duruşumuz aynen korundu: opak string,
çözülmez, doğrulanmaz, yorumlanmaz. Tool gövdeleriniz `SessionId` üzerinden
ikinci bir okuma yapmak zorunda değil.

### Entegrasyonunuz için

Chat fazınızı bekleten kalem buydu. **SSE proxy'si yazmanız artık gerekmiyor.**
`IRunAuthorizationHandler`'ı ABP izin hattınıza bağlayın; Tracon'in HTTP
API'sini front-end'e doğrudan açabilirsiniz. 163 operasyonun tamamı kapalı
kalmaz, ve yeni bir Tracon yeteneği için proxy'ye geçit yazmanız gerekmez —
kaçınmak istediğiniz maliyet buydu.

---

## A4 — İçerik guard'ında kaynak ve tool adı (Faz 140)

**Faz:** [140 — İçerik Guard'ının Kaynağı](../arsiv/fazlar/140-ICERIK-GUARDININ-KAYNAGI.md)

### Karar

**Kabul edildi.** Gerekçeniz doğruydu ve ölçüm onu güçlendirdi: ayrım çağrı
yerinde **zaten mevcuttu** ve `ContentGuardContext` kurulmadan hemen önce
atılıyordu. `ContentGuardMessageMasker` `message.Role`'ü zaten okuyordu (`:74`)
ve `FunctionResultContent`'i `TextContent`'ten zaten ayırıyordu (`:167`).

### Sevk edilen API

```csharp
public sealed record ContentGuardContext
{
    // mevcut: Direction, Text, RunId, TenantId, AgentName, ModelId
    public ContentGuardSource Source { get; init; }
    public string? ToolName { get; init; }
}

public enum ContentGuardSource
{
    Unknown = 0, UserMessage = 1, ToolResult = 2,
    Document = 3, ModelOutput = 4, SkillResource = 5
}
```

`Unknown = 0` tercihiniz aynen korundu ve bir kural eklendi: **`Unknown` en sıkı
kuralı alır.** Sevk ettiğimiz `PatternContentGuard` bunu uyguluyor
(`PatternContentGuard.cs:42`) — bilinmeyen kaynağı "izin ver"e çevirmek, kaynak
ayrımının amacını tersine çevirirdi.

### 🚨 Raporunuzda olmayan bir ayrıntı — `ToolName` her zaman dolmaz

`FunctionResultContent` tool **adını taşımaz**, yalnız `CallId` taşır. Adı
çözmek için mesaj listesinde `CallId → FunctionCallContent.Name` eşlemesi kuran
ikinci bir geçiş gerekiyor. Çok turlu bir konuşmada eski turların çağrısı
bağlamdan düşmüşse **ad çözülemez**.

O durumda `ToolName` `null` kalır ama **`Source` yine `ToolResult` olur.**

**Güvenlik kararınızı `Source`'a dayandırın, `ToolName`'e değil.** İkincisi bir
kolaylıktır; birincisi bir garantidir.

Bu turda üç kaynak fiilen dolduruluyor: `UserMessage`, `ToolResult`,
`ModelOutput`. `Document` ve `SkillResource` enum'da duruyor ama henüz
doldurulmuyor — o metinler guard'a farklı bir yoldan giriyor ve ölçmeden
doldurmak yanlış sınıflandırma üretirdi. Bugün onlar `Unknown` gelir, yani en
sıkı kuralı alır.

### Entegrasyonunuz için

İki desen setinizi koruyabilirsiniz. Yanlış pozitif ile güvenlik kaybı arasında
seçim yapmak zorunda değilsiniz.

---

## A5 — Genişletilebilir run olayı türü (Faz 141)

**Faz:** [141 — Genişletilebilir Çalıştırma Olayı](../arsiv/fazlar/141-GENISLETILEBILIR-CALISTIRMA-OLAYI.md)

### Karar

**Kabul edildi — önerdiğiniz tasarımla.** Kapalı enum tercihimizin gerekçesini
anlamanız ve `Custom` ara yolunu önermeniz bu talebin kabul edilme sebebidir.
Konsol beklentinizin mütevazı olması ("jenerik bir kart yeter") kapsamı
tartışmasız kıldı.

### Sevk edilen API

```csharp
public enum RunEventType { /* … 29 değer … */ Custom = 29 }

public readonly record struct RunEventDraft(RunEventType Type)
{
    // mevcut: Text, ToolName, ToolCallId, Payload
    public string? CustomType { get; init; }
}

public sealed record RunEvent { public string? CustomType { get; init; } }
```

`JobHandlerKeys` doğrulama kuralı birebir tekrarlandı: 1–128 karakter, küçük
harf ASCII + rakam + `.` `_` `-`, `tracon.` öneki **rezerve ve reddedilir**.

**Doğrulama iki yönlüdür** ve bu sizin önerinize eklediğimiz tek şey:
`Custom` iken `CustomType` boşsa **ve** `Custom` değilken `CustomType` doluysa
çağrı reddedilir. Tek yönlü kontrol sessiz bir kusur sınıfı üretirdi — kodun
kendi yorumu bunu yazıyor (`RunEventWriter.cs:349`): *"A one-way check would let
a caller write `CustomType` on a…"*

`runs_v1` görünümü etkilenmedi — tespitiniz doğruydu.

### Entegrasyonunuz için

Dört domain olayınızı (`preview-ready`, `content-saved`, `audio-generating`,
`audio-ready`) ve ontoloji ağacı olayınızı doğrudan SSE akışına yazabilirsiniz.
Mutasyon tool listesini front-end'de tutmanız gerekmiyor; o bilgi backend'de tek
bir yerde kalır.

**Migration gerekiyor:** `run_events` tablosuna `custom_type` sütunu eklendi
(üç SQL sağlayıcı için ayrı ayrı). Sütun nullable'dır; Tracon'in kendi
yazdığı her olayda `NULL` kalır.

---

## A3 — Onay isteğine görüntülenebilir varlık kimliği (Faz 142)

**Faz:** [142 — Onay İsteğinin Sunumu](../arsiv/fazlar/142-ONAY-ISTEGININ-SUNUMU.md)

### Karar

**Kabul edildi.** Argümanınızın kabul edilmesini sağlayan kısım şuydu: aynı
ekran **bizim konsolumuzda da** var ve o da ham argüman gösteriyordu. Kalem
yalnız size değil ürüne de yarıyor.

### Sevk edilen API

```csharp
public interface IToolApprovalPresenter
{
    ValueTask<ToolApprovalPresentation?> PresentAsync(
        ToolApprovalContext context, CancellationToken cancellationToken = default);
}

public sealed record ToolApprovalPresentation
{
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public string? EntityName { get; init; }
    public string? Message { get; init; }
}

public sealed record PendingApproval { public ToolApprovalPresentation? Presentation { get; init; } }
```

### Davranış — fail-open, bilerek

Sizin analiziniz doğruydu ve aynen uygulandı: burada fail-closed **yanlış**
olurdu. Bir sunum hatası, güvenlik gerektiren bir onayı engellememelidir.

| Durum | Davranış |
|---|---|
| Kayıtlı değil | Bugünkü çıktı aynen |
| `null` döner | Bugünkü çıktı aynen |
| `throw` eder | **Onay isteği yine yayımlanır**, sunum boş |
| Zaman aşımı | Aynı |

Zaman aşımı eklendi ve ayarlanabilir:
`TraconToolOptions.ApprovalPresentationTimeout`, **varsayılan 2 saniye**.
Sınırsız bekleme onay isteğini asardı.

### 🚨 Kayıt biçimi — dikkat edin

Çözümleyiciniz veritabanına gidecek. **`TryAddSingleton` ile kaydedilir ve
scope'u kendisi açar:**

```csharp
public sealed class SkillApprovalPresenter(IServiceScopeFactory scopes) : IToolApprovalPresenter
{
    public async ValueTask<ToolApprovalPresentation?> PresentAsync(
        ToolApprovalContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IYourDbContext>();
        // …
    }
}
```

`AddScopedTool` benzeri bir kayıt biçimi **eklemedik**. Sebep bizim kendi
tuzağımız: MAF, tool'lara `AIFunctionArguments.Services` olarak boş bir servis
sağlayıcı geçirir; çalışma anında servis çözen bir tasarım sessizce `null` alır.
Singleton + `IServiceScopeFactory` bu sınıfın tamamından kaçınır, ve
`ITenantContext` ile `IRunAttributionContext`'in zaten uyduğu kuraldır.

**Ham argümanlar kaybolmadı.** Sunum onların üstüne gelir; bir operatör neyin
onaylandığını her zaman tam görebilir.

Alanlar hem `GET /api/approvals/pending` yanıtında hem `RunAwaitingInput`
olayının payload'ında görünür — ikisini de istemiştiniz.

**Migration gerekiyor:** `pending_approvals` tablosuna sunum sütunları eklendi.

### Entegrasyonunuz için

Front-end'de tool adı → varlık türü eşleme tablosu tutmanız ve ek gidiş-dönüş
yapmanız gerekmiyor. Yedi yıkıcı tool'unuzun onay diyaloğu backend'den gelir.

---

## B7 — Tool argümanının sözleşme testleri (Faz 143)

**Faz:** [143 — Tool Argümanının Sözleşme Testleri](../arsiv/fazlar/143-TOOL-ARGUMANININ-SOZLESME-TESTLERI.md)

### Karar

**Kabul edildi.** Bölüm B'den alınan tek kalem bu oldu. Gerekçeniz belirleyiciydi:
iki yerde fail-closed davranış **vaat ediyoruz**, ama o vaatler yalnız bizim
kodumuzda test ediliyordu — tüketicinin implementasyonunda değil. Ve
"AI'ın halüsinasyon ürettiği argüman, kod hatasından daha sık" gözleminiz
yirmi iki tool'un yedisi yıkıcı olan bir kurulumda ağır basıyor.

### Sevk edilen

`Tracon.Testing.Contracts.Xunit` içinde iki yeni `abstract` suite:

```csharp
public abstract class ToolArgumentValidationContract : IAsyncLifetime
{
    // Missing_required_argument_is_rejected
    // Type_mismatch_is_rejected
    // Out_of_range_number_is_rejected
    // Pattern_violation_is_rejected
    // Unknown_extra_property_is_handled_deliberately
    // Throwing_validator_rejects_the_call
    // A_pre_cancelled_token_is_honored
}

public abstract class ToolAuthorizationContract : IAsyncLifetime { /* … */ }
```

**Bir doğrulayıcı sevk etmiyoruz.** Duruşumuz korundu: doğrulama tüketicinin
kendi güven sınırının içinde kalır. Suite sizin doğrulayıcınızı **sınar**.

Üreteç **tohumludur**, rastgele değil — düşen bir vaka birebir tekrar
üretilebilir. Rastgele bir suite kırılgan bir kapı olurdu.

"Fazla alan" konusunda karar dayatmıyoruz: `additionalProperties` bir tercihtir
ve suite onu beyan ettiriyor, zorlamıyor.

---

## Reddedilen iki talep — ve neden sizin hatanız değil

Bu iki kalemi reddediyoruz, ama ikisinde de **hata bizde.** İkisi de
dokümanımızın ürettiği yanlış anlamaydı ve ikisi de düzeltildi.

### A2 — Kota eşiği bildirimi: özellik zaten vardı

Talebiniz *"eşiğe yaklaşma bildirimi üretmiyor"* diyordu. **Üretiyor.**

| Ne | Nerede |
|---|---|
| Eşik yüzdeleri | `TraconQuotaOptions.ThresholdPercents`, **varsayılan `[80, 100]`** |
| Periyot başına bir kez | `QuotaEnforcer.cs:296` — tam olarak sizin önerdiğiniz semantik |
| Yayım | `quota.threshold` webhook'u, `WebhookQuotaSummary` ile |
| Payload | `Metric`, `AgentName`, `Period`, `ThresholdPercent`, `Limit`, `Used`, `ResetsAt` |

Yani `QuotaDefinition.WarnAtPercent` önerinizin istediği her alan zaten
taşınıyor. Tek fark: eşik **kiracı başına değil, global bir option**.

**Neden bulamadınız — bizim kusurumuz.** Özellik yalnız üretilen API
referansında görünüyordu. `llms-full.txt`'te `ThresholdPercents` **bir kez**
geçiyordu; anlatı sayfamız § "Quotas and rate limits" yalnız `429`'u
anlatıyordu; § "Webhooks" mekanizmayı anlatıp **hangi olayların var olduğunu
hiç listelemiyordu**. Belgeyi satır satır okudunuz ve özelliği bulamadınız —
bu, dokümanın hatasıdır.

**Düzeltildi:** § Quotas eşik bildirimini anlatıyor, § Webhooks **on olayın
tamamını** listeliyor. Ve kalıcı bir kapı eklendi: her `WebhookEvents` sabiti en
az bir anlatı sayfasında geçmek zorunda; üretilen referans sayılmıyor. Kapının
kırmızı olduğu, düzeltme geri alınarak kanıtlandı.

**Entegrasyonunuz için:** kendi sayacınızı kurmayın. `quota.threshold`
webhook'una abone olun ve bildirim hub'ınıza bağlayın. Sizin de öngördüğünüz
"iki sayaç er ya da geç ayrışır" sorunundan kaçınırsınız.

Kalan gerçek boşluk yalnız **kanaldır**: sinyal webhook'a gidiyor (operatör
tarafı), SSE akışına değil (son kullanıcı tarafı). Bunu şimdi kapatmadık —
Faz 141'in `Custom` olayı üstüne oturması gerekir. Kanalı gerçekten istiyorsanız
ayrı bir talep olarak açın; artık `Custom` ile kendiniz de köprüleyebilirsiniz.

### A9 — `IRunEventSink` düşürme görünürlüğü: dayandığı olgu yanlıştı

Alıntıladığınız cümle bizimdi: *"A channel that reaches capacity **drops** the
event and logs it; it does not block."*

O cümle **sizin kendi kanalınızı** tarif ediyordu — sink'inizin içinde kurmanız
gereken kuyruğu. Tracon'in **kanalı yoktur**: `RunEventWriter.cs:203`
sink'i doğrudan `await` eder. Dolayısıyla `TraconRunEventSinkOptions`
önerisi bir metrik eklemek değil, **var olmayan bir kanalı inşa etmek**
olurdu — istediğinizden çok daha büyük bir değişiklik.

Cümlenin **öznesi yoktu** ve bu yüzden bizim davranışımız gibi okundu. Sizin
okuma hatanız değil.

**Düzeltildi.** Metin artık açıkça yazıyor: *"Tracon holds no queue of its
own in front of your sink, so the buffer is yours to own."* Ayrıca sınıf
taraması **ikinci bir vaka** buldu: `concepts/runs.md`'deki sink örneği kendi
kuralını çiğniyordu — `await queue.PublishAsync(...)`, "do not block on further
I/O" cümlesinin iki satır üstünde. Örneği kopyalayan bir okuyucu, dokümanın
uyardığı yavaş sink'i kurardı. Örnek artık sınırlı bir `Channel`'a yazıp dönüyor.

**Entegrasyonunuz için:** sink kullanacaksanız kuyruğu **siz** kurun.
`Channel.CreateBounded` + `BoundedChannelFullMode.DropOldest` + `TryWrite`, ve
kendi arka plan okuyucunuz. Düşme sayımı da o kuyruğun sahibi olarak sizde olur.

---

## Alınmayan kalemler ve gerekçeleri

| Kalem | Gerekçe |
|---|---|
| **A6** · Akış delta birleştirme | Kendiniz *"çözebiliyoruz, bu yüzden P1"* dediniz. Ayrıca sunucuda birleştirme `MaxDelay` kadar **gecikme ekler** ve akışın algılanan hızını düşürür; bu ödünü kütüphane sizin adınıza vermemeli. `MaxDelay` uyarınız yerinde — kendi tamponunuzda mutlaka olsun |
| **A7** · Bağlama başına endpoint | Fark ettiğiniz asimetri **gerçek** ve doğruladık. Ama asimetri tek başına bir talep kanıtı değil; kendiniz *"engelleyici değil, tek endpoint kullanıyoruz"* diyorsunuz. Ölçülmüş bir kullanıcı acısı çıkarsa yeniden açarız |
| **B1** · Tool çağrısı sayısı sınırı | Boşluk gerçek ve doğruladık. `AgentGraph.MaxDuration` (Faz 114) ucuz-tool döngüsünü kesebilir, ama 🚨 **varsayılanı yoktur** — diğer üç boyutun aksine sıfır gelir ve siz vermedikçe hiçbir şey yapmaz. Yani "dakikalarca bekletir" senaryonuz bugün gerçekten açık. Kalemi almamamızın sebebi boşluğun yokluğu değil, bu turda A kalemlerinin önceliği; bu arada `MaxDuration`'ı kendiniz verebilirsiniz |
| **B2** · Tool sonucu önbelleği | Fikir sağlam ve `Effect` doğrulaması hazır. Ama çok kiracılı bir önbellek **kalıcı bir güvenlik yüzeyidir**; yanlış anahtar bir kiracıya başkasının sonucunu verir. 1.0 öncesi alınacak en riskli kalem |
| **B3** · Gölge run | Gerçek para harcayan bir mod; bütçe ve kota muhasebesinin ayrı raporlanması gerekiyor. Kapsam göründüğünden büyük |
| **B4** · Git `IAgentSource` | Boşluk gerçek (sevk edilen tek implementasyon `CodeAgentSource`). Ama yeni paket + Git bağımlılığı; ayrı bir tur ister |
| **B5** · Redaksiyon profili | Maliyeti kendiniz yazmıştınız: redakte edilmiş run **replay edilemez**. Bu, Faz 112'nin replay sözleşmesiyle doğrudan çelişiyor |
| **B6** · Transkript dışa aktarımı | Boşluk gerçek ve kalem ucuz — bugün yalnız `/api/data-subjects/{id}/export` var. Sıralanabilir; bu turun A kalemlerinden zayıf olduğu için alınmadı |
| **B8** · `SubjectRef` | `Labels` **zaten** bu işi yapabilir: tek bir anahtarı (`subject`) bölümleme olarak kullanın. Kalem yeni bir yetenek değil, var olanın tiplenmiş hâli; kalıcı şema maliyeti buna değer mi ölçülmedi |
| **B9** · Harcama projeksiyonu | Kota eşiği zaten var (A2). Projeksiyon onun üstüne biner; önce eşiğin kanal sorusu cevaplanmalı |
| **B10** · Prompt bisector | n blok = n koşum. Maliyeti en yüksek, talep kanıtı en zayıf kalem |

---

## 🚨 Bilinen sorun — kapanış kapısı tam çözüm koşumunda kırmızı

Dürüst olalım: `dotnet test Tracon.slnx` (tüm çözüm birlikte) bugün **üç
testte kırmızı**. Üçü de MCP Tasks tarafında:

- `McpTasksEndpointTests.Unknown_task_id_is_reported_as_a_protocol_error_not_a_500`
- `McpTaskCrossInstanceTests.Second_instance_reconstructs_a_completed_task_from_the_shared_database`
- `McpTaskCrossInstanceTests.Second_instance_reconstructs_an_approval_rejection_generically_not_with_todays_exact_wording`

**Bu bir yalıtım sorunudur, bu beş fazın regresyonu değildir.** Ölçüm:

| Koşum | Sonuç |
|---|---|
| Tüm çözüm | ❌ 3 düştü |
| Yalnız `Tracon.AspNetCore.FunctionalTests` | ✅ 766/766 |
| Yalnız `*McpTask*` filtresi (bugün, HEAD) | ✅ 12/12 |
| Yalnız `*McpTask*` filtresi (Faz 139 **öncesi** temel commit) | ✅ 12/12 |

Faz 139–143 hiçbir MCP koduna dokunmadı — `git diff` yalnız bir ekran
görüntüsü veriyor. Düşüşlerden birinin mesajı nedeni işaret ediyor: paralel
koşumda istemci yanlış protokol sürümüyle anlaşıyor (`2025-11-25`, oysa tasks
`2026-07-28` istiyor); ayrı koşumda doğru sürümü alıyor.

Kayıt: **F-190** olarak aday listemize girdi. Sizi bugün etkilemez — MCP Tasks
kullanmıyorsanız hiç etkilemez — ama kırmızı bir kapıyı size bildirmeden
geçmeyiz.

---

## Entegrasyon stratejiniz için özet

**Değişen kararlar:**

1. **SSE proxy'si yazmayın.** A1 geldi. `IRunAuthorizationHandler`'ı ABP izin
   hattınıza bağlayın, front-end'i doğrudan Tracon'e bağlayın. Chat fazınızı
   bekleten tek kalem buydu.
2. **Kota sayacınızı kurmayın.** `quota.threshold` webhook'una abone olun.
   İkinci bir sayaç üretmeyin.
3. **Front-end'de tool→varlık eşleme tablosu tutmayın.** A3 geldi; onay
   diyaloğunuz backend'den beslenir.
4. **Guard'ınızı iki desen setiyle yazın.** A4 geldi; `Source`'a bakın,
   `ToolName`'e güvenmeyin.
5. **Domain olaylarınızı SSE'ye yazın.** A5 geldi; mutasyon listesi backend'de
   tek yerde kalır.
6. **Tool doğrulayıcınızı sözleşme suite'iyle sınayın.** B7 geldi.

**Değişmeyen kararlar:**

7. **Delta tamponunu siz yazın** (A6) — `MaxDelay`'i unutmayın.
8. **Endpoint'i konfigürasyona taşıyın** (A7).
9. **Sink kullanacaksanız kuyruğu siz kurun** (A9) — Tracon'in kuyruğu yok.

**Dağıtım notları:**

- Beş faz **henüz yayınlanmadı**. Bir sonraki preview'a girecekler.
- **6 yeni migration** var (üç SQL sağlayıcı × 2): `run_events.custom_type` ve
  `pending_approvals` sunum sütunları. İkisi de additive ve nullable.
- **Yeni HTTP ucu yok** — 163 operasyon / 126 path değişmedi. Var olan uçların
  davranışı değişti (red yanıtları) ve iki yanıt additive alan taşıyor.
- Genişleme noktası sayısı 5 → 7.

---

Bir yerde yanlış ölçtüysek veya bir davranışı yanlış tarif ettiysek söyleyin;
düzeltiriz. Bu turda iki kez kendi dokümanımızın sizi yanılttığını bulduk —
üçüncüsü de olabilir.
