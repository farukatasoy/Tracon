# Faz 141 — Genişletilebilir Çalıştırma Olayı

> **Durum:** 📋 Planlandı (2026-09-03)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-187** (tüketici turu 3, A5)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — `run_events.type` zaten metin olarak saklanıyor (uygulamada **doğrulanmalı**)
> **Public API:** Büyüyor — kapalı enum'a bir değer + `RunEventDraft`'a bir alan. 🚨 **Tek yönlü kapı:** `Custom` sevk edildikten sonra geri alınamaz
> **Tüketici yüzeyi:** `docs-site/`: `concepts/runs.md` (olay tablosu), `guides/embedding.md`, `capabilities.md` · sevk edilen: XML `<example>`, konsol kartı
> **Manuel test alanı:** [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md)

---

## Bu Faza Başlarken

1. Bu doküman
2. Kararlar — yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-647\|K-411" docs/KARARLAR.md
   ```
   **K-647** (🚨 `RunEventType` üyesinin payload hakkındaki her iddiası, o
   payload'ı **okuyan** bir testle eşleşmek zorundadır —
   `RunEventPayloadContractTests`, yalnız küçülen `uncovered` taban çizgisi),
   **K-411** (senkronizasyon kopyaları — bu faz `.cs` **ve** `.ts` tarafına
   birlikte dokunuyor)
3. Alan hafızası (iki alan):
   [`hafiza/cekirdek-calistirma.md`](hafiza/cekirdek-calistirma.md) (olay yazma
   yolu) · [`hafiza/frontend.md`](hafiza/frontend.md) (konsol olay listesi
   **elle** bakılıyor)
4. `JobHandlerKeys` doğrulama kuralı — bu faz onu **birebir tekrarlar**:
   ```bash
   grep -n "" src/AgentPrism.Abstractions/Scheduling/JobHandlerKeys.cs
   ```

---

## Amaç

Tüketici bugün SSE akışına **hiçbir** kendi olayını yazamıyor. Yazma yolu açık —
`RunEventWriter.AppendAsync` public ve `AgentRunScope.Writer` erişilebilir — ama
taşınacak bir tür yok: `RunEventDraft.Type` kapalı bir enum.

Bu faz kapalılığı **korur** ve tek bir kaçış deliği açar.

- **F-187** — `RunEventType.Custom` ve onu niteleyen, ad alanı önekli bir
  `CustomType` string'i.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`RunEventType.cs:22-233`](../src/AgentPrism.Abstractions/Runs/RunEventType.cs) | Kapalı enum, **29 değer** (0–28). Son değer `StructuredResponseRepairAttempted = 28` |
| [`RunEventWriter.cs:356-368`](../src/AgentPrism.Core/Recording/RunEventWriter.cs) | `RunEventDraft` beş alan taşıyor: `Type`, `Text`, `ToolName`, `ToolCallId`, `Payload` |
| [`RunEventWriter.cs:139`](../src/AgentPrism.Core/Recording/RunEventWriter.cs) | `AppendAsync` **public** — yazma yolu açık |
| [`run-event.ts:8-10`](../src/AgentPrism.UI/frontend/src/lib/run-event.ts) | Konsol listesi **elle** bakılıyor; dosyanın kendi yorumu bunu yazıyor |
| `SqlServer/MigrationsViews/0001_read_views.sql:23` | `runs_v1` **run** seviyesindedir; olay türünden etkilenmez |

> Kanıtlar 2026-09-03 tarihinde doğrulandı.

### Neden kapalılık korunuyor

Kapalı enum bir sözleşmedir ve üç işi vardır: konsol her olayı tanıyıp doğru
çizer, replay sadakati korunur, `runs_v1` gibi sözleşme görünümleri kararlı
kalır. Serbest bir string tür bunların **hepsini** bozardı.

`Custom` ikisinin arasındaki yoldur: tür **kapalı** kalır, tüketicinin ayrımı
`CustomType` string'ine iner ve o string **AgentPrism'in sözleşmesinin parçası
olmaz**.

---

## 141.1 — Ad alanı ve rezerve önek

Kullanıcı kararı (2026-09-03): **çalışma anında reddet.**

`JobHandlerKeys` deseni birebir tekrarlanır — kod hazırdır ve tüketici o deseni
zaten tanıyor:

| Kural | Değer |
|---|---|
| Uzunluk | 1–128 karakter |
| Karakter kümesi | küçük harf ASCII, rakam, `.`, `_`, `-` |
| Rezerve önek | `agentprism.` — **reddedilir** |
| Zorunluluk | `Type == Custom` iken zorunlu; aksi hâlde `null` olmalı |

🚨 **İki yönlü doğrulama.** `Custom` iken `CustomType` boşsa **ve**
`Custom` değilken `CustomType` doluysa çağrı reddedilir. Tek yönlü doğrulama
sessiz bir kusur sınıfı üretir: `Custom` olmayan bir olaya `CustomType` yazan
tüketici onun taşınacağını sanır.

Rezerve önek zorlanmazsa **rezerve değildir**: ileride yerleşik bir `Custom`
türü eklenirse tüketiciyle çakışır.

## 141.2 — Konsolun sorumluluğu

Beklenti mütevazıdır ve tüketici bunu açıkça yazdı: özel çizim istenmiyor.

`Custom` olayı **tek bir jenerik kartla** çizilir: başlık `CustomType`, gövde
`Payload`'ın JSON gösterimi. Bilinmeyen bir `CustomType` konsolu **kırmaz** —
kart yine çizilir.

🚨 **`run-event.ts` elle bakılan bir kopyadır (K-411 sınıfı).** Enum'a değer
eklerken `.cs` ve `.ts` **birlikte** güncellenir; biri unutulursa build yeşil
kalır ama konsol olayı tanımaz.

## 141.3 — K-647 kapısı

`RunEventPayloadContractTests` her `RunEventType` üyesinin payload iddiasını
onu **okuyan** bir testle eşler ve `uncovered` taban çizgisi **yalnız küçülür**.

`Custom`'ın payload'ı hakkında AgentPrism **hiçbir iddiada bulunmaz** —
`Payload` tüketicinindir ve şekli sözleşme değildir. Plan bunu açıkça yazar,
çünkü taban çizgisi bir istisna değil bir **beyan** ister: `Custom`
`uncovered` listesine "AgentPrism payload iddiası taşımıyor" gerekçesiyle
girer, sessizce atlanmaz.

---

## Planlanan Public API

```csharp
// AgentPrism.Abstractions
public enum RunEventType
{
    // … mevcut 29 değer (0–28)

    /// <summary>Tüketicinin kendi olayı. CustomType onu niteler.</summary>
    Custom = 29
}

// AgentPrism.Core
public readonly record struct RunEventDraft(RunEventType Type)
{
    // mevcut: Text, ToolName, ToolCallId, Payload

    /// <summary>
    /// Type == Custom olduğunda zorunlu; aksi hâlde null olmalıdır.
    /// 1-128 karakter: küçük harf ASCII, rakam, '.', '_', '-'.
    /// "agentprism." öneki rezervedir ve reddedilir.
    /// </summary>
    public string? CustomType { get; init; }
}

// RunEvent üzerinde okuma tarafı
public sealed record RunEvent
{
    public string? CustomType { get; init; }
}
```

### HTTP `endpoint`'leri

Yeni uç yok. `GET /api/runs/{id}/events` ve SSE akışı `customType` alanını
taşır (additive; eski istemci alanı yok sayar).

### Arayüz payı

Jenerik kart + tür listesine bir değer. Bundle bütçesi 250 KB gzip; **payı
uygulamada ölçülüp yazılacak** — bugünkü kullanım
`ls -l src/AgentPrism.UI/wwwroot/assets/` ile alınır.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
├── Runs/RunEventType.cs                  (Custom = 29)
└── Runs/RunEvent.cs                      (CustomType okuma tarafı)

src/AgentPrism.Core/
└── Recording/RunEventWriter.cs           (RunEventDraft.CustomType + doğrulama)

src/AgentPrism.Sql.Shared/
└── Stores/SqlRunStore.cs                 (customType kalıcılığı — sütun mu payload mu, Açık Soru 1)

src/AgentPrism.UI/frontend/src/lib/
├── run-event.ts                          (🚨 elle bakılan liste — K-411)
└── transcript.ts                         (jenerik kart)
```

---

## Hata Modları ve Testler

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| `Custom` iken `CustomType` boş geçer | Birim | `RunEventDraftValidationTests` |
| `Custom` değilken `CustomType` dolu geçer (sessiz kusur) | Birim | `RunEventDraftValidationTests` |
| `agentprism.` önekli tür kabul edilir | Birim | `RunEventDraftValidationTests` |
| Geçersiz karakter kabul edilir | Birim | `RunEventDraftValidationTests` |
| 128 karakterden uzun tür kabul edilir | Birim | `RunEventDraftValidationTests` |
| `customType` depoda kaybolur | Sözleşme | `RunStoreContract` — bellek içi + üç SQL sağlayıcı |
| SSE akışında `customType` düşer | Fonksiyonel | `StreamingTests` |
| Replay `Custom` olayını taşımaz | Fonksiyonel | `ReplayTests` |
| Konsol bilinmeyen `CustomType`'ta kırılır | E2E (Playwright) | `RunEventCustomCardTests` |
| 🚨 `.cs` güncellenir, `.ts` unutulur | Fonksiyonel | mevcut enum eşleşme testi — **yoksa eklenir** |
| K-647 taban çizgisi sessizce büyür | Birim | `RunEventPayloadContractTests` |
| Başka kiracının `Custom` olayı görünür | Sözleşme | `TenantIsolationContract` |
| Eşzamanlı `Custom` yazımı `Sequence` çakıştırır | Fonksiyonel | `RunEventSequenceTests` |

---

## Manuel Kabul Case'leri

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `Custom` yazan bir tool | Run at, SSE akışını izle | `contoso.preview-ready` olayı akışta görünür |
| 2 | Aynı | Konsolda run'ı aç | Jenerik kart: başlık `contoso.preview-ready`, gövde JSON |
| 3 | `agentprism.test` yazmayı deneyen tool | Run at | Çağrı reddedilir; run **devam eder** veya hata net olur (Açık Soru 2) |
| 4 | Aynı run | Run'ı replay et | `Custom` olayı aynen taşınır |
| 5 | `Custom` olmadan `CustomType` veren kod | Derle ve koş | Reddedilir |
| 6 | Eski istemci (`customType` bilmeyen) | Akışı oku | Alan yok sayılır; kırılma yok |

---

## Açık Sorular

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `customType` ayrı bir sütun mu, `payload` içinde bir alan mı? | A: Ayrı sütun (üç migration) · B: `payload` içinde | **B** — üç migration'dan kaçınır ve `Payload` zaten serbest. Ama sorgulanabilirlik düşer; uygulama `run_events` şemasını **ölçerek** karar versin |
| 2 | Geçersiz `CustomType` run'ı düşürsün mü? | A: `ArgumentException` — çağrı düşer · B: Olay atlanır, uyarı loglanır | **A** — yazan taraf tüketicinin **kendi kodudur**, sessiz atlama kusuru gizler. Ama "gözlemlenebilirlik işlevselliği bozmaz" ilkesiyle gerilim var; uygulama bunu karara bağlasın |
| 3 | `Custom` olayı `IRunEventSink`'e de gitsin mi? | A: Gitsin · B: Gitmesin | **A** — sink "her olay" sözleşmesi taşıyor; istisna sözleşmeyi yalanlar |

---

## Bitiş Ölçütleri (DoD)

- [ ] Tüketici kodu SSE akışına kendi olayını yazabilir (case 1 kanıt)
- [ ] `agentprism.` önekli tür **reddedilir**
- [ ] İki yönlü doğrulama çalışır (`Custom`↔`CustomType`)
- [ ] `customType` bellek içi + üç SQL sağlayıcıda korunur
- [ ] Replay `Custom` olayını aynen taşır
- [ ] Konsol bilinmeyen türde kırılmaz; jenerik kart çizer
- [ ] 🚨 `.cs` ve `.ts` enum listesi eşleşir; eşleşme testle kilitli
- [ ] K-647 taban çizgisi büyümedi veya `Custom` gerekçesiyle **beyan edildi**
- [ ] Bundle payı ölçüldü ve yazıldı
- [ ] Dört doğrulama kapısı sıfır uyarı verir
- [ ] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü
- [ ] Manuel kabul case'leri `docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md` içine eklendi
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi; `npm run build` + `check-links.mjs` temiz

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 **Tek yönlü kapı.** `Custom` sevk edildikten sonra geri alınamaz | Kapsam bilinçli olarak dar: tek enum değeri, tek string, AgentPrism payload hakkında **hiçbir** iddia taşımaz. Sözleşme yüzeyi minimum |
| `.cs` / `.ts` kopyası ayrışır (K-411 sınıfı, beş kez tekrarladı) | Eşleşme testi kapı olur; yoksa bu fazda eklenir |
| Tüketici `Custom`'ı yerleşik türlerin yerine kullanır | XML dokümanı açıkça yazar: yerleşik bir tür varsa o kullanılır. Zorlanamaz; belgelenir |
| K-647 taban çizgisi sessizce büyür | `Custom` beyanla girer, atlanmaz |

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
