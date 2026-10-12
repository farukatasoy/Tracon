# Faz 195 — Workflow Düğüm Düzenleme ve Fonksiyon Düğümü Ayarı

> **Durum:** ✅ Tamamlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-12 (engelleyici kararlar sohbette alındı: F-292 + F-295 tek faz; tipli ayar; son fonksiyon düğümü silinince `agentNames`'e dönüş; konsolda JSON alanı + şema yardımı; aynı fonksiyonun zincirde iki kez kullanımı kapsam dışı)
> **Kaynak:** [ADAYLAR.md](../../ADAYLAR.md) · **F-292**, **F-295**
> **Önkoşul:** [Faz 194](194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) — **işlevsel bağımlılık yok**, ama dört dosyayı paylaşırlar (`workflow-editor.tsx`, `WorkflowContracts.cs`, `WorkflowDefinitionPayload.cs`, `full-replace-editors.test.tsx`). İki yazar aynı dosyaya aynı anda dokunmasın diye uygulama Faz 194 kapandıktan sonra başlar
> **Paketler:** `Tracon.Abstractions`, `.Core`, `.Workflows`, `.AspNetCore`, `.Sql.Shared`, `.UI` · üretilen: `Tracon.Client`, `@tracon/client`
> **Yeni paket:** Yok · **Migration:** Yok — workflow tanımı `workflows.definition` JSON blob'unda durur (`0007_workflows.sql`); yeni alan blob'a girer
> **Public API:** büyüyor — `1.0.0` GA'dan (K-603) önce ucuz. Ölçüldü (2026-10-12): `wc -l src/*/PublicAPI.Shipped.txt` → 17 dosya, her biri yalnız `#nullable enable` satırı
> **Tüketici yüzeyi:** site: `concepts/workflows.md` (§ *Function nodes*; `:96-98`'deki "console cannot change it yet" cümlesi), `ui.md` § *Workflows* + ekran görüntüsü, `capabilities.md` (`api/` ve `http-api/` üretilir) · sevk edilen: yeni `AddWorkflowFunction<TInput, TOutput, TSettings>` aşırı yüklemesinin XML `<example>`'ı, `src/Tracon.Workflows/README.md`, `CHANGELOG.md` `[Unreleased]`
> **Manuel test alanı:** `docs/manuel-test/15-WORKFLOWS.md`

---

## Bu Faza Başlarken

> `faz-baslangic` skill'ini uygula. Aşağıdaki liste o skill'in 2. adımıdır —
> **tamamını değil, yalnız işaret edilen bölümleri oku.**

1. Bu doküman
2. Kararlar — dosyanın tamamını **okuma**, yalnız bu kalemleri grep'le:
   ```bash
   grep -n "K-059\|K-495\|K-496\|K-497\|K-580\|K-725" docs/KARARLAR.md
   ```
   **K-495** (karışık zincirde agent düğümü `WorkflowAgentStepExecutor` olarak
   bağlanır — iki derleme yolunun farkı buradan gelir), **K-496** (fonksiyon adı
   kayıtta doğrulanır, çünkü kayıt süreç boyunca sabittir — ayar şeması da aynı
   gerekçeyle kayıtta doğrulanır), **K-497** (düğüm zaman aşımı kapsam dışı),
   **K-580** (`*Payload` jsonb izdüşümü elle senkronlanır), **K-725** (`PUT`
   tam değiştirme; istek tüketiciye ait her alanı taşır), **K-059** (`secret`
   veritabanına yazılmaz — ayar blob'u da veritabanındadır)
3. Faz devir notları — yalnız bu bölümler:
   ```bash
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/71-WORKFLOW-KOD-DUGUMU.md
   awk '/## Sonraki Faza Devir Notu/,0' docs/arsiv/fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md
   ```
   Faz 71 fonksiyon düğümünü kurdu ve editörün yazma tarafını bilerek bıraktı
   (Sapma #6). Faz 194 aynı editöre `parameters` korumasını ve fonksiyon
   düğümüne `GetRunParameters()`'ı ekledi. Faz 194 henüz kapanmadıysa bu faz
   başlamaz (Önkoşul).
4. Alan hafızası (üç alan):
   [`hafiza/workflows.md`](../../hafiza/workflows.md) (executor kimliği, karma zincir) ·
   [`hafiza/frontend.md`](../../hafiza/frontend.md) (tam değiştiren editör kapısı) ·
   [`hafiza/frontend-test-altyapisi.md`](../../hafiza/frontend-test-altyapisi.md)
   (`openapi-fetch` stub)

---

## Amaç

Bir operatör bugün fonksiyon düğümlü bir workflow'u konsoldan kuramaz ve
değiştiremez; zincir salt okunurdur ve `PUT /api/workflows/{name}` ister. Aynı
fonksiyonu iki workflow'da farklı ayarla (ses, LUFS hedefi, TTS sağlayıcısı)
kullanmak da kodda ayrı kayıt ister. Bu faz iki eksiği birlikte kapatır: konsol
zinciri kurar ve sıralar, fonksiyon düğümü kodun tanımladığı tipte bir ayar
alır. Ayar **veridir, ifade değildir** (K2): şemayı kod verir, operatör yalnız
değer yazar.

- **F-292** — konsol workflow editörü düğüm listesini kurar: agent ve fonksiyon
  ekleme, sıralama, silme; fonksiyon seçici `GET /api/workflows/functions`'tan
  beslenir.
- **F-295** — fonksiyon düğümü başına `Settings` (JSON); kodda tipli şema
  (`TSettings`); kayıtta doğrulama; fonksiyon ayarı tipli nesne olarak alır.

İki kalem aynı yüzeyi paylaşır: `WorkflowNodeReference`, düğüm listesi ve
editörün düğüm bölümü. Biri olmadan diğeri yarım kalır — ayar API'den yazılır
ama konsolda görünmez, ya da konsol zinciri kurar ama düğümü ayarlayamaz.

### Kapsam dışı

| Kalem | Neden |
|---|---|
| Aynı fonksiyonun bir zincirde iki kez kullanımı | Kullanıcı kararı (2026-10-12). Executor kimliği düğüm adıdır (`WorkflowDefinitionCompiler.cs:148-152`); tekrar eden ad reddedilir (`WorkflowDefinitionValidator.cs:86`). Gerekirse kodda ikinci ad kaydedilir |
| Ayar değerinde `{{param}}` yerleştirme | Faz 194 parametreyi talimat dışı alana bağlamayı kapsam dışı bıraktı (K2 komşusu). Fonksiyon run değerini `context.GetRunParameters()` ile kendisi okur |
| Agent düğümüne ayar | Agent'ın davranışı kendi tanımındadır; ayar yalnız fonksiyon düğümünde. Agent düğümünde `settings` → `400` |
| Şemadan üretilen form | Kullanıcı kararı: JSON alanı + şema yardımı. Form, ölçülmüş bir kullanım sıkıntısı olursa ayrı aday |
| Düğüm zaman aşımı | F-303'ün işidir (K-497) |
| Sequential dışı desende fonksiyon düğümü | MAF'ın hazır kurucuları yalnız agent kabul eder (Faz 71 sınırı) |
| Konsolda parametre **şeması** düzenleme | Faz 194 kapsam dışı bıraktı; editör yalnız korur |

### Bugün ne çalışmıyor — doğrulanmış kanıt

| Kanıt | Gözlem |
|---|---|
| [`WorkflowNodeReference.cs:22-34`](../../../src/Tracon.Abstractions/Workflows/WorkflowNodeReference.cs) | Düğüm yalnız `Name` ve `Kind` taşır |
| [`TraconWorkflowFunctionExtensions.cs:94-134`](../../../src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs) | İşleyici `(TInput, IWorkflowContext, CancellationToken)` alır; ayar yolu yok |
| [`WorkflowFunctionRegistration.cs:21-22`](../../../src/Tracon.Workflows/Internal/WorkflowFunctionRegistration.cs) · [`WorkflowFunctionRegistry.cs:75`](../../../src/Tracon.Workflows/Internal/WorkflowFunctionRegistry.cs) | Executor fabrikası yalnız `executorId` alır |
| [`WorkflowDefinitionCompiler.cs:146-153`](../../../src/Tracon.Workflows/Internal/WorkflowDefinitionCompiler.cs) | Fonksiyon düğümünün executor kimliği düğüm **adıdır**; checkpoint uyumu buna dayanır |
| [`WorkflowCatalog.cs:122`](../../../src/Tracon.Workflows/Internal/WorkflowCatalog.cs) | Graf **her run'da** yeniden kurulur — ayar her run'da yeniden okunabilir |
| [`WorkflowFunctionDescriptor.cs:4-16`](../../../src/Tracon.Abstractions/Workflows/WorkflowFunctionDescriptor.cs) · [`WorkflowContracts.cs:26-35`](../../../src/Tracon.AspNetCore/Contracts/WorkflowContracts.cs) | Katalog yalnız ad, açıklama, girdi/çıktı tip adı verir; şema yok |
| [`WorkflowEndpoints.cs:53-66`](../../../src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs) | 🚨 Uç açıklaması "the UI lets users pick from this list" der; `grep -rn "workflows/functions" src/Tracon.UI/frontend/src` **boş** — doküman koddan sapmış, bu faz doğru kılar |
| [`WorkflowEndpoints.cs:348-367`](../../../src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs) | Kayıtta yalnız fonksiyon adının varlığı kontrol edilir (K-496) |
| [`workflow-editor.tsx:36-45`](../../../src/Tracon.UI/frontend/src/screens/workflow-editor.tsx) · [`:404-430`](../../../src/Tracon.UI/frontend/src/screens/workflow-editor.tsx) | `nodes` "carried, never edited"; `NodeChain` salt okunur |
| [`locales/en/workflows.ts:79`](../../../src/Tracon.UI/frontend/src/locales/en/workflows.ts) · [`concepts/workflows.md:96-98`](../../../docs-site/src/content/docs/concepts/workflows.md) | Konsol ve site "zinciri değiştiremez, HTTP API değiştirir" der |
| [`full-replace-editors.test.tsx:53-57`](../../../src/Tracon.UI/frontend/src/full-replace-editors.test.tsx) | `Complete<T>` **derin**dir — düğüme eklenen `settings` her fixture'da zorunlu olur |
| [`IStructuredResponseValidator.cs:22-24`](../../../src/Tracon.Abstractions/Agents/IStructuredResponseValidator.cs) | Tracon bilerek yerleşik JSON Schema doğrulayıcısı taşımaz |
| [`Tracon.Workflows.csproj:10`](../../../src/Tracon.Workflows/Tracon.Workflows.csproj) | `TraconAotCompatible=false` — tipli ayarın yansımalı deserialize'ı bu pakette serbest |

**Düşen aday iddiası:** F-295'in "Hazırlık" alanı "JSON şema doğrulama deseni
var (`AgentParameter`)" diyordu. `AgentParameter` düz bir ad/tür listesidir,
JSON Schema değildir; repo'da JSON Schema doğrulayıcısı yoktur (yukarıdaki
satır). Tasarım bu yüzden tipli deserialize'a dayanır (§195.2).

> Kanıtlar 2026-10-12 tarihinde doğrulandı.

---

## 195.1 — Düğüm ayarı: veri modeli

`WorkflowNodeReference` yeni bir alan alır: `Settings` (`JsonElement?`).

- 🚨 **`JsonElement?`, `JsonElement` değil.** Atanmayan `JsonElement`
  `Undefined` kalır ve seri hâle getirme çöker; etki o kaydı içeren **liste
  ucunun tamamına** yayılır (`MEMORY.md`). `null` = "ayar yok". Init erişimcisi
  `Undefined`'ı `null`'a çevirir (`FreeFormJson` deseni, `JobRequest.Payload`).
- **Yalnız nesne.** Ayar bir JSON nesnesidir; dizi veya skaler kayıtta `400`.
- 🚨 **K-580:** `WorkflowDefinitionPayload` düğüm listesini `WorkflowNodeReference`
  olarak taşır (`WorkflowDefinitionPayload.cs:36`, `:60`, `:81`). Alan kendiliğinden
  akmalıdır ama source-generated `TraconJsonContext` (`Tracon.Sql.Shared`) ile
  ölçülmeli. `WorkflowDefinitionStoreContract` tam alan round-trip testi
  ayarlı bir düğümü kapsar.
- **Migration yok:** tanım `workflows.definition` blob'undadır.
- 🚨 **`secret` (K-059):** ayar veritabanına yazılır. Ayar bir `secret` değeri
  tutmaz; gerekiyorsa yapılandırma anahtarının **adını** tutar. Tracon
  tüketicinin `TSettings` tipini bilemez; kural XML'de, sitede ve aşırı
  yüklemenin `<example>`'ında yazılır.

## 195.2 — Kodda tipli şema ve doğrulama

Yeni aşırı yükleme `TSettings` alır. Mevcut iki parametreli aşırı yükleme
değişmez (K1 — sıfır sürpriz):

```csharp
builder.AddTracon()
       .UseWorkflows()
       .AddWorkflowFunction<List<ChatMessage>, AudioClip, TtsSettings>(
           "tts",
           provider => (messages, settings, context, cancellationToken) =>
               provider.GetRequiredService<ITts>().SpeakAsync(messages[^1].Text, settings.Voice, settings.LoudnessLufs, cancellationToken),
           description: "Text to speech");

public sealed record TtsSettings
{
    public required string Voice { get; init; }
    public double LoudnessLufs { get; init; } = -16;
}
```

- **Doğrulama = katı deserialize.** Kayıt anında düğümün ayarı `TSettings`'e
  deserialize edilir: bilinmeyen alan reddedilir
  (`JsonUnmappedMemberHandling.Disallow`), C# `required` üyesi eksikse
  reddedilir. Hata mesajı düğüm adını ve alanı adlandırır. Fonksiyonun göreceği
  nesne tam olarak doğrulanan nesnedir — iki ayrı kural yoktur.
  **Ölçülmeli:** `net8.0` hedefindeki geçişli System.Text.Json sürümünde
  `RespectNullableAnnotations` var mı; yoksa yalnız yukarıdaki iki kural
  kullanılır (Faz 193 `net8.0`'ı düşürürse sınır kalkar).
- **Ayar yoksa** `{}` deserialize edilir. `required` üyesi olan bir `TSettings`
  için ayarsız düğüm kayıtta reddedilir — operatör eksik ayarı ilk run'da değil,
  kaydederken görür.
- **Ayarsız kayıt** (iki parametreli aşırı yükleme): düğüm ayar taşırsa `400`
  ("function '{ad}' declares no settings").
- **Kayıtta doğrulamanın gerekçesi:** K-496 — fonksiyon kaydı süreç ömrü
  boyunca sabittir; şema da koddadır. Erken kontrol dürüst bir güvencedir
  (agent varlığının aksine).
- **Derlemede yeniden doğrulama.** Dağıtımlar arasında `TSettings` değişebilir;
  kayıtlı ayar yeni tipe uymayabilir. Derleyici (`CreateExecutor`) aynı
  deserialize'ı yapar; hata run'ı ilk düğümden önce `Failed` bitirir ve düğümü
  adlandırır.
- **Şema dışa aktarımı:** `AIJsonUtilities.CreateJsonSchema(typeof(TSettings), …)`
  (Microsoft.Extensions.AI). **Ölçüldü (2026-10-12, `maf-api-kesfi`):** imza
  `static JsonElement CreateJsonSchema(Type, string, bool, object,
  JsonSerializerOptions, AIJsonSchemaCreateOptions)`. Şema kayıt anında bir kez
  üretilir ve tanımlayıcıda tutulur; yeni bağımlılık yoktur. Şema **yalnız
  bilgi içindir** — doğrulamayı deserialize yapar, şema değil.
- **Kayıt yolu (iç):** `WorkflowFunctionRegistration` fabrikası
  `Func<string, JsonElement?, Executor>` olur. Ayar executor kurulurken bir kez
  deserialize edilir ve işleyiciye kapanışla bağlanır; `WorkflowNodeRetry.Wrap`
  bu sarılı işleyiciyi sarar (yeniden deneme aynı ayarı görür).
- **Canlı güncelleme:** graf her run'da kurulur (`WorkflowCatalog.cs:122`);
  ayar değişikliği **sonraki run**'da geçerlidir. `AwaitingInput` bir run'ın
  resume'u güncel tanımı derler, yani yeni ayarı görür. Executor kimliği ayara
  bağlı değildir; checkpoint uyumu değişmez. Site bu davranışı yazar.

## 195.3 — Katalog ve HTTP

- `WorkflowFunctionDescriptor` iki alan alır: `SettingsType` (`Type?`) ve
  `SettingsSchema` (`JsonElement?`).
- `WorkflowFunctionResponse` (`GET /api/workflows/functions`) `settingsSchema`
  taşır. Uç açıklaması (`WorkflowEndpoints.cs:53-66`) bu fazla doğru olur;
  metin yeniden okunur.
- Kayıt ucu (`SaveAsync`, `:348-367`) fonksiyon adı kontrolünün yanına ayar
  doğrulamasını ekler. Doğrulamaya erişim yolu: Açık Soru 1.
- Agent düğümünde `settings` → `400`.

## 195.4 — Düğüm listesinin biçimi: `nodes` ↔ `agentNames`

Kullanıcı kararı (2026-10-12): **tek kanonik biçim.**

| Zincir | Kaydedilen biçim | Derleme yolu |
|---|---|---|
| En az bir fonksiyon düğümü | `nodes` | Karma zincir — agent `WorkflowAgentStepExecutor` (K-495) |
| Yalnız agent | `agentNames` | Hazır `AgentWorkflowBuilder.BuildSequential` |

- **Dönüşüm kaydetme anında, istemcide** yapılır: editör son fonksiyon düğümü
  silinince zinciri `agentNames` olarak gönderir; ilk fonksiyon eklenince
  `nodes`. Dönüşüm kaydetmeden önce bir bilgi satırıyla gösterilir (yol değişir,
  token ayrıntısı değişir).
- **Sunucu bugünkü kuralı korur:** yalnız-agent `nodes` geçerli kalır
  (`WorkflowDefinitionValidator` bunu reddetmez). Sunucuda reddetmek, API ile
  kaydedilmiş mevcut tanımları kırardı. Kanonikleştirme yalnız konsolun
  yazdığı tanımlar içindir. Site iki biçimi ve farkını yazar.
- 🚨 **Checkpoint uyumu:** biçim değişimi executor kimliklerini değiştirir
  (karma zincirde kimlik düğüm adı; hazır desende `{ad}_{AIAgent.Id}` türevi,
  `hafiza/workflows.md`). Yapısal her düzenleme gibi, bekleyen bir run bu
  değişiklikten sonra resume edilemeyebilir. Editör, yapısal değişiklikte bu
  riski bir satırla söyler. **Ölçülmeli:** bugün agent ekleme/çıkarma da
  aynı sonucu veriyor mu — veriyorsa uyarı genel yapısal değişiklik uyarısıdır.

## 195.5 — Konsol editörü

- **Düğüm listesi** (`Sequential` desen): agent ekle (katalog), fonksiyon ekle
  (`GET /api/workflows/functions`), yukarı/aşağı taşı, sil. Mevcut
  `AgentPicker`'ın sıralama/silme deseni (`workflow-editor.tsx:436-537`, `swap` dahil)
  düğüm listesine genişler; iki ayrı liste bileşeni yazılmaz.
- **Desen kısıtı:** fonksiyon seçici yalnız `Sequential`'da görünür. Zincirde
  fonksiyon varken desen değiştirilemez; seçici bir satırla nedenini söyler.
- **Ayar alanı:** her fonksiyon düğümünde JSON metin alanı ve şemanın okunur
  özeti (alan adları, türleri, `required`). Doğrulama sunucunun `400`
  mesajıyla; editör doğrulayıcı kopyası taşımaz (editörün mevcut ilkesi,
  `workflow-editor.tsx:77-80`). İstemci yalnız "JSON olarak ayrıştırılamıyor"
  durumunda kaydı devre dışı bırakır.
- **Tam değiştirme kapısı:** `Complete<WorkflowSaveRequest>` derindir; ayarlı
  bir düğüm fixture'ı eklenir. Ayar korunmazsa `tsc` düşer.
- **Metin:** `workflowEditor.nodesReadOnly` kalkar; yeni anahtarlar
  `locales/en` + `locales/tr` (K-228). Sunucu hataları çevrilmez (K-232).
- **Graf görünümü** değişmez (Faz 71 ayırt edici fonksiyon şeklini kurdu).

---

## Planlanan Public API

> Taslak imzalardır. Gerçekleşen imzalar kapanışta ayrı bir bölüme yazılır.
> Hepsi `sealed record`'a alan veya statik sınıfa aşırı yükleme ekler: **bugün
> eklemek bedava / `1.0.0` GA sonrası kırıcı** (record alanları için).

```csharp
// Tracon.Abstractions
public sealed record WorkflowNodeReference
{
    /// <summary>
    /// Gets the function node's settings: a JSON object validated against the
    /// function's settings type. <see langword="null"/>: no settings. Only for
    /// <see cref="WorkflowNodeKind.Function"/>.
    /// </summary>
    public JsonElement? Settings { get; init; }
}

public sealed record WorkflowFunctionDescriptor
{
    /// <summary>Gets the settings type the function declares, or <see langword="null"/>.</summary>
    public Type? SettingsType { get; init; }

    /// <summary>Gets the JSON schema of <see cref="SettingsType"/>, for display only.</summary>
    public JsonElement? SettingsSchema { get; init; }
}

// Tracon.Workflows
public static partial class TraconWorkflowFunctionExtensions
{
    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput, TSettings>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, TSettings, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        string? description = null,
        WorkflowNodeRetryPolicy? retryPolicy = null)
        where TSettings : class;
}
```

`Tracon.AspNetCore`: `WorkflowFunctionResponse.SettingsSchema`.
`WorkflowSaveRequest.Nodes` zaten `WorkflowNodeReference` taşır
(`WorkflowContracts.cs:62`); alan oradan akar. `IWorkflowFunctionCatalog`'a
üye: Açık Soru 1.

### HTTP `endpoint`'leri

| Metot | Yol | Rol | Ne yapar |
|---|---|---|---|
| `GET` | `/api/workflows/functions` | Reader | Yanıt `settingsSchema` taşır; konsol seçicisi buradan beslenir |
| `PUT` | `/api/workflows/{name}` | Admin | Düğüm `settings`'i fonksiyonun tipine karşı doğrulanır; agent düğümünde ve ayarsız fonksiyonda `400` |

Roller bugünkü rollerdir; bu faz rol değiştirmez. Ölçüldü (2026-10-12):
`WorkflowEndpoints.cs:54,92`.

### Arayüz payı

Ölçüldü (2026-10-12, Faz 194 kapanışı): JS bundle **197,7 KB gzip**, bütçe 250 KB
(Faz 194 öncesi 197,4 KB; payı 0,3 KB). Taban budur. Bu
fazın payı düğüm listesi, fonksiyon seçici ve JSON alanıdır — şema
yorumlayıcısı yoktur. Kapanışta ölçülür ve yazılır.

---

## Planlanan Dosya Listesi

```
src/Tracon.Abstractions/
├── Workflows/WorkflowNodeReference.cs         (Settings)
├── Workflows/WorkflowFunctionDescriptor.cs    (SettingsType, SettingsSchema)
└── Workflows/IWorkflowFunctionCatalog.cs      (Açık Soru 1)
src/Tracon.Core/
└── Workflows/WorkflowDefinitionValidator.cs   (agent düğümünde ayar, nesne olmayan ayar)
src/Tracon.Workflows/
├── TraconWorkflowFunctionExtensions.cs        (yeni aşırı yükleme)
├── Internal/WorkflowFunctionRegistration.cs   (fabrika ayar alır)
├── Internal/WorkflowFunctionRegistry.cs       (deserialize, şema, doğrulama)
├── Internal/WorkflowFunctionSettings.cs       (yeni — katı seçenekler ve hata metni, tek yer)
└── Internal/WorkflowDefinitionCompiler.cs     (CreateExecutor'a ayar)
src/Tracon.AspNetCore/
├── Contracts/WorkflowContracts.cs             (WorkflowFunctionResponse.SettingsSchema)
└── Endpoints/WorkflowEndpoints.cs             (kayıtta ayar doğrulaması, uç açıklaması)
src/Tracon.Sql.Shared/
└── Internal/WorkflowDefinitionPayload.cs      (K-580 — ölçülür, gerekiyorsa değişir)
src/Tracon.UI/frontend/src/
├── screens/workflow-editor.tsx                (düğüm listesi, seçici, ayar alanı, kanonik biçim)
├── screens/workflow-editor.test.tsx
├── full-replace-editors.test.tsx              (ayarlı düğüm fixture'ı)
└── locales/{en,tr}/workflows.ts
samples/Tracon.Api/Program.cs                   (ayarlı örnek fonksiyon)
```

---

## Hata Modları ve Testler

> Mutlu yoldan değil, **ne bozulabilir**den türetilir. Seviyeyi plan seçer.
> Sınır geçen davranış (DI · HTTP · kiracı · akış · depo · paket) birim
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../../../.agents/ortak/test-seviyeleri.md).

| # | Ne bozulabilir | Seviye | Test sınıfı |
|---|---|---|---|
| 1 | Ayar fonksiyona ulaşmaz veya yanlış düğüme gider | Fonksiyonel (gerçek HTTP, iki ayarlı düğüm) | `WorkflowFunctionSettingsTests` |
| 2 | Bilinmeyen alan / eksik `required` / nesne olmayan ayar kayıtta kabul edilir | Fonksiyonel (`PUT` → `400`, mesaj düğümü adlandırır) | `WorkflowFunctionSettingsValidationTests` |
| 3 | Ayarsız kayıtlı fonksiyona veya agent düğümüne ayar yazılır | Fonksiyonel | `WorkflowFunctionSettingsValidationTests` |
| 4 | `TSettings` dağıtımlar arasında değişir; eski ayar run'da sessizce `default` olur | Fonksiyonel (kayıttan sonra tip değişir) | `WorkflowFunctionSettingsDriftTests` |
| 5 | `JsonElement` `Undefined` kalır, `GET /api/workflows` listesi çöker | Fonksiyonel (ayarsız + ayarlı düğüm aynı listede) | `WorkflowFunctionSettingsTests` |
| 6 | Payload izdüşümü ayarı düşürür (K-580) | Sözleşme (dört depo) | `WorkflowDefinitionStoreContract` |
| 7 | Yeniden deneme farklı ayar görür (retry sarmalı ayardan önce kurulur) | Birim (`WorkflowNodeRetry`) + Fonksiyonel | `WorkflowFunctionSettingsRetryTests` |
| 8 | **Eşzamanlılık:** iki run aynı executor örneğini ve ayarı paylaşır | Fonksiyonel (paralel iki run, farklı tanım sürümü) | `WorkflowFunctionSettingsConcurrencyTests` |
| 9 | **Başka kiracı:** aynı workflow adı iki kiracıda; ayar karışır | Fonksiyonel | `WorkflowFunctionSettingsTenantTests` |
| 10 | Ayar değişikliği bekleyen run'ın resume'unu bozar | Fonksiyonel (aynı süreçte resume) | `WorkflowFunctionSettingsResumeTests` |
| 11 | Şema dışa aktarımı bir tipte atar ve katalog ucu çöker | Birim + Fonksiyonel (`GET /api/workflows/functions`) | `WorkflowFunctionCatalogSchemaTests` |
| 12 | Editör kaydederken ayarı veya zinciri siler | Vitest (derin `Complete` + round-trip) | `full-replace-editors.test.tsx` |
| 13 | Son fonksiyon silinince `nodes` boş gider; ilk fonksiyon eklenince `agentNames` kalır | Vitest | `workflow-editor.test.tsx` |
| 14 | Sequential dışı desende fonksiyon eklenir veya fonksiyonlu zincirde desen değişir | Vitest | `workflow-editor.test.tsx` |
| 15 | Konsoldan kurulan zincir gerçekte koşmaz | E2E (Playwright: kur → kaydet → çalıştır) | mevcut workflow E2E dosyası — uygulama başında ölçülür |
| 16 | **Boş/aşırı girdi:** boş nesne, çok büyük ayar blob'u | Fonksiyonel | `WorkflowFunctionSettingsValidationTests` |
| 17 | **Alt sistem hatası:** derlemede deserialize atar, run asılı kalır | Fonksiyonel | `WorkflowFunctionSettingsDriftTests` |
| 18 | Ayar değeri log'a veya `run_events`'e düz yazılır | Fonksiyonel (log + olay taraması) | `WorkflowFunctionSettingsTests` |
| 19 | Üretilen istemciler yeni alanları taşımaz | Paket (istemci üretimi kapısı) | mevcut NSwag/TS üretim kapıları |

İptal sorusu: yeni kod yolu iptal noktası eklemez — deserialize senkrondur ve
derlemede yapılır; mevcut run iptal testleri yeterlidir. Uygulamada bu iddia
grep ile ölçülür.

---

## Manuel Kabul Case'leri

> Kapanışta `docs/manuel-test/15-WORKFLOWS.md` içine eklenir; ID'ler o anda
> alınır. Otomatikleştirilebilenler kapanışta koşulur.

| # | Ön koşul | Adımlar | Beklenen sonuç |
|---|---|---|---|
| 1 | Örnek uygulamada ayarlı fonksiyon | Konsol → yeni workflow → `Sequential` → agent ekle → fonksiyon ekle → ayarı yaz → kaydet → çalıştır | Run `Completed`; fonksiyon çıktısı ayarı içerir — 👤 görsel |
| 2 | Aynı workflow | Ayarda bilinmeyen alan → kaydet | Sunucunun `400` mesajı görünür, düğüm ve alan adlı |
| 3 | Aynı workflow | Fonksiyon düğümünü sil → kaydet → `GET /api/workflows/{ad}` | Yanıt `agentNames` taşır, `nodes` boş |
| 4 | Yalnız agent'lı workflow | Fonksiyon ekle → kaydet → `GET` | Yanıt `nodes` taşır, `agentNames` boş |
| 5 | Fonksiyonlu zincir | Deseni `Concurrent`'e çevirmeyi dene | Seçim devre dışı; neden satırı görünür — 👤 görsel |
| 6 | Ayarlı workflow | `PUT` ile ayarı değiştir → yeni run | Yeni run yeni ayarı kullanır |
| 7 | — | `GET /tracon/api/workflows/functions` | Ayarlı fonksiyon `settingsSchema` taşır |

---

## Açık Sorular

> Planı bloklamayan, faz uygulanırken karara bağlanacak sorular.

| # | Soru | Seçenekler | Öneri |
|---|---|---|---|
| 1 | Kayıt ucu ayar doğrulamasına nasıl ulaşır? | A: `IWorkflowFunctionCatalog`'a public üye (`string? ValidateSettings(string name, JsonElement? settings)`) · B: tanımlayıcıda doğrulama temsilcisi · C: iç tip + `InternalsVisibleTo` | **A** — uç bugün aynı arayüzün `Contains`'ini kullanır; tek erişim yolu kalır. Arayüze üye eklemek uygulayıcı için kırıcıdır ama preview'dadır ve tek uygulayıcı iç kayıttır; `CHANGELOG` anar. C, `AspNetCore`'un `Workflows`'a derleme bağımlılığı yoksa çalışmaz — ölçülmeli |
| 2 | Ayar blob'u için boyut sınırı | A: yok · B: `TraconWorkflowOptions` altında tek üst sınır (K-582 deseni) | **B** — tanım tam değiştirme ile yazılır ve her run'da okunur; sınırsız blob tanım listesini şişirir. Varsayılan değer ölçümle seçilir |
| 3 | Editör yapısal değişiklikte (biçim dönüşümü, düğüm ekle/sil) bekleyen run uyarısı göstersin mi? | A: her zaman bir satır · B: yalnız `AwaitingInput` run varsa (sayım çağrısı) | **A** — ek uç istemez; B'nin değeri ölçülürse ayrı aday |

---

## Bitiş Ölçütleri (DoD)

- [x] Konsoldan agent + fonksiyon zinciri kurulur, sıralanır, silinir ve kaydedilir; run koşar (test 15) — ✅ `workflow-editor.test.tsx` (kurma, taşıma, silme) · E2E `A_chain_built_in_the_console_with_function_settings_is_saved_and_runs`; örnek uygulamada konsoldan kuruldu ve `Echo: Hello from the console [spoken by alloy at -12 LUFS]` verdi
- [x] Ayarlı fonksiyon düğümü tipli ayarı alır (test 1) — ✅ `WorkflowFunctionSettingsTests` (gerçek HTTP); örnek uygulamada `[spoken by alloy at -14 LUFS]`
- [x] Bilinmeyen alan, eksik `required`, nesne olmayan ayar ve agent düğümündeki ayar kayıtta `400` döner (test 2-3) — ✅ `WorkflowFunctionSettingsValidationTests` (16 vaka); örnek uygulamada altı ayrı `400` mesajı (düğümü ve üyeyi adlandırır)
- [x] Değişen `TSettings` run'ı ilk düğümden önce adlandırılmış bir hatayla düşürür (test 4) — ✅ `WorkflowFunctionSettingsDriftTests` (4) · `WorkflowFunctionSettingsTests` (birim)
- [x] Son fonksiyon silinince konsol `agentNames` yazar; ilk fonksiyon eklenince `nodes` (test 13) — ✅ `workflow-editor.test.tsx` ("the stored form"); örnek uygulamada `speak` silinince `agentNames: ["support"]`, `nodes: []`, `version: 2`
- [x] `GET /api/workflows/functions` `settingsSchema` taşır; uç açıklaması koda uyar — ✅ `WorkflowFunctionCatalogSchemaTests`; örnek uygulamada şema ve `null`; iki uç açıklaması bu fazla yeniden yazıldı
- [x] Ayarsız + ayarlı düğüm aynı listede `GET /api/workflows`'u çökertmez (test 5) — ✅ `WorkflowFunctionSettingsTests.The_workflow_lists_are_served_when_nodes_with_and_without_settings_sit_side_by_side`
- [x] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>` — ✅ ⏳
- [x] `samples/Tracon.Api` ile gerçek `run` yapıldı (konsoldan kurulan ayarlı zincir), çıktı belgeye yazıldı — ✅ bkz. *Örnek Uygulama Koşumu*
- [x] `secret` taraması boş döndü; site ve XML "ayar `secret` değil, anahtar adı tutar" kuralını yazar — ✅ `kapi.py tarama` temiz; site ("Never put a secret in settings"), XML ve `<example>` (`ApiKeyConfigurationKey`) kuralı yazar
- [x] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` içine eklendi; otomatikleştirilebilenler koşuldu — ✅ `MT-WF-132…137` (+ `MT-WF-120` güncellendi); `132`, `133`, `134`, `135` (desen kilidi), `137` örnek uygulamada koşuldu; `136`'nın resume kısmı fazın önceki sample koşumunda ve testlerle
- [x] `faz-denetim` koşuldu; 🔴 bulgu kalmadı — ✅ 1 🔴 gerçek (düzeltildi, test + mutasyon denetimi), 1 🔴 gürültü (gerekçeli ret); iki triyaj da kullanıcı kararı (bkz. *Denetim Bulguları*); kapılar yeniden koşuldu
- [x] `docs-site/` güncellendi (`concepts/workflows.md`, `ui.md` + ekran görüntüsü, `capabilities.md`, `reference/configuration.md`); `npm run build` + `check-links.mjs` temiz — ✅ ⏳
- [x] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı — ✅ `workflowEditor.*` ve `workflows.column.participants` iki dilde; bundle 199,9 KB gzip (bütçe 250 KB; Faz 194 sonrası 197,7 KB, payı 2,2 KB)
- [x] `CHANGELOG.md` `[Unreleased]` yeni aşırı yüklemeyi ve (Açık Soru 1-A seçildi) arayüz üyesini adıyla anar — ✅ iki aşırı yükleme, `IWorkflowFunctionCatalog.ValidateSettings`, `MaxNodeSettingsLength`, iki yeni `WorkflowDefinitionStoreContract` vakası, belgelenen 41 istemci özelliği

### Doğrulama komutları

```bash
# Katalog — ayar şeması görünür
curl -s http://localhost:5081/tracon/api/workflows/functions

# Ayarlı karma zincir kaydı (Admin)
curl -s -X PUT http://localhost:5081/tracon/api/workflows/narrated \
  -H 'Content-Type: application/json' \
  -d '{"kind":"Sequential","nodes":[{"name":"support","kind":"Agent"},{"name":"speak","kind":"Function","settings":{"voice":"alloy","loudnessLufs":-14}}]}'

# Bilinmeyen alan — 400
curl -s -X PUT http://localhost:5081/tracon/api/workflows/narrated \
  -H 'Content-Type: application/json' \
  -d '{"kind":"Sequential","nodes":[{"name":"support","kind":"Agent"},{"name":"speak","kind":"Function","settings":{"voise":"alloy"}}]}'

# Run
curl -sN -X POST http://localhost:5081/tracon/api/workflows/narrated/run \
  -H 'Content-Type: application/json' -d '{"message":"Hello"}'
```

Adlar örnek uygulamadaki gerçek adlardır (`support` agent'ı, `speak` fonksiyonu); koşumun çıktıları
*Örnek Uygulama Koşumu*'ndadır. Koşum Echo kipinde (`ASPNETCORE_ENVIRONMENT=Production`, anahtarsız) `5199`
portunda yapıldı; komutlardaki `5081` örnek uygulamanın geliştirme portudur.

---

## Riskler

| Risk | Önlem |
|------|-------|
| Ayar bir mini dile dönüşür (K2 aşınır) | Ayar yalnız değerdir; `{{param}}` ve ifade kapsam dışı; şema kodda |
| Operatör ayara `secret` yazar (K-059) | XML, site ve `<example>` kuralı yazar; örnek ayar yalnız anahtar adı taşır |
| `TSettings` değişir, kayıtlı tanımlar run'da düşer | Derlemede yeniden doğrulama, adlandırılmış hata (test 4); site dağıtım notunu yazar |
| Biçim dönüşümü bekleyen run'ın resume'unu bozar | Editör uyarısı (Açık Soru 3); uyumun bugünkü durumu ölçülür (§195.4) |
| Faz 194 ile dosya çakışması | Önkoşul: Faz 194 kapandıktan sonra başlar |
| Konsol ile API iki biçimi farklı yazar | Sunucu iki biçimi de kabul eder; site farkı yazar; kanonikleştirme yalnız konsoldadır |
| Şema üretimi bazı tiplerde beklenmedik çıktı verir | Şema yalnız bilgi içindir; doğrulamayı deserialize yapar |

---

## Plandan Sapmalar

Numara `#N` ile verilir; sonraki fazlar bu numarayla atıf yapabilir.

| # | Plan | Gerçek | Neden |
|---|---|---|---|
| 1 | Planlanan Public API: `AddWorkflowFunction<TInput, TOutput, TSettings>(…, string? description = null, WorkflowNodeRetryPolicy? retryPolicy = null)` | **İki aşırı yükleme:** `(name, factory)` ve `(name, factory, Action<WorkflowFunctionOptions> configure)`. Yeni public `WorkflowFunctionOptions` (`Description`, `RetryPolicy`). İki parametreli aşırı yükleme **değişmedi** | Opsiyonel parametreli taslak RS0026 ile **derlenmedi**: aynı adlı iki aşırı yüklemenin ikisi de opsiyonel parametre taşırsa analyzer kırılır ve genel tip aritesi farkı onu yatıştırmaz. Opsiyonel parametre `1.0.0` GA'da donar; bir sonraki ayar bir aşırı yükleme daha olurdu (K-902, K-422, K-867). Bu, planın tek revizyonudur |
| 2 | §195.2 ve Planlanan Public API örnekleri `string` girdilidir (`AddWorkflowFunction<string, AudioClip, TtsSettings>`) | Örnekler `List<ChatMessage>` girdilidir (bu belgedeki §195.2 örneği dahil) | F-341 (K-908), başka bir oturumda bu fazla aynı anda kapandı: karışık zincirde her düğüm, önceki düğümün geçirdiği tipi almalıdır; agent düğümü `List<ChatMessage>` geçirir ve run, ilk düğüme kullanıcı mesajını o tipte gönderir. `string` girdili bir fonksiyon agent'tan sonra **koşmaz** ve kayıt `400` verir. `<example>`, sample, site ve testler buna uyarlandı; bu fazın kodu değişmedi |
| 3 | §195.2: hata yalnız katı okumadan gelir (`JsonException`) | **Tüketicinin setter'ı veya kurucusu fırlatırsa** kayıt `500` yerine `400` verir (`the settings type rejected a value (<Tür>)`); `NotSupportedException` da ret sayılır. İstisnanın kendi mesajı yanıta **girmez**; uyarı log'una gider | Setter'daki aralık denetimi, tüketicinin ayar tipinde doğal bir yazımdır. Operatör değeri yazdı; `500` onu sunucu hatası yapardı. Mesaj değeri taşıyabilir (K-059 komşusu). Test: `A_setter_in_the_settings_type_that_throws_is_a_400_naming_the_kind_of_refusal_never_a_500`, `A_setter_that_rejects_a_value_by_throwing_is_a_refusal_not_a_server_error` |
| 4 | Plan okunamayan ayar tipini düşünmedi | `AddWorkflowFunction<…, TSettings>` kayıtta tipin okunabildiğini kanıtlar (`WorkflowFunctionSettings.EnsureReadable`): arayüz veya soyut sınıf, `AddWorkflowFunction` çağrısında `TraconException` ile düşer | Aksi hâlde "abstract types are not supported" her kayıtta operatörün suçu gibi görünürdü. Test: `A_settings_type_no_settings_could_be_read_into_fails_at_registration` |
| 5 | §195.3: şema "kayıt anında bir kez üretilir" | Üretilemezse `settingsSchema` **`{"type":"object"}`** olur, asla `null`; neden uyarı log'una gider ve fonksiyon çalışır | Katalogda `null` "bu fonksiyon ayar almaz" demektir; ayar alan bir fonksiyon öyle görünmemeli. Konsol alan listesini kaybeder, fonksiyonu kaybetmez. Test: `A_schema_the_exporter_cannot_produce_costs_the_console_its_field_list_never_the_function` |
| 6 | Dosya listesi: `WorkflowDefinitionPayload.cs` "ölçülür, gerekiyorsa değişir" | **Ölçüldü, kod değişmedi** (yalnız `remarks`): `Nodes` düğüm kayıtlarını **kendisi** taşır, yeni düğüm alanı onlarla yazılır ve okunur. K-580 tuzağı bu alanda oluşmadı | Kanıt: `WorkflowDefinitionStoreContract` iki yeni case (`Function_node_settings_survive_the_round_trip`, `Saving_again_without_node_settings_clears_them`) dört depoda yeşil. 🚨 Sevk edilen sözleşme **genişledi**: yayın provası zorunlu (aşağıda) |
| 7 | Açık Soru 1 · 2 · 3: planın önerisi | **1-A** `IWorkflowFunctionCatalog.ValidateSettings` (varsayılan uygulamasız) · **2-B** `TraconWorkflowOptions.MaxNodeSettingsLength`, varsayılan 16 KiB, yalnız **kayıtta** (saklı tanım çalışır), `Tracon:Workflows:MaxNodeSettingsLength` yapılandırmadan bağlanır — canlıda ölçüldü · **3-A** yapısal değişiklikte editör **her zaman** bir satır söyler (bekleyen run sayılmaz) | Karar kaydı K-900 (1, 2) ve K-901 (3) |
| 8 | §195.4: "Ölçülmeli: agent ekleme/çıkarma da aynı sonucu veriyor mu" | Ölçüldü (`WorkflowStructuralChangeResumeTests`, 9 vaka): agent ekleme, **agent veya düğüm silme**, **sıra değişikliği**, **desen değişikliği**, `agentNames` → `nodes` biçim değişimi ve düğüm ekleme eski checkpoint'i `graph's structure differs` ile reddettirir; açıklama veya **ayar** değişikliği reddettirmez. **Beklenmeyen:** `Concurrent`'te sıra değişikliği de reddettirir (ekranın "order does not affect this pattern" ipucu run'ın sonucundan söz eder, checkpoint grafla ilgilidir) | Uyarı **genel** yapısal değişiklik uyarısıdır; biçim dönüşümüne özgü değildir ve her desende doğrudur. Editör iki ayrı satır yazar (biçim · yapı). K-901 |
| 9 | §195.2: "Ölçülmeli: `net8.0` hedefindeki geçişli System.Text.Json'da `RespectNullableAnnotations` var mı" | **Var.** `System.Text.Json` 10.0.0 geçişli paketi `net8.0`, `net9.0` ve `net10.0`'da `RespectNullableAnnotations`, `UnmappedMemberHandling`, katı sayı ve `required` verir | Faz 193'e (`net8.0`'ı düşürme) bağımlılık **yok**. K-900 |
| 10 | Plan "tek editör dosyası" saymıştı (`workflow-editor.tsx`) | Editör üç dosyaya bölündü: `screens/workflow-editor.tsx` (iskelet) · `screens/workflow-editor/steps.tsx` (adım listesi, ayar alanı) · `lib/workflow-steps.ts` (saf mantık: kanonik biçim, `chainBody`, `shapeOf`, şema özeti — 23 test) | Plan "iki ayrı liste bileşeni yazılmaz" dedi; tek `Step[]` hem `agentNames` hem `nodes` biçimini okur, kanonik olanı yazar ve ekranın biçime dallanmasını önler. Saf mantık DOM'suz sınanır |
| 11 | Plan istemci üretimini "mevcut kapılar" saydı | `scripts/nswag-prepare-document.py` **üçüncü dönüşümü** aldı: nullable `$ref` özelliğinde (`oneOf: [null, {description, $ref}]`) açıklamayı özelliğe taşır. `client-description-baseline.txt` **396 → 357** | Yeni `settingsSchema` C# istemciye belgesiz gidiyordu; aynı kusur 41 özelliği (`AgentDefinition.Harness` dahil) etkiliyordu. Taban çizgisi **yalnız küçüldü**. TS üreteci (`openapi-typescript`) yazılı belgeden üretir ve dönüşümü almaz |
| 12 | Plan `ExamplePrelude`'a dokunmadı | `tests/Tracon.Generators.UnitTests/Examples/ExamplePrelude.cs` içine `TtsSettings` taslağı eklendi | Yeni `<example>` derleniyor (`ExampleCompilationTests`); örneğin tipi için bir kök gerekir |
| 13 | Plan KARARLAR-INDEKS bütçesini saymadı | K-900…K-902 eklenince `docs/KARARLAR-INDEKS.md` 26.088 B oldu (bütçe 25.000 B). Üç başlığı kısaltmak yetmedi (25.108 B); `ARSIV_ESIK` **88 → 84** (`scripts/dokuman-bakim.py`) en eski dört kararı arşiv indeksine geçirdi. `842d126a` sonradan eşiği 60'a indirdi | Eşik **yalnız sıkılaştı**; içerik silinmedi, kararların tam metni `docs/KARARLAR.md`'de durur |
| 14 | Örnek uygulama: `Program.cs` | `Program.cs` + yeni `SpeakSettings.cs`; `speak` fonksiyonu `static options => options.Description = …` ile kayıtlı | Ayar tipinin kendi dosyası olur; `ApiKeyConfigurationKey` üyesi "anahtar adı yazılır, anahtar yazılmaz" kuralını örnekte gösterir |
| 15 | Plan: "İptal sorusu: yeni kod yolu iptal noktası eklemez … grep ile ölçülür" | **Ölçüldü:** `git diff b1a771fe 99fe0b13 -- 'src/*.cs'` içinde eklenen `await`/`async` satırları yalnız `Tracon.Testing.Contracts.Xunit` sözleşme case'lerindedir. Üretim kodu yeni `await` ve yeni `CancellationToken` parametresi eklemedi; ayara bağlı sarmalayıcı mevcut `cancellationToken`'ı işleyiciye aynen geçirir | Deserialize senkrondur ve derlemede olur; mevcut run iptal testleri yeterlidir |
| 16 | §195.5 yalnız editörü anlattı | **Liste ekranı da değişti:** `screens/workflows.tsx` iki biçimi de okur (`stepsOf`) ve sütun artık **Participants / Katılımcılar**'dır; `workflows.png` ve `workflows-light.png` yeniden üretildi | Konsol fonksiyonlu her zinciri `nodes` yazar (K-901); liste yalnız `agentNames`'i okuyunca konsolun kurduğu zincir boş görünürdü. Sütun fonksiyon da içerdiğinden "Agents" adı yanlıştı (denetim 🟡 #3) |
| 17 | §195.5: "İstemci yalnız 'JSON olarak ayrıştırılamıyor' durumunda kaydı devre dışı bırakır" | **Save ve Retry** devre dışıdır; `chainBody` geçersiz metinde `null` yazmaz, **fırlatır** | Hata bandındaki Retry bu korumayı aşıyordu ve tam değiştiren kayıt ayarı siliyordu (denetim 🔴 #1). Retry "Save'i yeniden bas" demektir; aynı korumayı paylaşır |
| 18 | §195.2 ayar değeri "JSON" | **Bilinen sınır:** konsol JSON sayısını `double` okur; 2^53 − 1'i aşan tamsayı konsolda açılıp kaydedilince alt basamaklarını kaybeder. **Çözülmedi:** XML ve site "bu genişlikteki kimliği `string` bildirin" der | Kayıpsız taşıma özel bir JSON ayrıştırıcısı ve yazıcısı ister (bundle payı, karmaşıklık); ayar tipinin `string` üyesi sorunu çözer. Denetim 🟡 #4, aday F-414 |
| 19 | Açık Soru 2: "varsayılan değer ölçümle seçilir" | 16 KiB **ölçüme değil tavan mantığına** dayanır: örnek uygulamadaki en büyük gerçekçi ayar 74 bayttır (`{"voice":"alloy","loudnessLufs":-14,"apiKeyConfigurationKey":"Tts:ApiKey"}`); 16 KiB onun yaklaşık 220 katıdır | Tavan koruma içindir ve yapılandırılabilir (`Tracon:Workflows:MaxNodeSettingsLength`). Gerçek tüketici ayarı ölçülmedi. Denetim 🟢 #9 |

Açık Sorular: 1-A · 2-B · 3-A (hepsi planın önerisi).

## Bu Fazda Verilen Kararlar

Üç karar `docs/KARARLAR.md`'ye girdi:

- **K-900** *(public-api, güvenlik)* — ayar tipli ve **katı okumayla** doğrulanır; kayıtta ve her derlemede aynı okuma; ayar `secret` değildir; boyut sınırı yalnız kayıtta (kullanıcı kararı)
- **K-901** *(public-api)* — konsol zinciri tek kanonik biçimde yazar, sunucu iki biçimi de kabul eder; yapısal değişiklik bekleyen run'ı resume edilemez kılar ve editör bunu söyler; aynı fonksiyon bir zincirde iki kez kullanılamaz (kullanıcı kararı)
- **K-902** *(public-api)* — ayarlı kayıt opsiyonel parametre yerine `Action<WorkflowFunctionOptions>` alır; `IWorkflowFunctionCatalog` `ValidateSettings` üyesini kazanır

Faz içinde kalan yerel tercihler:

- Ayar `JsonElement?`'dir ve `init` erişimcisi `Undefined`/`Null`'ı `null`'a çevirir: atanmamış bir `JsonElement` tek düğümle **tüm** liste ucunu çökertirdi (`MEMORY.md`).
- Konsolda geçersiz JSON kaydı **yalnız** o durumda devre dışı bırakır; ayarın tipe uygunluğu sunucunun `400` mesajıyla söylenir, editör bir doğrulayıcı kopyası taşımaz.
- Şema özeti (`describeSchema`) yalnız sunucu dışa aktarıcısının düz bir ayar sınıfı için yazdığı üyeleri (`properties`, `required`, `type`, `enum`, `items`) listeler; bir şema yorumlayıcısı **değildir** ve bir şeyi doğrulamaz.
- `settings` alanı her düğümde yazılır: ayarsız düğüm açık `null` taşır (`PUT` tam değiştirir, K-725); derin `Complete<WorkflowSaveRequest>` kapısı bunu `tsc` ile zorlar.

## Gerçekleşen Public API

Koddaki gerçek imzalar (`PublicAPI.Unshipped.txt`; `Shipped` dosyaları `1.0.0`'a kadar boştur).

```csharp
// Tracon.Abstractions
public sealed record WorkflowNodeReference
{
    public JsonElement? Settings { get; init; }          // Undefined ve Null → null; yalnız Function düğümü, yalnız nesne
}

public sealed record WorkflowFunctionDescriptor
{
    public Type? SettingsType { get; init; }              // null: fonksiyon ayar almaz
    public JsonElement? SettingsSchema { get; init; }     // yalnız gösterim; üretilemezse {"type":"object"}, asla null
}

public interface IWorkflowFunctionCatalog
{
    // null: ayar geçerli. Varsayılan uygulaması YOKTUR (K-902): tek uygulayıcı iç kayıttır.
    string? ValidateSettings(string name, JsonElement? settings);
}

// Tracon.Workflows
public static class TraconWorkflowFunctionExtensions
{
    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput, TSettings>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, TSettings, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory)
        where TSettings : class;

    public static ITraconBuilder AddWorkflowFunction<TInput, TOutput, TSettings>(
        this ITraconBuilder builder,
        string name,
        Func<IServiceProvider, Func<TInput, TSettings, IWorkflowContext, CancellationToken, ValueTask<TOutput>>> factory,
        Action<WorkflowFunctionOptions> configure)
        where TSettings : class;
}

public sealed class WorkflowFunctionOptions
{
    public string? Description { get; set; }
    public WorkflowNodeRetryPolicy? RetryPolicy { get; set; }
}

public sealed class TraconWorkflowOptions
{
    public int MaxNodeSettingsLength { get; set; }        // 16384; ≥ 1; yalnız kayıtta uygulanır
}

// Tracon.AspNetCore
public sealed record WorkflowFunctionResponse
{
    public JsonElement? SettingsSchema { get; init; }     // GET /api/workflows/functions
}

// Tracon.Testing.Contracts.Xunit (sevk edilen sözleşme genişledi)
WorkflowDefinitionStoreContract.Function_node_settings_survive_the_round_trip()
WorkflowDefinitionStoreContract.Saving_again_without_node_settings_clears_them()
```

İç tipler (public değil): `WorkflowFunctionSettings` (katı seçenekler, okuma, şema, hata metni — **tek yer**), `WorkflowFunctionSettingsException`. `WorkflowFunctionRegistration` fabrikası `Func<IServiceProvider, Func<string, JsonElement?, Executor>>` oldu; `WorkflowFunctionRegistry.CreateExecutor(name, executorId, settings = null)`.

Üretilen yüzey: `settings` (`WorkflowNodeReference`) ve `settingsSchema` (`WorkflowFunctionResponse`) `docs/openapi/tracon.json`, `Tracon.Client` ve `@tracon/client`'ta. HTTP uçları: yeni uç yok; `PUT /api/workflows/{name}` düğüm ayarını doğrular, `GET /api/workflows/functions` `settingsSchema` taşır. Roller değişmedi (Admin · Reader).

## Dosya Listesi (gerçekleşen)

Faz commit'i `99fe0b13` (84 dosya) ve denetim sonrası dört commit (`98fa3105`, `a7f5f72e`, `cf9e07bf`, `8e161a34`). Üretilen ve belge dosyaları sona toplandı.

```
src/Tracon.Abstractions/Workflows/
├── WorkflowNodeReference.cs            (Settings)
├── WorkflowFunctionDescriptor.cs       (SettingsType, SettingsSchema)
├── IWorkflowFunctionCatalog.cs         (ValidateSettings)
└── WorkflowDefinition.cs               (Nodes remarks: iki biçim)
src/Tracon.Core/Workflows/WorkflowDefinitionValidator.cs      (agent düğümünde ayar, nesne olmayan ayar)
src/Tracon.Workflows/
├── TraconWorkflowFunctionExtensions.cs (iki yeni aşırı yükleme, <example>)
├── WorkflowFunctionOptions.cs          (yeni)
├── TraconWorkflowOptions.cs            (MaxNodeSettingsLength)
├── Internal/WorkflowFunctionSettings.cs            (yeni — katı okuma, şema, hata metni)
├── Internal/WorkflowFunctionSettingsException.cs   (yeni)
├── Internal/WorkflowFunctionRegistration.cs        (fabrika ayar alır)
├── Internal/WorkflowFunctionRegistry.cs            (ValidateSettings, CreateExecutor, şema, boyut sınırı)
├── Internal/WorkflowDefinitionCompiler.cs          (CreateFunctionExecutor: ayar hatası run'ı adlandırılmış düşürür)
├── Internal/TraconWorkflowOptionsValidator.cs      (MaxNodeSettingsLength ≥ 1)
├── TraconWorkflowContextExtensions.cs              (yalnız <example>: F-341 sonrası tip — faz dışı düzeltme)
└── PublicAPI.Unshipped.txt, README.md
src/Tracon.AspNetCore/
├── Contracts/WorkflowContracts.cs      (WorkflowFunctionResponse.SettingsSchema)
├── Endpoints/WorkflowEndpoints.cs      (kayıtta ayar doğrulaması, iki uç açıklaması)
└── PublicAPI.Unshipped.txt
src/Tracon.Sql.Shared/Internal/WorkflowDefinitionPayload.cs   (yalnız remarks — K-580 ölçüldü)
src/Tracon.Testing.Contracts.Xunit/Contracts/WorkflowDefinitionStoreContract.cs  (iki case)
src/Tracon.UI/frontend/src/
├── lib/workflow-steps.ts               (yeni — saf mantık)        lib/workflow-steps.test.ts  (yeni)
├── screens/workflow-editor.tsx         (iskelet, ChainNotices; Retry kaydı gibi tutulur)
├── screens/workflows.tsx               (liste: iki biçimi okur, sütun Participants)   screens/workflows.test.tsx (yeni)
├── screens/workflow-editor/steps.tsx   (yeni — adım listesi, ayar alanı)
├── screens/workflow-editor.test.tsx · full-replace-editors.test.tsx · app.test.tsx · test/api-fixtures.ts
└── locales/{en,tr}/workflows.ts
samples/Tracon.Api/Program.cs · SpeakSettings.cs (yeni)
scripts/nswag-prepare-document.py · scripts/dokuman-bakim.py (ARSIV_ESIK)
Üretilen: docs/openapi/tracon.json · packages/tracon-client/src/schema.ts · src/Tracon.Client/Generated/TraconApiClient.g.cs
Taban çizgileri: tests/Tracon.Client.UnitTests/client-description-baseline.txt (396 → 357) · tests/Tracon.Core.UnitTests/Architecture/public-surface-baseline.txt (Tracon.Workflows 6 → 7)
Site: capabilities.md · concepts/workflows.md · ui.md · reference/configuration.md · public/llms*.txt · public/screenshots/workflow-editor{,-light}.png · workflows{,-light}.png (+ .ui-source.sha256)
Doküman: CHANGELOG.md · KARARLAR.md (K-900…K-902) + arsiv/KARARLAR-GECMISI.md + üretilen indeksler · hafiza/{analyzer-tanilari, elle-kosum-ortami, frontend-test-altyapisi, kod-haritasi, nswag-istemci-uretimi, workflows}.md · manuel-test/15-WORKFLOWS.md (MT-WF-132…137, MT-WF-120 güncellendi) + 00-INDEKS.md · Tracon.AgentMap.md
```

## Testler

Sayılar `[Fact]` + her `[InlineData]` satırıdır. Plandaki `Hata Modları` numarası parantezdedir.

| Sınıf | # | Ne doğrular |
|---|---:|---|
| `Tracon.Workflows.UnitTests.WorkflowFunctionSettingsTests` | 32 | Fonksiyon okunan nesneyi alır (1) · ayarsız düğüm `{}` okunur · iki workflow iki ayar (1) · katı okuma: bilinmeyen alan, eksik `required`, yanlış tür, `null`, enum sayı (2) · ret metni değeri **tekrarlamaz** (18) · setter'ı fırlatan ayar ret'tir, `500` değil · ayarsız fonksiyon ayar reddeder (3) · eski ayar tipi run'ı ilk düğümden önce düşürür (4, 17) · her deneme aynı ayarı görür (7) · iki eşzamanlı run, iki tanım sürümü (8) · resume güncel ayarı okur, eski checkpoint uyumlu kalır (10) · şema, okuma ne kabul ederse onu listeler (11) · şema üretilemezse `{"type":"object"}` (11) · okunamayan tip kayıtta düşer · boyut sınırı yalnız kayıtta (16) · `Undefined` düğüm ayarı (5) · düğüm ayarla gidiş-dönüş |
| `…WorkflowStructuralChangeResumeTests` | 9 | Agent ekleme, agent veya düğüm silme, sıra değişikliği (`Sequential` ve `Concurrent`), desen değişikliği, `agentNames` → `nodes` ve düğüm ekleme eski checkpoint'i **reddettirir**; açıklama değişikliği reddettirmez (K-901) |
| `…WorkflowNodeSettingsLimitValidationTests` | 4 | `MaxNodeSettingsLength` 0 ve −1 başlangıçta düşer; motor kapalıyken de kontrol edilir; 1 bayt başlar, varsayılan 16 KiB |
| `…WorkflowDefinitionValidatorTests` (eklenen) | 6 | Fonksiyon düğümü ayar taşıyabilir · agent düğümünde ayar adlandırılmış ret · nesne olmayan ayar (dizi, string, sayı, `true`) (2, 3) |
| `Tracon.AspNetCore.FunctionalTests.WorkflowFunctionSettingsTests` | 7 | Gerçek HTTP: kaydet → oku → çalıştır, ayar fonksiyona ulaşır (1) · aynı zincirde iki fonksiyon kendi ayarını alır (1) · bir fonksiyon iki workflow'da farklı ayar · ayarlı ve ayarsız düğüm aynı listede `GET /api/workflows`, `{ad}` ve `graph` ucunu çökertmez (5) · `null` ayar = yok · yeniden kayıt ayarı temizler (K-725) · ayar değeri log'a, SSE'ye, `run_events`'e ve `400`'e **düşmez** (18) |
| `…WorkflowFunctionSettingsValidationTests` | 16 | `PUT` → `400`: bilinmeyen alan, yanlış tür, `null`, `{}` eksik zorunlu, sayı olarak enum; mesaj düğümü ve üyeyi adlandırır, kayıt **saklanmaz** (2) · ayarsız düğüm eksik zorunlu üyede `400` (2) · dizi/string/sayı (2) · agent düğümünde ayar (3) · ayarsız fonksiyonda ayar (3) · setter fırlatırsa `400`, iç mesaj yok · boş nesne kabul (16) · boyut sınırı ve varsayılan sınır (16) |
| `…WorkflowFunctionSettingsDriftTests` | 4 | Kayıttan sonra `TSettings` değişir: run ilk düğümden önce adlandırılmış hatayla `Failed` olur (4, 17) · graf ucu `400` söyler, `500` değil · yeniden kayıt run'ı onarır · ayar tipini kaybeden fonksiyon ayarı **yok saymaz**, run'ı düşürür |
| `…WorkflowFunctionSettingsConcurrencyTests` | 2 | Run içindeyken yapılan kayıt **sonraki** run içindir · fonksiyonu paylaşan iki workflow ayar takas etmez (8) |
| `…WorkflowFunctionSettingsTenantTests` | 2 | Aynı workflow adı iki kiracıda ayarını korur · bir kiracıdaki kayıt öbürünün run'ını değiştirmez (9) |
| `…WorkflowFunctionSettingsResumeTests` | 1 | Ayar değişikliğinden sonra resume yeni ayarla koşar ve reddedilmez (10) |
| `…WorkflowFunctionSettingsRetryTests` | 1 | Yeniden denenen düğüm fonksiyonu aynı ayarla yeniden çağırır (7) |
| `…WorkflowFunctionCatalogSchemaTests` | 2 | `GET /api/workflows/functions`: ayarlı fonksiyon şema, ayarsız `null` taşır · yarım anlaşılan ayar tipi listeyi bozmaz (11) |
| `WorkflowDefinitionStoreContract` (iki case) | 8 | Dört depoda (bellek içi, SQLite, PostgreSQL, SQL Server): ayar gidiş-dönüşü ve yeniden kayıtta temizlenme (6) |
| `workflow-steps.test.ts` | 24 | Kanonik biçim, `chainBody` (geçersiz metinde fırlatır), ayar ayrıştırma, `shapeOf`, şema özeti (13) |
| `workflow-editor.test.tsx` | 21 | Zincir kurma, taşıma, silme, kanonik biçim ve uyarı satırları, desen kilidi, geçersiz JSON, **hata sonrası Retry'ın Save gibi tutulması**, şema alanları, katalog yüklenemezse zincir korunur (13, 14) |
| `workflows.test.tsx` | 2 | Liste: `nodes` biçimli zincirin adımlarını, `agentNames` biçimlisini ve kod grafiğinde `—` gösterir; sütun adı |
| `full-replace-editors.test.tsx` | — | Derin `Complete<WorkflowSaveRequest>`: ayarlı düğüm fixture'ı; ayar korunmazsa `tsc` düşer (12) |
| `Tracon.Ui.E2ETests.Ui.WorkflowTests` (eklenen) | 2 | Playwright: konsolda kur → kaydet → çalıştır (15) · yanlış yazılmış ayar üyesi reddedilir ve sunucunun cevabı görünür |

Planın sekiz fonksiyonel sınıfı **adıyla** yazıldı. Planda adı geçmeyen sınıflar: `Tracon.Workflows.UnitTests.WorkflowFunctionSettingsTests` (planın "Birim" kısımlarını taşır: 3, 7, 11, 16), `WorkflowStructuralChangeResumeTests` (§195.4'ün ölçümü), `WorkflowNodeSettingsLimitValidationTests` (denetim 🟡 #5) ve `workflows.test.tsx` (denetim 🟡 #3). Bütün konsol paketi: 32 dosya, 336 test.

## Tüketici Yüzeyi Envanteri

`tuketici-dokuman-senkronu` Adım 1: faz hem public tipe, hem HTTP ucuna, hem ekrana dokundu. Üç kova:

| Kova | Yüzey | Elle / üretilen | Durum |
|---|---|---|---|
| `docs-site/` | `concepts/workflows.md` (§ *Give a function settings* — "Wide numbers" maddesi dahil; § *Retry*'a "aynı ayar"; "console cannot change it yet" kalktı) · `ui.md` (editör anlatısı + `workflow-editor{,-light}.png`; "no control for the parameter schema" cümlesi; liste ekranının görüntüsü `workflows{,-light}.png` yeniden üretildi) · `reference/configuration.md` (`MaxNodeSettingsLength`) · `capabilities.md` (iki satır) | Elle | Yazıldı; `npm run check` yeşil |
| | `api/*` (XML'den) · `http-api/workflows.md` (OpenAPI'den: `settingsSchema` açıklaması, `PUT` açıklaması) · `llms.txt` · `llms-full.txt` | **Üretilir** (`llms*` commit edilir) | Yeniden üretildi |
| Sevk edilen metin | `///`: `WorkflowNodeReference.Settings` · `WorkflowFunctionDescriptor.SettingsType/SettingsSchema` · `IWorkflowFunctionCatalog.ValidateSettings` · `TraconWorkflowFunctionExtensions` (iki aşırı yükleme, her ikisi `<example>` taşır) · `WorkflowFunctionOptions` · `TraconWorkflowOptions.MaxNodeSettingsLength` · `WorkflowDefinition.Nodes` remarks · `WorkflowFunctionResponse.SettingsSchema` · iki uç açıklaması (paketlenen `tracon.json` ve `http-api/` buradan) | Elle | `K-NNN`/`F-NN`/faz numarası sevk edilen `///`'de **yok** (`ExampleCompilationTests` ve kendi kendine yeterlik kapısı yeşil) |
| | `src/Tracon.Workflows/README.md` (ayar, seçenek tablosu) · `CHANGELOG.md` `[Unreleased]` (aşırı yüklemeleri, arayüz üyesini ve seçeneği **adıyla** anar) | Elle | Yazıldı |
| Agent haritası ve yerel referans | `capabilities.md` "Workflow functions" ve "Workflow node retry" satırları → `Tracon.AgentMap.md` (`node docs-site/scripts/build-agent-map.mjs`) ve `llms*.txt` yeniden üretildi · `Tracon.LocalReference.md` tüketicinin diskinde üretilir; yeni XML otomatik girer | Üretilir | Harita kapısı (`--check`) yeşil |

Site `./scripts/site-deploy.sh` ile **yayınlanmadı** (Adım 10 dışarıya dönük iştir, istenmedi). Kapı ve derleme yeşildir.

## Örnek Uygulama Koşumu

`samples/Tracon.Api`, **Echo** sağlayıcıyla (anahtarsız), `ASPNETCORE_ENVIRONMENT=Production` (bellek içi depo) ve `http://127.0.0.1:5199` üzerinde, `842d126a` ile **birleşmiş** kodla koştu. Çıktılar gerçektir.

| Komut | Çıktı |
|---|---|
| `GET /tracon/api/workflows/functions` | `speak` → `settingsSchema`: `{"type":"object","properties":{"voice":{"type":"string"},"loudnessLufs":{"type":"number"},"apiKeyConfigurationKey":{"type":["string","null"]}},"required":["voice"],"additionalProperties":false}`; `narrate` ve `word-count` → `"settingsSchema": null` |
| `PUT /api/workflows/narrated` — `support` (Agent) → `speak` (Function, `{"voice":"alloy","loudnessLufs":-14}`) | `HTTP 200`; `nodes[0].settings` = `null`, `nodes[1].settings` = `{"voice":"alloy","loudnessLufs":-14}` |
| `POST /api/workflows/narrated/run` `{"message":"Hello"}` | `WorkflowOutput`: `Echo: Hello \n\n[spoken by alloy at -14 LUFS]` · `RunCompleted` |
| `PUT` aynı workflow, `settings: {"voice":"nova"}` → yeni run | `[spoken by nova at -16 LUFS]` — sonraki run yeni ayarı kullandı; `loudnessLufs` varsayılana (−16) döndü |
| `PUT` ayar `{"voise":"alloy"}` | `HTTP 400` · `Workflow 'narrated2': Function node 'speak' has invalid settings: The JSON property 'voise' could not be mapped to any .NET member contained in type 'Tracon.Api.SpeakSettings'. Path: $.voise` |
| `PUT` `speak` ayarsız | `HTTP 400` · `… JSON deserialization for type 'Tracon.Api.SpeakSettings' was missing required properties including: 'voice'.` |
| `PUT` `loudnessLufs: "loud"` | `HTTP 400` · `… The JSON value could not be converted to System.Double. Path: $.loudnessLufs` |
| `PUT` agent düğümünde `settings` | `HTTP 400` · `Node 'support' in workflow 'narrated2' is an agent node but carries 'settings'. Settings belong to function nodes only; an agent's behavior is set in the agent's own definition.` |
| `PUT` `word-count`'a (ayarsız fonksiyon) `settings` | `HTTP 400` · `… carries settings, but the function declares none. Remove the node's 'settings', or register the function with a settings type: AddWorkflowFunction<TInput, TOutput, TSettings>().` |
| `PUT` `settings: [1,2]` | `HTTP 400` · `Node 'speak' in workflow 'narrated2' has 'settings' that are a JSON array. Settings must be a JSON object, for example {"voice": "alloy"}.` |
| **Konsol** (gömülü tarayıcı): yeni workflow `console-narrated`, `support` + `speak`, ayar alanına `{"voise":"alloy"}` → Save | Hata bandı sunucunun `400` mesajını gösterir (`…'voise' could not be mapped… Path: $.voise`); kayıt oluşmaz |
| Aynı ekran: ayar `{"voice":"alloy","loudnessLufs":-12}` → Save | Detay sayfasına gider; `GET` → `nodes` taşır, `agentNames: []`, `version: 1`; graf `support → speak` (agent ve fonksiyon şekilleri); Run kutusu `Hello from the console` → `Echo: Hello from the console [spoken by alloy at -12 LUFS]` |
| Aynı workflow → Edit → `speak`'i sil | Editör: `This chain has no function node, so it is saved as a plain agent list and runs on the built-in sequential pattern.` ve `Changing the pattern, the steps or their order changes the graph of this workflow. A run that is waiting for input cannot be resumed after you save…`; desen seçici yeniden açılır |
| Aynı ekran → Save, sonra `GET` | `agentNames: ["support"]`, `nodes: []`, `version: 2` (kanonik biçim `agentNames`'e döndü) |

**Varsayılan dışı yapılandırma:** `Tracon__Workflows__MaxNodeSettingsLength=64` → `{"voice":"alloy"}` `HTTP 200`; 200 baytlık `voice` → `HTTP 400` · `Function node 'speak' has settings larger than 64 bytes of JSON. Keep the settings small… The limit is TraconWorkflowOptions.MaxNodeSettingsLength.` (yapılandırmadan bağlanma kanıtı).

**Bu koşumda ölçülmeyen:** PostgreSQL ile sample (Development ortamında yerel Postgres kapalı; depo sözleşmesi dört depoda test edildi) · eski checkpoint'ten resume **bu birleşik kodla** örnek uygulamada yeniden koşulmadı — davranış fazın önceki sample koşumunda ölçüldü (ayar değişikliği resume'u reddettirmez; yapı değişikliği `Workflow 'narrated' cannot be resumed from this checkpoint: the graph's structure differs…` ile reddedilir) ve `WorkflowFunctionSettingsResumeTests` ile `WorkflowStructuralChangeResumeTests` bugünkü kodda yeşildir.

### Kapanış kapıları ve yayın provası

⏳ (kapı koşumlarından sonra doldurulur)

## Süreç Ölçümü

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | 1 — aşırı yükleme imzası (RS0026, K-902). Diğer sapmalar planın kendi "ölçülmeli" kalemlerinin sonucudur ("Plandan Sapmalar" #7–#9) |
| Düzeltme turu sayısı | 12 + kapanış kapıları (elle sayıldı) — çözümleyici: genel API kaydı RS0016 (1) · CA2263/MA0009/MA0023 (1) · test tiplerinde IDE0032/MA0015 (1) · mimari ve ratchet kapıları: iptal süzgeci, ham istisna metni, Playwright, kaynak dili (1) · taban çizgileri: genel yüzey 6 → 7, istemci açıklaması 396 → 357 (1) · örnek derleme (`TtsSettings` taslağı) (1) · sevk edilen `///`'de iç referans (1) · OpenAPI anlık görüntüsü ve üç istemci (1) · site agent map (1) · `KARARLAR-INDEKS` bütçesi (1) · kültür duyarlı test dizesi (1) · bağımsız denetim sonrası (1) · kapanış kapıları: ⏳ |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | 1 / 1 / 0 — triyajı kullanıcı seçti (2026-10-12) |
| Fazın ürettiği regresyon | 2 — `🔴1` (hata bandındaki Retry, bu fazın editörünün korumasını aşıp kayıtlı ayarı siliyordu) ve `🟡3` (liste ekranı konsolun kanonik biçimini göstermiyordu). İkisini de bağımsız denetim yakaladı, kapanıştan önce düzeltildi |
| Faz kapandıktan sonra bulunan kusur | 0 — kapanış anı; sonraki `kusur-giderme` koşumunda güncellenir |

## Denetim Bulguları

Denetçi: `faz-denetcisi` (taze bağlam, salt-okunur), aralık `b1a771fe..HEAD`. Faz kodu `99fe0b13`'tür; `842d126a` yalnız F-341 etkileşimi için okundu. Denetçi derleme, test ve kapı **koşturmadı** (yalnız okudu); kapı sonuçları aşağıda "Örnek Uygulama Koşumu" altındadır. Triyaj `faz-tamamlama` Adım 5.1: **🔴 satırlarının sonucunu kullanıcı seçti**; her bulgunun kanıtı kodda **elle** doğrulandı.

| # | Seviye | Bulgu | Triyaj | Sonuç |
|---|---|---|---|---|
| 1 | 🔴 | Geçersiz JSON ile kayıt tam devre dışı değil: kayıt hatasından sonra görünen hata bandındaki **Retry** `blocked`'a bakmadan `save.mutate()` çağırır; `chainBody` bozuk ayar metnini sessizce `settings: null` yapar ve PUT tam değiştirdiği için kayıtlı ayar silinir (plan §195.5, K-725) | **Gerçek** (kullanıcı kararı) — `workflow-editor.tsx` `onRetry`, `workflow-steps.ts` `settingsValue` | **Düzeltildi** (`98fa3105`): Retry, Save'in korumasını paylaşır; `chainBody` geçersiz metinde fırlatır. Testler: `workflow-editor.test.tsx` "holds Retry back, like Save, while the settings text is not JSON, and sends nothing then" (400 → bozuk metin → Retry yok, PUT yok → JSON → Retry gönderir, `null` değil), `workflow-steps.test.ts` "builds no request from settings text that is not JSON". Mutasyon denetimi: koruma kaldırılınca editör testi düşüyor, `settingsValue` eski hâline dönünce adım testi düşüyor |
| 2 | 🔴 | Taban çizgisi büyüdü: `Tracon.Workflows` 6 → 7 (yeni public tip `WorkflowFunctionOptions`, planın "Planlanan Public API"sinde yoktu) | **Gürültü — gerekçeli ret** (kullanıcı kararı) | Gerekçe: opsiyonel parametreli aşırı yükleme RS0026 ile **derlenmedi**; opsiyonel parametre `1.0.0`'da donar; `AddTool` emsali var (K-422, K-867); karar K-902'dedir. "Plandan Sapmalar" #1 ve "Gerçekleşen Public API" bunu yazar. Emsal: Faz 194 aynı tabanı 5 → 6 yaptı. Taban çizgisi 7'de kalır |
| 3 | 🟡 | Workflow listesi `nodes` biçimini okumaz: konsolun yazdığı her fonksiyonlu zincir "Agents" sütununda `—` görünür | **Gerçek** — konsolun varsayılan çıktısı (K-901) | **Düzeltildi** (`98fa3105`): `participantsOf` iki biçimi okur, sütun **Participants / Katılımcılar**; `workflows.test.tsx` (mutasyon denetimi: eski okuma testi düşürüyor); `workflows{,-light}.png` yeniden üretildi |
| 4 | 🟡 | Konsol ayarı JS `Number`'ına çevirir: 2^53 − 1'i aşan tamsayı, yalnız açıklama değiştirilip kaydedilse bile yuvarlanır | **Gerçek** (sınır) | **Gerekçelendi ve belgelendi** (`8e161a34`): XML (`AddWorkflowFunction<…,TSettings>` remarks) ve site "bu genişlikteki kimliği `string` bildirin" der; "Plandan Sapmalar" #18. Kayıpsız taşıma özel JSON ayrıştırıcısı ister: **devredildi**, F-414 |
| 5 | 🟡 | `MaxNodeSettingsLength < 1` başlangıç doğrulaması testsiz | **Gerçek** | **Düzeltildi** (`a7f5f72e`): `WorkflowNodeSettingsLimitValidationTests` (0, −1, motor kapalı, 1 bayt, varsayılan) |
| 6 | 🟡 | DoD 9 harfiyen karşılanmadı: "konsoldan kurulan ayarlı zincir" örnek uygulamada koşulmadı | **Gerçek** (kanıt eksikti) | **Düzeltildi:** konsol, örnek uygulamada (gömülü tarayıcı) kuruldu, yanlış ayar `400` mesajıyla gösterildi, kaydedildi, çalıştırıldı, son fonksiyon silinip `agentNames`'e dönüldü — "Örnek Uygulama Koşumu"; `MT-WF-132` ve `MT-WF-134`'e işlendi |
| 7 | 🟢 | Tüketicinin `JsonConverter`'ının fırlattığı `JsonException`/`NotSupportedException` mesajı `400`'e ve `runs.error`'a girer; kayıt yolunda uyarı logu yazılmaz. K-900'ün "mesaj yanıta girmez" cümlesi yalnız setter ve kurucu için doğru | Gerçek, dar | **Devredildi:** F-415 |
| 8 | 🟢 | Uyarı ölçülenden geniş: yalnız agent ekleme, biçim değişimi, düğüm ekleme ve açıklama ölçülmüştü; sıra, desen ve silme ölçülmedi, ekran `Concurrent`'te "order does not affect this pattern" der | **Gerçek** | **Düzeltildi** (`a7f5f72e`): beş ölçüm vakası eklendi. **Sonuç: uyarı her desende doğrudur** — `Concurrent`'te sıra değişikliği de checkpoint'i reddettirir ("Plandan Sapmalar" #8) |
| 9 | 🟢 | `MaxNodeSettingsLength` 16 KiB'nin dayanağı yok; `ValidateSettings` XML'i boyut reddini anmıyor | **Gerçek** | **Düzeltildi** (`8e161a34`): XML boyut reddini anar. Dayanak tavan mantığıdır, ölçüm değil — "Plandan Sapmalar" #19 |
| 10 | 🟢 | CHANGELOG `Tracon.Testing.Contracts.Xunit`'e eklenen iki `WorkflowDefinitionStoreContract` vakasını anmıyor | **Gerçek** | **Düzeltildi** (`8e161a34`) |
| 11 | 🟢 | `<example>` `(name, factory)` aşırı yüklemesinde duruyor; `configure` aşırı yüklemesinde örnek yok | **Gerçek**, kozmetik | **Düzeltildi** (`8e161a34`); `ExampleCompilationTests` 81/81 |
| 12 | 🟢 | Fonksiyonel retry testi "SAME settings object" der ama `record` değer eşitliğiyle `Distinct()` sayar | **Gerçek** (birim test kimliği zaten kanıtlıyordu) | **Düzeltildi** (`a7f5f72e`): `ReferenceEquals` |
| 13 | 🟢 | `99fe0b13`, Faz 195 ile ilgisiz iki arşiv raporu değişikliği taşıyor (`docs/arsiv/incelemeler/2026-10-11-*-tuketici-uyum-raporu.md`) | Gerçek (commit hijyeni) | **Dokunulmadı:** ikisi de kullanıcının 2026-10-12 kararlarını taşır (podcast script'ini operatör yazar, onay `Loop` ölçütü olur); geri almak bu kararları siler |

Denetçi "bilinen sapmalar"ı yargıladı: aşırı yükleme biçimi (K-902) · Açık Sorular A/B/A (`Tracon.AspNetCore.csproj` yalnız `Core`'a başvurur, bu yüzden "C çalışmaz" doğru) · `{"type":"object"}` yedeği · setter → `400` · K-900/K-901/K-902 · taban çizgileri (istemci açıklaması 396 → 357 bir iyileşmedir; `ARSIV_ESIK` sıcak yolu küçültür) · iptal iddiası (üretim satırlarında yeni `await`/`CancellationToken` yok) — hepsi **sağlam**. Hata Modları 1–19'un hepsinin karşılığı vardır; sınıf adları plandakiyle aynıdır.

**Oturum olayı (bulgu değil):** bu fazın oturumunda bir kez `dotnet user-secrets list` çıktısı yerel sağlayıcı anahtarlarını oturum kaydına ham yazdı (yalnız bağlantı dizesi parolası maskelenmişti). Anahtarlar bu repoda ve hiçbir dosyada **yok**; döndürme kararı bakımcıdadır. Tuzak `hafiza/elle-kosum-ortami.md`'ye yazıldı.

## Faz Dışı Bulunan ve Kapatılan Kusur

- **`GetRunParameters` örneği agent'tan sonra koşmayan bir zinciri gösteriyordu** (`TraconWorkflowContextExtensions.cs`). Faz 194'ün `<example>`'ı `string` girdili bir fonksiyon yazıyordu; F-341'den (K-908) sonra böyle bir zincir kayıtta `400` verir. Örnek `List<ChatMessage>` yapıldı (`cf9e07bf`); `ExampleCompilationTests` 81/81. El yazısı kaynaklarda başka bir `string` girdili örnek kalmadı (`grep -rn "AddWorkflowFunction<string" src samples docs-site/src/content`, üretilen `api/` hariç, boş; testler tip kuralını kasten `string` ile sınar).

## Sonraki Faza Devir Notu

**Sıradaki faz: [Faz 196](../../../../196-UZUN-ADIM-SURE-VE-IPTAL.md).** Önkoşulu (bu faz) karşılandı; başlayabilir. Faz 197, 198, 199, 200 ve 201 de bu fazın kayıt yüzeyine ve editörüne dayanır (kendi `Önkoşul` satırlarında).

### Devralınan sözleşmeler

- **`WorkflowNodeReference.Settings`** `JsonElement?`'dir: `null` = "ayar yok", yalnız `Function` düğümünde, yalnız JSON nesnesi. `Undefined` ve `Null` `null`'a çevrilir (`init` erişimcisi). Düğüme alan ekleyen her faz (Faz 196 süre, Faz 200 agent sürümü) şunu bilir: `WorkflowDefinitionPayload` düğüm kayıtlarını **kendisi** taşır, yeni alan kendiliğinden akar; yine de `WorkflowDefinitionStoreContract`'a ayarlı düğüm gibi bir gidiş-dönüş case'i yazılır (K-580) ve derin `Complete<WorkflowSaveRequest>` kapısı alanı her fixture'da ister.
- **`AddWorkflowFunction<TInput, TOutput, TSettings>`** iki aşırı yüklemedir; `description` ve `retryPolicy` **`WorkflowFunctionOptions`** içindedir (K-902). Kayıt zamanında yeni bir ayar (süre, maliyet) **seçenek nesnesine** eklenir; opsiyonel parametre eklenmez (RS0026 ikinci aşırı yüklemeyi de kırar, `1.0.0`'da imza donar). İki parametreli aşırı yükleme **değişmedi**.
- **Ayarın okunması TEK yerdedir:** `WorkflowFunctionSettings` (katı seçenekler, okuma, şema, hata metni). Kayıt (`WorkflowFunctionRegistry.ValidateSettings`), derleme (`CreateExecutor`) ve şema (`AIJsonUtilities.CreateJsonSchema`) aynı seçeneği kullanır. İkinci bir şema doğrulayıcısı yazma: kontrol **okumadır** (K-900).
- **`IWorkflowFunctionCatalog.ValidateSettings`** varsayılan uygulamasızdır. Tek uygulayıcı iç kayıttır; üçüncü taraf uygulayıcı bu üyeyi yazmak zorundadır (`CHANGELOG` anar).
- **Konsol zinciri** tek `Step[]`'dir (`lib/workflow-steps.ts`): `stepsOf` iki sunucu biçimini okur, `chainBody` kanonik olanı yazar (fonksiyon varsa `nodes`, yoksa `agentNames`; ikisi de **her zaman** yazılır, diğeri boş — K-725). `shapeOf` bekleyen run'ı bozan şeyi (desen + adımların türü ve adı + sıra) tanımlar; ayar ve açıklama o tanımda yoktur.
- **Katalog yanıtı** `settingsSchema` taşır: ayarlı fonksiyonda JSON şeması (üretilemezse `{"type":"object"}`), ayarsızda `null`. Konsol şema üyelerini yalnız **listeler**.
- **Konsol bundle tabanı: 199,9 KB gzip** (bütçe 250 KB; Faz 194 sonrası 197,7 KB, bu fazın payı +2,2 KB). Faz 199'un "Faz 195 kapanışı yeniden ölçer" satırı bu sayıdır.

### 🚨 Bilinen tuzaklar

- **Ayar derleme başına OKUNUR, run başına değil.** Graf her run'da kurulur; değişiklik **sonraki** run'da geçerlidir ve `AwaitingInput` bir run'ın resume'u güncel ayarı okur. Ayar executor kimliğinin parçası **değildir**: ayar değişikliği eski checkpoint'i bozmaz. Desen, adım, sıra ve biçim (`agentNames` ↔ `nodes`) değişikliği bozar (`WorkflowStructuralChangeResumeTests`, K-901). Düğüme alan ekleyen faz, alanın kimliğe girip girmediğini **ölçer**.
- **Retry sarmalayıcısı ayara bağlanmış işleyicinin etrafında, derleme başına kurulur.** Kayıt anında kurulursa her deneme ayarsız kalır (`WorkflowFunctionSettingsRetryTests`). Faz 196/199'un yeni sarmalayıcıları aynı noktaya (`TraconWorkflowFunctionExtensions.cs`, `withRetry(...)`) girer.
- **`F-341` (K-908) ile birlikte:** ayarlı fonksiyon da önceki düğümün geçirdiği tipi almalıdır; agent'tan sonra gelen bir fonksiyon `List<ChatMessage>` (ya da atanabilir bir tip) alır. Örnek, sample ve site buna uyar.
- **Tüketicinin settings tipi kendi kodudur:** setter ve kurucu fırlatabilir. Okuma bunu `400`'e çevirir; istisnanın mesajı değeri taşıyabileceği için yanıta girmez, log'a gider. `Deserialize` içindeki ikinci `catch` bu yüzden `JsonException`'dan **geniştir**; daraltma.
- **`settingsSchema` `null` ≠ boş.** `null` "bu fonksiyon ayar almaz"dır ve konsol alanı gizler. Şema üretilemeyen ayarlı fonksiyon `{"type":"object"}` döner; asla `null` döndürme.
- **`installApiMock` ilk eşleşen rotayı yanıtlar:** bir `workflows/:name` fixture'ı `workflows/functions`'ı yutar. Literal fixture `:name`'den önce yazılır (`hafiza/frontend-test-altyapisi.md`).
- **İstemci üretimi:** nullable `$ref` özelliği `oneOf: [null, {description, $ref}]` olarak yazılır ve C# istemciye belgesiz gider; `nswag-prepare-document.py` üçüncü dönüşümü bunu onarır. Yeni nullable nesne özelliği ekleyen faz `client-description-baseline.txt`'in **büyümediğini** doğrular (`hafiza/nswag-istemci-uretimi.md`).
- **Yeni `<example>` yeni bir tip kullanıyorsa** `tests/Tracon.Generators.UnitTests/Examples/ExamplePrelude.cs`'e stub eklenir; yoksa `ExampleCompilationTests` kırılır.
- **`dotnet user-secrets list` değerleri ham basar** (sağlayıcı anahtarları dahil) ve çıktı oturum kaydına girer. Yalnız adları listele: `… | cut -d= -f1`. Anahtarsız koşum: `ASPNETCORE_ENVIRONMENT=Production` (`hafiza/elle-kosum-ortami.md`). Bu fazda bir tur yaşandı; bkz. "Denetim Bulguları" altındaki oturum olayı.

### Doküman bütçeleri (dar)

⏳ (son `dokuman-bakim.py` koşumundan sonra yazılır)

### Açık işler

- `F-412…F-415` adayları ([ADAYLAR.md](../../../../ADAYLAR.md)): şemadan üretilen ayar formu, uyarının yalnız bekleyen run varken görünmesi, 2^53'ten geniş tamsayı ayarı, tüketici `JsonConverter` mesajı.
- Site **yayınlanmadı** (`./scripts/site-deploy.sh` çalıştırılmadı; Adım 10 dışarıya dönük iştir, istenmedi). Kapı ve derleme yeşil.
- Faz 193 (`net8.0`/`net9.0` düşürme) takvime bağlıdır: 2026-11-10'dan önce başlamaz. Bu fazı etkilemez: `System.Text.Json` 10.0.0 geçişli paketi `net8.0`'a da akar (K-900), düşürme kararı ayar okumasını değiştirmez.
