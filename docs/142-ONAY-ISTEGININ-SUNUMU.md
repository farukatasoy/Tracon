# Faz 142 — Onay İsteğinin Sunumu

> **Durum:** 📋 Planlandı (2026-09-03)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-188** (tüketici turu 3, A3)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Gerekli olabilir — sunum alanları kalıcı mı, Açık Soru 1. Numara uygulama anında alınır
> **Public API:** Büyüyor — yeni kontrat + `PendingApproval`'a alanlar
> **Tüketici yüzeyi:** `docs-site/`: `concepts/tools.md` (onay bölümü), `concepts/governance.md`, `guides/embedding.md` · sevk edilen: XML `<example>`, konsol onay ekranı + ekran görüntüsü
> **Manuel test alanı:** [`docs/manuel-test/07-HTTP-YONETIM-API.md`](manuel-test/07-HTTP-YONETIM-API.md)

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
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (🚨 tool
   bağımlılığı **kurulum anında** alınır) ·
   [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (singleton/scoped sınırı)

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
| [`PendingApproval.cs:19-66`](../src/AgentPrism.Abstractions/Approvals/PendingApproval.cs) | Alanlar: `Id`, `TenantId`, `RunId`, `SessionId`, `RequestId`, `ToolName`, `Arguments`, `Status`, `DecidedBy`, `DecidedAt`, `ExpiresAt`, `CreatedAt`. Sunum alanı **yok** |
| [`ToolApprovalContext.cs:14-26`](../src/AgentPrism.Abstractions/Approvals/ToolApprovalContext.cs) | `TenantId`, `ToolName`, `Arguments` + `GetNumber`/`GetString` yardımcıları. Metin üretmiyor |
| [`ApprovalEndpoints.cs:35,50,66`](../src/AgentPrism.AspNetCore/Endpoints/ApprovalEndpoints.cs) | Üç uç ham `Arguments` döndürüyor |

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

- [ ] Çözümleyici kayıtlı değilken **hiçbir** davranış değişmez
- [ ] 🚨 `throw` eden çözümleyici onay isteğini **engellemez** (case 4 kanıt)
- [ ] Zaman aşımı çalışır; istek yine yayımlanır
- [ ] 🚨 Scoped bağımlılık gerçek DI konteynerinde çözülür (K-218 kapanır)
- [ ] Ham argümanlar her zaman erişilebilir kalır
- [ ] Alanlar `pending` ucunda **ve** `RunAwaitingInput` payload'ında görünür
- [ ] Bundle payı ölçüldü; `en.ts`/`tr.ts` eksiksiz
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/07-HTTP-YONETIM-API.md` içine eklendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi + onay ekranı görüntüsü yenilendi; `npm run build` + `check-links.mjs` temiz

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

> Kapanışta doldurulur.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur.

## Gerçekleşen Public API

> Kapanışta doldurulur.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Denetim Bulguları

> Kapanışta doldurulur.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur.
