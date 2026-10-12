# Faz 195 — Workflow Düğüm Düzenleme ve Fonksiyon Düğümü Ayarı

> **Durum:** 📋 Planlandı (2026-10-12)
> **Plan onayı:** Bakımcı, 2026-10-12 (engelleyici kararlar sohbette alındı: F-292 + F-295 tek faz; tipli ayar; son fonksiyon düğümü silinince `agentNames`'e dönüş; konsolda JSON alanı + şema yardımı; aynı fonksiyonun zincirde iki kez kullanımı kapsam dışı)
> **Kaynak:** [ADAYLAR.md](ADAYLAR.md) · **F-292**, **F-295**
> **Önkoşul:** [Faz 194](arsiv/fazlar/194-WORKFLOW-VE-KUYRUK-PARAMETRELERI.md) — **işlevsel bağımlılık yok**, ama dört dosyayı paylaşırlar (`workflow-editor.tsx`, `WorkflowContracts.cs`, `WorkflowDefinitionPayload.cs`, `full-replace-editors.test.tsx`). İki yazar aynı dosyaya aynı anda dokunmasın diye uygulama Faz 194 kapandıktan sonra başlar
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
   [`hafiza/workflows.md`](hafiza/workflows.md) (executor kimliği, karma zincir) ·
   [`hafiza/frontend.md`](hafiza/frontend.md) (tam değiştiren editör kapısı) ·
   [`hafiza/frontend-test-altyapisi.md`](hafiza/frontend-test-altyapisi.md)
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
| [`WorkflowNodeReference.cs:22-34`](../src/Tracon.Abstractions/Workflows/WorkflowNodeReference.cs) | Düğüm yalnız `Name` ve `Kind` taşır |
| [`TraconWorkflowFunctionExtensions.cs:94-134`](../src/Tracon.Workflows/TraconWorkflowFunctionExtensions.cs) | İşleyici `(TInput, IWorkflowContext, CancellationToken)` alır; ayar yolu yok |
| [`WorkflowFunctionRegistration.cs:21-22`](../src/Tracon.Workflows/Internal/WorkflowFunctionRegistration.cs) · [`WorkflowFunctionRegistry.cs:75`](../src/Tracon.Workflows/Internal/WorkflowFunctionRegistry.cs) | Executor fabrikası yalnız `executorId` alır |
| [`WorkflowDefinitionCompiler.cs:146-153`](../src/Tracon.Workflows/Internal/WorkflowDefinitionCompiler.cs) | Fonksiyon düğümünün executor kimliği düğüm **adıdır**; checkpoint uyumu buna dayanır |
| [`WorkflowCatalog.cs:122`](../src/Tracon.Workflows/Internal/WorkflowCatalog.cs) | Graf **her run'da** yeniden kurulur — ayar her run'da yeniden okunabilir |
| [`WorkflowFunctionDescriptor.cs:4-16`](../src/Tracon.Abstractions/Workflows/WorkflowFunctionDescriptor.cs) · [`WorkflowContracts.cs:26-35`](../src/Tracon.AspNetCore/Contracts/WorkflowContracts.cs) | Katalog yalnız ad, açıklama, girdi/çıktı tip adı verir; şema yok |
| [`WorkflowEndpoints.cs:53-66`](../src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs) | 🚨 Uç açıklaması "the UI lets users pick from this list" der; `grep -rn "workflows/functions" src/Tracon.UI/frontend/src` **boş** — doküman koddan sapmış, bu faz doğru kılar |
| [`WorkflowEndpoints.cs:348-367`](../src/Tracon.AspNetCore/Endpoints/WorkflowEndpoints.cs) | Kayıtta yalnız fonksiyon adının varlığı kontrol edilir (K-496) |
| [`workflow-editor.tsx:36-45`](../src/Tracon.UI/frontend/src/screens/workflow-editor.tsx) · [`:404-430`](../src/Tracon.UI/frontend/src/screens/workflow-editor.tsx) | `nodes` "carried, never edited"; `NodeChain` salt okunur |
| [`locales/en/workflows.ts:79`](../src/Tracon.UI/frontend/src/locales/en/workflows.ts) · [`concepts/workflows.md:96-98`](../docs-site/src/content/docs/concepts/workflows.md) | Konsol ve site "zinciri değiştiremez, HTTP API değiştirir" der |
| [`full-replace-editors.test.tsx:53-57`](../src/Tracon.UI/frontend/src/full-replace-editors.test.tsx) | `Complete<T>` **derin**dir — düğüme eklenen `settings` her fixture'da zorunlu olur |
| [`IStructuredResponseValidator.cs:22-24`](../src/Tracon.Abstractions/Agents/IStructuredResponseValidator.cs) | Tracon bilerek yerleşik JSON Schema doğrulayıcısı taşımaz |
| [`Tracon.Workflows.csproj:10`](../src/Tracon.Workflows/Tracon.Workflows.csproj) | `TraconAotCompatible=false` — tipli ayarın yansımalı deserialize'ı bu pakette serbest |

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
       .AddWorkflowFunction<string, AudioClip, TtsSettings>(
           "tts",
           provider => (text, settings, context, cancellationToken) =>
               provider.GetRequiredService<ITts>().SpeakAsync(text, settings.Voice, settings.LoudnessLufs, cancellationToken),
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
> testiyle kanıtlanamaz — [`.agents/ortak/test-seviyeleri.md`](../.agents/ortak/test-seviyeleri.md).

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

- [ ] Konsoldan agent + fonksiyon zinciri kurulur, sıralanır, silinir ve kaydedilir; run koşar (test 15)
- [ ] Ayarlı fonksiyon düğümü tipli ayarı alır (test 1)
- [ ] Bilinmeyen alan, eksik `required`, nesne olmayan ayar ve agent düğümündeki ayar kayıtta `400` döner (test 2-3)
- [ ] Değişen `TSettings` run'ı ilk düğümden önce adlandırılmış bir hatayla düşürür (test 4)
- [ ] Son fonksiyon silinince konsol `agentNames` yazar; ilk fonksiyon eklenince `nodes` (test 13)
- [ ] `GET /api/workflows/functions` `settingsSchema` taşır; uç açıklaması koda uyar
- [ ] Ayarsız + ayarlı düğüm aynı listede `GET /api/workflows`'u çökertmez (test 5)
- [ ] Dört doğrulama kapısı sıfır uyarı verir — `python3 scripts/kapi.py kapanis --taban <faz öncesi commit>`
- [ ] `samples/Tracon.Api` ile gerçek `run` yapıldı (konsoldan kurulan ayarlı zincir), çıktı belgeye yazıldı
- [ ] `secret` taraması boş döndü; site ve XML "ayar `secret` değil, anahtar adı tutar" kuralını yazar
- [ ] Manuel kabul case'leri `docs/manuel-test/15-WORKFLOWS.md` içine eklendi; otomatikleştirilebilenler koşuldu
- [ ] `faz-denetim` koşuldu; 🔴 bulgu kalmadı
- [ ] `docs-site/` güncellendi (`concepts/workflows.md`, `ui.md` + ekran görüntüsü, `capabilities.md`); `npm run build` + `check-links.mjs` temiz
- [ ] `en.ts` ve `tr.ts` eksiksiz; bundle payı ölçüldü ve yazıldı
- [ ] `CHANGELOG.md` `[Unreleased]` yeni aşırı yüklemeyi ve (Açık Soru 1-A seçilirse) arayüz üyesini adıyla anar

### Doğrulama komutları

```bash
# Katalog — ayar şeması görünür
curl -s http://localhost:5081/tracon/api/workflows/functions

# Ayarlı karma zincir kaydı (Admin)
curl -s -X PUT http://localhost:5081/tracon/api/workflows/narrated \
  -H 'Content-Type: application/json' \
  -d '{"kind":"Sequential","nodes":[{"name":"writer","kind":"Agent"},{"name":"tts","kind":"Function","settings":{"voice":"alloy"}}]}'

# Bilinmeyen alan — 400
curl -s -X PUT http://localhost:5081/tracon/api/workflows/narrated \
  -H 'Content-Type: application/json' \
  -d '{"kind":"Sequential","nodes":[{"name":"writer","kind":"Agent"},{"name":"tts","kind":"Function","settings":{"voise":"alloy"}}]}'

# Run
curl -sN -X POST http://localhost:5081/tracon/api/workflows/narrated/run \
  -H 'Content-Type: application/json' -d '{"message":"Hello"}'
```

Adlar örnek uygulamada uygulama anında kurulur; komutlar kapanışta gerçek
çıktıyla yeniden yazılır.

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

<!-- ============================================================
     AŞAĞISI KAPANIŞTA DOLDURULUR — `faz-tamamlama` skill'i.
     Plan anında boş kalır. Başlıkları SİLME.
     ============================================================ -->

## Plandan Sapmalar

> Kapanışta doldurulur. Plan ile gerçek arasındaki fark **gizlenmez** — sonraki
> oturumun en değerli bilgisidir.

## Bu Fazda Verilen Kararlar

> Kapanışta doldurulur. Dört kategoriden birine giren karar ayrıca
> `docs/KARARLAR.md`'ye K-NNN ve kategori etiketiyle girer. Beklenen adaylar:
> tipli ayar ve katı deserialize ile doğrulama (`public-api`), konsolun
> kanonik biçimi (`nodes` ↔ `agentNames`) — sunucu iki biçimi de kabul eder
> (`public-api`). Plan numara rezerve etmez.

## Gerçekleşen Public API

> Kapanışta doldurulur. Koddaki **gerçek** imzalar.

## Dosya Listesi (gerçekleşen)

> Kapanışta doldurulur.

## Süreç Ölçümü

> Kapanışta doldurulur. **Tablo olarak** — onay kutusu DEĞİL.

| Metrik | Değer |
|---|---|
| Plan revizyonu sayısı | |
| Düzeltme turu sayısı | |
| 🔴 bulgu: gerçek / gürültü / araştırılacak | |
| Fazın ürettiği regresyon | |
| Faz kapandıktan sonra bulunan kusur | |

## Denetim Bulguları

> Kapanışta doldurulur — `faz-denetim` çıktısı. Bulgu yoksa "🔴 ve 🟡 yok"
> yazılır; boş bırakılmaz.

## Sonraki Faza Devir Notu

> Kapanışta doldurulur: devralınan sözleşmeler, bilinen tuzaklar (🚨), yarım
> kalan işler, sıradaki faz.
