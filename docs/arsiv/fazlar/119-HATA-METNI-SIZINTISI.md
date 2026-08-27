# Faz 119 — Hata Metni Sızıntısının Kapatılması

> **Durum:** ✅ Tamamlandı (2026-08-27)
> **Kaynak:** [YAYIN-HAZIRLIK.md](../../YAYIN-HAZIRLIK.md) §16 — BL-027 · BL-037 (yayın denetimi bulgusu, aday listesinden değil)
> **Önkoşul:** Yok
> **Paketler:** `AgentPrism.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Mcp`
> **Yeni paket:** Yok · **Migration:** **Yok** — korelasyon kimliği mevcut metin alanına gömülür (bkz. 119.3)
> **Public API:** Büyüyor — `PublicAPI.Shipped.txt` boş (`wc -l src/*/PublicAPI.Shipped.txt` = 0), yüzey bugün ucuz
> **Tüketici yüzeyi:** `docs-site/` — hata sözleşmesini anlatan sayfa (`concepts/runs.md` ve HTTP hata bölümü) · sevk edilen: `RunError.Message`'ın XML dokümanı, `IJobHandler`/`IWebhookStore` hata alanlarının XML'i
> **Manuel test alanı:** `docs/manuel-test/` — mevcut hata/gözlemlenebilirlik ailesine eklenir

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-059\|K-639" docs/KARARLAR.md
   ```
   **K-059** (`secret` veritabanına da yazılmaz — bu fazın gerekçesinin kökü),
   **K-639** (aynı yayın denetiminin ilk kapatılan kusuru; sessiz güvenlik
   düşüşünün nasıl ele alındığına dair taze emsal)
3. [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) — yalnız §16:
   ```bash
   awk '/## 16. Sınıf taraması/,0' docs/YAYIN-HAZIRLIK.md
   ```
   21 vakanın tamamı, `dosya:satır` ile orada. Bu fazın iş listesi odur.
4. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md) (`RunRecording`
   zinciri, olay yazımı) ·
   [`hafiza/http-uc-tuzaklari.md`](../../hafiza/http-uc-tuzaklari.md) (SSE ve hata gövdesi)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI-GUVENLIK.md`](../../MIMARI-GUVENLIK.md) — denetim izi ve `secret` bölümü

---

## Amaç

Ham `exception.Message` metni bugün **21 ayrı yoldan** kalıcı duruma veya dışa
açık bir yanıta yazılıyor. Provider SDK'sının, uzak bir webhook hedefinin ya da
bir OAuth sağlayıcısının ürettiği mesaj; istek ayrıntısı, iç URL, `host:port`
veya kısmi kimlik bilgisi taşıyabilir. Bu faz o metnin sızmasını kapatır ve
teşhis yeteneğini korur.

- **BL-027 · BL-037** — çalışma anı hata metni, kalıcı alana veya dışa açık
  yanıta yazılmadan önce güvenli biçime indirgenir; tam detay yalnız sunucu
  log'unda kalır ve bir korelasyon kimliğiyle bulunabilir.

Bu bir "yeni yetenek" fazı değildir. **Kapatılmamış bir kusur sınıfıdır** ve
sınıf bu repoda daha önce bir kez (`HATA-S3-006`, `IRunInputStore` yolu)
kapatılmış, ama yalnız o tek yolda kapatılmıştır.

### Bugün ne çalışmıyor — doğrulanmış kanıt

Aşağıdaki satırlar **2026-08-27'de bu oturumda tek tek okunarak** doğrulandı;
tamamı ve kalan 15 vaka [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §16'dadır.

| Kanıt | Gözlem |
|---|---|
| [`Scheduling/JobWorkerBackgroundService.cs:269`](../../../src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs) | `ErrorMessage = exception.Message` → `jobs.error_message`. **Her** job türünün (AgentRun · Eval · Webhook · Workflow) tek hunisi |
| [`Scheduling/JobWorkerBackgroundService.cs:280`](../../../src/AgentPrism.Core/Scheduling/JobWorkerBackgroundService.cs) | `ReleaseForRetryAsync(job.Id, exception.Message, …)` — aynı metin retry yolunda da yazılıyor |
| [`Recording/RunRecordingAgent.Notifications.cs:138`](../../../src/AgentPrism.Core/Recording/RunRecordingAgent.Notifications.cs) | `Error = error?.Message` → webhook payload'ı → **kiracının tanımladığı dış URL'ye POST edilir.** Ham provider metninin kutudan çıktığı tek yol |
| [`Webhooks/WebhookDeliveryJobHandler.cs:327`](../../../src/AgentPrism.Core/Webhooks/WebhookDeliveryJobHandler.cs) | `$"HTTP {statusCode}: {body}"` — **uzak hedefin ham gövdesi** kalıcılaşıyor; tamamen üçüncü taraf kontrolünde |
| [`Webhooks/WebhookDeliveryJobHandler.cs:335`](../../../src/AgentPrism.Core/Webhooks/WebhookDeliveryJobHandler.cs) | `HttpRequestException.Message` — `host:port` taşır |
| [`Endpoints/GovernanceEndpoints.cs:84`](../../../src/AgentPrism.AspNetCore/Endpoints/GovernanceEndpoints.cs) | `DescribeOAuthFailure` → `result.Error` aynen HTML sayfasına. Uç **bearer token'dan muaf** (satır 40'ın XML dokümanı bunu açıkça söylüyor; loopback+policy ile sınırlı) |
| [`McpOAuthAuthorizationCoordinator.cs:253`](../../../src/AgentPrism.Mcp/McpOAuthAuthorizationCoordinator.cs) | O `Error`'ın kaynağı: `TrySetResult((false, ex.Message))` — OAuth token-exchange bacağının ham exception'ı |

**Kapsamın kanıtı:** `ContentGuardPipeline`'ın `src/` içinde yalnız iki çağrı
yeri var — `ContentGuardingChatClient` ve `HATA-S3-006` düzeltmesi
(`RunRecordingAgent.Persistence.cs:36-46`). Kalıcılaştıran veya dışa gönderen
başka hiçbir yol bir redaksiyon adımından geçmiyor.

> Kanıtlar 2026-08-27 tarihinde doğrulandı.

---

## 119.1 — Kural zaten var; eksik olan uygulanması

🚨 **Bu faz yeni bir redaksiyon mekanizması icat etmez.** Repo doğru deseni iki
yerde zaten taşıyor ve ikisi de bu fazın temelidir:

| Var olan | Kuralı |
|---|---|
| [`ToolFailureText.cs:6-11`](../../../src/AgentPrism.Core/Tools/ToolFailureText.cs) | `AgentPrismException` → mesaj korunur (bizim, kontrollü); yabancı exception → `$"Tool failed with {Type.Name}."` |
| [`ProviderFailureNormalizer.cs`](../../../src/AgentPrism.Core/Models/ProviderFailureNormalizer.cs) | Model çağrısı sınırında yabancı hatayı sabit `UpstreamMessage`'a indirger, tam detayı `Log(...)`'a verir |

**Neden `ContentGuardPipeline` DEĞİL:** content guard'lar **opt-in** ve
varsayılan kayıtlı guard sayısı **sıfırdır**
(`Registration.Operations.cs:96-99`, denetimde BL-018 olarak kayıtlı). Ham
metni o boru hattından geçirmek varsayılan kurulumda **hiçbir şey redakte
etmez** — sızıntı açık kalır. Bu, `HATA-S3-006` düzeltmesinin de taşıdığı ve bu
fazın tekrarlamayacağı sınırdır.

Bu fazın işi: `ToolFailureText`'in kuralını **tek bir paylaşılan yardımcıya**
yükseltmek ve 21 çağrı yerine uygulamak.

## 119.2 — Güvenli metnin şekli

```mermaid
flowchart LR
    accTitle: Hata metninin iki yolu
    accDescr: Yakalanan exception iki yola ayrilir. Tam detay yalnizca sunucu loguna gider. Kalici alana ve disa acik yanita yalnizca guvenli ozet ile korelasyon kimligi yazilir.
    E["yakalanan exception"] --> D{"AgentPrismException mi?"}
    D -->|"evet - bizim, kontrollu"| K["mesaj korunur"]
    D -->|"hayir - yabanci"| T["tip adi + korelasyon kimligi"]
    E --> L["ILogger: TAM detay + ayni korelasyon kimligi"]
    K --> P["kalici alan / disa acik yanit"]
    T --> P
```

Kural üç cümledir:

1. `AgentPrismException` **bizimdir**; mesajı zaten sözleşmenin parçasıdır ve
   korunur (`ErrorType` stabil bir koddur).
2. Yabancı exception'ın mesajı **hiçbir zaman** kalıcı alana veya dışa açık
   yanıta yazılmaz. Yerine tip adı yazılır.
3. Tam detay (`exception.ToString()`, inner zinciri dahil) **yalnız**
   `ILogger`'a gider — bugün de öyle yapılıyor, bu faz onu bozmaz.

## 119.3 — Korelasyon kimliği metne gömülür, sütuna değil

Operatörün teşhis yeteneğini kaybetmemesi için güvenli metin bir korelasyon
kimliği taşır; aynı kimlik log kaydına da yazılır.

**Kimlik ayrı bir sütuna DEĞİL, metnin içine gömülür.** Gerekçe ölçüldü: bu 21
sink'in çoğu düz `string` alandır (`jobs.error_message`,
`job_items.error`, `webhook_deliveries.error`,
`eval_case_results.failure_reason`, HTTP `detail`, SSE gövdesi, MCP hata
metni). Yapısal bir alan eklemek her biri için ayrı tip değişikliği ve
**üç sağlayıcıda migration** demektir; metne gömmek tek biçimi her sink'te
çalıştırır ve **migration gerektirmez**.

Beklenen biçim (kesin metin uygulama anında sabitlenir):

```
Tool failed with HttpRequestException. (ref: 7f3a9c21)
```

`RunError` özel durumdur: `Type` alanı **zaten var**
(`RunSupportTypes.cs:67`), bu yüzden orada tip adı tekrarlanmaz — yalnız
`Message` güvenli biçime iner.

## 119.4 — 22. vakayı kapı durdurur

Yazı tek başına yetmez; bu sınıf zaten bir kez yazıyla kapatılıp tekrarladı.
Repo'da üç emsali olan **cırcır (ratchet) kapısı** deseni uygulanır:
[`AmbientWriteSiteTests`](../../../tests/AgentPrism.Core.UnitTests/Architecture/AmbientWriteSiteTests.cs) ·
[`PlaywrightLocatorTests`](../../../tests/AgentPrism.Core.UnitTests/Architecture/PlaywrightLocatorTests.cs) ·
`SourceLanguageTests`.

Kapı `src/` içinde ham exception metninin kalıcı/dışa açık bir sink'e aktığı
her yeri tarar, taban çizgisiyle karşılaştırır: **yeni giriş testi kırar,
kaybolan giriş de kırar** (taban çizgisi bayatlayamaz). Taban çizgisi yalnız
küçülür.

🚨 Tarama `ILogger` çağrılarını **hedeflemez** — repo bilinçli olarak sunucu
tarafında tam detay loglar; onları da yakalayan bir kapı gürültü üretir ve
gerçek bulguyu gizler.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions — paylaşılan kural, üçüncü tarafın da uygulaması için
public static class SafeErrorText
{
    /// <summary>Kalıcı alana veya dışa açık yanıta yazılabilir metni üretir.</summary>
    public static string ForPersistence(Exception exception, string correlationId);

    /// <summary>Korelasyon kimliği üretir; aynı değer log kaydına da yazılır.</summary>
    public static string NewCorrelationId();
}
```

Tip adı ve yerleşimi **açık sorudur** (bkz. Açık Sorular #1) — `internal`
kalıp yalnız `InternalsVisibleTo` ile paylaşılması da mümkündür ve public
yüzeyi büyütmez.

### HTTP `endpoint`'leri

Yeni uç yok. **Davranış değişikliği var:** `AgentEndpoints` (502 `detail`, SSE
`error` frame), `OpenAICompat/*` (502 gövde, SSE hata), `RunEndpoints:603`,
`ImageEndpoints:169`, MCP `CallToolResult` hata metni ve OAuth callback
sayfası artık yabancı exception metni yerine güvenli özet döndürür.

🚨 Bu, tüketicinin **gözlemlediği** bir değişikliktir. `docs-site`'ın hata
sözleşmesi bölümü aynı turda güncellenir.

### Arayüz payı

Yok — arayüz kodu değişmiyor. Arayüz bu metinleri yalnız gösterir.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/
└── Diagnostics/
    └── SafeErrorText.cs                     (yeni — 119.2 kuralı)

src/AgentPrism.Core/
├── Scheduling/JobWorkerBackgroundService.cs (269, 280)
├── Scheduling/AgentRunJobHandler.cs         (265)
├── Scheduling/RunContinuationJobHandler.cs  (200)
├── Scheduling/AgentBatchJobHandler.cs       (84)
├── Scheduling/WorkflowJobHandler.cs         (70, 87)
├── Approvals/ApprovalResumeJobHandler.cs    (174)
├── Webhooks/WebhookDeliveryJobHandler.cs    (327, 335)
├── Recording/RunRecordingAgent.Completion.cs(282)
├── Recording/RunRecordingAgent.Notifications.cs (138)
├── Recording/RunReconciliationService.cs    (328)
└── Evaluation/EvalJobHandler.cs             (351)

src/AgentPrism.Workflows/
└── Internal/WorkflowRunner.cs               (1179-1198 ToRunError)

src/AgentPrism.AspNetCore/
├── Endpoints/AgentEndpoints.cs              (1135, 1233)
├── Endpoints/RunEndpoints.cs                (603)
├── Endpoints/ImageEndpoints.cs              (169)
├── Endpoints/GovernanceEndpoints.cs         (84)
├── OpenAICompat/OpenAIChatCompletionsEndpoints.cs (163, 318)
├── OpenAICompat/OpenAIResponsesEndpoints.cs (237, 397)
└── McpServer/CatalogToolCallHandler.cs      (106)

src/AgentPrism.Mcp/
└── McpOAuthAuthorizationCoordinator.cs      (253)

tests/AgentPrism.Core.UnitTests/Architecture/
├── RawExceptionTextSiteTests.cs             (yeni — cırcır kapısı)
└── raw-exception-text-baseline.txt          (yeni — taban çizgisi)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| Provider SDK exception'ının metni `runs.error_message`'a yazılıyor | Fonksiyonel (fırlatan sahte provider + gerçek depo) | `RunErrorRedactionTests` |
| Aynı metin webhook payload'ıyla **dışarı** çıkıyor | Fonksiyonel (yakalayıcı HTTP dinleyici) | `WebhookErrorRedactionTests` |
| Uzak webhook hedefinin ham gövdesi `webhook_deliveries.error`'a yazılıyor | Fonksiyonel (hata gövdesi döndüren sahte hedef) | `WebhookErrorRedactionTests` |
| Her job türünde ayrı ayrı sızıntı (AgentRun · Eval · Webhook · Workflow) | Fonksiyonel, **tür başına** | `JobErrorRedactionTests` |
| SSE `error` frame'i ham metin taşıyor | Fonksiyonel (akış sınırı) | `SseErrorRedactionTests` |
| OpenAI-uyumlu uçlar ham metin taşıyor | Fonksiyonel (HTTP sınırı) | `OpenAICompatErrorRedactionTests` |
| MCP `CallToolResult` ham metin taşıyor | Fonksiyonel (wire sınırı) | `McpErrorRedactionTests` |
| Auth'suz OAuth callback sayfası ham metin basıyor | Fonksiyonel (HTTP, auth'suz grup) | `McpOAuthCallbackRedactionTests` |
| `AgentPrismException`'ın kendi mesajı **yanlışlıkla** redakte ediliyor (aşırı düzeltme) | Birim | `SafeErrorTextTests` |
| Korelasyon kimliği metinde var ama log'da yok (veya tersi) | Fonksiyonel (log yakalayıcı) | `SafeErrorTextCorrelationTests` |
| İptal (`OperationCanceledException`) hata sanılıp redakte ediliyor | Birim | `SafeErrorTextTests` |
| Başka kiracının hata kaydı görünür hale geliyor | Sözleşme (`TenantIsolationContract`) | mevcut suite — regresyon |
| 22. sızıntı yeri eklenmesi | Mimari cırcır | `RawExceptionTextSiteTests` |

Beş soru — cevapları yukarıdaki tabloya girdi: **iptal** (`OperationCanceledException`
redaksiyon dışıdır, `ProviderFailureNormalizer.ShouldNormalize` bunu zaten
ayırıyor) · **eşzamanlılık** (korelasyon kimliği üretimi thread-safe olmalı) ·
**boş/aşırı girdi** (`null` exception, çok uzun tip adı) · **başka kiracı**
(hata kaydı kiracıya bağlı kalır, regresyon) · **alt sistem hatası** (log
yazımı düşerse redaksiyon yine de uygulanır — güvenlik log'a bağlanmaz).

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/` ilgili aile dosyasına eklenecek case taslakları.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Geçersiz API anahtarıyla yapılandırılmış provider | `samples/AgentPrism.Api` üzerinde bir agent çalıştır | `runs.error_message` sağlayıcının ham metnini **taşımaz**; tip adı + `ref:` kimliği taşır. Sunucu log'unda aynı kimlikle tam detay bulunur |
| 2 | Kayıtlı webhook + hata döndüren dış uç | Aynı run'ı çalıştır, webhook payload'ını yakala | Payload'ın `Error` alanı güvenli metindir; ham provider metni **kutudan çıkmaz** |
| 3 | Gövdesinde iç ayrıntı döndüren sahte webhook hedefi | Teslimatı tetikle, `webhook_deliveries.error`'ı oku | Uzak gövde kalıcılaşmaz |
| 4 | OpenAI-uyumlu uç + bozuk provider | `POST /v1/chat/completions` | 502 gövdesi güvenli metindir |
| 5 | Bilinen bir `AgentPrismException` (örn. kota aşımı) | Kotayı doldur, run başlat | Mesaj **aynen korunur** — aşırı düzeltme yok |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `SafeErrorText` public mi `internal` mi? | A: `public` (üçüncü taraf `IJobHandler`/`IRunEventSink` yazarı aynı kuralı uygulayabilir) · B: `internal` + `InternalsVisibleTo` (yüzey büyümez) | **A** — üçüncü tarafın kendi handler'ında aynı sızıntıyı üretmesi bu fazın kapatmak istediği sınıfın ta kendisi; kuralı gizlemek onu tekrar ettirir. `PublicAPI.Shipped.txt` boşken maliyeti sıfır |
| 2 | Korelasyon kimliği biçimi | A: kısa hex (8 karakter) · B: tam `Guid` | **A** — hata metnine gömülüyor, okunabilirliği önemli; çakışma riski log araması için kabul edilebilir, kimlik zaman+run bağlamıyla birlikte aranır |
| 3 | `WebhookDeliveryJobHandler`'ın uzak gövdesi tamamen mi atılsın, HTTP durum kodu korunsun mu? | A: yalnız `$"HTTP {statusCode}"` · B: durum kodu + gövdenin uzunluğu | **A** — durum kodu teşhis için yeterli, gövde tamamen üçüncü taraf kontrolünde |
| 4 | Mevcut kalıcı satırlardaki ham metinler ne olacak? | A: dokunulmaz (geçmiş kayıt) · B: temizlik migration'ı | **A** — paket hiç yayınlanmadı, üretim verisi yok; temizlik migration'ı 119.3'ün "migration yok" kazancını harcar |

---

## Bitiş Ölçütleri (DoD)

- [x] §16'daki **21 vakanın tamamı** kapatıldı; her biri için `dosya:satır` ile kapanış kaydı yazıldı — ayrıca uygulama sırasında **5 ek vaka** bulunup kapatıldı (toplam 26): `EgressAddressValidator.cs:290`, `ConversationBranchService.cs:146`, `RetentionJobHandler.cs:35`, `RetentionExecutor.cs:202`, `ModelRunJudge.cs:255`
- [x] Yargı gerektiren 5 kalem (§16 sonu) ölçüldü; her biri "düzeltildi" veya "gerekçeyle kapsam dışı" olarak kaydedildi — bkz. [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §16 sonu, sonuç tablosu
- [x] `RawExceptionTextSiteTests` cırcır kapısı yeşil; taban çizgisi dosyası repo'da (`tests/AgentPrism.Core.UnitTests/Architecture/raw-exception-text-baseline.txt`, tek gerekçeli girdi: `OpenAIResponsesEndpoints.cs:HandleAsync`); gerçek bir sızıntı eklenip kapının kırdığı elle doğrulandı (MT-OBS-058)
- [x] Fırlatan sahte handler/exception ile: `jobs.error_message` ham metin taşımıyor, log tam detayı **aynı korelasyon kimliğiyle** taşıyor — `JobWorkerBackgroundServiceTests.A_throwing_handlers_own_message_never_reaches_jobs_error_message` (gerçek `InMemoryJobStore` + gerçek arka plan döngüsü + gerçek `ILoggerProvider` yakalayıcı, aynı `(ref: ...)` hem `jobs.error_message`'ta hem log kaydında bulundu)
- [x] Webhook payload'ı dış uçta yakalandı; ham provider metni içermediği doğrulandı — `WebhookDeliveryRedactionTests` (gövde + `HttpRequestException` iki ayrı vaka, gerçek `WebhookHttpClient`/`HttpMessageHandler` boru hattı)
- [x] `AgentPrismException` mesajlarının korunduğu doğrulandı (aşırı düzeltme yok) — `SafeErrorTextTests.An_AgentPrismException_own_message_is_preserved` + mevcut `RunRecordingAgentTests` (ör. `ProviderInvocationException` mesajı `"The model provider request failed."` aynen kalıyor)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban 6e82c21` tamamı ✅ (bkz. Doğrulama komutları)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. altta "Örnek uygulama koşumu"
- [x] `secret` taraması boş döndü — `kapi.py tarama` ✅ temiz (ayrıca bu turda pre-existing bir migration-manifest kaydı boşluğu bulundu ve kapatıldı, bkz. Plandan Sapmalar)
- [x] Manuel kabul case'leri `docs/manuel-test/` içine eklendi; otomatikleştirilebilenler koşuldu — `12-GOZLEMLENEBILIRLIK-MALIYET.md`'ye MT-OBS-054..058 eklendi; MT-OBS-058 gerçekten koşuldu (2026-08-27), 054-057 gerçek sağlayıcı anahtarı/webhook hedefi gerektirdiği için 👤 koşulmadı
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — bkz. Denetim Bulguları
- [x] `docs-site/` hata sözleşmesi bölümü güncellendi; `npm run check` (content+build+links+weight) temiz — `concepts/runs.md` yeni "§ error message is safe to display" bölümü, `http-api.md` ve `concepts/workflows.md`'den ona çapraz referans
- [x] [`YAYIN-HAZIRLIK.md`](../../YAYIN-HAZIRLIK.md) §4 ve §16 güncellendi (BL-027/BL-037 kapandı, K-640 eklendi)

### Doğrulama komutları

```bash
# Ham exception metni kalan var mı — cırcır kapısı
./artifacts/bin/AgentPrism.Core.UnitTests/release/AgentPrism.Core.UnitTests \
  --filter-method "*RawExceptionTextSite*"

# Kalıcı hata alanında ham metin var mı
curl -s http://localhost:5081/agentprism/api/runs/<id> | jq '.error'
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| **Aşırı düzeltme** — `AgentPrismException`'ın kendi mesajları da redakte edilir, hata mesajları anlamsızlaşır | `SafeErrorTextTests` bunu birim seviyesinde kilitler; DoD'de ayrı satır |
| Cırcır kapısının regex'i gürültü üretir (`ILogger` çağrılarını yakalar) | Kapı yalnız kalıcı/dışa açık sink'leri hedefler; `ILogger` açıkça kapsam dışı (119.4) |
| 21 vaka tek turda kapatılırken bir sink gözden kaçar | Kapı taban çizgisi bunu yakalar: kapatılmayan yer taban çizgisinde kalır ve görünür olur |
| Tüketici teşhis yeteneği kaybeder | Korelasyon kimliği + log'da tam detay (119.3); manuel case #1 bunu doğrular |
| OpenAI-uyumlu istemciler hata metnine bağımlıysa kırılır | Bu yüzey preview öncesi değiştiriliyor; `PublicAPI.Shipped.txt` boş, sonradan değiştirmek kırıcı olurdu |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

1. **Kapsam 21 vakadan 26'ya çıktı.** Uygulama sırasında, listelenen 21 + 5
   yargı kalemi dışında **5 ek sızıntı yeri** elle inceleme ve mimari cırcır
   kapısının ilk taramasıyla bulundu: `EgressAddressValidator.cs:290` (DNS/argüman
   hatası, üç egress yüzeyinin ortak noktası), `ConversationBranchService.cs:146`
   (oturum durumu geri yüklenemediğinde HTTP yanıtına sızan `JsonException`),
   ve planın zaten "yargı gerektirir" dediği `RetentionJobHandler.cs`,
   `RetentionExecutor.cs`, `ModelRunJudge.cs` (üçü de düzeltildi). Bu, fazın
   kendi 119.4 iddiasını doğruladı: "22. vakayı kapı durdurur" — kapı gerçekten
   yeni vakalar buldu.
2. **`ConversationBranchService` ve `RetentionJobHandler`'a yeni opsiyonel
   `ILogger` parametresi eklendi** (ikisi de önceden logsuzdu). İkisi de DI
   kayıt fabrikasında (`AgentPrismServiceCollectionExtensions.Registration.*`)
   güncellendi. Planda öngörülmemişti — 119.4'ün kapı bulgusuydu.
3. **`WorkflowRunner.ToRunError` instance metoda çevrildi** (`private static` →
   `private`), çünkü korelasyon kimliği üretimi ve loglama `_logger`'a erişim
   gerektiriyordu. Dört çağrı yeri de `execution.RunId`'yi parametre olarak
   geçirecek şekilde güncellendi.
4. **`WebhookDeliveryJobHandler`'ın uzak HTTP gövdesi tamamen atıldı**, yalnız
   durum kodu (`"HTTP {statusCode}"`) kalıcılaşıyor — Açık Soru #3'ün planlanan
   kararıydı, değişiklik yok.
5. **Pre-existing, konuyla ilgili bir kusur bulundu ve kapatıldı:** faz öncesi
   commit'te (`6e82c21`) eklenen üç `provider_name` case-fix migration'ı
   (`0038_provider_name_case.sql` PostgreSQL, `0025_provider_name_case.sql`
   SQLite/SQL Server — K-639'un kapanışı) `scripts/applied-migrations.json`
   manifestine hiç kaydedilmemişti; bu, `kapi.py tarama`'yı (dolayısıyla dört
   kapının tamamını) kırıyordu. Migrasyonların içeriği doğruydu, yalnız manifest
   kaydı eksikti — üçü de kendi ekleme commit'ine (`6e82c21`) sabitlenerek
   eklendi. Bu Faz 119'un kapsamı değil ama onu engelliyordu, bu yüzden bu
   turda kapatıldı.
6. **Mevcut bir işlevsel test beklenen davranışı güncellemek zorunda kaldı:**
   `ImageEndpointTests.Endpoint_translates_a_provider_failure_to_bad_gateway`
   ham sağlayıcı metninin **görünmesini** doğruluyordu (bu fazın kapattığı tam
   sızıntı deseni) — artık tip adı + `(ref:` görünmesini, ham metnin
   görünmemesini doğruluyor. `WorkflowFunctionNodeTests`'te de aynı desen.

## Bu Fazda Verilen Kararlar

| K | Karar özeti |
|---|---|
| K-640 | Yabancı bir exception'ın mesajı hiçbir zaman kalıcı alana veya dışa açık yanıta yazılmaz; kural `AgentPrism.SafeErrorText` olarak public'tir; mimari cırcır kapısı 22. sızıntı yerini yakalar. Tam gerekçe: `docs/KARARLAR.md` K-640. |

## Gerçekleşen Public API

```csharp
// AgentPrism.Abstractions, namespace AgentPrism — src/AgentPrism.Abstractions/Diagnostics/SafeErrorText.cs
public static class SafeErrorText
{
    public static string ForPersistence(Exception exception, string correlationId);
    public static string NewCorrelationId();
}
```

Plandaki taslakla birebir aynı — tek fark yok. `PublicAPI.Unshipped.txt`'e üç
satır eklendi (`AgentPrism.Abstractions`).

### Davranış sözleşmeleri (yeni/değişen)

| Davranış | Kural |
|---|---|
| `AgentPrismException.Message` | Her zaman korunur (kalıcı alan ve dışa açık yanıtta) |
| Yabancı exception mesajı | Asla kalıcı alana/dışa açık yanıta yazılmaz; `"{TypeName} failed. (ref: {id})"` |
| Korelasyon kimliği | 8 karakter hex (`Guid.NewGuid().ToString("N")[..8]`); aynı kimlik hem güvenli metinde hem `ILogger` çağrısında |
| `WebhookDeliveryJobHandler`'ın uzak gövdesi | Asla kalıcılaşmaz; yalnız `"HTTP {statusCode}"` |
| Cancellation | `SafeErrorText`'in kendi sorunu değil — her çağıran zaten `when (exception is not OperationCanceledException)` ile filtreler |

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/
└── Diagnostics/SafeErrorText.cs                     (yeni)

src/AgentPrism.Core/
├── Scheduling/JobWorkerBackgroundService.cs         (düzeltildi)
├── Scheduling/AgentRunJobHandler.cs                 (düzeltildi)
├── Scheduling/RunContinuationJobHandler.cs          (düzeltildi)
├── Scheduling/AgentBatchJobHandler.cs               (düzeltildi)
├── Scheduling/WorkflowJobHandler.cs                 (düzeltildi)
├── Approvals/ApprovalResumeJobHandler.cs            (düzeltildi)
├── Webhooks/WebhookDeliveryJobHandler.cs            (düzeltildi)
├── Recording/RunRecordingAgent.cs                   (düzeltildi — 2 catch bloğu)
├── Recording/RunRecordingAgent.Completion.cs        (düzeltildi — ToRunError)
├── Recording/RunReconciliationService.cs            (düzeltildi)
├── Evaluation/EvalJobHandler.cs                     (düzeltildi)
├── Evaluation/ModelRunJudge.cs                      (düzeltildi — 22. vaka)
├── Retention/RetentionJobHandler.cs                 (düzeltildi — 22. vaka, yeni ILogger)
├── Retention/RetentionExecutor.cs                   (düzeltildi — 22. vaka)
├── Egress/EgressAddressValidator.cs                 (düzeltildi — 22. vaka)
├── Sessions/ConversationBranchService.cs            (düzeltildi — 22. vaka, yeni ILogger)
├── Voice/VoiceConversationDriver.cs                 (düzeltildi)
├── AgentPrismServiceCollectionExtensions.Registration.Core.cs    (DI — ConversationBranchService)
└── AgentPrismServiceCollectionExtensions.Registration.Storage.cs (DI — RetentionJobHandler)

src/AgentPrism.Workflows/
└── Internal/WorkflowRunner.cs                       (düzeltildi — ToRunError instance metoda çevrildi)

src/AgentPrism.AspNetCore/
├── Endpoints/AgentEndpoints.cs                      (düzeltildi — 2 catch bloğu)
├── Endpoints/RunEndpoints.cs                        (düzeltildi)
├── Endpoints/ImageEndpoints.cs                      (düzeltildi)
├── Endpoints/WorkflowEndpoints.cs                   (düzeltildi — yargı kalemi)
├── OpenAICompat/OpenAIChatCompletionsEndpoints.cs   (düzeltildi — 2 catch bloğu)
├── OpenAICompat/OpenAIResponsesEndpoints.cs         (düzeltildi — 2 catch bloğu)
└── McpServer/CatalogToolCallHandler.cs              (düzeltildi)

src/AgentPrism.Mcp/
└── McpOAuthAuthorizationCoordinator.cs              (düzeltildi)

scripts/
└── applied-migrations.json                          (pre-existing kusur kapatıldı — Plandan Sapmalar #5)

tests/AgentPrism.Core.UnitTests/
├── Architecture/RawExceptionTextSiteTests.cs        (yeni — cırcır kapısı)
├── Architecture/raw-exception-text-baseline.txt     (yeni — taban çizgisi)
├── Architecture/public-surface-baseline.txt          (düzeltildi — +1 public tip)
├── Diagnostics/SafeErrorTextTests.cs                (yeni)
├── Webhooks/WebhookDeliveryRedactionTests.cs        (yeni)
├── Scheduling/JobWorkerBackgroundServiceTests.cs    (genişletildi — 1 yeni test)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── ImageEndpointTests.cs                            (güncellendi — beklenen davranış değişti)

tests/AgentPrism.Workflows.UnitTests/
└── WorkflowFunctionNodeTests.cs                     (güncellendi — beklenen davranış değişti)

docs-site/src/content/docs/
├── concepts/runs.md                                 (yeni bölüm)
├── concepts/workflows.md                            (çapraz referans)
└── http-api.md                                      (çapraz referans)

docs/manuel-test/12-GOZLEMLENEBILIRLIK-MALIYET.md    (MT-OBS-054..058 eklendi)
```

### Testler

| Sınıf | Ne doğruluyor | Sayı |
|---|---|---|
| `SafeErrorTextTests` | `AgentPrismException` korunur, yabancı exception redakte edilir, korelasyon kimliği gömülür, `NewCorrelationId` benzersiz | 6 |
| `RawExceptionTextSiteTests` | Cırcır kapısı: taban çizgisiyle eşleşme + tarayıcının kendi regresyon testleri | 3 |
| `WebhookDeliveryRedactionTests` | Uzak gövde ve `HttpRequestException` mesajı kalıcılaşmaz | 2 |
| `JobWorkerBackgroundServiceTests` (yeni test) | `jobs.error_message` redakte edilir, aynı korelasyon kimliğiyle log'da tam detay bulunur | 1 |
| `WorkflowFunctionNodeTests` (güncellenen test) | `RunError.Message` gerçek bir workflow çalıştırmasında redakte edilir | 1 |
| `ImageEndpointTests` (güncellenen test) | HTTP `ProblemDetails.detail` redakte edilir | 1 |

Toplam yeni/değiştirilen test: 14. Tam regresyon: `AgentPrism.Core.UnitTests`
2068/2068, `AgentPrism.AspNetCore.FunctionalTests` 691/691,
`AgentPrism.Workflows.UnitTests` 102/102, `AgentPrism.Mcp.UnitTests` 32/32,
`AgentPrism.Voice.UnitTests` 43/43, `AgentPrism.Cli.FunctionalTests` 33/33,
`AgentPrism.Sqlite.IntegrationTests` 607/607.

### Örnek uygulama koşumu (2026-08-27)

`samples/AgentPrism.Api` gerçek yapılandırılmış (SQL kalıcılık, gerçek
Anthropic/OpenRouter anahtarları) bir ortamda çalıştırıldı:

```
$ curl -s -X POST http://localhost:5081/agentprism/api/agents/claude-support/run \
    -H "content-type: application/json" -H "Authorization: Bearer manuel-test-token-2026" \
    -d '{"message":"hello"}'
# HTTP 200, SSE akışı, gerçek Claude yanıtı, run tamamlandı (bkz. yukarı log)

$ curl -s "http://localhost:5081/agentprism/api/runs?take=3" -H "$APB"
# Normal kayıt davranışı bozulmadı (error: null, usage/cost dolu)
```

Bu, `RetentionJobHandler`/`ConversationBranchService`'e eklenen yeni `ILogger`
parametrelerinin ve `WorkflowRunner.ToRunError`'un instance metoda çevrilmesinin
**gerçek DI çözümlemesini kırmadığını** kanıtlar — sahte nesnelerin
göremeyeceği bir regresyon sınıfı (K-166/K-167 emsali). Yeni `SafeErrorText`
kod yolunun kendisi (yabancı exception dalı) bu ortamda tüm sağlayıcı
anahtarları geçerli olduğu için tetiklenemedi; o davranış
`JobWorkerBackgroundServiceTests`/`WebhookDeliveryRedactionTests`/
`WorkflowFunctionNodeTests`'te gerçek (sahte olmayan) nesnelerle kanıtlanmıştır.
MT-OBS-058 (mimari kapı) de gerçekten koşuldu — bkz. yukarı.

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (taban `6e82c21`). Özet: 🔴
yok, 4 🟡, 0 🟢. Tam çözüm derlendi, `RawExceptionTextSiteTests` yeşildi,
`AgentPrism.Core.UnitTests` (2068/2068) ve `AgentPrism.AspNetCore.FunctionalTests`
(691/691) tam koşumu geçti — regresyon yok.

| # | Bulgu | Seviye | Sonuç |
|---|---|---|---|
| 1 | Korelasyon kimliğinin log ile kalıcı metinde **aynı** olduğunu kanıtlayan test yoktu | 🟡 | **Düzeltildi** — `JobWorkerBackgroundServiceTests`'e gerçek `ILoggerProvider` yakalayıcı eklendi, aynı `(ref: ...)` hem `jobs.error_message`'ta hem log formatlı mesajında bulunuyor |
| 2 | Planlanan 9 fonksiyonel test sınıfından bir kısmı (job-türü-başına ayrı sınıf, MCP OAuth callback) hiç yazılmamıştı | 🟡 | **Gerekçelendi** — `JobWorkerBackgroundService` TÜM job türlerinin tek hunisidir (119 dokümanının kendi tespiti); tek noktada kanıtlamak her türü ayrı ayrı kanıtlamakla eşdeğerdir. `ProviderOutageErrorHandlingTests` (pre-existing) SSE/Responses/ChatCompletions/MCP yüzeylerinin tamamını gerçek HTTP üzerinden zaten kapsıyordu. MCP OAuth callback'i (`McpOAuthAuthorizationCoordinator`) tek bir küçük redaksiyon noktasıdır ve `SafeErrorTextTests`'in genel doğruluğuna dayanır; ayrı bir fixture (OAuth akışı sahteleme) bu fazın kapsamına orantısız kalırdı |
| 3 | `EgressAddressValidator.cs:302`'nin DNS hatası hiçbir yere (log dahil) korelasyon kimliğiyle yazılmıyor — fazın diğer tüm sitelerinden farklı | 🟡 | **Gerekçelendi** — paylaşılan statik sınıf üç farklı yüzeyden (webhook/MCP/model egress) çağrılıyor; `ILogger` eklemek üçünün de imzasını büyütür. `SocketException`/`ArgumentException` mesajı yalnız çağıranın KENDİ verdiği host adını anlatır (host:port/credential taşımaz) — düşük risk, tip adı yine de tutuluyor. Bkz. `YAYIN-HAZIRLIK.md` §16 sonu |
| 4 | `RawExceptionTextSiteTests` yalnız `catch (Exception` (isimsiz) şeklini tarar; isimli (`catch (HttpRequestException` gibi) bloklar kapsam dışı | 🟡 | **Gerekçelendi** — bilinçli bir tasarım sınırı (test dosyasının kendi `<remarks>`'ı bunu açıklıyor); bu fazda tüm isimli bloklar ELLE incelendi. Genişletme K-640'ın "yeniden açılma koşulu" sütununda kayıtlı, ayrı bir kalem |

## Sonraki Faza Devir Notu

**Faz 120 ([`120-JOB-SOZLESMESI-AT-LEAST-ONCE.md`](../../120-JOB-SOZLESMESI-AT-LEAST-ONCE.md))
bu fazdan sonra gelir ve AYNI dosyalara dokunur** — `IJobHandler.cs`,
`AgentBatchJobHandler.cs`, `WorkflowJobHandler.cs`, `EvalJobHandler.cs`. Faz
120'ye başlarken:

- 🚨 **`SafeErrorText` deseni zaten yerleşik.** Yeni bir `IJobHandler` hata
  yolu eklerken (at-least-once sözleşmesi yazılırken) ham `exception.Message`
  YAZMA — `SafeErrorText.ForPersistence(exception, SafeErrorText.NewCorrelationId())`
  kullan ve aynı kimlikle `ILogger`'a logla. `RawExceptionTextSiteTests` yeni
  bir `catch (Exception` + `.Message` çiftini otomatik yakalar.
- `RetentionJobHandler` ve `ConversationBranchService`'in artık opsiyonel
  `ILogger<T>` parametresi var (bu fazda eklendi) — Faz 120 bu imzaları
  değiştirirken göz önünde bulundurmalı.
- `WorkflowRunner.ToRunError` artık `private static` değil `private` (instance)
  — bir `RunId` parametresi alıyor. Faz 120 bu metoda dokunursa imza budur.
- Mimari cırcır kapısının taban çizgisi (`raw-exception-text-baseline.txt`)
  Faz 120'de yeni bir `catch (Exception` + `.Message` sitesi açılırsa
  KIRILACAKTIR — bu beklenen davranıştır, `AGENTPRISM_RAW_EXCEPTION_TEXT_REFRESH=1`
  ile kapatma, önce `SafeErrorText` ile düzelt.
- BL-041'in "gerçek kusur" tanımı zaten dar: `IJobHandler`'ın XML dokümanına
  at-least-once notu + `JobHandlerContract` + `JobLeaseExpiryTests`. Faz 119
  bu kapsamı GENİŞLETMEDİ.
