# Faz 61 — İstemci Tool'ları ve Gömülebilir Sohbet

> **Durum:** ✅ Tamamlandı (2026-08-18)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-108**, **F-64**
> **Önkoşul:** [Faz 53](53-KIRACI-API-ANAHTARLARI.md) — tarayıcıya yönetim token'ı konulamaz, kapsamlı anahtar şart · [Faz 55](55-ASENKRON-ONAY-KUTUSU.md) — sonuç kanalının emsali · [Faz 48](48-GUARDRAILS.md) — istemciden gelen sonuç guard'dan geçer
> **Paketler:** `AgentPrism.Abstractions`, `AgentPrism.Core`, `AgentPrism.AspNetCore`, `AgentPrism.UI`
> **Yeni paket:** Yok · **Migration:** Yok — senkron kanal seçildi, bekleyen çağrı tablosu **yoktur** (Açık Soru 1'in kullanıcı kararı)
> **Public API:** büyüyor **ve bir imza genişliyor** — `PublicAPI.Shipped.txt` bugün **boş** (ölçüldü: 1 satır), `EnablePublicApiTracking` `true`. Kırıcı sayılan değişiklik bugün **bedava**, ilk yayından sonra değil
> **Site etkisi:** `ui.md` (gömülebilir bileşen bölümü), `capabilities.md`, yeni `guides/client-side-tools.md` + ekran görüntüsü
> **Manuel test alanı:** `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-012\|K-058\|K-059\|K-218\|K-228\|K-232\|K-296\|K-404" docs/KARARLAR.md
   ```
   **K-012** (tool'lar yalnız kodda — bu fazın en sıkı sınırı),
   **K-058** (MCP istisnası: kayıt kodda, çalıştırma uzakta — bu fazın emsali),
   **K-218** (tool bağımlılığı **kurulum anında** alınır, `AIFunctionArguments.Services` boştur),
   **K-228** (eksik sözlük anahtarı derleme hatasıdır),
   **K-232** (sunucu yanıtı çevrilmez),
   **K-296** (SSE başlıkları çoktan gönderilmiştir),
   **K-404** (bilinmeyen tool adı kayıt anında `400` ile reddedilir)
3. [`55-ASENKRON-ONAY-KUTUSU.md`](55-ASENKRON-ONAY-KUTUSU.md) — yalnız 55.2 ve 55.4:
   ```bash
   awk '/## 55.2/,/## 55.5/' docs/55-ASENKRON-ONAY-KUTUSU.md
   ```
   Bu faz onun **kanal desenini** devralır, tablosunu devralmaz.
4. Alan hafızası (bu faz üç alana dokunuyor):
   [`hafiza/frontend.md`](hafiza/frontend.md) (yeni bir Vite çıktısı ve sözlük),
   [`hafiza/aspnetcore-di.md`](hafiza/aspnetcore-di.md) (yeni uç ve seçenek),
   [`hafiza/maf-api.md`](hafiza/maf-api.md) (`AIFunctionDeclaration` ilk kez kullanılıyor)
5. Gerektiğinde, tamamı değil ilgili bölümü:
   [`MIMARI.md`](MIMARI.md) bölüm 3 (K2'nin tanımı ve iki istisnası)

---

## Amaç

Paketi kullanan bir arka uç servisi, agent'ının **tarayıcıda** çalışan bir
tool'u çağırmasını isteyebilir: kullanıcının o anki ekran durumunu okumak,
dosya seçtirmek, yalnız tarayıcıda duran bir kimlik bilgisiyle çağrı yapmak.
Bugün bunun yolu yoktur. Aynı tüketicinin kendi uygulamasına koyacağı küçük bir
sohbet kutusu da yoktur; arayüz tam bir kontrol düzlemidir.

Bu faz ikisini birlikte verir, çünkü **gömülebilir bileşen, istemci tool'unu
çalıştıran taraftır**. Biri olmadan diğeri yarım kalır.

- **F-108** — Gövdesi sunucuda olmayan tool türü ve sonucu geri alan senkron kanal.
- **F-64** — Ayrı çıktı olarak gömülebilir sohbet bileşeni ve onu mümkün kılan CORS yüzeyi.

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`IToolRegistry.cs:24`](../src/AgentPrism.Abstractions/Tools/IToolRegistry.cs) | `TryGet` bir **`AIFunction`** döndürür. Kayıtlı her tool'un gövdesi sunucudadır |
| [`ToolRegistry.cs:33-38`](../src/AgentPrism.Core/Tools/ToolRegistry.cs) | "Sunucuda çalıştırma" kararının **tek** yeri. `ApprovalRequiredAIFunction` yalnız **kararı** erteler; onaydan sonra gövdeyi yine sunucu çalıştırır |
| [`ApprovalResumeJobHandler.cs:96`](../src/AgentPrism.Core/Approvals/ApprovalResumeJobHandler.cs) | Sürdürme `ToolApprovalResponseContent` yazar — **karar**, sonuç değil |
| [`AgentContracts.cs:233`](../src/AgentPrism.AspNetCore/Contracts/AgentContracts.cs) | `AgentRunRequest.Approvals` var. İstemci sonucu için **kardeş alanın emsali** budur |
| [`AgentEndpoints.cs:545`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | `approvals` için `sessionId` **zorunlu** — bekleyen istek oturum geçmişinde yaşar. Aynı kısıt tool sonucu için de geçerlidir |
| [`AgentEndpoints.cs:534-540`](../src/AgentPrism.AspNetCore/Endpoints/AgentEndpoints.cs) | Kuyruğa alınan çalıştırmada `approvals` **bilerek `400`**. Bu fazın kuyruk kararı aynı emsali izler |
| `grep -rn "ClientTool\|DeferredTool\|RemoteTool" src/` | Yalnız MCP'nin kendi `McpClientTool`'u. **Sıfır altyapı** |
| 🚨 `grep -rn "Cors\|AllowAnyOrigin\|WithOrigins" src/` | **Sıfır sonuç. CORS yoktur.** Farklı origin'deki bir sohbet kutusu bugün tarayıcı tarafından engellenir — F-64'ün gerçek ön koşulu budur ve aday listesi bunu yazmamıştı |
| [`postbuild.mjs:25`](../src/AgentPrism.UI/frontend/scripts/postbuild.mjs) | `JS_BUDGET_BYTES = 250 * 1024` gzip. Kapı **tek** çıktıya uygulanır |
| Ölçüm: `wwwroot/assets/index-*.js` | gzip **169.396 B = 165,4 KB** / 250 KB → kalan pay **84,6 KB**. 🚨 Aday listesindeki "151,3 KB, kalan ~99 KB" **bayattı** |
| [`ApiKeyScope.cs:26`](../src/AgentPrism.Abstractions/Security/ApiKeyScope.cs) | `RunsWrite` = "Starting a run, cancelling, giving approval". Gömülebilir bileşenin ihtiyacı tam olarak budur |
| `PublicAPI.Shipped.txt` (1 satır) · `Directory.Build.props:58` | İzleme **açık**, ama yayınlanmış yüzey **boş**. İmza genişletmek bugün bedava |

> Kanıtlar 2026-08-18 tarihinde doğrulandı.

### MAF ölçümleri — tahmin değil, koşuldu

Fazın çekirdek mekanizması `maf-api-kesfi` ve bir davranış probu ile ölçüldü
(2026-08-18, `Microsoft.Extensions.AI` + MAF 1.16.0):

| Ölçüm | Sonuç |
|---|---|
| `AIFunctionDeclaration` var mı? | **Var.** `AITool` türevi, `abstract`, yalnız `JsonSchema` + `ReturnJsonSchema` taşır |
| `AIFunction.AsDeclarationOnly()` ne döner? | `Microsoft.Extensions.AI.AIFunction+NonInvocableAIFunction`. 🚨 `is AIFunction` → **`False`**. Ad, açıklama ve şema korunur |
| Bildirim-yalnız tool modele gider mi? | **Evet.** `FunctionInvokingChatClient` onu `Tools` içinde modele geçirir |
| Sunucu onu çalıştırır mı? | **Hayır.** Tek model çağrısı sonrası döngü durur ve `FunctionCallContent` **çağırana döner** |
| `TerminateOnUnknownCalls` etkisi? | **Yok.** Varsayılan `False`; `true`/`false` ikisinde de davranış aynı — çağrı "bilinmeyen" değil, "çağrılamaz". Bu bayrağa **bağımlılık kurulmaz** |
| Kontrol: gerçek `AIFunction`? | İki model çağrısı, `FunctionResultContent` sunucuda üretiliyor — zıtlık kanıtlandı |
| Sonuç dışarıdan verilirse tur tamamlanır mı? | **Evet.** Geçmişe `FunctionResultContent` eklenince model ikinci turda nihai metni üretti |

🚨 **Bu ölçüm fazın en pahalı riskini kaldırdı.** Mekanizma MAF'ta **hazırdır**;
AgentPrism yeni bir çalıştırma yolu yazmaz, var olan bir MAF ayrımını kullanır.

### Ölçülen ve düzeltilen yanlış iddia

`OpenAIResponsesEndpoints.cs` XML dokümanı, çağıranın "standart OpenAI akışını
izleyip bir sonraki turda `function_call_output` verebileceğini" yazıyordu.
**Ölçüldü ve yanlış çıktı:** MAF'ın `OpenAIResponses.ToAgentRunRequest`'i
`input` dizisinin **her** öğesini iç `Responses.Models.InputMessage` tipine
çözer; bu tip `role` ve `content` alanlarını **zorunlu** ister. `type` üzerinden
çok biçimli dağıtım yoktur, bu yüzden `function_call_output` **ve**
`function_call` öğeleri `JsonException` ile düşer ve uç `400` döner.

Yorum bu fazın hazırlığı sırasında koda göre düzeltildi. **Sonuç: OpenAI uyumlu
yüzey bu fazın sonuç kanalı olamaz.** Tool çağrı turu orada yalnız **çıkış**
yönünde vardır.

---

## 61.1 — İki yarı neden tek faz

```mermaid
flowchart LR
    A["Tuketicinin sayfasi<br/>shop.example.com"] -->|"1 - soru"| B["AgentPrism<br/>api.example.com"]
    B -->|"2 - FunctionCallContent<br/>govde sunucuda YOK"| A
    A -->|"3 - tarayicida calistir"| A
    A -->|"4 - toolResults ile sonuc"| B
    B -->|"5 - nihai yanit"| A
```

Üçüncü adım tarayıcıda geçer. Onu çalıştıracak bir istemci yoksa F-108'in
uçtan uca kanıtı yazılamaz. Birinci ve dördüncü adımlar **farklı origin**
arasında geçer; CORS olmadan tarayıcı ikisini de engeller. Bu yüzden iki kalem
aynı fazdadır: **F-64 F-108'in istemcisi, CORS ise ikisinin ortak ön koşuludur.**

## 61.2 — İstemci tool'u: bildirim kodda, gövde istemcide

🚨 **K2 gevşemez.** Tool'un **bildirimi** — ad, açıklama, JSON şeması — kodda
kalır. Yalnız **gövdesi** istemcide çalışır. Bu, K-058'in (MCP) desenidir:
kayıt kodda, çalıştırma başka yerde.

```mermaid
flowchart TD
    A["AddClientTool ad + sema<br/>KODDA"] --> B["ToolRegistry"]
    B --> C["AIFunctionDeclaration<br/>NonInvocableAIFunction"]
    C --> D["Modele gider"]
    D --> E["Model cagirir"]
    E --> F["FunctionInvokingChatClient<br/>CALISTIRMAZ - dongu durur"]
    F --> G["FunctionCallContent cagirana doner"]
```

**Kapsam dışı, bilerek:** tool tanımının istemciden çalışma anında bildirilmesi
(Vercel AI SDK `useChat` deseni). O, K2'nin **üçüncü istisnası** olurdu ve ayrı
bir karar ister. Bu faz onu **açmaz**; `AgentDefinitionValidator` (K-404) hiç
değişmeden çalışmaya devam eder.

**`AgentPrismToolRegistration` bugün `AIFunction` alıyor.** İstemci tool'u
`AITool`'dur ama `AIFunction` **değildir** (ölçüldü). Kayıt tipi ve
`IToolRegistry.TryGet` bu yüzden `AITool`'a genişler. Bugün bedava; ilk
yayından sonra kırıcı olurdu.

## 61.3 — Sonuç kanalı: `AgentRunRequest.ToolResults`

Karar (kullanıcı, 2026-08-18): **senkron alan, tablo yok.**

`Approvals` ile birebir aynı sözleşme:

| Kural | Gerekçe |
|---|---|
| `sessionId` **zorunlu** | Bekleyen çağrı oturum geçmişinde yaşar. Oturumsuz çalıştırmada bulunamaz — `Approvals`'ın kısıtının aynısı (`AgentEndpoints.cs:545`) |
| Kuyruk yolunda **`400`** | `Prefer: respond-async` çalıştırmasında bağlı tarayıcı yoktur. Faz 46 `approvals` için aynı kararı verdi (`AgentEndpoints.cs:534-540`) |
| `callId` **eşleşmeli** | Oturum geçmişinde o `callId` ile bekleyen bir çağrı yoksa `400` |
| **Tek kullanımlık** | Aynı `callId` ikinci kez yanıtlanamaz — `409` |

## 61.4 — Güvenilmeyen sonucun işlenmesi

🚨 **Bu fazın en büyük güvenlik değişikliği budur.** Bugün konuşmadaki her
`FunctionResultContent`'i **sunucu kodu** üretir. Bu fazdan sonra bir kısmını
**tarayıcı** üretir; model bağlamına yeni bir enjeksiyon yüzeyi açılır.

| Önlem | Nerede |
|---|---|
| Guard boru hattından geçer | [`ContentGuardMessageMasker.cs:156`](../src/AgentPrism.Core/Guards/ContentGuardMessageMasker.cs) `FunctionResultContent`'i **bilerek** kapsıyor — yeni kod gerekmez, kapsandığı **test edilir** |
| Kiracı bağı | Sonuç, çağrıyı üreten çalıştırmanın kiracısına ait olmalı |
| Boyut sınırı | Sonuç gövdesi sınırlanır; sınırsız metin bağlam penceresini tüketir |
| Denetim izi | Sonuç `audit_log`'a yazılır. 🚨 K-089'un emsali **uygulanmaz**: bu bir güvenlik **kararı** değil, veridir — yazma hatası çalıştırmayı kesmez ("gözlemlenebilirlik işlevi bozmaz") |

## 61.5 — CORS: varsayılan kapalı

CORS bugün **yoktur** ve bu faz onu ekler. K1 gereği **varsayılan kapalıdır**;
açılması kodda, açık bir origin listesiyle olur.

🚨 `AllowAnyOrigin` **sunulmaz.** Kimlik bilgisi taşıyan bir uçta joker origin
bir güvenlik hatasıdır ve kütüphane onu kolay yapmamalıdır.

## 61.6 — Gömülebilir bileşen: ayrı çıktı, ayrı bütçe

🚨 **Kontrol düzlemi bundle'ına girmez.** Ölçüldü: konsol bugün 165,4 KB / 250
KB gzip kullanıyor, kalan pay 84,6 KB. Widget o payı yemez; **ayrı bir Vite
girişi, ayrı çıktı ve ayrı bir bütçe kapısı** alır. Hedef **< 30 KB gzip**.

Kontrol düzlemi bileşenleri bu çıktıya sızarsa hedef tutmaz; kapı bunu
sayısal olarak zorlar.

Kimlik: Faz 53'ün kiracı API anahtarı, `RunsWrite` kapsamı. Yönetim token'ı
tarayıcıya **konulmaz**.

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.

```csharp
// AgentPrism.Abstractions — genisleyen imza
public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> List();

    // AIFunction -> AITool. Istemci tool'u AIFunction DEGILDIR (olculdu).
    bool TryGet(string name, [NotNullWhen(true)] out AITool? tool);
}

// ToolDescriptor'a yeni alan
public sealed record ToolDescriptor
{
    // ... mevcut alanlar
    /// <summary>Whether the tool body runs on the caller instead of the server.</summary>
    public bool RunsOnClient { get; init; }
}

// AgentPrism.Core — kayit
public static class AgentPrismClientToolExtensions
{
    public static IAgentPrismBuilder AddClientTool(
        this IAgentPrismBuilder builder,
        string name,
        string description,
        JsonElement jsonSchema);
}

// AgentPrism.AspNetCore — sozlesme, Approvals'in kardesi
public sealed record ClientToolResult
{
    public required string CallId { get; init; }
    public required string Result { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record AgentRunRequest
{
    // ... mevcut alanlar
    public IReadOnlyList<ClientToolResult> ToolResults { get; init; } = [];
}

// AgentPrism.AspNetCore — CORS, varsayilan KAPALI
public sealed class AgentPrismEndpointOptions
{
    // ... mevcut alanlar
    /// <summary>Origins allowed to call the endpoints from a browser. Empty by default.</summary>
    public IList<string> AllowedOrigins { get; } = [];
}
```

🚨 **`AddClientTool` gövdesiz bir bildirim üretir.** Ölçülmüş yol:
`AIFunctionFactory.Create(...)` ile bir fonksiyon kurup `AsDeclarationOnly()`
çağırmak. `AIFunctionDeclaration` `abstract` ve `AITool.Name`/`Description`
üyelerinin ezilebilirliği **ölçülmedi**; doğrudan alt sınıf yazmak istenirse
önce `maf-api-kesfi` ile ölçülmelidir (Açık Soru 2).

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `POST` | `/api/agents/{name}/run` | Operator · `RunsWrite` | **Mevcut uç.** Gövdeye `toolResults` alanı eklenir; yeni uç açılmaz |

Yeni uç **yoktur**. Sonuç, onay kararıyla aynı kapıdan girer.

### Arayüz payı

| Çıktı | Bugün | Bu fazdan sonra | Kapı |
|---|---|---|---|
| Kontrol düzlemi | 165,4 KB gzip | Playground'a istemci tool rozeti + sözlük — **artış ölçülüp yazılacak** | 250 KB |
| Gömülebilir bileşen | yok | **yeni** | **< 30 KB gzip, ayrı kapı** |

---

## Planlanan Dosya Listesi

```
src/AgentPrism.Abstractions/Tools/
├── IToolRegistry.cs                 (imza genisler: AIFunction -> AITool)
├── ToolDescriptor.cs                (RunsOnClient alani)
└── AgentPrismToolRegistration.cs    (AITool kabul eder)

src/AgentPrism.Core/Tools/
├── ToolRegistry.cs                  (bildirim-yalniz kayit yolu)
└── ClientToolRegistration.cs        (yeni)

src/AgentPrism.AspNetCore/
├── Contracts/AgentContracts.cs      (ClientToolResult + ToolResults)
├── Endpoints/AgentEndpoints.cs      (toolResults isleme, kuyrukta 400)
├── Internal/ClientToolResultResolver.cs   (yeni - ToolApprovalResolver kardesi)
└── AgentPrismEndpointOptions.cs     (AllowedOrigins)

src/AgentPrism.UI/frontend/
├── vite.config.ts                   (ikinci giris)
├── src/embed/                       (yeni - gomulebilir bilesen)
│   ├── main.ts
│   └── ClientToolRunner.ts
├── src/locales/{en,tr}.ts           (yeni anahtarlar - K-228)
└── scripts/postbuild.mjs            (ikinci butce kapisi)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.

| Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|
| İstemci tool'u sunucuda **çalışır** (K2 ihlali) | Fonksiyonel | `ClientToolEndpointTests` — gövdeli fonksiyon hiç çağrılmadan `FunctionCallContent` döner |
| Bilinmeyen `callId` ile sonuç gönderilir | Fonksiyonel | `ClientToolEndpointTests` → `400` |
| Aynı `callId` iki kez yanıtlanır | Fonksiyonel | `ClientToolEndpointTests` → `409` |
| `sessionId` olmadan sonuç gönderilir | Fonksiyonel | `ClientToolEndpointTests` → `400` |
| Kuyruk yolunda (`Prefer: respond-async`) sonuç gönderilir | Fonksiyonel | `ClientToolEndpointTests` → `400` (Faz 46 emsali) |
| Başka kiracının çalıştırmasına sonuç yazılır | Sözleşme (`TenantIsolationContract`) | dört koşumda birden |
| İstemciden gelen sonuç guard'ı **atlar** | Fonksiyonel | `ClientToolGuardTests` — engellenen desen taşıyan sonuç maskelenir |
| Aşırı büyük sonuç bağlamı doldurur | Fonksiyonel | `ClientToolEndpointTests` → sınır aşımında `400` |
| Çalıştırma iptal edilirken sonuç gelir | Fonksiyonel | `ClientToolEndpointTests` |
| Eşzamanlı iki sonuç aynı `callId` için yarışır | Fonksiyonel | `ClientToolConcurrencyTests` |
| CORS varsayılan **açık** gelir | Fonksiyonel | `CorsOptionsTests` — `AllowedOrigins` boşken `Origin` başlıklı istek reddedilir |
| `AllowAnyOrigin` yazılabilir | Birim | `CorsOptionsTests` — böyle bir API **yok** |
| Widget bundle'ı kontrol düzlemi kodunu çeker | E2E / kapı | `postbuild.mjs` ikinci bütçe kapısı, < 30 KB gzip |
| Eksik sözlük anahtarı | Derleme | `tsc --noEmit` (K-228) |
| Tarayıcıda tool çalışır ve tur tamamlanır | E2E (Playwright) | `UiTests.Istemci_toolu_tarayicida_calisir_ve_calistirma_tamamlanir` |

Sözleşme testi `tests/Shared/Contracts/` altına — hem bellek içi hem üç SQL
sağlayıcısı üzerinde koşar.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` içine
> eklenecek case'lerin taslağı.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | `AddClientTool("read_page_title", …)` kodda kayıtlı | Playground'dan tool'u kullandıran bir istem gönder | Yanıt `FunctionCallContent` taşır; sunucu logunda tool **çalışmamıştır** |
| 2 | 1'in çıktısı | `toolResults` ile sonucu gönder | Çalıştırma tamamlanır, nihai metin sonucu kullanır |
| 3 | 1'in çıktısı | Aynı `callId` ile ikinci kez gönder | `409` |
| 4 | — | `sessionId` olmadan `toolResults` gönder | `400` |
| 5 | — | `Prefer: respond-async` ile `toolResults` gönder | `400`, mesaj kuyruk kısıtını açıklar |
| 6 | `AllowedOrigins` boş | Farklı origin'den `fetch` | Tarayıcı engeller; sunucu CORS başlığı **yollamaz** |
| 7 | `AllowedOrigins` doluyken | Aynı istek | Geçer |
| 8 | Widget kurulu, `RunsWrite` anahtarı | Sohbet kutusundan mesaj gönder ve istemci tool'unu tetikle | 👤 insan gerekir — tur uçtan uca tamamlanır |
| 9 | — | `npm run build` | Widget çıktısı < 30 KB gzip; kapı sayıyı yazar |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | İstemci tool'u onay akışına da girebilmeli mi? | A: Hayır, ikisi ayrı · B: Evet, `RequiresApproval` birlikte çalışsın | **A.** Onay sunucu gövdesini bekletmek içindir; istemci tool'unda gövde zaten kullanıcının makinesinde. İkisini birleştirmek iki bekleme durumu üretir |
| 2 | Bildirim nasıl kurulur? | A: `AIFunctionFactory.Create(...).AsDeclarationOnly()` · B: `AIFunctionDeclaration` alt sınıfı | **A** — ölçüldü ve çalışıyor. B daha temiz görünür ama `AITool.Name`/`Description` ezilebilirliği **ölçülmedi**; B seçilecekse önce `maf-api-kesfi` koşulur |
| 3 | Widget kendi paketine mi girsin? | A: `AgentPrism.UI` içinde ikinci çıktı · B: yeni `AgentPrism.Embed` paketi | **A.** Yeni paket K-007 gerekçesi ister ve widget aynı yayın hattını paylaşır. Ayrılırsa bu bir sonraki fazın işidir |
| 4 | Sonuç hatası nasıl taşınır? | A: `ErrorMessage` alanı modele iletilir · B: hata `400` olur | **A.** Tarayıcıda tool başarısız olabilir (kullanıcı iptal etti); model bunu görüp toparlayabilmeli. `400` turu öldürür |
| 5 | Widget'ın sözlüğü konsolla ortak mı? | A: Ortak `locales/` · B: Widget'ın kendi küçük sözlüğü | **B.** Ortak sözlük tüm anahtarları widget bundle'ına çeker ve 30 KB hedefini bozar. 🚨 Ölçülmeli, tahmin edilmemeli |

---

## Bitiş Ölçütleri (DoD)

- [x] Kodda kayıtlı bir istemci tool'u modele gider, **sunucuda çalışmaz**, `FunctionCallContent` çağırana döner — gerçek OpenAI çağrısıyla doğrulandı (aşağıda)
- [x] `toolResults` ile gönderilen sonuç turu tamamlar; nihai yanıt sonucu kullanır — gerçek OpenAI çağrısıyla doğrulandı (aşağıda)
- [x] `sessionId` yokken, kuyruk yolunda, bilinmeyen `callId` ile ve ikinci kez gönderimde sırasıyla `400`/`400`/`400`/`409` — `ClientToolEndpointTests` (9 test) + `samples/AgentPrism.Api` üzerinde `curl` ile doğrulandı
- [x] İstemciden gelen sonuç guard boru hattından geçer (engellenen desen maskelenir) — `ClientToolGuardTests`, `422` + `errorType: content_blocked`, yasaklı terim yanıt gövdesinde yok
- [x] Başka kiracının çalıştırmasına sonuç yazılamaz — `ClientToolEndpointTests.Another_tenants_pending_call_cannot_be_answered` + `SessionStoreContract : TenantIsolationContract<ISessionStore>` (dört sağlayıcı, mevcut kanıt — Denetim Bulguları #9)
- [x] `AllowedOrigins` boşken CORS başlığı **yollanmaz**; `AllowAnyOrigin` API'si **yoktur** — `CorsOptionsTests` (5 test) + `samples/AgentPrism.Api` üzerinde `curl` ile doğrulandı
- [x] Gömülebilir bileşen ayrı çıktıdır ve < 30 KB gzip; kapı sayıyı build çıktısına yazar — ölçüldü: **2,7 KB gzip** (`postbuild-embed.mjs`)
- [x] Kontrol düzlemi bundle'ı 250 KB gzip altında; yeni pay ölçülüp belgeye yazıldı — ölçüldü: **165,7 KB gzip** (önceki: 165,4 KB — `runsOnClient` rozeti ve iki yeni anahtar +0,3 KB)
- [x] Dört doğrulama kapısı sıfır uyarı verir — `build`/`test`/`pack`/`format` dördü de bu oturumda koşuldu, dördü de temiz
- [x] `samples/AgentPrism.Api` ile gerçek `run` yapıldı, çıktı belgeye yazıldı — aşağıda
- [x] `secret` taraması boş döndü — `faz-tamamlama` Adım 1'in deseni koşuldu, bu fazda dokunulan dosyalarda eşleşme yok
- [x] Manuel kabul case'leri `docs/manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md` içine eklendi; otomatikleştirilebilenler koşuldu — 13 case (MT-IST-001…013), `curl` ile koşulabilenler `samples/AgentPrism.Api` üzerinde gerçek OpenAI çağrısıyla teyit edildi; MT-IST-012 (tarayıcı etkileşimi) 👤 insan gerektirir olarak işaretli — otomatik karşılığı `UiTests.Embed_widget_runs_a_client_side_tool_and_completes_the_turn_in_a_real_browser`'dır
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — iki 🔴 bulundu ve **düzeltildi** (Denetim Bulguları bölümü); ikinci koşumda 🔴 yok
- [x] `docs-site/` güncellendi (`ui.md`, `capabilities.md`, `guides/client-side-tools.md`); `npm run build` + `check-links.mjs` temiz — `892 sayfa, 0 kırık bağlantı`; API/HTTP referansı `npm run generate` ile tazelendi (aynı geçişte 604 bayat senkronizasyon kopyası da temizlendi — Faz 61'den önceydi, bu fazın hatası değil)
- [x] `en.ts` ve `tr.ts` eksiksiz (K-228) — `tsc --noEmit` + `i18n.test.ts` (16 test) temiz; widget'ın kendi sözlüğü ayrı, `SourceLanguageTests` istisnasıyla (Denetim Bulgusu #1)

### Doğrulama komutları — gerçek çıktı

`samples/AgentPrism.Api` üzerinde, gerçek bir OpenAI çağrısıyla (`gpt-5.4-mini`), `support` agent'ının `read_shopping_cart` istemci tool'uyla koşuldu:

```bash
$ curl -s -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: e2e-key-1' \
  -d '{"sessionId":"e2e-manual-1","message":"What is in my shopping cart right now?"}'
```
```json
{
  "runId": "01a01279-53c7-7449-8e51-264871b54313",
  "sessionId": "e2e-manual-1",
  "response": {
    "messages": [{
      "role": "assistant",
      "contents": [{
        "$type": "functionCall", "name": "read_shopping_cart",
        "arguments": {}, "callId": "call_qbyUNWHadUVfjzaNXC1AxypY"
      }]
    }],
    "finishReason": "tool_calls"
  }
}
```

`FunctionCallContent` döndü; `read_shopping_cart`'ın **çalıştığına dair sunucu logunda hiçbir iz yoktu** (K2 doğrulandı).

```bash
$ curl -s -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -H 'Idempotency-Key: e2e-key-2' \
  -d '{"sessionId":"e2e-manual-1","toolResults":[{"callId":"call_qbyUNWHadUVfjzaNXC1AxypY","result":"2x Wireless Mouse, 1x USB-C Cable"}]}'
```
```json
{
  "runId": "01a01279-7d38-7522-912e-65f55e88127f",
  "sessionId": "e2e-manual-1",
  "response": {
    "messages": [{
      "role": "assistant",
      "contents": [{ "$type": "text", "text": "Your shopping cart currently has:\n\n- 2x Wireless Mouse\n- 1x USB-C Cable" }]
    }],
    "finishReason": "stop"
  }
}
```

Tur tamamlandı; model sonucu doğru kullandı.

```bash
$ curl -s -o /dev/null -w "%{http_code}\n" -X POST http://localhost:5080/agentprism/api/agents/support/run \
  -H "Authorization: Bearer $TOKEN" -H 'Prefer: respond-async' -H 'Content-Type: application/json' \
  -d '{"sessionId":"e2e-manual-1","toolResults":[{"callId":"x","result":"y"}]}'
400

$ curl -s -o /dev/null -w "%{http_code} %{content_type}\n" http://localhost:5080/agentprism/embed/embed.js
200 text/javascript; charset=utf-8

$ curl -s -D - -o /dev/null http://localhost:5080/agentprism/api/meta -H "Origin: https://shop.example.com" | grep -i access-control
(çıktı boş — beklenen, AllowedOrigins yapılandırılmadı)
```

---

## Riskler

| Risk | Önlem |
|------|-------|
| 🚨 İstemciden gelen sonuç bir enjeksiyon yüzeyidir | Guard boru hattı zorunlu ve **test edilir**; boyut sınırı; kiracı bağı |
| 🚨 CORS yanlış açılırsa kimlik bilgisi sızar | Varsayılan kapalı; `AllowAnyOrigin` API'si sunulmaz; açık origin listesi kodda |
| Widget bundle'ı kontrol düzlemi kodunu çeker | Ayrı Vite girişi + ayrı bütçe kapısı; sözlük ayrı (Açık Soru 5) |
| `IToolRegistry` imzası genişliyor | Bugün bedava (`PublicAPI.Shipped.txt` boş); ilk yayından sonra yapılamaz — bu faz **yayından önce** kalmalı |
| K2 sınırının gevşediği algısı | Bildirim kodda kalır; tanımın istemciden bildirilmesi **kapsam dışı** ve dokümanda açıkça yazılı |
| Kuyruk yolu kullanıcıyı şaşırtır | `400` mesajı kısıtı ve nedenini açıklar (K-232: İngilizce) |

🚨 **Bu fazla ilgisi olmayan, ÖNCEDEN VAR OLAN bir gözlem:** `UiTests.Runs_button_on_session_page_navigates_to_filtered_list`
(oturum sayfasındaki "N runs" düğmesi, `support` agent'ıyla — istemci
tool'larıyla ilgisiz) izole `dotnet test tests/AgentPrism.Ui.E2ETests`
koşumlarında aralıklı düşüyor (bu oturumda 4 izole koşumdan 2'sinde: `tbody tr`
sayısı beklenenle uyuşmuyor). Kök neden araştırılmadı — bu fazın kapsamı
dışında bir ekranın zamanlama/yarış davranışı gibi görünüyor. Bu fazın KENDİ
testleri (yeni 3 E2E, hepsi istemci tool'u/widget/rozet) izole koşumlarda
**tutarlı biçimde geçti**; flaky test farklı, ilgisiz bir özelliktedir.
Kayıt altına alındı, düzeltilmedi — ayrı bir kusur olarak ele alınmalı.

---

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

Sekiz kalem — hepsi ölçülerek keşfedildi, K-435…K-443 olarak kayıtlı (bkz. bir
sonraki bölüm). Özet:

1. **`IToolRegistry`/`AgentPrismToolRegistration` `AITool`'a değil
   `AIFunctionDeclaration`'a genişledi** (K-435). Plan `AITool` öngörüyordu;
   ölçüldü ki `AITool`'un kendisi `.JsonSchema` taşımıyor, yalnız
   `AIFunctionDeclaration` (ve onun altındaki `AIFunction`) taşıyor.
2. **İstemci tool'u bildirimi `AIFunctionFactory.CreateDeclaration(...)` ile
   kuruldu**, planın Açık Soru 2'sindeki iki seçenekten hiçbiri değil (K-436).
   Üçüncü, ölçülmüş bir yol bulundu — sahte bir gövde fonksiyonu gerekmiyor.
3. **`FunctionResultContent` `ChatRole.Tool` altında gönderiliyor**, plan bunu
   hiç belirtmiyordu (K-437). Gerçek bir OpenAI çağrısıyla ölçüldü.
4. **CORS, `services.AddCors()` OLMADAN elle kurulan `CorsService`/`CorsMiddleware`
   ile uygulandı** (K-438). Plan bu kısıtı öngörmüyordu; `MapAgentPrism()`
   `app.Build()`'den sonra çalıştığı için `AddCors()` orada çağrılamıyor —
   ölçüldü (host başlangıcında `InvalidOperationException`).
5. **`toolResults` eşleştirmesi BİLEREK iki kez yapılıyor** — akış başlamadan
   önce doğrulama, sonra gerçek mesaj kurulumu (K-439). K-324'ün SSE-başlıkları-
   çoktan-gönderilmiş kısıtı `toolResults`'un `400`/`409` ayrımı için de geçerli
   çıktı; plan bunu öngörmüyordu.
6. **65.536 karakterlik bir boyut sınırı eklendi** (K-440). Plan "boyut sınırı"
   istiyordu ama sayı vermiyordu.
7. **Gömülebilir bileşen yalnız akışsız (`Idempotency-Key`) istek kullanıyor,
   SSE ayrıştırıcı taşımıyor** (K-441). 30 KB bütçesi konsolun `sse.ts`'ini
   taşımayı göze alamazdı.
8. **Gömülebilir bileşen `wwwroot/embed/`'e ayrı bir Vite girişiyle derleniyor
   ve VAR OLAN genel varlık sunum mekanizmasıyla, sıfır yeni C# `endpoint`
   koduyla sunuluyor** (K-442). Plan yeni bir sunum mekanizması varsayıyordu;
   `EmbeddedUiAssetCatalog`'un zaten genel olduğu ölçüldü.

Ayrıca, planın Açık Soru listesindeki kararlar:

- **Açık Soru 1 (onay + istemci tool'u ayrık kalsın mı?)** → **A** seçildi ve
  `ToolRegistry`/`McpTenantTools`'ta bir kayıt-anı denetimiyle **zorlandı**:
  `RequiresApproval: true` taşıyan bir kayıt `AIFunction` değilse başlangıçta
  `AgentPrismException` fırlar.
- **Açık Soru 3 (widget ayrı paket mi?)** → **A** (aynı `AgentPrism.UI`
  paketi, ikinci Vite girişi) — plandaki önerinin aynısı.
- **Açık Soru 4 (sonuç hatası nasıl taşınır?)** → **A** (`ErrorMessage`
  model'e iletilir) — plandaki önerinin aynısı, `"Error: {mesaj}"` biçiminde.
- **Açık Soru 5 (widget'ın sözlüğü ayrı mı?)** → **B** (kendi küçük sözlüğü) —
  plandaki önerinin aynısı, ama uygulama sırasında `SourceLanguageTests`'in
  taban çizgisini bozduğu için (K-408'in tek istisnası `locales/tr.ts`'tir)
  Türkçe kısmı `embed/locale.tr.ts` diye AYRI bir dosyaya taşındı ve o dosya
  istisna listesine eklendi — plan bu ayrıntıyı öngörmüyordu.

**Bağımsız denetimde bulunan ve düzeltilen iki kritik kusur** (ayrıntı Denetim
Bulguları bölümünde): gömülebilir bileşen hiçbir zaman bir `sessionId` almıyordu
— istemci tool turu asla tamamlanamazdı; ve yeni widget sözlüğü dil sınırı
kapısını (`SourceLanguageTests`) kırıyordu.

## Bu Fazda Verilen Kararlar

K-435 ile K-443 arası — tam metin ve gerekçe `docs/KARARLAR.md`'de:

| Karar | Özet |
|---|---|
| K-435 | `IToolRegistry`/`AgentPrismToolRegistration` `AIFunctionDeclaration`'a genişledi, `AITool`'a değil |
| K-436 | İstemci tool'u bildirimi `AIFunctionFactory.CreateDeclaration(...)` ile kurulur |
| K-437 | İstemciden gelen sonuç `ChatRole.Tool` altında gönderilir |
| K-438 | CORS `services.AddCors()` olmadan elle kurulur |
| K-439 | `toolResults` eşleştirmesi bilerek iki kez yapılır (akış kısıtı) |
| K-440 | `ClientToolResult.Result`/`ErrorMessage` 65.536 karakterle sınırlanır |
| K-441 | Gömülebilir bileşen yalnız akışsız istek kullanır |
| K-442 | Gömülebilir bileşen var olan genel varlık mekanizmasıyla sunulur |
| K-443 | Gömülebilir bileşen `sessionId`'yi ilk mesajdan önce, istemci tarafında ayırır (denetimde bulunan kusurun düzeltmesi) |

## Gerçekleşen Public API

Taslaktan üç sapma dışında planla birebir: `AITool` yerine `AIFunctionDeclaration`
(K-435), `ClientToolResult.Result` planın öngördüğü `required string` değil
`string?` (hem `Result` hem `ErrorMessage` opsiyonel, ikisinden en az biri
anlamlı olmalı — zorunlu kılınmadı), ve `AgentPrismEndpointOptions.AllowedOrigins`
`IList<string>` olarak plandaki gibi kaldı.

```csharp
// AgentPrism.Abstractions — genişleyen imza
public interface IToolRegistry
{
    IReadOnlyList<ToolDescriptor> List();
    bool TryGet(string name, [NotNullWhen(true)] out AIFunctionDeclaration? tool);
}

public sealed record ToolDescriptor
{
    // ... mevcut alanlar
    public bool RunsOnClient { get; init; }
}

public sealed class AgentPrismToolRegistration
{
    public AgentPrismToolRegistration(AIFunctionDeclaration function, bool requiresApproval = false, string? source = null);
    public AIFunctionDeclaration Function { get; }
    public bool RequiresApproval { get; }
    public string? Source { get; }
}

// AgentPrism.Core
public static class AgentPrismClientToolExtensions
{
    public static IAgentPrismBuilder AddClientTool(
        this IAgentPrismBuilder builder,
        string name,
        string description,
        JsonElement jsonSchema);
}

// AgentPrism.AspNetCore
public sealed record ClientToolResult
{
    public required string CallId { get; init; }
    public string? Result { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record AgentRunRequest
{
    // ... mevcut alanlar
    public IReadOnlyList<ClientToolResult> ToolResults { get; init; } = [];
}

public sealed class AgentPrismEndpointOptions
{
    // ... mevcut alanlar
    public IList<string> AllowedOrigins { get; } = [];
}
```

`ToolRegistry`, `McpTenantTools`, `McpToolRegistry` gövdeleri de `AIFunctionDeclaration`'a
genişledi (aynı imza kayması, `IToolRegistry`'nin gerçekleştirdiği sözleşmenin doğal sonucu).

## Dosya Listesi (gerçekleşen)

```
src/AgentPrism.Abstractions/Tools/
├── IToolRegistry.cs                       (AITool -> AIFunctionDeclaration)
├── ToolDescriptor.cs                      (RunsOnClient alanı)
└── AgentPrismToolRegistration.cs          (AITool -> AIFunctionDeclaration)

src/AgentPrism.Core/Tools/
├── ToolRegistry.cs                        (AIFunctionDeclaration + onay-guard)
└── AgentPrismClientToolExtensions.cs      (yeni — AddClientTool)

src/AgentPrism.Mcp/
├── Internal/McpTenantTools.cs             (AIFunctionDeclaration + onay-guard)
└── McpToolRegistry.cs                     (AIFunctionDeclaration)

src/AgentPrism.AspNetCore/
├── Contracts/AgentContracts.cs            (ClientToolResult + ToolResults)
├── Endpoints/AgentEndpoints.cs            (toolResults işleme: erken doğrulama + BuildMessagesAsync)
├── Internal/ClientToolResultResolver.cs   (yeni — eşleştirme + mesaj kurulumu + boyut sınırı sabiti)
├── Internal/AgentPrismCorsMiddleware.cs   (yeni — AddCors() olmadan elle CORS)
├── AgentPrismEndpointOptions.cs           (AllowedOrigins)
└── AgentPrismEndpointRouteBuilderExtensions.cs (CORS bağlantısı)

src/AgentPrism.UI/frontend/
├── vite.embed.config.ts                   (yeni — ikinci Vite girişi, iife/lib modu)
├── scripts/postbuild-embed.mjs            (yeni — 30 KB bütçe kapısı)
├── scripts/postbuild.mjs                  (embed/ hariç tutuldu)
├── src/embed/                             (yeni)
│   ├── main.ts                            (script tag'i okur, otomatik mount eder)
│   ├── widget.ts                          (Shadow DOM widget; sessionId ilk mesajdan önce ayrılır)
│   ├── client.ts                          (fetch sarmalayıcı, akışsız istek)
│   ├── tool-runner.ts                     (ClientToolRunner kaydı)
│   ├── locale.ts + locale.tr.ts           (widget'ın kendi küçük sözlüğü, tr ayrı dosyada — K-408)
│   ├── client.test.ts, tool-runner.test.ts
├── src/lib/types.ts                       (ToolDescriptor.runsOnClient)
├── src/screens/tools.tsx, agent-editor.tsx (runsOnClient rozeti)
└── src/locales/{en,tr}.ts                 (yeni anahtarlar)

tests/
├── AgentPrism.Core.UnitTests/Tools/ToolRegistrationTests.cs          (4 yeni test)
├── AgentPrism.Core.UnitTests/Architecture/SourceLanguageTests.cs     (embed/locale.tr.ts istisnası)
├── AgentPrism.AspNetCore.FunctionalTests/ClientToolEndpointTests.cs  (yeni — 9 test)
├── AgentPrism.AspNetCore.FunctionalTests/ClientToolGuardTests.cs     (yeni — 1 test)
├── AgentPrism.AspNetCore.FunctionalTests/CorsOptionsTests.cs         (yeni — 5 test)
├── AgentPrism.Compilation/AgentDefinitionValidatorTests.cs           (MutableToolRegistry imza güncellendi)
└── AgentPrism.Ui.E2ETests/
    ├── Infrastructure/UiHost.cs           (client-tool-agent + configureApp parametresi)
    └── UiTests.cs                         (3 yeni test — rozet, statik varlık, tam etkileşim)

samples/AgentPrism.Api/
├── Program.cs                             (read_shopping_cart client tool + CORS config)
└── appsettings.json                       (AgentPrism:Ui:AllowedOrigins)

docs/
├── manuel-test/26-ISTEMCI-TOOLLARI-VE-GOMULEBILIR.md  (yeni — 13 case)
└── manuel-test/00-INDEKS.md               (satır 26)

docs-site/src/content/docs/
├── guides/client-side-tools.md            (yeni)
├── ui.md, capabilities.md, concepts/tools.md, reference/configuration.md (güncellendi)
└── astro.config.mjs                       (sidebar satırı)
```

## Denetim Bulguları

`faz-denetim` taze bağlamlı bir agent ile koşuldu (çalışma ağacı, `main`'e göre).

### 🔴 — ikisi de düzeltildi

| # | Bulgu | Sonuç |
|---|---|---|
| 1 | `SourceLanguageTests` kırmızıydı: `embed/locale.ts` içindeki Türkçe metin taban çizgisini büyütüyordu, K-408'in tek istisnası (`locales/tr.ts`) bunu kapsamıyordu. | **Düzeltildi.** Türkçe kısım `embed/locale.tr.ts`'e taşındı; `SourceLanguageTests.SkippedFiles`'e eklendi (aynı K-228 gerekçesi, aynı dar kapsam). Doğrulandı: 795/795 test geçti. |
| 2 | Gömülebilir bileşen hiçbir zaman `sessionId` almıyordu (`sessionId: null` sabit kalıyordu, sunucu yalnız gönderileni yankılıyor); istemci tool çağrısı asla yanıtlanamazdı — fazın var oluş sebebi çalışmıyordu. | **Düzeltildi (K-443).** Widget artık `sessionId`'yi kurucuda `crypto.randomUUID()` ile ayırıyor, ilk mesajdan önce. **Gerçek bir tarayıcıda** doğrulandı: yeni `UiTests.Embed_widget_runs_a_client_side_tool_and_completes_the_turn_in_a_real_browser` testi widget'ı Shadow DOM üzerinden tıklar, `RunsWrite` kapsamlı gerçek bir API anahtarıyla tur uçtan uca tamamlanır. |

### 🟡 — kapandı veya gerekçelendi

| # | Bulgu | Sonuç |
|---|---|---|
| 3 | `tool.ShouldNotBeOfType<AIFunction>()` hiçbir koşulda düşemez (Shouldly tam tip eşitliği denetler, `AIFunction` `abstract`'tır). | **Düzeltildi.** `ShouldNotBeAssignableTo<AIFunction>()`'a çevrildi. |
| 4 | Planlanan `ClientToolConcurrencyTests` (aynı `callId` için yarış) yazılmadı. | **Gerekçelendi.** Yarış penceresi tek süreç içi, mikrosaniye mertebesinde; yapay gecikme eklemeden deterministik bir test yazmak SUT'a test-özel kod sızdırırdı. Davranış K-439'da açıkça tarif edildi (ikinci eşleşme kaybolursa sonuç sessizce düşer, model bir sonraki turda tekrar sorabilir) — güvenlik riski değil, en kötü ihtimalle bir kullanıcı deneyimi tekrarı. `docs/ADAYLAR.md`'ye F-NN olarak not düşülmedi çünkü ayrı bir yetenek değil, mevcut mekanizmanın test derinliği. |
| 5 | Varsayılan (akış/SSE) yol `toolResults` için hiç sınanmıyordu; K-439'un TEK gerekçesi o yoldur. | **Düzeltildi.** `ClientToolEndpointTests.Streaming_path_client_tool_call_and_result_round_trip` eklendi — `Idempotency-Key` YOK, gerçek SSE çerçeveleri okunuyor. |
| 6 | CORS politikası `/run`'a değil `{prefix}`'in tamamına uygulanıyor; bir origin'e verilen erişim yönetim API'sinin tamamını kapsıyor. | **Gerekçelendi ve dokümante edildi.** `AllowedOrigins`'in XML dokümanına ve `guides/client-side-tools.md`'ye açık uyarı eklendi: CORS başlığı okuma İZNİ verir, kimlik doğrulamayı ATLAMAZ — gerçek sınır hâlâ API anahtarının kapsamıdır. Path bazlı CORS kısıtlaması ayrı bir karmaşıklık/esneklik dengesi ister, bu fazın kapsamı dışında bırakıldı. |
| 7 | `endpoints` bir `IApplicationBuilder` değilse CORS sessizce hiçbir şey yapmaz, log yazmaz. | **Gerekçelendi.** Aynı dosyada AYNI koşullu desen (idempotency arabellekleme, JSON bağlama orta katmanı) zaten log YAZMADAN aynı şekilde çalışıyor — bu davranış Faz 61'e özgü değil, dosyanın var olan emsaliyle tutarlı. |
| 8 | `AgentDefinitionCompiler.cs`'teki yorum bayattı ("registry only ever returns AIFunction"). | **Düzeltildi.** Koda göre güncellendi. |
| 9 | DoD satırı "sözleşme testi, dört koşum" harfiyen karşılanmadı — tek bir bellek içi fonksiyonel test teslim edildi. | **Gerekçelendi.** `toolResults`'un kiracı sınırı YENİ bir depo YOLU açmıyor; `AgentSessionManager` → `ISessionStore` üzerinden akıyor ve `SessionStoreContract : TenantIsolationContract<ISessionStore>` bu sınırı ZATEN dört sağlayıcıda kanıtlıyor. Planın satırı yanlış planlanmıştı — yeni kod bu sınırı yeniden AÇMADI, yalnız MEVCUT kanıtlanmış sınırı kullandı. |
| 10 | `ui.md`'nin `AllowedOrigins` bağlantısı `reference/configuration/`ye gidiyordu ama o sayfada satır yoktu. | **Düzeltildi.** Tabloya satır eklendi. |
| 11 | K-441 var olmayan bir bölüme atıf yapıyordu ("bkz. fazın DoD bölümü"). | **Düzeltildi** — bu kapanışla birlikte DoD bölümü artık gerçek kanıt taşıyor. |

### 🟢 — `docs/ADAYLAR.md`'ye

| # | Bulgu | Neden şimdi değil |
|---|---|---|
| 12 | `RunReplayService`'in `toolTransform`'u yalnız `AIFunction`'lara uygulanıyor; istemci tool'u taşıyan bir `run` sadık biçimde replay edilemez. | Replay bu fazın kapsamı dışında; **`docs/ADAYLAR.md`'ye F-109 olarak eklendi.** |
| 13 | Tek istekteki `toolResults` dizisinde aynı `callId` iki kez geçerse `400` (Unknown) döner, `409` değil. | Davranış güvenli (ikinci kez kabul edilmiyor), yalnız durum kodu sözleşmeyle tam tutarlı değil; küçük bir iyileştirme. |
| 14 | `MatchAsync` iki kez koştuğu için `toolResults` taşıyan her istek oturumu iki kez çözüyor. | Maliyeti ölçülmedi; K-439 bu çıkış yolunu zaten bilerek seçti. |

**Temiz çıkan başlıklar:** 3.5 (imza-gövde kayması — tüm tüketiciler izlendi), 3.6 (plan dışı public API yok).

## Sonraki Faza Devir Notu

**Devralınan sözleşmeler:**
- `IToolRegistry.TryGet` artık `AIFunctionDeclaration` döner — yeni bir tool
  tüketicisi (`AIFunction` bekleyen eski kod) derleme hatası alır, bu bilinçli.
- `AgentRunRequest.ToolResults` `Approvals`'ın birebir kardeşi: `sessionId`
  zorunlu, kuyruk yolunda `400`, bilinmeyen/tekrar `callId` `400`/`409`.
- `AgentPrismEndpointOptions.AllowedOrigins` boşsa CORS başlığı hiç gönderilmez;
  `AllowAnyOrigin` API'si YOK ve eklenmemeli (K1).

**🚨 Bilinen tuzaklar:**
- `services.AddCors()` `MapAgentPrism()` içinde çağrılamaz (`app.Build()`'den
  sonra, DI kabı mühürlü). Yeni bir DI-bağımlı ASP.NET Core özelliği
  `MapAgentPrism()`'e eklenecekse aynı kısıt geçerlidir — `AgentPrismCorsMiddleware.cs`'teki
  elle kurulum deseni örnek alınabilir.
- `ClientToolResultResolver.MatchAsync`'in NEDEN iki kez çağrıldığını
  (K-439) anlamadan bu kodu "sadeleştirmeye" çalışma — SSE başlıklarının
  akış başlamadan gönderildiği fiziksel kısıt hâlâ geçerli.
- Gömülebilir bileşenin `sessionId`'yi kurucuda hemen ayırması (K-443) rastgele
  değil: sessionsiz bir `run` geçmiş TUTMAZ, dolayısıyla bekleyen bir tool
  çağrısı asla bulunamaz. Widget'a yeni bir "ilk mesaj" yolu eklenirse bu
  invariant korunmalı.
- Widget vanilla TS + Shadow DOM + CSS-in-JS; React/Tailwind YOK. Yeni bir
  widget özelliği eklenirken 30 KB bütçesi (`postbuild-embed.mjs`) ilk
  kontrol edilecek şeydir.

**Yarım kalan/kapsam dışı bırakılan işler:** F-109 (`docs/ADAYLAR.md` — replay), ve yukarıdaki 🟢 kalemler 13-14 (küçük, aday gerektirmeyen iyileştirmeler).

**Sıradaki faz:** `docs/ADAYLAR.md`'den seçilecek; F-108 ve F-64 bu fazla
kapandı.
