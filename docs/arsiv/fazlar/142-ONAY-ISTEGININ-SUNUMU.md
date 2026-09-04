# Faz 142 — Onay İsteğinin Sunumu

> **Durum:** ✅ Tamamlandı (2026-09-04)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-188** (tüketici turu 3, A3)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Gerekli olabilir — sunum alanları kalıcı mı, Açık Soru 1. Numara uygulama anında alınır
> **Public API:** Büyüyor — yeni kontrat + `PendingApproval`'a alanlar
> **Tüketici yüzeyi:** `docs-site/`: `concepts/tools.md` (onay bölümü), `concepts/governance.md`, `guides/embedding.md` · sevk edilen: XML `<example>`, konsol onay ekranı + ekran görüntüsü
> **Manuel test alanı:** [`docs/manuel-test/07-HTTP-YONETIM-API.md`](../../manuel-test/07-HTTP-YONETIM-API.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-218\|K-103\|K-584" docs/KARARLAR.md
   ```
   🚨 **K-218** (tool'un gördüğü servis sağlayıcı **BOŞTUR** — bu fazın en
   önemli tuzağı), **K-103** (onay kontrolü run kuyruğa taşınınca handler'dan
   düşer), **K-584** (`Destructive` etkili tool taşıyan koşu varsayılan olarak
   devam etmez)
3. Alan hafızası:
   [`hafiza/tool-onay-ve-yetkilendirme.md`](../../hafiza/tool-onay-ve-yetkilendirme.md)
   (🚨 tool bağımlılığı **kurulum anında** alınır) ·
   [`hafiza/aspnetcore-di.md`](../../hafiza/aspnetcore-di.md) (singleton/scoped sınırı)

---

## Amaç

Bir onay diyaloğu bugün şuna dönüşüyor: *"`delete_skill` tool'unu
`{ "skillId": "8f14e45f-…" }` argümanıyla çalıştırmak istiyor. Onaylıyor
musunuz?"* Bu, kullanıcının bilinçli bir karar vermesini sağlamaz. Aynı
sorun **AgentPrism'in kendi konsolunda da** vardır.

- **F-188** — Onay isteği yayımlanmadan önce argümanlardan görüntülenebilir
  alanlar üreten, salt okuyan bir çözümleyici.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`PendingApproval.cs:19-66`](../../../src/AgentPrism.Abstractions/Approvals/PendingApproval.cs) | Alanlar: `Id`, `TenantId`, `RunId`, `SessionId`, `RequestId`, `ToolName`, `Arguments`, `Status`, `DecidedBy`, `DecidedAt`, `ExpiresAt`, `CreatedAt`. Sunum alanı **yok** |
| [`ToolApprovalContext.cs:14-26`](../../../src/AgentPrism.Abstractions/Approvals/ToolApprovalContext.cs) | `TenantId`, `ToolName`, `Arguments` + `GetNumber`/`GetString` yardımcıları. Metin üretmiyor |
| [`ApprovalEndpoints.cs:35,50,66`](../../../src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs) | Üç uç ham `Arguments` döndürüyor |

> Kanıtlar 2026-09-03 tarihinde doğrulandı.

**`AddToolApprovalPolicy` bu işi yapamaz.** O bir **karar** noktasıdır
(`Required`/`NotRequired`/`Undecided`) ve dönüş tipi metin taşımaz. Metni oraya
sıkıştırmak bir güvenlik sınırını sunum katmanına çevirirdi.

---

## 142.1 — 🚨 Kayıt biçimi ve K-218 tuzağı

Bu fazın en pahalı tuzağı budur. Çözümleyici veritabanına gidecek — Guid'den
skill adını okuyacak. Ama `MEMORY.md`'nin yazdığı gibi: **MAF,
`AIFunctionArguments.Services` olarak `EmptyServiceProvider` geçirir.** Çalışma
anında servis çözen bir tasarım sessizce `null` alır.

Kullanıcı kararı (2026-09-03): **singleton + `IServiceScopeFactory`.**

```csharp
public sealed class SkillApprovalPresenter(IServiceScopeFactory scopes) : IToolApprovalPresenter
{
    public async ValueTask<ToolApprovalPresentation?> PresentAsync(
        ToolApprovalContext context, CancellationToken cancellationToken = default)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMyDbContext>();
        // …
    }
}
```

Gerekçe `ITenantContext` ve `IRunAttributionContext` ile aynıdır ve repo bu
kuralı zaten uyguluyor: singleton servisler buna bağımlanır, scoped kayıt
captive dependency olur. Sözleşme bunu XML dokümanında **açıkça** yazar ve
`<example>` tam olarak yukarıdaki şekli gösterir.

## 142.2 — 🚨 Burada fail-closed YANLIŞ olur

`IToolAuthorizationHandler` ve `IToolArgumentsValidator` `throw` ettiğinde çağrı
**reddedilir** — ikisi de kapıdır. Bu çözümleyici bir kapı **değildir**.

| Durum | Davranış | Gerekçe |
|---|---|---|
| Kayıtlı değil | Bugünkü çıktı aynen | K1 — sıfır sürpriz |
| `null` döner | Bugünkü çıktı aynen | Çözümleyici "bilmiyorum" diyebilmeli |
| `throw` eder | 🚨 **Onay isteği yine yayımlanır**, sunum alanları boş | Bir **sunum** hatası, güvenlik gerektiren bir onayı engellememelidir |
| Zaman aşımına uğrar | Aynı — istek yayımlanır | Aynı gerekçe |

Bu, kütüphanenin fail-closed duruşunun bir istisnası değil, onun doğru
uygulanmasıdır: kapı fail-closed olur, süsleme fail-open.

**Zaman aşımı zorunludur.** Çözümleyici dış kaynağa gidiyor; sınırsız bekleme
onay isteğini asar. Süre `AgentPrismToolOptions` ailesine girer; varsayılan
uygulamada ölçülüp yazılacak.

## 142.3 — Alanlar nereye görünür

| Yüzey | Ne taşır |
|---|---|
| `GET /api/approvals/pending` | `entityType`, `entityId`, `entityName`, `message` |
| `GET /api/approvals/{id}` | Aynı |
| `RunAwaitingInput` olayının payload'ı | Aynı (🚨 K-647 kapısı: payload iddiası okuyan bir test ister) |
| Konsol onay ekranı | Varlık adı başlıkta, ham argümanlar katlanmış hâlde |

🚨 **Ham argümanlar kaybolmaz.** Sunum onların **üstüne** gelir. Bir operatör
neyin onaylandığını her zaman tam olarak görebilmelidir; süsleme denetim izini
daraltamaz.

---

## Planlanan Public API

```csharp
// AgentPrism.Abstractions
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

public sealed record PendingApproval
{
    // mevcut alanlar…
    public ToolApprovalPresentation? Presentation { get; init; }
}
```

**Kayıt:** `TryAddSingleton<IToolApprovalPresenter, NullToolApprovalPresenter>()`.

### HTTP `endpoint`'leri

Yeni uç yok; iki uç additive alan taşır.

### Arayüz payı

Onay kartına başlık + katlanır ham argüman bölümü. Bundle payı **ölçülüp
yazılacak**. Yeni metin `locales/en.ts` **ve** `tr.ts` — eksik anahtar derleme
hatasıdır (K-228).

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
└── Approvals/
    ├── IToolApprovalPresenter.cs         (yeni)
    ├── ToolApprovalPresentation.cs       (yeni)
    └── PendingApproval.cs                (Presentation alanı)

src/AgentPrism.Core/
├── Approvals/NullToolApprovalPresenter.cs (yeni — null döner)
├── Approvals/ToolApprovalPresenterRunner.cs (yeni — timeout + hata yutma)
└── Tools/ApprovingAIFunction.cs           (çözümleyici çağrısı — konum ölçülmeli)

src/AgentPrism.UI/frontend/src/
├── screens/approvals.tsx                  (sunum + katlanır ham argüman)
└── locales/{en,tr}.ts                     (yeni anahtarlar)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| 🚨 Çözümleyici `throw` edince onay isteği **kaybolur** | Fonksiyonel | `ToolApprovalPresenterTests` |
| Zaman aşımı yok; onay isteği asılı kalır | Fonksiyonel | `ToolApprovalPresenterTests` |
| 🚨 Çözümleyici scoped bağımlılığı çözemez (K-218) | Fonksiyonel | `ToolApprovalPresenterScopeTests` — **gerçek DI konteyneriyle** |
| Ham argümanlar sunumla **değiştirilir** (denetim izi daralır) | Fonksiyonel | `ApprovalEndpointTests` |
| Kayıtlı değilken çıktı değişir | Fonksiyonel | `ApprovalEndpointTests` |
| Başka kiracının varlık adı sızar | Sözleşme | `TenantIsolationContract` |
| Kuyruğa alınan run'da sunum düşer (K-103 sınıfı) | Fonksiyonel | `QueuedApprovalTests` |
| `RunAwaitingInput` payload iddiası testsiz kalır | Birim | `RunEventPayloadContractTests` (K-647) |
| Çözümleyici iptal edilir | Fonksiyonel | `ToolApprovalPresenterTests` |
| Aynı anda iki onay isteği yarışır | Fonksiyonel | `ToolApprovalConcurrencyTests` |
| Çok uzun `Message` konsolu bozar | E2E | `ApprovalScreenTests` |
| `en.ts`/`tr.ts` anahtarı eksik | Derleme | K-228 |

🚨 **`ToolApprovalPresenterScopeTests` gerçek bir DI konteyneri kurar.** İzole
ölçüm entegre davranışı kanıtlamaz — `MEMORY.md`'deki K-218 vakasında ayrı bir
konsol probunda aynı çağrı **çalışıyordu**.

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Çözümleyici yok | `cancel_order` ile onay tetikle | Bugünkü çıktı; fark yok |
| 2 | Sipariş adını çözen çözümleyici | Aynı | Konsolda sipariş adı görünür, ham argüman katlanmış durur |
| 3 | Aynı | `GET /api/approvals/pending` | `entityName` dolu |
| 4 | `throw` eden çözümleyici | Onay tetikle | 🚨 Onay isteği **yine görünür**, sunum boş |
| 5 | Yavaş (5 sn) çözümleyici | Onay tetikle | Zaman aşımı; istek yayımlanır |
| 6 | Scoped DB bağımlılığı olan çözümleyici | Onay tetikle | Ad çözülür — `EmptyServiceProvider` tuzağına düşmez |
| 7 | Aynı | Ham argümanı aç | Tam argüman görünür |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Sunum alanları kalıcı mı olsun? | A: `pending_approvals`'a sütun (üç migration) · B: Yalnız yanıt anında çözülsün | **A** — onay kuyruktan sürdürülüyor (K-103) ve çözümleyici o an başka bir süreçte olabilir. Ama üç migration maliyet; uygulama ölçsün |
| 2 | Çözümleyici çağrısı `ApprovingAIFunction`'da mı, uç katmanında mı? | A: Tool sarmalayıcısında · B: Uçta | **A** — SSE akışındaki `RunAwaitingInput` da alanları taşımalı; uçta çözülürse akış onu göremez. 🚨 Konum **grep ile ölçülerek** doğrulansın (K-320 dersi) |
| 3 | Zaman aşımı süresi? | A: 2 sn · B: `TimeoutSeconds` gibi ayarlanabilir | **B** varsayılan **ölçülerek** seçilir; sabit sayı uydurulmaz |

---

## Bitiş Ölçütleri (DoD)

- [x] Çözümleyici kayıtlı değilken **hiçbir** davranış değişmez —
  `ToolApprovalPresenterTests.Unregistered_presenter_resolves_nothing_and_is_never_called`
  (spy asla çağrılmaz) + `ToolApprovalPresenterRunner`'ın kendi hızlı yolu
  (`presenter is NullToolApprovalPresenter` → hiç `ToolApprovalContext`
  kurulmaz).
- [x] 🚨 `throw` eden çözümleyici onay isteğini **engellemez** —
  `ToolApprovalPresenterTests.Throwing_presenter_does_not_block_the_approval_request`.
- [x] Zaman aşımı çalışır; istek yine yayımlanır —
  `ToolApprovalPresenterTests.Presenter_past_the_configured_timeout_resolves_null_without_blocking`
  (20 ms yapılandırılmış zaman aşımı, sonsuz bekleyen sahte çözümleyiciye
  karşı 5 saniyeden kısa sürede döner).
- [x] 🚨 Scoped bağımlılık gerçek DI konteynerinde çözülür (K-218 kapanır) —
  `ToolApprovalPresenterScopeTests.Presenter_resolves_a_scoped_dependency_through_the_real_container`,
  gerçek `ServiceCollection.BuildServiceProvider()` + `IServiceScopeFactory`.
- [x] Ham argümanlar her zaman erişilebilir kalır — `PendingApprovalStoreContract`
  ve gerçek örnek uygulama koşumunda (aşağıda) her durumda `arguments` dolu.
- [x] Alanlar `pending` ucunda **ve** `RunAwaitingInput` payload'ında görünür —
  gerçek koşum kanıtı aşağıda.
- [x] Bundle payı ölçüldü; `en.ts`/`tr.ts` eksiksiz — `npm run build`:
  `javascript : 177.3 KB gzipped (budget 250 KB)` (Faz 141'den beri değişim
  yok denecek kadar küçük; bu fazın kendi JS eklentisi `<1 KB`).
- [x] Dört doğrulama kapısı sıfır uyarı verir — aşağıdaki "Doğrulama Kapıları
  Çıktısı" bölümü.
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
  — aşağıda.
- [x] `secret` taraması boş döndü — `python3 scripts/kapi.py tarama`'nın
  `olası secret` bölümü boş (yalnız migration-manifest sıralama boşluğu
  kaldı, bkz. Sonraki Faza Devir Notu).
- [x] Manuel kabul case'leri eklendi — **`07-HTTP-YONETIM-API.md` DEĞİL**,
  bkz. Plandan Sapmalar #2: `21-DAYANIKLILIK-VE-IPTAL.md` (MT-RES-020
  genişletildi, MT-RES-073 yeni) ve `10-ARAYUZ-AGENT-PLAYGROUND.md`
  (MT-UIAG-028 düzeltildi, MT-UIAG-053 yeni).
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları.
- [x] `docs-site/` güncellendi + onay ekranı görüntüsü yenilendi; `npm run build` +
  `check-links.mjs` temiz — `npm run check` (check:content · build ·
  check:links · check:weight) dördü de yeşil;
  `docs-site/public/screenshots/approvals.png`
  `AGENTPRISM_UI_SCREENSHOTS=1` ile yenilendi (gerçek bir `IToolApprovalPresenter`
  kayıtlı E2E host'ta, artık boş liste değil `Order ORD-7` satırı gösteriyor).

### Gerçek koşum kanıtı (`samples/AgentPrism.Api`, gerçek OpenAI sağlayıcısı)

Kuyruklu yol — `Prefer: respond-async` ile `ORD-1001` iptali:

```
POST /api/agents/support/run  (Prefer: respond-async, sessionId=faz142-demo)
→ 202 {"runId":"01a06b96-062d-7af2-a9c8-b0c1a7539332", …}

GET /api/runs/{runId}          → status: "AwaitingApproval" (ilk sorguda)

GET /api/approvals/pending     → [{
  "toolName": "cancel_order",
  "arguments": "orderId=ORD-1001",
  "presentation": {
    "entityType": "order", "entityId": "ORD-1001",
    "entityName": "Order ORD-1001",
    "message": "Cancel order ORD-1001 for Priya Shah."
  },
  "status": "Pending", …
}]

GET /api/runs/{runId}/events   → son olay:
  event: RunAwaitingInput
  payload: [{"requestId":"ficc_call_…","toolName":"cancel_order",
    "entityType":"order","entityId":"ORD-1001",
    "entityName":"Order ORD-1001",
    "message":"Cancel order ORD-1001 for Priya Shah."}]

POST /api/approvals/{id}/decide {"approved":true}
→ 200 {"status":"Approved", "presentation": {…SAME…}, …}
```

Senkron yol (playground stili) — `ORD-1002`, `event: approvals` çerçevesi:

```
POST /api/agents/support/run  (Prefer YOK — SSE akışı)
…
id: 13
event: update
data: {"contents":[{"$type":"toolApprovalRequest","toolCall":{"$type":"functionCall",
  "name":"cancel_order","arguments":{"orderId":"ORD-1002"}, …},
  "requestId":"ficc_call_ejhBzi5SUOy6MxANJrmIl9U6"}]}

id: 14
event: approvals
data: [{"requestId":"ficc_call_ejhBzi5SUOy6MxANJrmIl9U6","toolName":"cancel_order",
  "entityType":"order","entityId":"ORD-1002","entityName":"Order ORD-1002",
  "message":"Cancel order ORD-1002 for Marcus Lee."}]

id: 15
event: done
```

Fail-open — bilinmeyen `ORD-9999`:

```
GET /api/approvals/pending → [{"toolName":"cancel_order",
  "arguments":"orderId=ORD-9999", "presentation": null, "status":"Pending", …}]
```

İstek yine yayımlandı, ham argüman dolu, `presentation` yalnız `null` —
tam olarak 142.2'nin dört-durum tablosunun vaat ettiği gibi.

### Doğrulama Kapıları Çıktısı

- `dotnet build AgentPrism.slnx -c Release` — 0 uyarı, 0 hata (temiz `artifacts/`
  ile üç kez doğrulandı; ilk iki koşumun kırmızıları bu oturumun kendi eşzamanlı
  ad-hoc `dotnet pack`/`dotnet build` çağrılarının `artifacts/package/`'ı
  kirletmesinden kaynaklanıyordu — kod kusuru değildi, `rm -rf artifacts` + tek
  seferlik temiz koşumla doğrulandı).
- `dotnet test AgentPrism.slnx --no-build` — Core (2341), AspNetCore.FunctionalTests
  (766), Workflows.UnitTests (107), Sql.Shared.UnitTests (20),
  PostgreSql/SqlServer/Sqlite.IntegrationTests (gerçek Testcontainers ile),
  Generators.UnitTests (271, `<example>` blokları dahil), Ui.E2ETests
  (ekran görüntüsü koşumu dahil) — tümü yeşil.
- `python3 scripts/kapi.py tarama` — senkronizasyon kopyası: temiz; `secret`:
  temiz; bayat doküman referansı: temiz; migration-integrity: kırmızı (BEKLENEN,
  bkz. Sonraki Faza Devir Notu — kapanış commit'inden sonra ayrı bir commit'le
  kapanır).
- `docs-site`: `npm run check` (check:content · build · check:links ·
  check:weight) dördü de yeşil.

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 K-218: çözümleyici çalışma anında servis çözer ve `null` alır | Kayıt biçimi karara bağlandı (singleton + `IServiceScopeFactory`); `ToolApprovalPresenterScopeTests` gerçek konteynerle ölçer |
| Sunum hatası onayı engeller (yanlış fail-closed) | Dört durum tabloyla sabitlendi; case 4 kanıt |
| Çözümleyici konumu yanlış varsayılır (K-320 sınıfı) | Açık Soru 2 konumu **ölçmeyi** şart koşuyor |
| Sunum ham argümanın yerini alır ve denetim izi daralır | Ham argüman her zaman erişilebilir; testle kilitli |
| `secret` sunum metnine sızar | Çözümleyici tüketicinin kodudur; XML dokümanı uyarır. `secret` taraması DoD'de |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     ============================================================ -->

## Plandan Sapmalar

1. **🚨 `ApprovingAIFunction` hiç yazılmadı — planın merkezi varsayımı ölçümde
   yanlış çıktı.** `ToolRegistryWrapperOrderTests.cs`'te ZATEN ölçülmüş bir
   gerçek: MEAI'nin function-invoking istemcisi `ApprovalRequiredAIFunction`'ı
   `AITool.GetService(Type)` üzerinden BULARAK ayırır, `InvokeAsync`'i hiç
   çağırmadan — bir tool sarmalayıcısının onay tetikleyen çağrıda `InvokeAsync`'i
   hiç görmeyeceği anlamına gelir. Sunum çözümü bunun yerine
   `RunRecordingAgent`'a taşındı: `ToolApprovalRequestContent`'in ZATEN okunduğu
   tek nokta (`ChildRunApproval` ailesi). Açık Soru 2'nin cevabı NE
   `ApprovingAIFunction` NE uçtu — `RunRecordingAgent` oldu. Vaka
   `docs/hafiza/tool-onay-ve-yetkilendirme.md`'ye yazıldı.
2. **Manuel kabul case'leri `07-HTTP-YONETIM-API.md`'ye DEĞİL, mevcut yerleşik
   dosyalara eklendi.** `/api/approvals/*` uçlarının kuyruk senaryosu zaten
   `21-DAYANIKLILIK-VE-IPTAL.md` §3'te (`MT-RES-020`+) yaşıyordu — presentation
   alanları oraya (MT-RES-020 genişletildi, MT-RES-073 eklendi) ve canlı
   playground SSE senaryosu `10-ARAYUZ-AGENT-PLAYGROUND.md`'ye (MT-UIAG-028
   düzeltildi, MT-UIAG-053 eklendi) gitti — kurulu yerleşim korunuyor.
3. **Açık Soru 1 → A: kalıcı.** `pending_approvals.presentation` jsonb/text
   sütunu (3 migration: Postgres 0045, SqlServer/Sqlite 0032). Gerekçe planın
   önerdiği gibi: onay kuyruktan sürdürülüyor ve çözümleyici o an başka bir
   süreçte/pencerede olabilir; yeniden çözmek tutarsız bir "aynı istek farklı
   anda farklı ad" riski taşırdı.
4. **Açık Soru 3 → 2 saniye, `AgentPrismToolOptions.ApprovalPresentationTimeout`
   ile ayarlanabilir.** Gerekçe: çözümleyici tek bir hızlı, salt okunur arama
   (birincil anahtarla nokta okuma) yapmalı — gerçek iş değil; kısa varsayılan
   yalnız bir isim kaybettirir, onayın kendisini asla geciktirmez.
5. **🚨 `AgentPrismRunOptions.Clone()` sessizce `BeforePendingApprovalIsPublished`'ı
   düşürüyordu — fazın kapsamı dışında, K-103 döneminden kalma bir kusur, bu
   fazda dokunulan aynı satırda bulunup düzeltildi.** Private copy ctor bu
   alanı hiç kopyalamıyordu; `Clone()`'un kendi XML dokümanı "tüm alanları
   korur" diyordu, kod tutmuyordu. Bağımsız denetimin 🟡 #3 bulgusu.
6. **🚨 `RunEventWriter.CompleteAsync`'in kapanış olayı switch'i
   `RunStatus.AwaitingApproval`'ı hiç eşlemiyordu, default kola düşüp HER
   onay-bekleyen kökü `RunFailed` + "The run was canceled." olarak
   yayımlıyordu.** Kusur `RunRecordingAgentOutcomeMatrixTests`'in kendi
   yorumunda bilerek pin'lenmişti ("gelecekte düzeltilecek quirk"); fazın
   kendi DoD'si (RunAwaitingInput payload'ı) bu düzeltmeyi zorunlu kıldı. Vaka
   `docs/hafiza/cekirdek-calistirma.md`'de.
7. **Embedding point sayısı altıdan yediye çıktı — bağımsız denetimin 🟡 #2
   bulgusuyla bulundu, plan bunu öngörmüyordu.** `IToolApprovalPresenter`
   diğer altı genişleme noktasıyla (TryAdd + fail-open, tüketici override
   kazanır) aynı ailede olduğu hâlde ne `GET /api/diagnostics`'e ne
   `capabilities.md`'ye eklenmişti; ikisi de düzeltildi
   (`AgentPrismDiagnosticsCollector`, `docs-site/.../capabilities.md`).
8. **`guides/embedding.md`'nin "six points" listesine 7. madde EKLENMEDİ —
   bilinçli kapsam kararı.** O liste embedding noktalarının GENEL ailesi
   değil, bir widget'ı BAŞKA bir uygulamaya gömmenin altyapı-yapıştırma
   sorunlarıdır (kiracı, atıf, yetkilendirme, event bridge, depolama, run/session
   yetkilendirmesi); onay sunumu farklı bir eksen. Listeyi "yedi nokta"ya
   çevirip başlığı değiştirmek yerine §3'e çapraz referans eklendi.

## Bu Fazda Verilen Kararlar

| Karar | Kayıt |
|---|---|
| Sunum alanları `pending_approvals`'a kalıcı sütun olarak yazılır (Açık Soru 1 → A) | K-675 |
| `IToolApprovalPresenter` fail-open'dır — kayıtlı değil/`null` döner/`throw` eder/zaman aşımına uğrar dört durumun HİÇBİRİ onay isteğinin yayımını engellemez | K-676 |

Tam gerekçe `docs/KARARLAR.md`'de (Bölüm 2, sona eklendi).

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions
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

// PendingApproval: +Presentation
public sealed record PendingApproval
{
    // … mevcut alanlar
    public ToolApprovalPresentation? Presentation { get; init; }
}

// AgentPrismRunOptions.BeforePendingApprovalIsPublished — İMZA DEĞİŞTİ (Unshipped, kırıcı değil):
public Func<
    IEnumerable<ChatMessage>,
    IReadOnlyDictionary<string, ToolApprovalPresentation?>, // YENİ 2. parametre
    CancellationToken,
    ValueTask>? BeforePendingApprovalIsPublished { get; init; }

// AgentPrism.Core
public sealed class ToolApprovalPresenterRunner // singleton, timeout + fail-open sarmalayıcı
{
    public ValueTask<IReadOnlyDictionary<string, ToolApprovalPresentation?>> ResolveAllAsync(
        IReadOnlyList<ToolApprovalRequestContent> requests, string tenantId, string? agentName,
        CancellationToken cancellationToken);
}

// AgentPrismToolOptions: +ApprovalPresentationTimeout (varsayılan 2 sn)
// RunEventType.RunAwaitingInput: Payload artık AwaitingApproval için de dolu (bkz. XML doküman)
// Kayıt: TryAddSingleton<IToolApprovalPresenter, NullToolApprovalPresenter>()
```

**Planlanmış ama gerçekleşmeyen:** `ApprovingAIFunction` (bkz. Plandan Sapmalar #1).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
├── Approvals/IToolApprovalPresenter.cs        (yeni)
├── Approvals/ToolApprovalPresentation.cs      (yeni)
├── Approvals/PendingApproval.cs               (Presentation alanı)
├── Runs/AgentPrismRunOptions.cs               (hook imzası + Clone() kusuru)
├── Runs/RunEventType.cs                       (RunAwaitingInput doküman)
└── Diagnostics/{AgentPrismDiagnosticsReport,ExtensionPointDiagnostic}.cs (7. nokta)

src/AgentPrism.Core/
├── Approvals/NullToolApprovalPresenter.cs     (yeni)
├── Approvals/ToolApprovalPresenterRunner.cs   (yeni)
├── Approvals/FunctionCallArguments.cs         (yeni — ToolApprovalRuleEvaluator ile paylaşılan)
├── Approvals/PendingToolApprovalEventItem.cs  (yeni — olay payload DTO'su)
├── Graph/ChildAgentInvoker.cs                 (ChildRunApproval.CollectRequests)
├── Recording/RunEventWriter.cs                (AwaitingApproval kapanış olayı kusuru)
├── Recording/RunRecordingAgent.cs + .Completion.cs (sunum çözümü + payload üretimi)
├── Recording/RunRecordingAgentDecorator.cs    (approvalPresenterRunner kaydı)
├── Scheduling/AgentRunJobHandler.cs           (Presentation → PendingApproval)
├── Diagnostics/AgentPrismDiagnosticsCollector.cs (7. nokta)
├── AgentPrismOptions.cs                       (ApprovalPresentationTimeout)
├── AgentPrismCoreJsonContext.cs               (payload DTO serileştirme)
├── AgentPrismServiceCollectionExtensions.Binding.Core.cs      (timeout config binding)
└── AgentPrismServiceCollectionExtensions.Registration.Core.cs (kayıt)

src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs  (approvals SSE çerçevesi)

src/AgentPrism.Sql.Shared/Internal/{SqlQueriesBase,AgentPrismJsonContext}.cs
src/AgentPrism.Sql.Shared/Stores/SqlPendingApprovalStore.cs
src/AgentPrism.PostgreSql/{Internal/PostgresQueries.cs, Migrations/0045_pending_approval_presentation.sql}
src/AgentPrism.SqlServer/{Internal/SqlServerQueries.cs, Migrations/0032_pending_approval_presentation.sql}
src/AgentPrism.Sqlite/{Internal/SqliteQueries.cs, Migrations/0032_pending_approval_presentation.sql}

src/AgentPrism.UI/frontend/src/
├── lib/transcript.ts                          (presentation alanları + applyApprovalPresentations)
├── components/transcript.tsx                  (varlık adı başlıkta, katlanır argümanlar)
├── screens/approvals.tsx                      (varlık adı + katlanır argümanlar)
├── screens/playground/use-playground-run.ts   (approvals SSE çerçevesi işleme)
└── locales/{en,tr}/operations.ts              (approvals.rawArguments)

samples/AgentPrism.Api/{OrderApprovalPresenter.cs, Program.cs}
tests/AgentPrism.Ui.E2ETests/Infrastructure/{ScriptedApprovalPresenter.cs, UiHost.cs}
tests/AgentPrism.Ui.E2ETests/DocumentationScreenshotTests.cs (SeedPendingApprovalAsync + landmark)

tests/AgentPrism.Core.UnitTests/Approvals/{ToolApprovalPresenterTests,ToolApprovalPresenterScopeTests}.cs (yeni)
tests/AgentPrism.Core.UnitTests/Scheduling/AgentRunJobHandlerTests.cs (queue path presentation testi)
tests/AgentPrism.Core.UnitTests/Diagnostics/DiagnosticsCollectorTests.cs (7. nokta)
tests/AgentPrism.Core.UnitTests/Recording/RunRecordingAgentOutcomeMatrixTests.cs (RunAwaitingInput + payload)
tests/AgentPrism.Core.UnitTests/Configuration/ServiceRegistrationSnapshotTests.cs
tests/AgentPrism.AspNetCore.FunctionalTests/DiagnosticsEndpointTests.cs (7. nokta)
tests/AgentPrism.Testing.Contracts.Xunit/Contracts/PendingApprovalStoreContract.cs (+2 case, tüm store'larda koşar)

docs-site/src/content/docs/{concepts/governance.md, concepts/tools.md, guides/embedding.md, capabilities.md}
```

## Denetim Bulguları

Bağımsız denetim ([`faz-denetim`](../../../.agents/skills/faz-denetim/SKILL.md)) 🔴 bulgu üretmedi.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Kuyruk yolunun (`AgentRunJobHandler`) `IToolApprovalPresenter`'ı gerçekten `PendingApproval.Presentation`'a yazdığını kanıtlayan hızlı/izole bir test yoktu — yalnız E2E ekran görüntüsü testi. | **Düzeltildi**: `AgentRunJobHandlerTests.A_registered_presenter_reaches_the_persisted_PendingApproval_row` — gerçek `RunRecordingAgent` + `AgentRunJobHandler` + `InMemoryPendingApprovalStore` zincirini DI/HTTP olmadan koşar. |
| 2 | 🟡 | `IToolApprovalPresenter` diğer altı "TryAdd + fail-open" genişleme noktasıyla aynı ailede olduğu hâlde `capabilities.md`'ye ve `AgentPrismDiagnosticsCollector`'a (`GET /api/diagnostics`) eklenmemişti. | **Düzeltildi**: yedinci nokta olarak ikisine de eklendi; `DiagnosticsCollectorTests` ve `DiagnosticsEndpointTests`'in "altı"ya sabit sayıları "yedi"ye güncellendi. |
| 3 | 🟡 | `AgentPrismRunOptions.Clone()`'un `BeforePendingApprovalIsPublished`'ı kopyalamaması (K-103 döneminden kalma, bu fazda dokunulan satırda bulunan) hiçbir yerde açıkça kayıt altına alınmamıştı. | **Gerekçelendi/kayıt altına alındı**: Plandan Sapmalar #5. |
| 🟢 | 🟢 | `ChildRunApproval.Describe`/`CollectRequests` neredeyse aynı döngüyü iki kez yürütüyor. | **Devredildi**: ayrı bir temizlik fazına değecek kadar önemli değil; birleştirilmedi, kod tekrarı davranışı etkilemiyor. |
| 🟢 | 🟢 | `ToolApprovalPresenterRunner.ResolveAllAsync` N>1 bekleyen istekte sıralı çözer. | **Devredildi**: mantık N>1 için de doğru (paylaşılan mutable state yok); performans optimizasyonu, DoD veya güvenlik sınırı değil. |

Denetimin "temiz" bulduğu başlıklar: 3.2 (test tiyatrosu), 3.6 (plan dışı public API), 3.7 (repo kuralları).

## Sonraki Faza Devir Notu

- **`IToolApprovalPresenter` artık AgentPrism'in yedinci embedding noktasıdır.**
  Yeni bir genişleme noktası eklerken bu ikili kontrolü unutma:
  `AgentPrismDiagnosticsCollector.CollectExtensionPoints()` (kod) VE
  `docs-site/.../capabilities.md`'nin "Embedding points" tablosu (doküman) —
  ikisi de elle senkron tutulur, hiçbir kapı bu boşluğu otomatik yakalamaz
  (bağımsız denetimin bu fazda bulduğu tam boşluk buydu).
- **`AgentPrismRunOptions.BeforePendingApprovalIsPublished`'ın imzası
  ikinci bir parametre kazandı** (`IReadOnlyDictionary<string,
  ToolApprovalPresentation?>`). Bu hook'u okuyan/yazan başka bir yer varsa
  (bugün yalnız `AgentRunJobHandler` ve `AgentEndpoints.cs` var) imza-gövde
  taramasını (`grep -rn "BeforePendingApprovalIsPublished" src/`) çalıştır.
- **SSE `approvals` çerçevesi yalnız SENKRON streaming run ucundadır**
  (`AgentEndpoints.AgentRunStream.ExecuteStreamingAsync`), kuyruklu
  (`Prefer: respond-async`) yolda DEĞİL — kuyruklu yolun kendi mailbox'ı
  (`GET /api/approvals/pending`) zaten var. İkisini karıştırma; bağımsız
  denetim bu ayrımı görev bağlamındaki bir özetleme hatasında bulup düzeltti.
  bkz. `docs/hafiza/cekirdek-calistirma.md`.
- **`docs/manuel-test/`'in `docs/manuel-test/*.md` bütçesi %12 boşlukla DAR
  durumdaydı bu faz başlarken** (`dokuman-bakim.py --denetle`). Yeni case
  eklerken kısa yaz; bir sonraki faz bu ağacı taşırabilir — şimdi taşı,
  aşınca değil.
- **`scripts/applied-migrations.json`'a bu fazın 3 migration'ı için
  `sourceCommits` girdisi HENÜZ yazılmadı** — kapanış commit'inden SONRA,
  o commit'in SHA'sını referanslayan ayrı bir "docs: pin phase 142's
  migrations..." commit'iyle eklenir (bkz. phase 126/129/132/137/141
  emsali). Bu commit atılmadan `kapi.py tarama` migration-integrity
  kontrolünde kırmızı kalır — bu BEKLENEN bir sıralamadır, kusur değildir.
