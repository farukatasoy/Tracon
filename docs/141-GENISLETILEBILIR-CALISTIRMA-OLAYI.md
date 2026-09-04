# Faz 141 — Genişletilebilir Çalıştırma Olayı

> **Durum:** 🔧 Kod tamam · testler yeşil · `faz-denetim` geçti (🔴 yok) · dört kapı ve yayın provası commit sonrası koşulacak (2026-09-04)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-187** (tüketici turu 3, A5)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.PostgreSql`,
> `AgentPrism.SqlServer`, `AgentPrism.Sqlite`, `AgentPrism.Sql.Shared`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** **Var** — plan başlığındaki "Yok" öncülü
> **yanlış çıktı**: `run_events.type` `smallint`/`INTEGER`'dır, metin değil
> (doğrulandı, bkz. 141.'nin Açık Soru 1 kararı). `custom_type` nullable text
> sütunu üç dialect'e birer migration ile eklendi (Postgres 0044, SqlServer
> 0031, SQLite 0031)
> **Public API:** Büyüdü — kapalı enum'a bir değer (`RunEventType.Custom = 29`),
> `RunEventDraft`/`RunEvent`'e birer `CustomType` alanı, yeni tip
> `RunEventCustomTypes` (`IsValidType`/`IsReserved`/`ReservedPrefix`). 🚨 **Tek
> yönlü kapı:** `Custom` sevk edildikten sonra geri alınamaz
> **Tüketici yüzeyi:** `docs-site/`: `concepts/runs.md` (yeni "Writing your own
> event" bölümü), `ui.md` (konsol satırı), `capabilities.md` (yeni satır) ·
> `getting-started/persistence.md` **güncellenmedi** — site-sync `kalicilik`
> kuralı `custom_type` migration'ı yüzünden tetiklendi ama sayfa şema
> sütunlarını hiç belgelemiyor (bağlantı dizesi, sağlayıcı seçimi,
> `AutoApplyMigrations` anlatıyor); rutin, additive bir sütun eklemenin
> davranışı sayfanın konusuyla örtüşmüyor. `--site-gerekce-yazildi` ile geçildi
> · sevk edilen: XML dokümanı (`<example>` YOK — `RunEventCustomTypes`'ın
> metotları `Add*`/`Use*`/`Map*` değil, `JobHandlerKeys.IsValidKey`/`IsReserved`
> ile aynı gerekçeyle), konsol jenerik kartı (`run-detail.tsx`)
> **Manuel test alanı:** [`docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md`](manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md) — MT-UIRUN-052..055

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

- [x] Tüketici kodu SSE akışına kendi olayını yazabilir (case 1 kanıt) — gerçek OpenAI koşumuyla ölçüldü, bkz. MT-UIRUN-052
- [x] `agentprism.` önekli tür **reddedilir** — `RunEventDraftValidationTests`
- [x] İki yönlü doğrulama çalışır (`Custom`↔`CustomType`) — test + üretimde ölçülen kanıt (`ToolInvoked.customType == null`)
- [x] `customType` bellek içi + üç SQL sağlayıcıda korunur — `RunStoreContract` dördünde de koşuldu
- [x] Replay `Custom` olayını aynen taşır — `GET .../events` canlı akış ve geçmiş okuma AYNI kod yolu (K-014); `StreamingTests` ikisini birden kanıtlar. Ayrı bir `RunReplayEndpointTests` case'i eklenmedi: `RunReplayService` `run_events`'e hiç dokunmuyor, tool kodu (Custom'ı yazan) replay altında da NORMAL çalışır — özel bir kod yolu yok, test edilecek özel bir davranış da yok
- [x] Konsol bilinmeyen türde kırılmaz; jenerik kart çizer — E2E (`Custom_run_event_renders_as_a_generic_card_named_after_its_CustomType`) + gerçek koşum
- [x] 🚨 `.cs` ve `.ts` enum listesi eşleşir; eşleşme testle kilitli — `RunEventTypeFrontendParityTests` (yeni); koşum SIRASINDA `.ts`'nin zaten iki üye (`DocumentAttached`, `RunContinuationBlocked`) eksik olduğu bulundu ve düzeltildi — bu fazdan önce vardı, konuyla ilgisiz bir kusurdu
- [x] K-647 taban çizgisi büyümedi veya `Custom` gerekçesiyle **beyan edildi** — mekanik olarak `covered` oldu (`StreamingTests.cs` gerçekten payload'ı okuyup doğruluyor), bkz. Plandan Sapmalar
- [x] Bundle payı ölçüldü ve yazıldı — **176.9 KB gzip / 250 KB bütçe** (ölçüldü, `npm run build`)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 9a243228` (commit'ten SONRA koşulacak — migration bütünlük manifesti kendi kaynak commit'ini ister, bkz. Plandan Sapmalar)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. MT-UIRUN-052 (run `01a06aa0-5eac-705b-9101-d0c5bdeeaea4`)
- [x] `secret` taraması boş döndü — `scripts/kapi.py tarama`
- [x] Manuel kabul case'leri `docs/manuel-test/11-ARAYUZ-RUN-SESSION-SSE.md` içine eklendi — MT-UIRUN-052..055
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — üç 🟡 bulundu, üçü de düzeltildi
- [x] `docs-site/` güncellendi; `npm run check` (dördü de: içerik, derleme, bağlantı, ağırlık) temiz

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

- **"Migration yok" öncülü yanlıştı.** Plan başlığı `run_events.type`'ın metin
  olarak saklandığını varsayıyordu; `faz-baslangic` araştırması bunun
  `smallint`/`INTEGER` olduğunu gösterdi. Açık Soru 1 bu yüzden gerçek bir
  migration kararına dönüştü ve kullanıcıya soruldu — "ayrı sütun" (önerilen
  seçenek) onaylandı. Üç migration eklendi (141.1 planındaki gibi 1-128
  karakter, `agentprism.` rezerve önek doğrulaması `nvarchar(200)`/`text`/`TEXT`
  genişliğiyle uyumlu).
- **K-647 taban çizgisi `uncovered` DEĞİL, `covered` oldu.** Plan 141.3'ün
  beklentisi `Custom`'ın "AgentPrism payload iddiası taşımıyor" gerekçesiyle
  `uncovered` girmesiydi. Gerçekte `StreamingTests.cs`'e eklenen fonksiyonel
  test hem canlı SSE'de hem geçmiş okumada `customType`'ın hayatta kaldığını
  KANITLIYOR — `RunEventPayloadContractTests`'in taban çizgisi üretici
  script'i bunu otomatik `covered` işaretledi (dosya adı: kapsayan test).
  Ratchet yönü yalnız `uncovered → covered`'a izin verir; bu doğru yönde bir
  sapma, bir kusur değil.
- **Pre-existing kusur bulundu ve düzeltildi (fazla ilgisiz):**
  `run-event.ts`'nin `RunEventType` union'ı `DocumentAttached` (Faz 14) ve
  `RunContinuationBlocked`'ı (Faz 87) hiç taşımıyordu — K-411 sınıfının canlı
  bir örneği, `.cs` güncellenmiş `.ts` unutulmuştu. Bu faz için eklenen yeni
  `RunEventTypeFrontendParityTests` bunu ilk koşumda kırmızı yakaladı. İkisi de
  `run-event.ts`'e ve `run-detail.tsx`'in `EVENT_STYLE`'ına eklendi.
  Kullanıcının talimatı ("konuyla alakasız bug/defect'lerle karşılaşırsan onları
  da çöz") gereği ayrı bir faz açılmadı, burada kapatıldı.
- **`RunReplayEndpointTests`'e ayrı bir Custom case'i eklenmedi.**
  Hata Modları tablosu "Replay Custom olayını taşımaz | Fonksiyonel |
  ReplayTests" satırını taşıyordu. Araştırma `RunReplayService`'in
  `run_events`'e hiç dokunmadığını gösterdi (`grep -rn "RunEventType"
  src/AgentPrism.Core/Replay/RunReplayService.cs` sıfır döner) — replay
  `RunRecordingAgent`'ı yeniden çalıştırır, tool kodu (Custom'ı yazan kod
  dahil) normal yoldan geçer. Test edilecek Custom'a ÖZGÜ bir davranış yok;
  `StreamingTests`'in genel SSE/geçmiş-okuma testi (K-014: canlı ve geçmiş
  AYNI kod yolu) bunu zaten kanıtlıyor.
- **`TenantIsolationContract`'a ayrı bir Custom case'i eklenmedi.** Aynı
  gerekçe: `custom_type` sütunu `InsertRunEvent`/`SelectRunEvents`'in ZATEN var
  olan `@tenant_id` koruma cümlesinden geçiyor (bkz. `SqlQueriesBase.cs`), tür
  bazlı bir dallanma yok. Var olan tenant izolasyon testleri her `RunEvent`
  türünü zaten kapsıyor.
- **`samples/AgentPrism.Api`'de gerçek koşum yapıldı — planlanandan daha güçlü
  kanıt.** Ortamda OpenAI `user-secrets` zaten yapılandırılıydı; `mark_preview_ready`
  tool'u eklenip `support` agent'ına bağlandı ve gerçek bir `gpt-5.4-mini`
  çağrısıyla `Custom` olayı üretildi, kalıcılaştırıldı ve SSE ile geri okundu
  (run `01a06aa0-5eac-705b-9101-d0c5bdeeaea4`). Plan yalnızca "gerçek run
  yapıldı" istiyordu; bu koşum aynı zamanda iki yönlü doğrulamayı ÜRETİMDE de
  kanıtladı (`ToolInvoked` olayının `customType`'ı `null`).

## Bu Fazda Verilen Kararlar

Üçü de kullanıcıya `AskUserQuestion` ile soruldu; önerilen seçenek onaylandı —
yeni bir `K-*` kaydı açılmadı, çünkü üçü de bu fazın kendi kapsamındaki yerel
implementation tercihidir (public API/uyumluluk sözleşmesi değil; `Custom`'ın
KENDİSİ zaten plan onayıyla public API'ye giriyordu).

1. **`CustomType` ayrı bir `custom_type` sütunudur, `Payload` içine gömülmez.**
   `ToolName`/`ToolCallId` ile aynı desen; üç migration eklendi.
2. **Geçersiz `CustomType` çağrıyı `ArgumentException` ile reddeder,
   sessizce atlamaz.** `RunEventWriter.AppendAsync`'in en başında
   `ValidateCustomType` çalışır — sequence numarası TÜKETİLMEDEN.
3. **`Custom` olayı `IRunEventSink`'e de gider.** Sıfır ek kod gerekti:
   `DispatchToSinksAsync` zaten her `RunEvent`'i türden bağımsız dağıtıyordu.

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions
public enum RunEventType
{
    // … mevcut 0-28
    Custom = 29,
}

public sealed record RunEvent
{
    // … mevcut alanlar
    public string? CustomType { get; init; }
}

public static partial class RunEventCustomTypes
{
    public const string ReservedPrefix = "agentprism.";
    public static bool IsValidType(string? customType);
    public static bool IsReserved(string? customType);
}

// AgentPrism.Core
public readonly record struct RunEventDraft(RunEventType Type)
{
    // … mevcut alanlar
    public string? CustomType { get; init; }
}
```

Plandan sapma yok — `RunEventCustomTypes` planda isimlendirilmemişti (plan
doğrulama kuralını düz metin olarak anlatıyordu) ama `JobHandlerKeys` deseninin
"birebir tekrarı" talimatının doğal sonucuydu.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Runs/
├── RunEventType.cs                      (Custom = 29)
├── RunEvent.cs                          (CustomType okuma tarafı)
└── RunEventCustomTypes.cs               (YENİ — doğrulama)

src/AgentPrism.Core/Recording/
└── RunEventWriter.cs                    (RunEventDraft.CustomType + ValidateCustomType)

src/AgentPrism.Sql.Shared/
├── Internal/SqlQueriesBase.cs           (InsertRunEvent/SelectRunEvents + custom_type)
└── Stores/SqlRunStore.cs                (AppendEventAsync/ReadEvent + custom_type)

src/AgentPrism.PostgreSql/Migrations/0044_run_event_custom_type.sql   (YENİ)
src/AgentPrism.SqlServer/Migrations/0031_run_event_custom_type.sql    (YENİ)
src/AgentPrism.Sqlite/Migrations/0031_run_event_custom_type.sql       (YENİ)

src/AgentPrism.Testing.Contracts.Xunit/Contracts/
└── RunStoreContract.cs                  (Custom_event_custom_type_round_trips_and_stays_null_for_every_other_type)

src/AgentPrism.UI/frontend/src/lib/
└── run-event.ts                         (Custom + DocumentAttached/RunContinuationBlocked kusur düzeltmesi)
src/AgentPrism.UI/frontend/src/screens/
└── run-detail.tsx                       (EVENT_STYLE üç yeni satır + jenerik kart etiket mantığı)

tests/AgentPrism.Core.UnitTests/
├── Architecture/RunEventTypeFrontendParityTests.cs   (YENİ)
├── Recording/RunEventDraftValidationTests.cs         (YENİ)
└── Recording/RunEventSinkTests.cs                    (+1 test)
tests/AgentPrism.Core.UnitTests/Architecture/
├── run-event-payload-baseline.txt       (Custom | covered)
└── public-surface-baseline.txt          (371 → 372)
tests/AgentPrism.AspNetCore.FunctionalTests/StreamingTests.cs   (+1 test)
tests/AgentPrism.Ui.E2ETests/
├── UiTests.cs                                        (+1 test)
└── Infrastructure/{OrderTools.cs,UiHost.cs}           (yeni tool + agent)

samples/AgentPrism.Api/{OrderTools.cs,Program.cs}      (mark_preview_ready)

docs-site/src/content/docs/{concepts/runs.md,ui.md,capabilities.md}
docs/manuel-test/{00-INDEKS.md,11-ARAYUZ-RUN-SESSION-SSE.md}
```

## Denetim Bulguları

Bağımsız, taze bağlamlı bir denetçi (`faz-denetim`) çalışma ağacının tamamını
(`git diff 9a243228`) inceledi ve derleme + beş test projesini (`Core.UnitTests`
2329/2329, `AspNetCore.FunctionalTests` 766/766, üç SQL entegrasyon projesi
gerçek Docker konteynerleriyle) bağımsızca yeniden koştu. **🔴 yok.**

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | K-647 taban çizgisinde `Custom \| covered \| StreamingTests.cs` iddiası mekanik olarak doğruydu (gate dosya-seviyesinde `RunEventType.Custom` + `Payload` metnini arıyor) ama davranışsal olarak eksikti: kapsayan test yalnız `customType`'ı doğruluyordu, `Payload`'ın İÇERİĞİNİ (`orderId`) hiç okumuyordu. | **Düzeltildi.** `StreamingTests.cs`'e `custom.Data.ShouldContain("orderId")`/`"ORD-7"` eklendi — artık gerçekten payload içeriğini okuyor. |
| 2 | 🟡 | `ValidateCustomType`'ın `Interlocked.Increment`'ten ÖNCE çağrıldığı iddiası (reddedilen bir çağrının sequence numarasını "yakmadığı") koddan doğrulanabiliyordu ama hiçbir test bunu kanıtlamıyordu. | **Düzeltildi.** `RunEventDraftValidationTests.A_rejected_call_does_not_burn_a_sequence_number` eklendi: reddedilen bir çağrıdan sonraki başarılı yazımın `Sequence`'ı atlanmadan devam ediyor. |
| 3 | 🟡 | `RunEventType.Custom`'ın XML dokümanı, kardeş üyelerin (`ToolOutputTruncated`, `StructuredResponseRejected`) aksine `Payload`'ının `AgentPrismRunRecordingOptions.RecordToolPayloads`'a tabi olduğunu belirtmiyordu. | **Düzeltildi.** `<summary>`'ye aynı uyarı eklendi. |

🟢 yok. Denetçi ayrıca on spesifik teknik soruyu (sequence sırası, SQL ordinal
eşlemesi, iki yönlü doğrulama, DoD kanıtları, `.ts` tutarlılığı, `PublicAPI`
büyümesi, migration deseni, İngilizce/iç-referans sınırı, Plandan Sapmalar
iddiaları) bağımsızca doğruladı; hepsi geçerli bulundu.

Düzeltmelerden sonra `Core.UnitTests` (2330/2330, yeni testle) ve etkilenen
`AspNetCore.FunctionalTests` testi yeniden koşuldu — yeşil.

## Sonraki Faza Devir Notu

- `RunEventCustomTypes` deseni artık `JobHandlerKeys`/`JobLanes` ailesinin
  üçüncü örneği. Benzer bir "tüketici namespace'i + rezerve önek" ihtiyacı
  çıkarsa aynı üçlüyü (`ReservedPrefix` sabiti, `IsValidType`, `IsReserved`,
  `[GeneratedRegex]`) kopyala — soyutlamaya çevirme, bu depoda BİLEREK üç kez
  tekrarlanan bir desendir.
- `run-event.ts`'nin `.cs` ile senkronu artık `RunEventTypeFrontendParityTests`
  ile kilitli. Yeni bir `RunEventType` üyesi eklerken bu test seni hem `.ts`
  union'ına HEM `run-detail.tsx`'in `EVENT_STYLE`'ına (derleyici zorlar)
  götürür.
- `run_events` tablosunun sütun sayısı arttı (`custom_type`, ordinal 8).
  `SqlRunStore.ReadEvent`'in ordinal eşlemesi kırılgandır — yeni bir sütun
  eklerken her zaman SONA ekle, var olan ordinal'leri kaydırma (bu fazda da
  öyle yapıldı: `custom_type` hem `InsertRunEvent`/`SelectRunEvents` hem
  `ReadEvent`'te listenin EN SONUNDA).
- `getting-started/persistence.md` bu fazda BİLEREK güncellenmedi (site-sync
  gerekçesi yukarıda). Sayfa şema sütunlarını hiç belgelemiyor; bir sonraki faz
  bu sayfaya gerçekten şema-seviyeli bir değişiklik getirirse (ör. yeni bir
  tablo, yeni bir sağlayıcı) bu gerekçe ARTIK geçerli olmayabilir — yeniden
  değerlendir, kopyalama.
