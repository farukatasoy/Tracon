# Faz 112 — Replay'in İstemci Tool Sözleşmesi

> **Durum:** ✅ Tamamlandı (2026-08-26)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-109**
> **Önkoşul:** Faz 61 (istemci tool'ları, K-435) ve Faz 47 (replay, K-315) — ikisi de arşivde; yalnız aşağıdaki grep'lerle okunur
> **Paketler:** `AgentPrism.Core` (`Replay/`), `AgentPrism.AspNetCore` (`Endpoints/RunEndpoints.cs`)
> **Yeni paket:** Yok · **Migration:** Yok
> **Public API:** Büyüyor — `RunReplayOutcome`'a bir üye. Faz 7'den önce ucuz: `wc -l src/*/PublicAPI.Shipped.txt` toplamı **17** satır ve her dosya yalnız başlık taşıyor (K-603)
> **Tüketici yüzeyi:** `docs-site/src/content/docs/guides/client-side-tools.md`, `capabilities.md`, `troubleshooting.md`
> · sevk edilen: `RunReplayOutcome` XML dokümanı, replay ucunun `.WithDescription` metni (`api/` ve `http-api/` üretilir)
> **Manuel test alanı:** `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-585\|K-586\|K-603" docs/KARARLAR.md
   grep -n "K-315\|K-435" docs/arsiv/KARARLAR-INDEKS-ARSIV.md
   ```
   **K-315** (replay OTURUMSUZDUR — bu fazın tasarımını belirleyen kısıt),
   **K-435** (`IToolRegistry` `AIFunction`'a değil `AIFunctionDeclaration`'a genişledi — istemci tool'unun gövdesiz olmasının kaynağı),
   **K-585** (`ReplayToolMode`'a dördüncü üye AÇILMADI — bu fazda da açılmaz),
   **K-586** (skill/alt-agent taşıyan agent devam ettirilemez — aynı sınıf sınır),
   **K-603** (`PublicAPI.Shipped.txt` boş; yüzey büyütmek bugün ucuz).
3. Alan hafızası (bu faz iki alana dokunuyor):
   [`hafiza/cekirdek-calistirma.md`](../../hafiza/cekirdek-calistirma.md) (`RunRecording` zinciri, tool kaydı) ·
   [`hafiza/http-uc-tuzaklari.md`](../../hafiza/http-uc-tuzaklari.md) (problem details, durum kodu eşlemesi)
4. Gerektiğinde, tamamı değil ilgili bölümü: [`MIMARI.md`](../../MIMARI.md) — çalıştırma yolu

---

## Amaç

Faz 61 `AddClientTool` ile **sevk edilmiş** bir yetenek getirdi: gövdesi
tarayıcıda çalışan, sunucuda yalnız beyan olarak duran bir tool. Faz 47 replay'i
getirdi ve "her run yeniden oynatılabilir" beklentisini kurdu. İkisi hiç
karşılaşmadı. Bugün istemci tool'u taşıyan bir agent'ın run'ı replay edildiğinde
istek `200 OK` döner, fakat run **sessizce yarım biter** — çünkü çağrıyı
cevaplayacak hiçbir istemci yoktur.

Bu faz o boşluğu bir **açık sözleşme** ile kapatır: replay bu run türünü
çalıştırmaya kalkışmaz, erken ve anlaşılır biçimde reddeder.

- **F-109** — replay ile istemci tool'u arasındaki sözleşmeyi seç ve HTTP,
  replay kaydı ve site anlatısı boyunca tutarlı kıl.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`AgentDefinitionCompiler.cs:465-478`](../../../src/AgentPrism.Core/Compilation/AgentDefinitionCompiler.cs) | `toolTransform` yalnız `tools[index] is AIFunction` olanlara uygulanır. İstemci tool'u `AIFunctionDeclaration`'dır; **bilerek** atlanır — sarmalanacak sunucu gövdesi yoktur |
| [`RunReplayService.cs:137-140`](../../../src/AgentPrism.Core/Replay/RunReplayService.cs) | `ReplayTools` modunda `playback.Wrap` `toolTransform` olarak geçirilir. İstemci tool'u sarmalanmadığı için oynatma defterine **hiç bakılmaz** |
| [`RunReplayService.cs:159-163`](../../../src/AgentPrism.Core/Replay/RunReplayService.cs) | `ReplayMismatchGuard` yalnız `playback` çağrıldığında tetiklenir. İstemci tool'unda `playback` hiç çağrılmaz → **muhafız da susar** |
| `grep -rn "ClientTool" src/AgentPrism.Core/Replay/` | **Sıfır isabet.** Replay yolunda istemci tool'u için ne kontrol, ne ret, ne uyarı var |
| [`RunRecordingAgent.Completion.cs:57`](../../../src/AgentPrism.Core/Recording/RunRecordingAgent.Completion.cs) | Cevaplanmamış çağrı `DrainUnfinished("The run ended before the tool result arrived.")` ile kapatılır. Replay run'ı **`Completed` biter**, bir tool kaydı da hatalı görünür |
| [`RunReplayResponse`](../../../src/AgentPrism.AspNetCore/Contracts/ReplayContracts.cs) | Yanıtta bekleyen tool çağrısı taşıyan **hiçbir alan yok**. Çağıran `200 OK` ve boş `Output` alır; nedenini öğrenemez |

> Kanıtlar 2026-08-26 tarihinde doğrulandı.

### 🚨 Aday metnindeki iddia ölçümle daraldı

`ADAYLAR.md`, F-109'un kapsamını *"kaydedilmiş istemci tool sonucunu tekrar
oynatmak **veya** bu run türünü reddetmek"* diye iki seçenekli yazmıştı.
Ölçüm birinci seçeneği **bugünkü kayıtla imkânsız** buldu:

| Ölçüm | Sonuç |
|---|---|
| [`ToolInvocationTracker.cs:161-190`](../../../src/AgentPrism.Core/Recording/ToolInvocationTracker.cs) `DrainUnfinished` | İstemci tool'u için yazılan kayıt `Result = null`, `Error = "The run ended before the tool result arrived."` taşır. **Sonuç metni kayıtta yoktur** |
| [`RunRecordingAgent.Persistence.cs:188-194`](../../../src/AgentPrism.Core/Recording/RunRecordingAgent.Persistence.cs) | `OnCall`/`OnResult` yalnız **yanıt** içeriği üzerinde döner. İstemcinin geri gönderdiği sonuç bir sonraki turun **girdisidir**; hiçbir `ToolInvocationRecord` üretmez |
| [`ClientToolResultResolver.cs:110-145`](../../../src/AgentPrism.AspNetCore/Internal/ClientToolResultResolver.cs) | Sonuç `ChatMessage(ChatRole.Tool, …)` olarak **oturum geçmişine** yazılır — tek yaşadığı yer orasıdır |
| K-315 | Replay **oturumsuzdur**; oturum geçmişi replay'e taşınmaz |

∴ Oynatma seçeneği yeni bir kalıcılık yolu (istemci sonucunu tool defterine
yazmak) açmadan mümkün değildir. O ayrı ve daha pahalı bir iştir. **Bu faz ret
sözleşmesini kurar.**

---

## 112.1 — Bugünkü akış ve deliğin yeri

```mermaid
flowchart TD
    A["POST /api/runs/{id}/replay<br/>toolMode: ReplayTools"] --> B["RunReplayService.PrepareAsync"]
    B --> C{"Onay isteyen tool var mı?"}
    C -->|"evet + LiveTools"| D["409 ApprovalRequired"]
    C -->|hayır| E["CompileAsync(toolTransform: playback.Wrap)"]
    E --> F{"tools[i] is AIFunction?"}
    F -->|evet| G["playback sarmalar"]
    F -->|"hayır — istemci tool'u"| H["ATLANIR<br/>çıplak declaration kalır"]
    G --> I["Run başlar"]
    H --> I
    I --> J{"Model istemci tool'unu çağırdı mı?"}
    J -->|hayır| K["Normal yanıt · 200 OK"]
    J -->|evet| L["Kimse cevaplamaz"]
    L --> M["DrainUnfinished · run 'Completed'<br/>Output boş · 200 OK"]

    style H fill:#fdd,stroke:#c00
    style L fill:#fdd,stroke:#c00
    style M fill:#fdd,stroke:#c00
```

Kırmızı yol bir hata **döndürmez**. Tüketici için bu, sessizce yanlış cevaptır.

## 112.2 — Seçilen sözleşme: hazırlıkta erken ret

**Karar (2026-08-26, kullanıcı):** Ret, run hiç başlamadan
`RunReplayService.PrepareAsync` içinde, agent **tanımının** tool listesi
taranarak verilir.

Gerekçe ve kabul edilen bedel:

| Lehine | Aleyhine (bilerek kabul edildi) |
|---|---|
| Run hiç başlamaz; token harcanmaz | Agent istemci tool'u **taşıyor** ama o run'da **çağırmamış** olsa bile replay reddedilir |
| Tek yer, tek kural — `ApprovalRequired` deseninin birebir aynısı | Bazı meşru replay istekleri kapanır |
| Kaynak run'ın tool kaydına bakan alternatif deliği **kapatmıyordu**: yeni sürüm tool'u çağırabilir ve run yine sessizce yarım biterdi | — |

Aleyhine satırı bir kusur değil, **bilinçli muhafazakârlıktır**. Aynı seçim
K-586'da (skill/alt-agent taşıyan agent devam ettirilemez) zaten verilmiştir;
bu faz o çizgiyi sürdürür, yeni bir felsefe açmaz.

### Hangi mod reddedilir

**Üç mod da reddedilir.** İstemci tool'u hiçbir modda çalışamaz:

| Mod | Bugünkü davranış | Bu fazdan sonra |
|---|---|---|
| `ReplayTools` | Sarmalanmaz, sessizce yarım biter | Reddedilir |
| `NoTools` | Tool hiç bağlanmaz — **zaten güvenli** | Reddedilir mi? → **Açık Soru 1** |
| `LiveTools` | Sunucuda gövde yok, çalıştırılamaz | Reddedilir |

## 112.3 — `FindClientTool` — `FindApprovalTool`'un aynadaki eşi

Mevcut yardımcı ([`RunReplayService.cs:288-305`](../../../src/AgentPrism.Core/Replay/RunReplayService.cs))
birebir kopyalanır; tek fark yüklemdir:

| Mevcut | Yeni |
|---|---|
| `descriptor.RequiresApproval` | `descriptor.RunsOnClient` |

`ToolDescriptor.RunsOnClient` ([`ToolDescriptor.cs:50`](../../../src/AgentPrism.Abstractions/Tools/ToolDescriptor.cs))
zaten vardır ve `ToolRegistry.cs:166` onu `registration.Function is not AIFunction`
ile doldurur. **Yeni bir tespit mekanizması yazılmaz.**

## 112.4 — İki hazırlık yolu ve ikisinin de kapatılması

`PrepareAsync` iki koldan ilerler; onay kontrolü **ikisinde de** vardır ve
istemci tool kontrolü de ikisine birden girer:

| Kol | Konum | Durum |
|---|---|---|
| Kalıcı tanım | `RunReplayService.cs:126` | `definition.ToolNames` taranır |
| Katalog / kod agent'ı | `RunReplayService.cs:230` | `descriptor.ToolNames` taranır |

🚨 **İkinci kol Faz 47'de yazılmış ve kolayca gözden kaçar.** Yalnız birincisi
kapatılırsa kod tanımlı agent'lar delikte kalır. `faz-uygulama` Adım 4'ün
imza–gövde kuralı burada geçerlidir.

## 112.5 — Neden `ReplayToolMode`'a dördüncü üye eklenmiyor

K-585 bu tartışmayı kapattı: `ReplayToolMode` bir **replay isteğinin**
sözleşmesidir. "İstemci tool'u var" bir istek değil, agent'ın **şeklidir**.
Yeni bilgi `RunReplayOutcome`'a girer — o zaten "bu agent bu koşullarda
replay edilemez" sonuçlarının yaşadığı yerdir.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Core — Replay/RunReplayService.cs
public enum RunReplayOutcome
{
    Ready = 0,
    RunNotFound = 1,
    InputNotFound = 2,
    NotSupported = 3,
    ApprovalRequired = 4,

    /// <summary>
    /// The agent carries a client-side tool (<c>AddClientTool</c>). The tool
    /// has no server-side body and no recorded result, so no replay mode can
    /// answer a call to it.
    /// </summary>
    ClientToolNotReplayable = 5,
}
```

Yeni tip, yeni arayüz veya yeni ayar **yoktur**. Yüzey tek bir enum üyesi
kadar büyür.

### HTTP `endpoint`'leri

Yeni uç yok. Mevcut ucun bir sonucu eklenir:

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/runs/{runId:guid}/replay` | Operator (`LiveTools` için Admin) | İstemci tool'u taşıyan agent için **`409 Conflict`** döner |

`409` seçimi `ApprovalRequired`'ın eşidir ([`RunEndpoints.cs:509-512`](../../../src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs)):
istek biçimsel olarak geçerlidir, agent'ın şekli çakışır.

🚨 **`switch` ifadesinin `_ =>` kolu bu değişikliği yutar.** Yeni üye için arm
yazılmazsa istek sessizce `400 "Replay not supported"` döner — makul görünen
**yanlış** cevap. Kapanışta bu satır elle doğrulanır.

### Arayüz payı

**Yok.** Sunucu yanıtı çevrilmez (K-232); replay ekranı hata metnini olduğu
gibi gösterir. `locales/en.ts` ve `tr.ts` değişmez. Bugünkü taban çizgisi
ölçüldü: `index-DESnx11L.js.br` = 148 928 B.

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Core/Replay/
└── RunReplayService.cs          (değişir: FindClientTool + iki kol + enum üyesi)

src/AgentPrism.AspNetCore/Endpoints/
└── RunEndpoints.cs              (değişir: switch arm + .WithDescription metni)

tests/AgentPrism.Core.UnitTests/Replay/
└── ReplayClientToolContractTests.cs        (yeni)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── ReplayClientToolEndpointTests.cs        (yeni)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| İstemci tool'u taşıyan agent yine replay edilir ve run sessizce yarım biter | Fonksiyonel (HTTP sınırı) | `ReplayClientToolEndpointTests` |
| Yalnız kalıcı tanım kolu kapatılır; **kod tanımlı** agent delikte kalır | Fonksiyonel | `ReplayClientToolEndpointTests` (katalog kolu ayrı case) |
| `switch` `_ =>` koluna düşer; `409` yerine `400` döner | Fonksiyonel | `ReplayClientToolEndpointTests` (durum kodu iddiası) |
| Sunucu tool'u taşıyan agent yanlışlıkla reddedilir (yanlış pozitif) | Birim | `ReplayClientToolContractTests` |
| Hiç tool taşımayan agent reddedilir | Birim | `ReplayClientToolContractTests` |
| Başka kiracının run'ı üzerinden istemci tool'u bilgisi sızar | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| Replay isteği iptal edilirse ret yolu `OperationCanceledException` yutar | Birim | `ReplayClientToolContractTests` |
| MCP kaynaklı declaration-only tool `RunsOnClient` işaretlenir ve yanlışlıkla reddedilir | Birim | `ReplayClientToolContractTests` — 🚨 `McpTenantTools.cs:137` de `is not AIFunction` kullanıyor; **ölçülmeli** |

Beş sorunun cevabı: **iptal** → ret yolu `async` değil, token yalnız depo
çağrısına geçer · **eşzamanlılık** → hazırlık salt okunurdur, paylaşılan durum
yok · **boş/aşırı girdi** → `ToolNames.Count == 0` erken çıkar · **başka
kiracı** → `PrepareAsync` zaten kiracı süzer (`RunNotFound`), sözleşme testi
bunu sabitler · **alt sistem hatası** → `_tools.List()` boş dönerse ret
tetiklenmez ve eski davranışa düşer; bu **kabul edilir**, çünkü tool kaydı
yoksa agent zaten derlenemez.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` içine
> eklenecek case'lerin taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `AddClientTool` ile bir tool kayıtlı, agent onu taşıyor, tamamlanmış bir run var | `POST /api/runs/{id}/replay` `{"toolMode":"ReplayTools"}` | `409`; `detail` tool adını ve nedeni yazar |
| 2 | Aynı agent | `{"toolMode":"LiveTools"}` (Admin) | `409`, aynı gerekçe |
| 3 | Yalnız sunucu tool'u taşıyan agent | `{"toolMode":"ReplayTools"}` | `200`; replay eskisi gibi çalışır — **gerileme yok** |
| 4 | Kod tanımlı (katalog) agent, istemci tool'u taşıyor | Aynı istek | `409` — katalog kolu da kapalı |
| 5 | Hiç tool taşımayan agent | Aynı istek | `200` |
| 6 | Arayüz: run detayında "Replay" düğmesi | İstemci tool'lu run'da tıkla | Sunucunun `detail` metni hata olarak gösterilir; ekran boş kalmaz 👤 insan gerekir |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | `NoTools` modu da reddedilsin mi? O modda tool hiç bağlanmaz, yani teknik olarak güvenlidir | A: Üç modu da reddet · B: Yalnız `ReplayTools`/`LiveTools`'u reddet, `NoTools`'a izin ver | **A.** `NoTools` replay'i tool'suz bir dünyayı ölçer; istemci tool'u davranışın **belirleyici** parçasıysa sonuç kaynak run'la kıyaslanabilir değildir. Tek kural açıklaması kolaydır; iki kural "hangi modda ne olur" tablosu ister |
| 2 | MCP kaynaklı tool `registration.Function is not AIFunction` ile `RunsOnClient = true` alabiliyor mu? (`McpTenantTools.cs:137`) | — | **Ölçülmeli.** `McpClientTool : AIFunction` olduğu `McpTenantTools.cs:74` yorumunda yazıyor, yani beklenen cevap "hayır". İddia doğrulanmadan koda güvenilmez |
| 3 | Ret metni tool adını yazsın mı? | A: Yazsın · B: Yalnız "bir istemci tool'u" desin | **A.** `ApprovalRequired` de adı yazıyor; tool adı zaten agent tanımında görünür, yeni bilgi sızdırmaz |

---

## Bitiş Ölçütleri (DoD)

- [x] İstemci tool'u taşıyan bir agent'ın run'ına `POST /api/runs/{id}/replay` → `409`, `detail` tool adını içerir (üç `ToolMode` için de) — bkz. "Doğrulama komutları" altındaki gerçek çıktı
- [x] Yalnız sunucu tool'u taşıyan agent için replay eskisi gibi `200` döner — gerileme yok
- [x] Kod tanımlı (katalog) agent kolu da `409` döner
- [x] `RunReplayOutcome.ClientToolNotReplayable` için `RunEndpoints` `switch`'inde **açık** bir arm var; `_ =>` koluna düşmüyor
- [x] Dört doğrulama kapısı sıfır uyarı verir (`python3 scripts/kapi.py kapanis --taban 0c4d931`)
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — bkz. altında
- [x] `secret` taraması boş döndü (`python3 scripts/kapi.py tarama`)
- [x] Manuel kabul case'leri `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` içine eklendi (MT-IST-014…016); otomatikleştirilebilenler (014, 015) koşuldu, çıktı aşağıda; 016 👤 insan gerekir
- [x] `faz-denetim` koşuldu (taze bağlamlı ayrı agent); 🔴 bulgu kalmadı, iki 🟡 bulgu kapandı — bkz. "Denetim Bulguları"
- [x] `docs-site/` güncellendi (`guides/client-side-tools.md` replay sınırını yazar, `capabilities.md` satırı düzeltilir, `troubleshooting.md`'ye eşdeğer girdi eklendi — denetimde bulundu); `npm run build` + `check-links.mjs` temiz (8 önceden var olan kırık bağlantı taban commit'te de kırık — bu fazın kapsamı dışında)
- [x] OpenAPI belgesi yeniden üretildi ve TS şeması + NSwag istemcisi + `@agentprism/client` `dist`'i zinciri koşuldu (K-627'nin dört adımlı zinciri) — dördü de tek satırlık tutarlı diff üretti

### Doğrulama komutları — gerçek çıktı (`samples/AgentPrism.Api`, echo sağlayıcı, 2026-08-26)

```bash
# Yalnız read_shopping_cart'ı taşıyan persistent (DB) agent oluşturuldu:
curl -s -X POST "$APU/api/agents" -H "content-type: application/json" \
  -d '{"name":"manuel-ist-replay","model":{"provider":"echo","model":"echo-1"},"toolNames":["read_shopping_cart"]}'
# -> HTTP 201

# Bir run tamamlandı, girdisi kaydedildi:
curl -s -X POST "$APU/api/agents/manuel-ist-replay/run" -H "content-type: application/json" \
  -H 'Idempotency-Key: mt-ist-014-demo' -d '{"message":"merhaba"}'
# -> {"runId":"01a03ee6-6b93-7be1-90fe-d149a186a3fd","response":{"messages":[{"authorName":"manuel-ist-replay","role":"assistant","contents":[{"$type":"text","text":"Echo: merhaba"}]}], ...}}

# Üç mod da 409:
for MODE in ReplayTools LiveTools NoTools; do
  curl -si -X POST "$APU/api/runs/01a03ee6-6b93-7be1-90fe-d149a186a3fd/replay" \
    -H "content-type: application/json" -d "{\"toolMode\":\"$MODE\"}"
done
# -> üçü de: HTTP 409, detail: "Agent 'manuel-ist-replay' carries the client-side tool
#    'read_shopping_cart' (AddClientTool). Its body runs in the caller's browser, not on
#    the server, and no call to it was recorded — there is no result to play back and no
#    client waiting to answer it live. This run cannot be replayed in any tool mode."

# Yalnız sunucu tool'u taşıyan agent'ta (cached-support) gerileme yok:
curl -si -X POST "$APU/api/runs/01a03ee6-c0eb-788a-bb58-c9300e76aea9/replay" \
  -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'
# -> HTTP 200, {"runId":"...","output":"Echo: hello","compareLocation":"..."}

# Kod tanımlı (katalog) support agent'ı (cancel_order onay gerektirir + read_shopping_cart
# istemci tool'u) — hangi guard önce tetiklenirse tetiklensin, ikisi de reddeder:
curl -si -X POST "$APU/api/runs/01a03ee6-9ce1-7f58-a1c6-5aa4e1e5d8e8/replay" \
  -H "content-type: application/json" -d '{"toolMode":"LiveTools"}'
# -> HTTP 409, detail: "Agent 'support' carries the tool 'cancel_order', which requires
#    approval, and cannot run in 'LiveTools' mode. ... This agent has no persistent
#    definition (code-defined or deleted), so 'ReplayTools'/'NoTools' are not available
#    either — this run cannot be replayed."
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| Yalnız bir hazırlık kolu kapatılır; kod tanımlı agent delikte kalır | Her iki kol için ayrı fonksiyonel test; DoD'de ayrı satır |
| `switch`'in `_ =>` kolu yeni üyeyi yutar ve `400` döner | Test durum kodunu iddia eder; DoD'de ayrı satır |
| Ret fazla geniş olur ve tool'u çağırmayan meşru replay'ler kapanır | Bilinçli kabul edildi (112.2); site sayfası bu sınırı **açıkça** yazar |
| MCP declaration'ları yanlışlıkla istemci tool'u sayılır | Açık Soru 2 ölçülmeden kod yazılmaz |
| Üretilmiş yüzeyler (OpenAPI/TS/NSwag/`dist`) güncellenmez ve frontend `tsc` kırmızı kalır | K-627'nin dört adımlı zinciri DoD'de |

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

- **Planlanan `tests/AgentPrism.Core.UnitTests/Replay/ReplayClientToolContractTests.cs` yazılmadı.** Plan bu davranışları "Birim" seviyesinde öngörmüştü (yanlış-pozitif, tool'suz agent, iptal, MCP false-positive). Ölçüm: `RunReplayService` `internal`'dır ve `PrepareAsync` yedi bağımlılığı (`IRunStore`, `IRunInputStore`, `IAgentDefinitionStore`, `IAgentCatalog`, `AgentDefinitionCompiler`, `IToolRegistry`, `ITenantContext`) birlikte kullanır — gerçek bir "birim" testi bu bağımlılıkların tamamını (mocksuz repo, gerçek `AgentDefinitionCompiler`) yeniden kurmayı gerektirir, ki bu `.agents/ortak/test-seviyeleri.md`'nin "DI kapsamı sınırını geçen davranış fonksiyonel seviyede kanıtlanır" kuralına göre zaten fonksiyonel testin işidir. Dört davranıştan ikisi (**sunucu tool'u yanlış-pozitif**, **tool'suz agent**) `ReplayClientToolEndpointTests` içinde zaten dolaylı kanıtlanıyordu. Kalan ikisi denetimde 🟡 bulgu olarak işaretlendi ve kapatıldı: **MCP false-positive** artık `McpTenantToolsTests.An_mcp_tool_is_never_marked_RunsOnClient` ile kilitli (gerçek DI olmadan, `McpTenantTools.Create`'i doğrudan çağıran ucuz bir birim testi — planın öngördüğü yerde değil ama planın istediği güvenceyi veriyor). **İptal yolu** için ayrı bir test yazılmadı: `FindClientTool`/`FindClientTool` çağrısı zaten alınmış verinin (`definition.ToolNames`/`descriptor.ToolNames`) üzerinde çalışan SENKRON bir döngüdür, iki mevcut `await` noktası arasına eklendi, yeni bir `try`/`catch` veya yeni bir asenkron sınır açmadı — iptal davranışı `PrepareAsync`'in zaten var olan, testlerle kanıtlı akışından değişmedi.
- **Manuel kabul case'leri `support` agent'ını (katalog kolu) tam izole test edemedi.** Plan case 4'ün ("Kod tanımlı agent, istemci tool'u taşıyor → 409, tool adı görünür") `read_shopping_cart`'ın adını izole göstermesini varsaymıştı. Ölçüm: örnek uygulamanın TEK istemci-tool'lu kod agent'ı (`support`) AYNI ZAMANDA `cancel_order`'ı (onay gerektirir) taşıyor; `PrepareFromCatalogAsync` onay kontrolünü istemci-tool kontrolünden ÖNCE çalıştırıyor, yani gerçek koşumda `409`'un `detail`'i `cancel_order`'ı adlandırıyor, `read_shopping_cart`'ı değil. MT-IST-015 bunu açıkça yazar; izole gösterim otomatik testte (`ReplayClientToolEndpointTests.A_code_defined_agent_carrying_a_client_side_tool_is_also_rejected`, yalnız istemci tool'u taşıyan ayrı bir agent ile) kanıtlanır. Örnek uygulamaya yeni bir "yalnız istemci tool'lu kod agent'ı" eklenmedi — kapsam dışı, gerekmiyordu.
- **`troubleshooting.md` ilk taslakta atlandı, denetimde bulundu ve eklendi.** Fazın kendi "Tüketici yüzeyi" satırı bu sayfayı listeliyordu ama DoD'nin site maddesi yalnız `client-side-tools.md`/`capabilities.md`'yi sayıyordu — plan ile DoD arasında bir sapma vardı. Bağımsız denetim (🟡 #2) bunu yakaladı; "Replay returns `409` for an agent that carries a client-side tool" bölümü artık mevcut `RunNotFound`/`InputNotFound` deseninin yanında duruyor.

## Bu Fazda Verilen Kararlar

- **K-628** — İstemci taraflı tool taşıyan agent hiçbir `toolMode`'da replay edilemez; ret `PrepareAsync`'te, run başlamadan, agent tanımının tool listesi taranarak verilir. Tam gerekçe: `docs/KARARLAR.md`.

## Gerçekleşen Public API

Plandaki taslakla birebir aynı gerçekleşti — tek fark yok:

```csharp
// AgentPrism.Core — Replay/RunReplayService.cs
public enum RunReplayOutcome
{
    Ready = 0,
    RunNotFound = 1,
    InputNotFound = 2,
    NotSupported = 3,
    ApprovalRequired = 4,
    ClientToolNotReplayable = 5,
}
```

`PublicAPI.Unshipped.txt`'e tek satır eklendi: `AgentPrism.RunReplayOutcome.ClientToolNotReplayable = 5 -> AgentPrism.RunReplayOutcome`.

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Core/Replay/RunReplayService.cs          (değişti: FindClientTool + iki kol + enum üyesi)
src/AgentPrism.Core/PublicAPI.Unshipped.txt              (değişti: yeni enum üyesi)
src/AgentPrism.AspNetCore/Endpoints/RunEndpoints.cs      (değişti: switch arm + .WithDescription metni)

tests/AgentPrism.AspNetCore.FunctionalTests/
└── ReplayClientToolEndpointTests.cs                      (yeni — 6 test, planın "Fonksiyonel" satırlarının tamamı)
tests/AgentPrism.Mcp.UnitTests/McpTenantToolsTests.cs     (değişti: 1 yeni test — denetimden, MCP false-positive kilidi)

docs-site/src/content/docs/guides/client-side-tools.md   (değişti: "Client-side tools cannot be replayed" bölümü)
docs-site/src/content/docs/capabilities.md               (değişti: satır düzeltildi)
docs-site/src/content/docs/troubleshooting.md            (değişti: yeni girdi — denetimden)
docs-site/public/llms-full.txt                            (üretildi)

docs/openapi/agentprism.json                              (üretildi — .WithDescription metni)
packages/agentprism-client/src/schema.ts                  (üretildi)
src/AgentPrism.Client/Generated/AgentPrismApiClient.g.cs  (üretildi)

docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md   (değişti: MT-IST-014…016)
docs/KARARLAR.md                                          (değişti: K-628)
```

Planlanmış ama yazılmayan: `tests/AgentPrism.Core.UnitTests/Replay/ReplayClientToolContractTests.cs`
(gerekçe: "Plandan Sapmalar").

## Denetim Bulguları

Bağımsız denetim (taze bağlamlı ayrı agent), 2026-08-26. 🔴: yok. 🟡: 2, ikisi de kapatıldı.

| # | Seviye | Bulgu | Sonuç |
|---|---|---|---|
| 1 | 🟡 | Planın öngördüğü `ReplayClientToolContractTests.cs` yazılmadı; iptal yolu ve MCP false-positive için kilitleyen test yok | **Kısmen düzeltildi, kısmen gerekçelendi**: MCP false-positive artık `McpTenantToolsTests.An_mcp_tool_is_never_marked_RunsOnClient` ile kilitli. İptal yolu için yeni test yazılmadı — gerekçe "Plandan Sapmalar"da |
| 2 | 🟡 | Fazın "Tüketici yüzeyi" `troubleshooting.md`'yi de listeliyordu, dokunulmamıştı | **Düzeltildi** — sayfaya `RunNotFound`/`InputNotFound` deseninde yeni bir bölüm eklendi |

**Temiz çıkan başlıklar:** 3.1 (dört ölçülebilir DoD satırı da teste bağlı, 677/677 yeşil), 3.2, 3.3, 3.5, 3.6 (tek public API üyesi plana birebir uyuyor), 3.7.

## Sonraki Faza Devir Notu

- **Devraldığı sözleşme:** `RunReplayOutcome.ClientToolNotReplayable` (= 5) — istemci tool'u taşıyan bir agent HİÇBİR `toolMode`'da replay edilemez, ret `409`. `FindClientTool` (`RunReplayService.cs`) `ToolDescriptor.RunsOnClient` okur; yeni bir tool sınıfı `RunsOnClient=true` işaretlenirse otomatik olarak bu korumaya girer, kod değişikliği gerekmez.
- **🚨 Yeni bir replay hazırlık kontrolü eklerken İKİ kolu da kapat.** `RunReplayService.PrepareAsync`'in kalıcı-tanım kolu (`definition.ToolNames`) ile katalog/kod-agent kolu (`descriptor.ToolNames`, `PrepareFromCatalogAsync`) AYRI kod yollarıdır; Faz 47 ve bu faz ikisinde de yalnız birini kapatıp ikincisini unutma riskiyle karşılaştı. `grep -n "FindApprovalTool\|FindClientTool" src/AgentPrism.Core/Replay/RunReplayService.cs` her ikisinin de İKİ çağrı yeri olduğunu doğrular.
- **Bilinen sınır:** Örnek uygulamanın (`samples/AgentPrism.Api`) tek istemci-tool'lu kod agent'ı (`support`) aynı zamanda onay gerektiren bir tool taşıyor; katalog kolunun istemci-tool mesajını İZOLE gösteren bir manuel senaryo yoktur (yalnız otomatik testte var). Yeni bir kod agent'ı gerekirse ekle, gerekmiyorsa MT-IST-015'in notunu koru.
- Faz 61'in (istemci tool'ları) ve Faz 47'nin (replay) hiçbiri bu fazla değişmedi; yalnız ikisinin kesişimi kapatıldı.
